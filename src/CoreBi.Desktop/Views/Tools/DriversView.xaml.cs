using CoreBi.Desktop.Services;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Tools.Drivers;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;

namespace CoreBi.Desktop.Views;

/// <summary>Aba Drivers: placa de vídeo (verifica e instala), placa-mãe, Windows Update, backup e restauração, e o inventário com os dispositivos com problema.</summary>
public partial class DriversView : UserControl
{
    private FeatureContext _context = null!;
    public DriversView()
    {
        InitializeComponent();
        // Esqueleto já no primeiro quadro: ligado só depois da entrada, o painel vazio aparecia por um instante e piscava
        Skeleton.Set(true, DrvGpuCard, DrvBoardCard, DrvTableCard, DrvBackupCard);
    }
    internal void Initialize(FeatureContext context) => _context = context;
    private bool RequireAdmin() => PermissionGuard.RequireAdmin(_context.Owner);

    internal bool _drvInit, _drvBusy;
    internal List<GpuDriverStatus> _drvGpus = new();
    internal BoardSupportInfo _drvBoardInfo = new();
    internal List<WuDriver> _drvWu = new();
    internal CancellationTokenSource? _drvCts;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Thickness HeadMargin = new(8, 2, 8, 0);
    private List<DriverLine> _lines = new();
    private ICollectionView? _view;
    private string _query = "";
    private bool? _narrowBody;

    internal void InitDriver()
    {
        if (_drvInit) return;
        _drvInit = true;
        // Primeira leitura: esqueleto nos painéis, cada um sai quando a parte dele chega
        Skeleton.Set(true, DrvGpuCard, DrvBoardCard, DrvTableCard, DrvBackupCard);
        BtnDrvScan_Click(this, new RoutedEventArgs());
    }

