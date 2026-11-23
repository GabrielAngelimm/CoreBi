using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

    private static readonly (string Group, string Icon, string ResourceKey, string Full)[] Groups =
    {
        ("FPS", "\uEC4A", "Signal", "Quadros por segundo"),
        ("GPU", "gpu", "Signal", "Placa de vídeo"),
        ("VRAM", "vram", "Signal", "Memória da placa de vídeo"),
        ("CPU", "cpu", "Signal", "Processador"),
        ("RAM", "ram", "Signal", "Memória"),
        ("Outros", "\uE727", "Signal", ""),
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
        ["cpu_clockmax"] = "Precisa do CoreBi aberto como administrador.",
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
    private readonly List<Action> _rowRefresh = new();
    /// <summary>O que cada linha tinha marcado quando o interruptor dela foi desligado, para voltar igual.</summary>
    private readonly Dictionary<string, HashSet<string>> _groupMemory = new();

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

    private void BuildPicker()
    {
        OvItemsPanel.Children.Clear();
        _chips.Clear();
        _rowRefresh.Clear();
        var groups = OverlayMetrics.All.GroupBy(m => m.Group).ToList();
        for (int i = 0; i < groups.Count; i++)
        {
            string group = groups[i].Key;
            var items = groups[i].ToList();
            var meta = Groups.FirstOrDefault(g => g.Group == group);
            var row = new Border
            {
                SnapsToDevicePixels = true,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, i == 0 ? 0 : 3, 0, i < groups.Count - 1 ? 3 : 0),
            };

            row.SetResourceReference(Border.BorderBrushProperty, "Line");
            var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var glyph = new HwIcon { Kind = meta.Icon ?? "\uE946", Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top };
            grid.Children.Add(glyph);

            var name = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            var title = new TextBlock { Text = group, Padding = new Thickness(0, 4, 0, 0) };
            title.SetResourceReference(StyleProperty, "GroupTitle");
            name.Children.Add(title);
            if (meta.Full is { Length: > 0 } full)
            {
                var sub = new TextBlock { Text = full, Margin = new Thickness(0, 2, 0, 0) };
                sub.SetResourceReference(StyleProperty, "Caption");
                name.Children.Add(sub);
            }
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            // A linha inteira liga e desliga num interruptor: desligada some do overlay; religada volta com o que estava marcado
            // (ou com os itens padrão do grupo). A linha ligada vira um bloco elevado; a desligada fica só no contorno, apagada.
            var toggle = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 5, 0, 0) };
            toggle.SetResourceReference(StyleProperty, "Switch");
            Grid.SetColumn(toggle, 3);
            grid.Children.Add(toggle);

            void Refresh()
            {
                bool on = items.Any(m => ShowItems.Contains(m.Key));
                toggle.IsChecked = on;
                toggle.ToolTip = on ? $"Tirar a linha {group} do overlay" : $"Mostrar a linha {group} no overlay";
                glyph.SetResourceReference(Control.ForegroundProperty, on ? meta.ResourceKey ?? "Signal" : "Faint");
                title.SetResourceReference(TextBlock.ForegroundProperty, on ? "Fg" : "Muted");
                row.SetResourceReference(Border.BackgroundProperty, on ? "Raised" : "Surface");
                foreach (var m in items)
                    if (_chips.TryGetValue(m.Key, out var c)) c.IsChecked = ShowItems.Contains(m.Key);
            }

            toggle.Click += (_, _) =>
            {
                if (toggle.IsChecked == true)
                {
                    var back = _groupMemory.TryGetValue(group, out var kept) && kept.Count > 0 ? kept
                        : items.Where(m => m.Default).Select(m => m.Key).DefaultIfEmpty(items[0].Key).ToHashSet();
                    ShowItems.UnionWith(back);
                }
                else
                {
                    _groupMemory[group] = items.Where(m => ShowItems.Contains(m.Key)).Select(m => m.Key).ToHashSet();
                    foreach (var m in items) ShowItems.Remove(m.Key);
                }
                Refresh();
                SaveItems();
            };

            var wrap = new WrapPanel { Margin = new Thickness(0, 1, 0, -6), VerticalAlignment = VerticalAlignment.Top };
            foreach (var m in items)
            {
                var chip = new CheckBox { Content = m.Label, Tag = m.Key, IsChecked = ShowItems.Contains(m.Key) };
                chip.SetResourceReference(StyleProperty, "OptionChipSignal");
                if (ItemHints.TryGetValue(m.Key, out var hint)) chip.ToolTip = hint;
                chip.Click += (_, _) =>
                {
                    if (chip.IsChecked == true) ShowItems.Add(m.Key); else ShowItems.Remove(m.Key);
                    Refresh();
                    SaveItems();
                };
                _chips[m.Key] = chip;
                wrap.Children.Add(chip);
            }
            Grid.SetColumn(wrap, 2);
            grid.Children.Add(wrap);

            Refresh();
            _rowRefresh.Add(Refresh);
            row.Child = grid;
            OvItemsPanel.Children.Add(row);
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
        // As pílulas e os tiques trocam no lugar (nada é recriado, a lista não pisca)
        foreach (var refresh in _rowRefresh) refresh();
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
        Grid.SetRow(OvItemsCard, wide ? 0 : 1);
        Grid.SetColumn(OvItemsCard, wide ? 2 : 0);
        Grid.SetColumnSpan(OvItemsCard, wide ? 1 : 3);
        Grid.SetColumnSpan(OvPositionCard, wide ? 1 : 3);
        OvItemsCard.Margin = new Thickness(0, wide ? 0 : 16, 0, 0);
        OvMonitorBox.MinHeight = wide ? 300 : 440;
    }

    /// <summary>Tamanho do tracejado de um canto livre (CornerZone): a prévia passa por ele antes de crescer.</summary>
    private static readonly Size ZoneSize = new(184, 104);
    private static readonly TimeSpan MoveTime = TimeSpan.FromMilliseconds(320), GrowTime = TimeSpan.FromMilliseconds(300);
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

        // A caixa já está no lugar e no tamanho finais; a transformação a leva de onde estava até lá. Ao trocar de canto, ela
        // vai encolhida ao tamanho do tracejado até ele e então cresce até o tamanho escolhido, presa ao canto; ao trocar só o
        // tamanho, cresce ou encolhe direto, presa ao canto.
        Rect to = new(OvPreviewHost.TranslatePoint(new Point(), OvScreen), OvPreviewHost.RenderSize);
        if (to.Width <= 0 || to.Height <= 0) return;
        bool right = corner.EndsWith('R'), bottom = corner.StartsWith('B');
        OvPreviewHost.RenderTransformOrigin = new Point(right ? 1 : 0, bottom ? 1 : 0);
        Point Anchor(Rect r) => new(right ? r.Right : r.Left, bottom ? r.Bottom : r.Top);
        double startZoom = from.Width / to.Width;
        Vector startShift = Anchor(from) - Anchor(to);
        if (moving)
        {
            double zoneZoom = Math.Min(1, Math.Min(ZoneSize.Width / to.Width, ZoneSize.Height / to.Height));
            Animate2(OvPreviewZoom, ScaleTransform.ScaleXProperty, startZoom, zoneZoom, 1);
            Animate2(OvPreviewZoom, ScaleTransform.ScaleYProperty, startZoom, zoneZoom, 1);
            Animate2(OvPreviewShift, TranslateTransform.XProperty, startShift.X, 0, 0);
            Animate2(OvPreviewShift, TranslateTransform.YProperty, startShift.Y, 0, 0);
        }
        else
        {
            var grow = new DoubleAnimation(startZoom, 1, GrowTime) { EasingFunction = Glide, FillBehavior = FillBehavior.Stop };
            OvPreviewZoom.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            OvPreviewZoom.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
    }

    /// <summary>Duas etapas: vai de <paramref name="start"/> a <paramref name="mid"/> no deslize e daí a <paramref name="end"/> no crescimento.</summary>
    private static void Animate2(Animatable target, DependencyProperty property, double start, double mid, double end)
    {
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(mid, KeyTime.FromTimeSpan(MoveTime), Travel));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(end, KeyTime.FromTimeSpan(MoveTime + GrowTime), Glide));
        target.BeginAnimation(property, a);
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
    /// A escala da prévia: a do tamanho escolhido, mas nunca maior que a tela desenhada (com "Tudo" no Grande a caixa passava da
    /// borda e era cortada).
    /// </summary>
    private void FitScale()
    {
        double target = PreviewFactor * _scale;
        OvPreview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = OvPreview.DesiredSize;
        if (natural.Width > 0 && natural.Height > 0)
            target = Math.Min(target, Math.Min((OvScreen.Width - 32) / natural.Width, (OvScreen.Height - 32) / natural.Height));
        if (Math.Abs(OvPreviewScale.ScaleX - target) > 0.0005) OvPreviewScale.ScaleX = OvPreviewScale.ScaleY = target;
    }

    private void OvPreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => HideCoveredZones();

    /// <summary>Um tracejado que a prévia cobre (overlay grande, com muitos itens) some, para não ficar um por cima do outro.</summary>
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
        OvSampleNote.Visibility = sample ? Visibility.Visible : Visibility.Collapsed;
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

        var cpu = new List<string>();
        if (show.Contains("cpu_load")) cpu.Add(Pct(cpuLive.LoadPct));
        if (show.Contains("cpu_maxcore")) cpu.Add(cpuLive.BusiestCoreLoadPct is float mx ? $"núcleo {mx:0}%" : "núcleo —");
        if (show.Contains("cpu_temp")) cpu.Add(Temp(cpuLive.TempC));
        if (show.Contains("cpu_clock")) cpu.Add(cpuLive.AvgClockMHz is float ca ? $"{ca / 1000:0.00} GHz" : "— GHz");
        if (show.Contains("cpu_clockmax")) cpu.Add(cpuLive.MaxClockMHz is float cm ? $"pico {cm / 1000:0.00} GHz" : "pico —");
        if (show.Contains("cpu_power")) cpu.Add(cpuLive.PowerW is float cw && cw > 0 ? $"{cw:0} W" : "— W"); // 0 = sem leitura (precisa de administrador)
        Line("CPU", cpu, ShortOverlayName(HardwareNaming.ShortCpuName(_context.CpuName)));

        var ram = new List<string>();
        if (show.Contains("ram_used")) ram.Add(memory is { } used ? $"{used.Used / 1073741824.0:0.0} GB" : "—");
        if (show.Contains("ram_pct")) ram.Add(Pct(memory?.UsedPercent));
        Line("RAM", ram, memory is { } capacity ? $"{Math.Round(capacity.Total / 1073741824.0):0} GB" : null);
        return lines;
    }
}
