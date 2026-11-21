using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Health;
using CoreBi.Engine.Monitoring.Sensors;
using LibreHardwareMonitor.Hardware;

namespace CoreBi.Engine.Monitoring.Storage;

public sealed class DiskHealthCard
{
    public int Index { get; init; }
    public string Title { get; init; } = "";
    public string Meta { get; init; } = "";
    public HealthVerdict Verdict { get; init; } = HealthVerdict.Unknown;
    public string? Label { get; init; }
    public string VerdictText => Verdict.ToVerdictText(Label);
    public string Reasons { get; init; } = "";
    public List<KeyValuePair<string, string>> Metrics { get; init; } = new();
    public DiskDetail? Disk { get; init; }
}

public sealed class DiskDetail
{
    public const double WearWarnAt = 80, WearCritAt = 95;
    public const ulong UnsafeShutdownWarnAt = 50;

    public string Model { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Bus { get; init; } = "";
    public long SizeBytes { get; init; }
    public IReadOnlyList<string> Letters { get; init; } = Array.Empty<string>();
    public string Serial { get; init; } = "";
    public string Firmware { get; init; } = "";
    public bool IsSsd { get; init; }
    public double? Temperature { get; init; }
    public double? TemperatureMax { get; init; }
    public double TempWarnAt { get; init; }
    public double TempCritAt { get; init; }
    public double? Wear { get; init; }
    public double? AvailableSpare { get; init; }
    public double? SpareThreshold { get; init; }
    public long? PowerOnHours { get; init; }
    public long? StartStopCycles { get; init; }
    public long? ReadErrors { get; init; }
    public long? WriteErrors { get; init; }
    public long? ReadUncorrected { get; init; }
    public long? WriteUncorrected { get; init; }
    public double? WrittenGb { get; init; }
    public double? ReadGb { get; init; }
    public bool? PredictFailure { get; init; }
    /// <summary>HealthStatus do Windows: 0 saudável, 1 atenção, 2 não saudável, 5 desconhecido.</summary>
    public int WindowsHealth { get; init; } = 5;
    public string? SensorHardware { get; init; }
    public NvmeLog? Nvme { get; init; }
}

internal sealed class DiskSample
{
    public int Index;
    public string Model = "", Serial = "", Firmware = "", Bus = "", Media = "";
    public long Size;
    public int Health = 5;
    public string Pnp = "";
    public bool? PredictFail;
    public double? Temp, TempMax, Wear;
    public long? PowerOnHours, ReadErr, WriteErr, ReadErrUncorrected, WriteErrUncorrected, StartStop;
    public double? WrittenGb, ReadGb, Spare, SpareThreshold;
    public IReadOnlyList<string> Letters = Array.Empty<string>();
    public string? SensorHardware;
    public NvmeLog? Nvme;
}

/// <summary>
/// Lê a saúde dos discos pela API de armazenamento do Windows + S.M.A.R.T. (WMI) e completa com sensores do
/// LibreHardwareMonitor quando faltam leituras. Nada é escrito no disco.
/// </summary>
public static class DiskHealth
{
    public static List<DiskHealthCard> Read(IEnumerable<SensorReading>? sensors = null, IReadOnlyDictionary<string, NvmeLog>? nvme = null)
    {
        var samples = new Dictionary<int, DiskSample>();
        try
        {
            foreach (var d in WmiDiskTopology.ReadDiskDrivesOrThrow())
            {
                samples[d.Index] = new DiskSample
                {
                    Index = d.Index,
                    Model = d.Model,
                    Serial = d.Serial,
                    Firmware = d.Firmware,
                    Bus = d.Bus,
                    Size = d.SizeBytes,
                    Pnp = d.PnpDeviceId,
                };
            }
        }
        catch (Exception ex) { return new List<DiskHealthCard> { ErrorCard(ex.Message) }; }

        foreach (var kv in WmiDiskTopology.ReadPhysicalMetadata())
        {
            if (!samples.TryGetValue(kv.Key, out var r)) continue;
            r.Health = kv.Value.HealthStatus;
            r.Media = kv.Value.Media;
            if (kv.Value.Bus != null) r.Bus = kv.Value.Bus;
        }

        WmiQuery.ForEach("SELECT * FROM MSFT_StorageReliabilityCounter", c =>
        {
            string devId = WmiQuery.GetString(c, "DeviceId") ?? "";
            if (!int.TryParse(devId, out int idx) || !samples.TryGetValue(idx, out var r)) return;
            r.Temp = Num(c["Temperature"]);
            r.TempMax = Num(c["TemperatureMax"]);
            r.Wear = Num(c["Wear"]);
            r.PowerOnHours = (long?)Num(c["PowerOnHours"]);
            r.ReadErr = (long?)Num(c["ReadErrorsTotal"]);
            r.WriteErr = (long?)Num(c["WriteErrorsTotal"]);
            r.ReadErrUncorrected = (long?)Num(c["ReadErrorsUncorrected"]);
            r.WriteErrUncorrected = (long?)Num(c["WriteErrorsUncorrected"]);
            r.StartStop = (long?)Num(c["StartStopCycleCount"]);
        }, WmiDiskTopology.StorageScope);

        WmiQuery.ForEach("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", p =>
        {
            string inst = WmiQuery.GetString(p, "InstanceName") ?? "";
            bool fail = WmiQuery.GetNullableBool(p, "PredictFailure") == true;
            foreach (var r in samples.Values)
                if (r.Pnp.Length > 0 && inst.StartsWith(r.Pnp, StringComparison.OrdinalIgnoreCase))
                    r.PredictFail = (r.PredictFail ?? false) || fail;
        }, @"root\wmi");

        foreach (var kv in WmiDiskTopology.ReadLettersByDiskIndex())
            if (samples.TryGetValue(kv.Key, out var r)) r.Letters = kv.Value;

        if (sensors != null) EnrichFromSensors(samples, sensors, nvme);

        return samples.Values.OrderBy(r => r.Index).Select(Evaluate).ToList();
    }

