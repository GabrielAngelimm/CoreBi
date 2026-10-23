using System.Windows.Input;
using CoreBi.Engine.Tools.Startup;
using System.Windows.Controls;
using CoreBi.Desktop.Services;

using System.Windows;

namespace CoreBi.Desktop.Views;

/// <summary>Aba Ferramentas · Inicialização: programas que abrem junto com o Windows.</summary>
public partial class StartupView : UserControl
{
    internal FeatureContext Context { get; set; } = null!;
    public StartupView() => InitializeComponent();
    private bool RequireAdmin() => PermissionGuard.RequireAdmin(Context.Owner);

    private readonly StartupService _startup = new();
    private IReadOnlyList<StartupBackup> _removed = Array.Empty<StartupBackup>();

    private bool _scanning;
    internal async Task ScanStartupAsync()
    {
        if (_scanning || Context.IsClosing) return;
        _scanning = true;
        List<StartupEntry> list;
        try
        {
            list = await Task.Run(StartupManager.List, Context.Lifetime);
            if (Context.IsClosing) return;
            try { _removed = await Task.Run(() => _startup.Backups, Context.Lifetime); }
            catch (Exception ex) { _removed = Array.Empty<StartupBackup>(); StartupResult.Text = "Backups indisponíveis: " + ex.Message; }
        }
        catch (Exception ex) { StartupResult.Text = $"Não foi possível listar: {ex.Message}"; return; }
        finally { _scanning = false; }
        if (Context.IsClosing) return;

        StartupList.ItemsSource = list;
        int on = list.Count(e => e.Enabled);
        StartupSummary.Text = list.Count == 0 ? "NENHUM PROGRAMA NA INICIALIZAÇÃO"
                            : $"{list.Count} PROGRAMAS · {on} ATIVADOS · {list.Count - on} DESATIVADOS";
        BtnStartupRestore.IsEnabled = _removed.Count > 0;
        BtnStartupRestore.Content = _removed.Count > 0
            ? $"RESTAURAR REMOVIDOS ({_removed.Count})" : "RESTAURAR REMOVIDOS";
    }

    internal async void BtnStartupScan_Click(object sender, RoutedEventArgs e)
    {
        StartupResult.Text = "";
        await ScanStartupAsync();
    }

    internal async void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: StartupEntry item } box) return;
        bool turnOn = box.IsChecked == true;
        if (item.Machine && !RequireAdmin()) { box.IsChecked = item.Enabled; return; }
        try
        {
            StartupManager.SetEnabled(item, turnOn);
            StartupResult.Text = $"{item.Name}: {(turnOn ? "ativado" : "desativado")}. Vale na próxima inicialização do Windows.";
        }
        catch (Exception ex) { StartupResult.Text = $"{item.Name}: falhou ({ex.Message})."; }
        await ScanStartupAsync();
    }

    internal async void StartupRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: StartupEntry item }) return;
        if (item.Machine && !RequireAdmin()) return;
        var r = MessageBox.Show($"Remover \"{item.Name}\" da inicialização do Windows?\n\n{item.Command}\n\n" +
                                "O programa continua instalado, só deixa de abrir sozinho. " +
                                "Dá para desfazer em \"Restaurar removidos\".\n\n" +
                                "Se só quiser testar, prefira desligar o interruptor.",
                                "Inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            using var critical = Context.BeginCriticalOperation();
            await Task.Run(() => _startup.Remove(item));
            StartupResult.Text = $"{item.Name} removido da inicialização.";
        }
        catch (Exception ex) { StartupResult.Text = $"{item.Name}: não foi possível remover ({ex.Message})."; }
        await ScanStartupAsync();
    }

    internal async void BtnStartupRestore_Click(object sender, RoutedEventArgs e)
    {
        var removed = _removed;
        if (removed.Count == 0) return;
        if (removed.Any(b => b.Machine) && !RequireAdmin()) return;
        string names = string.Join("\n", removed.Take(12).Select(b => $"  • {b.Name}"))
                       + (removed.Count > 12 ? $"\n  … e mais {removed.Count - 12}" : "");
        var r = MessageBox.Show($"Devolver {removed.Count} programas à inicialização do Windows?\n\n{names}",
                                "Inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        var failed = new List<string>();
        foreach (var b in removed.ToList())
        {
            try { using var critical = Context.BeginCriticalOperation(); await Task.Run(() => _startup.Restore(b)); }
            catch (Exception ex) { failed.Add($"{b.Name} ({ex.Message})"); }
        }
        StartupResult.Text = failed.Count == 0 ? "Programas restaurados." : $"Não restaurados: {string.Join("; ", failed)}";
        await ScanStartupAsync();
    }
}
