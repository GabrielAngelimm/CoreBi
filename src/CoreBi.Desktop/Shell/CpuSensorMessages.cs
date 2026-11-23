using CoreBi.Engine.Monitoring.Sensors;

namespace CoreBi.Desktop;

/// <summary>Mensagens curtas consistentes para o diagnóstico de sensores da CPU (barra lateral, Energia e Gargalo).</summary>
internal static class CpuSensorMessages
{
    public static string Sidebar(SensorMonitor.CpuSensorProblem problem) => problem switch
    {
        SensorMonitor.CpuSensorProblem.NotElevated => "Sem permissão de administrador.",
        SensorMonitor.CpuSensorProblem.PawnIOMissing => "Falta o driver PawnIO.",
        SensorMonitor.CpuSensorProblem.ReadFailed => "O driver não respondeu.",
        _ => "",
    };

    public static string? Inline(SensorMonitor.CpuSensorProblem problem, string prefix) => problem switch
    {
        SensorMonitor.CpuSensorProblem.NotElevated => $"{prefix}: abra como administrador",
        SensorMonitor.CpuSensorProblem.PawnIOMissing => $"{prefix}: falta o driver PawnIO",
        SensorMonitor.CpuSensorProblem.ReadFailed => $"{prefix}: o driver não respondeu",
        _ => null,
    };

    public static string VoltagesEmpty(bool sensorsOpen, SensorMonitor.CpuSensorProblem problem) =>
        !sensorsOpen ? "Aguardando os sensores…" : problem switch
        {
            SensorMonitor.CpuSensorProblem.NotElevated => "Sem leitura: abra o CoreBi como administrador.",
            SensorMonitor.CpuSensorProblem.PawnIOMissing => "Sem leitura: falta o driver PawnIO.",
            SensorMonitor.CpuSensorProblem.None => "A biblioteca de sensores não lê as tensões desta placa-mãe.",
            _ => "Os sensores da placa-mãe e do processador não responderam.",
        };
}