    /// <summary>
    /// Quedas de energia e resets forçados acontecem em qualquer PC: o que preocupa é a frequência (por hora ligado), não o total.
    /// </summary>
    public static HealthVerdict RateUnsafeShutdowns(ulong count, long? powerOnHours)
    {
        double perHour = powerOnHours is > 0 ? (double)count / powerOnHours.Value : 0;
        return count >= 200 && perHour > 0.1 ? HealthVerdict.Critical
            : count >= DiskDetail.UnsafeShutdownWarnAt && perHour > 0.02 ? HealthVerdict.Warning
            : HealthVerdict.Good;
    }

    private static void EnrichFromSensors(Dictionary<int, DiskSample> samples, IEnumerable<SensorReading> sensors,
        IReadOnlyDictionary<string, NvmeLog>? nvme)
    {
        var list = sensors.Where(x => x.HardwareType == HardwareType.Storage).ToList();
        foreach (var r in samples.Values)
        {
            // A model name cannot distinguish two identical physical disks.
            if (samples.Values.Count(other => string.Equals(other.Model, r.Model, StringComparison.OrdinalIgnoreCase)) != 1) continue;
            var mine = list.Where(x => x.Hardware.Contains(r.Model, StringComparison.OrdinalIgnoreCase) ||
                                       r.Model.Contains(x.Hardware, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mine.Count == 0) continue;
            r.SensorHardware = mine[0].Hardware;
            if (nvme != null && nvme.TryGetValue(r.SensorHardware, out var log)) r.Nvme = log;
            r.Temp ??= mine.FirstOrDefault(x => x.Type == SensorType.Temperature && x.RawValue is > 0)?.RawValue;
            if (r.Wear == null)
            {
                var used = mine.FirstOrDefault(x => x.Name.Contains("Percentage Used", StringComparison.OrdinalIgnoreCase))?.RawValue;
                var left = mine.FirstOrDefault(x => x.Name.Contains("Remaining Life", StringComparison.OrdinalIgnoreCase))?.RawValue;
                if (used != null) r.Wear = used;
                else if (left != null) r.Wear = 100 - left;
            }
            r.WrittenGb = mine.FirstOrDefault(x => x.Name.Contains("Data Written", StringComparison.OrdinalIgnoreCase) ||
                                                   x.Name.Contains("Host Writes", StringComparison.OrdinalIgnoreCase))?.RawValue;
            r.ReadGb = mine.FirstOrDefault(x => x.Name.Contains("Data Read", StringComparison.OrdinalIgnoreCase) ||
                                                x.Name.Contains("Host Reads", StringComparison.OrdinalIgnoreCase))?.RawValue;
            // Contadores que o Windows nem sempre repassa (comum em NVMe): o LibreHardwareMonitor lê do próprio disco.
            if (r.PowerOnHours == null && mine.FirstOrDefault(x => x.Name.Equals("Power On Hours", StringComparison.OrdinalIgnoreCase))?.RawValue is > 0 and float hours)
                r.PowerOnHours = (long)hours;
            if (r.StartStop == null && mine.FirstOrDefault(x => x.Name.Equals("Power On Count", StringComparison.OrdinalIgnoreCase))?.RawValue is > 0 and float cycles)
                r.StartStop = (long)cycles;
            r.Spare = mine.FirstOrDefault(x => x.Name.Equals("Available Spare", StringComparison.OrdinalIgnoreCase))?.RawValue;
            r.SpareThreshold = mine.FirstOrDefault(x => x.Name.Equals("Available Spare Threshold", StringComparison.OrdinalIgnoreCase))?.RawValue;
        }
    }

    private static double? Num(object? v)
    {
        if (v == null) return null;
        try
        {
            double d = Convert.ToDouble(v);
            // 0xFFFF.. / valores gigantes = "não suportado" em vários drivers
            return d < 0 || d > 1e12 ? null : d;
        }
        catch { return null; }
    }

    private static DiskHealthCard ErrorCard(string msg) => new()
    {
        Title = "Não foi possível ler os discos",
        Verdict = HealthVerdict.Unknown,
        Reasons = msg,
    };

    internal static DiskHealthCard Evaluate(DiskSample r)
    {
        bool isSsd = r.Media == "SSD" || r.Bus == "NVMe";
        var crit = new List<string>();
        var warn = new List<string>();
        bool anyHealthData = r.Health != 5 || r.PredictFail != null || r.Wear != null || r.Nvme != null;

        if (r.PredictFail == true) crit.Add("O próprio disco avisa (S.M.A.R.T.) que deve falhar em breve. Faça backup agora.");
        if (r.Health == 2) crit.Add("O Windows classifica o disco como não saudável.");
        else if (r.Health == 1) warn.Add("O Windows classifica a saúde do disco como \"atenção\".");

        if (r.Wear is double w)
        {
            if (w >= DiskDetail.WearCritAt) crit.Add($"Desgaste de {w:0}%: o SSD está no fim da vida útil. Troque e faça backup.");
            else if (w >= DiskDetail.WearWarnAt) warn.Add($"Desgaste de {w:0}%: o SSD já gastou boa parte da vida útil. Planeje a troca.");
        }
        double okMax = isSsd ? 70 : 55, critMax = isSsd ? 85 : 65;
        if (r.Temp is double t)
        {
            if (t >= critMax) crit.Add($"Temperatura de {t:0} °C é muito alta para este disco.");
            else if (t >= okMax) warn.Add($"Temperatura de {t:0} °C está alta. Melhore a ventilação.");
        }
        if (r.Nvme?.CriticalWarning is > 0) crit.Add("O SSD acusa um aviso crítico no próprio relatório de saúde NVMe. Faça backup agora.");
        if (r.Nvme?.MediaErrors is > 0) warn.Add($"O SSD registrou {(r.Nvme.MediaErrors == 1 ? "1 erro" : $"{r.Nvme.MediaErrors:N0} erros")} de mídia e integridade dos dados.");
        if (r.Spare is double sp && r.SpareThreshold is double st && st > 0 && sp < st)
            crit.Add($"Os blocos de reserva do SSD estão em {sp:0}%, abaixo do mínimo de {st:0}% definido pelo fabricante. Faça backup.");
        if (r.ReadErrUncorrected > 0 || r.WriteErrUncorrected > 0)
            warn.Add($"Erros não corrigidos: {r.ReadErrUncorrected ?? 0} de leitura e {r.WriteErrUncorrected ?? 0} de escrita.");

        HealthVerdict verdict = crit.Count > 0 ? HealthVerdict.Critical
            : warn.Count > 0 ? HealthVerdict.Warning
            : anyHealthData ? HealthVerdict.Good : HealthVerdict.Unknown;

        var reasons = crit.Concat(warn).ToList();
        if (reasons.Count == 0)
            reasons.Add(verdict == HealthVerdict.Good
                ? "Nenhum problema encontrado nas leituras disponíveis."
                : "Este disco não informa dados de saúde (comum em pendrives e alguns HDs externos).");

        // Alguns drivers devolvem o limite do disco (100 °C) no lugar da máxima registrada: acima do crítico não é leitura.
        double? recordedTempMax = r.TempMax is > 0 && r.TempMax < critMax ? r.TempMax : null;

        string kind = r.Media.Length > 0 ? r.Media : (isSsd ? "SSD" : "");
        string lettersText = r.Letters.Count > 0 ? string.Join(" ", r.Letters) : "";
        var meta = new[]
        {
            kind,
            r.Bus,
            r.Size > 0 ? ByteFormat.Decimal((ulong)r.Size) : "",
            lettersText.Length > 0 ? "Unidades: " + lettersText : ""
        }.Where(x => !string.IsNullOrEmpty(x));

        var metrics = new List<KeyValuePair<string, string>>();
        void M(string k, string? val) { if (val != null) metrics.Add(new(k, val)); }
        M("Temperatura", r.Temp is double tt ? $"{tt:0} °C" + (recordedTempMax is double tm ? $" (máx. registrado {tm:0})" : "") : null);
        if (r.Wear is double wr) { M("Desgaste", $"{wr:0}%"); M("Vida útil restante", $"{Math.Max(0, 100 - wr):0}%"); }
        M("Horas ligado", r.PowerOnHours is long h ? $"{h:N0} h ({h / 24.0:N0} dias)" : null);
        M("Ciclos liga/desliga", r.StartStop is long ss ? $"{ss:N0}" : null);
        M("Erros de leitura", r.ReadErr is long re ? $"{re:N0}" + (r.ReadErrUncorrected is long ru && ru > 0 ? $" ({ru:N0} não corrigidos)" : "") : null);
        M("Erros de escrita", r.WriteErr is long we ? $"{we:N0}" + (r.WriteErrUncorrected is long wu && wu > 0 ? $" ({wu:N0} não corrigidos)" : "") : null);
        M("Total gravado", r.WrittenGb is double wg && wg > 0 ? $"{wg:N0} GB" : null);
        M("Total lido", r.ReadGb is double rg && rg > 0 ? $"{rg:N0} GB" : null);
        M("Blocos de reserva", r.Spare is double spare ? $"{spare:0}%" + (r.SpareThreshold is double min && min > 0 ? $" (mínimo {min:0}%)" : "") : null);
        M("Aviso crítico (NVMe)", r.Nvme?.CriticalWarning is ulong cw ? (cw == 0 ? "Nenhum" : $"0x{cw:X2}") : null);
        M("Erros de mídia (NVMe)", r.Nvme?.MediaErrors is ulong me ? $"{me:N0}" : null);
        M("Desligamentos inesperados", r.Nvme?.UnsafeShutdowns is ulong us ? $"{us:N0}" : null);
        M("Falha prevista (S.M.A.R.T.)", r.PredictFail is bool pf ? (pf ? "Sim" : "Não") : null);
        M("Saúde segundo o Windows", WmiDiskTopology.MapHealthStatusText(r.Health));
        M("Firmware", r.Firmware.Length > 0 ? r.Firmware : null);

        return new DiskHealthCard
        {
            Index = r.Index,
            Title = r.Model,
            Meta = string.Join(" · ", meta),
            Verdict = verdict,
            Reasons = string.Join(" ", reasons),
            Metrics = metrics,
            Disk = new DiskDetail
            {
                Model = r.Model,
                Kind = kind,
                Bus = r.Bus,
                SizeBytes = r.Size,
                Letters = r.Letters,
                Serial = r.Serial,
                Firmware = r.Firmware,
                IsSsd = isSsd,
                Temperature = r.Temp,
                TemperatureMax = recordedTempMax,
                TempWarnAt = okMax,
                TempCritAt = critMax,
                Wear = r.Wear,
                AvailableSpare = r.Spare,
                SpareThreshold = r.SpareThreshold,
                PowerOnHours = r.PowerOnHours,
                StartStopCycles = r.StartStop,
                ReadErrors = r.ReadErr,
                WriteErrors = r.WriteErr,
                ReadUncorrected = r.ReadErrUncorrected,
                WriteUncorrected = r.WriteErrUncorrected,
                WrittenGb = r.WrittenGb is > 0 ? r.WrittenGb : null,
                ReadGb = r.ReadGb is > 0 ? r.ReadGb : null,
                PredictFailure = r.PredictFail,
                WindowsHealth = r.Health,
                SensorHardware = r.SensorHardware,
                Nvme = r.Nvme,
            },
        };
    }
}
