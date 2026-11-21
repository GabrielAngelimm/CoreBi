using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Sensors;

namespace CoreBi.Desktop;

public sealed record GpuReadout(string Label, string Value, string Unit, string? Tip, bool First);
public sealed record GpuSpecRow(string Key, string Value, string? Note = null, string? Tip = null);
public sealed record GpuSpecGroup(string Title, IReadOnlyList<GpuSpecRow> Rows);
public sealed record GpuFact(string Label, string Value, bool Warn, string? Tip);

/// <summary>Um instrumento ao vivo da placa: leitura grande, complemento e histórico curto no canal Gpu.</summary>
public sealed class GpuGauge : ObservableObject
{
    public GpuGauge(string key, string label, string? tip = null) { Key = key; Label = label; Tip = tip; }

    public string Key { get; }
    public string Label { get; }
    public string? Tip { get; }

    private string _value = "—", _unit = "", _sub = "", _tone = "";
    private bool _first;
    private double _max = 100;
    private IReadOnlyList<float?> _samples = Array.Empty<float?>();

    public string Value { get => _value; set => Set(ref _value, value); }
    public string Unit { get => _unit; set => Set(ref _unit, value); }
    public string Sub { get => _sub; set => Set(ref _sub, value); }
    public string Tone { get => _tone; set => Set(ref _tone, value); }
    public bool First { get => _first; set => Set(ref _first, value); }
    public double Max { get => _max; set => Set(ref _max, value); }
    public IReadOnlyList<float?> Samples { get => _samples; set { _samples = value; OnPropertyChanged(); } }
}

/// <summary>
/// Ficha técnica da página Placa de vídeo, montada a partir das seções do motor (GpuInfoCollector): rótulos curtos, nenhuma linha
/// sem valor e nenhum dado repetido entre grupos. A placa de identificação resume (pode repetir um dado de um grupo).
/// </summary>
internal static class GpuSheet
{
    /// <summary>Primeiro valor útil de cada chave, em todas as seções (as linhas "—" e "Sem dados" ficam de fora).</summary>
    public static Dictionary<string, string> Flatten(GpuInfo g)
    {
        var d = new Dictionary<string, string>();
        foreach (var section in g.Sections)
            foreach (var row in section.Children)
                if (row.Value.Length > 0 && row.Value != "—" && row.Header != "Sem dados") d.TryAdd(row.Header, row.Value);
        return d;
    }

    private static string? V(Dictionary<string, string> d, string key) => d.TryGetValue(key, out var v) ? v : null;

    /// <summary>"2475 MHz" → ("2475", "MHz"); sem espaço, a unidade fica vazia.</summary>
    private static (string Value, string Unit) Split(string s)
    {
        int sp = s.LastIndexOf(' ');
        return sp > 0 ? (s[..sp], s[(sp + 1)..]) : (s, "");
    }

