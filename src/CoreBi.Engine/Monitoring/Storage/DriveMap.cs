using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Monitoring.Storage;

public sealed class DriveCard
{
    public string Letter { get; init; } = "";
    public string Label { get; init; } = "";
    public string Kind { get; init; } = "";
    public string FileSystem { get; init; } = "";
    public string Disk { get; init; } = "";
    public long Total { get; init; }
    public long Free { get; init; }
    public bool Removable { get; init; }

    public double UsedFraction => Total > 0 ? (double)(Total - Free) / Total : 0;
    public string UsedPercent => $"{UsedFraction:P0}";
    public string Title => string.IsNullOrWhiteSpace(Label) ? Letter : $"{Letter}  {Label}";

    public static string Fmt(long bytes) => bytes <= 0 ? "0 B" : ByteFormat.Binary(bytes);
}

public static class DriveMap
{
    public static List<DriveCard> Read()
    {
        var disks = WmiDiskTopology.ReadDisksByLetter();
        var list = new List<DriveCard>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                string letter = d.Name.TrimEnd('\\');
                disks.TryGetValue(letter, out var disk);
                bool usb = disk.Bus?.Equals("USB", StringComparison.OrdinalIgnoreCase) == true;
                string kind = d.DriveType switch
                {
                    DriveType.Removable => "Removível",
                    DriveType.Fixed when usb => "Externo (USB)",
                    DriveType.Fixed => "Interno",
                    DriveType.Network => "Rede",
                    DriveType.CDRom => "Óptico",
                    _ => d.DriveType.ToString()
                };
                list.Add(new DriveCard
                {
                    Letter = letter,
                    Label = d.VolumeLabel,
                    Kind = kind,
                    FileSystem = d.DriveFormat,
                    Disk = disk.Model ?? "",
                    Total = d.TotalSize,
                    Free = d.TotalFreeSpace,
                    Removable = d.DriveType == DriveType.Removable || usb,
                });
            }
            catch { /* unidade removida durante a enumeração */ }
        }
        return list.OrderBy(c => c.Letter).ToList();
    }
}
