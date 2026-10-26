using CoreBi.Desktop.Features.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Sensors;
using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

/// <summary>
/// Monitor de hardware: componentes em cartões, sensores agrupados por tipo com atual, mínimo, média e máximo.
/// O índice é um filtro: "Visão geral" mostra todos os componentes na ordem de uso e escolher um deles mostra só ele.
/// Os cartões só existem com a página à vista; escondida, ela solta as linhas e não gasta nada a cada leitura.
/// </summary>
public partial class SensorsView : UserControl
{
    public static readonly DependencyProperty SectionColumnsProperty =
        DependencyProperty.Register(nameof(SectionColumns), typeof(int), typeof(SensorsView), new PropertyMetadata(2));
    public int SectionColumns { get => (int)GetValue(SectionColumnsProperty); set => SetValue(SectionColumnsProperty, value); }

    private SensorMonitor? _monitor;
    private string? _component;       // componente escolhido no índice (null = visão geral)
    private string? _group;           // filtro por tipo (null = todos)
    private string _search = "";      // filtro por texto
    private readonly HashSet<string> _collapsed = new();
    private int _builtRows = -1;
    private string _chipKeys = "";
    private string _indexKeys = "";
    private DateTime _since = DateTime.Now;
    private IReadOnlyList<IndexEntry> _index = Array.Empty<IndexEntry>();
    private bool _syncing;

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
        int components = live.Select(SensorBoard.KeyOf).Distinct().Count();
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
        var layout = SensorBoard.Build(_monitor.Rows.ToList(), _component, _group, _search, _collapsed);
        _component = layout.SelectedId;
        _builtRows = _monitor.Rows.Count(r => r.HasReading);
        _syncing = true;
        BuildIndex(layout);
        BuildChips(layout);
        _syncing = false;
        double offset = SensorScroll.VerticalOffset;
        SensorCards.ItemsSource = layout.Cards;
        SensorScroll.ScrollToVerticalOffset(offset);

        if (layout.Cards.Count == 0)
            ShowEmpty("Nenhum sensor encontrado", _search.Length > 0 ? $"Nada corresponde a “{_search}” neste filtro." : "Este tipo de sensor não existe neste componente.");
        else SensorEmpty.Visibility = Visibility.Collapsed;
        UpdateFootnote();
    }

    private void Release()
    {
        SensorCards.ItemsSource = null;
        SensorIndex.ItemsSource = null;
        _builtRows = -1;
        _indexKeys = "";
    }

    private void ShowEmpty(string title, string text)
    {
        SensorEmptyTitle.Text = title;
        SensorEmptyText.Text = text;
        SensorEmpty.Visibility = Visibility.Visible;
    }

    private void UpdateFootnote() => SensorFootnote.Text =
        $"Mínimo, média e máximo contam desde as {_since:HH:mm}. Leitura a cada segundo (pausada durante o teste de placa de vídeo do Benchmark). " +
        "Passe o mouse num sensor para ver o nome técnico. Placas de rede virtuais e camadas de filtro do Windows não aparecem.";

    // ---------------- Índice e filtros ----------------

    /// <summary>O índice só é refeito quando o conjunto de componentes muda; a seleção acompanha o filtro.</summary>
    private void BuildIndex(SensorLayout layout)
    {
        string keys = string.Join("|", layout.Index.Select(i => i.Id));
        if (keys != _indexKeys)
        {
            _indexKeys = keys;
            _index = layout.Index;
            SensorIndex.ItemsSource = _index;
        }
        SensorIndex.SelectedItem = _index.FirstOrDefault(i => i.Id == _component);
    }

    /// <summary>
    /// Filtros por tipo, cada um com quantos sensores tem no componente escolhido; "Todos os tipos" fica separado dos tipos
    /// por um fio. Só são refeitos quando o conjunto muda.
    /// </summary>
    private void BuildChips(SensorLayout layout)
    {
        string keys = $"{_group}/" + string.Join(",", layout.Filters.Select(g => $"{g.Key}:{g.Count}")) + $"/{layout.Total}/{layout.Filters.Sum(f => f.Count)}";
        if (keys == _chipKeys) return;
        _chipKeys = keys;
        _group = layout.Filters.Any(f => f.Key == _group) ? _group : null;
        CategoryChips.Children.Clear();
        AddChip("Todos os tipos", null, layout.Filters.Sum(f => f.Count));
        var divider = new Border { Width = 1, Height = 18, Margin = new Thickness(6, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
        divider.SetResourceReference(Border.BackgroundProperty, "LineStrong");
        CategoryChips.Children.Add(divider);
        foreach (var g in layout.Filters) AddChip(g.Title, g.Key, g.Count);
    }

    private void AddChip(string label, string? key, int count)
    {
        var text = new TextBlock();
        text.Inlines.Add(new System.Windows.Documents.Run(label));
        var number = new System.Windows.Documents.Run("  " + count) { FontSize = 11.5 };
        number.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Faint");
        text.Inlines.Add(number);
        System.Windows.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        var chip = new RadioButton { Content = text, GroupName = "SensorGroup", Style = (Style)FindResource("Chip"), IsChecked = key == _group };
        chip.Checked += (_, _) => { if (_syncing || _group == key) return; _group = key; Rebuild(); SensorScroll.ScrollToTop(); };
        CategoryChips.Children.Add(chip);
    }

    private void SensorIndex_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || SensorIndex.SelectedItem is not IndexEntry entry || entry.Id == _component) return;
        _component = entry.Id;
        Rebuild();
        SensorScroll.ScrollToTop();
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

    /// <summary>Com largura, índice à esquerda e grupos em duas colunas; estreita, só os cartões, um grupo por linha.</summary>
    private void SensorBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool index = e.NewSize.Width >= 1000;
        IndexColumn.Width = new GridLength(index ? 300 : 0);
        IndexGap.Width = new GridLength(index ? 16 : 0);
        IndexCard.Visibility = index ? Visibility.Visible : Visibility.Collapsed;
        if (!index && _component != null) { _component = null; Rebuild(); } // sem o índice não há como voltar à visão geral
        double content = e.NewSize.Width - (index ? 316 : 0);
        SectionColumns = content >= 1060 ? 2 : 1;
    }
}
