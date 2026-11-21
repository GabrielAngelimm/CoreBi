using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Power;
using Microsoft.Win32;

namespace CoreBi.Engine.Monitoring.Inventory;

internal sealed record ComputerSystemSnapshot(
    string Domain,
    bool PartOfDomain,
    string Workgroup,
    bool HypervisorPresent,
    string? Manufacturer,
    string? Model,
    bool? AutomaticManagedPagefile);

internal static class OsInventoryReader
{
    public static ComputerSystemSnapshot ReadComputerSystem()
    {
        var snap = WmiQuery.FirstOrDefault(
            "SELECT Domain, PartOfDomain, Workgroup, HypervisorPresent, Manufacturer, Model, AutomaticManagedPagefile FROM Win32_ComputerSystem",
            cs =>
            {
                bool partOfDomain = WmiQuery.GetNullableBool(cs, "PartOfDomain") == true;
                string domain = WmiQuery.GetString(cs, "Domain") ?? "—";
                string workgroup = WmiQuery.GetString(cs, "Workgroup") ?? domain;
                string? rawMaker = WmiQuery.GetString(cs, "Manufacturer");
                string? rawModel = WmiQuery.GetString(cs, "Model");
                return new ComputerSystemSnapshot(
                    domain,
                    partOfDomain,
                    workgroup,
                    WmiQuery.GetNullableBool(cs, "HypervisorPresent") == true,
                    Fmt.IsPlaceholder(rawMaker) ? null : rawMaker,
                    Fmt.IsPlaceholder(rawModel) ? null : rawModel,
                    WmiQuery.GetNullableBool(cs, "AutomaticManagedPagefile"));
            });
        return snap ?? new ComputerSystemSnapshot("—", false, "—", false, null, null, null);
    }

    public static OsInfo ReadOs(ComputerSystemSnapshot computerSystem)
    {
        string caption = "Windows", build = "—";
        DateTime? installed = null, boot = null;
        WmiQuery.ForEachOrThrow("SELECT Caption, BuildNumber, InstallDate, LastBootUpTime FROM Win32_OperatingSystem", os =>
        {
            caption = (WmiQuery.GetString(os, "Caption") ?? "Windows").Replace("Microsoft ", "");
            build = WmiQuery.GetString(os, "BuildNumber") ?? "—";
            installed = CimDate(os["InstallDate"]);
            boot = CimDate(os["LastBootUpTime"]);
        });

        string version = "—";
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            if (key?.GetValue("DisplayVersion") is string dv) version = dv;
            else if (key?.GetValue("ReleaseId") is string rid) version = rid;
            if (key?.GetValue("UBR") is int ubr && build != "—") build = $"{build}.{ubr}";
        }

