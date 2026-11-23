using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Games;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Sensors;

namespace CoreBi.Desktop.Views;

public partial class OverlayView : UserControl
{
    /// <summary>
    /// Escala da prévia em relação ao overlay real: largura da tela desenhada (984) pela do monitor principal,
    /// com 40 % de folga para o texto continuar legível; mesmo no tamanho Grande a caixa cabe no quarto da tela do seu canto.
    /// </summary>
    private static readonly double PreviewFactor = 984.0 / Math.Max(1280, SystemParameters.PrimaryScreenWidth) * 1.4;

    /// <summary>A prévia cresce até o tamanho escolhido com a mesma curva dos números da Rede.</summary>
    private static readonly IEasingFunction Glide = new QuinticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>O ícone (HwIcon) e a legenda de cada grupo, curta para caber numa linha sob o nome.</summary>
    private static readonly Dictionary<string, (string Icon, string Caption)> GroupInfo = new()
    {
        ["FPS"] = ("\uEC4A", "Quadros por segundo"),
        ["GPU"] = ("gpu", "Placa de vídeo"),
        ["VRAM"] = ("vram", "Memória de vídeo"),
        ["CPU"] = ("cpu", "Processador"),
        ["RAM"] = ("ram", "Memória"),
        ["Outros"] = ("\uE727", "Nomes e hora"),
    };

    /// <summary>
    /// Onde cada item fica na grade do seu grupo, coluna por coluna. As colunas têm o mesmo sentido em todos os grupos:
    /// carga | clock | calor e energia; no FPS, taxa | tempo de quadro. VRAM, RAM e Outros são uma linha só. A coluna mais curta é
    /// a da direita, para a grade ler da esquerda para a direita e o vão ficar no fim (na GPU o consumo vai para a carga).
    /// </summary>
    private static readonly Dictionary<string, string[][]> ItemColumns = new()
    {
        ["FPS"] = new[] { new[] { "fps", "low1" }, new[] { "frametime", "graph" } },
        ["GPU"] = new[] { new[] { "gpu_load", "gpu_fan", "gpu_power" }, new[] { "gpu_clock", "gpu_memclock", "gpu_volt" }, new[] { "gpu_temp", "gpu_hotspot" } },
        ["VRAM"] = new[] { new[] { "vram_used" }, new[] { "vram_pct" } },
        ["CPU"] = new[] { new[] { "cpu_load", "cpu_maxcore" }, new[] { "cpu_clock", "cpu_clockmax" }, new[] { "cpu_temp", "cpu_power" } },
        ["RAM"] = new[] { new[] { "ram_used" }, new[] { "ram_pct" } },
        ["Outros"] = new[] { new[] { "hwnames" }, new[] { "game" }, new[] { "clock" } },
    };

    private static readonly Dictionary<string, string> ItemHints = new()
    {
        ["frametime"] = "Tempo de cada quadro, em milissegundos. Quanto mais estável, mais liso o jogo.",
        ["low1"] = "O FPS do 1% de quadros mais lentos. Mostra as travadas que a média esconde.",
        ["graph"] = "O tempo de cada quadro numa linha: os picos são as travadas.",
        ["gpu_hotspot"] = "O ponto mais quente do chip. Depende da placa de vídeo.",
        ["gpu_memclock"] = "Depende da placa de vídeo.",
        ["gpu_fan"] = "Depende da placa de vídeo.",
        ["hwnames"] = "Mostra o modelo ao lado de CPU e GPU e a quantidade de RAM (ex.: CPU Ryzen 7 5700X).",
        ["cpu_temp"] = "Precisa do CoreBi aberto como administrador.",
        ["cpu_clock"] = "Precisa do CoreBi aberto como administrador.",
        ["cpu_clockmax"] = "O clock do núcleo mais rápido. Precisa do CoreBi aberto como administrador.",
        ["cpu_power"] = "Precisa do CoreBi aberto como administrador.",
    };

    private static readonly Dictionary<string, HashSet<string>> Presets = new()
    {
        ["min"] = new() { "fps", "frametime", "low1" },
        ["gargalo"] = new() { "hwnames", "fps", "low1", "graph", "gpu_load", "gpu_clock", "vram_used", "vram_pct", "cpu_load", "cpu_maxcore", "cpu_clock", "ram_pct" },
        ["termico"] = new() { "hwnames", "fps", "gpu_temp", "gpu_hotspot", "gpu_power", "gpu_fan", "cpu_temp", "cpu_power", "cpu_clock", "clock" },
    };

