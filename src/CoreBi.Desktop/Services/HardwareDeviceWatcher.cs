using System.Runtime.InteropServices;
using System.Windows.Threading;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Devices;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Storage;

namespace CoreBi.Desktop.Services;

/// <summary>
/// Acompanha eventos WM_DEVICECHANGE (volumes removíveis, USB e troca de driver de vídeo).
/// Durante mudanças de dispositivo, pausa imediatamente os sensores para evitar falhas nativas na NVML durante
/// atualizações de driver; retoma quando os avisos assentam ou aposenta apenas a GPU se o driver de vídeo mudou.
/// </summary>
internal sealed class HardwareDeviceWatcher : IDisposable
{
    private const int DbtDevNodesChanged = 0x0007;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevTypVolume = 2;

    private readonly FeatureContext _context;
    private readonly Dispatcher _dispatcher;
    private readonly Func<Task> _onStorageChanged;

    private Dictionary<string, ConnectedDevice> _usbPresent = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastVolumeEvent = DateTime.MinValue;
    private DispatcherTimer? _usbDebounce;
    private DispatcherTimer? _displaySettle;

    private string? _displayBefore;
    private bool _displayChanged;
    private string? _displayLast;
    private int _displayGeneration, _displayChecks;
    private bool _sensorsPaused;

    public HardwareDeviceWatcher(FeatureContext context, Dispatcher dispatcher, Func<Task> onStorageChanged)
    {
        _context = context;
        _dispatcher = dispatcher;
        _onStorageChanged = onStorageChanged;
    }

    public void StartInitialSnapshots()
    {
        FireAndForget(SnapshotUsbAsync(), "devices.usb.init");
        FireAndForget(SnapshotDisplayAsync(), "devices.display.init");
        FireAndForget(RefreshDrivesAsync(), "devices.drives.init");
    }

