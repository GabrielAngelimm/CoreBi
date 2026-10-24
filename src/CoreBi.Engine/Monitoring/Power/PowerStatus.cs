using CoreBi.Engine.Monitoring.Storage;
using Microsoft.Win32;
using System.Runtime.InteropServices;

using static CoreBi.Engine.Monitoring.Power.BatteryInfo;

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
    DiskVerdict Verdict, string VerdictText, string Reason, bool IsUps);

/// <summary>Estados de suspensão que o firmware oferece e como o Windows liga o PC.</summary>
public sealed record SleepCapabilities(bool S3, bool ModernStandby, bool Hibernate, bool? FastStartup, bool UpsPresent, bool LidPresent);

/// <summary>Fonte de energia agora, baterias, suspensão e o tempo de BIOS da última partida.</summary>
public sealed record PowerSnapshot(bool? OnAc, bool BatterySaver, IReadOnlyList<BatteryState> Batteries, SleepCapabilities? Sleep, double? BiosSeconds);

public static class PowerStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    [DllImport("powrprof.dll")][return: MarshalAs(UnmanagedType.U1)] private static extern bool GetPwrCapabilities([Out] byte[] capabilities);

    /// <summary>Leitura instantânea (sem WMI) para acompanhar a cada segundo: na tomada, carga e economia de bateria.</summary>
    public static (bool? OnAc, int? Percent, bool Saver) Line()
    {
        try
        {
            if (!GetSystemPowerStatus(out var s)) return (null, null, false);
            bool? ac = s.ACLineStatus switch { 0 => false, 1 => true, _ => null };
            int? pct = s.BatteryLifePercent <= 100 && (s.BatteryFlag & 128) == 0 ? s.BatteryLifePercent : null;
            return (ac, pct, (s.SystemStatusFlag & 1) != 0);
        }
        catch { return (null, null, false); }
    }

    public static PowerSnapshot Read()
    {
        var (ac, _, saver) = Line();
        var sleep = Capabilities();
        var batteries = Batteries(sleep?.UpsPresent == true);
        return new PowerSnapshot(ac, saver, batteries, sleep, BiosSeconds());
    }

    /// <summary>SYSTEM_POWER_CAPABILITIES lido por posição (o layout é fixo desde o Windows 8).</summary>
    private static SleepCapabilities? Capabilities()
    {
        try
        {
            var c = new byte[128];
            if (!GetPwrCapabilities(c)) return null;
            bool s4 = c[6] != 0, hiberFile = c[8] != 0;
            bool? fast = null;
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power"))
                if (key?.GetValue("HiberbootEnabled") is int v) fast = v == 1 && hiberFile;
            return new SleepCapabilities(S3: c[5] != 0, ModernStandby: c[20] != 0, Hibernate: s4 && hiberFile, fast, UpsPresent: c[12] != 0, LidPresent: c[2] != 0);
        }
        catch { return null; }
    }

    /// <summary>"Último tempo de BIOS" do Gerenciador de Tarefas: o firmware grava em ms a cada partida completa.</summary>
    private static double? BiosSeconds()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            return key?.GetValue("FwPOSTTime") is int ms && ms is > 300 and < 300_000 ? ms / 1000.0 : null;
        }
        catch { return null; }
    }

    private static List<BatteryState> Batteries(bool upsPresent)
    {
        var list = new List<BatteryState>();
        var win = Q(@"root\cimv2", "Win32_Battery");
        var stat = Q(@"root\WMI", "BatteryStaticData");
        var full = Q(@"root\WMI", "BatteryFullChargedCapacity");
        var cyc = Q(@"root\WMI", "BatteryCycleCount");
        var live = Q(@"root\WMI", "BatteryStatus");
        int count = Math.Max(win.Count, stat.Count);
        for (int i = 0; i < count; i++)
        {
            var w = At(win, i); var st = At(stat, i); var fu = At(full, i); var cy = At(cyc, i); var lv = At(live, i);
            // Nobreak USB aparece no Win32_Battery, mas não tem os dados de bateria ACPI do root\WMI
            bool ups = upsPresent && st == null && fu == null;
            long? design = L(st, "DesignedCapacity"), fullCap = L(fu, "FullChargedCapacity"), remaining = L(lv, "RemainingCapacity");
            long? cycles = L(cy, "CycleCount");
            if (cycles == 0) cycles = null; // muitos fabricantes não informam (vem 0)
            long? pct = L(w, "EstimatedChargeRemaining");
            bool charging = B(lv, "Charging") == true, discharging = B(lv, "Discharging") == true;
            long? cr = L(lv, "ChargeRate"), dr = L(lv, "DischargeRate"), mv = L(lv, "Voltage"), run = L(w, "EstimatedRunTime");

            double? wear = design is > 0 && fullCap is > 0 ? Math.Max(0, 100.0 * (1 - (double)fullCap.Value / design.Value)) : null;
            double? rate = charging && cr is > 0 ? cr / 1000.0 : discharging && dr is > 0 ? -dr / 1000.0 : null;
            // Até carregar: o que falta da capacidade máxima atual dividido pela potência de carga (estimativa linear)
            int? toFull = charging && cr is > 0 && fullCap is > 0 && remaining is >= 0 && fullCap > remaining
                ? (int)Math.Round((fullCap.Value - remaining.Value) * 60.0 / cr.Value) : null;

            var (verdict, label, reason) = Health(wear, cycles);
            string status = BatteryInfo.Status(L(w, "BatteryStatus"));
            list.Add(new BatteryState(
                Title: ups ? "Nobreak" : count > 1 ? $"Bateria {i + 1}" : "Bateria",
                Manufacturer: S(st, "ManufactureName"), Model: S(st, "DeviceName") ?? S(w, "Name"),
                Chemistry: Chemistry(L(w, "Chemistry")) is var ch && ch != "—" ? ch : null,
                Percent: pct is >= 0 and <= 100 ? (int)pct.Value : null,
                StatusText: status != "—" ? status : charging ? "Carregando" : discharging ? "Descarregando" : null,
                charging, discharging,
                DesignWh: design is > 0 ? design / 1000.0 : null, FullWh: fullCap is > 0 ? fullCap / 1000.0 : null,
                RemainingWh: remaining is >= 0 && (fullCap is > 0 || design is > 0) ? remaining / 1000.0 : null,
                wear, cycles, Volts: mv is > 0 ? mv / 1000.0 : null, rate,
                RuntimeMin: discharging && run is > 0 and < 71582788 ? (int)run.Value : null, toFull,
                verdict, label, reason, ups));
        }
        return list;
    }

    /// <summary>Mesmos limites do relatório: 30 % de desgaste é atenção, 50 % é desgastada; 1000 ciclos também pede atenção.</summary>
    private static (DiskVerdict, string, string) Health(double? wear, long? cycles)
    {
        if (wear == null) return (DiskVerdict.Unknown, "Sem dados", "O Windows não informou a capacidade original ou a atual, então o desgaste não pôde ser calculado.");
        if (wear >= 50) return (DiskVerdict.Critical, "Desgastada", $"Perdeu {wear:0} % da capacidade original: a autonomia é metade do que era.");
        if (wear >= 30) return (DiskVerdict.Warning, "Atenção", $"Perdeu {wear:0} % da capacidade original; a autonomia já caiu bastante.");
        if (cycles is >= 1000) return (DiskVerdict.Warning, "Atenção", $"{cycles} ciclos de carga: perto do fim da vida útil típica (500 a 1000).");
        return (DiskVerdict.Good, "Saudável", wear >= 15 ? $"Desgaste de {wear:0} %, normal para uma bateria com uso." : $"Desgaste de {wear:0} %: bateria em ótimo estado.");
    }
}
