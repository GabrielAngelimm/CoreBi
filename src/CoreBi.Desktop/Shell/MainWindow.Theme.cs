using System.Windows;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    internal void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Apply(!ThemeManager.IsLight);
        _settings.LightTheme = ThemeManager.IsLight;
        SaveSettings();
        UpdateThemeButton();
        RedrawThemedVisuals();
    }

    /// <summary>A chave da barra lateral fica ligada no modo escuro; a troca também vem da paleta de comandos.</summary>
    internal void UpdateThemeButton() => ThemeSwitch.IsChecked = !ThemeManager.IsLight;

    /// <summary>Gráficos desenhados em código guardam o pincel do momento do desenho: refaz com a paleta nova.</summary>
    internal void RedrawThemedVisuals()
    {
        UpdateDashboard();
        DrawChart();
        PageNet.RedrawTheme();
        UpdateCpuSensorWarning();
    }
}
