using System.Globalization;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Monitoring.Inventory;

public static class Fmt
{
    private static readonly CultureInfo Br = ByteFormat.PtBr;

    /// <summary>Capacidade binária como o Explorador do Windows mostra (acima de 100 trunca casas: 1 TB = 931 GB).</summary>
    public static string Bytes(double b) => ByteFormat.Binary(b);

    public static string Vram(ulong b) => b >= 1UL << 30 ? $"{ByteFormat.CommercialVramGb(b):0} GB" : Bytes(b);

    public static string Ghz(int mhz) => mhz > 0 ? string.Format(Br, "{0:0.00} GHz", mhz / 1000.0) : "—";
    public static string Volt(double? v) => v is double x && x > 0 ? string.Format(Br, "{0:0.00#} V", x) : "—";
    public static string OnOff(bool? v) => v switch { true => "Ativado", false => "Desativado", _ => "—" };
    public static string Date(DateTime? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";
    public static string Stamp(DateTime? d) => d?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "—";

    /// <summary>"há 1 ano e 7 meses", "há 3 meses", "há 12 dias".</summary>
    public static string Ago(DateTime? d, DateTime? now = null)
    {
        if (d is not { } from) return "";
        var to = now ?? System.DateTime.Now;
        int months = (to.Year - from.Year) * 12 + to.Month - from.Month - (to.Day < from.Day ? 1 : 0);
        if (months < 1)
        {
            int days = Math.Max(0, (int)(to - from).TotalDays);
            return days switch { 0 => "hoje", 1 => "há 1 dia", _ => $"há {days} dias" };
        }
        int y = months / 12, m = months % 12;
        string ys = y == 1 ? "1 ano" : $"{y} anos", ms = m == 1 ? "1 mês" : $"{m} meses";
        return y == 0 ? $"há {ms}" : m == 0 ? $"há {ys}" : $"há {ys} e {ms}";
    }

    /// <summary>Duração curta: "4 h 32 min", "2 d 5 h", "12 min".</summary>
    public static string Span(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays} d {t.Hours} h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        return $"{Math.Max(0, t.Minutes)} min";
    }

    public static string Cache(long bytes)
        => bytes >= 1 << 20 ? string.Format(Br, "{0:0.#} MB", bytes / (double)(1 << 20)) : $"{bytes / 1024} KB";

    /// <summary>"8 × 32 KB", ou "6 × 2 MB + 2 × 4 MB" quando núcleos diferentes têm caches diferentes.</summary>
    public static string CacheGroup(IEnumerable<CpuCache> group)
        => string.Join(" + ", group.OrderByDescending(c => c.Instances).Select(c => c.Instances > 1 ? $"{c.Instances} × {Cache(c.Size)}" : Cache(c.Size)));

    /// <summary>Linhas de cache para a ficha: da topologia do Windows ou, sem ela, os totais do WMI.</summary>
    public static List<KeyValuePair<string, string>> CacheLines(CpuInfo c)
    {
        var list = new List<KeyValuePair<string, string>>();
        var caches = c.Topology.Caches;
        if (caches.Count > 0)
        {
            foreach (var g in caches.GroupBy(x => (x.Level, x.Kind)).OrderBy(g => g.Key.Level).ThenBy(g => g.Key.Kind == "dados" ? 0 : 1))
            {
                string name = g.Key.Kind is "dados" or "instruções" ? $"L{g.Key.Level} {g.Key.Kind}" : $"L{g.Key.Level}";
                list.Add(new(name, CacheGroup(g)));
            }
            return list;
        }
        if (c.L2Kb > 0) list.Add(new("L2", Cache(c.L2Kb * 1024)));
        if (c.L3Kb > 0) list.Add(new("L3", Cache(c.L3Kb * 1024)));
        return list;
    }

    /// <summary>Texto que o fabricante deixou de preencher (placas montadas, BIOS genérica).</summary>
    public static bool IsPlaceholder(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        string t = s.Trim().ToLowerInvariant();
        return t is "—" or "default string" or "to be filled by o.e.m." or "system product name" or "system manufacturer" or "system version"
            or "not applicable" or "not specified" or "none" or "n/a" or "0" or "base board serial number" or "123456789" or "unknown" or "x.x"
            || t.StartsWith("to be filled");
    }

    public static string Clean(string? s) => IsPlaceholder(s) ? "—" : s!.Trim();

    /// <summary>Nome curto do fabricante para o sobretítulo: "ASUSTeK COMPUTER INC." → "ASUS".</summary>
    public static string ShortMaker(string maker)
    {
        string m = maker.ToLowerInvariant();
        (string Key, string Name)[] known =
        {
            ("asus", "ASUS"), ("micro-star", "MSI"), ("msi", "MSI"), ("gigabyte", "Gigabyte"), ("asrock", "ASRock"),
            ("dell", "Dell"), ("hewlett", "HP"), ("hp ", "HP"), ("lenovo", "Lenovo"), ("acer", "Acer"), ("samsung", "Samsung"),
            ("biostar", "Biostar"), ("evga", "EVGA"), ("intel", "Intel"), ("advanced micro devices", "AMD"), ("nvidia", "NVIDIA"),
            ("microsoft", "Microsoft"), ("apple", "Apple"), ("g skill", "G.Skill"), ("g.skill", "G.Skill"), ("corsair", "Corsair"),
            ("kingston", "Kingston"), ("crucial", "Crucial"), ("micron", "Micron"), ("hynix", "SK hynix"), ("adata", "ADATA"),
            ("teamgroup", "TeamGroup"), ("team group", "TeamGroup"), ("patriot", "Patriot"), ("american megatrends", "AMI"),
        };
        foreach (var (key, name) in known) if (m.Contains(key)) return name;
        return maker.Trim();
    }
}