    /// <summary>Chip, arquitetura e fabricante da placa, numa frase curta; vazio se o banco de especificações não conhece a GPU.</summary>
    public static string Subtitle(Dictionary<string, string> d)
    {
        var parts = new List<string>();
        if (V(d, "Chip") is { } chip) parts.Add($"chip {chip}");
        if (V(d, "Arquitetura") is { } arch) parts.Add($"arquitetura {arch}");
        if (V(d, "Fabricante da placa") is { } board) parts.Add($"placa {board}");
        if (parts.Count == 0) return "";
        string s = string.Join(", ", parts);
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    public static List<GpuReadout> Readouts(GpuInfo g, Dictionary<string, string> d)
    {
        var list = new List<(string Label, string Value, string Unit, string? Tip)>();
        if (g.DedicatedVramBytes > 0)
        {
            var (v, u) = Split(Fmt.Vram(g.DedicatedVramBytes));
            string? type = V(d, "Tipo");
            bool system = type?.StartsWith("Memória do sistema") == true;
            string label = system ? "Memória dedicada" : type is { } t && (t.StartsWith("GDDR") || t.StartsWith("HBM")) ? $"Memória {t}" : "Memória";
            list.Add((label, v, u, system ? type : null));
        }
        foreach (var key in new[] { "CUDA cores", "Stream processors" })
            if (V(d, key) is { } cores) { list.Add((key, cores, "", "Os núcleos que desenham a imagem.")); break; }
        if (V(d, "Boost (referência)") is { } boost)
        {
            var (v, u) = Split(boost);
            list.Add(("Clock boost", v, u, "Valor de referência da fabricante do chip."));
        }
        else if (V(d, "Máximo (curva V/F)") is { } max)
        {
            var (v, u) = Split(max);
            list.Add(("Clock máximo", v, u, "O mais alto da curva de tensão e frequência da placa."));
        }
        if (V(d, "Limite atual") is { } limit)
        {
            var (v, u) = Split(limit);
            list.Add(("Limite de energia", v, u, "O máximo que a placa pode puxar agora."));
        }
        else if (V(d, "TDP (referência)") is { } tdp)
        {
            var (v, u) = Split(tdp);
            list.Add(("TDP", v, u, "Consumo de referência da fabricante do chip."));
        }
        return list.Select((r, i) => new GpuReadout(r.Label, r.Value, r.Unit, r.Tip, i == 0)).ToList();
    }

    /// <summary>livePcie: o driver informa o link PCIe em uso ao vivo (faixa sob os instrumentos), então a ficha não o repete.</summary>
    public static List<GpuSpecGroup> Groups(GpuInfo g, Dictionary<string, string> d, bool livePcie)
    {
        var groups = new List<GpuSpecGroup>();
        void Group(string title, params GpuSpecRow?[] rows)
        {
            var kept = rows.Where(r => r != null).Cast<GpuSpecRow>().ToList();
            if (kept.Count > 0) groups.Add(new(title, kept));
        }
        GpuSpecRow? R(string key, string? label = null, string? note = null, string? tip = null) =>
            V(d, key) is { } v ? new(label ?? key, v, note, tip) : null;

        if (V(d, "Falha ao ler") is { } error) Group("Leitura", new GpuSpecRow("Falha ao ler", error));

        // O nome da placa é o título da página (o completo na dica), então a ficha não o repete como "Modelo".
        Group("Identificação",
            R("Fabricante da placa"),
            R("Chip"),
            R("Arquitetura"),
            R("Processo"),
            R("Área do die"),
            R("Transistores"),
            R("Lançamento"),
            R("Revisão"),
            R("Device ID", "ID do dispositivo", tip: "Fabricante e modelo no barramento PCI (VEN DEV · SUBSYS)."));

        Group("Unidades de processamento",
            R("CUDA cores", tip: "Os núcleos que desenham a imagem."),
            R("Stream processors", tip: "Os núcleos que desenham a imagem."),
            R("SMs", tip: "Blocos que agrupam os núcleos."),
            R("Compute units", tip: "Blocos que agrupam os núcleos."),
            R("Tensor cores", tip: "Fazem as contas de IA, como o DLSS."),
            R("RT cores", tip: "Fazem o ray tracing."),
            R("Ray accelerators", tip: "Fazem o ray tracing."),
            R("ROPs / TMUs"),
            R("Pixel fillrate"),
            R("Texture fillrate"),
            R("FP32 (teórico)", "FP32", "teórico"));

        Group("Memória",
            R("Tamanho"),
            R("Reservada (dedicada)", "Reservada"),
            R("Tipo"),
            R("Barramento", tip: "Quantos bits a placa lê da VRAM de uma vez."),
            R("Clock da memória"),
            R("Largura de banda"),
            R("Compartilhada do sistema", "Compartilhada da RAM", tip: "Quanto da RAM o Windows deixa a placa usar quando a VRAM acaba. É bem mais lenta que a VRAM."),
            V(d, "Resizable BAR") is { } bar
                ? new GpuSpecRow("Resizable BAR", bar.StartsWith("Ativado") ? "Ativado" : bar, null, bar.StartsWith("Ativado (") ? bar[9..^1] : null)
                : null);

        Group("Clocks e energia",
            R("Base (referência)", "Clock base", "referência"),
            R("Boost (referência)", "Clock boost", "referência"),
            R("Máximo (curva V/F)", "Clock máximo", tip: "O mais alto da curva de tensão e frequência da placa."),
            R("Video engine máx.", "Clock de vídeo", "máximo"),
            R("TDP (referência)", "TDP", "referência"),
            R("Limite atual", "Limite de energia", tip: "O máximo que a placa pode puxar agora."),
            R("Limite padrão da placa", "Limite padrão"),
            R("Faixa ajustável"));

        Group("Barramento",
            R("Interface"),
            livePcie || V(d, "Em uso agora") is not { } now ? null
                : new GpuSpecRow("Em uso", now.Split(" (")[0], null, "Em repouso a conexão cai para economizar energia e volta ao máximo em jogo."),
            R("Localização"));

        Group(V(d, "BIOS (VBIOS)") != null ? "Driver e BIOS" : "Driver",
            R("Versão do driver", "Versão"),
            R("Data do driver", "Data"),
            R("Assinatura digital", "Assinatura"),
            R("BIOS (VBIOS)", "VBIOS", tip: "Firmware da própria placa de vídeo."),
            R("Versão CUDA", "CUDA"),
            R("Compute capability"));

        Group("Tecnologias",
            R("DirectX"),
            V(d, "APIs") is { } apis ? new GpuSpecRow("APIs", apis.Replace(" · ", ", ")) : null,
            V(d, "Recursos da arquitetura") is { } feats ? new GpuSpecRow("Recursos", feats.Replace(" · ", ", ")) : null);

        return groups;
    }

    /// <summary>Leituras do driver (NVML) com rótulos curtos; o consumo sobre o limite já está no instrumento de consumo.</summary>
    public static List<GpuFact> Facts(GpuInfo g)
    {
        var facts = new List<GpuFact>();
        foreach (var (key, value) in g.LiveExtras())
        {
            switch (key)
            {
                case "Estado de desempenho":
                    facts.Add(new(key, value, false, "P0 é o desempenho máximo; números maiores são estados de economia."));
                    break;
                case "PerfCap (limitador)":
                    bool warn = value.Contains("Térmico") || value.Contains("HW slowdown") || value.Contains("Power brake");
                    facts.Add(new("Limitador do clock", value, warn, "O que o driver diz que está segurando o clock agora."));
                    break;
                case "Consumo / limite":
                    break;
                case "PCIe em uso":
                    facts.Add(new(key, value, false, "Em repouso a conexão cai para economizar energia e volta ao máximo em jogo."));
                    break;
                default:
                    facts.Add(new(key, value, false, null));
                    break;
            }
        }
        return facts;
    }
}

/// <summary>
/// Os instrumentos ao vivo da placa escolhida. Só lê o driver e guarda amostras quando a página está à vista (o CoreBi fica aberto
/// em jogos); o tempo em que ela ficou escondida entra no histórico como vão, sem ligar os pontos.
/// </summary>
internal sealed class GpuBoard
{
    public const int Window = 60; // segundos de histórico

