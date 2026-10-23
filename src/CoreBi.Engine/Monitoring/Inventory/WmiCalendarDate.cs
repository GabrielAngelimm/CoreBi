using System.Globalization;
namespace CoreBi.Engine.Monitoring.Inventory;

/// <summary>Firmware/driver release dates are calendar dates, not local instants.</summary>
public static class WmiCalendarDate
{
    public static string Format(string? value) => value is { Length: >= 8 }
        && DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "—";
}
