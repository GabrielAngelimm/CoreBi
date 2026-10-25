using CoreBi.Desktop.Features.Monitoring.Inventory;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Storage;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CoreBi.Desktop.Views;

/// <summary>
/// Sistema: a ficha técnica do computador. Um índice com as seis categorias (cada célula já diz qual é o componente)
/// e, embaixo, a ficha do escolhido: placa com nome e quatro leituras, uma figura que explica o componente
/// (núcleos, slots, telas, inicialização segura, linha do tempo) e os dados em grupos. Tudo é lido uma vez ao abrir
/// o CoreBi; discos e unidades são relidos quando algo é conectado.
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
            _inventory.Disks = await disks;
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
        var (index, sheets) = SystemSheets.Build(_inventory, _gpus(), _drives, now);
        _sheets = sheets;
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
        SheetReadouts.ItemsSource = s.Readouts;

        bool figure = s.Figure != null;
        FigStage.Visibility = figure ? Visibility.Visible : Visibility.Collapsed;
        FigTitle.Text = s.FigureTitle ?? "";
        SheetFigure.Content = s.Figure;
        SheetNotes.ItemsSource = s.Notes;

        SpecGroups.ItemsSource = s.Groups;
        SpecGroups.Visibility = s.Groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DiskList.ItemsSource = s.Disks;
        DiskList.Visibility = s.Disks != null ? Visibility.Visible : Visibility.Collapsed;
        BuildTable(s.Table);
        SheetFootnote.Text = s.Footnote ?? "";
        SheetFootnote.Visibility = s.Footnote != null ? Visibility.Visible : Visibility.Collapsed;
        SheetLink.Content = s.Link?.Text;
        SheetLink.Visibility = s.Link != null ? Visibility.Visible : Visibility.Collapsed;
        SpecScroll.ScrollToTop();
        Layout(SheetCard.ActualWidth);
    }

    /// <summary>Tabela compacta (módulos de memória): colunas no tamanho do conteúdo, fio entre as linhas.</summary>
    private void BuildTable(SpecTable? table)
    {
        SpecTableGrid.Children.Clear();
        SpecTableGrid.ColumnDefinitions.Clear();
        SpecTableGrid.RowDefinitions.Clear();
        TablePanel.Visibility = table != null ? Visibility.Visible : Visibility.Collapsed;
        if (table == null) return;
        TableTitle.Text = table.Title;
        for (int c = 0; c < table.Header.Count; c++)
            SpecTableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == table.Header.Count - 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        void Cell(string text, int row, int col, bool header)
        {
            var t = new TextBlock { Text = text, Margin = new Thickness(0, header ? 8 : 9, 22, header ? 8 : 9), FontSize = header ? 12 : 13 };
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            if (header) { t.FontWeight = FontWeights.SemiBold; t.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); }
            else if (col == 0) t.FontWeight = FontWeights.SemiBold;
            else if (col == table.Header.Count - 1) t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            Grid.SetRow(t, row); Grid.SetColumn(t, col);
            SpecTableGrid.Children.Add(t);
        }

        void Rule(int row)
        {
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, SnapsToDevicePixels = true };
            line.SetResourceReference(Border.BackgroundProperty, "Line");
            Grid.SetRow(line, row); Grid.SetColumnSpan(line, table.Header.Count);
            SpecTableGrid.Children.Add(line);
        }

        SpecTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < table.Header.Count; c++) Cell(table.Header[c], 0, c, true);
        Rule(0);
        for (int r = 0; r < table.Rows.Count; r++)
        {
            SpecTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < table.Rows[r].Count && c < table.Header.Count; c++) Cell(table.Rows[r][c], r + 1, c, false);
            Rule(r + 1);
        }
    }

    // ---------------- Layout que acompanha a largura ----------------

    private void IndexGrid_Loaded(object sender, RoutedEventArgs e) { _indexGrid = (UniformGrid)sender; Layout(SheetCard.ActualWidth); }
    private void SpecColumns_Loaded(object sender, RoutedEventArgs e) { _columns = (ColumnsPanel)sender; Layout(SheetCard.ActualWidth); }

    private void SheetCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Cantos arredondados também para o fundo da célula escolhida e o palco da figura.
        SheetCard.Clip = new RectangleGeometry(new Rect(e.NewSize), 12, 12);
        Layout(e.NewSize.Width);
    }

    /// <summary>
    /// Índice em uma fila de seis (ou 3 × 2 em janelas estreitas); palco da figura mais estreito abaixo de 1300 px;
    /// grupos em 3, 2 ou 1 coluna conforme a largura que sobra.
    /// </summary>
    private void Layout(double width)
    {
        if (width <= 0) return;
        if (_indexGrid != null)
        {
            bool two = width < 1080;
            _indexGrid.Rows = two ? 2 : 1;
            _indexGrid.Columns = two ? 3 : 6;
        }
        bool figure = _sheet?.Figure != null;
        double fig = width >= 1300 ? 480 : 380;
        FigColumn.Width = new GridLength(figure ? fig : 0);
        if (_columns != null)
        {
            double specs = width - (figure ? fig : 0) - 64;
            _columns.Columns = specs >= 1000 && !figure ? 3 : specs >= 620 ? 2 : 1;
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
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
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