    private static readonly FrameStats SampleFrames = new(0, "Seu jogo", 144, 6.9, 118,
        Enumerable.Range(0, 120).Select(i => 6.9 + Math.Sin(i * 1.7) * 0.35 + Math.Sin(i * 0.43) * 0.25 + (i is 38 or 91 ? 6.2 : i is 39 or 92 ? 2.1 : 0)).ToList());

    private FeatureContext? _context;
    private HashSet<string>? _showItems;
    private string? _corner;
    private bool _ready;
    private readonly Dictionary<string, CheckBox> _chips = new();

    public OverlayView()
    {
        InitializeComponent();
        // Primeira carga: a prévia espera o nome do hardware e a primeira leitura dos sensores; a página entra inteira em esqueleto
        Skeleton.SetActive(OvPositionCard, true);
    }

    private static bool Animate => SystemParameters.ClientAreaAnimation;

    internal bool IsOverlayEnabled => ChkOverlay.IsChecked == true;

    /// <summary>Overlay ligado e não suspenso por um benchmark: só então precisa da captura de quadros (ETW).</summary>
    internal bool IsOverlayLive => IsOverlayEnabled && !_suspendedForBenchmark;

    internal void Initialize(FeatureContext context)
    {
        _context = context;
        var settings = context.Settings;
        foreach (var rb in OvCornerPanel.Children.OfType<RadioButton>())
            rb.IsChecked = (string)rb.Tag == settings.OverlayCorner;

        var scales = OvScalePanel.Children.OfType<RadioButton>().ToList();
        // O tamanho salvo vai para a opção mais próxima (os três tamanhos já mudaram de valor uma vez).
        var nearest = scales.MinBy(rb => Math.Abs(double.Parse((string)rb.Tag, CultureInfo.InvariantCulture) - settings.OverlayScale));
        foreach (var rb in scales) rb.IsChecked = rb == nearest;

        BuildPicker();
        LayoutPreview(settings.OverlayCorner, settings.OverlayScale);
        SetOpacity(settings.OverlayOpacity);
        _ready = true;
        UpdateStatus(null);
        UpdateReady();
    }

    /// <summary>Sai do esqueleto quando a prévia tem o que mostrar: nomes do hardware e a primeira leitura dos sensores.</summary>
    private void ReleaseSkeleton()
    {
        if (_context is { HardwareRead: true, SensorsRead: true } && Skeleton.GetActive(OvPositionCard))
            Skeleton.SetActive(OvPositionCard, false);
    }

    internal void RefreshHardware()
    {
        if (_context == null) return;
        UpdatePreview();
        if (IsOverlayEnabled) UpdateOverlay();
    }

    internal void ToggleOverlay() => ChkOverlay.IsChecked = ChkOverlay.IsChecked != true;

    private HashSet<string> ShowItems => _showItems ??= LoadShowItems();

    private HashSet<string> LoadShowItems()
    {
        if (_context == null) return OverlayMetrics.Defaults();
        var settings = _context.Settings;
        var set = settings.OverlayItems is { } list ? list.ToHashSet() : OverlayMetrics.Defaults();
        if (!settings.OverlayNamesOffered)
        {
            set.Add("hwnames");
            settings.OverlayNamesOffered = true;
            settings.OverlayItems = OverlayMetrics.All.Select(x => x.Key).Where(set.Contains).ToList();
            _context.SaveSettings();
        }
        return set;
    }

