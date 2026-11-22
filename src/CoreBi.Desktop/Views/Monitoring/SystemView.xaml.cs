using CoreBi.Desktop.Features.Monitoring.Inventory;
using CoreBi.Desktop.Features.Reports;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring;
using CoreBi.Engine.Monitoring.Health;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Storage;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CoreBi.Desktop.Views;

/// <summary>
/// Sistema: a ficha técnica do computador. Seis botões de categoria em cima e, embaixo, a ficha da escolhida.
/// </summary>
public partial class SystemView : UserControl
{
    private FeatureContext _context = null!;
    private Func<IReadOnlyList<HealthCard>?> _networkResult = () => null;
    private SystemInventory? _inventory;
    private List<DriveCard> _drives = new();
    private Dictionary<string, Sheet> _sheets = new();
    private Sheet? _sheet;
    private string _selected = SystemSheets.Cpu;
    private string _signature = "";
    private UniformGrid? _indexGrid;
    private ColumnsPanel? _columns;

    public SystemView()
    {
        InitializeComponent();
        // Ao abrir, as fichas são refeitas (espaço livre, memória em uso) depois da entrada da página, sem engasgar o movimento;
        // antes da primeira leitura do sistema, o esqueleto ocupa o painel
        Skeleton.SetActive(SheetCard, true);
        IsVisibleChanged += (_, _) => { if (IsVisible && _inventory != null) PageMotion.AfterEntrance(this, Rebuild); };
    }

    internal void Initialize(FeatureContext context, Func<IReadOnlyList<HealthCard>?> networkResult)
    {
        _context = context;
        _networkResult = networkResult;
    }

    /// <summary>Árvore chave/valor das seis categorias, para o relatório.</summary>
    internal List<InfoNode> Nodes => _inventory?.ToNodes() ?? new();

    internal int CategoryCount => SysIndex.Items.Count;

    /// <summary>As fichas como estão na tela, para o relatório; null antes da primeira leitura.</summary>
    internal (List<IndexItem> Index, Dictionary<string, Sheet> Sheets)? Built { get; private set; }

    /// <summary>Lê tudo (WMI, registro e APIs do Windows) fora da thread da janela e monta as fichas.</summary>
    internal async Task RefreshAsync()
    {
        try
        {
            var inventory = Task.Run(SystemInfoCollector.Read);
            var drives = Task.Run(DriveMap.Read);
            _inventory = await inventory;
            _drives = await drives;
            if (_context == null || _context.IsClosing) return;
            _context.Drives = _drives;
            Rebuild();
        }
        catch (Exception ex)
        {
            HeaderMotion.Say(SystemSub, null, $"Não foi possível ler as informações do sistema: {ex.Message}");
            Skeleton.SetActive(SheetCard, false);
        }
    }

    /// <summary>Discos e unidades mudaram (pendrive, HD externo): relê só o armazenamento.</summary>
    internal async Task RefreshStorageAsync()
    {
        if (_inventory == null) return;
        try
        {
            var disks = Task.Run(SystemInfoCollector.ReadDisks);
            var drives = Task.Run(DriveMap.Read);
            var bitLocker = Task.Run(SystemInfoCollector.ReadBitLocker);
            _inventory.Disks = await disks;
            _inventory.BitLocker = await bitLocker;
            _drives = await drives;
            if (_context == null || _context.IsClosing) return;
            _context.Drives = _drives;
            Rebuild();
        }
        catch { }
    }

    /// <summary>As especificações das placas de vídeo (chip, arquitetura, VRAM exata) chegam depois do inventário.</summary>
    internal void RefreshHardware() { if (_inventory != null) Rebuild(); }

    /// <summary>Abre a ficha de uma categoria pelo nome ("Processador", "Memória RAM"...), como a Visão geral pede.</summary>
    internal void Select(string category)
    {
        if (SystemSheets.KeyOf(category) is not { } key) return;
        _selected = key;
        if (SysIndex.ItemsSource is IEnumerable<IndexItem> items && items.FirstOrDefault(i => i.Key == key) is { } item)
            SysIndex.SelectedItem = item;
    }

    private void Rebuild()
    {
        if (_inventory == null) return;
        var now = DateTime.Now;
        var (index, sheets) = SystemSheets.Build(_inventory, _context.Gpus, _drives, now, MemoryInfo.ReadSnapshot());
        Built = (index, sheets);
        // Ao voltar à página, a ficha refeita quase sempre é igual à da tela: trocar as abas e os grupos por cópias idênticas
        // fazia tudo piscar logo depois da entrada. Só troca o que mudou de verdade (espaço livre, memória em uso, uma placa nova)
        string signature = System.Text.Json.JsonSerializer.Serialize(SystemReport.From(index, sheets));
        if (signature == _signature && SysIndex.ItemsSource != null)
        {
            Skeleton.SetActive(SheetCard, false);
            return;
        }
        _signature = signature;
        _sheets = sheets;
        SysIndex.ItemsSource = index;
        SysIndex.SelectedItem = index.FirstOrDefault(i => i.Key == _selected) ?? index.FirstOrDefault();
        HeaderMotion.Say(SystemSub, null, $"{Environment.MachineName} · lido às {now:HH:mm}");
        Skeleton.SetActive(SheetCard, false);
    }

