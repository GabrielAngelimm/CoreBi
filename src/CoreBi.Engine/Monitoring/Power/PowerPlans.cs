using System.Runtime.InteropServices;

namespace CoreBi.Engine.Monitoring.Power;

/// <summary>Plano de energia instalado; IsActive marca o que o Windows está usando.</summary>
public sealed record PowerPlan(Guid Id, string Name, bool IsActive);

/// <summary>
/// Uma regulagem do plano ativo como o Windows guarda: valor na tomada (AC) e na bateria (DC), já em texto.
/// Ausente quando o plano não define a regulagem (algumas ficam ocultas e só existem se alguém as criou).
/// </summary>
public sealed record PlanSetting(string Key, string Title, string Ac, string Dc);

/// <summary>Plano ativo e o que ele regula: estado do processador (mín./máx. em %), turbo, resfriamento, tempos e economias.</summary>
public sealed record PlanDetail(
    IReadOnlyList<PowerPlan> Plans,
    PowerPlan? Active,
    int? ProcessorMinAc, int? ProcessorMaxAc, int? ProcessorMinDc, int? ProcessorMaxDc,
    IReadOnlyList<PlanSetting> Settings,
    string? WindowsMode);

/// <summary>Planos de energia pela API do Windows (powrprof.dll). Leitura em qualquer conta; trocar o plano ativo é a única escrita.</summary>
public static class PowerPlans
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid ProcMin = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    private static readonly Guid ProcMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    private static readonly Guid Boost = new("be337238-0d82-4146-a960-4f3749d470c7");
    private static readonly Guid Cooling = new("94d3a615-a899-4ac5-ae2b-e4d8f634367f");
    private static readonly Guid SubPcie = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    private static readonly Guid Aspm = new("ee12f906-d277-404b-b6da-e5fa1a576df5");
    private static readonly Guid SubUsb = new("2a737441-1930-4402-8d77-b2bebba308a3");
    private static readonly Guid UsbSuspend = new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
    private static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoOff = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid SleepAfter = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
    private static readonly Guid HibernateAfter = new("9d7815a6-7ee4-497e-8888-515a05f02364");
    private static readonly Guid WakeTimers = new("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d");
    private static readonly Guid SubDisk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    private static readonly Guid DiskOff = new("6738e2c4-e8a5-4a42-b16a-e040e769756e");

    private const uint AccessScheme = 16;

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid guid);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr sub, uint flags, uint index, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActualOverlayScheme(out Guid overlay);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    public static PlanDetail Read()
    {
        var plans = List();
        var active = plans.FirstOrDefault(p => p.IsActive);
        if (active == null) return new PlanDetail(plans, null, null, null, null, null, Array.Empty<PlanSetting>(), null);
        Guid id = active.Id;

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
        Add("cooling", "Resfriamento", SubProcessor, Cooling, v => v == 1 ? "Ativo" : "Passivo");
        Add("aspm", "PCI Express", SubPcie, Aspm, v => v switch { 0 => "Sem economia", 1 => "Economia moderada", 2 => "Economia máxima", _ => $"Modo {v}" });
        Add("usb", "Suspensão seletiva USB", SubUsb, UsbSuspend, v => v == 0 ? "Desativada" : "Ativada");
        Add("display", "Desligar a tela", SubVideo, VideoOff, Minutes);
        Add("sleep", "Suspender", SubSleep, SleepAfter, Minutes);
        Add("disk", "Desligar os discos", SubDisk, DiskOff, Minutes);
        Add("hibernate", "Hibernar", SubSleep, HibernateAfter, Minutes);
        Add("wake", "Despertadores", SubSleep, WakeTimers, v => v switch { 0 => "Bloqueados", 1 => "Permitidos", 2 => "Só os importantes", _ => $"Modo {v}" });

        int? P(uint? v) => v is uint x && x <= 100 ? (int)x : null;
        return new PlanDetail(plans, active,
            P(Ac(SubProcessor, ProcMin)), P(Ac(SubProcessor, ProcMax)), P(Dc(SubProcessor, ProcMin)), P(Dc(SubProcessor, ProcMax)),
            settings, active.Id == Balanced ? WindowsMode() : null);
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
        if (PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref size) != 0 || size == 0) return null;
        var buffer = new byte[size];
        if (PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0) return null;
        string name = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
        return name.Length > 0 ? name : null;
    }

    /// <summary>"Modo de energia" de Configurações (vale só com o plano Equilibrado). Nulo nas versões do Windows sem essa opção.</summary>
    private static string? WindowsMode()
    {
        try
        {
            if (PowerGetActualOverlayScheme(out Guid g) != 0) return null;
            return g.ToString() switch
            {
                "961cc777-2547-4f9d-8174-7d86181b8a7a" => "Melhor eficiência de energia",
                "3af9b8d9-7c97-431d-ad78-34a8bfea439f" => "Melhor bateria",
                "00000000-0000-0000-0000-000000000000" => "Equilibrado",
                "ded574b5-45a0-4f42-8737-46345c09c238" => "Melhor desempenho",
                _ => null,
            };
        }
        catch (EntryPointNotFoundException) { return null; }
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
