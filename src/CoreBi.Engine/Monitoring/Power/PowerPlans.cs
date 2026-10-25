using System.Runtime.InteropServices;

namespace CoreBi.Engine.Monitoring.Power;

/// <summary>Plano de energia instalado; IsActive marca o que o Windows está usando.</summary>
public sealed record PowerPlan(Guid Id, string Name, bool IsActive);

/// <summary>
/// Uma regulagem do plano ativo como o Windows guarda: valor na tomada (AC) e na bateria (DC), já em texto.
/// Ausente quando o plano não define a regulagem (algumas ficam ocultas e só existem se alguém as criou).
/// </summary>
public sealed record PlanSetting(string Key, string Title, string Ac, string Dc);

/// <summary>O essencial do que um plano regula: estado do processador (mín./máx. em %), turbo e os tempos de tela e suspensão.</summary>
public sealed record PlanValues(
    int? ProcessorMinAc, int? ProcessorMaxAc, int? ProcessorMinDc, int? ProcessorMaxDc,
    IReadOnlyList<PlanSetting> Settings)
{
    public static readonly PlanValues Empty = new(null, null, null, null, Array.Empty<PlanSetting>());
}

/// <summary>Planos instalados, o ativo e o que ele regula.</summary>
public sealed record PlanDetail(IReadOnlyList<PowerPlan> Plans, PowerPlan? Active, PlanValues Values);

/// <summary>Planos de energia pela API do Windows (powrprof.dll). Leitura em qualquer conta; trocar o plano ativo é a única escrita.</summary>
public static class PowerPlans
{
    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid ProcMin = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    private static readonly Guid ProcMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    private static readonly Guid Boost = new("be337238-0d82-4146-a960-4f3749d470c7");
    private static readonly Guid Epp = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    private static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoOff = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid SleepAfter = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    private const uint AccessScheme = 16;

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid guid);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr sub, uint flags, uint index, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    public static PlanDetail Read()
    {
        var plans = List();
        var active = plans.FirstOrDefault(p => p.IsActive);
        return new PlanDetail(plans, active, active == null ? PlanValues.Empty : ReadValues(active.Id));
    }

    /// <summary>O que um plano regula, ativo ou não: o Windows guarda os valores de cada plano e a leitura não muda nada.</summary>
    public static PlanValues ReadValues(Guid id)
    {
        try
        {
            uint? Ac(Guid sub, Guid setting) => PowerReadACValueIndex(IntPtr.Zero, ref id, ref sub, ref setting, out uint v) == 0 ? v : null;
            uint? Dc(Guid sub, Guid setting) => PowerReadDCValueIndex(IntPtr.Zero, ref id, ref sub, ref setting, out uint v) == 0 ? v : null;

            var settings = new List<PlanSetting>();
            void Add(string key, string title, Guid sub, Guid setting, Func<uint, string> text)
            {
                if (Ac(sub, setting) is not uint ac) return;
                settings.Add(new PlanSetting(key, title, text(ac), Dc(sub, setting) is uint dc ? text(dc) : text(ac)));
            }
            Add("boost", "Turbo do processador", SubProcessor, Boost, v => v switch
            {
                0 => "Desativado",
                1 => "Ativado",
                2 => "Agressivo",
                3 => "Eficiente",
                4 => "Eficiente agressivo",
                5 or 6 => "Agressivo garantido",
                _ => $"Modo {v}",
            });
            // Preferência de energia do processador (0 favorece desempenho, 100 favorece economia): é o que separa o Equilibrado do Alto desempenho
            Add("epp", "Prioridade do processador", SubProcessor, Epp, v => v switch { <= 30 => "Desempenho", <= 55 => "Equilibrada", _ => "Economia" });
            Add("display", "Apagar a tela após", SubVideo, VideoOff, Minutes);
            Add("sleep", "Suspender após", SubSleep, SleepAfter, Minutes);

            int? P(uint? v) => v is uint x && x <= 100 ? (int)x : null;
            return new PlanValues(P(Ac(SubProcessor, ProcMin)), P(Ac(SubProcessor, ProcMax)), P(Dc(SubProcessor, ProcMin)), P(Dc(SubProcessor, ProcMax)), settings);
        }
        catch (DllNotFoundException) { return PlanValues.Empty; }
        catch (EntryPointNotFoundException) { return PlanValues.Empty; }
    }

    /// <summary>Planos instalados, na ordem do Windows, com o nome traduzido que o Painel de Controle mostra.</summary>
    public static List<PowerPlan> List()
    {
        var list = new List<PowerPlan>();
        Guid? active = null;
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr) == 0)
            {
                active = Marshal.PtrToStructure<Guid>(ptr);
                LocalFree(ptr);
            }
            for (uint i = 0; i < 64; i++)
            {
                uint size = 16;
                var buffer = new byte[16];
                if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, i, buffer, ref size) != 0) break;
                var id = new Guid(buffer);
                list.Add(new PowerPlan(id, FriendlyName(id) ?? id.ToString(), id == active));
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return list;
    }

    /// <summary>Troca o plano ativo (o mesmo que escolher o plano em Opções de energia). Retorna o código de erro do Windows, 0 se deu certo.</summary>
    public static uint SetActive(Guid id)
    {
        try { return PowerSetActiveScheme(IntPtr.Zero, ref id); }
        catch (DllNotFoundException) { return 126; }
    }

    private static string? FriendlyName(Guid id)
    {
        uint size = 0;
        uint first = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if ((first != 0 && first != 234) || size == 0) return null; // 234: ERROR_MORE_DATA, só informa o tamanho
        var buffer = new byte[size];
        if (PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0) return null;
        string name = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
        return name.Length > 0 ? name : null;
    }

    /// <summary>Tempos do plano vêm em segundos; 0 é "nunca".</summary>
    internal static string Minutes(uint seconds) => seconds switch
    {
        0 => "Nunca",
        < 60 => $"{seconds} s",
        < 3600 => $"{seconds / 60} min",
        _ when seconds % 3600 == 0 => $"{seconds / 3600} h",
        _ => $"{seconds / 3600} h {seconds % 3600 / 60:00} min",
    };
}
