using LibreHardwareMonitor.Hardware;
using CoreBi.Engine.Games;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Sensors;

using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop;

/// <summary>Overlay: o que mostrar (escolhido na aba Overlay) e a montagem das linhas de sensores.</summary>
public partial class MainWindow
{
    internal HashSet<string>? _ovShow;
    internal HashSet<string> OvShow => _ovShow ??= LoadOvShow();

    private HashSet<string> LoadOvShow()
    {
        var set = _settings.OverlayItems is { } l ? l.ToHashSet() : OverlayMetrics.Defaults();
        // Quem já tinha uma seleção salva recebe "Nome do hardware" ligado uma vez; depois vale a escolha dele.
        if (!_settings.OverlayNamesOffered)
        {
            set.Add("hwnames");
            _settings.OverlayNamesOffered = true;
            _settings.OverlayItems = OverlayMetrics.All.Select(x => x.Key).Where(set.Contains).ToList();
            SaveSettings();
        }
        return set;
    }

    /// <summary>
    /// Componentes na ordem do overlay e o glifo de cada linha (todos na mesma cor, a do sinal). O nome é o rótulo da linha no
    /// overlay (a sigla); embaixo dele vai o nome por extenso, para quem não conhece a sigla. "Outros" se explica sozinho.
    /// </summary>
    private static readonly (string Group, string Icon, string Brush, string Full)[] OvGroups =
    {
        ("FPS", "\uEC4A", "Signal", "Quadros por segundo"),
        ("GPU", "gpu", "Signal", "Placa de vídeo"),
        ("VRAM", "vram", "Signal", "Memória da placa de vídeo"),
        ("CPU", "cpu", "Signal", "Processador"),
        ("RAM", "ram", "Signal", "Memória"),
        ("Outros", "", "Signal", ""),
    };

    /// <summary>Itens que dependem da placa ou de permissão: a dica explica por que podem aparecer com travessão.</summary>
    private static readonly Dictionary<string, string> OvItemHints = new()
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

    /// <summary>
    /// Predefinições: "Essencial" (o padrão do catálogo) e "Só FPS" são o básico; "Gargalo" junta o que mostra quem limita o jogo (uso da GPU e do
    /// núcleo mais ocupado, memória) e "Térmico" o que importa para temperatura, consumo e ventoinha.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> OvPresets = new()
    {
        ["min"] = new() { "fps", "frametime", "low1" },
        ["gargalo"] = new() { "hwnames", "fps", "low1", "graph", "gpu_load", "gpu_clock", "vram_used", "vram_pct", "cpu_load", "cpu_maxcore", "cpu_clock", "ram_pct" },
        ["termico"] = new() { "hwnames", "fps", "gpu_temp", "gpu_hotspot", "gpu_power", "gpu_fan", "cpu_temp", "cpu_power", "cpu_clock", "clock" },
    };

