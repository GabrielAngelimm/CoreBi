namespace CoreBi.Engine.Games;

public sealed record OverlayMetric(string Key, string Group, string Label, bool Default);

public sealed record OverlayLine(string Label, IReadOnlyList<string> Parts, string? Detail = null);

public static class OverlayMetrics
{
    public const string DefaultCorner = "TL";
    public const double DefaultScale = 1.0;
    public const double MinScale = 0.5;
    public const double MaxScale = 3.0;
    public const double DefaultOpacity = 1.0;
    public const double MinOpacity = 0.3;
    public const double MaxOpacity = 1.0;

    public static readonly OverlayMetric[] All =
    {
        new("fps", "FPS", "FPS", true),
        new("frametime", "FPS", "Frametime (ms)", true),
        new("low1", "FPS", "1% low", true),
        new("graph", "FPS", "Gráfico de frametime", false),

        new("gpu_load", "GPU", "Uso", true),
        new("gpu_temp", "GPU", "Temperatura", true),
        new("gpu_hotspot", "GPU", "Hotspot", false),
        new("gpu_clock", "GPU", "Clock do núcleo", true),
        new("gpu_memclock", "GPU", "Clock da memória", false),
        new("gpu_power", "GPU", "Consumo", true),
        new("gpu_volt", "GPU", "Tensão do núcleo", false),
        new("gpu_fan", "GPU", "Ventoinha", false),

        new("vram_used", "VRAM", "Usada (GB)", true),
        new("vram_pct", "VRAM", "Uso (%)", true),

        new("cpu_load", "CPU", "Uso total", true),
        new("cpu_maxcore", "CPU", "Núcleo mais ocupado", false),
        new("cpu_temp", "CPU", "Temperatura", true),
        new("cpu_clock", "CPU", "Clock médio", true),
        new("cpu_clockmax", "CPU", "Clock máximo (núcleo)", false),
        new("cpu_power", "CPU", "Consumo", true),

        new("ram_used", "RAM", "Usada (GB)", true),
        new("ram_pct", "RAM", "Uso (%)", true),

        new("hwnames", "Outros", "Nome do hardware", true),
        new("game", "Outros", "Nome do jogo", false),
        new("clock", "Outros", "Hora", false),
    };

    public static HashSet<string> Defaults() => All.Where(m => m.Default).Select(m => m.Key).ToHashSet();

    public static string NormalizeCorner(string? corner) =>
        corner is "TL" or "TR" or "BL" or "BR" ? corner : DefaultCorner;

    public static double NormalizeScale(double scale) =>
        double.IsFinite(scale) && scale is >= MinScale and <= MaxScale ? scale : DefaultScale;

    public static double NormalizeOpacity(double opacity) =>
        double.IsFinite(opacity) && opacity is >= MinOpacity and <= MaxOpacity ? opacity : DefaultOpacity;

    // O diagnóstico PerfCap e a temperatura da VRAM (sem sensor confiável) saíram do overlay; seleções antigas mantêm os demais itens.
    public static List<string>? NormalizeItems(IEnumerable<string>? items) =>
        items?.Where(x => !string.IsNullOrWhiteSpace(x) && x is not ("gpu_perfcap" or "vram_temp"))
            .Distinct()
            .Take(100)
            .ToList();
}
