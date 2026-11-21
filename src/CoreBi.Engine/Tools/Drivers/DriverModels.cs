namespace CoreBi.Engine.Tools.Drivers;

/// <summary>Uma linha do inventário de drivers instalados.</summary>
public sealed class DriverRow
{
    public string Device { get; init; } = "";
    public string Class { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Version { get; init; } = "";
    public string Date { get; init; } = "";
    public bool Signed { get; init; }
}

/// <summary>Resultado da consulta de um driver de vídeo.</summary>
public sealed class GpuDriverStatus
{
    public string Vendor { get; set; } = "";
    public string GpuName { get; set; } = "";
    public string Installed { get; set; } = "";
    public string Latest { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string OfficialPage { get; set; } = "";
    public string Message { get; set; } = "";

    public bool CanAutoInstall => Vendor is "NVIDIA" or "AMD" && DownloadUrl.Length > 0;
    public bool UpdateAvailable =>
        Vendor == "NVIDIA" && Latest.Length > 0 && Installed.Length > 0 && CompareVersions(Latest, Installed) > 0;

    internal static int CompareVersions(string a, string b) =>
        Version.TryParse(a, out var left) && Version.TryParse(b, out var right) ? left.CompareTo(right) : 0;
}

/// <summary>Placa-mãe, BIOS estruturada e página oficial de suporte do fabricante.</summary>
public sealed class BoardInfo
{
    public string Maker { get; set; } = "";
    public string Model { get; set; } = "";
    public string BiosVersion { get; set; } = "";
    public string BiosDate { get; set; } = "";
    public string Brand { get; set; } = "";
    public string SupportUrl { get; set; } = "";

    public string Bios => BiosVersion.Length == 0 ? ""
        : BiosDate.Length > 0 && BiosDate != "—" ? $"{BiosVersion} ({BiosDate})" : BiosVersion;
}

/// <summary>Um driver disponível no Windows Update.</summary>
public sealed class WuDriver
{
    public string Title { get; init; } = "";
    public long SizeBytes { get; init; }
    public string UpdateId { get; init; } = "";
    public int Revision { get; init; }
}
