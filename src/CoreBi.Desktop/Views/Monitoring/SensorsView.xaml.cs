using CoreBi.Desktop.Features.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Sensors;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

/// <summary>
/// Monitor de hardware: componentes em cartões, sensores agrupados por tipo com atual, mínimo, média e máximo.
/// Os cartões só existem com a página à vista; escondida, ela solta as linhas e não gasta nada a cada leitura.
/// </summary>
public partial class SensorsView : UserControl
{
    public static readonly DependencyProperty SectionColumnsProperty =
        DependencyProperty.Register(nameof(SectionColumns), typeof(int), typeof(SensorsView), new PropertyMetadata(2));
    public int SectionColumns { get => (int)GetValue(SectionColumnsProperty); set => SetValue(SectionColumnsProperty, value); }

    private SensorMonitor? _monitor;
    private string? _group;           // filtro por tipo (null = todos)
    private string _search = "";      // filtro por texto
    private readonly HashSet<string> _collapsed = new();
    private int _builtRows = -1;
    private string _chipKeys = "";
    private DateTime _since = DateTime.Now;
    private List<SensorComponent> _components = new();
    private bool _syncingIndex;
    private DateTime _jumpedAt;

    public SensorsView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => { if (IsVisible) Rebuild(); else Release(); };
    }

    internal void Initialize(SensorMonitor monitor) => _monitor = monitor;

    /// <summary>Detecção do hardware concluída: monta filtros e cartões e começa a contar as estatísticas.</summary>
    internal void Populate()
    {
        _since = DateTime.Now;
        Tick();
        Rebuild();
    }

    /// <summary>A cada leitura (1 s): estado no subtítulo e sensores novos, se o hardware mudou.</summary>
    internal void Tick()
    {
        if (_monitor is not { IsOpen: true }) return;
        var live = _monitor.Rows.Where(r => r.HasReading).ToList();
        int components = live.Select(r => r.ComponentId).Distinct().Count();
        StatusText.Text = $"{live.Count} sensores em {components} componentes · atualizado às {DateTime.Now:HH:mm:ss}";
        if (IsVisible && live.Count != _builtRows) Rebuild();
    }

    private void Rebuild()
    {
        if (_monitor is not { IsOpen: true })
        {
            ShowEmpty("Detectando o hardware…", "Os sensores aparecem assim que a leitura do hardware terminar.");
            return;
        }
        if (!IsVisible) return;
        BuildChips();
        double offset = SensorScroll.VerticalOffset;
        _components = SensorBoard.Build(_monitor.Rows.ToList(), _group, _search, _collapsed);
        _builtRows = _monitor.Rows.Count(r => r.HasReading);
        _syncingIndex = true;
        SensorCards.ItemsSource = _components;
        SensorIndex.ItemsSource = _components;
        SensorIndex.SelectedIndex = _components.Count > 0 ? 0 : -1;
        _syncingIndex = false;
        SensorScroll.ScrollToVerticalOffset(offset);

        if (_components.Count == 0)
            ShowEmpty("Nenhum sensor encontrado", _search.Length > 0 ? $"Nada corresponde a “{_search}” neste filtro." : "Este tipo de sensor não existe neste PC.");
        else SensorEmpty.Visibility = Visibility.Collapsed;
        UpdateFootnote();
    }

    private void Release()
    {
        SensorCards.ItemsSource = null;
        SensorIndex.ItemsSource = null;
        _components = new();
        _builtRows = -1;
    }

    private void ShowEmpty(string title, string text)
    {
        SensorEmptyTitle.Text = title;
        SensorEmptyText.Text = text;
        SensorEmpty.Visibility = Visibility.Visible;
    }

    private void UpdateFootnote() => SensorFootnote.Text =
        $"Mínimo, média e máximo contam desde as {_since:HH:mm}. Leitura a cada segundo (pausada durante o teste de placa de vídeo do Benchmark). " +
        "A faixa vai do zero ao topo da escala: o trecho aceso é o intervalo visto e o traço, a leitura atual. Passe o mouse num sensor para ver o nome técnico.";

    // ---------------- Filtros ----------------

    private void BuildChips()
    {
        if (_monitor == null) return;
        var groups = _monitor.Rows.Where(r => r.HasReading).Select(r => SensorNaming.Group(r.Type)).Distinct().OrderBy(g => g.Order).ToList();
        string keys = string.Join(",", groups.Select(g => g.Key));
        if (keys == _chipKeys) return;
        _chipKeys = keys;
        if (_group != null && groups.All(g => g.Key != _group)) _group = null;
        CategoryChips.Children.Clear();
        AddChip("Todos", null);
        foreach (var g in groups) AddChip(g.Title, g.Key);
    }

    private void AddChip(string label, string? key)
    {
        var chip = new RadioButton { Content = label, GroupName = "SensorGroup", Style = (Style)FindResource("Chip"), IsChecked = key == _group };
        chip.Checked += (_, _) => { if (_group == key) return; _group = key; Rebuild(); SensorScroll.ScrollToTop(); };
        CategoryChips.Children.Add(chip);
    }

    private void SensorSearch_Changed(object sender, TextChangedEventArgs e)
    {
        _search = SensorSearch.Text.Trim();
        Rebuild();
        SensorScroll.ScrollToTop();
    }

    private void BtnSensorsReset_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.ResetStats();
        _since = DateTime.Now;
        UpdateFootnote();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SensorComponent c) return;
        c.Expanded = !c.Expanded;
        if (c.Expanded) _collapsed.Remove(c.Id); else _collapsed.Add(c.Id);
    }

    // ---------------- Índice e rolagem ----------------

    private void SensorIndex_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingIndex || SensorIndex.SelectedItem is not SensorComponent c) return;
        if (SensorCards.ItemContainerGenerator.ContainerFromItem(c) is not FrameworkElement card) return;
        double y = card.TransformToAncestor(SensorStack).Transform(new Point(0, 0)).Y;
        _jumpedAt = DateTime.UtcNow;
        SensorScroll.ScrollToVerticalOffset(y);
    }

    /// <summary>Acende no índice o componente cujo cartão está no topo da área visível.</summary>
    private void SensorScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 || _components.Count == 0) return;
        if ((DateTime.UtcNow - _jumpedAt).TotalMilliseconds < 400) return; // o clique no índice manda; o último cartão pode não chegar ao topo
        int current = 0;
        for (int i = 0; i < _components.Count; i++)
        {
            if (SensorCards.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement card) continue;
            double y = card.TransformToAncestor(SensorStack).Transform(new Point(0, 0)).Y;
            if (y <= SensorScroll.VerticalOffset + 40) current = i; else break;
        }
        if (SensorScroll.VerticalOffset >= SensorScroll.ScrollableHeight - 1) current = _components.Count - 1;
        if (SensorIndex.SelectedIndex == current) return;
        _syncingIndex = true;
        SensorIndex.SelectedIndex = current;
        SensorIndex.ScrollIntoView(SensorIndex.SelectedItem);
        _syncingIndex = false;
    }

    /// <summary>Com largura, índice à esquerda e grupos em duas colunas; estreita, só os cartões, um grupo por linha.</summary>
    private void SensorBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool index = e.NewSize.Width >= 1000;
        IndexColumn.Width = new GridLength(index ? 276 : 0);
        IndexGap.Width = new GridLength(index ? 16 : 0);
        IndexCard.Visibility = index ? Visibility.Visible : Visibility.Collapsed;
        double content = e.NewSize.Width - (index ? 292 : 0);
        SectionColumns = content >= 1180 ? 2 : 1;
    }
}
