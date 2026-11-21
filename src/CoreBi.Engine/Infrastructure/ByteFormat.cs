using System.Globalization;

namespace CoreBi.Engine.Infrastructure;

internal static class ByteFormat
{
    public const double BytesPerGiB = 1024.0 * 1024 * 1024;
    public const double BytesPerMiB = 1024.0 * 1024;

    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Binary(double bytes, bool compactLarge = false)
    {
        if (bytes <= 0 || !double.IsFinite(bytes)) return "—";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        string fmt = compactLarge && value >= 100 ? "0" : "0.#";
        return $"{value.ToString(fmt, PtBr)} {units[i]}";
    }

    public static string Decimal(ulong bytes)
    {
        if (bytes == 0) return "";
        double gb = bytes / 1e9;
        return gb >= 1000
            ? $"{(gb / 1000).ToString("0.##", PtBr)} TB"
            : $"{gb.ToString("0", PtBr)} GB";
    }

    /// <summary>
    /// Arredonda a VRAM dedicada para o tamanho comercial em GB quando o firmware/driver reserva uma fração pequena
    /// (ex.: 15,9 GB utilizáveis → 16 GB).
    /// </summary>
    public static double CommercialVramGb(ulong bytes) =>
        bytes == 0 ? 0 : Math.Ceiling(bytes / (double)(1UL << 30) - 0.15);
}
