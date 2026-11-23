using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Tools.Startup;

namespace CoreBi.Desktop.Views;

/// <summary>Linha da tabela: a entrada, o ícone já carregado e as dicas montadas.</summary>
public sealed class StartupRow(StartupEntry entry, ImageSource? icon)
{
    public StartupEntry Entry { get; } = entry;
    public ImageSource? Icon { get; } = icon;
    /// <summary>Dica do nome: o nome da entrada no Windows (quando é outro) e a descrição do arquivo.</summary>
    public string NameTip => string.Join("\n", new[]
    {
        Entry.Title,
        Entry.Name != Entry.Title ? "Entrada: " + Entry.Name : "",
        Entry.Detail.Length > 0 && Entry.Detail != Entry.Title ? Entry.Detail : "",
    }.Where(s => s.Length > 0));
    public string CommandTip => Entry.Store ? "Tarefa de inicialização de um app da Microsoft Store." : Entry.Command + "\n" + Entry.Source;
}

/// <summary>Linha de "Removidos".</summary>
public sealed class RemovedRow(StartupBackup backup, ImageSource? icon)
{
    public StartupBackup Backup { get; } = backup;
    public ImageSource? Icon { get; } = icon;
    public string Title => Backup.DisplayName;
    public string When => Backup.RemovedAt is { } at ? StartupView.Day(at) : "";
    public string Tip => string.Join("\n", new[] { Backup.Publisher ?? "", Backup.Command ?? Backup.Original ?? "" }.Where(s => s.Length > 0));
}

/// <summary>Uma barra dos últimos inícios: as duas partes do Windows em pixels e a dica com a data e os tempos.</summary>
public sealed record BootBarItem(double MainHeight, double PostHeight, string Day, string Tip, bool Last);

/// <summary>Aba Ferramentas · Inicialização: programas que abrem junto com o Windows e quanto o início demora.</summary>
public partial class StartupView : UserControl
{
    private FeatureContext _context = null!;
    public StartupView()
    {
        InitializeComponent();
        // Esqueleto já no primeiro quadro: ligado só depois da entrada, o painel vazio aparecia por um instante e piscava
        Skeleton.SetActive(StartupCard, true);
        Skeleton.SetActive(BootCard, true);
    }
    internal void Initialize(FeatureContext context) => _context = context;
    private bool RequireAdmin() => PermissionGuard.RequireAdmin(_context.Owner);

    private const string NextBoot = "Vale no próximo início do Windows.";
    private const int Bars = 10;
    private const double BarHeight = 96;
    private static readonly Thickness HeadMargin = new(8, 10, 8, 0);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly StartupService _startup = new();
    private IReadOnlyList<StartupBackup> _removed = Array.Empty<StartupBackup>();
    private List<StartupRow> _rows = new();
    private ICollectionView? _view;
    private string _filter = "all";
    private HashSet<StartupRow> _passing = new();

    private void StartupListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        => ToolPage.AlignHead(StartupListScroll, StartupHead, HeadMargin);

    private void Say(string status, string detail = "") => ToolPage.Say(StartupSummary, StartupResult, status, detail);

    /// <summary>Leitura pedida (abrir a página, "Ler novamente"): o subtítulo mostra a hora da leitura.</summary>
    internal Task RefreshAsync(bool entering = false) => ScanAsync(announce: true, entering);

    private bool _scanning;

    private static string Signature(IEnumerable<StartupEntry> list) =>
        string.Join("\n", list.Select(e => $"{e.Source}|{e.Name}|{e.Title}|{e.Command}|{e.Enabled}|{e.Machine}|{e.FileMissing}|{e.IconPath}"));
    private static string Signature(IEnumerable<StartupBackup> list) => string.Join("\n", list.Select(b => b.Id));

