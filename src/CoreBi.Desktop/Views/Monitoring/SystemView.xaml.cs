using CoreBi.Desktop.Features.Monitoring.Inventory;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring.Graphics;
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
/// Sistema: a ficha técnica do computador. Seis botões de categoria em cima e, embaixo, a ficha da escolhida: placa
/// com o nome da peça e quatro leituras, e os dados em grupos na largura toda, com os vereditos ao lado do valor. Tudo é lido uma vez ao abrir o CoreBi; discos e unidades são relidos quando algo é conectado, e o uso da
/// RAM a cada vez que a página aparece.
/// </summary>
public partial class SystemView : UserControl
{
    internal MainWindow? Owner { get; set; }

    private FeatureContext? _context;
    private Func<IReadOnlyList<GpuInfo>> _gpus = () => Array.Empty<GpuInfo>();
    private Action<string> _navigate = _ => { };
    private SystemInventory? _inventory;
    private List<DriveCard> _drives = new();
    private Dictionary<string, Sheet> _sheets = new();
    private Sheet? _sheet;
    private string _selected = SystemSheets.Cpu;
    private UniformGrid? _indexGrid;
    private ColumnsPanel? _columns;

    public SystemView()
    {
        InitializeComponent();
        // "Ligado há" e as idades ("há 3 meses") são calculados na hora em que a página aparece.
        IsVisibleChanged += (_, _) => { if (IsVisible && _inventory != null) Rebuild(); };
    }

    internal void Initialize(FeatureContext context, Func<IReadOnlyList<GpuInfo>> gpus, Action<string> navigate)
    {
        _context = context;
        _gpus = gpus;
        _navigate = navigate;
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
            if (_context?.IsClosing == true) return;
            Rebuild();
        }
        catch (Exception ex) { SystemSub.Text = $"Não foi possível ler as informações do sistema: {ex.Message}"; }
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
            if (_context?.IsClosing == true) return;
            Rebuild();
        }
        catch { }
    }

    /// <summary>As especificações das placas de vídeo (chip, arquitetura, VRAM exata) chegam depois do inventário.</summary>
    internal void GpusReady() { if (_inventory != null) Rebuild(); }

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
        var (index, sheets) = SystemSheets.Build(_inventory, _gpus(), _drives, now, MemoryInfo.ReadSnapshot());
        _sheets = sheets;
        Built = (index, sheets);
        SysIndex.ItemsSource = index;
        SysIndex.SelectedItem = index.FirstOrDefault(i => i.Key == _selected) ?? index.FirstOrDefault();
        SystemSub.Text = $"{Environment.MachineName} · lido às {now:HH:mm}";
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

    // ---------------- Layout que acompanha a largura ----------------

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
            double specs = width - 64;
            _columns.Columns = specs >= 1100 ? 3 : specs >= 620 ? 2 : 1;
        }
    }

    // ---------------- Ações ----------------

    /// <summary>Clique numa linha copia o valor; o ícone vira um visto por um instante.</summary>
    private void SpecRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SpecRow row } host) return;
        try { Clipboard.SetText(row.Value); }
        catch { return; }
        if (FindTagged(host, "copy") is not TextBlock icon) return;
        icon.Text = "";
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        reset.Tick += (_, _) =>
        {
            reset.Stop();
            icon.Text = "";
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
        };
        reset.Start();
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
        if (_sheet?.Link is { } link) _navigate(link.Page);
    }

    private void BtnReport_Click(object sender, RoutedEventArgs e) => Owner?.BtnReport_Click(sender, e);
}
