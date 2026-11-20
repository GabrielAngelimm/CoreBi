using CoreBi.Engine.Tools.Apps;
using System.ComponentModel;
using System.Windows.Controls;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Infrastructure;
using System.Diagnostics;
using System.Windows;

namespace CoreBi.Desktop.Views;

/// <summary>Aba Ferramentas · Instalar apps: essenciais pós-formatação pelo winget.</summary>
public partial class AppsView : UserControl
{
    internal FeatureContext Context { get; set; } = null!;
    public AppsView() => InitializeComponent();

    internal List<InstallableApp>? _installApps;
    internal bool _appsBusy, _appsStop;
    private List<AppTile> _tiles = new();
    private bool _wingetAvailable;
    private bool? _narrowHeader;

    private void AppsHeader_SizeChanged(object sender, SizeChangedEventArgs e)
        => _narrowHeader = ToolPage.PlaceConsole(AppsHeader, AppsConsole, _narrowHeader);

    /// <summary>Quatro cartões por linha na janela larga, três ou dois nas mais estreitas.</summary>
    private void AppsList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        int columns = AppsList.ActualWidth >= 1240 ? 4 : AppsList.ActualWidth >= 860 ? 3 : 2;
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
    }

    private bool _appsRefreshing;
    internal async Task InitAppsAsync()
    {
        if (_appsBusy || _appsRefreshing || Context.IsClosing) return;
        _appsRefreshing = true;
        AppsList.IsEnabled = false;
        BtnAppsInstall.IsEnabled = false;
        Say("Lendo aplicativos instalados…");
        try
        {
            bool first = _installApps == null;
            if (_installApps == null)
            {
                _installApps = AppInstaller.Catalog();
                _tiles = _installApps.Select(a => new AppTile(a)).ToList();
                foreach (var app in _installApps)
                    app.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(InstallableApp.Selected) or nameof(InstallableApp.Installed)) UpdateInstallButton(); };
                AppsList.ItemsSource = _tiles;
                AppsCount.Text = _installApps.Count.ToString();
            }
            var names = await Task.Run(AppInstaller.InstalledNames, Context.Lifetime);
            if (Context.IsClosing) return;
            foreach (var app in _installApps)
            {
                app.Installed = names.Any(name => name.StartsWith(app.DisplayPrefix, StringComparison.OrdinalIgnoreCase));
                if (first) app.Selected = app.PreSelect && !app.Installed;
                else if (app.Installed) app.Selected = false;
            }
            _wingetAvailable = AppInstaller.WingetAvailable;
            BtnAppsStore.Visibility = _wingetAvailable ? Visibility.Collapsed : Visibility.Visible;
            ToolPage.SetProgress(AppsProgressFill, 0);
            int installed = _installApps.Count(a => a.Installed);
            if (_wingetAvailable) Say($"{installed} de {_installApps.Count} já instalados");
            else Say("winget não encontrado", "Atualize o Instalador de Aplicativo na Microsoft Store e abra esta página de novo.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DiagnosticLog.Write("apps.inventory", ex); Say("Não foi possível ler os apps instalados", ex.Message); }
        finally
        {
            _appsRefreshing = false;
            if (!Context.IsClosing) { AppsList.IsEnabled = true; UpdateInstallButton(); }
        }
    }

    internal void BtnAppsAll_Click(object sender, RoutedEventArgs e)
    {
        if (_installApps == null || _appsBusy) return;
        foreach (var a in _installApps) a.Selected = !a.Installed;
    }

    internal void BtnAppsNone_Click(object sender, RoutedEventArgs e)
    {
        if (_installApps == null || _appsBusy) return;
        foreach (var a in _installApps) a.Selected = false;
    }

    internal void BtnAppsStore_Click(object sender, RoutedEventArgs e)
    {
        // Página do "Instalador de Aplicativo" (App Installer), que traz o winget.
        try { Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9NBLGGH4NNS1") { UseShellExecute = true }); }
        catch (Exception ex) { Say("Não foi possível abrir a Microsoft Store", ex.Message); }
    }

    internal async void BtnAppsInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_appsBusy) { _appsStop = true; BtnAppsInstall.IsEnabled = false; Say("Parando depois do app atual…"); return; }
        using var workload = Context.Workloads.TryBegin("Install apps");
        if (workload == null) { Say("Termine a outra operação antes"); return; }
        using var critical = Context.BeginCriticalOperation();
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
                    var (success, msg) = await AppInstaller.InstallAsync(app, Context.Lifetime);
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

/// <summary>Cartão de um app: o item do catálogo e a etapa desta instalação, que dá o estado mostrado embaixo do cartão.</summary>
public sealed class AppTile : INotifyPropertyChanged
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
    public string Tip => $"Pacote winget: {App.WingetId}";

    private AppPhase _phase;
    public AppPhase Phase { get => _phase; set { if (_phase == value) return; _phase = value; Changed(); } }

    /// <summary>O que o cartão mostra: o resultado desta instalação vale mais que o inventário; sem nada a dizer, fica vazio.</summary>
    public string StateText => Phase switch
    {
        AppPhase.Queued => "Na fila",
        AppPhase.Installing => "Instalando…",
        AppPhase.Skipped => "Parado antes de instalar",
        AppPhase.Done or AppPhase.Failed => App.Status.TrimEnd('.'),
        _ => App.Installed ? "Instalado" : "",
    };

    /// <summary>Tom do estado: Ok (instalado), Signal (instalando), Danger (falhou), Faint (na fila ou parado).</summary>
    public string Tone => Phase switch
    {
        AppPhase.Queued or AppPhase.Skipped => "Faint",
        AppPhase.Installing => "Signal",
        AppPhase.Done => "Ok",
        AppPhase.Failed => "Danger",
        _ => App.Installed ? "Ok" : "",
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tone)));
    }
}
