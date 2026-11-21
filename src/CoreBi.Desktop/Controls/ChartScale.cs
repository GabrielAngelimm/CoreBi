namespace CoreBi.Desktop;

/// <summary>Cálculos de escala e formatação relativa compartilhados pelos gráficos temporais.</summary>
internal static class ChartScale
{
    public static double PowerStep(double peak) =>
        peak <= 80 ? 20 : peak <= 200 ? 50 : peak <= 400 ? 100 : 200;

    public static string RelativeAgo(int seconds) =>
        seconds <= 0 ? "agora" : seconds >= 60 ? $"há {seconds / 60} min {seconds % 60:00} s" : $"há {seconds} s";
}
