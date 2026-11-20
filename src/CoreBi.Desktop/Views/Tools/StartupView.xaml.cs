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

    private const string NextBoot = "Vale na próxima vez que o Windows iniciar.";
    private static readonly Thickness HeadMargin = new(8, 10, 8, 0);

    private readonly StartupService _startup = new();
    private IReadOnlyList<StartupBackup> _removed = Array.Empty<StartupBackup>();
    private bool? _narrowHeader;

    private void StartupHeader_SizeChanged(object sender, SizeChangedEventArgs e)
        => _narrowHeader = ToolPage.PlaceConsole(StartupHeader, StartupConsole, _narrowHeader);

    private void StartupListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        => ToolPage.AlignHead(StartupListScroll, StartupHead, HeadMargin);

    private void Say(string status, string detail = "") => ToolPage.Say(StartupSummary, StartupResult, status, detail);

    /// <summary>Leitura pedida (abrir a página, "Ler novamente"): o console mostra a hora da leitura.</summary>
    internal Task ScanStartupAsync() => ScanAsync(announce: true);

    private bool _scanning;
    /// <summary>Lê a lista e os removidos. Depois de uma ação (announce = false) o console continua mostrando o resultado dela.</summary>
    private async Task ScanAsync(bool announce)
    {
        if (_scanning || Context.IsClosing) return;
        _scanning = true;
        if (announce) { Say("Lendo programas…"); ToolPage.SetProgress(StartupProgressFill, 0); }
        List<StartupEntry> list;
        string backupError = "";
        try
        {
            list = await Task.Run(StartupManager.List, Context.Lifetime);
            if (Context.IsClosing) return;
            if (announce) ToolPage.SetProgress(StartupProgressFill, 0.5);
            try { _removed = await Task.Run(() => _startup.Backups, Context.Lifetime); }
            catch (Exception ex) { _removed = Array.Empty<StartupBackup>(); backupError = "Não foi possível ler os removidos: " + ex.Message; }
        }
        catch (Exception ex) { Say("Não foi possível ler a inicialização", ex.Message); ToolPage.SetProgress(StartupProgressFill, 0); return; }
        finally { _scanning = false; }
        if (Context.IsClosing) return;

        StartupList.ItemsSource = list;
        StartupEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StartupCount.Text = list.Count > 0 ? list.Count.ToString() : "";
        int off = list.Count(e => !e.Enabled);
        if (announce)
        {
            ToolPage.SetProgress(StartupProgressFill, 1);
            Say($"Lido às {DateTime.Now:HH:mm}",
                backupError.Length > 0 ? backupError : list.Count == 0 ? "" : off == 0 ? "Todos ligados" : off == 1 ? "1 desligado" : $"{off} desligados");
        }
        else if (backupError.Length > 0) StartupResult.Text = backupError;
        BtnStartupRestore.IsEnabled = _removed.Count > 0;
        BtnStartupRestore.Content = _removed.Count > 0
            ? $"Restaurar removidos ({_removed.Count})" : "Restaurar removidos";
    }

    internal async void BtnStartupScan_Click(object sender, RoutedEventArgs e) => await ScanStartupAsync();

    internal async void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: StartupEntry item } box) return;
        bool turnOn = box.IsChecked == true;
        if (item.Machine && !RequireAdmin()) { box.IsChecked = item.Enabled; return; }
        try
        {
            StartupManager.SetEnabled(item, turnOn);
            Say($"{item.Name} {(turnOn ? "ligado" : "desligado")}", NextBoot);
        }
        catch (Exception ex) { Say($"Não foi possível mudar {item.Name}", ex.Message); }
        await ScanAsync(announce: false);
    }

    internal async void StartupRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: StartupEntry item }) return;
        if (item.Machine && !RequireAdmin()) return;
        var r = MessageBox.Show($"Remover \"{item.Name}\" da inicialização do Windows?\n\n{item.Command}\n\n" +
                                "O programa continua instalado e dá para desfazer em Restaurar removidos. " +
                                "Para só testar, desligue o interruptor.",
                                "Inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            using var critical = Context.BeginCriticalOperation();
            await Task.Run(() => _startup.Remove(item));
            Say($"{item.Name} removido", "Dá para desfazer em Restaurar removidos.");
        }
        catch (Exception ex) { Say($"Não foi possível remover {item.Name}", ex.Message); }
        await ScanAsync(announce: false);
    }

    internal async void BtnStartupRestore_Click(object sender, RoutedEventArgs e)
    {
        var removed = _removed;
        if (removed.Count == 0) return;
        if (removed.Any(b => b.Machine) && !RequireAdmin()) return;
        string names = string.Join("\n", removed.Take(12).Select(b => $"  • {b.Name}"))
                       + (removed.Count > 12 ? $"\n  … e mais {removed.Count - 12}" : "");
        string what = removed.Count == 1 ? "1 programa" : $"{removed.Count} programas";
        var r = MessageBox.Show($"Devolver {what} à inicialização do Windows?\n\n{names}",
                                "Inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        var failed = new List<string>();
        int done = 0;
        Say(removed.Count == 1 ? "Restaurando 1 programa…" : $"Restaurando {removed.Count} programas…");
        ToolPage.SetProgress(StartupProgressFill, 0);
        foreach (var b in removed.ToList())
        {
            try { using var critical = Context.BeginCriticalOperation(); await Task.Run(() => _startup.Restore(b)); }
            catch (Exception ex) { failed.Add($"{b.Name} ({ex.Message})"); }
            ToolPage.SetProgress(StartupProgressFill, (double)++done / removed.Count);
        }
        int ok = removed.Count - failed.Count;
        if (failed.Count == 0) Say(ok == 1 ? "1 programa restaurado" : $"{ok} programas restaurados", NextBoot);
        else Say($"{ok} de {removed.Count} restaurados", "Não restaurados: " + string.Join("; ", failed));
        await ScanAsync(announce: false);
    }
}
