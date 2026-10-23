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

    /// <summary>Mostra o ícone do tema para onde o clique leva (sol = ir para o claro, lua = ir para o escuro).</summary>
    internal void UpdateThemeButton()
    {
        bool light = ThemeManager.IsLight;
        BtnTheme.Content = light ? "" : "";
        BtnTheme.ToolTip = light ? "Tema escuro" : "Tema claro";
    }

    /// <summary>Gráficos desenhados em código guardam o pincel do momento do desenho: refaz com a paleta nova.</summary>
    internal void RedrawThemedVisuals()
    {
        UpdateDashboard();
        DrawChart();
        UpdateCpuSensorWarning();
    }
}
