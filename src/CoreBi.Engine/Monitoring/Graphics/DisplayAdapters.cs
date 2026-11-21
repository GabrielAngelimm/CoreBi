using System.Management;

namespace CoreBi.Engine.Monitoring.Graphics;

/// <summary>Retrato das placas de vídeo para notar a troca de driver com o CoreBi aberto (atualização manual ou pelo Windows Update).</summary>
public static class DisplayAdapters
{
    /// <summary>
    /// Dispositivo, versão do driver e código de erro de cada placa. Healthy é false enquanto alguma placa está sem driver ou
    /// parada (código de erro diferente de zero), o estado do meio de uma troca. Null se o WMI não respondeu.
    /// </summary>
    public static (string Signature, bool Healthy)? Read()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT PNPDeviceID, DriverVersion, ConfigManagerErrorCode FROM Win32_VideoController");
            var parts = new List<string>();
            bool healthy = true;
            foreach (ManagementObject vc in s.Get())
                using (vc)
                {
                    uint code = vc["ConfigManagerErrorCode"] is uint c ? c : 0;
                    if (code != 0) healthy = false;
                    parts.Add($"{vc["PNPDeviceID"]}|{vc["DriverVersion"]}|{code}");
                }
            parts.Sort(StringComparer.Ordinal);
            return (string.Join(";", parts), healthy);
        }
        catch { return null; }
    }
}
