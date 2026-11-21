using System.Globalization;

namespace CoreBi.Engine.Infrastructure;

/// <summary>Firmware/driver release dates are calendar dates, not local instants.</summary>
public static class WmiCalendarDate
{
    public static bool TryParse(string? value, out DateTime date)
    {
        if (value is { Length: >= 8 }
            && DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;
        date = default;
        return false;
    }

    public static string Format(string? value) =>
        TryParse(value, out var date) ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "—";
}
