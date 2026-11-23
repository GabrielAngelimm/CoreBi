using System.Text.RegularExpressions;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Tools.Drivers;

/// <summary>Inventário local via WMI de drivers assinados, GPUs e placa-mãe/BIOS.</summary>
public static class DriverInventory
{
    private static readonly HashSet<string> MainClasses = new(StringComparer.Ordinal)
    {
        "DISPLAY", "NET", "MEDIA", "BLUETOOTH", "HDC", "SCSIADAPTER", "USB", "SYSTEM",
    };

    /// <summary>
    /// O atalho da placa-mãe por fabricante. ASUS, MSI e ASRock montam o endereço da página do modelo a partir do nome que o
    /// Windows dá à placa (Direct = true). A Gigabyte separa cada revisão da placa numa página (Rev. 1.0, 1.3...), o Windows não
    /// diz qual é a sua (o campo vem "x.x") e a busca do próprio site não abre por link; o atalho busca as páginas de suporte do
    /// modelo dentro de gigabyte.com, onde cada revisão aparece. Os outros abrem a central de suporte.
    /// </summary>
    private static readonly (string[] Tokens, string Brand, Func<string, (string Url, bool Direct)> SupportUrl)[] BoardVendors =
    {
        (new[] { "ASUS" }, "ASUS", m => ("https://www.asus.com/supportonly/" + Uri.EscapeDataString(m) + "/helpdesk_download/", true)),
        (new[] { "Micro-Star", "MSI" }, "MSI", m => ("https://www.msi.com/Motherboard/" + MsiSlug(m) + "/support", true)),
        (new[] { "GIGABYTE" }, "Gigabyte", m => ("https://www.google.com/search?q=" + Uri.EscapeDataString($"site:gigabyte.com \"{m}\" support"), false)),
        (new[] { "ASRock" }, "ASRock", m => AsrockPlatform(m) is { } p
            ? ($"https://www.asrock.com/mb/{p}/{Uri.EscapeDataString(m)}/index.asp#Download", true)
            : ("https://www.asrock.com/support/index.asp", false)),
        (new[] { "Dell" }, "Dell", _ => ("https://www.dell.com/support/home/", false)),
        (new[] { "LENOVO" }, "Lenovo", _ => ("https://support.lenovo.com/", false)),
        (new[] { "HP", "Hewlett" }, "HP", _ => ("https://support.hp.com/", false)),
        (new[] { "Acer" }, "Acer", _ => ("https://www.acer.com/support", false)),
    };

    /// <summary>"MAG B550 TOMAHAWK (MS-7C91)" vira "MAG-B550-TOMAHAWK", o trecho do endereço das páginas da MSI.</summary>
    internal static string MsiSlug(string model)
    {
        string name = Regex.Replace(model, @"\(MS-[^)]*\)", "").Trim();
        return Uri.EscapeDataString(Regex.Replace(name, @"\s+", "-"));
    }

    /// <summary>
    /// O site da ASRock separa as placas por plataforma. Os chipsets AMD têm números próprios (B550, X670...); qualquer outro
    /// chipset com letra e três dígitos é Intel. Sem chipset no nome, null.
    /// </summary>
    internal static string? AsrockPlatform(string model)
    {
        var m = Regex.Match(model, @"\b([ABHQXZW])(\d{3})");
        if (!m.Success) return null;
        int n = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        bool amd = m.Groups[1].Value is "A" or "B" or "X" && n is 320 or 350 or 370 or 399 or 450 or 470 or 520 or 550 or 570 or 620 or 650 or 670 or 840 or 850 or 870;
        return amd ? "AMD" : "Intel";
    }

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
        28 => "Sem driver instalado",
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
        string query = (b.Model.Length > 0 && !b.Model.Contains("Default", StringComparison.OrdinalIgnoreCase) ? b.Model : sysModel).Trim();
        foreach (var (tokens, brand, urlFactory) in BoardVendors)
        {
            if (tokens.Any(t => combined.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                b.Brand = brand;
                (b.SupportUrl, b.SupportIsModelPage) = query.Length > 0 ? urlFactory(query) : ("", false);
                break;
            }
        }
        if (b.Maker.Length == 0) b.Maker = sys;
        if (b.Model.Length == 0) b.Model = sysModel;
        return b;
    }
}
