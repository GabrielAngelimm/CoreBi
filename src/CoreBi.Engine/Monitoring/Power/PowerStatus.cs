using System.Management;
using System.Runtime.InteropServices;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Health;

namespace CoreBi.Engine.Monitoring.Power;

/// <summary>
/// Uma bateria como o Windows informa (WMI). Capacidades em Wh, taxa em W (positiva carregando, negativa descarregando).
/// Nulo onde o fabricante não informa: nada é estimado além do que está marcado como estimativa (ToFullMin).
/// </summary>
public sealed record BatteryState(
    string Title, string? Manufacturer, string? Model, string? Chemistry,
    int? Percent, string? StatusText, bool Charging, bool Discharging,
    double? DesignWh, double? FullWh, double? RemainingWh, double? WearPct, long? Cycles,
    double? Volts, double? RateW, int? RuntimeMin, int? ToFullMin,
    HealthVerdict Verdict, string VerdictText, string Reason, bool IsUps);

public sealed record PowerSnapshot(bool? OnAc, bool BatterySaver, IReadOnlyList<BatteryState> Batteries, bool UpsPresent);

public static class PowerStatus
{
    private const long UnknownRuntimeMinutes = 71_582_788;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    // Layout fixo de SYSTEM_POWER_CAPABILITIES desde o Windows 8; UpsPresent fica no byte 12.
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct SystemPowerCapabilities
    {
        [FieldOffset(12)] public byte UpsPresent;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    [DllImport("powrprof.dll")][return: MarshalAs(UnmanagedType.U1)] private static extern bool GetPwrCapabilities(out SystemPowerCapabilities capabilities);

    public static (bool? OnAc, int? Percent, bool Saver) Line()
    {
        try
        {
            if (!GetSystemPowerStatus(out var s)) return (null, null, false);
            bool? ac = s.ACLineStatus switch { 0 => false, 1 => true, _ => null };
            int? pct = s.BatteryLifePercent <= 100 && (s.BatteryFlag & 128) == 0 ? s.BatteryLifePercent : null;
            return (ac, pct, (s.SystemStatusFlag & 1) != 0);
        }
        catch (Exception ex) { DiagnosticLog.Write("power.line", ex); return (null, null, false); }
    }

    public static PowerSnapshot Read()
    {
        var (ac, _, saver) = Line();
        bool ups = UpsPresent();
        return new PowerSnapshot(ac, saver, ReadBatteries(ups), ups);
    }

    private static bool UpsPresent()
    {
        try { return GetPwrCapabilities(out var caps) && caps.UpsPresent != 0; }
        catch (Exception ex) { DiagnosticLog.Write("power.capabilities", ex); return false; }
    }

    private static List<Dictionary<string, object?>> QueryAll(string scope, string className) =>
        WmiQuery.Select("SELECT * FROM " + className, item =>
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyData p in item.Properties) d[p.Name] = p.Value;
            return d;
        }, scope);

    private static Dictionary<string, object?>? Match(List<Dictionary<string, object?>> list, string? instanceName, int index)
    {
        if (!string.IsNullOrEmpty(instanceName))
        {
            var byName = list.FirstOrDefault(d => string.Equals(Str(d, "InstanceName"), instanceName, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }
        return index < list.Count ? list[index] : null;
    }

    private static List<BatteryState> ReadBatteries(bool upsPresent)
    {
        var list = new List<BatteryState>();
        var win = QueryAll(@"root\cimv2", "Win32_Battery");
        var stat = QueryAll(@"root\WMI", "BatteryStaticData");
        var full = QueryAll(@"root\WMI", "BatteryFullChargedCapacity");
        var cyc = QueryAll(@"root\WMI", "BatteryCycleCount");
        var live = QueryAll(@"root\WMI", "BatteryStatus");
        int count = Math.Max(win.Count, stat.Count);
        for (int i = 0; i < count; i++)
        {
            var w = i < win.Count ? win[i] : null;
            var st = i < stat.Count ? stat[i] : null;
            string? inst = Str(st, "InstanceName");
            var fu = Match(full, inst, i);
            var cy = Match(cyc, inst, i);
            var lv = Match(live, inst, i);
            // Nobreak USB aparece no Win32_Battery, mas não tem os dados de bateria ACPI do root\WMI.
            bool ups = upsPresent && st == null && fu == null;
            long? design = Long(st, "DesignedCapacity");
            long? fullCap = Long(fu, "FullChargedCapacity");
            long? remaining = Long(lv, "RemainingCapacity");
            long? cycles = Long(cy, "CycleCount");
            if (cycles == 0) cycles = null; // muitos fabricantes não informam (vem 0)
            long? pct = Long(w, "EstimatedChargeRemaining");
            bool charging = Bool(lv, "Charging") == true;
            bool discharging = Bool(lv, "Discharging") == true;
            long? cr = Long(lv, "ChargeRate");
            long? dr = Long(lv, "DischargeRate");
            long? mv = Long(lv, "Voltage");
            long? run = Long(w, "EstimatedRunTime");

            double? wear = design is > 0 && fullCap is > 0 ? Math.Max(0, 100.0 * (1 - (double)fullCap.Value / design.Value)) : null;
            double? rate = charging && cr is > 0 ? cr / 1000.0 : discharging && dr is > 0 ? -dr / 1000.0 : null;
            // Até carregar: o que falta da capacidade máxima atual dividido pela potência de carga (estimativa linear).
            int? toFull = charging && cr is > 0 && fullCap is > 0 && remaining is >= 0 && fullCap > remaining
                ? (int)Math.Round((fullCap.Value - remaining.Value) * 60.0 / cr.Value) : null;

            var (verdict, label, reason) = EvaluateHealth(wear, cycles);
            string status = MapStatus(Long(w, "BatteryStatus"));
            list.Add(new BatteryState(
                Title: ups ? "Nobreak" : count > 1 ? $"Bateria {i + 1}" : "Bateria",
                Manufacturer: Str(st, "ManufactureName"),
                Model: Str(st, "DeviceName") ?? Str(w, "Name"),
                Chemistry: MapChemistry(Long(w, "Chemistry")) is var ch && ch != "—" ? ch : null,
                Percent: pct is >= 0 and <= 100 ? (int)pct.Value : null,
                StatusText: status != "—" ? status : charging ? "Carregando" : discharging ? "Descarregando" : null,
                Charging: charging,
                Discharging: discharging,
                DesignWh: design is > 0 ? design / 1000.0 : null,
                FullWh: fullCap is > 0 ? fullCap / 1000.0 : null,
                RemainingWh: remaining is >= 0 && (fullCap is > 0 || design is > 0) ? remaining / 1000.0 : null,
                WearPct: wear,
                Cycles: cycles,
                Volts: mv is > 0 ? mv / 1000.0 : null,
                RateW: rate,
                RuntimeMin: discharging && run is > 0 and < UnknownRuntimeMinutes ? (int)run.Value : null,
                ToFullMin: toFull,
                Verdict: verdict,
                VerdictText: label,
                Reason: reason,
                IsUps: ups));
        }
        return list;
    }

    internal static (HealthVerdict Verdict, string Label, string Reason) EvaluateHealth(double? wear, long? cycles)
    {
        if (wear == null) return (HealthVerdict.Unknown, "Sem dados", "O Windows não informou a capacidade original ou a atual, então o desgaste não pôde ser calculado.");
        if (wear >= 50) return (HealthVerdict.Critical, "Desgastada", $"Perdeu {wear:0} % da capacidade original: a autonomia é metade do que era.");
        if (wear >= 30) return (HealthVerdict.Warning, "Atenção", $"Perdeu {wear:0} % da capacidade original; a autonomia já caiu bastante.");
        if (cycles is >= 1000) return (HealthVerdict.Warning, "Atenção", $"{cycles} ciclos de carga: perto do fim da vida útil típica (500 a 1000).");
        return (HealthVerdict.Good, "Saudável", wear >= 15
            ? $"Desgaste de {wear:0} %, normal para uma bateria com uso."
            : $"Desgaste de {wear:0} %: bateria em ótimo estado.");
    }

    private static long? Long(Dictionary<string, object?>? d, string k)
    {
        if (d != null && d.TryGetValue(k, out var v) && v != null)
            try { return Convert.ToInt64(v); } catch { }
        return null;
    }

    private static bool? Bool(Dictionary<string, object?>? d, string k) =>
        d != null && d.TryGetValue(k, out var v) && v is bool b ? b : null;

    private static string? Str(Dictionary<string, object?>? d, string k) =>
        d != null && d.TryGetValue(k, out var v) && v is string s && s.Trim().Length > 0 ? s.Trim() : null;

    private static string MapChemistry(long? c) => c switch
    {
        3 => "Chumbo-ácido",
        4 => "NiCd",
        5 => "NiMH",
        6 => "Íon-lítio",
        7 => "Zinco-ar",
        8 => "Polímero de lítio",
        _ => "—"
    };

    private static string MapStatus(long? s) => s switch
    {
        1 => "Descarregando",
        2 => "Na tomada",
        3 => "Totalmente carregada",
        4 => "Carga baixa",
        5 => "Carga crítica",
        6 or 7 or 8 or 9 => "Carregando",
        11 => "Parcialmente carregada",
        _ => "—"
    };
}