    /// <summary>
    /// O que mostrar: um grupo por componente, com o ícone, o nome e a legenda numa coluna à esquerda e os itens em três colunas.
    /// O fio de cada grupo fica no topo da sua faixa, com 8 px até o grupo de cima e até o seu; o cartão termina no último grupo.
    /// </summary>
    private void BuildPicker()
    {
        OvItemsPanel.Children.Clear();
        _chips.Clear();
        var itemStyle = (Style)Resources["OvItem"];
        var groups = OverlayMetrics.All.GroupBy(m => m.Group).ToList();
        for (int i = 0; i < groups.Count; i++)
        {
            string group = groups[i].Key;
            var slot = new Grid();
            if (i > 0)
            {
                var rule = new Border { VerticalAlignment = VerticalAlignment.Top };
                rule.SetResourceReference(StyleProperty, "Divider");
                slot.Children.Add(rule);
            }

            // 8 px acima e abaixo de cada fio; o último grupo encosta na margem do cartão (-2 px: medido, a legenda fica a 25 px do pé, como o título do topo)
            bool last = i == groups.Count - 1;
            var row = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, i == 0 ? 0 : 8, 0, last ? -2 : 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Ícone sutil (20 px, no tom secundário) e, ao lado, o título de 16 e a legenda de 12, cada um numa linha só,
            // no alto da primeira linha de itens (38 px)
            GroupInfo.TryGetValue(group, out var info);
            var head = new Grid { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 12, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new HwIcon { Kind = info.Icon ?? "\uE946", Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0) };
            icon.SetResourceReference(Control.ForegroundProperty, "Muted");
            head.Children.Add(icon);
            var name = new StackPanel();
            var title = new TextBlock { Text = group, TextWrapping = TextWrapping.NoWrap };
            title.SetResourceReference(StyleProperty, "GroupTitle");
            name.Children.Add(title);
            if (info.Caption is { Length: > 0 } caption)
            {
                var sub = new TextBlock { Text = caption, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
                sub.SetResourceReference(StyleProperty, "Caption");
                name.Children.Add(sub);
            }
            Grid.SetColumn(name, 1);
            head.Children.Add(name);
            row.Children.Add(head);

            // Três colunas com o mesmo sentido em todos os grupos: carga | clock | calor e energia (FPS: taxa | tempo de quadro)
            var grid = new Grid { VerticalAlignment = VerticalAlignment.Top };
            for (int c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var byKey = groups[i].ToDictionary(m => m.Key);
            var columns = ItemColumns.TryGetValue(group, out var layout) ? layout.Select(col => col.Where(byKey.ContainsKey).ToList()).ToList() : new();
            // Item sem lugar no desenho (novo no motor) entra em linha, depois dos outros
            var rest = groups[i].Where(m => !columns.Any(col => col.Contains(m.Key))).Select(m => m.Key).ToList();
            int rows = columns.Count > 0 ? columns.Max(col => col.Count) : 0;
            var cells = new List<(string Key, int Row, int Col)>();
            for (int c = 0; c < columns.Count; c++)
                for (int r = 0; r < columns[c].Count; r++) cells.Add((columns[c][r], r, c));
            for (int k = 0; k < rest.Count; k++) cells.Add((rest[k], rows + k / 3, k % 3));
            foreach (var (key, r, c) in cells)
            {
                var m = byKey[key];
                while (grid.RowDefinitions.Count <= r) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var item = new CheckBox { Style = itemStyle, Content = m.Label, Tag = m.Key, IsChecked = ShowItems.Contains(m.Key) };
                if (ItemHints.TryGetValue(m.Key, out var hint)) item.ToolTip = hint;
                item.Click += (_, _) =>
                {
                    if (item.IsChecked == true) ShowItems.Add(m.Key); else ShowItems.Remove(m.Key);
                    SaveItems();
                };
                _chips[m.Key] = item;
                Grid.SetRow(item, r);
                Grid.SetColumn(item, c);
                grid.Children.Add(item);
            }
            Grid.SetColumn(grid, 1);
            row.Children.Add(grid);

            slot.Children.Add(row);
            OvItemsPanel.Children.Add(slot);
        }
        SyncPreset();
    }

    private void SyncPreset()
    {
        string? match = Presets.FirstOrDefault(p => ShowItems.SetEquals(p.Value)).Key
            ?? (ShowItems.SetEquals(OverlayMetrics.Defaults()) ? "default"
                : ShowItems.Count == OverlayMetrics.All.Length ? "all" : null);
        foreach (var rb in OvPresetPanel.Children.OfType<RadioButton>())
            rb.IsChecked = (string)rb.Tag == match;
    }

    private void SaveItems()
    {
        if (_context == null) return;
        _context.Settings.OverlayItems = OverlayMetrics.All.Select(m => m.Key).Where(ShowItems.Contains).ToList();
        _context.SaveSettings();
        SyncPreset();
        UpdateOverlay();
        UpdatePreview();
    }

    private void OvPreset_Click(object sender, RoutedEventArgs e)
    {
        _showItems = (string)((FrameworkElement)sender).Tag switch
        {
            "all" => OverlayMetrics.All.Select(m => m.Key).ToHashSet(),
            var k when Presets.TryGetValue(k, out var set) => set.ToHashSet(),
            _ => OverlayMetrics.Defaults(),
        };
        // Os LEDs trocam no lugar (nada é recriado, a lista não pisca)
        foreach (var (key, item) in _chips) item.IsChecked = _showItems.Contains(key);
        SaveItems();
    }

    private void OvOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OvOpacityText == null || OvPreviewHost == null) return;
        OvOpacityText.Text = $"{e.NewValue:0} %";
        double opacity = OpacityOf(e.NewValue);
        OvPreviewHost.Opacity = opacity;
        OnOpacityChanged(opacity);
    }