    /// <summary>Monta "O que mostrar": uma linha por componente (glifo e nome) com os itens em pílulas e a contagem.</summary>
    internal void BuildOverlayPicker()
    {
        OvItemsPanel.Children.Clear();
        var groups = OverlayMetrics.All.GroupBy(m => m.Group).ToList();
        for (int i = 0; i < groups.Count; i++)
        {
            string group = groups[i].Key;
            var items = groups[i].ToList();
            var meta = OvGroups.FirstOrDefault(g => g.Group == group);
            var row = new Border { BorderThickness = new Thickness(0, 0, 0, i < groups.Count - 1 ? 1 : 0), Padding = new Thickness(0, 10, 0, 10) };
            row.SetResourceReference(Border.BorderBrushProperty, "Line");
            var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // 128: cabe o nome por extenso embaixo da sigla (até duas linhas) sem apertar as pílulas
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Só o desenho, sem ladrilho: aceso no sinal quando a linha aparece no overlay, apagado quando nada dela está marcado
            var glyph = new HwIcon { Kind = meta.Icon ?? "\uE946", Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top };
            grid.Children.Add(glyph);

            // O nome fica na altura do glifo (30 px), na mesma linha de base do texto da primeira fila de pílulas ao lado;
            // embaixo, o nome por extenso, menor e em tom secundário
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

            // A contagem com 8 px em cima ficava 1 px abaixo da base do nome e das pílulas (medido): 7 a põe na mesma base
            var count = new TextBlock { FontSize = 12, Margin = new Thickness(12, 7, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            System.Windows.Documents.Typography.SetNumeralAlignment(count, FontNumeralAlignment.Tabular);
            Grid.SetColumn(count, 3);
            grid.Children.Add(count);

            void Refresh()
            {
                int on = items.Count(m => OvShow.Contains(m.Key));
                count.Text = $"{on}/{items.Count}";
                count.ToolTip = on == 0 ? $"Nada marcado: a linha {group} não aparece no overlay." : $"{on} de {items.Count} itens no overlay.";
                count.SetResourceReference(TextBlock.ForegroundProperty, on > 0 ? "Muted" : "Faint");
                glyph.SetResourceReference(Control.ForegroundProperty, on > 0 ? meta.Brush ?? "Signal" : "Faint");
            }

            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            foreach (var m in items)
            {
                var chip = new CheckBox { Content = m.Label, Tag = m.Key, IsChecked = OvShow.Contains(m.Key) };
                chip.SetResourceReference(StyleProperty, "OptionChipSignal");
                if (OvItemHints.TryGetValue(m.Key, out var hint)) chip.ToolTip = hint;
                chip.Click += (_, _) =>
                {
                    if (chip.IsChecked == true) OvShow.Add(m.Key); else OvShow.Remove(m.Key);
                    Refresh();
                    SaveOverlayItems();
                };
                wrap.Children.Add(chip);
            }
            Grid.SetColumn(wrap, 2);
            grid.Children.Add(wrap);

            Refresh();
            row.Child = grid;
            OvItemsPanel.Children.Add(row);
        }
        SyncOverlayPreset();
    }

    /// <summary>Marca a predefinição que bate com a seleção atual (nenhuma, se a seleção for personalizada).</summary>
    internal void SyncOverlayPreset()
    {
        string? match = OvPresets.FirstOrDefault(p => OvShow.SetEquals(p.Value)).Key
                      ?? (OvShow.SetEquals(OverlayMetrics.Defaults()) ? "default"
                        : OvShow.Count == OverlayMetrics.All.Length ? "all" : null);
        foreach (var rb in OvPresetPanel.Children.OfType<RadioButton>())
            rb.IsChecked = (string)rb.Tag == match;
    }

    internal void SaveOverlayItems()
    {
        _settings.OverlayItems = OverlayMetrics.All.Select(m => m.Key).Where(OvShow.Contains).ToList();
        SaveSettings();
        SyncOverlayPreset();
        UpdateOverlay();
        UpdateOverlayPreview();
    }

    internal void OvPreset_Click(object sender, RoutedEventArgs e)
    {
        var keys = (string)((FrameworkElement)sender).Tag switch
        {
            "all" => OverlayMetrics.All.Select(m => m.Key).ToHashSet(),
            var k when OvPresets.TryGetValue(k, out var set) => set.ToHashSet(),
            _ => OverlayMetrics.Defaults(),
        };
        _ovShow = keys;
        SaveOverlayItems();
        BuildOverlayPicker();
    }

    // ------------------------------------------------------------------ prévia

    /// <summary>FPS de exemplo da prévia quando não há jogo na frente: 144 FPS estáveis com dois engasgos.</summary>
    private static readonly FrameStats OvSampleFrames = new(0, "Seu jogo", 144, 6.9, 118,
        Enumerable.Range(0, 120).Select(i => 6.9 + Math.Sin(i * 1.7) * 0.35 + Math.Sin(i * 0.43) * 0.25 + (i is 38 or 91 ? 6.2 : i is 39 or 92 ? 2.1 : 0)).ToList());

    /// <summary>Prévia da aba Overlay: a mesma caixa do jogo, com os itens marcados, os sensores ao vivo e o FPS do jogo se houver.</summary>
    internal void UpdateOverlayPreview()
    {
        if (!PageOverlay.IsVisible) return;
        var live = _frames.Running ? _frames.GetForegroundStats() : null;
        var f = live ?? OvSampleFrames;
        PageOverlay.ShowPreview(f, BuildOverlayLines(f), OvShow, OvShow.Contains("clock") ? DateTime.Now.ToString("HH:mm") : null,
                                live == null, ChkOverlay.IsChecked == true);
        UpdateOverlayStatus(live);
    }

    internal static string? HwName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        return name.Length <= 20 ? name : name[..19].TrimEnd() + "…";
    }

    internal List<OverlayLine> BuildOverlayLines(FrameStats? f)
    {
        var show = OvShow;
        var memory = MemoryInfo.ReadPhysicalUsage();
        var sensors = _monitor.Snapshot();
        var gpuInfo = _gpus.FirstOrDefault();
        var gpuLive = gpuInfo != null ? GpuMonitor.ReadLive(gpuInfo, sensors) : null;
        var cpuLive = GpuMonitor.ReadCpu(_monitor, sensors);
        var lines = new List<OverlayLine>();
        string T(float? v) => v is float x ? $"{x:0} °C" : "—";
        string P(double? v) => v is double x ? $"{x:0}%" : "—";
        bool names = show.Contains("hwnames");
        void Line(string label, List<string> parts, string? detail = null) { if (parts.Count > 0) lines.Add(new OverlayLine(label, parts, names ? detail : null)); }

        var gpu = new List<string>();
        if (show.Contains("gpu_load")) gpu.Add(P(gpuLive?.LoadPct));
        if (show.Contains("gpu_temp")) gpu.Add(T(gpuLive?.TempC));
        if (show.Contains("gpu_hotspot")) gpu.Add("HS " + T(gpuLive?.HotspotTempC));
        if (show.Contains("gpu_clock")) gpu.Add(gpuLive?.CoreClockMHz is float c ? $"{c:0} MHz" : "— MHz");
        if (show.Contains("gpu_memclock")) gpu.Add(gpuLive?.MemoryClockMHz is float mc ? $"mem {mc:0} MHz" : "mem —");
        if (show.Contains("gpu_power")) gpu.Add(gpuLive?.PowerW is float w ? $"{w:0} W" : "— W");
        if (show.Contains("gpu_volt")) gpu.Add(gpuLive?.VoltageV is float v ? $"{v:0.000} V" : "— V");
        if (show.Contains("gpu_fan"))
            gpu.Add(gpuLive?.FanRpm is float rpm && rpm > 0 ? $"{rpm:0} RPM"
                  : gpuLive?.FanPct is float pct ? $"fan {pct:0}%" : "fan —");
        Line("GPU", gpu, HwName(gpuInfo != null ? HardwareNaming.ShortGpuName(gpuInfo.Name) : null));

        var vram = new List<string>();
        if (show.Contains("vram_used")) vram.Add(gpuLive?.VramUsedGb is double vu ? $"{vu:0.0} GB" : "—");
        if (show.Contains("vram_pct")) vram.Add(P(gpuLive?.VramPct));
        Line("VRAM", vram);

        var cpu = new List<string>();
        if (show.Contains("cpu_load")) cpu.Add(P(cpuLive.LoadPct));
        if (show.Contains("cpu_maxcore")) cpu.Add(cpuLive.BusiestCoreLoadPct is float mx ? $"núcleo {mx:0}%" : "núcleo —");
        if (show.Contains("cpu_temp")) cpu.Add(T(cpuLive.TempC));
        if (show.Contains("cpu_clock")) cpu.Add(cpuLive.AvgClockMHz is float ca ? $"{ca / 1000:0.00} GHz" : "— GHz");
        if (show.Contains("cpu_clockmax")) cpu.Add(cpuLive.MaxClockMHz is float cm ? $"pico {cm / 1000:0.00} GHz" : "pico —");
        if (show.Contains("cpu_power")) cpu.Add(cpuLive.PowerW is float cw && cw > 0 ? $"{cw:0} W" : "— W"); // 0 = sem leitura (precisa de administrador)
        Line("CPU", cpu, HwName(HardwareNaming.ShortCpuName(_cpuName)));

        var ram = new List<string>();
        if (show.Contains("ram_used")) ram.Add(memory is { } used ? $"{used.Used / 1073741824.0:0.0} GB" : "—");
        if (show.Contains("ram_pct")) ram.Add(P(memory?.UsedPercent));
        Line("RAM", ram, memory is { } capacity ? $"{Math.Round(capacity.Total / 1073741824.0):0} GB" : null);
        return lines;
    }
}
