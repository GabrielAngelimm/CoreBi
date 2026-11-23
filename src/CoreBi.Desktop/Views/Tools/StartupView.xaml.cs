using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Tools.Startup;

namespace CoreBi.Desktop.Views;

/// <summary>Linha da tabela: a entrada, o ícone já carregado e as dicas montadas.</summary>
public sealed class StartupRow(StartupEntry entry, ImageSource? icon)
{
    public StartupEntry Entry { get; } = entry;
    public ImageSource? Icon { get; } = icon;
    public bool Glyph { get; } = FileIcons.IsGlyph(entry.IconPath);
    /// <summary>Dica do nome: o nome da entrada no Windows (quando é outro) e a descrição do arquivo.</summary>
    public string NameTip => string.Join("\n", new[]
    {
        Entry.Title,
        Entry.Name != Entry.Title ? "Entrada: " + Entry.Name : "",
        Entry.Detail.Length > 0 && Entry.Detail != Entry.Title ? Entry.Detail : "",
    }.Where(s => s.Length > 0));
    /// <summary>A empresa sem a razão social ("Microsoft Corporation" → "Microsoft"); a dica mostra o nome inteiro.</summary>
    public string Publisher => HardwareNaming.CompanyName(Entry.Publisher);
    public string CommandTip => Entry.Store ? "Tarefa de inicialização de um app da Microsoft Store.\n" + Entry.Command : Entry.Command + "\n" + Entry.Source;
    public string OriginTip => Entry.Origin switch
    {
        "Microsoft Store" => "Tarefa do app da Store: liga e desliga aqui, mas sai só desinstalando o app.",
        var o when o.EndsWith("todos") => "Vale para todos os usuários do PC. Mudar pede o CoreBi como administrador.",
        _ => "Vale só para o seu usuário.",
    };
}

/// <summary>Linha de "Removidos", nas mesmas colunas da tabela: quando saiu, o fabricante, o comando e a origem.</summary>
public sealed class RemovedRow(StartupBackup backup, ImageSource? icon)
{
    public StartupBackup Backup { get; } = backup;
    public ImageSource? Icon { get; } = icon;
    public bool Glyph { get; } = FileIcons.IsGlyph(backup.IconPath);
    public string Title => Backup.DisplayName;
    public string When => Backup.RemovedAt is { } at ? "removido " + StartupView.Day(at) : "";
    public string Publisher => HardwareNaming.CompanyName(Backup.Publisher);
    public string PublisherTip => Backup.Publisher ?? "";
    public string Command => Backup.Command ?? Backup.Original ?? "";
    public string Origin => StartupEntry.OriginOf(Backup.Kind, Backup.Machine);
}

