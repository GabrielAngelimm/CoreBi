using CoreBi.Engine.Tools.Drivers;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using CoreBi.Desktop.Services;

using System.Windows;

namespace CoreBi.Desktop.Views;

/// <summary>Aba Drivers: inventário, NVIDIA (verifica e instala), AMD/placa-mãe (página oficial), Windows Update e backup.</summary>
public partial class DriversView : UserControl
{
    internal FeatureContext Context { get; set; } = null!;
    public DriversView() => InitializeComponent();
    private bool RequireAdmin() => PermissionGuard.RequireAdmin(Context.Owner);

    internal bool _drvInit, _drvBusy;
    internal List<GpuDriverStatus> _drvGpus = new();
    internal BoardInfo _drvBoardInfo = new();
    internal List<WuDriver> _drvWu = new();
    internal CancellationTokenSource? _drvCts;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Thickness HeadMargin = new(8, 2, 8, 0);
    private List<DriverLine> _lines = new();
    private ICollectionView? _view;
    private string? _group;      // classe escolhida no filtro (null = todas)
    private string _query = "";
    private bool? _narrowHeader;
    private bool? _narrowBody;

    internal void InitDriver()
    {
        if (_drvInit) return;
        _drvInit = true;
        BtnDrvScan_Click(this, new RoutedEventArgs());
    }

    // ------------------------------------------------------------------ layout

    private void DrvHeader_SizeChanged(object sender, SizeChangedEventArgs e)
        => _narrowHeader = ToolPage.PlaceConsole(DrvHeader, DrvConsole, _narrowHeader);

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

    // ------------------------------------------------------------------ console

    /// <summary>Estado da tarefa no console. A barra só muda quando a tarefa informa uma fração de trabalho feito.</summary>
    internal void DrvSet(string text, double? frac = null, string detail = "")
    {
        if (Context.IsClosing) return;
        ToolPage.Say(DrvStatus, DrvDetail, text, detail);
        if (frac.HasValue) ToolPage.SetProgress(DrvProgressFill, frac.Value);
    }

    /// <summary>A tarefa parou com erro: o console diz o quê e o trilho some (parado, o console não mostra progresso).</summary>
    private void DrvFail(string text, string detail)
    {
        if (Context.IsClosing) return;
        ToolPage.Say(DrvStatus, DrvDetail, text, detail);
        ToolPage.EndProgress(DrvProgressFill);
    }

    internal void DrvBusy(bool busy)
    {
        _drvBusy = busy;
        BtnDrvScan.IsEnabled = !busy;
        BtnDrvBackup.IsEnabled = !busy;
        BtnDrvWuSearch.IsEnabled = !busy;
        DrvStudio.IsEnabled = !busy;
        BtnDrvInstall.IsEnabled = !busy && _drvGpus.Any(g => g.CanAutoInstall);
        BtnDrvWuInstall.IsEnabled = !busy && _drvWu.Count > 0;
    }

    // ------------------------------------------------------------------ verificação

