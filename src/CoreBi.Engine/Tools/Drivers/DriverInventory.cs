using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Tools.Drivers;

/// <summary>Inventário local via WMI de drivers assinados, GPUs e placa-mãe/BIOS.</summary>
public static class DriverInventory
{
    private static readonly HashSet<string> MainClasses = new(StringComparer.Ordinal)
    {
        "DISPLAY", "NET", "MEDIA", "BLUETOOTH", "HDC", "SCSIADAPTER", "USB", "SYSTEM",
    };

    private static readonly (string[] Tokens, string Brand, Func<string, string> SupportUrl)[] BoardVendors =
    {
        // Onde o site aceita busca, o atalho já abre o modelo da placa (a primeira resposta é a página de suporte dele)
        (new[] { "ASUS" }, "ASUS", q => "https://www.asus.com/searchresult?searchType=support&searchKey=" + q),
        (new[] { "Micro-Star", "MSI" }, "MSI", q => "https://www.msi.com/search/" + q),
        (new[] { "GIGABYTE" }, "Gigabyte", q => "https://www.gigabyte.com/Search?kw=" + q),
        (new[] { "ASRock" }, "ASRock", _ => "https://www.asrock.com/support/index.asp"),
        (new[] { "Dell" }, "Dell", _ => "https://www.dell.com/support/home/"),
        (new[] { "LENOVO" }, "Lenovo", _ => "https://support.lenovo.com/"),
        (new[] { "HP", "Hewlett" }, "HP", _ => "https://support.hp.com/"),
        (new[] { "Acer" }, "Acer", _ => "https://www.acer.com/support"),
    };

