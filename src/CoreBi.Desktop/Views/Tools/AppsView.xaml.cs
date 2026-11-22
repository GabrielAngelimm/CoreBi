using CoreBi.Desktop.Services;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Tools.Apps;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoreBi.Desktop.Views;

/// <summary>Aba Ferramentas · Instalar apps: o catálogo no estilo do Ninite, instalado pelo winget.</summary>
public partial class AppsView : UserControl
{
    private FeatureContext _context = null!;
    public AppsView() => InitializeComponent();
    internal void Initialize(FeatureContext context) => _context = context;

    internal List<InstallableApp>? _installApps;
    internal bool _appsBusy, _appsStop;
    private List<AppTile> _tiles = new();
    private List<AppGroup> _groups = new();
    private readonly List<FrameworkElement> _groupViews = new();
    /// <summary>O pacote, na ordem em que os apps foram marcados (é a ordem da instalação).</summary>
    private readonly ObservableCollection<AppTile> _package = new();
    private bool _wingetAvailable, _syncingKits;
    private string _view = "all";
    /// <summary>Categoria escolhida pelo título (nula: todas).</summary>
    private string? _category;

    // Geometria das colunas: largura mínima de uma coluna, o vão entre elas e as alturas que entram na divisão
    private const double MinColumn = 250, ColumnGap = 28, GroupHead = 37, RowHeight = 32, GroupGap = 14;

    private void Say(string status, string detail = "") => ToolPage.Say(AppsStatus, AppsDetail, status, detail);

    // ----------------------------------------------------------------------------------------------------------------- montagem

