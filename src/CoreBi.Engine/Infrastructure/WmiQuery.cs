using System.Management;

namespace CoreBi.Engine.Infrastructure;

internal static class WmiQuery
{
    public static void ForEach(string query, Action<ManagementBaseObject> action, string? scope = null, string? diagnosticOp = null)
    {
        try { ForEachOrThrow(query, action, scope); }
        catch (Exception ex)
        {
            if (diagnosticOp != null) DiagnosticLog.Write(diagnosticOp, ex);
        }
    }

    public static void ForEachOrThrow(string query, Action<ManagementBaseObject> action, string? scope = null)
    {
        using var searcher = scope == null
            ? new ManagementObjectSearcher(query)
            : new ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();
        foreach (ManagementBaseObject item in results)
        {
            using (item) action(item);
        }
    }

    public static List<T> Select<T>(string query, Func<ManagementBaseObject, T> map, string? scope = null, string? diagnosticOp = null)
    {
        var list = new List<T>();
        ForEach(query, item => list.Add(map(item)), scope, diagnosticOp);
        return list;
    }

    public static List<T> SelectOrThrow<T>(string query, Func<ManagementBaseObject, T> map, string? scope = null)
    {
        var list = new List<T>();
        ForEachOrThrow(query, item => list.Add(map(item)), scope);
        return list;
    }

    public static T? FirstOrDefault<T>(string query, Func<ManagementBaseObject, T> map, string? scope = null, string? diagnosticOp = null)
    {
        T? found = default;
        bool hasValue = false;
        ForEach(query, item =>
        {
            if (!hasValue)
            {
                found = map(item);
                hasValue = true;
            }
        }, scope, diagnosticOp);
        return found;
    }

    public static string? GetString(ManagementBaseObject item, string property)
    {
        try { return item[property]?.ToString()?.Trim(); }
        catch { return null; }
    }

    public static ulong GetUInt64(ManagementBaseObject item, string property)
    {
        try { return item[property] is { } v ? Convert.ToUInt64(v) : 0UL; }
        catch { return 0UL; }
    }

    public static uint GetUInt32(ManagementBaseObject item, string property)
    {
        try { return item[property] is { } v ? Convert.ToUInt32(v) : 0U; }
        catch { return 0U; }
    }

    public static int GetInt32(ManagementBaseObject item, string property)
    {
        try { return item[property] is { } v ? Convert.ToInt32(v) : 0; }
        catch { return 0; }
    }

    public static int? GetNullableInt32(ManagementBaseObject item, string property)
    {
        try { return item[property] is { } v ? Convert.ToInt32(v) : null; }
        catch { return null; }
    }

    public static long? GetNullableInt64(ManagementBaseObject item, string property)
    {
        try { return item[property] is { } v ? Convert.ToInt64(v) : null; }
        catch { return null; }
    }

    public static bool? GetNullableBool(ManagementBaseObject item, string property)
    {
        try { return item[property] is { } v ? Convert.ToBoolean(v) : null; }
        catch { return null; }
    }
}