    /// <summary>Drivers instalados (sem os genéricos da Microsoft, exceto vídeo, rede e áudio).</summary>
    public static List<DriverRow> ListInstalled()
    {
        var list = new List<DriverRow>();
        WmiQuery.ForEach("SELECT DeviceName, DeviceClass, Manufacturer, DriverVersion, DriverDate, IsSigned FROM Win32_PnPSignedDriver", o =>
        {
            string name = WmiQuery.GetString(o, "DeviceName") ?? "";
            if (name.Length == 0) return;
            string cls = WmiQuery.GetString(o, "DeviceClass") ?? "";
            if (!MainClasses.Contains(cls)) return;
            string maker = WmiQuery.GetString(o, "Manufacturer") ?? "";
            if (maker.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) && cls is not ("DISPLAY" or "NET" or "MEDIA")) return;
            list.Add(new DriverRow
            {
                Device = name,
                Class = cls,
                Vendor = maker,
                Version = WmiQuery.GetString(o, "DriverVersion") ?? "—",
                Date = WmiCalendarDate.Format(WmiQuery.GetString(o, "DriverDate")),
                Signed = WmiQuery.GetNullableBool(o, "IsSigned") == true,
            });
        });
        return list.OrderBy(r => r.Class).ThenBy(r => r.Device).ToList();
    }

    /// <summary>
    /// Dispositivos presentes com problema (o ponto de exclamação do Gerenciador de Dispositivos). Ficam de fora os desativados
    /// por quem usa (código 22) e os que não estão conectados (45), que não são defeito.
    /// </summary>
    public static List<DeviceProblem> ReadProblems()
    {
        var list = new List<DeviceProblem>();
        WmiQuery.ForEach("SELECT Name, Description, PNPClass, ConfigManagerErrorCode, Present FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0", o =>
        {
            int code = WmiQuery.GetString(o, "ConfigManagerErrorCode") is { } c && int.TryParse(c, out int n) ? n : 0;
            if (code is 0 or 22 or 45 || WmiQuery.GetNullableBool(o, "Present") == false) return;
            string name = WmiQuery.GetString(o, "Name") is { Length: > 0 } nm ? nm : WmiQuery.GetString(o, "Description") ?? "Dispositivo desconhecido";
            list.Add(new DeviceProblem(name, WmiQuery.GetString(o, "PNPClass") ?? "", code, ProblemMeaning(code)));
        }, diagnosticOp: "drivers.problems");
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Os códigos do Gerenciador de Dispositivos em português claro (os mais comuns; o resto vira "código N").</summary>
    internal static string ProblemMeaning(int code) => code switch
    {
        1 or 40 => "Configuração do driver incorreta",
        3 or 39 => "Driver corrompido ou ausente",
        10 => "O dispositivo não consegue iniciar",
        12 => "Sem recursos livres para o dispositivo",
        14 => "Reinicie o PC para o dispositivo funcionar",
        18 or 24 => "Reinstale o driver",
        19 => "Registro do dispositivo com problema",
        28 => "Sem driver: o aparelho não funciona",
        29 => "Desativado pela BIOS",
        31 => "O driver não carregou direito",
        32 => "O serviço do driver está desligado",
        37 or 41 => "O driver não conseguiu iniciar o dispositivo",
        38 => "Reinicie o PC: o driver antigo ainda está na memória",
        43 => "O Windows parou o dispositivo após um erro",
        47 => "Pronto para ser removido com segurança",
        48 => "Driver bloqueado por incompatibilidade",
        52 => "Assinatura do driver não verificada",
        _ => $"Problema código {code}",
    };

    public static List<GpuDriverStatus> ReadGpus()
    {
        var list = new List<GpuDriverStatus>();
        WmiQuery.ForEach("SELECT Name, DriverVersion, AdapterCompatibility FROM Win32_VideoController", o =>
        {
            string name = WmiQuery.GetString(o, "Name") ?? "";
            if (name.Length == 0 || HardwareNaming.IsMicrosoftBasicAdapter(name)) return;
            string maker = (WmiQuery.GetString(o, "AdapterCompatibility") ?? "") + " " + name;
            string ver = WmiQuery.GetString(o, "DriverVersion") ?? "";
            var g = new GpuDriverStatus { GpuName = name };
            if (maker.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                g.Vendor = "NVIDIA";
                g.Installed = HardwareNaming.NvidiaDriverVersion(ver) ?? ver;
                g.OfficialPage = "https://www.nvidia.com/Download/index.aspx";
            }
            else if (maker.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                     || maker.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                     || maker.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase))
            {
                g.Vendor = "AMD";
                g.Installed = ver;
                g.OfficialPage = "https://www.amd.com/en/support/download/drivers.html";
            }
            else if (maker.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                g.Vendor = "Intel";
                g.Installed = ver;
                g.OfficialPage = "https://www.intel.com/content/www/us/en/download-center/home.html";
            }
            else
            {
                g.Vendor = "";
                g.Installed = ver;
            }
            list.Add(g);
        });
        return list;
    }

    public static BoardSupportInfo ReadBoard()
    {
        var b = new BoardSupportInfo();
        var board = WmiQuery.FirstOrDefault("SELECT Manufacturer, Product FROM Win32_BaseBoard",
            o => (Maker: WmiQuery.GetString(o, "Manufacturer") ?? "", Model: WmiQuery.GetString(o, "Product") ?? ""));
        b.Maker = board.Maker ?? "";
        b.Model = board.Model ?? "";

        var bios = WmiQuery.FirstOrDefault("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS", o =>
        {
            string formatted = WmiCalendarDate.Format(WmiQuery.GetString(o, "ReleaseDate"));
            return (Version: WmiQuery.GetString(o, "SMBIOSBIOSVersion") ?? "", Date: formatted == "—" ? "" : formatted);
        });
        b.BiosVersion = bios.Version ?? "";
        b.BiosDate = bios.Date ?? "";

        var sysInfo = WmiQuery.FirstOrDefault("SELECT Manufacturer, Model FROM Win32_ComputerSystem",
            o => (Maker: WmiQuery.GetString(o, "Manufacturer") ?? "", Model: WmiQuery.GetString(o, "Model") ?? ""));
        string sys = sysInfo.Maker ?? "";
        string sysModel = sysInfo.Model ?? "";

        string combined = b.Maker + " " + sys;
        string query = Uri.EscapeDataString(b.Model.Length > 0 && !b.Model.Contains("Default", StringComparison.OrdinalIgnoreCase) ? b.Model : sysModel);
        foreach (var (tokens, brand, urlFactory) in BoardVendors)
        {
            if (tokens.Any(t => combined.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                b.Brand = brand;
                b.SupportUrl = urlFactory(query);
                break;
            }
        }
        if (b.Maker.Length == 0) b.Maker = sys;
        if (b.Model.Length == 0) b.Model = sysModel;
        return b;
    }
}
