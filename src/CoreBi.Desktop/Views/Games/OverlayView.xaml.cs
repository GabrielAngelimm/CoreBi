using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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
    /// Escala da prévia em relação ao overlay real: largura da tela desenhada (798) pela do monitor principal,
    /// com 40 % de folga para o texto continuar legível; mesmo no tamanho Grande a caixa cabe no quarto da tela do seu canto.
    /// </summary>
    private static readonly double PreviewFactor = 798.0 / Math.Max(1280, SystemParameters.PrimaryScreenWidth) * 1.4;

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

    public OverlayView()
    {
        InitializeComponent();
        OvAdminNote.Visibility = ProcessElevation.IsElevated ? Visibility.Collapsed : Visibility.Visible;
    }

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
        var groups = OverlayMetrics.All.GroupBy(m => m.Group).ToList();
        for (int i = 0; i < groups.Count; i++)
        {
            string group = groups[i].Key;
            var items = groups[i].ToList();
            var meta = Groups.FirstOrDefault(g => g.Group == group);
            var row = new Border { BorderThickness = new Thickness(0, 0, 0, i < groups.Count - 1 ? 1 : 0), Padding = new Thickness(0, 10, 0, 10) };
            row.SetResourceReference(Border.BorderBrushProperty, "Line");

            var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var glyph = new HwIcon { Kind = meta.Icon ?? "\uE946", Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top };
            grid.Children.Add(glyph);

            var name = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            var title = new TextBlock { Text = group, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Padding = new Thickness(0, 6, 0, 0) };
            title.SetResourceReference(TextBlock.FontFamilyProperty, "LabelFont");
            name.Children.Add(title);
            if (meta.Full is { Length: > 0 } full)
            {
                var sub = new TextBlock { Text = full, FontSize = 11.5, LineHeight = 15, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                sub.SetResourceReference(TextBlock.FontFamilyProperty, "SmallFont");
                sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                name.Children.Add(sub);
            }
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            // 7 px acima alinha a linha de base da contagem com o nome e a primeira fila de pílulas (medido).
            var count = new TextBlock { FontSize = 12, Margin = new Thickness(12, 7, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            Typography.SetNumeralAlignment(count, FontNumeralAlignment.Tabular);
            Grid.SetColumn(count, 3);
            grid.Children.Add(count);

            void Refresh()
            {
                int on = items.Count(m => ShowItems.Contains(m.Key));
                count.Text = $"{on}/{items.Count}";
                count.ToolTip = on == 0 ? $"Nada marcado: a linha {group} não aparece no overlay." : $"{on} de {items.Count} itens no overlay.";
                count.SetResourceReference(TextBlock.ForegroundProperty, on > 0 ? "Muted" : "Faint");
                glyph.SetResourceReference(Control.ForegroundProperty, on > 0 ? meta.ResourceKey ?? "Signal" : "Faint");
            }

            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
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
                wrap.Children.Add(chip);
            }
            Grid.SetColumn(wrap, 2);
            grid.Children.Add(wrap);

            Refresh();
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
        SaveItems();
        BuildPicker();
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

    internal void LayoutPreview(string corner, double scale)
    {
        OvPreviewHost.HorizontalAlignment = corner.EndsWith('R') ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        OvPreviewHost.VerticalAlignment = corner.StartsWith('B') ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        OvPreviewScale.ScaleX = OvPreviewScale.ScaleY = PreviewFactor * scale;
        OvCornerName.Text = corner switch
        {
            "TR" => "Canto superior direito",
            "BL" => "Canto inferior esquerdo",
            "BR" => "Canto inferior direito",
            _ => "Canto superior esquerdo",
        };
        if (_corner != null && _corner != corner && SystemParameters.ClientAreaAnimation)
            OvPreviewHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, OpacityOf(OvOpacity.Value), TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
        _corner = corner;
    }

    internal void UpdatePreview()
    {
        if (_context == null || !IsVisible) return;
        var live = _context.Frames.Running ? _context.Frames.GetForegroundStats() : null;
        var frames = live ?? SampleFrames;
        ShowPreview(frames, BuildLines(), ShowItems, ShowItems.Contains("clock") ? DateTime.Now.ToString("HH:mm") : null,
            live == null, IsOverlayEnabled);
        UpdateStatus(live);
    }

    internal void ShowPreview(FrameStats f, IReadOnlyList<OverlayLine> lines, IReadOnlySet<string> show, string? extra, bool sample, bool overlayOn)
    {
        OvPreview.Update(f, lines, show, extra);
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