        string group = computerSystem.PartOfDomain ? computerSystem.Domain : computerSystem.Workgroup;
        string arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "64 bits (x64)",
            Architecture.Arm64 => "64 bits (ARM64)",
            Architecture.X86 => "32 bits (x86)",
            var a => a.ToString()
        };

        // Última atualização instalada (KB mais recente). As datas vêm em M/d/yyyy independente do idioma.
        string? kb = null;
        DateTime? updated = null;
        var enUs = CultureInfo.GetCultureInfo("en-US");
        WmiQuery.ForEach("SELECT HotFixID, InstalledOn FROM Win32_QuickFixEngineering", q =>
        {
            if (q["InstalledOn"]?.ToString() is string on &&
                DateTime.TryParse(on, enUs, DateTimeStyles.None, out var when) &&
                (updated == null || when > updated))
            {
                updated = when;
                kb = q["HotFixID"]?.ToString();
            }
        });

        return new OsInfo(caption, caption, version, build, arch, installed, boot, Environment.MachineName,
            Environment.UserName, group, computerSystem.PartOfDomain, CultureInfo.InstalledUICulture.NativeName, kb, updated);
    }

    public static WindowsSettings ReadSettings()
    {
        static int? Dword(RegistryKey root, string path, string name)
        {
            try { using var k = root.OpenSubKey(path); return k?.GetValue(name) is int v ? v : null; }
            catch { return null; }
        }

        int? gameMode = Dword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
        int? hags = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        int? hvci = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
        int? fast = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled");
        int? capture = Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
        bool? mouse = null;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            if (k?.GetValue("MouseSpeed") is string ms) mouse = ms != "0";
        }
        catch { }

        return new WindowsSettings(
            gameMode != 0, // sem a chave, o Modo de jogo vem ligado
            hags switch { 2 => true, 1 => false, _ => null },
            hvci is int h && h == 1,
            fast is int f ? f == 1 : null,
            TimeZoneInfo.Local.DisplayName,
            capture is int c ? c == 1 : null,
            mouse,
            PowerPlans.ReadActiveDisplayName(),
            ReadActivated());
    }

    public static WindowsSecurity ReadSecurity()
    {
        // Defender: só conta quando é ele o antivírus ligado (com outro antivírus fica em modo passivo).
        bool? realTime = null, tamper = null;
        DateTime? signatures = null;
        string? av = ReadAntivirus();
        if (av?.Contains("Defender", StringComparison.OrdinalIgnoreCase) == true)
        {
            WmiQuery.ForEach("SELECT RealTimeProtectionEnabled, IsTamperProtected, AntivirusSignatureLastUpdated FROM MSFT_MpComputerStatus", d =>
            {
                realTime = d["RealTimeProtectionEnabled"] as bool?;
                tamper = d["IsTamperProtected"] as bool?;
                signatures = CimDate(d["AntivirusSignatureLastUpdated"]);
            }, @"root\Microsoft\Windows\Defender");
        }

        // UAC: o nível do controle deslizante sai de EnableLUA, ConsentPromptBehaviorAdmin e PromptOnSecureDesktop.
        string? uac = null;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            if (k?.GetValue("EnableLUA") is int lua)
            {
                int consent = k.GetValue("ConsentPromptBehaviorAdmin") is int c ? c : 5;
                bool secure = k.GetValue("PromptOnSecureDesktop") is not int sd || sd == 1;
                uac = lua == 0 ? "Desativado" : consent switch
                {
                    0 => "Nunca notificar",
                    2 => "Sempre notificar",
                    5 => secure ? "Padrão (avisa quando apps pedem alterações)" : "Avisa sem escurecer a tela",
                    _ => "Personalizado"
                };
            }
        }
        catch { }

        return new WindowsSecurity(av, realTime, tamper, signatures, uac);
    }

    public static Dictionary<string, bool> ReadBitLocker()
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        WmiQuery.ForEach("SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume", v =>
        {
            if (WmiQuery.GetString(v, "DriveLetter") is { Length: > 0 } letter && WmiQuery.GetInt32(v, "ProtectionStatus") is int status and (0 or 1))
                map[letter] = status == 1;
        }, @"root\CIMV2\Security\MicrosoftVolumeEncryption");
        return map;
    }

    /// <summary>
    /// Antivírus que a Central de Segurança do Windows registra como ligados. No productState, o nibble dos bits 12–15
    /// vale 1 quando a proteção em tempo real está ativa. "" quando há antivírus, mas nenhum ligado.
    /// </summary>
    private static string? ReadAntivirus()
    {
        var on = new List<string>();
        bool any = false;
        WmiQuery.ForEach("SELECT displayName, productState FROM AntiVirusProduct", av =>
        {
            any = true;
            if (((WmiQuery.GetInt32(av, "productState") >> 12) & 0xF) == 1 && WmiQuery.GetString(av, "displayName") is { Length: > 0 } name)
                on.Add(name);
        }, @"root\SecurityCenter2");
        return !any ? null : string.Join(", ", on.Distinct());
    }

    // GUID do produto Windows OS no SoftwareLicensingProduct.
    private static bool? ReadActivated()
    {
        bool? activated = null;
        WmiQuery.ForEach("SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL", l =>
        {
            if (WmiQuery.GetInt32(l, "LicenseStatus") == 1) activated = true;
            else activated ??= false;
        });
        return activated;
    }

    private static DateTime? CimDate(object? v)
    {
        try { return v is string s && s.Length >= 14 ? ManagementDateTimeConverter.ToDateTime(s) : null; }
        catch { return null; }
    }
}
