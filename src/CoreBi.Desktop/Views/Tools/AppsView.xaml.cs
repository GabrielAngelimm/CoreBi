using CoreBi.Desktop.Services;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Tools.Apps;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

/// <summary>Aba Ferramentas · Instalar apps: essenciais pós-formatação pelo winget.</summary>
public partial class AppsView : UserControl
{
    private FeatureContext _context = null!;
    public AppsView() => InitializeComponent();
    internal void Initialize(FeatureContext context) => _context = context;

    internal List<InstallableApp>? _installApps;
    internal bool _appsBusy, _appsStop;
    private List<AppTile> _tiles = new();
    private bool _wingetAvailable;

    /// <summary>Largura mínima de uma célula da grade (cartão e o vão de 12 px): o nome e o estado cabem na mesma linha.</summary>
    private const double MinCell = 220;

    /// <summary>
    /// Quantos cartões por linha: o máximo que a largura deixa define quantas linhas a grade precisa, e as colunas se dividem
    /// por igual entre elas, para a última linha não ficar com um cartão sozinho (7 apps: uma linha de 7 na tela larga, 4 e 3
    /// nas menores).
    /// </summary>
    private void AppsList_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateColumns();

    private void UpdateColumns()
    {
        int count = Math.Max(1, _tiles.Count);
        int fit = Math.Max(2, (int)(AppsList.ActualWidth / MinCell));
        int rows = (count + fit - 1) / fit;
        int columns = Math.Max(1, (count + rows - 1) / rows);
        if (AppColumns != columns) AppColumns = columns;
    }

    public static readonly DependencyProperty AppColumnsProperty =
        DependencyProperty.Register(nameof(AppColumns), typeof(int), typeof(AppsView), new PropertyMetadata(4));
    /// <summary>Cartões por linha da grade (lido pelo painel da lista).</summary>
    public int AppColumns { get => (int)GetValue(AppColumnsProperty); set => SetValue(AppColumnsProperty, value); }

    private void Say(string status, string detail = "") => ToolPage.Say(AppsStatus, AppsDetail, status, detail);

    /// <summary>O botão principal diz quantos apps vai instalar; sem nenhum marcado, fica desligado.</summary>
    private void UpdateInstallButton()
    {
        if (_appsBusy || _installApps == null) return;
        int n = _installApps.Count(a => a.Selected && !a.Installed);
        BtnAppsInstall.Content = n == 0 ? "Instalar apps" : n == 1 ? "Instalar 1 app" : $"Instalar {n} apps";
        BtnAppsInstall.IsEnabled = _wingetAvailable && n > 0 && !_appsRefreshing;
        UpdateToggleAll();
    }

    /// <summary>Marcar ou desmarcar todos num botão só: com todos os que faltam já marcados, ele desmarca; sem nada a marcar, some.</summary>
    private void UpdateToggleAll()
    {
        if (_installApps == null) return;
        var open = _installApps.Where(a => !a.Installed).ToList();
        bool all = open.Count > 0 && open.All(a => a.Selected);
        BtnAppsToggleAll.Content = all ? "Desmarcar todos" : "Marcar todos";
        Ui.SetIcon(BtnAppsToggleAll, all ? "" : "");
        BtnAppsToggleAll.ToolTip = all ? null : "Marca os que ainda não estão instalados.";
        BtnAppsToggleAll.Visibility = open.Count > 0 ? Visibility.Visible : Visibility.Hidden;
    }