    /// <summary>Lê a lista, os removidos e os inícios. Depois de uma ação (announce = false) o estado continua mostrando o resultado dela.</summary>
    private async Task ScanAsync(bool announce, bool entering = false)
    {
        if (_scanning || _context == null || _context.IsClosing) return;
        _scanning = true;
        // Voltar à página relê em silêncio: sem "Lendo…" nem medidor, só a hora no fim (e a lista só muda se mudou)
        bool quiet = entering && StartupList.ItemsSource != null;
        if (announce && !quiet) Say("Lendo programas…");
        if (StartupList.ItemsSource == null) Skeleton.Set(true, StartupCard, BootCard);
        List<StartupEntry> list;
        BootReport? boots = null;
        string backupError = "";
        try
        {
            list = await Task.Run(_startup.List, _context.Lifetime);
            if (_context.IsClosing) return;
            try { _removed = await Task.Run(() => _startup.Backups, _context.Lifetime); }
            catch (UnauthorizedAccessException) { _removed = Array.Empty<StartupBackup>(); backupError = "Os removidos só aparecem com o CoreBi aberto como administrador."; }
            catch (Exception ex) { _removed = Array.Empty<StartupBackup>(); backupError = "Não foi possível ler os removidos: " + ex.Message; }
            boots = await Task.Run(_startup.Boots, _context.Lifetime);
            // Ícones lidos fora da tela (o primeiro acesso a cada arquivo leva alguns milissegundos); depois vêm do cache
            await Task.Run(() =>
            {
                foreach (var e in list) FileIcons.Get(e.IconPath);
                foreach (var b in _removed) FileIcons.Get(b.IconPath);
            }, _context.Lifetime);
        }
        catch (Exception ex) { Say("Não foi possível ler a inicialização", ex.Message); _scanning = false; Skeleton.Set(false, StartupCard, BootCard); return; }
        if (entering) await PageMotion.EntranceDone(this);
        _scanning = false;
        if (_context.IsClosing) return;

        // Mesma lista da tela (nomes, comandos, estados): as linhas ficam. Refazê-las a cada volta ou a cada interruptor fazia as
        // chaves ligadas piscarem
        if (_view == null || Signature(_rows.Select(r => r.Entry)) != Signature(list))
        {
            _rows = list.Select(e => new StartupRow(e, FileIcons.Get(e.IconPath))).ToList();
            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = o => o is StartupRow r && _passing.Contains(r);
            ApplyFilter();
            StartupList.ItemsSource = _view;
        }
        StartupEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCounts();
        ShowRemoved();
        if (boots != null) ShowBoots(boots);
        Skeleton.Set(false, StartupCard, BootCard);
        if (announce) Say($"Lido às {DateTime.Now:HH:mm}", backupError);
        else if (backupError.Length > 0) StartupResult.Text = backupError;
    }

    // ---------- Filtro ----------