    /// <summary>Monta as linhas e os grupos uma vez; as colunas são redistribuídas quando a largura muda.</summary>
    private void Build()
    {
        _installApps = AppInstaller.Catalog();
        _tiles = _installApps.Select(a => new AppTile(a)).ToList();
        _groups = AppCatalog.Categories
            .Select(c => new AppGroup(c, _tiles.Where(t => t.App.Category == c).ToList()))
            .Where(g => g.Tiles.Count > 0).ToList();
        var template = (DataTemplate)Resources["CategoryGroup"];
        _groupViews.Clear();
        foreach (var g in _groups) _groupViews.Add(new ContentPresenter { Content = g, ContentTemplate = template, Focusable = false });
        foreach (var tile in _tiles)
            tile.App.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(InstallableApp.Selected)) OnSelectedChanged(tile);
                else if (e.PropertyName == nameof(InstallableApp.Installed)) { UpdateCounts(); ApplyFilter(); }
            };
        _package.CollectionChanged += (_, _) => RebuildPack();
        AppsCount.Text = _tiles.Count.ToString();
        LayoutColumns(force: true);
        RebuildPack();
    }

    private void AppsColumns_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) LayoutColumns();
    }

    private int _columns;

    /// <summary>
    /// Quantas colunas cabem (até 5) e quais categorias vão em cada uma: as categorias seguem a ordem do catálogo e são divididas
    /// em blocos contíguos com a menor altura máxima possível, para as colunas terminarem quase juntas.
    /// </summary>
    private void LayoutColumns(bool force = false)
    {
        if (_groups.Count == 0) return;
        double width = AppsListScroll.ActualWidth > 0 ? AppsListScroll.ActualWidth : 1500;
        int columns = Math.Clamp((int)((width + ColumnGap) / (MinColumn + ColumnGap)), 1, 5);
        if (columns == _columns && !force) return;
        _columns = columns;

        var heights = _groups.Select(g => GroupHead + g.Tiles.Count * RowHeight + GroupGap).ToArray();
        var split = Partition(heights, columns);

        foreach (var view in _groupViews)
            if (view.Parent is Panel old) old.Children.Remove(view);
        AppsColumns.Children.Clear();
        AppsColumns.ColumnDefinitions.Clear();
        int start = 0;
        for (int c = 0; c < split.Count; c++)
        {
            if (c > 0) AppsColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColumnGap) });
            AppsColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var stack = new StackPanel();
            for (int i = start; i < start + split[c]; i++) stack.Children.Add(_groupViews[i]);
            start += split[c];
            Grid.SetColumn(stack, c * 2);
            AppsColumns.Children.Add(stack);
        }
    }

    /// <summary>Divide <paramref name="heights"/> em até <paramref name="parts"/> blocos contíguos minimizando o bloco mais alto.</summary>
    internal static List<int> Partition(IReadOnlyList<double> heights, int parts)
    {
        int n = heights.Count;
        parts = Math.Max(1, Math.Min(parts, n));
        var prefix = new double[n + 1];
        for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + heights[i];
        // best[k, i]: menor altura máxima pondo os i primeiros em k blocos; cut guarda onde começa o último bloco
        var best = new double[parts + 1, n + 1];
        var cut = new int[parts + 1, n + 1];
        for (int i = 1; i <= n; i++) best[1, i] = prefix[i];
        for (int k = 2; k <= parts; k++)
            for (int i = k; i <= n; i++)
            {
                best[k, i] = double.MaxValue;
                for (int j = k - 1; j < i; j++)
                {
                    double value = Math.Max(best[k - 1, j], prefix[i] - prefix[j]);
                    if (value < best[k, i]) { best[k, i] = value; cut[k, i] = j; }
                }
            }
        var sizes = new List<int>();
        int end = n;
        for (int k = parts; k >= 1; k--)
        {
            int begin = k == 1 ? 0 : cut[k, end];
            sizes.Insert(0, end - begin);
            end = begin;
        }
        return sizes;
    }

    // ------------------------------------------------------------------------------------------------------- seleção e pacote

    private void OnSelectedChanged(AppTile tile)
    {
        if (tile.App.Selected && !_package.Contains(tile)) _package.Add(tile);
        else if (!tile.App.Selected && _package.Contains(tile) && !_appsBusy) _package.Remove(tile);
        UpdateInstallButton();
        UpdateCounts();
        SyncKits();
        if (_view == "pack") ApplyFilter();
    }

    /// <summary>Refaz a faixa do pacote: uma ficha por app e, no fim, o indicador "+N" das que não cabem.</summary>
    private void RebuildPack()
    {
        var template = (DataTemplate)Resources["PackChip"];
        PackStrip.Children.Clear();
        foreach (var tile in _package)
            PackStrip.Children.Add(new ContentPresenter { Content = tile, ContentTemplate = template, Focusable = false });
        PackStrip.Children.Add(MoreChip());
        bool empty = _package.Count == 0;
        PackEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        PackStrip.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        BtnPackClear.Visibility = empty || _appsBusy ? Visibility.Hidden : Visibility.Visible;
        PackCount.Text = empty ? "" : _package.Count.ToString();
    }

    /// <summary>Indicador "+N" do fim da faixa, com a lista dos que não couberam na dica.</summary>
    private Border MoreChip()
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        text.SetResourceReference(StyleProperty, "ItemTitle");
        text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(OverflowStrip.HiddenCount)) { Source = PackStrip, StringFormat = "+{0}" });
        var chip = new Border { Height = 44, MinWidth = 44, CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), Padding = new Thickness(14, 0, 14, 0), Child = text };
        chip.SetResourceReference(Border.BorderBrushProperty, "Line");
        chip.SetResourceReference(Border.BackgroundProperty, "Raised");
        chip.ToolTipOpening += (_, _) =>
            chip.ToolTip = string.Join("\n", _package.Skip(_package.Count - PackStrip.HiddenCount).Select(t => t.App.Name));
        chip.ToolTip = "";
        return chip;
    }

    private void PackRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_appsBusy || (sender as FrameworkElement)?.DataContext is not AppTile tile) return;
        tile.App.Selected = false;
    }

    private void BtnPackClear_Click(object sender, RoutedEventArgs e)
    {
        if (_appsBusy) return;
        foreach (var tile in _package.ToList()) tile.App.Selected = false;
    }

    /// <summary>
    /// Kit: com todos os apps dele que faltam já no pacote, tira; senão, põe os que faltam. Fica aceso quando o pacote tem o kit
    /// inteiro (o que já está instalado não conta) e apagado quando não há nada dele a instalar.
    /// </summary>
    private void Kit_Click(object sender, RoutedEventArgs e)
    {
        if (_installApps == null || _appsBusy || sender is not CheckBox chip || !Enum.TryParse<AppKit>(chip.Tag as string, out var kit)) return;
        var open = _installApps.Where(a => a.Kits.HasFlag(kit) && !a.Installed).ToList();
        bool all = open.Count > 0 && open.All(a => a.Selected);
        foreach (var a in open) a.Selected = !all;
        SyncKits();
    }

    private void SyncKits()
    {
        if (_installApps == null || _syncingKits) return;
        _syncingKits = true;
        foreach (var chip in new[] { KitPost, KitGames, KitWork, KitCreator, KitDev })
        {
            if (!Enum.TryParse<AppKit>(chip.Tag as string, out var kit)) continue;
            var apps = _installApps.Where(a => a.Kits.HasFlag(kit)).ToList();
            var open = apps.Where(a => !a.Installed).ToList();
            chip.IsChecked = open.Count > 0 && open.All(a => a.Selected);
            chip.IsEnabled = open.Count > 0 && !_appsBusy;
            chip.ToolTip = open.Count == 0
                ? "Todos os apps deste kit já estão instalados."
                : (chip.IsChecked == true ? "Tira do pacote: " : "Põe no pacote: ") + string.Join(", ", open.Select(a => a.Name)) + ".";
        }
        _syncingKits = false;
    }

    // ---------------------------------------------------------------------------------------------------- filtro, busca, contagens

    private void AppsView_Checked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is RadioButton { Tag: string view }) { _view = view; ApplyFilter(); }
    }

    private void AppsSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    /// <summary>O título da categoria filtra por ela; clicar de novo na mesma volta a todas.</summary>
    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppGroup group) return;
        FilterCategory(_category == group.Name ? null : group.Name);
    }

    /// <summary>Escolhe a categoria como o clique no título (capturas de tela).</summary>
    internal void FilterCategory(string? name)
    {
        _category = name;
        foreach (var g in _groups) g.Active = g.Name == _category;
        ApplyFilter();
    }

    /// <summary>
    /// Estado (o filtro do topo), categoria (o título clicado) e busca se somam; o que não entra só apaga, nada sai do lugar. O
    /// título de uma categoria sem nenhuma linha acesa apaga junto.
    /// </summary>
    private void ApplyFilter()
    {
        // "Todos" já nasce marcado: o Checked dele chega durante o InitializeComponent, antes de a busca existir
        if (AppsSearch == null) return;
        string query = AppsSearch.Text.Trim();
        foreach (var tile in _tiles)
        {
            bool view = _view switch
            {
                "available" => !tile.App.Installed,
                "pack" => tile.App.Selected && !tile.App.Installed,
                "installed" => tile.App.Installed,
                _ => true,
            };
            bool category = _category == null || tile.App.Category == _category;
            bool match = query.Length == 0
                || tile.App.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || tile.App.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || tile.App.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            tile.Dimmed = !(view && category && match);
        }
        foreach (var g in _groups) g.Dimmed = g.Tiles.All(t => t.Dimmed);
    }

    private void UpdateCounts()
    {
        if (_installApps == null) return;
        int pack = _installApps.Count(a => a.Selected && !a.Installed), installed = _installApps.Count(a => a.Installed);
        ViewAll.Content = $"Todos  {_installApps.Count}";
        ViewAvailable.Content = $"Disponíveis  {_installApps.Count - installed}";
        ViewPack.Content = $"No pacote  {pack}";
        ViewInstalled.Content = $"Instalados  {installed}";
    }

    /// <summary>O botão principal diz quantos apps vai instalar; sem nenhum marcado, fica desligado.</summary>
    private void UpdateInstallButton()
    {
        if (_appsBusy || _installApps == null) return;
        int n = _installApps.Count(a => a.Selected && !a.Installed);
        BtnAppsInstall.Content = n == 0 ? "Instalar apps" : n == 1 ? "Instalar 1 app" : $"Instalar {n} apps";
        BtnAppsInstall.IsEnabled = _wingetAvailable && n > 0 && !_appsRefreshing;
    }

    // ------------------------------------------------------------------------------------------------------------------- leitura

    private bool _appsRefreshing;
    private CancellationTokenSource? _exportCts;

    internal async Task RefreshAsync(bool entering = false)
    {
        if (_appsBusy || _appsRefreshing || _context == null || _context.IsClosing) return;
        _appsRefreshing = true;
        // Voltar à página relê em silêncio: as linhas não apagam, sem "Lendo…" nem medidor (as marcas só mudam se mudou algo)
        bool quiet = entering && _installApps != null;
        if (!quiet)
        {
            AppsColumns.IsEnabled = false;
            BtnAppsInstall.IsEnabled = false;
            Say("Lendo aplicativos instalados…");
        }
        try
        {
            bool first = _installApps == null;
            if (first)
            {
                Skeleton.SetActive(AppsCard, true); // até saber o que já está instalado, as marcas das linhas não valem
                Build();
            }
            // Primeiro o registro (instantâneo), para a página acender; depois o winget confirma pelos IDs, em segundo plano
            var names = await Task.Run(AppInstaller.InstalledNames, _context.Lifetime);
            if (entering) await PageMotion.EntranceDone(this);
            if (_context.IsClosing) return;
            Apply(names, _wingetIds);
            _wingetAvailable = AppInstaller.WingetAvailable;
            BtnAppsStore.Visibility = _wingetAvailable ? Visibility.Collapsed : Visibility.Visible;
            if (_wingetAvailable) Say($"Lido às {DateTime.Now:HH:mm}");
            else Say("winget não encontrado", "Atualize o Instalador de Aplicativo na Microsoft Store e abra esta página de novo.");
            if (_wingetAvailable) _ = ConfirmWithWingetAsync(names);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DiagnosticLog.Write("apps.inventory", ex); Say("Não foi possível ler os apps instalados", ex.Message); }
        finally
        {
            _appsRefreshing = false;
            Skeleton.SetActive(AppsCard, false);
            if (!_context.IsClosing) { AppsColumns.IsEnabled = true; UpdateInstallButton(); }
        }
    }

    private HashSet<string>? _wingetIds;

    /// <summary>O "winget export" leva alguns segundos; quando volta, corrige o que o registro não vê (apps MSIX, nomes diferentes).</summary>
    private async Task ConfirmWithWingetAsync(HashSet<string> names)
    {
        _exportCts?.Cancel();
        var cts = _exportCts = CancellationTokenSource.CreateLinkedTokenSource(_context.Lifetime);
        try
        {
            var ids = await AppInstaller.InstalledPackageIdsAsync(cts.Token);
            if (ids == null || cts.IsCancellationRequested || _appsBusy || _context.IsClosing) return;
            _wingetIds = ids;
            Apply(names, ids);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DiagnosticLog.Write("apps.export", ex); }
    }

    private void Apply(IEnumerable<string> names, ISet<string>? wingetIds)
    {
        if (_installApps == null) return;
        var list = names as ICollection<string> ?? names.ToList();
        foreach (var app in _installApps)
        {
            app.Installed = AppInstaller.IsInstalled(app, list, wingetIds);
            if (app.Installed) app.Selected = false;
        }
        UpdateCounts();
        SyncKits();
        ApplyFilter();
        UpdateInstallButton();
    }

    private void BtnAppsStore_Click(object sender, RoutedEventArgs e)
    {
        // Página do "Instalador de Aplicativo" (App Installer), que traz o winget.
        try { Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9NBLGGH4NNS1") { UseShellExecute = true }); }
        catch (Exception ex) { Say("Não foi possível abrir a Microsoft Store", ex.Message); }
    }

    // ---------------------------------------------------------------------------------------------------------------- instalação

    private async void BtnAppsInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_appsBusy) { _appsStop = true; BtnAppsInstall.IsEnabled = false; Say("Parando depois do app atual…"); return; }
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.InstallApps);
        if (workload == null) { Say("Termine a outra operação antes"); return; }
        using var critical = _context.BeginCriticalOperation("Aguarde a instalação terminar. Em Instalar apps, Parar encerra a fila depois do app atual.");
        if (_installApps == null) return;

        var sel = _package.Where(t => t.App.Selected && !t.App.Installed).ToList();
        if (sel.Count == 0) { Say("Nenhum app marcado"); return; }

        _exportCts?.Cancel();
        foreach (var t in _tiles) t.Phase = AppPhase.Idle;
        _appsBusy = true; _appsStop = false;
        SetBusy(true);
        Ui.SetRunning(BtnAppsInstall, true, "Instalar apps", "Parar depois do atual", "");
        foreach (var t in sel) t.Phase = AppPhase.Queued;
        int ok = 0, done = 0;
        var failed = new List<string>();
        ToolPage.SetProgress(AppsProgressFill, 0);

        try
        {
            foreach (var tile in sel)
            {
                var app = tile.App;
                if (_appsStop) { tile.Phase = AppPhase.Skipped; continue; }
                Say($"Instalando {app.Name}…", $"{done + 1} de {sel.Count}");
                tile.Phase = AppPhase.Installing;
                try
                {
                    var (success, msg) = await AppInstaller.InstallAsync(app, _context.Lifetime);
                    app.Status = msg;
                    tile.Phase = success ? AppPhase.Done : AppPhase.Failed;
                    if (success) ok++;
                    else failed.Add(app.Name);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { app.Status = $"Falhou: {ex.Message}"; tile.Phase = AppPhase.Failed; failed.Add(app.Name); }
                done++;
                ToolPage.SetProgress(AppsProgressFill, (double)done / sel.Count);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _appsBusy = false;
            if (done < sel.Count) ToolPage.EndProgress(AppsProgressFill);
            Ui.SetRunning(BtnAppsInstall, false, "Instalar apps", idleIcon: "");
            SetBusy(false);
        }
        if (_context.IsClosing) return;

        // Os instalados saem do pacote; os que falharam ou ficaram parados continuam marcados, para tentar de novo
        foreach (var tile in sel.Where(t => t.Phase == AppPhase.Done)) tile.App.Selected = false;
        var names = await Task.Run(AppInstaller.InstalledNames);
        var finished = new HashSet<string>(_wingetIds ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var tile in sel.Where(t => t.Phase == AppPhase.Done)) foreach (var p in tile.App.Packages) finished.Add(p.Id);
        _wingetIds = finished;
        Apply(names, finished);
        RebuildPack();

        string failures = failed.Count > 0 ? "Falharam: " + string.Join(", ", failed) : "";
        if (_appsStop) Say($"Parado: {ok} de {sel.Count} instalados", failures);
        else if (ok == sel.Count) Say(ok == 1 ? "1 app instalado" : $"{ok} apps instalados");
        else Say($"{ok} de {sel.Count} instalados", failures);
    }

    /// <summary>Durante a instalação, o catálogo, os kits e o filtro param; a faixa vira a fila, com o estado de cada app.</summary>
    private void SetBusy(bool busy)
    {
        AppsColumns.IsEnabled = !busy;
        AppsKits.IsEnabled = !busy;
        AppsViewGroup.IsEnabled = !busy;
        PackTitle.Text = busy ? "Instalando" : "No pacote";
        PackNote.Text = busy ? "Um por vez, sem janelas" : "Na ordem em que vão instalar";
        BtnPackClear.Visibility = busy || _package.Count == 0 ? Visibility.Hidden : Visibility.Visible;
        if (!busy) SyncKits();
    }
}

