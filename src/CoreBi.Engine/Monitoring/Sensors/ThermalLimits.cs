namespace CoreBi.Engine.Monitoring.Sensors;

/// <summary>
/// Limites de temperatura num lugar só, para que Sensores, Discos, Placa de vídeo e Gargalo deem o mesmo veredito
/// para a mesma leitura.
/// </summary>
public static class ThermalLimits
{
    /// <summary>Processador e placa de vídeo: atenção a partir de 85 °C; a 95 °C a peça já corta o clock pelo calor.</summary>
    public const float ChipWarnC = 85, ChipCritC = 95;

    /// <summary>
    /// Ponto mais quente e junção da memória da GPU ficam 10 a 20 °C acima da média do chip; NVIDIA e AMD só cortam
    /// por volta de 105 a 110 °C.
    /// </summary>
    public const float JunctionWarnC = 95, JunctionCritC = 105;

    /// <summary>Disco: o SSD aguenta mais calor que o HD. Disco de tipo desconhecido usa os limites do HD.</summary>
    public static (double Warn, double Crit) Disk(bool isSsd) => isSsd ? (70, 85) : (55, 65);
}
