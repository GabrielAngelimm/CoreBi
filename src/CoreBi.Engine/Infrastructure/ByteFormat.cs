using System.Globalization;

namespace CoreBi.Engine.Infrastructure;

public static class ByteFormat
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Potências de 1024 com uma casa: 8 GB, 15,9 GB, 512 MB.</summary>
    public static string Binary(double bytes)
    {
        if (bytes <= 0 || !double.IsFinite(bytes)) return "—";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value.ToString("0.#", PtBr)} {units[i]}";
    }

    /// <summary>Potências de 1000 com uma casa, como o fabricante anuncia o disco: 500,1 GB, 2 TB.</summary>
    public static string Decimal(ulong bytes)
    {
        if (bytes == 0) return "";
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        while (value >= 1000 && i < units.Length - 1) { value /= 1000; i++; }
        return $"{value.ToString("0.#", PtBr)} {units[i]}";
    }

    /// <summary>
    /// Arredonda a VRAM dedicada para o tamanho comercial em GB quando o firmware/driver reserva uma fração pequena
    /// (ex.: 15,9 GB utilizáveis → 16 GB).
    /// </summary>
    public static double CommercialVramGb(ulong bytes) =>
        bytes == 0 ? 0 : Math.Ceiling(bytes / (double)(1UL << 30) - 0.15);
}