/// <summary>Aba Ferramentas · Inicialização: programas que abrem junto com o Windows e os que saíram por aqui.</summary>
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
    private static readonly Thickness HeadMargin = new(8, 16, 8, 0);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly StartupService _startup = new();
    private IReadOnlyList<StartupBackup> _removed = Array.Empty<StartupBackup>();
    private List<StartupRow> _rows = new();
    private ICollectionView? _view;
    private string _filter = "all";
    private HashSet<StartupRow> _passing = new();
    /// <summary>Por que a lista de removidos está vazia quando não deu para ler (sem administrador); null, o texto de sempre.</summary>
    private string? _removedNote;
    private const string EmptyRemovedText = "Nada removido. O que você tirar pela lixeira aparece aqui, para devolver quando quiser.";

    private void StartupListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        => ToolPage.AlignHead(StartupListScroll, StartupHead, HeadMargin);

    private void Say(string status, string detail = "") => ToolPage.Say(StartupSummary, StartupResult, status, detail);

    /// <summary>Leitura pedida (abrir a página, "Ler novamente"): o subtítulo mostra a hora da leitura.</summary>
    internal Task RefreshAsync(bool entering = false) => ScanAsync(announce: true, entering);

    private bool _scanning;

    private static string Signature(IEnumerable<StartupEntry> list) =>
        string.Join("\n", list.Select(e => $"{e.Source}|{e.Name}|{e.Title}|{e.Command}|{e.Enabled}|{e.Machine}|{e.FileMissing}|{e.IconPath}"));
    private static string Signature(IEnumerable<StartupBackup> list) => string.Join("\n", list.Select(b => b.Id));

    /// <summary>Lê a lista e os removidos. Depois de uma ação (announce = false) o estado continua mostrando o resultado dela.</summary>
    private async Task ScanAsync(bool announce, bool entering = false)
    {
        if (_scanning || _context == null || _context.IsClosing) return;
        _scanning = true;
        // Voltar à página relê em silêncio: sem "Lendo…" nem medidor, só a hora no fim (e a lista só muda se mudou)
        bool quiet = entering && StartupList.ItemsSource != null;
        if (announce && !quiet) Say("Lendo programas…");
        if (StartupList.ItemsSource == null) Skeleton.Set(true, StartupCard);
        List<StartupEntry> list;
        string backupError = "";
        try
        {
            list = await Task.Run(_startup.List, _context.Lifetime);
            if (_context.IsClosing) return;
            _removedNote = null;
            try { _removed = await Task.Run(() => _startup.Backups, _context.Lifetime); }
            catch (UnauthorizedAccessException) { _removed = Array.Empty<StartupBackup>(); _removedNote = "Abra o CoreBi como administrador para ver os removidos."; }
            catch (Exception ex) { _removed = Array.Empty<StartupBackup>(); backupError = "Não foi possível ler os removidos: " + ex.Message; }
            // Ícones lidos fora da tela (o primeiro acesso a cada arquivo leva alguns milissegundos); depois vêm do cache
            await Task.Run(() =>
            {
                foreach (var e in list) FileIcons.IsGlyph(e.IconPath);
                foreach (var b in _removed) FileIcons.IsGlyph(b.IconPath);
            }, _context.Lifetime);
        }
        catch (Exception ex) { Say("Não foi possível ler a inicialização", ex.Message); _scanning = false; Skeleton.Set(false, StartupCard); return; }
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
        Skeleton.Set(false, StartupCard);
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
        bool removed = _filter == "removed";
        if (ActivePane == null || RemovedPane == null) return;
        ActivePane.Visibility = removed ? Visibility.Collapsed : Visibility.Visible;
        RemovedPane.Visibility = removed ? Visibility.Visible : Visibility.Collapsed;
        BtnStartupRestore.Visibility = removed && _removed.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _passing = _rows.Where(r => _filter switch { "on" => r.Entry.Enabled, "off" => !r.Entry.Enabled, _ => true }).ToHashSet();
        _view?.Refresh();
        StartupListScroll?.ScrollToTop();
    }

    private void UpdateCounts()
    {
        int on = _rows.Count(r => r.Entry.Enabled);
        Ui.SetSuffix(FilterAll, _rows.Count.ToString(PtBr));
        Ui.SetSuffix(FilterOn, on.ToString(PtBr));
        Ui.SetSuffix(FilterOff, (_rows.Count - on).ToString(PtBr));
        Ui.SetSuffix(FilterRemoved, _removedNote != null ? "—" : _removed.Count.ToString(PtBr));
    }

    // ---------- Removidos ----------

    private void ShowRemoved()
    {
        if (RemovedList.ItemsSource is not IEnumerable<RemovedRow> shown || Signature(shown.Select(r => r.Backup)) != Signature(_removed))
            RemovedList.ItemsSource = _removed.Select(b => new RemovedRow(b, FileIcons.Get(b.IconPath))).ToList();
        RemovedEmpty.Text = _removedNote ?? EmptyRemovedText;
        RemovedEmpty.Visibility = _removed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnStartupRestore.Visibility = _filter == "removed" && _removed.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCounts();
    }

    /// <summary>"hoje às 14:05", "ontem às 09:12", "21/11 às 18:30".</summary>
    internal static string Day(DateTime at)
    {
        string day = at.Date == DateTime.Today ? "hoje" : at.Date == DateTime.Today.AddDays(-1) ? "ontem" : at.ToString("dd/MM", PtBr);
        return $"{day} às {at:HH:mm}";
    }

    /// <summary>
    /// Capturas dos testes ("PageStartup:demo"): três removidos de exemplo, com o filtro em Removidos, para ver a tabela deles mesmo
    /// sem administrador. Nada é lido nem salvo; desligar volta ao que foi lido de verdade.
    /// </summary>
    internal void PreviewDemo(string? mode)
    {
        if (mode == null)
        {
            _removed = _realRemoved ?? _removed;
            _realRemoved = null;
            _removedNote = _realNote;
            ShowRemoved();
            FilterAll.IsChecked = true;
            return;
        }
        _realRemoved = _removed;
        _realNote = _removedNote;
        var now = DateTime.Now;
        string Program(string relative) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), relative);
        string steam = Program(@"Steam\steam.exe"), edge = Program(@"Microsoft\Edge\Application\msedge.exe"),
            spotify = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");
        _removed = new[]
        {
            new StartupBackup { Name = "Steam", Title = "Steam", Publisher = "Valve Corporation", IconPath = steam, Kind = "Run", Command = $"\"{steam}\" -silent", RemovedAt = now.AddMinutes(-12) },
            new StartupBackup { Name = "MicrosoftEdgeAutoLaunch", Title = "Microsoft Edge", Publisher = "Microsoft Corporation", IconPath = edge, Kind = "Run",
                Command = $"\"{edge}\" --no-startup-window --win-session-start", RemovedAt = now.AddDays(-1).AddHours(-3) },
            new StartupBackup { Name = "Spotify", Title = "Spotify", Publisher = "Spotify AB", IconPath = spotify, Kind = "Run", Command = $"\"{spotify}\" --autostart --minimized", RemovedAt = now.AddDays(-4) },
        };
        _removedNote = null;
        ShowRemoved();
        FilterRemoved.IsChecked = true;
    }

    private IReadOnlyList<StartupBackup>? _realRemoved;
    private string? _realNote;

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
            string args = exe != null ? $"/select,\"{exe}\"" : $"\"{folder}\"";
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), args) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) { Say("Não foi possível abrir a pasta", ex.Message); }
    }

    /// <summary>
    /// Remover não pede confirmação: o programa vai para o filtro Removidos e volta com um clique. Só os de todos os usuários
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
            Say($"{item.Title} removido", "Dá para devolver no filtro Removidos.");
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
