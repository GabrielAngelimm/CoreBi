using CoreBi.Engine.Monitoring.Inventory;

namespace CoreBi.Desktop.Features.Monitoring.Inventory;

public static partial class SystemSheets
{
    private static Sheet OsSheet(SystemInventory inv, DateTime now)
    {
        if (inv.Os is not { } o) return Missing(Os, "Sistema operacional", inv.Errors.GetValueOrDefault(SystemInventory.OsTitle));
        string up = o.LastBoot is DateTime boot ? Fmt.Span(now - boot) : "—";
        var (upValue, upUnit) = Split(up);
        var windows = new List<SpecRow>
        {
            new("Edição", o.Name),
            new("Versão", o.Version, null, "A atualização anual de recursos (23H2 = segundo semestre de 2023)."),
            new("Build", o.Build, null, null, Tone.None, "O número depois do ponto sobe a cada atualização mensal."),
            new("Arquitetura", o.Architecture),
            new("Idioma", Cap(o.Language)),
        };
        var computer = new List<SpecRow> { new("Nome", o.Machine, null, "Como o PC aparece na rede."), new("Usuário", o.User), new(o.Domain ? "Domínio" : "Grupo de trabalho", o.Workgroup) };
        // "Ligado há" fica na placa: ao lado do último boot, que já diz o mesmo, seria o mesmo dado duas vezes.
        var session = new List<SpecRow> { new("Instalado em", Fmt.Date(o.Installed), Fmt.Ago(o.Installed, now)), new("Último boot", Fmt.Stamp(o.LastBoot)) };
        if (o.LastUpdate != null) session.Insert(1, new("Última atualização", Fmt.Date(o.LastUpdate), o.LastUpdateId, "A atualização mais recente instalada pelo Windows Update."));
        var groups = new List<SpecGroup> { new("Windows", windows), new("Computador", computer), new("Sessão", session) };
        if (inv.Settings is { } st)
        {
            computer.Add(new("Fuso horário", st.TimeZone));
            if (st.Activated != null) windows.Add(Verdict("Ativação", st.Activated == true ? "Ativado" : "Não ativado", st.Activated == true ? Tone.Ok : Tone.Warn, null,
                st.Activated == true ? null : "Ative em Configurações › Sistema › Ativação."));
            groups.Add(new("Ajustes para jogos", new()
            {
                Verdict("Modo de jogo", st.GameMode ? "Ativado" : "Desativado", st.GameMode ? Tone.Ok : Tone.Warn, null,
                    st.GameMode ? null : "Ative em Configurações › Jogos › Modo de jogo.",
                    "Dá prioridade ao jogo e segura atualizações e notificações enquanto ele roda."),
                new("Agendamento de GPU por hardware", Fmt.OnOff(st.GpuScheduling), null,
                    "A placa de vídeo organiza a própria fila de trabalho. É exigido pela geração de quadros do DLSS. Fica em Configurações › Sistema › Tela › Elementos gráficos."),
                new("Integridade da memória", st.MemoryIntegrity ? "Ativada" : "Desativada", null,
                    "Proteção do Windows contra drivers maliciosos (isolamento de núcleo). Pode custar um pouco de desempenho em jogos."),
                new("Inicialização rápida", Fmt.OnOff(st.FastStartup), null,
                    "Ao desligar, o Windows salva parte do sistema para ligar mais rápido. Por isso o \"Ligado há\" pode ser longo mesmo desligando todo dia."),
                new("Plano de energia", st.PowerPlan ?? "—", null, "Quanto o Windows deixa o processador acelerar e quão rápido ele volta a economizar."),
                new("Captura em segundo plano", st.BackgroundCapture switch { true => "Ativada", false => "Desativada", _ => "—" }, null,
                    "Grava os últimos segundos de jogo o tempo todo, para salvar um clipe depois. Custa um pouco de desempenho."),
                new("Aceleração do mouse", st.MouseAcceleration switch { true => "Ativada", false => "Desativada", _ => "—" }, null,
                    "\"Aumentar a precisão do ponteiro\": o cursor anda mais quando o mouse se move rápido. Em jogos de tiro, muitos preferem desligada."),
            }, Help: "Opções do Windows que mudam como os jogos rodam.")
            { Column = 2 });
        }
        if (inv.Security is { } sec)
        {
            static string On(bool? v) => v switch { true => "Ativada", false => "Desativada", _ => "—" };
            var security = new List<SpecRow>();
            if (sec.Antivirus != null)
                security.Add(Verdict("Antivírus", sec.Antivirus.Length > 0 ? sec.Antivirus : "Nenhum ligado", sec.Antivirus.Length > 0 ? Tone.Ok : Tone.Warn, null,
                    sec.Antivirus.Length > 0 ? null : "Ligue a proteção em tempo real em Segurança do Windows."));
            security.Add(new("Proteção em tempo real", On(sec.RealTime), null, "Verifica arquivos e programas no momento em que são abertos."));
            security.Add(new("Proteção contra violação", On(sec.Tamper), null, "Impede que programas desliguem o antivírus por conta própria."));
            if (sec.Signatures is DateTime sig) security.Add(new("Definições de vírus", Fmt.Date(sig), Fmt.Ago(sig, now), "Quando o antivírus baixou a lista de ameaças mais recente."));
            security.Add(new("Controle de conta (UAC)", sec.Uac ?? "—", null, "O aviso que aparece quando um programa pede permissão de administrador."));
            groups.Add(new("Segurança do Windows", security) { Column = 0 });
        }
        // Três colunas: Windows e Segurança | Computador e Sessão | Ajustes para jogos.
        foreach (var (title, column) in new[] { ("Windows", 0), ("Computador", 1), ("Sessão", 1) })
            if (groups.FindIndex(g => g.Title == title) is int at and >= 0) groups[at] = groups[at] with { Column = column };

        string arch = o.Architecture.StartsWith("64") ? "64" : o.Architecture.StartsWith("32") ? "32" : "—";
        return new Sheet
        {
            Key = Os,
            Eyebrow = "Microsoft",
            Title = o.Name,
            Subtitle = o.Language.Length > 0 ? $"Em {o.Language}" : "",
            Readouts = new()
            {
                new("Versão", o.Version),
                new("Build", o.Build),
                new("Arquitetura", arch, arch != "—" ? "bits" : ""),
                new("Ligado há", upValue, upUnit),
            },
            Groups = groups,
        };
    }
}
