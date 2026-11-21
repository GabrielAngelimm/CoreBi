using LibreHardwareMonitor.Hardware;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CoreBi.Engine.Monitoring.Devices;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Storage;

using System.Runtime.InteropServices;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    // ---------------- Unidades e dispositivos conectados ----------------

    internal List<DriveCard> _drives = new();
    internal Dictionary<string, ConnectedDevice> _usbPresent = new(StringComparer.OrdinalIgnoreCase);
    internal DateTime _lastVolumeEvent = DateTime.MinValue;
    internal DispatcherTimer? _usbDebounce;
    internal ToastWindow? _toast;

    internal async void RefreshDrives()
    {
        _drives = await Task.Run(DriveMap.Read);
    }

    internal void Toast(string title, string text, string icon)
    {
        AddEvent(icon, title, text.Split('\n')[0], "");
        _toast ??= new ToastWindow();
        _toast.Notify(title, text, icon);
    }

    // ---------------- Troca de driver de vídeo com o app aberto ----------------
    // O Windows avisa (DBT_DEVNODES_CHANGED) quando qualquer dispositivo muda, inclusive a placa de vídeo no meio de uma troca de
    // driver. Ler a placa nessa hora é falha nativa dentro da NVML, que derruba o processo. Então: a cada aviso, as leituras
    // pausam na hora; quando os avisos param e as placas estão de pé, compara com o retrato de antes. Se nada mudou (pendrive,
    // fone, controle), as leituras voltam. Se o driver de vídeo mudou, a placa sai das leituras até o CoreBi abrir de novo.

    internal string? _displayBefore;       // retrato das placas de vídeo antes do aviso
    internal bool _displayChanged;         // algum retrato desta rodada de avisos saiu diferente
    internal string? _displayLast;         // retrato da conferência anterior, para exigir dois iguais seguidos
    internal int _displayGeneration, _displayChecks;
    internal DispatcherTimer? _displaySettle;

    internal async void SnapshotDisplay()
    {
        var now = await Task.Run(DisplayAdapters.Read);
        if (now is { Healthy: true } && _displayBefore == null) _displayBefore = now.Value.Signature;
    }

    internal void HoldSensorsForDeviceChange()
    {
        _monitor.Pause();
        _displayGeneration++;
        _displayChecks = 0;
        _displayLast = null;
        _displaySettle ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Normal, async (_, _) => await SettleDisplayAsync(), Dispatcher);
        _displaySettle.Stop();
        _displaySettle.Start();
    }

    internal async Task SettleDisplayAsync()
    {
        _displaySettle!.Stop();
        int generation = _displayGeneration;
        var now = await Task.Run(DisplayAdapters.Read);
        if (_features.IsClosing || generation != _displayGeneration) return; // chegou outro aviso: a conferência recomeça
        _displayChecks++;
        // Sem WMI, segue com o que sabe; com placa parada ou retrato ainda mudando, espera mais (até cerca de 1 min)
        bool settled = now == null || _displayChecks >= 30 || (now.Value.Healthy && now.Value.Signature == _displayLast);
        if (now != null)
        {
            if (_displayBefore != null && now.Value.Signature != _displayBefore) _displayChanged = true;
            _displayLast = now.Value.Signature;
        }
        if (!settled) { _displaySettle.Start(); return; }

        if (_displayChanged && !_monitor.GpuRetired)
        {
            _monitor.RetireGpu();
            Toast("Driver de vídeo atualizado", "Abra o CoreBi de novo para voltar a ler a placa de vídeo.", "\uE7F4");
        }
        if (now is { Healthy: true } && !_displayChanged) _displayBefore = now.Value.Signature;
        _monitor.Resume();
    }

    internal async void SnapshotUsb()
    {
        try
        {
            var list = await Task.Run(UsbDeviceInventory.List);
            if (_features.IsClosing) return;
            _usbPresent = list.Where(d => d.Present).GroupBy(d => d.InstanceId).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch { }
    }

    /// <summary>WM_DEVICECHANGE: unidades (pendrive, HD externo) e qualquer dispositivo USB.</summary>
    internal void OnDeviceChange(int ev, IntPtr lParam)
    {
        const int DBT_DEVNODES_CHANGED = 0x0007, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVTYP_VOLUME = 2;
        if ((ev == DBT_DEVICEARRIVAL || ev == DBT_DEVICEREMOVECOMPLETE) && lParam != IntPtr.Zero &&
            Marshal.ReadInt32(lParam, 4) == DBT_DEVTYP_VOLUME)
        {
            int mask = Marshal.ReadInt32(lParam, 12); // DEV_BROADCAST_VOLUME.dbcv_unitmask: bit 0 = A:
            var letters = Enumerable.Range(0, 26).Where(i => (mask & (1 << i)) != 0).Select(i => $"{(char)('A' + i)}:").ToList();
            _lastVolumeEvent = DateTime.Now;
            OnVolumes(letters, ev == DBT_DEVICEARRIVAL);
        }
        else if (ev == DBT_DEVNODES_CHANGED)
        {
            HoldSensorsForDeviceChange();
            // Chega várias vezes seguidas por dispositivo: espera assentar e compara com a lista anterior.
            _usbDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(1500), DispatcherPriority.Normal, (_, _) =>
            {
                _usbDebounce!.Stop();
                DiffUsb();
            }, Dispatcher);
            _usbDebounce.Stop();
            _usbDebounce.Start();
        }
    }

    internal async void OnVolumes(List<string> letters, bool arrived)
    {
        if (arrived) await Task.Delay(800); // o sistema de arquivos leva um instante para montar
        if (_features.IsClosing) return;
        var before = _drives;
        var now = await Task.Run(DriveMap.Read);
        if (_features.IsClosing) return;
        foreach (var l in letters)
        {
            if (arrived && now.FirstOrDefault(d => d.Letter.Equals(l, StringComparison.OrdinalIgnoreCase)) is { } d)
                Toast(d.Removable ? "Pendrive conectado" : "Unidade conectada",
                      $"{d.Title} · {DriveCard.Fmt(d.Free)} livres de {DriveCard.Fmt(d.Total)}" + (d.Disk.Length > 0 ? $"\n{d.Disk}" : ""), "");
            else if (!arrived)
            {
                var old = before.FirstOrDefault(d => d.Letter.Equals(l, StringComparison.OrdinalIgnoreCase));
                Toast("Unidade removida", old != null ? $"{old.Title} pode ser desconectada." : $"{l} removida.", "");
            }
        }
        _drives = now;

        // Discos físicos e unidades da página Sistema também mudam com pendrive/HD externo.
        await PageSystem.RefreshStorageAsync();
    }

    internal async void DiffUsb()
    {
        List<ConnectedDevice> list;
        try { list = await Task.Run(UsbDeviceInventory.List); } catch { return; }
        var present = list.Where(d => d.Present).GroupBy(d => d.InstanceId)
                          .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var added = present.Values.Where(d => !_usbPresent.ContainsKey(d.InstanceId)).ToList();
        var removed = _usbPresent.Values.Where(d => !present.ContainsKey(d.InstanceId)).ToList();
        _usbPresent = present;

        // Pendrives já geram aviso de unidade: não repete com o nome genérico "armazenamento em massa".
        bool volumeJustNow = (DateTime.Now - _lastVolumeEvent).TotalSeconds < 4;
        static bool Storage(ConnectedDevice d) => d.Name.Contains("Mass Storage", StringComparison.OrdinalIgnoreCase) ||
                                            d.Name.Contains("Armazenamento", StringComparison.OrdinalIgnoreCase);
        // Um dispositivo composto (ex.: Arduino Leonardo) gera vários registros: anuncia o mais descritivo.
        static ConnectedDevice? Best(List<ConnectedDevice> l) =>
            l.FirstOrDefault(d => d.Maker != null) ??
            l.FirstOrDefault(d => !d.Name.Contains("Composite", StringComparison.OrdinalIgnoreCase) &&
                                  !d.Name.Contains("Hub", StringComparison.OrdinalIgnoreCase)) ?? l.FirstOrDefault();

        added = added.Where(d => !Storage(d)).ToList();
        removed = removed.Where(d => !Storage(d)).ToList();
        if (!volumeJustNow && Best(added) is { } a)
            Toast("Dispositivo USB conectado", a.Name + (a.Maker != null && !a.Name.Contains(a.Maker) ? $" · {a.Maker}" : ""), "");
        else if (!volumeJustNow && Best(removed) is { } r)
            Toast("Dispositivo USB desconectado", r.Name, "");
    }

}