    public void HandleDeviceChange(int eventCode, IntPtr lParam)
    {
        if ((eventCode == DbtDeviceArrival || eventCode == DbtDeviceRemoveComplete) && lParam != IntPtr.Zero
            && Marshal.ReadInt32(lParam, 4) == DbtDevTypVolume)
        {
            int mask = Marshal.ReadInt32(lParam, 12); // DEV_BROADCAST_VOLUME.dbcv_unitmask: bit 0 = A:
            var letters = Enumerable.Range(0, 26).Where(i => (mask & (1 << i)) != 0).Select(i => $"{(char)('A' + i)}:").ToList();
            _lastVolumeEvent = DateTime.Now;
            FireAndForget(HandleVolumesAsync(letters, eventCode == DbtDeviceArrival), "devices.volumes");
        }
        else if (eventCode == DbtDevNodesChanged)
        {
            HoldSensorsForDeviceChange();
            _usbDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(1500), DispatcherPriority.Normal, (_, _) =>
            {
                _usbDebounce!.Stop();
                FireAndForget(DiffUsbAsync(), "devices.usb.diff");
            }, _dispatcher);
            _usbDebounce.Stop();
            _usbDebounce.Start();
        }
    }

    private async Task RefreshDrivesAsync()
    {
        var drives = await Task.Run(DriveMap.Read);
        if (!_context.IsClosing) _context.Drives = drives;
    }

    private async Task SnapshotDisplayAsync()
    {
        var now = await Task.Run(DisplayAdapters.Read);
        if (!_context.IsClosing && now is { Healthy: true } && _displayBefore == null)
            _displayBefore = now.Value.Signature;
    }

    private async Task SnapshotUsbAsync()
    {
        var list = await Task.Run(UsbDeviceInventory.List);
        if (_context.IsClosing) return;
        _usbPresent = list.GroupBy(d => d.InstanceId).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private void HoldSensorsForDeviceChange()
    {
        _context.Sensors.Pause();
        _sensorsPaused = true;
        _displayGeneration++;
        _displayChecks = 0;
        _displayLast = null;
        _displaySettle ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Normal,
            (_, _) => FireAndForget(SettleDisplayAsync(), "devices.display.settle"), _dispatcher);
        _displaySettle.Stop();
        _displaySettle.Start();
    }

    private async Task SettleDisplayAsync()
    {
        _displaySettle?.Stop();
        int generation = _displayGeneration;
        (string Signature, bool Healthy)? now;
        try
        {
            now = await Task.Run(DisplayAdapters.Read);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("devices.display.read", ex);
            ResumeSensors();
            return;
        }
        if (_context.IsClosing || generation != _displayGeneration) return;
        _displayChecks++;
        bool settled = now == null || _displayChecks >= 30 || (now.Value.Healthy && now.Value.Signature == _displayLast);
        if (now != null)
        {
            if (_displayBefore != null && now.Value.Signature != _displayBefore) _displayChanged = true;
            _displayLast = now.Value.Signature;
        }
        if (!settled) { _displaySettle?.Start(); return; }

        if (_displayChanged && !_context.Sensors.GpuRetired)
        {
            _context.Sensors.RetireGpu();
            _context.Notify("Driver de vídeo atualizado", "Abra o CoreBi de novo para voltar a ler a placa de vídeo.", "\uE7F4");
        }
        if (now is { Healthy: true } && !_displayChanged) _displayBefore = now.Value.Signature;
        ResumeSensors();
    }

    private void ResumeSensors()
    {
        if (!_sensorsPaused) return;
        _sensorsPaused = false;
        _context.Sensors.Resume();
    }

    private async Task HandleVolumesAsync(List<string> letters, bool arrived)
    {
        if (arrived) await Task.Delay(800); // o sistema de arquivos leva um instante para montar
        if (_context.IsClosing) return;
        var before = _context.Drives;
        var now = await Task.Run(DriveMap.Read);
        if (_context.IsClosing) return;
        foreach (var letter in letters)
        {
            if (arrived && now.FirstOrDefault(d => d.Letter.Equals(letter, StringComparison.OrdinalIgnoreCase)) is { } d)
                _context.Notify(d.Removable ? "Pendrive conectado" : "Unidade conectada",
                    $"{d.Title} · {DriveCard.Fmt(d.Free)} livres de {DriveCard.Fmt(d.Total)}" + (d.Disk.Length > 0 ? $"\n{d.Disk}" : ""), "");
            else if (!arrived)
            {
                var old = before.FirstOrDefault(d => d.Letter.Equals(letter, StringComparison.OrdinalIgnoreCase));
                _context.Notify("Unidade removida", old != null ? $"{old.Title} pode ser desconectada." : $"{letter} removida.", "");
            }
        }
        _context.Drives = now;
        await _onStorageChanged();
    }

    private async Task DiffUsbAsync()
    {
        var list = await Task.Run(UsbDeviceInventory.List);
        if (_context.IsClosing) return;
        var present = list.GroupBy(d => d.InstanceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var added = present.Values.Where(d => !_usbPresent.ContainsKey(d.InstanceId)).ToList();
        var removed = _usbPresent.Values.Where(d => !present.ContainsKey(d.InstanceId)).ToList();
        _usbPresent = present;

        // Pendrives já geram aviso de volume; evita duplicar com "armazenamento em massa".
        bool volumeJustNow = (DateTime.Now - _lastVolumeEvent).TotalSeconds < 4;
        static bool IsStorage(ConnectedDevice d) => d.Name.Contains("Mass Storage", StringComparison.OrdinalIgnoreCase)
            || d.Name.Contains("Armazenamento", StringComparison.OrdinalIgnoreCase);
        static ConnectedDevice? PickBest(List<ConnectedDevice> items) =>
            items.FirstOrDefault(d => d.Maker != null)
            ?? items.FirstOrDefault(d => !d.Name.Contains("Composite", StringComparison.OrdinalIgnoreCase)
                                      && !d.Name.Contains("Hub", StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault();

        added = added.Where(d => !IsStorage(d)).ToList();
        removed = removed.Where(d => !IsStorage(d)).ToList();
        if (!volumeJustNow && PickBest(added) is { } a)
            _context.Notify("Dispositivo USB conectado", a.Name + (a.Maker != null && !a.Name.Contains(a.Maker) ? $" · {a.Maker}" : ""), "");
        else if (!volumeJustNow && PickBest(removed) is { } r)
            _context.Notify("Dispositivo USB desconectado", r.Name, "");
    }

    private static async void FireAndForget(Task task, string operation)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DiagnosticLog.Write(operation, ex); }
    }

    public void Dispose()
    {
        _usbDebounce?.Stop();
        _displaySettle?.Stop();
        ResumeSensors();
    }
}