    private readonly GpuInfo _gpu;
    private readonly double _coreCeiling, _memCeiling;
    private readonly Dictionary<string, List<float?>> _history = new();
    private readonly HashSet<string> _seen = new();
    private DateTime _last = DateTime.MinValue;
    private bool _sampledWithSensors;

    /// <summary>Já houve leitura com os sensores abertos e nenhum instrumento respondeu: a página diz isso uma vez.</summary>
    public bool NoSensors => _sampledWithSensors && Gauges.Count == 0;

    public ObservableCollection<GpuGauge> Gauges { get; } = new();

    private readonly GpuGauge _load = new("load", "Uso");
    private readonly GpuGauge _core = new("core", "Clock do núcleo");
    private readonly GpuGauge _mem;
    private readonly GpuGauge _temp = new("temp", "Temperatura");
    private readonly GpuGauge _power = new("power", "Consumo");
    private readonly GpuGauge _vram = new("vram", "VRAM em uso", "Memória da placa de vídeo ocupada agora.");
    private readonly GpuGauge _fan = new("fan", "Ventoinha");
    private GpuGauge[] All => new[] { _load, _core, _mem, _temp, _power, _vram, _fan };

    public GpuBoard(GpuInfo gpu, Dictionary<string, string> spec)
    {
        _gpu = gpu;
        _mem = new("mem", "Clock da memória", gpu.HasVendorTelemetry ? "Como o driver informa. Em GDDR6 e GDDR6X a taxa efetiva é o dobro." : null);
        static double Mhz(string? s) => s != null && double.TryParse(s.Split(' ')[0], out double v) ? v : 0;
        _coreCeiling = Math.Max(Mhz(spec.GetValueOrDefault("Máximo (curva V/F)")), Mhz(spec.GetValueOrDefault("Boost (referência)")));
        _memCeiling = Mhz(spec.GetValueOrDefault("Clock da memória"));
    }

