using LibreHardwareMonitor.Hardware;
using CoreBi.Engine.Games;
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

    /// <summary>Componentes na ordem do overlay: o que cada linha mede, glifo (todos na mesma cor).</summary>
    private static readonly (string Group, string Caption, string Icon, string Brush)[] OvGroups =
    {
        ("FPS", "Quadros por segundo e estabilidade", "\uEC4A", "Accent"),
        ("GPU", "Placa de vídeo principal", "", "Accent"),
        ("VRAM", "Memória da placa de vídeo", "\uE964", "Accent"),
        ("CPU", "Processador", "\uE950", "Accent"),
        ("RAM", "Memória do sistema", "\uEEA0", "Accent"),
        ("Outros", "Nomes e rodapé do overlay", "", "Accent"),
    };

    /// <summary>Itens que dependem da placa ou de permissão: a dica explica por que podem aparecer com travessão.</summary>
    private static readonly Dictionary<string, string> OvItemHints = new()
    {
        ["gpu_hotspot"] = "Depende da placa de vídeo.",
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
        ["termico"] = new() { "hwnames", "fps", "gpu_temp", "gpu_hotspot", "gpu_power", "gpu_fan", "vram_temp", "cpu_temp", "cpu_power", "cpu_clock", "clock" },
    };

    /// <summary>Monta "O que mostrar": uma linha por componente (glifo, nome, o que mede) com os itens em pílulas e a contagem.</summary>
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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var glyph = new TextBlock { Text = meta.Icon ?? "\uE946" };
            glyph.SetResourceReference(StyleProperty, "IconGlyph");
            glyph.FontSize = 14;
            var tile = new Border { Child = glyph, VerticalAlignment = VerticalAlignment.Top };
            tile.SetResourceReference(StyleProperty, "IconTile");
            grid.Children.Add(tile);

            var name = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            name.Children.Add(new TextBlock { Text = group, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            var caption = new TextBlock { Text = meta.Caption ?? "", FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            name.Children.Add(caption);
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

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
                glyph.SetResourceReference(TextBlock.ForegroundProperty, on > 0 ? meta.Brush ?? "Accent" : "Faint");
            }

            var wrap = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            foreach (var m in items)
            {
                var chip = new CheckBox { Content = m.Label, Tag = m.Key, IsChecked = OvShow.Contains(m.Key) };
                chip.SetResourceReference(StyleProperty, "OptionChip");
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

    // ------------------------------------------------------------------ leitura dos sensores

    internal SensorRow? GpuRow(SensorType type, string name, bool startsWith = false) =>
        _gpus.Count == 0 ? null : _monitor.Rows.FirstOrDefault(r => r.Hardware == _gpus[0].Name && r.Type == type &&
                                                                   (startsWith ? r.Name.StartsWith(name) : r.Name == name) && r.RawValue != null);

    internal uint? MainGpuClock(int nvmlType, string lhmName)
    {
        if (_gpus.Count > 0 && _gpus[0].NvmlHandle != IntPtr.Zero && Nvml.Clock(_gpus[0].NvmlHandle, nvmlType) is uint c) return c;
        return GpuRow(SensorType.Clock, lhmName)?.RawValue is float v && v > 0 ? (uint)v : null;
    }

    /// <summary>VRAM usada e total em GB: NVML quando disponível, senão LibreHardwareMonitor (MB).</summary>
    internal (double Used, double Total)? MainGpuVram()
    {
        if (_gpus.Count == 0) return null;
        if (_gpus[0].NvmlHandle != IntPtr.Zero && Nvml.MemoryInfo(_gpus[0].NvmlHandle) is Nvml.Memory m && m.Total > 0)
            return (m.Used / 1073741824.0, m.Total / 1073741824.0);
        if (GpuRow(SensorType.SmallData, "GPU Memory Used")?.RawValue is float u && GpuRow(SensorType.SmallData, "GPU Memory Total")?.RawValue is float t && t > 0)
            return (u / 1024.0, t / 1024.0);
        return null;
    }

    internal (float? Avg, float? Max) CpuClocks()
    {
        var cores = _monitor.Rows.Where(r => r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Clock &&
                                             r.Name.StartsWith("Core #") && !r.Name.Contains("Effective") && r.RawValue > 0)
                                 .Select(r => r.RawValue!.Value).ToList();
        return cores.Count > 0 ? (cores.Average(), cores.Max()) : (null, null);
    }

    /// <summary>Nome do hardware para o rótulo: vazio vira nada (a linha volta a mostrar só "CPU"/"GPU") e nome longo é cortado.</summary>
    internal static string? HwName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        return name.Length <= 20 ? name : name[..19].TrimEnd() + "…";
    }

    /// <summary>Monta as linhas GPU / VRAM / CPU / RAM só com o que foi escolhido; linha vazia não aparece.</summary>
    internal List<OverlayLine> BuildOverlayLines(FrameStats? f)
    {
        var show = OvShow;
        var memory = MemoryInfo.ReadPhysicalUsage();
        var s = BottleneckAnalyzer.Sample(_monitor, _gpus.FirstOrDefault(), f, memory);
        var lines = new List<OverlayLine>();
        string T(float? v) => v is float x ? $"{x:0} °C" : "—";
        string P(double? v) => v is double x ? $"{x:0}%" : "—";
        bool names = show.Contains("hwnames");
        void Line(string label, List<string> parts, string? detail = null) { if (parts.Count > 0) lines.Add(new OverlayLine(label, parts, names ? detail : null)); }

        var gpu = new List<string>();
        if (show.Contains("gpu_load")) gpu.Add(P(s.GpuLoad));
        if (show.Contains("gpu_temp")) gpu.Add(T(s.GpuTemp));
        if (show.Contains("gpu_hotspot")) gpu.Add("HS " + T(GpuRow(SensorType.Temperature, "GPU Hot Spot")?.RawValue));
        if (show.Contains("gpu_clock")) gpu.Add(MainGpuClock(Nvml.ClockGraphics, "GPU Core") is uint c ? $"{c} MHz" : "— MHz");
        if (show.Contains("gpu_memclock")) gpu.Add(MainGpuClock(Nvml.ClockMem, "GPU Memory") is uint mc ? $"mem {mc} MHz" : "mem —");
        if (show.Contains("gpu_power")) gpu.Add(MainGpuPower() is float w ? $"{w:0} W" : "— W");
        if (show.Contains("gpu_volt")) gpu.Add(GpuRow(SensorType.Voltage, "GPU Core Voltage")?.RawValue is float v ? $"{v:0.000} V" : "— V");
        if (show.Contains("gpu_fan"))
            gpu.Add(GpuRow(SensorType.Fan, "GPU Fan", true)?.RawValue is float rpm && rpm > 0 ? $"{rpm:0} RPM"
                  : GpuRow(SensorType.Control, "GPU Fan", true)?.RawValue is float pct ? $"fan {pct:0}%" : "fan —");
        Line("GPU", gpu, HwName(_gpus.Count > 0 ? GameCatalog.ShortGpuName(_gpus[0].Name) : null));

        var vram = new List<string>();
        var vr = MainGpuVram();
        if (show.Contains("vram_used")) vram.Add(vr is { } a ? $"{a.Used:0.0} GB" : "—");
        if (show.Contains("vram_pct")) vram.Add(P(s.VramPct));
        if (show.Contains("vram_temp")) vram.Add(T(GpuRow(SensorType.Temperature, "GPU Memory")?.RawValue));
        Line("VRAM", vram);

        var cpu = new List<string>();
        var (avg, max) = CpuClocks();
        if (show.Contains("cpu_load")) cpu.Add(P(s.CpuTotal));
        if (show.Contains("cpu_maxcore")) cpu.Add(s.CpuMaxCore is float mx ? $"núcleo {mx:0}%" : "núcleo —");
        if (show.Contains("cpu_temp")) cpu.Add(T(s.CpuTemp));
        if (show.Contains("cpu_clock")) cpu.Add(avg is float ca ? $"{ca / 1000:0.00} GHz" : "— GHz");
        if (show.Contains("cpu_clockmax")) cpu.Add(max is float cm ? $"pico {cm / 1000:0.00} GHz" : "pico —");
        if (show.Contains("cpu_power")) cpu.Add(_monitor.CpuPower is float cw && cw > 0 ? $"{cw:0} W" : "— W"); // 0 = sem leitura (precisa de administrador)
        Line("CPU", cpu, HwName(GameCatalog.ShortCpuName(_cpuName)));

        var ram = new List<string>();
        if (show.Contains("ram_used")) ram.Add(memory is { } used ? $"{used.Used / 1073741824.0:0.0} GB" : "—");
        if (show.Contains("ram_pct")) ram.Add(P(memory?.UsedPercent));
        Line("RAM", ram, memory is { } capacity ? $"{Math.Round(capacity.Total / 1073741824.0):0} GB" : null);
        return lines;
    }
}
