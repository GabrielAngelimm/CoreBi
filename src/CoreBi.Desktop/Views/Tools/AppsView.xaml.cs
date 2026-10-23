using System.Windows.Input;
using CoreBi.Engine.Tools.Apps;
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

    private bool _appsRefreshing;
    internal async Task InitAppsAsync()
    {
        if (_appsBusy || _appsRefreshing || Context.IsClosing) return;
        _appsRefreshing = true;
        AppsList.IsEnabled = false;
        BtnAppsInstall.IsEnabled = false;
        AppsStatus.Text = "Lendo aplicativos instalados…";
        try
        {
            bool first = _installApps == null;
            _installApps ??= AppInstaller.Catalog();
            AppsList.ItemsSource = _installApps;
            var names = await Task.Run(AppInstaller.InstalledNames, Context.Lifetime);
            if (Context.IsClosing) return;
            foreach (var app in _installApps)
            {
                app.Installed = names.Any(name => name.StartsWith(app.DisplayPrefix, StringComparison.OrdinalIgnoreCase));
                if (first) app.Selected = app.PreSelect && !app.Installed;
                else if (app.Installed) app.Selected = false;
            }
            bool available = AppInstaller.WingetAvailable;
            BtnAppsStore.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            BtnAppsInstall.IsEnabled = available;
            AppsStatus.Text = available ? $"{_installApps.Count(a => a.Installed)} de {_installApps.Count} já instalados. Selecione os apps desejados."
                : "O winget não foi encontrado. Atualize o Instalador de Aplicativo pela Microsoft Store e abra esta página novamente.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DiagnosticLog.Write("apps.inventory", ex); AppsStatus.Text = "Falhou: " + ex.Message; }
        finally { _appsRefreshing = false; if (!Context.IsClosing) AppsList.IsEnabled = true; }
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
        catch (Exception ex) { AppsStatus.Text = $"Não foi possível abrir a Microsoft Store: {ex.Message}"; }
    }

    internal async void BtnAppsInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_appsBusy) { _appsStop = true; BtnAppsInstall.IsEnabled = false; AppsStatus.Text = "Parando depois do app atual…"; return; }
        using var workload = Context.Workloads.TryBegin("Install apps");
        if (workload == null) { AppsStatus.Text = "Termine a outra operação antes."; return; }
        using var critical = Context.BeginCriticalOperation();
        if (_installApps == null) return;

        var sel = _installApps.Where(a => a.Selected && !a.Installed).ToList();
        if (sel.Count == 0) { AppsStatus.Text = "Nenhum app selecionado."; return; }
        if (sel.Any(a => a.WingetId == "RARLab.WinRAR") && sel.Any(a => a.WingetId == "7zip.7zip"))
        {
            var r = MessageBox.Show("WinRAR e 7-Zip fazem a mesma coisa. Instalar os dois mesmo assim?", "Instalar apps",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }

        _appsBusy = true; _appsStop = false;
        Ui.SetRunning(BtnAppsInstall, true, "Instalar selecionados", "Parar depois do atual", "\uE896");
        foreach (var a in sel) a.Status = "Na fila.";
        int ok = 0, done = 0;
        var failed = new List<string>();
        AppsProgress.ScaleX = 0;

        try
        {
            foreach (var app in sel)
            {
                if (_appsStop) { app.Status = "Não instalado (parado)."; continue; }
                AppsStatus.Text = $"Instalando {app.Name} ({done + 1} de {sel.Count})… baixando do site oficial, pode levar alguns minutos.";
                app.Status = "Instalando…";
                try
                {
                    var (success, msg) = await AppInstaller.InstallAsync(app, Context.Lifetime);
                    app.Status = msg;
                    if (success) { ok++; app.Selected = false; }
                    else failed.Add(app.Name);
                }
                catch (Exception ex) { app.Status = $"Falhou: {ex.Message}"; failed.Add(app.Name); }
                done++;
                AppsProgress.ScaleX = (double)done / sel.Count;
            }

            AppInstaller.RefreshInstalled(_installApps);
        }
        finally
        {
            _appsBusy = false;
            Ui.SetRunning(BtnAppsInstall, false, "Instalar selecionados", idleIcon: "\uE896");
            BtnAppsInstall.IsEnabled = AppInstaller.WingetAvailable;
        }
        AppsStatus.Text = $"{ok} de {sel.Count} instalados." +
                          (failed.Count > 0 ? $" Falharam: {string.Join(", ", failed)} (veja o motivo na lista)." : "") +
                          (_appsStop ? " Instalação interrompida." : "");
    }
}