    private bool _appsRefreshing;
    internal async Task RefreshAsync(bool entering = false)
    {
        if (_appsBusy || _appsRefreshing || _context == null || _context.IsClosing) return;
        _appsRefreshing = true;
        // Voltar à página relê em silêncio: os cartões não apagam, sem "Lendo…" nem medidor (as marcas só mudam se mudou algo)
        bool quiet = entering && _installApps != null;
        if (!quiet)
        {
            AppsList.IsEnabled = false;
            BtnAppsInstall.IsEnabled = false;
            Say("Lendo aplicativos instalados…");
            ToolPage.SetProgress(AppsProgressFill, 0);
        }
        try
        {
            bool first = _installApps == null;
            if (first) Skeleton.SetActive(AppsCard, true); // até saber o que já está instalado, as marcas dos cartões não valem
            if (_installApps == null)
            {
                _installApps = AppInstaller.Catalog();
                _tiles = _installApps.Select(a => new AppTile(a)).ToList();
                foreach (var app in _installApps)
                    app.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(InstallableApp.Selected) or nameof(InstallableApp.Installed)) UpdateInstallButton(); };
                AppsList.ItemsSource = _tiles;
                AppsCount.Text = _installApps.Count.ToString();
                UpdateColumns();
            }
            var names = await Task.Run(AppInstaller.InstalledNames, _context.Lifetime);
            if (entering) await PageMotion.EntranceDone(this);
            if (_context.IsClosing) return;
            foreach (var app in _installApps)
            {
                app.Installed = AppInstaller.IsInstalled(app, names);
                if (first) app.Selected = app.PreSelect && !app.Installed;
                else if (app.Installed) app.Selected = false;
            }
            _wingetAvailable = AppInstaller.WingetAvailable;
            BtnAppsStore.Visibility = _wingetAvailable ? Visibility.Collapsed : Visibility.Visible;
            if (!quiet) ToolPage.SetProgress(AppsProgressFill, 1);
            int installed = _installApps.Count(a => a.Installed);
            if (_wingetAvailable) Say($"{installed} de {_installApps.Count} já instalados");
            else Say("winget não encontrado", "Atualize o Instalador de Aplicativo na Microsoft Store e abra esta página de novo.");
        }
        catch (OperationCanceledException) { ToolPage.EndProgress(AppsProgressFill); }
        catch (Exception ex) { DiagnosticLog.Write("apps.inventory", ex); Say("Não foi possível ler os apps instalados", ex.Message); ToolPage.EndProgress(AppsProgressFill); }
        finally
        {
            _appsRefreshing = false;
            Skeleton.SetActive(AppsCard, false);
            if (!_context.IsClosing) { AppsList.IsEnabled = true; UpdateInstallButton(); }
        }
    }

    private void BtnAppsToggleAll_Click(object sender, RoutedEventArgs e)
    {
        if (_installApps == null || _appsBusy) return;
        var open = _installApps.Where(a => !a.Installed).ToList();
        bool selectAll = !(open.Count > 0 && open.All(a => a.Selected));
        foreach (var a in _installApps) a.Selected = selectAll && !a.Installed;
    }

    private void BtnAppsStore_Click(object sender, RoutedEventArgs e)
    {
        // Página do "Instalador de Aplicativo" (App Installer), que traz o winget.
        try { Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9NBLGGH4NNS1") { UseShellExecute = true }); }
        catch (Exception ex) { Say("Não foi possível abrir a Microsoft Store", ex.Message); }
    }

    private async void BtnAppsInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_appsBusy) { _appsStop = true; BtnAppsInstall.IsEnabled = false; Say("Parando depois do app atual…"); return; }
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.InstallApps);
        if (workload == null) { Say("Termine a outra operação antes"); return; }
        using var critical = _context.BeginCriticalOperation("Aguarde a instalação terminar. Em Instalar apps, Parar encerra a fila depois do app atual.");
        if (_installApps == null) return;

        var sel = _tiles.Where(t => t.App.Selected && !t.App.Installed).ToList();
        if (sel.Count == 0) { Say("Nenhum app marcado"); return; }
        if (sel.Any(t => t.App.WingetId == "RARLab.WinRAR") && sel.Any(t => t.App.WingetId == "7zip.7zip"))
        {
            var r = MessageBox.Show("WinRAR e 7-Zip fazem a mesma coisa. Instalar os dois mesmo assim?", "Instalar apps",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }

        foreach (var t in _tiles) t.Phase = AppPhase.Idle;
        _appsBusy = true; _appsStop = false;
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
                    if (success) { ok++; app.Selected = false; }
                    else failed.Add(app.Name);
                }
                catch (Exception ex) { app.Status = $"Falhou: {ex.Message}"; tile.Phase = AppPhase.Failed; failed.Add(app.Name); }
                done++;
                ToolPage.SetProgress(AppsProgressFill, (double)done / sel.Count);
            }

            AppInstaller.RefreshInstalled(_installApps);
        }
        finally
        {
            _appsBusy = false;
            if (done < sel.Count) ToolPage.EndProgress(AppsProgressFill);
            Ui.SetRunning(BtnAppsInstall, false, "Instalar apps", idleIcon: "");
            UpdateInstallButton();
        }
        string failures = failed.Count > 0 ? "Falharam: " + string.Join(", ", failed) : "";
        if (_appsStop) Say($"Parado: {ok} de {sel.Count} instalados", failures);
        else if (ok == sel.Count) Say(ok == 1 ? "1 app instalado" : $"{ok} apps instalados");
        else Say($"{ok} de {sel.Count} instalados", failures);
    }
}

public enum AppPhase { Idle, Queued, Installing, Done, Failed, Skipped }

public sealed class AppTile : ObservableObject
{
    internal AppTile(InstallableApp app)
    {
        App = app;
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(InstallableApp.Installed) or nameof(InstallableApp.Status)) Changed();
        };
    }

    public InstallableApp App { get; }
    public string Tip => Phase == AppPhase.Failed && App.Status.Length > 0 ? $"{App.Status}\nPacote winget: {App.WingetId}" : $"Pacote winget: {App.WingetId}";

    private AppPhase _phase;
    public AppPhase Phase { get => _phase; set { if (_phase == value) return; _phase = value; Changed(); } }

    public string StateText => Phase switch
    {
        AppPhase.Queued => "Na fila",
        AppPhase.Installing => "Instalando…",
        AppPhase.Skipped => "Parado",
        AppPhase.Done => "Instalado",
        AppPhase.Failed => "Falhou",
        _ => App.Installed ? "Instalado" : "",
    };

    public string Tone => Phase switch
    {
        AppPhase.Queued or AppPhase.Skipped => "Faint",
        AppPhase.Installing => "Signal",
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
