using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Storage;

namespace CoreBi.Desktop.Features.Monitoring.Inventory;

public static partial class SystemSheets
{
    private static Sheet StorageSheet(SystemInventory inv, IReadOnlyList<DriveCard> drives)
    {
        var disks = new List<DiskView>();
        var groups = new List<SpecGroup>();
        foreach (var d in inv.Disks)
        {
            var vols = drives.Where(v => v.Disk.Length > 0 && v.Disk == d.Model).ToList();
            double size = Math.Max(1, d.Size);
            var volumes = vols.Select(v => new VolumeView(v.Letter, (string.IsNullOrWhiteSpace(v.Label) ? v.Kind : v.Label) + (v.FileSystem.Length > 0 ? $" · {v.FileSystem}" : ""),
                $"{v.UsedPercent} usado de {DriveCard.Fmt(v.Total)}", Math.Max(0.04, v.Total / size), v.UsedFraction,
                $"{DriveCard.Fmt(v.Free)} livres")).ToList();
            var (sv, su) = Split(Fmt.Bytes(d.Size));
            string meta = string.Join(" · ", new[] { $"Disco {d.Index}", d.System ? "disco do Windows" : "", d.Removable && d.Bus != "USB" ? "removível" : "" }.Where(x => x.Length > 0));
            var tone = d.Health switch { "Saudável" => Tone.Ok, null => Tone.None, _ => Tone.Warn };
            string freeText = vols.Count > 0 ? "" : "sem unidade montada";
            disks.Add(new DiskView(d.Model, meta, sv, su, freeText, volumes, d.Health ?? "", tone) { First = disks.Count == 0 });
            var (about, layout) = DiskRows(d, vols, inv.BitLocker);
            // O nome do disco já está na linha de cima; os grupos levam só o número dele quando há mais de um.
            bool many = inv.Disks.Count > 1;
            groups.Add(new(many ? $"Especificações do disco {d.Index}" : "Especificações", about));
            if (layout.Count > 0) groups.Add(new(many ? $"Partições e proteção do disco {d.Index}" : "Partições e proteção", layout));
        }
        double total = inv.Disks.Sum(d => (double)d.Size);
        double freeAll = drives.Sum(v => (double)v.Free), usedAll = drives.Sum(v => (double)(v.Total - v.Free));
        var (uv, uu) = Split(Fmt.Bytes(usedAll));
        var (fv, fu) = Split(Fmt.Bytes(freeAll));
        return new Sheet
        {
            Key = Storage,
            Eyebrow = "Capacidade total",
            Title = total > 0 ? Fmt.Bytes(total) : "—",
            // Do que é feito o armazenamento ("SSD NVMe · HD SATA"); contagens e espaço ficam nas leituras.
            Subtitle = string.Join(" · ", inv.Disks.Select(d => string.Join(" ", new[] { d.Media, d.Bus }.Where(x => x.Length > 0 && x != "—"))).Where(x => x.Length > 0).Distinct()),
            Readouts = new()
            {
                new("Em uso", uv, uu),
                new("Livre", fv, fu),
                new("Discos", inv.Disks.Count.ToString(Br)),
                new("Unidades", drives.Count.ToString(Br)),
            },
            Disks = disks,
            Groups = groups,
            Link = new("Saúde, temperatura e vida útil em Discos", "PageDisks"),
        };
    }

    /// <summary>Ficha de um disco: o que ele é, como está ligado e o estado que o Windows informa.</summary>
    private static (List<SpecRow> About, List<SpecRow> Layout) DiskRows(PhysicalDiskInfo d, List<DriveCard> vols, Dictionary<string, bool> bitLocker)
    {
        string media = d.Media switch { "SSD" => "SSD", "HDD" => "HD (disco giratório)", var m => m };
        var rows = new List<SpecRow>
        {
            new("Interface", d.Bus, null, d.Bus switch
            {
                "NVMe" => "Ligado direto nas linhas PCIe do processador ou do chipset: o tipo mais rápido.",
                "SATA" => "Até cerca de 550 MB/s, o limite dos SSDs SATA.",
                "USB" => "Disco externo pela USB.",
                _ => null
            }),
            new("Tipo", media),
        };
        if (d.Rpm is int rpm) rows.Add(new("Rotação", $"{rpm} rpm", null, "Quanto mais rápido o prato gira, menor o tempo de acesso."));
        rows.Add(new("Firmware", d.Firmware, null, "Versão do software interno do disco. Fabricantes às vezes lançam correções."));
        // NVMe: o Windows entrega o identificador com grupos de zeros na frente (0000_0000_..._707C_18B5); a parte útil é o fim.
        string serial = d.Serial.Trim().TrimEnd('.');
        string shortSerial = System.Text.RegularExpressions.Regex.Replace(serial, "^(0000_)+", "");
        rows.Add(new("Nº de série", shortSerial, null, shortSerial != serial ? $"Identificador completo: {serial}" : null));
        var layout = new List<SpecRow>();
        if (d.Partition != null)
            layout.Add(new("Tabela de partições", d.Partition, d.Partitions > 0 ? $"{d.Partitions} {(d.Partitions == 1 ? "partição" : "partições")}" : null,
                "GPT é o padrão atual e é o que o Windows 11 usa para iniciar em UEFI; MBR é o formato antigo, limitado a 2 TB. " +
                "Além das unidades com letra, o Windows guarda partições ocultas de inicialização e recuperação."));
        if (d.LogicalSector > 0)
            layout.Add(new("Setor", d.PhysicalSector > d.LogicalSector ? $"{d.LogicalSector} B lógico · {d.PhysicalSector / 1024} KB físico" : $"{d.LogicalSector} B",
                null, "O menor bloco que o disco lê ou grava."));
        var locked = vols.Where(v => bitLocker.TryGetValue(v.Letter.TrimEnd('\\'), out _)).ToList();
        if (locked.Count > 0)
        {
            var on = locked.Where(v => bitLocker[v.Letter.TrimEnd('\\')]).Select(v => v.Letter.TrimEnd('\\')).ToList();
            layout.Add(new("BitLocker", on.Count > 0 ? $"Ativado em {string.Join(", ", on)}" : "Desativado", null,
                "Criptografia do Windows: sem a senha ou a chave de recuperação, ninguém lê os dados do disco."));
        }
        return (rows, layout);
    }
}
