using System.Windows;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Tools.Apps;

namespace CoreBi.Desktop;

/// <summary>
/// Rodapé da barra lateral: o aviso de sensores do processador e o tema. A barra é só navegação; as leituras ao vivo ficam nas
/// páginas (a central de monitoramento que ficava aqui repetia a Visão geral e era redesenhada a cada segundo).
/// </summary>
public partial class MainWindow
{
    private void UpdateCpuSensorWarning()
    {
        if (!_monitor.IsOpen) return; // ainda detectando: não acusar falta de sensor antes da hora
        var problem = _monitor.DiagnoseCpuSensors();
        CpuSensorWarn.Visibility = problem == SensorMonitor.CpuSensorProblem.None ? Visibility.Collapsed : Visibility.Visible;
        CpuSensorWarnText.Text = CpuSensorMessages.Sidebar(problem);
    }

    /// <summary>Clique numa das opções do tema: só troca quando a escolhida é a outra.</summary>
    private void ThemeOption_Click(object sender, RoutedEventArgs e)
    {
        bool light = sender == ThemeLight;
        if (light == ThemeManager.IsLight) return;
        ApplyTheme(light, animate: true);
        _settings.LightTheme = light;
        SaveSettings();
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

        using var workload = _workloads.TryBegin(WorkloadOperations.InstallPawnIo);
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
