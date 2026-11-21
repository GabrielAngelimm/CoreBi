namespace CoreBi.Engine.Infrastructure;

/// <summary>
/// Coordena a suspensão segura de leituras nativas de GPU (como NVML) durante mudanças de dispositivos
/// ou troca do driver de vídeo, evitando falha nativa fora do alcance de blocos catch.
/// </summary>
public static class GpuAccessGate
{
    private static volatile bool _suspended;

    public static bool IsSuspended => _suspended;

    internal static void SetSuspended(bool suspended) => _suspended = suspended;
}
