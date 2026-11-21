using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Monitoring.Sensors;

public sealed partial class SensorMonitor
{
    private static bool? _pawnIoInstalled;

    /// <summary>
    /// Driver PawnIO (necessário para temperatura/consumo da CPU via MSR/SMN).
    /// A biblioteca procura exatamente esta chave de desinstalação; o resultado é cacheado para evitar abrir o Registro a cada tick.
    /// </summary>
    public static bool IsPawnIOInstalled => _pawnIoInstalled ??= ProbePawnIO();

    public static void RefreshPawnIOStatus() => _pawnIoInstalled = ProbePawnIO();

    private static bool ProbePawnIO()
    {
        try
        {
            using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                                                                     Microsoft.Win32.RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return key != null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            DiagnosticLog.Write("sensors.pawnio", ex);
            return false;
        }
    }

    internal static bool IsElevated => ProcessElevation.IsElevated;

    public enum CpuSensorProblem { None, NotElevated, PawnIOMissing, ReadFailed }

    /// <summary>Por que a temperatura/consumo da CPU não aparecem (o motivo mais provável primeiro).</summary>
    public CpuSensorProblem DiagnoseCpuSensors()
    {
        if (CpuTemperature != null) return CpuSensorProblem.None;
        if (!ProcessElevation.IsElevated) return CpuSensorProblem.NotElevated;
        if (!IsPawnIOInstalled) return CpuSensorProblem.PawnIOMissing;
        return CpuSensorProblem.ReadFailed;
    }

    public static string Explain(CpuSensorProblem p) => p switch
    {
        // A primeira frase vale sozinha (a Visão geral mostra só ela); sem citar outros programas.
        CpuSensorProblem.NotElevated =>
            "O Windows só libera os sensores internos do processador com o CoreBi aberto como administrador. " +
            "Feche e abra de novo aceitando o pedido de administrador.",
        CpuSensorProblem.PawnIOMissing =>
            "Falta o driver PawnIO, que lê a temperatura e o consumo do processador. Ele é gratuito e assinado. " +
            "Depois de instalar, feche e abra o CoreBi.",
        CpuSensorProblem.ReadFailed =>
            "O PawnIO está instalado, mas a leitura falhou. Se acabou de instalar, reinicie o PC. Se continuar, " +
            "confira se o antivírus bloqueou o driver ou se a biblioteca de sensores ainda não reconhece este processador.",
        _ => ""
    };
}