    private void SysIndex_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SysIndex.SelectedItem is not IndexItem item || !_sheets.TryGetValue(item.Key, out var sheet)) return;
        _selected = item.Key;
        Show(sheet);
    }

    private void Show(Sheet s)
    {
        _sheet = s;
        Sheet.Visibility = Visibility.Visible;
        SheetEyebrow.Text = s.Eyebrow;
        SheetEyebrow.Visibility = s.Eyebrow.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SheetTitle.Text = s.Title;
        SheetTitle.ToolTip = s.Title;
        SheetSub.Text = s.Subtitle;
        SheetSub.ToolTip = s.Subtitle;
        SheetSub.Visibility = s.Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SheetReadouts.ItemsSource = s.Readouts.Select((r, i) => r with { First = i == 0 }).ToList();

        SpecGroups.ItemsSource = s.Groups;
        SpecGroups.Visibility = s.Groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DiskList.ItemsSource = s.Disks;
        DiskList.Visibility = s.Disks != null ? Visibility.Visible : Visibility.Collapsed;
        SheetLink.Content = s.Link?.Text;
        SheetFooter.Visibility = s.Link != null ? Visibility.Visible : Visibility.Collapsed;
        SpecScroll.ScrollToTop();
        Layout(SheetCard.ActualWidth);
    }


    private void IndexGrid_Loaded(object sender, RoutedEventArgs e) { _indexGrid = (UniformGrid)sender; Layout(SheetCard.ActualWidth); }
    private void SpecColumns_Loaded(object sender, RoutedEventArgs e) { _columns = (ColumnsPanel)sender; Layout(SheetCard.ActualWidth); }

    private void SheetCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Cantos arredondados também para a faixa do índice.
        double r = Ui.PanelRadius(SheetCard);
        SheetCard.Clip = new RectangleGeometry(new Rect(e.NewSize), r, r);
        Layout(e.NewSize.Width);
    }

    /// <summary>Índice em uma fila de seis (ou 3 × 2 em janelas estreitas); grupos em 3, 2 ou 1 coluna conforme a largura.</summary>
    private void Layout(double width)
    {
        if (width <= 0) return;
        if (_indexGrid != null)
        {
            bool two = width < 960;
            _indexGrid.Rows = two ? 2 : 1;
            _indexGrid.Columns = two ? 3 : 6;
        }
        if (_columns != null)
        {
            double specs = width - 48;  // 24 de cada lado (SpecScroll)
            _columns.Columns = specs >= 1100 ? 3 : specs >= 620 ? 2 : 1;
        }
    }


    private DispatcherTimer? _copyTimer;
    private TextBlock? _copiedIcon;

    /// <summary>Clique numa linha copia o valor; o ícone vira um visto por um instante.</summary>
    private void SpecRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SpecRow row } host) return;
        try { Clipboard.SetText(row.Value); }
        catch { return; }
        if (FindTagged(host, "copy") is not TextBlock icon) return;
        if (_copiedIcon != null && _copiedIcon != icon)
        {
            _copiedIcon.Text = "\uE8C8";
            _copiedIcon.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        }
        _copyTimer?.Stop();
        _copiedIcon = icon;
        icon.Text = "\uE73E";
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        _copyTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _copyTimer.Tick -= CopyTimer_Tick;
        _copyTimer.Tick += CopyTimer_Tick;
        _copyTimer.Start();
    }

    private void CopyTimer_Tick(object? sender, EventArgs e)
    {
        _copyTimer?.Stop();
        if (_copiedIcon == null) return;
        _copiedIcon.Text = "\uE8C8";
        _copiedIcon.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        _copiedIcon = null;
    }

    private static DependencyObject? FindTagged(DependencyObject root, string tag)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Tag: string t } && t == tag) return child;
            if (FindTagged(child, tag) is { } found) return found;
        }
        return null;
    }

    private void SheetLink_Click(object sender, RoutedEventArgs e)
    {
        if (_sheet?.Link is { } link) _context.Navigate(link.Page);
    }

    private async void BtnReport_Click(object sender, RoutedEventArgs e) => await ExportReportAsync();

    internal async Task ExportReportAsync()
    {
        BtnReport.IsEnabled = false;
        try
        {
            string? path = await ReportExportService.ExportInteractiveAsync(
                _context, Built, Nodes, _networkResult(), status => HeaderMotion.Say(SystemSub, null, status));
            // O estado conta o andamento ("Gerando PDF…") e termina no nome do arquivo; o caminho inteiro fica na dica
            if (path != null)
            {
                SystemSub.ToolTip = path;
                HeaderMotion.Say(SystemSub, null, $"Relatório salvo: {System.IO.Path.GetFileName(path)}");
            }
        }
        catch (Exception ex)
        {
            HeaderMotion.Say(SystemSub, null, "O relatório não foi gerado");
            MessageBox.Show(_context.Owner, "Não foi possível gerar o relatório:\n" + ex.Message, "CoreBi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BtnReport.IsEnabled = true;
        }
    }
}
