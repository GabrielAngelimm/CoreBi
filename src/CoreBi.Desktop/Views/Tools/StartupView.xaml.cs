using CoreBi.Engine.Tools.Startup;
using System.Windows.Controls;
using CoreBi.Desktop.Services;

using System.Windows;

namespace CoreBi.Desktop.Views;

/// <summary>Aba Ferramentas · Inicialização: programas que abrem junto com o Windows.</summary>
public partial class StartupView : UserControl
{
    private FeatureContext _context = null!;
    public StartupView()
    {
        InitializeComponent();
        // Esqueleto já no primeiro quadro: ligado só depois da entrada, o painel vazio aparecia por um instante e piscava
        Skeleton.SetActive(StartupCard, true);
    }
    internal void Initialize(FeatureContext context) => _context = context;
    private bool RequireAdmin() => PermissionGuard.RequireAdmin(_context.Owner);

    private const string NextBoot = "Vale no próximo início do Windows.";
    private static readonly Thickness HeadMargin = new(8, 10, 8, 0);

    private readonly StartupService _startup = new();
    private IReadOnlyList<StartupBackup> _removed = Array.Empty<StartupBackup>();

    private void StartupListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        => ToolPage.AlignHead(StartupListScroll, StartupHead, HeadMargin);

    private void Say(string status, string detail = "") => ToolPage.Say(StartupSummary, StartupResult, status, detail);

    /// <summary>Leitura pedida (abrir a página, "Ler novamente"): o subtítulo mostra a hora da leitura.</summary>
    internal Task RefreshAsync(bool entering = false) => ScanAsync(announce: true, entering);

    private bool _scanning;

    private static string Signature(IEnumerable<StartupEntry> list) =>
        string.Join("\n", list.Select(e => $"{e.Source}|{e.Name}|{e.Command}|{e.Detail}|{e.Enabled}|{e.Machine}|{e.FileMissing}"));
    /// <summary>Lê a lista e os removidos. Depois de uma ação (announce = false) o subtítulo continua mostrando o resultado dela.</summary>
    private async Task ScanAsync(bool announce, bool entering = false)
    {
        if (_scanning || _context == null || _context.IsClosing) return;
        _scanning = true;
        // Voltar à página relê em silêncio: sem "Lendo…" nem medidor, só a hora no fim (e a lista só muda se mudou)
        bool quiet = entering && StartupList.ItemsSource != null;
        // Leitura sem medidor, só a mensagem que vira "Lido às" (como em Drivers); o medidor fica para restaurar, que tem etapas
        if (announce && !quiet) Say("Lendo programas…");
        if (StartupList.ItemsSource == null) Skeleton.SetActive(StartupCard, true); // primeira leitura: a tabela ainda não tem o que mostrar
        List<StartupEntry> list;
        string backupError = "";
        try
        {
            list = await Task.Run(_startup.List, _context.Lifetime);
            if (_context.IsClosing) return;
            try { _removed = await Task.Run(() => _startup.Backups, _context.Lifetime); }
            catch (UnauthorizedAccessException) { _removed = Array.Empty<StartupBackup>(); backupError = "Os removidos só aparecem com o CoreBi aberto como administrador."; }
            catch (Exception ex) { _removed = Array.Empty<StartupBackup>(); backupError = "Não foi possível ler os removidos: " + ex.Message; }
        }
        catch (Exception ex) { Say("Não foi possível ler a inicialização", ex.Message); _scanning = false; Skeleton.SetActive(StartupCard, false); return; }
        if (entering) await PageMotion.EntranceDone(this);
        _scanning = false;
        Skeleton.SetActive(StartupCard, false);
        if (_context.IsClosing) return;

        // Mesma lista da tela (nomes, comandos, estados): as linhas ficam. Refazê-las a cada volta ou a cada interruptor fazia as
        // chaves ligadas piscarem
        if (StartupList.ItemsSource is not IEnumerable<StartupEntry> shown || Signature(shown) != Signature(list)) StartupList.ItemsSource = list;
        StartupEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StartupCount.Text = list.Count > 0 ? list.Count.ToString() : "";
        StartupOff.Text = OffText(list.Count, list.Count(e => !e.Enabled));
        if (announce)
        {
            Say($"Lido às {DateTime.Now:HH:mm}", backupError);
        }
        else if (backupError.Length > 0) StartupResult.Text = backupError;
        // Sem nada removido por aqui, não há o que restaurar: o botão só aparece quando há.
        BtnStartupRestore.Visibility = _removed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnStartupRestore.Content = $"Restaurar removidos ({_removed.Count})";
    }

    /// <summary>O que acompanha a contagem no título da tabela: quantos estão desligados.</summary>
    private static string OffText(int total, int off) => total switch
    {
        0 => "",
        1 => off == 0 ? "ligado" : "desligado",
        _ => off == 0 ? "todos ligados" : off == total ? "todos desligados" : off == 1 ? "1 desligado" : $"{off} desligados",
    };

    private async void BtnStartupScan_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    // Chaves mudadas nesta sessão (nome → ligado): o estado conta todas, não só a última; mudar e desfazer tira da conta
    private readonly Dictionary<string, (string Name, bool On)> _pending = new();

    private void SayPending(StartupEntry item, bool turnOn)
    {
        string key = $"{item.Source}|{item.Name}|{item.Command}";
        if (_pending.TryGetValue(key, out var before) && before.On != turnOn) _pending.Remove(key);
        else _pending[key] = (item.Name, turnOn);
        int on = _pending.Values.Count(p => p.On), off = _pending.Count - on;
        if (_pending.Count == 0) { Say("Nada muda no próximo início"); return; }
        static string Count(int n, string what) => n == 1 ? $"1 programa {what}" : $"{n} programas {what}s";
        string summary = on > 0 && off > 0 ? $"{on} {(on == 1 ? "ligado" : "ligados")} e {off} {(off == 1 ? "desligado" : "desligados")}"
            : on > 0 ? Count(on, "ligado") : Count(off, "desligado");
        // A lista dos nomes vai na dica do estado (a linha mostra só a conta)
        string names = string.Join("\n", _pending.Values.OrderBy(p => !p.On).ThenBy(p => p.Name).Select(p => $"{(p.On ? "Ligado" : "Desligado")}: {p.Name}"));
        Say(summary + "\n" + names, NextBoot);
    }

    internal async void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: StartupEntry item } box) return;
        bool turnOn = box.IsChecked == true;
        if (item.Machine && !RequireAdmin()) { box.IsChecked = item.Enabled; return; }
        try
        {
            _startup.SetEnabled(item, turnOn);
            item.Enabled = turnOn; // a linha já mostra o novo estado: a releitura encontra a mesma lista e não refaz nada
            SayPending(item, turnOn);
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
            using var critical = _context.BeginCriticalOperation("Aguarde a alteração da inicialização do Windows terminar.");
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
            try { using var critical = _context.BeginCriticalOperation("Aguarde a restauração dos programas de inicialização terminar."); await Task.Run(() => _startup.Restore(b)); }
            catch (Exception ex) { failed.Add($"{b.Name} ({ex.Message})"); }
            ToolPage.SetProgress(StartupProgressFill, (double)++done / removed.Count);
        }
        int ok = removed.Count - failed.Count;
        if (failed.Count == 0) Say(ok == 1 ? "1 programa restaurado" : $"{ok} programas restaurados", NextBoot);
        else Say($"{ok} de {removed.Count} restaurados", "Não restaurados: " + string.Join("; ", failed));
        await ScanAsync(announce: false);
    }
}
