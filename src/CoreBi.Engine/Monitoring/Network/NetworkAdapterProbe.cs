using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Monitoring.Network;

internal static class NetworkAdapterProbe
{
    public static NetworkAdapterInfo? ReadAdapter()
    {
        try
        {
            var localRoute = RoutedLocalAddress();
            var selected = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(n => new { Nic = n, Properties = n.GetIPProperties() })
                .Where(n => n.Properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                                   && !g.Address.Equals(IPAddress.Any)))
                .OrderByDescending(n => localRoute != null && n.Properties.UnicastAddresses.Any(u => u.Address.Equals(localRoute)))
                .ThenByDescending(n => n.Nic.Speed)
                .FirstOrDefault();
            if (selected == null) return null;
            var nic = selected.Nic;
            var props = selected.Properties;
            var gw = props.GatewayAddresses.First(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)).Address;
            string ip = localRoute != null && props.UnicastAddresses.Any(u => u.Address.Equals(localRoute))
                ? localRoute.ToString()
                : props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "—";
            string dns = props.DnsAddresses.FirstOrDefault(d => d.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "—";
            var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
            string mac = macBytes.Length > 0 ? string.Join("-", macBytes.Select(b => b.ToString("X2"))) : "—";
            return new NetworkAdapterInfo(nic.Name, nic.Description, AdapterType(nic.NetworkInterfaceType),
                nic.Speed > 0 ? nic.Speed / 1_000_000.0 : null, ip, gw, dns, mac);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("network.adapter", ex);
            return null;
        }
    }

    private static string AdapterType(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet (cabo)",
        _ => type.ToString()
    };

    private static IPAddress? RoutedLocalAddress()
    {
        try
        {
            // Connect em UDP consulta a rota do Windows sem enviar pacote ao destino.
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53));
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException) { return null; }
    }
}