    private void OvOpacity_Wheel(object sender, MouseWheelEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 5;
        OvOpacity.Value = Math.Clamp(OvOpacity.Value + Math.Sign(e.Delta) * step, OvOpacity.Minimum, OvOpacity.Maximum);
        e.Handled = true;
    }

    private static double OpacityOf(double percent) => Math.Clamp(percent / 100, 0.3, 1);

    internal void SetOpacity(double opacity)
    {
        OvOpacity.Value = Math.Round(opacity * 100);
        OvPreviewHost.Opacity = opacity;
    }

    private void OvMain_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        bool wide = e.NewSize.Width >= 1180;
        Grid.SetRow(OvSide, wide ? 0 : 1);
        Grid.SetColumn(OvSide, wide ? 2 : 0);
        Grid.SetColumnSpan(OvSide, wide ? 1 : 3);
        Grid.SetColumnSpan(OvPositionCard, wide ? 1 : 3);
        OvSide.Margin = new Thickness(0, wide ? 0 : 16, 0, 0);
        OvMonitorBox.MinHeight = wide ? 300 : 440;
    }

    /// <summary>Tamanho do tracejado de um canto livre (CornerZone): a prévia passa por ele antes de crescer.</summary>
    private static readonly Size ZoneSize = new(184, 104);
    private static readonly TimeSpan MoveTime = TimeSpan.FromMilliseconds(420), GrowTime = TimeSpan.FromMilliseconds(300);
    /// <summary>O deslize de um canto a outro: sai e chega devagar, sem mudar de tamanho no caminho.</summary>
    private static readonly IEasingFunction Travel = new CubicEase { EasingMode = EasingMode.EaseInOut };
    private double _scale = 1;

    internal void LayoutPreview(string corner, double scale)
    {
        bool moving = _corner != null && _corner != corner;
        bool resizing = _corner != null && Math.Abs(_scale - scale) > 0.001;
        bool animate = (moving || resizing) && Animate && OvPreviewHost.IsVisible;
        // Onde a caixa está agora, como se vê (no meio de outro movimento, inclusive), antes de trocar canto e tamanho
        Rect from = animate ? VisibleRect() : Rect.Empty;
        StopMotion();

        OvPreviewHost.HorizontalAlignment = corner.EndsWith('R') ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        OvPreviewHost.VerticalAlignment = corner.StartsWith('B') ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        _scale = scale;
        FitScale();
        OvCornerName.Text = corner switch
        {
            "TR" => "Superior direito",
            "BL" => "Inferior esquerdo",
            "BR" => "Inferior direito",
            _ => "Superior esquerdo",
        };
        _corner = corner;
        OvScreen.UpdateLayout();
        HideCoveredZones();
        if (!animate) return;

        // A caixa já está no lugar e no tamanho finais; a transformação a leva de onde estava até lá. Ao trocar de canto ela
        // desliza direto até o novo canto, num movimento só e suave; ao trocar o tamanho, cresce ou encolhe presa ao canto.
        Rect to = new(OvPreviewHost.TranslatePoint(new Point(), OvScreen), OvPreviewHost.RenderSize);
        if (to.Width <= 0 || to.Height <= 0) return;
        bool right = corner.EndsWith('R'), bottom = corner.StartsWith('B');
        OvPreviewHost.RenderTransformOrigin = new Point(right ? 1 : 0, bottom ? 1 : 0);
        Point Anchor(Rect r) => new(right ? r.Right : r.Left, bottom ? r.Bottom : r.Top);
        double startZoom = from.Width / to.Width;
        Vector startShift = Anchor(from) - Anchor(to);
        var (time, ease) = moving ? (MoveTime, Travel) : (GrowTime, Glide);
        void Run(Animatable target, DependencyProperty property, double start, double end) =>
            target.BeginAnimation(property, new DoubleAnimation(start, end, time) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        Run(OvPreviewZoom, ScaleTransform.ScaleXProperty, startZoom, 1);
        Run(OvPreviewZoom, ScaleTransform.ScaleYProperty, startZoom, 1);
        if (moving)
        {
            Run(OvPreviewShift, TranslateTransform.XProperty, startShift.X, 0);
            Run(OvPreviewShift, TranslateTransform.YProperty, startShift.Y, 0);
        }
    }

    private void StopMotion()
    {
        OvPreviewZoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        OvPreviewZoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        OvPreviewShift.BeginAnimation(TranslateTransform.XProperty, null);
        OvPreviewShift.BeginAnimation(TranslateTransform.YProperty, null);
    }

    /// <summary>O retângulo da caixa como se vê na tela desenhada, com a transformação do momento.</summary>
    private Rect VisibleRect() =>
        OvPreviewHost.TransformToAncestor(OvScreen).TransformBounds(new Rect(OvPreviewHost.RenderSize));

    /// <summary>
    /// A escala da prévia: a do tamanho escolhido, mas só até onde a caixa não cobre nenhum outro tracejado (com "Tudo" no
    /// Grande ela cobria). Só a prévia encolhe; o overlay de verdade mantém o tamanho escolhido. A caixa fica a 16 px da borda,
    /// o contorno tracejado dela sai 5 px e sobram 8 px até o tracejado vizinho.
    /// </summary>
    private void FitScale()
    {
        double target = PreviewFactor * _scale;
        OvPreview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = OvPreview.DesiredSize;
        if (natural.Width > 0 && natural.Height > 0)
        {
            double maxWidth = OvScreen.Width - 16 - ZoneSize.Width - 16 - 5 - 8;
            double maxHeight = OvScreen.Height - 16 - ZoneSize.Height - 16 - 5 - 8;
            target = Math.Min(target, Math.Min(maxWidth / natural.Width, maxHeight / natural.Height));
        }
        if (Math.Abs(OvPreviewScale.ScaleX - target) > 0.0005) OvPreviewScale.ScaleX = OvPreviewScale.ScaleY = target;
    }

    private void OvPreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => HideCoveredZones();

    /// <summary>Segurança: se a prévia ainda assim cobrir um tracejado (a escala limita antes), ele some em vez de ficar por baixo.</summary>
    private void HideCoveredZones()
    {
        // Pelo lugar de layout (alinhamento e tamanho), sem a transformação de um movimento em andamento
        var size = OvPreviewHost.RenderSize;
        var box = new Rect(OvPreviewHost.HorizontalAlignment == HorizontalAlignment.Right ? OvScreen.Width - 16 - size.Width : 16,
            OvPreviewHost.VerticalAlignment == VerticalAlignment.Bottom ? OvScreen.Height - 16 - size.Height : 16, size.Width, size.Height);
        box.Inflate(8, 8);
        foreach (var rb in OvCornerPanel.Children.OfType<RadioButton>())
        {
            string tag = (string)rb.Tag;
            double x = tag.EndsWith('R') ? OvScreen.Width - 16 - ZoneSize.Width : 16;
            double y = tag.StartsWith('B') ? OvScreen.Height - 16 - ZoneSize.Height : 16;
            bool covered = tag != _corner && box.IntersectsWith(new Rect(x, y, ZoneSize.Width, ZoneSize.Height));
            rb.Visibility = covered ? Visibility.Hidden : Visibility.Visible;
        }
    }

    internal void UpdatePreview()
    {
        if (_context == null) return;
        ReleaseSkeleton();
        if (!IsVisible) return;
        UpdateReady();
        var live = _context.Frames.Running ? _context.Frames.GetForegroundStats() : null;
        var frames = live ?? SampleFrames;
        ShowPreview(frames, BuildLines(), ShowItems, ShowItems.Contains("clock") ? DateTime.Now.ToString("HH:mm") : null,
            live == null, IsOverlayEnabled);
        UpdateStatus(live);
    }

    internal void ShowPreview(FrameStats f, IReadOnlyList<OverlayLine> lines, IReadOnlySet<string> show, string? extra, bool sample, bool overlayOn)
    {
        OvPreview.Update(f, lines, show, extra);
        FitScale();
        // O subtítulo fica sempre, para o monitor não pular quando um jogo começa a ser medido
        string note = sample ? "Clique num canto para mover a prévia. FPS de exemplo até um jogo abrir; sensores ao vivo." : "Clique num canto para mover a prévia. FPS e sensores ao vivo, do jogo aberto.";
        if (OvSampleNote.Text != note) OvSampleNote.Text = note;
        OvSampleNote.ToolTip = sample ? "Enquanto o CoreBi não mede um jogo, a prévia mostra 144 FPS de exemplo. Os sensores são lidos ao vivo." : null;
        OvLed.SetResourceReference(Shape.FillProperty, overlayOn ? "HudSignal" : "HudSep");
    }

    private static string? ShortOverlayName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        return name.Length <= 20 ? name : name[..19].TrimEnd() + "…";
    }

    private List<OverlayLine> BuildLines()
    {
        if (_context == null) return new();
        var show = ShowItems;
        var memory = MemoryInfo.ReadPhysicalUsage();
        var sensors = _context.Sensors.Snapshot();
        var gpuInfo = _context.PrimaryGpu;
        var gpuLive = gpuInfo != null ? GpuMonitor.ReadLive(gpuInfo, sensors) : null;
        var cpuLive = CpuTelemetry.Read(_context.Sensors, sensors);
        var lines = new List<OverlayLine>();
        static string Temp(float? v) => v is float x ? $"{x:0} °C" : "—";
        static string Pct(double? v) => v is double x ? $"{x:0}%" : "—";
        bool names = show.Contains("hwnames");
        void Line(string label, List<string> parts, string? detail = null)
        {
            if (parts.Count > 0) lines.Add(new OverlayLine(label, parts, names ? detail : null));
        }

        var cpu = new List<string>();
        if (show.Contains("cpu_load")) cpu.Add(Pct(cpuLive.LoadPct));
        if (show.Contains("cpu_maxcore")) cpu.Add(cpuLive.BusiestCoreLoadPct is float mx ? $"núcleo {mx:0}%" : "núcleo —");
        if (show.Contains("cpu_temp")) cpu.Add(Temp(cpuLive.TempC));
        if (show.Contains("cpu_clock")) cpu.Add(cpuLive.AvgClockMHz is float ca ? $"{ca / 1000:0.00} GHz" : "— GHz");
        if (show.Contains("cpu_clockmax")) cpu.Add(cpuLive.MaxClockMHz is float cm ? $"pico {cm / 1000:0.00} GHz" : "pico —");
        if (show.Contains("cpu_power")) cpu.Add(cpuLive.PowerW is float cw && cw > 0 ? $"{cw:0} W" : "— W"); // 0 = sem leitura (precisa de administrador)
        Line("CPU", cpu, ShortOverlayName(HardwareNaming.ShortCpuName(_context.CpuName)));

        var gpu = new List<string>();
        if (show.Contains("gpu_load")) gpu.Add(Pct(gpuLive?.LoadPct));
        if (show.Contains("gpu_temp")) gpu.Add(Temp(gpuLive?.TempC));
        if (show.Contains("gpu_hotspot")) gpu.Add("HS " + Temp(gpuLive?.HotspotTempC));
        if (show.Contains("gpu_clock")) gpu.Add(gpuLive?.CoreClockMHz is float c ? $"{c:0} MHz" : "— MHz");
        if (show.Contains("gpu_memclock")) gpu.Add(gpuLive?.MemoryClockMHz is float mc ? $"mem {mc:0} MHz" : "mem —");
        if (show.Contains("gpu_power")) gpu.Add(gpuLive?.PowerW is float w ? $"{w:0} W" : "— W");
        if (show.Contains("gpu_volt")) gpu.Add(gpuLive?.VoltageV is float v ? $"{v:0.000} V" : "— V");
        if (show.Contains("gpu_fan"))
            gpu.Add(gpuLive?.FanRpm is float rpm && rpm > 0 ? $"{rpm:0} RPM"
                  : gpuLive?.FanPct is float pct ? $"fan {pct:0}%" : "fan —");
        Line("GPU", gpu, ShortOverlayName(gpuInfo != null ? HardwareNaming.ShortGpuName(gpuInfo.Name) : null));

        var vram = new List<string>();
        if (show.Contains("vram_used")) vram.Add(gpuLive?.VramUsedGb is double vu ? $"{vu:0.0} GB" : "—");
        if (show.Contains("vram_pct")) vram.Add(Pct(gpuLive?.VramPct));
        Line("VRAM", vram);

        var ram = new List<string>();
        if (show.Contains("ram_used")) ram.Add(memory is { } used ? $"{used.Used / 1073741824.0:0.0} GB" : "—");
        if (show.Contains("ram_pct")) ram.Add(Pct(memory?.UsedPercent));
        Line("RAM", ram, memory is { } capacity ? $"{Math.Round(capacity.Total / 1073741824.0):0} GB" : null);
        return lines;
    }
}