    private List<float?> History(string key) => _history.TryGetValue(key, out var h) ? h : _history[key] = new List<float?>();

    public void Sample(SensorMonitor m)
    {
        var now = DateTime.Now;
        // Segundos em que a página ficou escondida: viram vão no histórico.
        int gap = _last == DateTime.MinValue ? 0 : Math.Clamp((int)Math.Round((now - _last).TotalSeconds) - 1, 0, Window);
        _last = now;
        if (m.IsOpen) _sampledWithSensors = true;

        var live = GpuMonitor.ReadLive(_gpu, m.Snapshot());
        float? load = live.LoadPct;
        float? core = live.CoreClockMHz;
        float? mem = live.MemoryClockMHz;
        float? temp = live.TempC;
        float? hot = live.HotspotTempC;
        float? power = live.PowerW;
        float? limit = live.PowerLimitW;
        (double Used, double Total)? vram = live.VramUsedGb is double vu && live.VramTotalGb is double vt2 ? (vu, vt2) : null;
        float? rpm = live.FanRpm;
        float? fanPct = live.FanPct;

        Push("load", load, gap);
        _load.Value = load is float l ? $"{l:0}" : "—";
        _load.Unit = "%";
        _load.Max = 100;

        Push("core", core, gap);
        _core.Value = core is float cc ? $"{cc:N0}" : "—";
        _core.Unit = "MHz";
        _core.Max = Ceiling(core is null ? _coreCeiling : Math.Max(_coreCeiling, Peak("core")), 500);

        Push("mem", mem, gap);
        _mem.Value = mem is float mm ? $"{mm:N0}" : "—";
        _mem.Unit = "MHz";
        _mem.Max = Ceiling(Math.Max(_memCeiling, Peak("mem")), 500);

        Push("temp", temp, gap);
        _temp.Value = temp is float tt ? $"{tt:0}" : "—";
        _temp.Unit = "°C";
        _temp.Sub = hot is float hs ? $"ponto mais quente {hs:0} °C" : "";
        _temp.Tone = temp >= 95 || hot >= 105 ? "Danger" : "";
        _temp.Max = 100;

        Push("power", power, gap);
        _power.Value = power is float p ? $"{p:0}" : "—";
        _power.Unit = "W";
        _power.Sub = power is float pp && limit is float pl ? $"{100 * pp / pl:0} % do limite de {pl:0} W" : "";
        _power.Max = Ceiling(Math.Max(limit ?? 0, Peak("power") * 1.25), 50);

        Push("vram", vram is { } vv ? (float)vv.Used : null, gap);
        _vram.Value = vram is { } va ? $"{va.Used:0.0}" : "—";
        _vram.Unit = "GB";
        _vram.Sub = vram is { } vb ? $"{100 * vb.Used / vb.Total:0} % de {vb.Total:0} GB" : "";
        _vram.Max = vram is { } vt ? vt.Total : Math.Max(1, Peak("vram"));

        if (rpm != null)
        {
            Push("fan", rpm, gap);
            _fan.Value = $"{rpm:N0}";
            _fan.Unit = "RPM";
            _fan.Sub = fanPct is float fp ? $"{fp:0} % da velocidade" : "";
            _fan.Max = Ceiling(Math.Max(1000, Peak("fan") * 1.25), 500);
        }
        else
        {
            Push("fan", fanPct, gap);
            _fan.Value = fanPct is float fp ? $"{fp:0}" : "—";
            _fan.Unit = fanPct != null ? "%" : "";
            _fan.Sub = "";
            _fan.Max = 100;
        }

        foreach (var g in All) g.Samples = History(g.Key).ToArray();
        UpdateShown();
    }

