using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CoreBi.Desktop;

/// <summary>
/// Paleta de comandos (Ctrl + K): leva a qualquer página ou ação pelo nome, sem tirar as mãos do teclado.
/// As entradas de página são lidas da própria barra lateral, então a navegação tem uma única fonte.
/// </summary>
public partial class MainWindow
{
    public sealed record PaletteEntry(string Title, string Section, string Icon, string Keywords, Action Run);

    private readonly List<PaletteEntry> _paletteEntries = new();

    private void BuildPalette()
    {
        string section = "Visão geral";
        foreach (var child in NavPanel.Children)
        {
            if (child is TextBlock header) section = header.Text;
            else if (child is RadioButton nav)
            {
                string title = nav.Content as string ?? "";
                _paletteEntries.Add(new PaletteEntry(title, nav == NavDash ? "Página" : section, Ui.GetIcon(nav), section,
                    () => nav.IsChecked = true));
            }
        }
        _paletteEntries.Add(new PaletteEntry("Alternar tema claro ou escuro", "Ação", "", "aparência modo cor",
            () => Theme_Click(this, new RoutedEventArgs())));
        _paletteEntries.Add(new PaletteEntry("Exportar relatório do sistema", "Ação", "", "html pdf json salvar imprimir",
            () => { NavSystem.IsChecked = true; BtnReport_Click(this, new RoutedEventArgs()); }));
        _paletteEntries.Add(new PaletteEntry("Mostrar ou ocultar o overlay", "Ação", "", "fps jogo ctrl shift o",
            () => ChkOverlay.IsChecked = ChkOverlay.IsChecked != true));
        _paletteEntries.Add(new PaletteEntry("Testar a rede", "Ação", "", "ping velocidade lag",
            () => { NavNet.IsChecked = true; _ = PageNet.RunOrCancelAsync(); }));
    }

    internal void BtnCommand_Click(object sender, RoutedEventArgs e) => OpenPalette();

    internal void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (PaletteLayer.IsVisible) ClosePalette(); else OpenPalette();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && PaletteLayer.IsVisible)
        {
            ClosePalette();
            e.Handled = true;
        }
    }

    private void OpenPalette()
    {
        PaletteSearch.Text = "";
        FilterPalette();
        PaletteLayer.Visibility = Visibility.Visible;
        PaletteSearch.Focus();
        Keyboard.Focus(PaletteSearch);
    }

    private void ClosePalette() => PaletteLayer.Visibility = Visibility.Collapsed;

    private void FilterPalette()
    {
        string q = PaletteSearch.Text.Trim();
        var items = q.Length == 0 ? _paletteEntries
            : _paletteEntries.Where(p => Matches(p.Title, q) || Matches(p.Section, q) || Matches(p.Keywords, q)).ToList();
        PaletteList.ItemsSource = items;
        PaletteList.SelectedIndex = items.Count > 0 ? 0 : -1;
        PaletteEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Busca sem acento e sem diferenciar maiúsculas ("energia" acha "Energia", "inicializacao" acha "Inicialização").</summary>
    private static bool Matches(string text, string query)
        => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, query,
               System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;

    internal void PaletteSearch_TextChanged(object sender, TextChangedEventArgs e) => FilterPalette();

    internal void PaletteSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int count = PaletteList.Items.Count;
        switch (e.Key)
        {
            case Key.Down when count > 0:
                PaletteList.SelectedIndex = (PaletteList.SelectedIndex + 1) % count;
                PaletteList.ScrollIntoView(PaletteList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                PaletteList.SelectedIndex = (PaletteList.SelectedIndex - 1 + count) % count;
                PaletteList.ScrollIntoView(PaletteList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                RunPaletteSelection();
                e.Handled = true;
                break;
        }
    }

    internal void PaletteList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(PaletteList, source) is ListBoxItem)
            RunPaletteSelection();
    }

    private void RunPaletteSelection()
    {
        if (PaletteList.SelectedItem is not PaletteEntry entry) return;
        ClosePalette();
        entry.Run();
    }

    internal void PaletteScrim_MouseDown(object sender, MouseButtonEventArgs e) => ClosePalette();

    /// <summary>Clique dentro do painel não fecha a paleta.</summary>
    internal void PalettePanel_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