public enum AppPhase { Idle, Queued, Installing, Done, Failed, Skipped }

/// <summary>Uma categoria do catálogo: o título (que filtra por ela), a quantidade e as linhas.</summary>
public sealed class AppGroup : ObservableObject
{
    internal AppGroup(string name, List<AppTile> tiles) { Name = name; Tiles = tiles; }
    public string Name { get; }
    public List<AppTile> Tiles { get; }
    public int Count => Tiles.Count;

    private bool _active, _dimmed;
    /// <summary>A categoria escolhida no filtro.</summary>
    public bool Active { get => _active; set { if (Set(ref _active, value)) OnPropertyChanged(nameof(HeadTip)); } }
    /// <summary>Nenhuma linha dela entra no filtro: o título apaga junto.</summary>
    public bool Dimmed { get => _dimmed; set => Set(ref _dimmed, value); }
    public string HeadTip => Active ? "Voltar a todas as categorias" : $"Mostrar só {Name}";
}

/// <summary>Uma linha do catálogo e a ficha do pacote: o app, o ícone e o estado da instalação.</summary>
public sealed class AppTile : ObservableObject
{
    internal AppTile(InstallableApp app)
    {
        App = app;
        Icon = AppIcons.Get(app.Icon);
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(InstallableApp.Installed) or nameof(InstallableApp.Status)) Changed();
        };
    }

    public InstallableApp App { get; }
    public ImageSource? Icon { get; }
    public bool ShowMonogram => Icon == null;
    public string Monogram => App.Name.TrimStart('.', ' ').Substring(0, 1).ToUpperInvariant();

    public string Tip
    {
        get
        {
            string ids = string.Join(", ", App.Packages.Select(p => p.Id));
            string head = Phase == AppPhase.Failed && App.Status.Length > 0 ? App.Status : App.Description;
            return $"{head}\nPacote winget: {ids}";
        }
    }

    private AppPhase _phase;
    public AppPhase Phase { get => _phase; set { if (_phase == value) return; _phase = value; Changed(); } }

    private bool _dimmed;
    /// <summary>Fora do filtro ou da busca: a linha fica quase apagada, mas no lugar.</summary>
    public bool Dimmed { get => _dimmed; set => Set(ref _dimmed, value); }

    public string StateText => Phase switch
    {
        AppPhase.Queued => "Na fila",
        AppPhase.Installing => "Instalando…",
        AppPhase.Skipped => "Parado",
        AppPhase.Done => "Instalado agora",
        AppPhase.Failed => "Falhou",
        _ => App.Installed ? "Instalado" : "",
    };

    public string Tone => Phase switch
    {
        AppPhase.Installing => "Signal",
        AppPhase.Done => "Ok",
        AppPhase.Failed => "Danger",
        _ => "",
    };

    private void Changed()
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(Tone));
        OnPropertyChanged(nameof(Tip));
    }
}

/// <summary>
/// Ícones oficiais dos apps (Assets/Apps/&lt;nome&gt;.png, tirados do instalador de cada fabricante por tools/AppIcons.ps1). As
/// marcas são dos donos; o ícone só identifica o app. Sem o arquivo, a linha mostra a inicial do nome.
/// </summary>
internal static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (Cache.TryGetValue(name, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            var uri = new Uri($"pack://application:,,,/CoreBi.Desktop;component/Assets/Apps/{name}.png", UriKind.Absolute);
            var info = Application.GetResourceStream(uri);
            if (info != null)
            {
                using var stream = info.Stream;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UriFormatException or NotSupportedException) { }
        Cache[name] = image;
        return image;
    }
}