    private static double Ceiling(double v, double step) => Math.Max(step, Math.Ceiling(v / step) * step);

    private double Peak(string key) => History(key).Where(v => v != null).Select(v => (double)v!.Value).DefaultIfEmpty(0).Max();

    private void Push(string key, float? value, int gap)
    {
        var h = History(key);
        for (int i = 0; i < gap; i++) h.Add(null);
        h.Add(value);
        if (value != null) _seen.Add(key);
        if (h.Count > Window) h.RemoveRange(0, h.Count - Window);
    }

    /// <summary>Instrumento que nunca teve leitura não aparece (placa sem sensor de ventoinha, consumo ou clock).</summary>
    private void UpdateShown()
    {
        var shown = All.Where(g => _seen.Contains(g.Key)).ToList();
        if (shown.Select(g => g.Key).SequenceEqual(Gauges.Select(g => g.Key))) return;
        Gauges.Clear();
        for (int i = 0; i < shown.Count; i++)
        {
            shown[i].First = i == 0;
            Gauges.Add(shown[i]);
        }
    }
}

/// <summary>
/// Histórico curto de um instrumento da placa de vídeo: área chapada e traço no canal Gpu sobre uma linha de base, sem eixos.
/// Amostras nulas (sem leitura ou página escondida) interrompem o traço. Sem pulso: são sete instrumentos lado a lado e o app fica
/// aberto em jogos, então nada se mexe além do próprio valor.
/// </summary>
public sealed class GpuTrace : InstrumentBase
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(nameof(Samples), typeof(IReadOnlyList<float?>), typeof(GpuTrace),
        new FrameworkPropertyMetadata(Array.Empty<float?>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaxProperty = Number(nameof(Max), typeof(GpuTrace), 100);

    public IReadOnlyList<float?> Samples { get => (IReadOnlyList<float?>)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public double Max { get => (double)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }

    public GpuTrace()
    {
        SetResourceReference(ChannelBrushProperty, "Gpu");
        Height = 40;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 10) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        double baseY = Math.Round(h) - 0.5;
        dc.DrawLine(Stroke(TrackBrush, 1), new Point(0, baseY), new Point(w, baseY));

        var samples = Samples ?? Array.Empty<float?>();
        double max = double.IsNaN(Max) || Max <= 0 ? 1 : Max;
        double X(int i) => (w - 3) * (i + GpuBoard.Window - samples.Count) / (GpuBoard.Window - 1);
        double Y(double v) => 2 + (baseY - 3) * (1 - Math.Clamp(v / max, 0, 1));

        var fill = Alpha(ChannelBrush, 0x24);
        var pen = new Pen(ChannelBrush, 1.5) { LineJoin = PenLineJoin.Round };
        pen.Freeze();

        var run = new List<Point>();
        void Flush()
        {
            if (run.Count >= 2)
            {
                dc.DrawGeometry(fill, null, CreateAreaGeometry(run, baseY));
                dc.DrawGeometry(null, pen, CreatePolylineGeometry(run));
            }
            run.Clear();
        }
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i] is float v) run.Add(new Point(X(i), Y(v)));
            else Flush();
        }
        Flush();
        if (samples.Count > 0 && samples[^1] is float last)
            dc.DrawEllipse(ChannelBrush, null, new Point(X(samples.Count - 1), Y(last)), 2.5, 2.5);
    }
}
