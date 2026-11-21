using CoreBi.Engine.Monitoring.Inventory;
using CoreBi.Engine.Monitoring.Memory;

namespace CoreBi.Desktop.Features.Monitoring.Inventory;

public static partial class SystemSheets
{
    private static Sheet RamSheet(SystemInventory inv, MemoryInfo.Snapshot? memory, DateTime now)
    {
        if (inv.Ram is not { Modules.Count: > 0 } r) return Missing(Ram, "Memória RAM", inv.Errors.GetValueOrDefault(SystemInventory.RamTitle));
        var makers = r.Modules.Select(m => m.Maker).Where(m => m != "—").Distinct().ToList();
        var sizes = r.Modules.GroupBy(m => m.Capacity).Select(g => $"{g.Count()} × {Fmt.Bytes(g.Key)}");
        var partList = r.Modules.Select(m => m.Part).Where(p => p != "—").Distinct().ToList();
        string form = string.Join(" / ", r.Modules.Select(m => m.Form).Distinct());
        var volts = r.Modules.Select(m => m.Volt).Where(v => v != null).Distinct().ToList();

        // Velocidade, perfil e canais: o que mais muda o desempenho da RAM, com o veredito ao lado do valor.
        var speed = new List<SpecRow>
        {
            new("Em uso", r.Configured > 0 ? $"{r.Configured} MT/s" : "—", r.Configured > 0 ? $"{r.Configured / 2} MHz reais" : null,
                "MT/s são transferências por segundo. A DDR faz duas por ciclo, por isso o clock real é a metade."),
            new("Nominal (JEDEC)", r.Rated > 0 ? $"{r.Rated} MT/s" : "—", null, "Velocidade que o módulo informa ao Windows."),
        };
        if (r.PartSpeed > 0) speed.Add(new("Do part number", $"{r.PartSpeed} MT/s", null, "Velocidade escrita no código do modelo, a do perfil XMP/EXPO."));
        if (r.Profile is { } p)
        {
            var (value, help) = p.Tone switch
            {
                Tone.Ok when r.PartSpeed > 0 => ("Ativo", "A RAM roda na velocidade do part number, o que só acontece com o perfil ligado."),
                Tone.Ok => ("Não precisa", "A RAM já roda na velocidade máxima que informa."),
                Tone.Warn when p.Title.StartsWith("Abaixo") => ("Abaixo da nominal", $"Roda a {r.Configured} de {r.Rated} MT/s. Confira XMP/EXPO na BIOS."),
                Tone.Warn => ("Desligado", $"Roda a {r.Configured} MT/s, mas o part number indica {r.PartSpeed}. Ative o perfil na BIOS."),
                _ => ("Não identificado", "Confira na BIOS se o XMP/EXPO está ligado."),
            };
            speed.Add(Verdict("Perfil XMP/EXPO", value, p.Tone, p.Tone == Tone.Ok && r.PartSpeed > 0 ? "estimado" : null, help,
                "O Windows não informa se o perfil está ligado; a estimativa compara a velocidade em uso com a do módulo."));
        }
        if (r.ChannelFinding is { } ch)
        {
            string value = ch.Tone == Tone.Ok ? ch.Title.TrimEnd(':') : r.Channels == null && r.Modules.Count > 1 ? "Não identificado" : "Canal único";
            speed.Add(Verdict("Canais", value, ch.Tone, null,
                ch.Tone == Tone.Ok ? Cap(ch.Text.TrimEnd('.')) + ": o dobro de dados por vez com o processador." : Cap(ch.Text)));
        }

        var capacity = new List<SpecRow>
        {
            new("Instalada", Fmt.Bytes(r.Total)),
            new("Tipo", form.Length > 0 && form != "—" ? $"{r.Type} · {form}" : r.Type, form.Contains("SODIMM") || form.Contains("SO-DIMM") ? "de notebook" : null),
        };
        if (memory is { } mem)
        {
            double reserved = (double)r.Total - mem.Total;
            capacity.Add(new("Utilizável", Fmt.Bytes(mem.Total), reserved > 64 << 20 ? $"{Fmt.Bytes(reserved)} reservados" : null,
                "O que sobra para o Windows depois do que o firmware e o vídeo integrado separam."));
        }
        if (r.MaxCapacity > 0) capacity.Add(new("Máximo da placa", Fmt.Bytes(r.MaxCapacity), null, "Capacidade máxima que a placa-mãe declara suportar."));
        string? buffer = r.Modules.Select(m => m.Buffer).Distinct().SingleOrDefault();
        if (buffer != null) capacity.Add(new("Buffer", Cap(buffer), null, "Sem buffer é o módulo comum de PC; registrada é a de servidor."));
        capacity.Add(new("ECC", r.Ecc ? "Sim" : "Não", null, "Correção de erros por hardware, comum em servidores e estações de trabalho."));

        // Slots da placa, ocupados e vazios, com o canal de cada um. Só o tamanho: tipo e velocidade já estão nos outros grupos.
        var map = r.SlotMap();
        string SlotName(RamSlot sl, int i) => sl.Name.Length > 0 ? sl.Name : $"Slot {i + 1}";
        var slotRows = map.Select((sl, i) => sl.Module is { } m
            ? new SpecRow(SlotName(sl, i), Fmt.Bytes(m.Capacity), sl.Channel != null ? $"canal {sl.Channel}" : null,
                $"{m.Bank} · {m.Locator}")
            : new SpecRow(SlotName(sl, i), "Vazio", sl.Channel != null ? $"canal {sl.Channel}" : null)).ToList();

        // Módulos lado a lado: uma coluna por módulo, as linhas que os módulos informam ao Windows.
        var mods = map.Select((sl, i) => (Name: SlotName(sl, i), Module: sl.Module)).Where(x => x.Module != null).Select(x => (x.Name, Module: x.Module!)).ToList();
        SpecRow Cells(string key, Func<RamModule, string> cell, string? tip = null)
        {
            var cells = mods.Select(x => cell(x.Module)).ToList();
            return new SpecRow(key, string.Join(" / ", cells.Distinct()), null, tip) { Cells = cells };
        }
        var moduleRows = new List<SpecRow>
        {
            Cells("Fabricante", m => m.Maker),
            Cells("Part number", m => m.Part, "Código do modelo. Com ele dá para achar a ficha técnica e os timings no site da fabricante."),
            Cells("Velocidade", m => m.Configured > 0 ? $"{m.Configured} MT/s" : "—"),
            Cells("Ranks", m => m.Ranks switch { 1 => "1 · single", 2 => "2 · dual", 4 => "4 · quad", > 0 => m.Ranks.ToString(Br), _ => "—" },
                "Quantos conjuntos de chips o módulo tem. Dual rank costuma render um pouco mais em jogos."),
            Cells("Tensão", m => m.MinVolt is double lo && m.MaxVolt is double hi && lo != hi ? $"{Fmt.Volt(m.Volt)} ({N(lo, "0.0#")}–{N(hi, "0.0#")})" : Fmt.Volt(m.Volt),
                "Tensão em uso e, entre parênteses, a faixa que o módulo aceita."),
            // Placas que dão o mesmo nome a todos os slots (DIMM 1) se diferenciam pelo banco (P0 CHANNEL A).
            mods.Select(x => x.Module.Locator).Distinct().Count() == mods.Count
                ? Cells("Posição", m => m.Locator, "Nome que a placa-mãe dá ao slot.")
                : Cells("Posição", m => m.Bank, "Nome que a placa-mãe dá ao banco do slot."),
            Cells("Nº de série", m => m.Serial),
        };
        if (mods.Any(x => x.Module.TotalWidth > x.Module.DataWidth && x.Module.DataWidth > 0))
            moduleRows.Insert(5, Cells("Largura", m => m.DataWidth > 0 ? $"{m.DataWidth} / {m.TotalWidth} bits" : "—", "Dados / total; os bits a mais são do ECC."));
        moduleRows.RemoveAll(row => row.Cells!.All(c => c == "—"));
        // Módulos na mesma velocidade repetiriam o "Em uso" de Velocidade e canais: a linha só fica quando eles diferem.
        if (r.Configured > 0) moduleRows.RemoveAll(row => row.Key == "Velocidade" && row.Cells!.All(c => c == $"{r.Configured} MT/s"));

        var groups = new List<SpecGroup>
        {
            new("Velocidade e canais", speed),
            new("Capacidade", capacity),
            new("Slots", slotRows),
        };
        // Até dois módulos cabem lado a lado; com mais, cada um ganha o próprio grupo.
        if (mods.Count <= 2)
            groups.Add(new(mods.Count == 1 ? "Módulo" : "Módulos", moduleRows) { Columns = mods.Select(x => x.Name).ToList() });
        else
            for (int k = 0; k < mods.Count; k++)
                groups.Add(new($"Módulo {mods[k].Name}", moduleRows.Select(row => row with { Value = row.Cells![k], Cells = null }).ToList()));
        foreach (var pf in r.PageFiles)
            capacity.Add(new("Arquivo de paginação", $"{Fmt.Bytes(pf.AllocatedMb * 1048576.0)} em {pf.Path[..Math.Min(2, pf.Path.Length)]}",
                r.PageFileAuto == true ? "tamanho automático" : null, "Espaço no disco que o Windows usa como extensão da RAM quando ela enche."));

        var (rv, ru) = r.Configured > 0 ? (r.Configured.ToString(Br), "MT/s") : ("—", "");
        return new Sheet
        {
            Key = Ram,
            Eyebrow = string.Join(" · ", new[] { makers.Count == 1 ? makers[0] : null, $"{r.Modules.Count} {(r.Modules.Count == 1 ? "módulo" : "módulos")}" }.Where(x => x != null)),
            Title = $"{Fmt.Bytes(r.Total)} {r.Type}",
            Subtitle = string.Join(" · ", new[] { string.Join(" + ", sizes), partList.Count == 1 ? partList[0] : null, form }.Where(x => !string.IsNullOrEmpty(x) && x != "—")),
            Readouts = new()
            {
                new("Velocidade", rv, ru),
                new("Slots", r.Modules.Count.ToString(Br), r.Slots > 0 ? $"de {r.Slots}" : ""),
                new("Canais", r.Channels?.ToString(Br) ?? "—", "", r.Channels == null ? "Os nomes dos slots não dizem em que canal cada módulo está." : "Estimado pelos nomes dos slots."),
                new("Tensão", volts.Count == 1 ? N(volts[0]!.Value, "0.00#") : "—", volts.Count == 1 ? "V" : ""),
            },
            Groups = groups,
            Link = new("Temperatura e uso da memória ao vivo em Sensores", "PageSensors:ram"),
        };
    }
}
