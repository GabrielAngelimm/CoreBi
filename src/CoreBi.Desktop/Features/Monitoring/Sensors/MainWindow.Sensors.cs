using LibreHardwareMonitor.Hardware;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CoreBi.Engine.Monitoring.Sensors;

using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    internal readonly SensorMonitor _monitor;

    // ---------------- Sensores da CPU (PawnIO) ----------------

    /// <summary>Mostra o aviso no menu quando temperatura/consumo da CPU não respondem, com o motivo provável.</summary>
    internal void UpdateCpuSensorWarning()
    {
        if (!_monitor.IsOpen) return; // ainda detectando: não acusar falta de sensor antes da hora
        var p = _monitor.DiagnoseCpuSensors();
        CpuSensorWarn.Visibility = p == SensorMonitor.CpuSensorProblem.None ? Visibility.Collapsed : Visibility.Visible;
        CpuSensorWarnText.Text = p switch
        {
            SensorMonitor.CpuSensorProblem.NotElevated => "Sem permissão de administrador.",
            SensorMonitor.CpuSensorProblem.PawnIOMissing => "Falta o driver PawnIO.",
            SensorMonitor.CpuSensorProblem.ReadFailed => "O driver não respondeu.",
            _ => ""
        };
    }

    internal async void BtnFixCpuSensors_Click(object sender, RoutedEventArgs e)
    {
        var p = _monitor.DiagnoseCpuSensors();
        string why = SensorMonitor.Explain(p);
        if (p != SensorMonitor.CpuSensorProblem.PawnIOMissing)
        {
            MessageBox.Show(why, "CoreBi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // winget vem com o Windows 11 e com o Windows 10 atualizado (App Installer).
        string winget = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                               @"Microsoft\WindowsApps\winget.exe");
        if (!System.IO.File.Exists(winget))
        {
            MessageBox.Show(why + "\n\nO instalador de pacotes do Windows (winget) não foi encontrado neste PC. " +
                            "Atualize o \"Instalador de Aplicativo\" pela Microsoft Store e use o botão de novo, " +
                            "ou instale o PawnIO (autor: namazso) manualmente.",
                            "CoreBi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show(why + "\n\nInstalar o PawnIO agora? Vai abrir uma janela com o instalador do Windows (winget). " +
                                "Quando terminar, feche o CoreBi e abra de novo.",
                                "CoreBi", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        using var workload = _workloads.TryBegin("Install PawnIO");
        if (workload == null) { MessageBox.Show(this, "Termine a outra operação antes.", "CoreBi"); return; }
        using var critical = _features.BeginCriticalOperation();
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(winget) { UseShellExecute = false };
            foreach (string argument in new[] { "install", "--id", "namazso.PawnIO", "--exact", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements" })
                start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o winget.");
            await process.WaitForExitAsync();
            MessageBox.Show(this, process.ExitCode == 0 ? "Instalador concluído. Feche e abra o CoreBi para detectar os sensores novamente."
                : $"O winget encerrou com código 0x{process.ExitCode:X8}. Verifique o instalador antes de tentar novamente.", "CoreBi");
        }
        catch (Exception ex) { MessageBox.Show(this, "Não foi possível instalar o PawnIO: " + ex.Message, "CoreBi", MessageBoxButton.OK, MessageBoxImage.Warning); }

    }
}
