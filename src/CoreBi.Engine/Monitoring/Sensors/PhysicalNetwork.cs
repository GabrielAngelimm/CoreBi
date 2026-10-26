using System.Net.NetworkInformation;

using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Monitoring.Sensors;

/// <summary>
/// Quais interfaces de rede são placas de verdade. O Windows expõe dezenas de interfaces que não são hardware: camadas de
/// filtro dos drivers (WFP, QoS), Wi-Fi Direct ("Conexão Local* 8"), switches virtuais do Hyper-V/WSL, minipontas WAN,
/// o adaptador do depurador de kernel. A biblioteca de sensores lista todas; a página só quer as físicas e conectadas.
/// </summary>
public static class PhysicalNetwork
{
    private static readonly string[] VirtualMarks =
    {
        "virtual", "hyper-v", "vmware", "virtualbox", "vbox", "miniport", "filter", "debug", "loopback", "tunnel", "tap-",
        "tap adapter", "wireguard", "openvpn", "vpn", "bluetooth", "wi-fi direct", "teredo", "isatap", "pseudo", "npcap",
        "zerotier", "tailscale", "hamachi", "-qos", "-wfp", "packet scheduler", "vswitch", "vethernet"
    };

    /// <summary>Placa Ethernet ou Wi-Fi física, pelo tipo, pela descrição do driver e pelo nome da conexão.</summary>
    public static bool IsPhysical(NetworkInterfaceType type, string description, string name)
    {
        if (type is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.Wireless80211))
            return false;
        if (name.Contains('*')) return false; // "Conexão Local* 8": interfaces criadas pelo Windows (Wi-Fi Direct, filtros)
        string text = (description + " " + name).ToLowerInvariant();
        return !VirtualMarks.Any(text.Contains);
    }

    /// <summary>Nomes das conexões físicas e ativas agora; null se o Windows não respondeu (aí nada é escondido).</summary>
    public static HashSet<string>? ActiveNames()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && IsPhysical(n.NetworkInterfaceType, n.Description, n.Name))
                .Select(n => n.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException)
        {
            DiagnosticLog.Write("sensors.network", ex);
            return null;
        }
    }
}
