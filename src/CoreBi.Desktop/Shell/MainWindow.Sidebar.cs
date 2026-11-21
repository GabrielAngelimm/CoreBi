using System.IO;
using System.Windows;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Storage;
using CoreBi.Engine.Tools.Apps;

namespace CoreBi.Desktop;

/// <summary>Monitor lateral (CPU, GPU, RAM e disco do sistema) e o aviso de sensores do processador.</summary>
public partial class MainWindow
{
    private int _sideDiskTicks;
    private bool _sideDiskReading;

    private void UpdateSidebarTelemetry()
    {
        UpdateSidebarCpu();
        if (_context.PrimaryGpu != null)
        {
            float? load = _context.PrimaryGpuLoad();
            SideGpuTemp.Text = _context.PrimaryGpuTemp() is float t ? $"{t:0.#} °C" : "—";
            SideGpuLoad.Text = load is float l ? $"{l:0} %" : "—";
            Ui.Glide(SideGpuBar, (load ?? 0) / 100.0, LiveMotion);
        }

        if (MemoryInfo.ReadPhysicalUsage() is { Total: > 0 } mem)
        {
            double used = (double)mem.Used / mem.Total;
            SideRam.Text = $"{mem.Used / 1073741824.0:0.0} GB";
            SideRamLoad.Text = $"{used * 100:0} %";
            Ui.Glide(SideRamBar, used, LiveMotion);
        }

        if (++_sideDiskTicks >= 5)
        {
            _sideDiskTicks = 0;
            RefreshSideStorageAsync();
        }
    }

    private void UpdateSidebarCpu()
    {
        float? temp = _monitor.CpuTemperature;
        float? load = _monitor.CpuLoad;
        SideTemp.Text = temp == null ? "—" : $"{temp:0.#} °C";
        SideLoad.Text = load == null ? "—" : $"{load:0} %";
        Ui.Glide(SideCpuBar, (load ?? 0) / 100.0, LiveMotion);
    }

    private async void RefreshSideStorageAsync()
    {
        if (_sideDiskReading || _context.IsClosing) return;
        _sideDiskReading = true;
        try
        {
            var stats = await Task.Run(ReadSystemDriveUsage);
            if (_context.IsClosing || stats is not { } s) return;
            double used = (double)s.Used / s.Total;
            SideDisk.Text = DriveCard.Fmt(s.Used);
            SideDiskLoad.Text = $"{used * 100:0} %";
            Ui.Glide(SideDiskBar, used, LiveMotion);
            SideDiskRow.ToolTip = $"Disco {s.Letter}: {DriveCard.Fmt(s.Used)} em uso de {DriveCard.Fmt(s.Total)}, {DriveCard.Fmt(s.Free)} livres";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { _sideDiskReading = false; }
    }

    private static (string Letter, long Used, long Free, long Total)? ReadSystemDriveUsage()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            if (!drive.IsReady || drive.TotalSize <= 0) return null;
            return (drive.Name.TrimEnd('\\'), drive.TotalSize - drive.TotalFreeSpace, drive.TotalFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private void UpdateCpuSensorWarning()
    {
        if (!_monitor.IsOpen) return; // ainda detectando: não acusar falta de sensor antes da hora
        var problem = _monitor.DiagnoseCpuSensors();
        CpuSensorWarn.Visibility = problem == SensorMonitor.CpuSensorProblem.None ? Visibility.Collapsed : Visibility.Visible;
        CpuSensorWarnText.Text = CpuSensorMessages.Sidebar(problem);
    }

    private async void BtnFixCpuSensors_Click(object sender, RoutedEventArgs e)
    {
        var problem = _monitor.DiagnoseCpuSensors();
        string why = SensorMonitor.Explain(problem);
        if (problem != SensorMonitor.CpuSensorProblem.PawnIOMissing)
        {
            MessageBox.Show(this, why, "CoreBi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!AppInstaller.WingetAvailable)
        {
            MessageBox.Show(this, why + "\n\nO instalador de pacotes do Windows (winget) não foi encontrado neste PC. " +
                "Atualize o \"Instalador de Aplicativo\" pela Microsoft Store e use o botão de novo, " +
                "ou instale o PawnIO (autor: namazso) manualmente.",
                "CoreBi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(this, why + "\n\nInstalar o PawnIO agora? Vai abrir uma janela com o instalador do Windows (winget). " +
            "Quando terminar, feche o CoreBi e abra de novo.",
            "CoreBi", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        using var workload = _workloads.TryBegin("Install PawnIO");
        if (workload == null) { MessageBox.Show(this, "Termine a outra operação antes.", "CoreBi"); return; }
        using var critical = _context.BeginCriticalOperation("Aguarde a instalação do PawnIO terminar antes de fechar o CoreBi.");
        try
        {
            int exitCode = await AppInstaller.InstallInteractiveAsync(AppInstaller.PawnIOPackageId, _context.Lifetime);
            SensorMonitor.RefreshPawnIOStatus();
            MessageBox.Show(this, exitCode == 0
                ? "Instalador concluído. Feche e abra o CoreBi para detectar os sensores novamente."
                : $"O winget encerrou com código 0x{exitCode:X8}. Verifique o instalador antes de tentar novamente.", "CoreBi");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível instalar o PawnIO: " + ex.Message, "CoreBi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