    internal async void BtnDrvScan_Click(object sender, RoutedEventArgs e)
    {
        if (_drvBusy) return;
        DrvBusy(true);
        _drvCts = CancellationTokenSource.CreateLinkedTokenSource(Context.Lifetime);
        try
        {
            DrvSet("Lendo drivers instalados…", 0);
            var inv = await Task.Run(DriverManager.Inventory);
            ShowInventory(inv);
            DrvSet("Lendo placa de vídeo e placa-mãe…", 0.2);
            _drvGpus = await Task.Run(DriverManager.ReadGpus);
            _drvBoardInfo = await Task.Run(DriverManager.ReadBoard);
            ShowBoard();
            DrvSet("Lendo placa de vídeo e placa-mãe…", 0.4);

            bool studio = DrvStudio.IsChecked == true;
            int i = 0;
            foreach (var g in _drvGpus)
            {
                double at = 0.4 + 0.6 * i / Math.Max(1, _drvGpus.Count);
                if (g.Vendor is "NVIDIA" or "AMD") DrvSet($"Consultando a {g.Vendor}…", at, g.GpuName);
                if (g.Vendor == "NVIDIA") await DriverManager.CheckNvidiaAsync(g, studio, _drvCts.Token);
                else if (g.Vendor == "AMD") await DriverManager.CheckAmdAsync(g, _drvCts.Token);
                else if (g.Vendor == "Intel") g.Message = "A Intel não tem consulta automática. Veja a versão mais recente na página oficial.";
                else g.Message = "Sem consulta automática para esta placa: o driver dela chega pelo Windows Update.";
                i++;
            }
            if (Context.IsClosing) return;
            ShowGpus();
            DrvSet($"Lido às {DateTime.Now:HH:mm}", 1);
        }
        catch (Exception ex) { DrvFail("Não foi possível ler os drivers", ex.Message); }
        finally { _drvCts?.Dispose(); _drvCts = null; if (!Context.IsClosing) DrvBusy(false); }
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
        DrvStudio.Visibility = _drvGpus.Any(g => g.Vendor == "NVIDIA") ? Visibility.Visible : Visibility.Collapsed;
        BtnDrvInstall.Visibility = _drvGpus.Any(g => g.Vendor is "NVIDIA" or "AMD") ? Visibility.Visible : Visibility.Collapsed;
        // Só há página oficial quando o fabricante é conhecido (NVIDIA, AMD, Intel); sem ela, o botão não aparece.
        BtnDrvGpuPage.Visibility = _drvGpus.Any(g => g.OfficialPage.Length > 0) ? Visibility.Visible : Visibility.Collapsed;

        // O botão diz a versão que vai instalar. Sem driver mais novo, reinstalar não é a ação principal da tela.
        var target = _drvGpus.FirstOrDefault(x => x.CanAutoInstall);
        BtnDrvInstall.Content = target == null ? "Baixar e instalar"
            : target.Vendor == "NVIDIA" && !target.UpdateAvailable ? $"Reinstalar {target.Latest}"
            : $"Baixar e instalar {target.Latest}";
        UpdatePrimary();
    }

