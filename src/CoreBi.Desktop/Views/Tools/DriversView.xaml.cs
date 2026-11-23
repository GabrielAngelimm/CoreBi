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
        Skeleton.Set(true, DrvGpuCard, DrvBoardCard, DrvTableCard);
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
    private string? _group;
    private bool? _narrowBody;

    internal void InitDriver()
    {
        if (_drvInit) return;
        _drvInit = true;
        // Primeira leitura: esqueleto nos painéis, cada um sai quando a parte dele chega
        Skeleton.Set(true, DrvGpuCard, DrvBoardCard, DrvTableCard);
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
            Skeleton.Set(false, DrvGpuCard, DrvBoardCard, DrvTableCard);
        }
    }

    private void DrvStudio_Click(object sender, RoutedEventArgs e)
    {
        // A escolha muda qual driver a NVIDIA devolve: consulta de novo na hora, em vez de esperar outro clique.
        if (!_drvBusy) BtnDrvScan_Click(sender, e);
    }

    private void ShowGpus()
    {
        bool studioOn = DrvStudio.IsChecked == true;
        foreach (var g in _drvGpus) Verdict(g, studioOn);
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
        DrvGpuSize.Text = target == null ? "" : SizePt(target.SizeText);
        DrvGpuSize.ToolTip = DrvGpuSize.Text.Length > 0 ? "Tamanho do instalador." : null;
        UpdatePrimary();
    }

    /// <summary>
    /// O veredito no pé da placa. Com a NVIDIA dá para comparar as versões: "Em dia" em verde ou "Driver novo" em âmbar, e uma
    /// frase curta que diz a linha do driver (Game Ready ou Studio). Nos outros casos fica só a frase do motor, sem palavra da frente.
    /// </summary>
    internal static void Verdict(GpuDriverStatus g, bool studio)
    {
        g.Lead = ""; g.Tone = "";
        if (g.Vendor != "NVIDIA" || g.Latest.Length == 0 || g.Installed.Length == 0) return;
        string line = studio ? "Studio" : "Game Ready";
        if (g.UpdateAvailable) { g.Lead = "Driver novo"; g.Tone = "warn"; g.Message = $"Saiu um {line} mais novo."; }
        else { g.Lead = "Em dia"; g.Tone = "ok"; g.Message = $"É o {line} mais novo da NVIDIA."; }
    }

    /// <summary>"1.04 GB" da NVIDIA em português: "1,04 GB".</summary>
    internal static string SizePt(string size)
    {
        size = size.Trim();
        var m = System.Text.RegularExpressions.Regex.Match(size, @"^(\d+)\.(\d+)(\s*\w+)$");
        return m.Success ? $"{m.Groups[1].Value},{m.Groups[2].Value}{m.Groups[3].Value}" : size;
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
        bool known = b.SupportUrl.Length > 0;
        // Com o fabricante conhecido, o nome dele está no botão; a linha do fabricante só aparece quando não há botão
        DrvBoardMaker.Text = known || b.Maker.Equals(b.Model, StringComparison.OrdinalIgnoreCase) ? "" : b.Maker;
        DrvBoardMaker.Visibility = DrvBoardMaker.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        string version = b.BiosVersion.Trim();
        string date = b.BiosDate == "—" ? "" : b.BiosDate.Trim();
        DrvBoardBios.Text = version;
        DrvBoardBiosDate.Text = date.Length > 0 ? date : "—";
        // Pé da placa no mesmo desenho do da placa de vídeo: a idade na frente, em tinta, e o conselho em Muted
        string brandName = b.Brand.Length > 0 ? "a " + b.Brand : "o fabricante";
        DrvBoardAge.Inlines.Clear();
        if (DateTime.TryParseExact(date, "dd/MM/yyyy", PtBr, DateTimeStyles.None, out var when))
        {
            var lead = new System.Windows.Documents.Run(Age(when)) { FontWeight = FontWeights.SemiBold };
            lead.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Fg");
            DrvBoardAge.Inlines.Add(lead);
            var gap = new System.Windows.Documents.Run("  ·  ");
            gap.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Faint");
            DrvBoardAge.Inlines.Add(gap);
            DrvBoardAge.Inlines.Add(new System.Windows.Documents.Run($"Atualize só se {brandName} recomendar."));
        }
        var age = DrvBoardAge.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DrvBoardAge.Visibility = age;
        DrvBoardAgeHair.Visibility = age;
        DrvBiosBox.Visibility = version.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        string brand = b.Brand.Length > 0 ? b.Brand : "fabricante";
        BtnDrvBoardPage.Content = $"Abrir no site da {brand}";
        BtnDrvBoardPage.ToolTip = b.SupportIsModelPage
            ? "Abre a página de suporte do seu modelo: drivers de chipset, áudio e rede, e as versões da BIOS."
            : b.Brand == "Gigabyte"
            ? "Abre o site da Gigabyte já buscando o seu modelo. Ele tem uma página por revisão da placa: a sua está impressa nela, perto dos slots (ex.: Rev. 1.3)."
            : "Abre a central de suporte do fabricante. Busque pelo modelo para achar os drivers e a BIOS.";
        BtnDrvBoardPage.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
        DrvBoardNote.Visibility = known || b.Model.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>"Há 1 ano e 2 meses": a idade da BIOS, na frente do pé da placa.</summary>
    internal static string Age(DateTime when)
    {
        int months = Math.Max(0, (DateTime.Today.Year - when.Year) * 12 + DateTime.Today.Month - when.Month - (DateTime.Today.Day < when.Day ? 1 : 0));
        int years = months / 12, rest = months % 12;
        string y = years == 1 ? "1 ano" : $"{years} anos", m = rest == 1 ? "1 mês" : $"{rest} meses";
        return "Há " + (months == 0 ? "menos de 1 mês" : years == 0 ? m : rest == 0 ? y : $"{y} e {m}");
    }

    private void ShowInventory(List<DriverRow> inventory)
    {
        var lines = inventory.Select(DriverLine.From).OrderBy(l => l.Order).ThenBy(l => l.Device, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (_view != null && lines.SequenceEqual(_lines)) { UpdateTableState(); return; } // releitura igual: a tabela fica
        _lines = lines;
        _view = CollectionViewSource.GetDefaultView(_lines);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DriverLine.Group)));
        _view.Filter = o => o is DriverLine l && (_group == null || l.Group == _group)
                                              && (_query.Length == 0 || l.Device.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                                                                     || l.Vendor.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                                                                     || l.Group.Contains(_query, StringComparison.CurrentCultureIgnoreCase));
        DrvList.ItemsSource = _view;
        BuildGroups();
        UpdateTableState();
    }

    /// <summary>
    /// Os tipos de peça do inventário como segmentos (Todos primeiro, depois na ordem da tabela). O escolhido continua escolhido
    /// numa releitura, se o tipo ainda existir.
    /// </summary>
    private void BuildGroups()
    {
        var groups = _lines.Select(l => l.Group).Distinct().ToList();
        if (_group != null && !groups.Contains(_group)) _group = null;
        DrvGroups.Children.Clear();
        foreach (string? g in groups.Prepend(null))
        {
            var segment = new RadioButton
            {
                Content = g ?? "Todos",
                Tag = g,
                GroupName = "DrvGroup",
                IsChecked = g == _group,
                ToolTip = g == null ? "Todos os drivers." : $"Só os drivers de {g.ToLower(PtBr)}.",
            };
            segment.SetResourceReference(StyleProperty, "Segment");
            DrvGroups.Children.Add(segment);
        }
        DrvGroupBox.Visibility = groups.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _headWide = null;
        PlaceGroups();
    }

    private void DrvGroup_Checked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not RadioButton { IsLoaded: true } segment || !IsLoaded) return;
        _group = segment.Tag as string;
        _view?.Refresh();
        DrvListScroll.ScrollToTop();
        UpdateTableState();
    }

    private bool? _headWide;

    private void DrvTableHead_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) PlaceGroups();
    }

    /// <summary>
    /// Os tipos ficam entre o título e a busca quando cabem; senão descem para uma linha só deles, embaixo do título, alinhados à
    /// esquerda. A decisão compara a largura do cabeçalho com a soma do título, do trilho e da busca.
    /// </summary>
    private void PlaceGroups()
    {
        if (DrvGroupBox.Visibility != Visibility.Visible || DrvTableHead.ActualWidth <= 0) return;
        DrvGroupBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var title = (FrameworkElement)DrvTableHead.Children[0];
        title.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double need = title.DesiredSize.Width + DrvGroupBox.DesiredSize.Width + DrvSearch.Width + 16 + 12;
        bool wide = need <= DrvTableHead.ActualWidth;
        if (wide == _headWide) return;
        _headWide = wide;
        Grid.SetRow(DrvGroupBox, wide ? 0 : 1);
        Grid.SetColumn(DrvGroupBox, wide ? 1 : 0);
        Grid.SetColumnSpan(DrvGroupBox, wide ? 1 : 3);
        DrvGroupBox.Margin = wide ? new Thickness(16, 0, 0, 0) : new Thickness(0, 12, 0, 0);
    }

    /// <summary>Uma linha por dispositivo com problema; sem nenhum, a linha verde de que todos funcionam.</summary>
    private void ShowProblems(List<DeviceProblem> problems)
    {
        var rows = problems.Select((p, i) => new ProblemLine(p.Name, p.Meaning, ProblemTip(p), i == 0)).ToList();
        Ui.SetItems(DrvProblems, rows);
        DrvHealthy.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A dica da linha: o que o problema quer dizer na prática e o que fazer.</summary>
    internal static string ProblemTip(DeviceProblem p) => p.Code == 28
        ? "O Windows reconhece este aparelho, mas não tem o driver dele, então ele não funciona.\n" +
          "Procure no Windows Update (no cartão ao lado) ou no site do fabricante. Se for um modo que você não usa, como o de atualizar firmware, pode ignorar."
        : $"{p.Meaning}.\nÉ o ponto de exclamação do Gerenciador de Dispositivos (código {p.Code}). Lá dá para atualizar o driver ou ver detalhes.";

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
        DrvEmpty.Text = _lines.Count == 0 ? "Nenhum driver encontrado." : "Nenhum driver com esse nome ou fabricante neste tipo.";
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
        DrvWuList.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DrvWuList.Visibility = _drvWu.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        DrvWuScroll.Visibility = _drvWu.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePrimary();
    }

    internal async void BtnDrvWuSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_drvBusy) return;
        DrvBusy(true);
        // Sem etapas para contar, o medidor não abre: o estado diz "Procurando…" e, dentro do cartão, as linhas da lista pulsam
        // (o esqueleto) até a resposta chegar; o botão diz o que está fazendo
        DrvSet("Procurando no Windows Update…", null, "pode levar 1 minuto");
        Searching(true);
        try
        {
            _drvWu = await WindowsUpdateService.SearchAsync();
            ShowWu($"Tudo em dia: nenhum driver novo às {DateTime.Now:HH:mm}.");
            DrvSet(_drvWu.Count == 0 ? "Nenhum driver novo no Windows Update"
                 : _drvWu.Count == 1 ? "1 driver no Windows Update" : $"{_drvWu.Count} drivers no Windows Update");
        }
        catch (Exception ex)
        {
            _drvWu = new();
            ShowWu("Não deu para falar com o Windows Update agora.");
            DrvFail("Não foi possível procurar no Windows Update", ex.Message);
        }
        finally { Searching(false); _drvCts?.Dispose(); _drvCts = null; if (!_context.IsClosing) DrvBusy(false); }
    }

    /// <summary>Durante a busca: o esqueleto pulsa no lugar da lista e o botão vira "Procurando…".</summary>
    private void Searching(bool on)
    {
        BtnDrvWuSearch.Content = on ? "Procurando…" : "Procurar";
        DrvWuList.Visibility = on ? Visibility.Hidden : _drvWu.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        Skeleton.SetActive(DrvWuCard, on);
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

    private DriverBackupDialog? Dialog => (_context.Owner as MainWindow)?.DriverLayer;

    /// <summary>Onde salvar por padrão: ao lado do último backup; sem nenhum, Documentos\CoreBi-Drivers.</summary>
    private string BackupRoot()
    {
        string? last = _context.Settings.DriverBackupFolder;
        string? parent = last is { Length: > 0 } ? Path.GetDirectoryName(last) : null;
        return parent != null && Directory.Exists(parent) ? parent : DriverBackup.DefaultRoot;
    }

    internal async void BtnDrvBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin() || _drvBusy || Dialog is not { } dialog) return;
        string? lastFolder = _context.Settings.DriverBackupFolder;
        var count = DriverBackup.CountInstalledAsync(_context.Lifetime);
        var last = lastFolder is { Length: > 0 } ? await Task.Run(() => DriverBackup.Find(lastFolder)) : null;
        int total;
        try { total = await count; } catch { total = 0; }
        string? root = await dialog.AskBackupAsync(BackupRoot(), total, last);
        if (root == null) return;
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.DriverBackup);
        if (workload == null) { DrvSet("Termine a outra operação antes"); return; }

        DrvBusy(true);
        _drvCts = CancellationTokenSource.CreateLinkedTokenSource(_context.Lifetime);
        DrvSet("Copiando os drivers…", 0);
        try
        {
            Directory.CreateDirectory(root);
            // O progresso conta os pacotes que o Windows já copiou contra o total, a cada 400 ms
            var info = await DriverBackup.ExportAsync(root, (done, all) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (all > 0) DrvSet($"Copiando os drivers… {Math.Min(done, all)} de {all}", Math.Min(1, (double)done / all));
            })), _drvCts.Token);
            _context.Settings.DriverBackupFolder = info.Folder;
            _context.SaveSettings();
            DrvSet($"Backup salvo: {info.Count} drivers", 1, Size(info.Bytes));
            DrvDetail.ToolTip = info.Folder;
        }
        catch (Exception ex) { DrvFail("Não foi possível fazer o backup", ex.Message); }
        finally { _drvCts?.Dispose(); _drvCts = null; if (!_context.IsClosing) DrvBusy(false); }
    }

    /// <summary>
    /// Restaurar: a janela mostra o backup achado (o último usado, ou o que for escolhido ali) e só confirma com um backup de verdade;
    /// então um ponto de restauração e cada driver devolvido ao Windows, que instala nos dispositivos em que ele é o melhor.
    /// </summary>
    internal async void BtnDrvRestore_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin() || _drvBusy || Dialog is not { } dialog) return;
        string start = _context.Settings.DriverBackupFolder is { Length: > 0 } last && Directory.Exists(last) ? last
            : Directory.Exists(DriverBackup.DefaultRoot) ? DriverBackup.DefaultRoot : "";
        var info = await dialog.AskRestoreAsync(start);
        if (info == null) return;
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

    // ---------- Capturas dos testes ----------

    /// <summary>
    /// "PageDriver:demo": quatro drivers achados no Windows Update; "searching": a busca em andamento; "backup" e "restore": as
    /// janelas com dados de exemplo. Nada é procurado, copiado nem instalado; null volta ao normal.
    /// </summary>
    internal void PreviewDemo(string? mode)
    {
        if (mode == null)
        {
            Dialog?.Cancel();
            Searching(false);
            _drvWu = new();
            ShowWu("Drivers testados pela Microsoft para este PC.");
            return;
        }
        string folder = Path.Combine(DriverBackup.DefaultRoot, $"Drivers {Environment.MachineName} 2026-11-20 21h14");
        var sample = new DriverBackupInfo(folder, DateTime.Now.AddDays(-3).Date.AddHours(21).AddMinutes(14), Environment.MachineName, 69, 1_840_000_000);
        switch (mode)
        {
            case "demo":
                _drvWu = new()
                {
                    new WuDriver { Title = "Realtek Semiconductor Corp. - Extension - 10.0.26100.31", SizeBytes = 1_200_000 },
                    new WuDriver { Title = "Intel - Bluetooth - 23.100.1.1", SizeBytes = 18_400_000 },
                    new WuDriver { Title = "Advanced Micro Devices, Inc. - System - 5.24.0.0", SizeBytes = 2_600_000 },
                    new WuDriver { Title = "Logitech - HIDClass - 1.10.84.0", SizeBytes = 900_000 },
                    new WuDriver { Title = "GuangZhou FiiO Electronics Co.,Ltd - USB - 5.12.0.0", SizeBytes = 4_100_000 },
                };
                ShowWu();
                break;
            case "searching":
                BtnDrvWuSearch.Content = "Procurando…";
                DrvWuList.Visibility = Visibility.Hidden;
                Skeleton.SetActive(DrvWuCard, true);
                break;
            case "backup":
                Dialog?.Preview(false, DriverBackup.DefaultRoot, 69, sample);
                break;
            case "restore":
                Dialog?.Preview(true, folder, 69, sample);
                break;
        }
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
            "SCSIADAPTER" or "HDC" => ("Armazenamento", 4),
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
