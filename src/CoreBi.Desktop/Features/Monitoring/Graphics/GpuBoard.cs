using System.Collections.ObjectModel;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Sensors;

namespace CoreBi.Desktop;

/// <summary>Uma leitura ao vivo da placa: número grande, medidor da fração da escala e complemento.</summary>
public sealed class GpuGauge : ObservableObject
{
    public GpuGauge(string key, string label, string? tip = null) { Key = key; Label = label; Tip = tip; }

    public string Key { get; }
    public string Label { get; }
    public string? Tip { get; }

    private string _value = "—", _unit = "", _sub = "", _tone = "";
    private bool _first;
    private double _max = 100;
    private double _fraction;

    public string Value { get => _value; set => Set(ref _value, value); }
    public string Unit { get => _unit; set => Set(ref _unit, value); }
    public string Sub { get => _sub; set => Set(ref _sub, value); }
    public string Tone { get => _tone; set => Set(ref _tone, value); }
    public bool First { get => _first; set => Set(ref _first, value); }
    public double Max { get => _max; set => Set(ref _max, value); }
    /// <summary>Quanto da escala (<see cref="Max"/>) a leitura ocupa, de 0 a 1, para o medidor.</summary>
    public double Fraction { get => _fraction; set => Set(ref _fraction, value); }
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
        // O limitador do driver só aparece quando segura a placa por calor ou por proteção do hardware (energia e ociosa são o normal)
        _core.Sub = live.ThermalThrottled ? "segurado pela temperatura" : live.HardwareThrottled ? "segurado pelo hardware" : "";
        _core.Tone = _core.Sub.Length > 0 ? "Warn" : "";
        _core.Unit = "MHz";
        _core.Max = Ceiling(core is null ? _coreCeiling : Math.Max(_coreCeiling, Peak("core")), 500);

        Push("mem", mem, gap);
        _mem.Value = mem is float mm ? $"{mm:N0}" : "—";
        _mem.Unit = "MHz";
        _mem.Max = Ceiling(Math.Max(_memCeiling, Peak("mem")), 500);

        Push("temp", temp, gap);
        _temp.Value = temp is float tt ? $"{tt:0}" : "—";
        _temp.Unit = "°C";
        _temp.Sub = hot is float hs ? $"ponto quente {hs:0} °C" : "";
        _temp.Tone = temp >= ThermalLimits.ChipCritC || hot >= ThermalLimits.JunctionCritC ? "Danger" : "";
        _temp.Max = 100;

        Push("power", power, gap);
        _power.Value = power is float p ? $"{p:0}" : "—";
        _power.Unit = "W";
        _power.Sub = power is float pp && limit is float pl ? $"{100 * pp / pl:0} % de {pl:0} W" : "";
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

        // A escala de cada medidor é o Max acima; a última amostra dá a fração (sem leitura, o medidor fica vazio)
        foreach (var g in All)
            g.Fraction = History(g.Key) is { Count: > 0 } h && h[^1] is float v && g.Max > 0 ? Math.Clamp(v / g.Max, 0, 1) : 0;
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