    /// <summary>
    /// Uma ação principal por tela: o driver novo da placa de vídeo, quando há; senão, instalar o que o Windows Update achou;
    /// senão, procurar no Windows Update. As outras ficam secundárias.
    /// </summary>
    private void UpdatePrimary()
    {
        var target = _drvGpus.FirstOrDefault(x => x.CanAutoInstall);
        bool gpu = target != null && (target.UpdateAvailable || target.Vendor == "AMD");
        bool wuInstall = !gpu && _drvWu.Count > 0;
        Emphasize(BtnDrvInstall, gpu);
        Emphasize(BtnDrvWuInstall, wuInstall);
        Emphasize(BtnDrvWuSearch, !gpu && !wuInstall);
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

        // O motor entrega "versão (data)"; a versão vira a leitura e a data fica embaixo. Sem versão, o bloco some.
        var bios = Regex.Match(b.Bios.Trim(), @"^(?<v>.*?)\s*\((?<d>[^()]*)\)$");
        string version = bios.Success ? bios.Groups["v"].Value.Trim() : b.Bios.Trim();
        string date = bios.Success ? bios.Groups["d"].Value.Trim() : "";
        if (date == "—") date = "";
        DrvBoardBios.Text = version;
        DrvBoardBiosDate.Text = date;
        DrvBoardBiosDate.Visibility = date.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DrvBiosBox.Visibility = version.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool known = b.SupportUrl.Length > 0;
        BtnDrvBoardPage.Content = b.Brand.Length > 0 ? $"Suporte da {b.Brand}" : "Suporte do fabricante";
        BtnDrvBoardPage.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
        BtnDrvBoardPage.IsEnabled = known;
        DrvBoardNote.Visibility = known || b.Model.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------------ tabela de drivers instalados

    private void ShowInventory(List<DriverRow> inventory)
    {
        _lines = inventory.Select(DriverLine.From).OrderBy(l => l.Order).ThenBy(l => l.Device, StringComparer.CurrentCultureIgnoreCase).ToList();
        _view = CollectionViewSource.GetDefaultView(_lines);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DriverLine.Group)));
        _view.Filter = o => o is DriverLine l && (_group == null || l.Group == _group)
                            && (_query.Length == 0 || l.Device.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                                                   || l.Vendor.Contains(_query, StringComparison.CurrentCultureIgnoreCase));
        DrvList.ItemsSource = _view;
        if (_group != null && _lines.All(l => l.Group != _group)) _group = null;
        BuildChips();
        UpdateTableState();
    }

    /// <summary>Um filtro por classe, com a quantidade; "Todos" fica separado das classes.</summary>
    private void BuildChips()
    {
        DrvChips.Children.Clear();
        if (_lines.Count == 0) return;
        DrvChips.Children.Add(Chip("Todos", _lines.Count, null));
        foreach (var g in _lines.GroupBy(l => l.Group)) DrvChips.Children.Add(Chip(g.Key, g.Count(), g.Key));
    }

    private RadioButton Chip(string name, int count, string? group)
    {
        var text = new TextBlock { Typography = { NumeralAlignment = FontNumeralAlignment.Tabular } };
        text.Inlines.Add(new Run(name));
        var n = new Run("  " + count.ToString(PtBr));
        n.SetResourceReference(TextElement.ForegroundProperty, "Faint");
        text.Inlines.Add(n);
        var chip = new RadioButton { Content = text, GroupName = "DrvClass", IsChecked = _group == group };
        chip.SetResourceReference(StyleProperty, "Chip");
        chip.Checked += (_, _) => { _group = group; _view?.Refresh(); UpdateTableState(); };
        return chip;
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
        DrvEmpty.Text = _lines.Count == 0 ? "Nenhum driver encontrado." : "Nenhum driver com esse nome ou fabricante.";
        DrvEmpty.Visibility = shown == 0 && _view != null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ ações

    internal void BtnDrvGpuPage_Click(object sender, RoutedEventArgs e)
    {
        var g = _drvGpus.FirstOrDefault(x => x.OfficialPage.Length > 0 && x.Vendor != "Intel") ?? _drvGpus.FirstOrDefault(x => x.OfficialPage.Length > 0);
        ExternalLinks.OpenHttps(g?.OfficialPage ?? "https://www.nvidia.com/Download/index.aspx");
    }

    internal void BtnDrvBoardPage_Click(object sender, RoutedEventArgs e) => ExternalLinks.OpenHttps(_drvBoardInfo.SupportUrl);

    internal async void BtnDrvInstall_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin()) return;
        using var workload = Context.Workloads.TryBegin("Driver download");
        if (workload == null) { DrvSet("Termine a outra operação antes"); return; }

        var g = _drvGpus.FirstOrDefault(x => x.CanAutoInstall);
        if (g == null || _drvBusy) return;
        string msg = g.Vendor == "AMD"
            ? $"Baixar e abrir o instalador AMD Software {g.Latest}?\nO arquivo vem do servidor oficial da AMD."
            : g.UpdateAvailable
            ? $"Instalar o driver NVIDIA {g.Latest} ({g.SizeText})?\nO CoreBi baixa e abre o instalador oficial."
            : $"Você já está com a versão {g.Installed}. Baixar e abrir o instalador {g.Latest} de novo, para reinstalar?";
        if (MessageBox.Show(Context.Owner, msg, "Drivers", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        DrvBusy(true);
        _drvCts = CancellationTokenSource.CreateLinkedTokenSource(Context.Lifetime);
        DrvSet("Baixando o instalador…", 0);
        try
        {
            // A fração vem dos bytes baixados: é a única etapa com porcentagem real.
            string r = await DriverManager.DownloadAndLaunchAsync(g, (t, f) => Dispatcher.BeginInvoke(new Action(() =>
                DrvSet(t, f, f > 0 && f < 1 ? (f * 100).ToString("0", PtBr) + " %" : ""))), _drvCts.Token);
            DrvSet(r, 1);
        }
        catch (Exception ex) { DrvFail("Não foi possível instalar o driver", ex.Message); }
        finally { _drvCts?.Dispose(); _drvCts = null; if (!Context.IsClosing) DrvBusy(false); }
    }

    private void ShowWu()
    {
        DrvWuItems.ItemsSource = _drvWu.Select(w => new WuLine(w.Title,
            w.SizeBytes > 0 ? (w.SizeBytes / 1048576.0).ToString("0", PtBr) + " MB" : "")).ToList();
        DrvWuCount.Text = _drvWu.Count > 0 ? _drvWu.Count.ToString(PtBr) : "";
        BtnDrvWuInstall.Visibility = _drvWu.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnDrvWuInstall.Content = _drvWu.Count == 1 ? "Instalar 1 driver" : $"Instalar {_drvWu.Count} drivers";
        DrvWuEmpty.Visibility = _drvWu.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        DrvWuScroll.Visibility = _drvWu.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePrimary();
    }

    internal async void BtnDrvWuSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_drvBusy) return;
        DrvBusy(true);
        // Sem fração real durante a busca: a barra recomeça e só enche quando o Windows Update responde.
        DrvSet("Procurando no Windows Update…", 0, "pode levar 1 minuto");
        try
        {
            _drvWu = await DriverManager.SearchWindowsUpdateAsync();
            DrvWuList.Text = "Nenhum driver novo.";
            ShowWu();
            DrvSet(_drvWu.Count == 0 ? "Nenhum driver novo no Windows Update"
                 : _drvWu.Count == 1 ? "1 driver no Windows Update" : $"{_drvWu.Count} drivers no Windows Update", 1);
        }
        catch (Exception ex)
        {
            _drvWu = new();
            ShowWu();
            DrvWuList.Text = "Não foi possível procurar: " + ex.Message;
            DrvFail("Não foi possível procurar no Windows Update", ex.Message);
        }
        finally { _drvCts?.Dispose(); _drvCts = null; if (!Context.IsClosing) DrvBusy(false); }
    }

    internal async void BtnDrvWuInstall_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin()) return;
        using var workload = Context.Workloads.TryBegin("Windows Update");
        if (workload == null) { DrvSet("Termine a outra operação antes"); return; }
        using var critical = Context.BeginCriticalOperation();

        if (_drvBusy || _drvWu.Count == 0) return;
        string what = _drvWu.Count == 1 ? "1 driver" : $"{_drvWu.Count} drivers";
        if (MessageBox.Show(Context.Owner, $"Instalar {what} do Windows Update? Pode ser preciso reiniciar.", "Drivers",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        DrvBusy(true);
        DrvSet("Baixando drivers do Windows Update…", 0);
        try
        {
            string r = await DriverManager.InstallWindowsUpdateAsync(_drvWu, t => Dispatcher.BeginInvoke(new Action(() => DrvSet(t))));
            DrvSet(r, 1);
            _drvWu = new();
            ShowWu();
            DrvWuList.Text = r;
        }
        catch (Exception ex) { DrvFail("Não foi possível instalar pelo Windows Update", ex.Message); }
        finally { _drvCts?.Dispose(); _drvCts = null; if (!Context.IsClosing) DrvBusy(false); }
    }

    internal async void BtnDrvBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin()) return;
        using var workload = Context.Workloads.TryBegin("Driver backup");
        if (workload == null) { DrvSet("Termine a outra operação antes"); return; }

        if (_drvBusy) return;
        DrvBusy(true);
        _drvCts = CancellationTokenSource.CreateLinkedTokenSource(Context.Lifetime);
        DrvSet("Exportando drivers…", 0);
        try
        {
            string r = await DriverManager.BackupAsync(t => Dispatcher.BeginInvoke(new Action(() => DrvSet(t))), _drvCts.Token);
            DrvSet(r, 1);
        }
        catch (Exception ex) { DrvFail("Não foi possível fazer o backup", ex.Message); }
        finally { _drvCts?.Dispose(); _drvCts = null; if (!Context.IsClosing) DrvBusy(false); }
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

/// <summary>Driver encontrado no Windows Update, com o tamanho já formatado.</summary>
public sealed record WuLine(string Title, string Size);