    /// <summary>Abaixo de 1180 px a tabela desce para baixo da coluna da esquerda (com altura própria) e a página rola.</summary>
    private void DrvBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = DrvBody.ActualWidth < 1180;
        if (narrow == _narrowBody) return;
        _narrowBody = narrow;
        // Estreita, a tabela desce para baixo da coluna e a página cresce e rola; larga, a página tem a altura da janela
        if (narrow) DrvRoot.ClearValue(HeightProperty);
        else DrvRoot.SetBinding(HeightProperty, new Binding(nameof(ScrollViewer.ViewportHeight)) { Source = DrvScroll });
        DrvSideColumn.Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(520);
        DrvGap.Width = new GridLength(narrow ? 0 : 16);
        Grid.SetColumnSpan(DrvSide, narrow ? 3 : 1);
        Grid.SetRow(DrvTableCard, narrow ? 1 : 0);
        Grid.SetColumn(DrvTableCard, narrow ? 0 : 2);
        Grid.SetColumnSpan(DrvTableCard, narrow ? 3 : 1);
        DrvTableCard.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        DrvTableCard.Height = narrow ? 600 : double.NaN;
    }

    private void DrvListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        => ToolPage.AlignHead(DrvListScroll, DrvHead, HeadMargin);

    internal void DrvSet(string text, double? frac = null, string detail = "")
    {
        if (_context.IsClosing) return;
        ToolPage.Say(DrvStatus, DrvDetail, text, detail);
        if (frac.HasValue) ToolPage.SetProgress(DrvProgressFill, frac.Value);
    }

    private void DrvFail(string text, string detail)
    {
        if (_context.IsClosing) return;
        ToolPage.Say(DrvStatus, DrvDetail, text, detail);
        ToolPage.EndProgress(DrvProgressFill);
    }

    internal void DrvBusy(bool busy)
    {
        _drvBusy = busy;
        BtnDrvScan.IsEnabled = !busy;
        BtnDrvBackup.IsEnabled = !busy;
        BtnDrvRestore.IsEnabled = !busy;
        BtnDrvWuSearch.IsEnabled = !busy;
        DrvStudio.IsEnabled = !busy;
        BtnDrvInstall.IsEnabled = !busy && _drvGpus.Any(g => g.CanAutoInstall);
        BtnDrvWuInstall.IsEnabled = !busy && _drvWu.Count > 0;
    }

    internal async void BtnDrvScan_Click(object sender, RoutedEventArgs e)
    {
        if (_drvBusy) return;
        DrvBusy(true);
        _drvCts = CancellationTokenSource.CreateLinkedTokenSource(_context.Lifetime);
        try
        {
            // Uma mensagem só durante a leitura inteira, sem medidor (é rápida): o estado passa direto a "Lido às"
            DrvSet("Lendo drivers instalados…");
            var inv = await Task.Run(DriverInventory.ListInstalled);
            var problems = await Task.Run(DriverInventory.ReadProblems);
            ShowInventory(inv);
            ShowProblems(problems);
            Skeleton.SetActive(DrvTableCard, false);
            _drvBoardInfo = await Task.Run(DriverInventory.ReadBoard);
            ShowBoard();
            Skeleton.SetActive(DrvBoardCard, false);
            string? folder = _context.Settings.DriverBackupFolder;
            ShowBackup(folder is { Length: > 0 } ? await Task.Run(() => DriverBackup.Find(folder)) : null);
            Skeleton.SetActive(DrvBackupCard, false);

            _drvGpus = await Task.Run(DriverInventory.ReadGpus);
            bool studio = DrvStudio.IsChecked == true;
            foreach (var g in _drvGpus) await GpuDriverLookup.CheckAsync(g, studio, _drvCts.Token);
            if (_context.IsClosing) return;
            ShowGpus();
            DrvSet($"Lido às {DateTime.Now:HH:mm}");
        }
        catch (Exception ex) { DrvFail("Não foi possível ler os drivers", ex.Message); }
        finally
        {
            _drvCts?.Dispose();
            _drvCts = null;
            if (!_context.IsClosing) DrvBusy(false);
            Skeleton.Set(false, DrvGpuCard, DrvBoardCard, DrvTableCard, DrvBackupCard);
        }
    }

    private void DrvStudio_Click(object sender, RoutedEventArgs e)
    {
        // A escolha muda qual driver a NVIDIA devolve: consulta de novo na hora, em vez de esperar outro clique.
        if (!_drvBusy) BtnDrvScan_Click(sender, e);
    }

    private void ShowGpus()
    {
        DrvGpus.ItemsSource = null;
        DrvGpus.ItemsSource = _drvGpus;
        DrvGpuEmpty.Visibility = _drvGpus.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool nvidia = _drvGpus.Any(g => g.Vendor == "NVIDIA"), install = _drvGpus.Any(g => g.Vendor is "NVIDIA" or "AMD");
        DrvStudio.Visibility = nvidia ? Visibility.Visible : Visibility.Collapsed;
        BtnDrvInstall.Visibility = install ? Visibility.Visible : Visibility.Collapsed;
        DrvGpuActions.Visibility = install || nvidia ? Visibility.Visible : Visibility.Collapsed;
        // Só há página oficial quando o fabricante é conhecido (NVIDIA, AMD, Intel); sem ela, o atalho não aparece.
        BtnDrvGpuPage.Visibility = _drvGpus.Any(g => g.OfficialPage.Length > 0) ? Visibility.Visible : Visibility.Collapsed;

        // O botão diz a versão que vai instalar. Sem driver mais novo, reinstalar não é a ação principal da tela.
        var target = _drvGpus.FirstOrDefault(x => x.CanAutoInstall);
        BtnDrvInstall.Content = target == null ? "Baixar e instalar"
            : target.Vendor == "NVIDIA" && !target.UpdateAvailable ? $"Reinstalar {target.Latest}"
            : $"Baixar e instalar {target.Latest}";
        UpdatePrimary();
    }

    /// <summary>
    /// Uma ação principal por tela: o driver novo da placa de vídeo, quando há; senão, instalar o que o Windows Update achou.
    /// Sem nenhum dos dois, nada grita: tudo fica secundário.
    /// </summary>
    private void UpdatePrimary()
    {
        var target = _drvGpus.FirstOrDefault(x => x.CanAutoInstall);
        bool gpu = target != null && (target.UpdateAvailable || target.Vendor == "AMD");
        bool wuInstall = !gpu && _drvWu.Count > 0;
        Emphasize(BtnDrvInstall, gpu);
        Emphasize(BtnDrvWuInstall, wuInstall);
    }

    private void Emphasize(Button button, bool primary)
    {
        if (TryFindResource(primary ? "PrimaryButton" : "SecondaryButton") is Style style && button.Style != style) button.Style = style;
    }

    internal void ShowBoard()
    {
        var b = _drvBoardInfo;
        DrvBoard.Text = b.Model;
        DrvBoard.ToolTip = b.Model.Length > 0 ? b.Model : null;
        DrvBoardMaker.Text = b.Maker.Equals(b.Model, StringComparison.OrdinalIgnoreCase) ? "" : b.Maker;
        DrvBoardMaker.Visibility = DrvBoardMaker.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        string version = b.BiosVersion.Trim();
        string date = b.BiosDate == "—" ? "" : b.BiosDate.Trim();
        DrvBoardBios.Text = version;
        DrvBoardBiosDate.Text = date.Length > 0 ? "de " + date : "";
        DrvBiosBox.Visibility = version.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool known = b.SupportUrl.Length > 0;
        BtnDrvBoardPage.Content = b.Brand.Length > 0 ? $"Suporte da {b.Brand}" : "Suporte do fabricante";
        BtnDrvBoardPage.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
        DrvBoardNote.Visibility = known || b.Model.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowInventory(List<DriverRow> inventory)
    {
        var lines = inventory.Select(DriverLine.From).OrderBy(l => l.Order).ThenBy(l => l.Device, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (_view != null && lines.SequenceEqual(_lines)) { UpdateTableState(); return; } // releitura igual: a tabela fica
        _lines = lines;
        _view = CollectionViewSource.GetDefaultView(_lines);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DriverLine.Group)));
        _view.Filter = o => o is DriverLine l && (_query.Length == 0 || l.Device.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                                                                  || l.Vendor.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                                                                  || l.Group.Contains(_query, StringComparison.CurrentCultureIgnoreCase));
        DrvList.ItemsSource = _view;
        UpdateTableState();
    }

    /// <summary>Uma linha por dispositivo com problema; sem nenhum, a linha verde de que todos funcionam.</summary>
    private void ShowProblems(List<DeviceProblem> problems)
    {
        var rows = problems.Select((p, i) => new ProblemLine(p.Name, p.Meaning,
            $"{p.Meaning} (código {p.Code} no Gerenciador de Dispositivos)." + (p.Code == 28 ? " O Windows Update ou o site do fabricante costumam ter o driver." : ""), i == 0)).ToList();
        Ui.SetItems(DrvProblems, rows);
        DrvHealthy.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnDrvDeviceManager_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "mmc.exe")) { ArgumentList = { "devmgmt.msc" }, UseShellExecute = false })?.Dispose(); }
        catch (Exception ex) { DrvSet("Não foi possível abrir o Gerenciador de Dispositivos", null, ex.Message); }
    }

    private void DrvSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _query = DrvSearch.Text.Trim();
        _view?.Refresh();
        UpdateTableState();
    }

    private void UpdateTableState()
    {
        int shown = _view?.Cast<object>().Count() ?? 0;
        DrvCount.Text = _lines.Count == 0 ? "" : shown == _lines.Count ? _lines.Count.ToString(PtBr) : $"{shown} de {_lines.Count}";
        DrvEmpty.Text = _lines.Count == 0 ? "Nenhum driver encontrado." : "Nenhum driver com esse nome, fabricante ou tipo.";
        DrvEmpty.Visibility = shown == 0 && _view != null ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void BtnDrvGpuPage_Click(object sender, RoutedEventArgs e)
    {
        var g = _drvGpus.FirstOrDefault(x => x.OfficialPage.Length > 0 && x.Vendor != "Intel") ?? _drvGpus.FirstOrDefault(x => x.OfficialPage.Length > 0);
        ExternalLinks.OpenHttps(g?.OfficialPage ?? "https://www.nvidia.com/Download/index.aspx");
    }

    internal void BtnDrvBoardPage_Click(object sender, RoutedEventArgs e) => ExternalLinks.OpenHttps(_drvBoardInfo.SupportUrl);

    internal async void BtnDrvInstall_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin()) return;
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.DriverDownload);
        if (workload == null) { DrvSet("Termine a outra operação antes"); return; }

        var g = _drvGpus.FirstOrDefault(x => x.CanAutoInstall);
        if (g == null || _drvBusy) return;
        string msg = g.Vendor == "AMD"
            ? $"Baixar e abrir o instalador AMD Software {g.Latest}?\nO arquivo vem do servidor oficial da AMD."
            : g.UpdateAvailable
            ? $"Instalar o driver NVIDIA {g.Latest} ({g.SizeText})?\nO CoreBi baixa e abre o instalador oficial."
            : $"Você já está com a versão {g.Installed}. Baixar e abrir o instalador {g.Latest} de novo, para reinstalar?";
        if (MessageBox.Show(_context.Owner, msg, "Drivers", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        DrvBusy(true);
        _drvCts = CancellationTokenSource.CreateLinkedTokenSource(_context.Lifetime);
        DrvSet("Baixando o instalador…", 0);
        try
        {
            string r = await DriverInstaller.DownloadAndLaunchAsync(g, (t, f) => Dispatcher.BeginInvoke(new Action(() =>
                DrvSet(t, f))), _drvCts.Token);
            DrvSet(r, 1);
        }
        catch (Exception ex) { DrvFail("Não foi possível instalar o driver", ex.Message); }
        finally { _drvCts?.Dispose(); _drvCts = null; if (!_context.IsClosing) DrvBusy(false); }
    }

    private void ShowWu(string? note = null)
    {
        DrvWuItems.ItemsSource = _drvWu.Select(w => new WuLine(w.Title,
            w.SizeBytes > 0 ? (w.SizeBytes / 1048576.0).ToString("0", PtBr) + " MB" : "")).ToList();
        DrvWuCount.Text = _drvWu.Count > 0 ? _drvWu.Count.ToString(PtBr) : "";
        BtnDrvWuInstall.Visibility = _drvWu.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnDrvWuInstall.Content = _drvWu.Count == 1 ? "Instalar 1" : $"Instalar {_drvWu.Count}";
        if (note != null) DrvWuList.Text = note;
        DrvWuList.Visibility = _drvWu.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        DrvWuScroll.Visibility = _drvWu.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePrimary();
    }

    internal async void BtnDrvWuSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_drvBusy) return;
        DrvBusy(true);
        // Sem etapas para contar, o medidor não abre: só o estado, que passa de "Procurando…" ao resultado
        DrvSet("Procurando no Windows Update…", null, "pode levar 1 minuto");
        try
        {
            _drvWu = await WindowsUpdateService.SearchAsync();
            ShowWu($"Nenhum driver novo para este PC. Procurado às {DateTime.Now:HH:mm}.");
            DrvSet(_drvWu.Count == 0 ? "Nenhum driver novo no Windows Update"
                 : _drvWu.Count == 1 ? "1 driver no Windows Update" : $"{_drvWu.Count} drivers no Windows Update");
        }
        catch (Exception ex)
        {
            _drvWu = new();
            ShowWu("Não foi possível procurar: " + ex.Message);
            DrvFail("Não foi possível procurar no Windows Update", ex.Message);
        }
        finally { _drvCts?.Dispose(); _drvCts = null; if (!_context.IsClosing) DrvBusy(false); }
    }

    internal async void BtnDrvWuInstall_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin()) return;
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.WindowsUpdate);
        if (workload == null) { DrvSet("Termine a outra operação antes"); return; }
        using var critical = _context.BeginCriticalOperation("Aguarde a instalação de drivers do Windows Update terminar.");

        if (_drvBusy || _drvWu.Count == 0) return;
        string what = _drvWu.Count == 1 ? "1 driver" : $"{_drvWu.Count} drivers";
        if (MessageBox.Show(_context.Owner, $"Instalar {what} do Windows Update? Pode ser preciso reiniciar.", "Drivers",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        DrvBusy(true);
        DrvSet("Baixando drivers do Windows Update…");
        try
        {
            string r = await WindowsUpdateService.InstallAsync(_drvWu, t => Dispatcher.BeginInvoke(new Action(() => DrvSet(t))));
            DrvSet(r);
            _drvWu = new();
            ShowWu(r);
        }
        catch (Exception ex) { DrvFail("Não foi possível instalar pelo Windows Update", ex.Message); }
        finally { _drvCts?.Dispose(); _drvCts = null; if (!_context.IsClosing) DrvBusy(false); }
    }

    // ---------- Backup e restauração ----------

    private static string Size(long bytes) => bytes >= 1L << 30
        ? (bytes / (double)(1L << 30)).ToString("0.0", PtBr) + " GB"
        : (bytes / 1048576.0).ToString("0", PtBr) + " MB";

    private static string When(DateTime at) =>
        (at.Date == DateTime.Today ? "hoje" : at.Date == DateTime.Today.AddDays(-1) ? "ontem" : at.ToString("dd/MM/yyyy", PtBr)) + $" às {at:HH:mm}";

    /// <summary>O último backup feito ou usado por aqui; sem nenhum, o que o backup faz e onde guardar.</summary>
    private void ShowBackup(DriverBackupInfo? info)
    {
        if (info == null)
        {
            DrvBackupLast.Text = "Nenhum backup feito por aqui.";
            DrvBackupLast.Foreground = (Brush)FindResource("Muted");
            DrvBackupWhere.Text = "Para formatar, guarde em outro disco ou num pen drive: a pasta Documentos some junto com o Windows.";
            DrvBackupWhere.ToolTip = null;
            BtnDrvRestore.Content = "Restaurar";
            return;
        }
        DrvBackupLast.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        DrvBackupLast.Text = $"Último backup {When(info.Created)}";
        string what = info.Count == 1 ? "1 driver" : $"{info.Count} drivers";
        DrvBackupWhere.Text = $"{what} · {Size(info.Bytes)} · {info.Folder}";
        DrvBackupWhere.ToolTip = info.Folder;
        BtnDrvRestore.Content = "Restaurar";
    }

    private string StartFolder()
    {
        string? last = _context.Settings.DriverBackupFolder;
        string? parent = last is { Length: > 0 } ? Path.GetDirectoryName(last) : null;
        return parent != null && Directory.Exists(parent) ? parent : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    internal async void BtnDrvBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin()) return;
        if (_drvBusy) return;
        var pick = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Onde guardar o backup dos drivers (o CoreBi cria uma pasta nova dentro)",
            InitialDirectory = StartFolder(),
        };
        if (pick.ShowDialog(_context.Owner) != true) return;
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.DriverBackup);
        if (workload == null) { DrvSet("Termine a outra operação antes"); return; }

        DrvBusy(true);
        _drvCts = CancellationTokenSource.CreateLinkedTokenSource(_context.Lifetime);
        DrvSet("Copiando os drivers…", 0);
        try
        {
            // O progresso conta os pacotes que o Windows já copiou contra o total, a cada 400 ms
            var info = await DriverBackup.ExportAsync(pick.FolderName, (done, total) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (total > 0) DrvSet($"Copiando os drivers… {Math.Min(done, total)} de {total}", Math.Min(1, (double)done / total));
            })), _drvCts.Token);
            _context.Settings.DriverBackupFolder = info.Folder;
            _context.SaveSettings();
            ShowBackup(info);
            DrvSet($"Backup salvo: {info.Count} drivers", 1, Size(info.Bytes));
        }
        catch (Exception ex) { DrvFail("Não foi possível fazer o backup", ex.Message); }
        finally { _drvCts?.Dispose(); _drvCts = null; if (!_context.IsClosing) DrvBusy(false); }
    }

    /// <summary>
    /// Restaurar: escolher a pasta de um backup (ou a pasta que guarda vários, e vale o mais novo), confirmar e devolver cada driver
    /// ao Windows, que instala nos dispositivos em que ele é o melhor. Antes, um ponto de restauração.
    /// </summary>
    internal async void BtnDrvRestore_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin()) return;
        if (_drvBusy) return;
        string? last = _context.Settings.DriverBackupFolder;
        var pick = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Escolha a pasta do backup de drivers",
            InitialDirectory = last is { Length: > 0 } && Directory.Exists(last) ? last : StartFolder(),
        };
        if (pick.ShowDialog(_context.Owner) != true) return;
        var info = await Task.Run(() => DriverBackup.Find(pick.FolderName));
        if (info == null)
        {
            MessageBox.Show(_context.Owner, "Essa pasta não tem drivers de um backup. Escolha a pasta criada pelo \"Fazer backup\" (com uma subpasta por driver).",
                "Drivers", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string from = info.Computer.Length > 0 ? $" de {info.Computer}" : "";
        string msg = $"Restaurar {info.Count} drivers do backup{from} de {When(info.Created)}?\n\n" +
                     "O Windows instala cada um nos dispositivos que precisam dele. Um driver mais novo que já esteja instalado não é trocado. " +
                     "Leva alguns minutos e a tela pode piscar quando chegar a vez do driver de vídeo. Antes, o CoreBi cria um ponto de restauração.";
        if (MessageBox.Show(_context.Owner, msg, "Drivers", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.DriverBackup);
        if (workload == null) { DrvSet("Termine a outra operação antes"); return; }
        using var critical = _context.BeginCriticalOperation("Aguarde a restauração dos drivers terminar.");

        DrvBusy(true);
        _drvCts = CancellationTokenSource.CreateLinkedTokenSource(_context.Lifetime);
        try
        {
            DrvSet("Criando ponto de restauração…", 0);
            string point = await DriverBackup.RestorePointAsync(_drvCts.Token);
            var result = await DriverBackup.RestoreAsync(info, (done, total, name) => Dispatcher.BeginInvoke(new Action(() =>
                DrvSet($"Restaurando drivers… {Math.Min(done + 1, total)} de {total}", total > 0 ? (double)done / total : 0, name))), _drvCts.Token);
            _context.Settings.DriverBackupFolder = info.Folder;
            _context.SaveSettings();
            ShowBackup(info);
            string summary = result.Installed == 0 ? "Nenhum driver precisou ser instalado"
                : result.Installed == 1 ? "1 driver instalado" : $"{result.Installed} drivers instalados";
            string detail = (result.Current > 0 ? $"{result.Current} já em dia. " : "") + (result.Reboot ? "Reinicie o PC para terminar. " : "") + point;
            if (result.Failed.Count > 0)
            {
                DrvFail($"{summary}, {result.Failed.Count} com erro", "Com erro:\n" + string.Join("\n", result.Failed));
            }
            else DrvSet(summary, 1, detail.Trim());
            // Os drivers mudaram: a tabela e os dispositivos com problema relidos em silêncio
            ShowInventory(await Task.Run(DriverInventory.ListInstalled));
            ShowProblems(await Task.Run(DriverInventory.ReadProblems));
        }
        catch (Exception ex) { DrvFail("Não foi possível restaurar os drivers", ex.Message); }
        finally { _drvCts?.Dispose(); _drvCts = null; if (!_context.IsClosing) DrvBusy(false); }
    }
}

/// <summary>Linha da tabela de drivers: os dados do inventário e a classe do Windows com nome em português e ordem de leitura.</summary>
public sealed record DriverLine(string Device, string Vendor, string Version, string Date, bool Signed, string Group, int Order)
{
    /// <summary>
    /// Nomes genéricos que o Windows em inglês dá ao fabricante dos drivers que vêm com ele; o Windows em português mostra
    /// estes. Só a exibição muda: o inventário continua com o texto original.
    /// </summary>
    private static readonly Dictionary<string, string> StandardVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["(Standard system devices)"] = "(Dispositivos padrão do sistema)",
        ["(Standard display types)"] = "(Tipos de vídeo padrão)",
        ["(Standard disk drives)"] = "(Unidades de disco padrão)",
        ["(Standard keyboards)"] = "(Teclados padrão)",
        ["(Standard monitor types)"] = "(Tipos de monitor padrão)",
        ["(Standard USB Host Controller)"] = "(Controlador host USB padrão)",
        ["(Standard USB HUBs)"] = "(Hubs USB padrão)",
    };

    /// <summary>21/06/2006 é a data fixa que a Microsoft põe nos drivers que vêm com o Windows, não a idade do driver.</summary>
    private const string InboxDate = "21/06/2006";

    /// <summary>Dica da data: só nos drivers com a data padrão do Windows.</summary>
    public string? DateTip => Date == InboxDate ? "Data padrão dos drivers que vêm com o Windows. A versão diz qual é o driver." : null;

    internal static DriverLine From(DriverRow r)
    {
        var (group, order) = r.Class switch
        {
            "DISPLAY" => ("Vídeo", 0),
            "MEDIA" => ("Som", 1),
            "NET" => ("Rede", 2),
            "BLUETOOTH" => ("Bluetooth", 3),
            "SCSIADAPTER" => ("Controladoras de armazenamento", 4),
            "HDC" => ("Controladoras de disco", 5),
            "USB" => ("USB", 6),
            "SYSTEM" => ("Sistema", 7),
            _ => (r.Class.Length > 0 ? r.Class : "Outros", 8),
        };
        // Versão ou data ausente chega como "—": a célula fica vazia em vez de mostrar o travessão.
        static string Value(string v) => v == "—" ? "" : v;
        string vendor = StandardVendors.TryGetValue(r.Vendor.Trim(), out var pt) ? pt : r.Vendor;
        return new DriverLine(r.Device, vendor, Value(r.Version), Value(r.Date), r.Signed, group, order);
    }
}

/// <summary>Dispositivo com problema, pronto para a linha (a primeira leva o atalho do Gerenciador de Dispositivos).</summary>
public sealed record ProblemLine(string Name, string Meaning, string Tip, bool First);

/// <summary>Driver encontrado no Windows Update, com o tamanho já formatado.</summary>
public sealed record WuLine(string Title, string Size);