    private void StartupFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not RadioButton { Tag: string tag }) return;
        _filter = tag;
        ApplyFilter();
    }

    /// <summary>
    /// Quem passa no filtro é decidido na escolha, não a cada interruptor: a linha que acabou de ser desligada em "Ligados" fica na
    /// tela até a próxima escolha, em vez de sumir debaixo do mouse.
    /// </summary>
    private void ApplyFilter()
    {
        _passing = _rows.Where(r => _filter switch { "on" => r.Entry.Enabled, "off" => !r.Entry.Enabled, _ => true }).ToHashSet();
        _view?.Refresh();
        StartupListScroll.ScrollToTop();
    }

    private void UpdateCounts()
    {
        int on = _rows.Count(r => r.Entry.Enabled);
        Ui.SetSuffix(FilterAll, _rows.Count.ToString(PtBr));
        Ui.SetSuffix(FilterOn, on.ToString(PtBr));
        Ui.SetSuffix(FilterOff, (_rows.Count - on).ToString(PtBr));
    }

    // ---------- Removidos ----------

    private void ShowRemoved()
    {
        if (RemovedList.ItemsSource is not IEnumerable<RemovedRow> shown || Signature(shown.Select(r => r.Backup)) != Signature(_removed))
            RemovedList.ItemsSource = _removed.Select(b => new RemovedRow(b, FileIcons.Get(b.IconPath))).ToList();
        RemovedEmpty.Visibility = _removed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnStartupRestore.Visibility = _removed.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"hoje às 14:05", "ontem às 09:12", "21/11 às 18:30".</summary>
    internal static string Day(DateTime at)
    {
        string day = at.Date == DateTime.Today ? "hoje" : at.Date == DateTime.Today.AddDays(-1) ? "ontem" : at.ToString("dd/MM", PtBr);
        return $"{day} às {at:HH:mm}";
    }

    // ---------- Tempo de início ----------

    private static string Seconds(double ms) => ms >= 10_000 ? (ms / 1000).ToString("0", PtBr) + " s" : (ms / 1000).ToString("0.0", PtBr) + " s";

    private void ShowBoots(BootReport report)
    {
        var last = report.Last;
        string fastNote = report.FastStartup
            ? " Com a Inicialização rápida do Windows ligada, desligar e ligar o PC não é um início completo: só reiniciar entra nesta conta."
            : "";
        if (last == null)
        {
            BootShown.Visibility = Visibility.Hidden;
            BootMissing.Visibility = Visibility.Visible;
            BootWhen.Text = "";
            BootMissing.Text = report.Unavailable == "admin"
                ? "O Windows só mostra o tempo dos inícios com o CoreBi aberto como administrador."
                : "O Windows ainda não registrou um início completo neste PC. Reinicie e volte aqui." + fastNote;
            return;
        }
        BootShown.Visibility = Visibility.Visible;
        BootMissing.Visibility = Visibility.Collapsed;
        BootWhen.Text = "Ligado " + Day(last.When);

        double bios = report.BiosMs ?? 0, total = bios + last.TotalMs;
        // Total em segundos inteiros (décimos abaixo de 10 s); acima de 2 minutos, em minutos e segundos
        if (total >= 120_000)
        {
            var span = TimeSpan.FromMilliseconds(total);
            BootTotal.Text = $"{(int)span.TotalMinutes}:{span.Seconds:00}";
            BootUnit.Text = "min";
        }
        else
        {
            BootTotal.Text = total >= 10_000 ? (total / 1000).ToString("0", PtBr) : (total / 1000).ToString("0.0", PtBr);
            BootUnit.Text = "s";
        }
        BootSub.Text = bios > 0 ? "do botão de ligar até o PC ficar pronto" : "do Windows carregar até o PC ficar pronto";
        BootSub.ToolTip = fastNote.Length > 0 ? fastNote.Trim() : null;

        BootBarBios.Width = new GridLength(bios, GridUnitType.Star);
        BootBarMain.Width = new GridLength(last.MainPathMs, GridUnitType.Star);
        BootBarPost.Width = new GridLength(last.PostBootMs, GridUnitType.Star);
        BootBiosRow.Visibility = bios > 0 ? Visibility.Visible : Visibility.Collapsed;
        BootBios.Text = bios > 0 ? Seconds(bios) : "—";
        BootMain.Text = Seconds(last.MainPathMs);
        BootPost.Text = Seconds(last.PostBootMs);

        // Do mais antigo (esquerda) ao mais novo (direita); as vagas que faltam ficam vazias no fim, sem barra
        var boots = report.Boots.Take(Bars).Reverse().ToList();
        double max = Math.Max(1, boots.Max(b => b.TotalMs));
        var items = new List<BootBarItem>();
        for (int i = 0; i < boots.Count; i++)
        {
            var b = boots[i];
            double scale = (BarHeight - 4) / max;
            string tip = $"{Day(b.When)}: {Seconds(b.TotalMs)}\nWindows {Seconds(b.MainPathMs)} · programas e área de trabalho {Seconds(b.PostBootMs)}";
            items.Add(new BootBarItem(Math.Max(2, b.MainPathMs * scale), b.PostBootMs * scale, b.When.ToString("dd/MM", PtBr), tip, i == boots.Count - 1));
        }
        Ui.SetItems(BootBars, items);
        Ui.SetItems(BootDays, items);
    }

    private async void BtnStartupScan_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    // ---------- Ações ----------

    // Chaves mudadas nesta sessão (nome → ligado): o estado conta todas, não só a última; mudar e desfazer tira da conta
    private readonly Dictionary<string, (string Name, bool On)> _pending = new();

    private void SayPending(StartupEntry item, bool turnOn)
    {
        string key = $"{item.Source}|{item.Name}|{item.Command}";
        if (_pending.TryGetValue(key, out var before) && before.On != turnOn) _pending.Remove(key);
        else _pending[key] = (item.Title, turnOn);
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
            UpdateCounts();
        }
        catch (Exception ex) { box.IsChecked = item.Enabled; Say($"Não foi possível mudar {item.Title}", ex.Message); }
        await ScanAsync(announce: false);
    }

    private void StartupOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: StartupEntry { Location: { } folder } item }) return;
        try
        {
            string? exe = item.IconPath != null && File.Exists(item.IconPath) && !item.IconPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? item.IconPath : null;
            // Com o arquivo, o Explorador abre a pasta já com ele selecionado
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
            if (exe != null) start.ArgumentList.Add("/select," + exe); else start.ArgumentList.Add(folder);
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) { Say("Não foi possível abrir a pasta", ex.Message); }
    }

    /// <summary>
    /// Remover não pede confirmação: o programa vai para Removidos, ao lado, e volta com um clique. Só os de todos os usuários
    /// pedem o CoreBi como administrador.
    /// </summary>
    internal async void StartupRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StartupEntry item } || !item.CanRemove) return;
        if (item.Machine && !RequireAdmin()) return;
        try
        {
            using var critical = _context.BeginCriticalOperation("Aguarde a alteração da inicialização do Windows terminar.");
            await Task.Run(() => _startup.Remove(item));
            Say($"{item.Title} removido", "Dá para devolver em Removidos.");
        }
        catch (Exception ex) { Say($"Não foi possível remover {item.Title}", ex.Message); }
        await ScanAsync(announce: false);
    }

    private async void StartupRestoreOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StartupBackup backup }) await RestoreAsync(new[] { backup });
    }

    internal async void BtnStartupRestore_Click(object sender, RoutedEventArgs e) => await RestoreAsync(_removed.ToList());

    private async Task RestoreAsync(IReadOnlyList<StartupBackup> removed)
    {
        if (removed.Count == 0) return;
        if (removed.Any(b => b.Machine) && !RequireAdmin()) return;
        var failed = new List<string>();
        int done = 0;
        bool steps = removed.Count > 1; // um só não abre o medidor: é instantâneo
        Say(removed.Count == 1 ? $"Devolvendo {removed[0].DisplayName}…" : $"Devolvendo {removed.Count} programas…");
        if (steps) ToolPage.SetProgress(StartupProgressFill, 0);
        foreach (var b in removed)
        {
            try { using var critical = _context.BeginCriticalOperation("Aguarde a restauração dos programas de inicialização terminar."); await Task.Run(() => _startup.Restore(b)); }
            catch (Exception ex) { failed.Add($"{b.DisplayName} ({ex.Message})"); }
            if (steps) ToolPage.SetProgress(StartupProgressFill, (double)++done / removed.Count);
        }
        int ok = removed.Count - failed.Count;
        if (failed.Count == 0) Say(ok == 1 ? $"{removed[0].DisplayName} devolvido" : $"{ok} programas devolvidos", NextBoot);
        else
        {
            if (steps) ToolPage.EndProgress(StartupProgressFill);
            Say(removed.Count == 1 ? $"Não foi possível devolver {removed[0].DisplayName}" : $"{ok} de {removed.Count} devolvidos", "Falhou: " + string.Join("; ", failed));
        }
        await ScanAsync(announce: false);
    }
}
