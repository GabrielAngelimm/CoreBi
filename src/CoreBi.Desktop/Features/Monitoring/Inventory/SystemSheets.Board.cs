using System.Globalization;
using CoreBi.Engine.Monitoring.Inventory;

namespace CoreBi.Desktop.Features.Monitoring.Inventory;

public static partial class SystemSheets
{
    private static Sheet BoardSheet(SystemInventory inv, DateTime now)
    {
        if (inv.Board is not { } b) return Missing(Board, "Placa-mãe e BIOS", inv.Errors.GetValueOrDefault(SystemInventory.BoardTitle));
        var f = inv.Firmware;
        var board = new List<SpecRow> { new("Fabricante", b.Maker), new("Modelo", b.Model), new("Revisão", b.Revision) };
        if (inv.Ram is { Slots: > 0 } ram)
            board.Add(new("Slots de memória", ram.Slots.ToString(Br), ram.MaxCapacity > 0 ? $"até {Fmt.Bytes(ram.MaxCapacity)}" : null, $"{ram.Modules.Count} em uso"));
        if (inv.Cpus.FirstOrDefault() is { Socket: not "—" } cpu) board.Add(new("Soquete", cpu.Socket, null, "Encaixe do processador."));
        board.Add(new("Nº de série", b.Serial));
        if (b.Chassis != null) board.Add(new("Tipo de computador", b.Chassis, null, "O formato que o firmware declara."));
        var groups = new List<SpecGroup> { new("Placa-mãe", board) };
        if (f != null)
        {
            var bios = new List<SpecRow> { new("Fabricante", f.Vendor), new("Versão", f.Version), new("Data", Fmt.Date(f.Date), Fmt.Ago(f.Date, now)) };
            if (f.Smbios != null) bios.Add(new("SMBIOS", f.Smbios, null, "Versão da tabela de hardware que a BIOS entrega ao Windows."));
            groups.Add(new("BIOS", bios, Help: "O firmware da placa: liga o hardware antes do Windows. Atualizações trazem correções e suporte a processadores novos."));

            bool? tpmOk = f.Tpm == null ? null : f.Tpm.StartsWith("2") && f.TpmReady != false;
            string tpmValue = f.Tpm == null ? "Não lido" : f.TpmReady == false && f.Tpm != "Ausente" ? $"{f.Tpm} · desligado" : f.Tpm;
            static Tone T(bool? ok) => ok switch { true => Tone.Ok, false => Tone.Warn, _ => Tone.None };
            var security = new List<SpecRow>
            {
                Verdict("Modo de inicialização", f.Uefi switch { true => "UEFI", false => "Legado (CSM)", _ => "—" }, T(f.Uefi), null,
                    f.Uefi == false ? "O Windows 11 pede UEFI. Dá para converter o disco e mudar na BIOS." : null),
                Verdict("Secure Boot", Fmt.OnOff(f.SecureBoot), T(f.SecureBoot), null,
                    f.SecureBoot == false ? "Ative na BIOS. Alguns jogos com anti-cheat exigem." : null,
                    "Só deixa iniciar sistemas e drivers assinados."),
                Verdict("TPM", tpmValue, T(tpmOk), null,
                    f.Tpm == null ? "O TPM só é lido com o CoreBi aberto como administrador." : tpmOk == false ? "Ative fTPM (AMD) ou PTT (Intel) na BIOS." : null,
                    "Chip de segurança usado pelo BitLocker e pelo Windows Hello."),
                new("Tipo de TPM", f.TpmMaker switch
                {
                    "AMD" => "fTPM, no processador AMD",
                    "INTC" => "PTT, no chipset Intel",
                    "MSFT" => "Virtual (Microsoft)",
                    null => "—",
                    var other => $"Chip dedicado ({other})"
                }, null, "Onde o TPM mora: no próprio processador (firmware) ou num chip separado na placa."),
                new("Firmware do TPM", f.TpmFirmware ?? "—"),
                f.Windows11Ready switch
                {
                    true => Verdict("Requisitos do Windows 11", "Atendidos", Tone.Ok),
                    false => Verdict("Requisitos do Windows 11", "Falta um", Tone.Warn, null, "UEFI, Secure Boot e TPM 2.0 precisam estar ligados."),
                    _ => Verdict("Requisitos do Windows 11", "Não deu para conferir", Tone.None),
                },
            };
            groups.Add(new("Segurança", security, Help: "O que o Windows 11 exige do firmware para iniciar com segurança."));

        }
        // PCs montados repetem a placa aqui; o grupo só aparece quando diz algo novo (notebooks e PCs de marca).
        if ((b.SystemMaker != null || b.SystemModel != null) && b.SystemModel != b.Model)
            groups.Add(new("Computador", new() { new("Fabricante", b.SystemMaker ?? "—"), new("Modelo", b.SystemModel ?? "—") }));

        string biosMonth = f?.Date is DateTime d ? d.ToString("MM/yyyy", CultureInfo.InvariantCulture) : "—";
        return new Sheet
        {
            Key = Board,
            Eyebrow = b.Maker != "—" ? Fmt.ShortMaker(b.Maker) : b.SystemMaker != null ? Fmt.ShortMaker(b.SystemMaker) : "",
            Title = b.Model != "—" ? b.Model : b.SystemModel ?? "Placa-mãe",
            Subtitle = string.Join(" · ", new[] { b.Maker, b.Revision != "—" ? $"revisão {b.Revision}" : null }.Where(x => x != null && x != "—")),
            Readouts = new()
            {
                new("BIOS", f?.Version ?? "—"),
                new("Lançada em", biosMonth, "", f?.Date != null ? Fmt.Ago(f.Date, now) : null),
                new("Firmware", f?.Uefi switch { true => "UEFI", false => "Legado", _ => "—" }),
                new("TPM", f?.Tpm is { } tpm && tpm != "Ausente" ? tpm : "—"),
            },
            Groups = groups,
            Link = new("Temperaturas, ventoinhas e tensões ao vivo em Sensores", "PageSensors:board"),
        };
    }
}
