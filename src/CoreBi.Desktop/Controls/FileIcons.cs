using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoreBi.Desktop;

/// <summary>
/// Ícone de um programa a partir do arquivo dele (o .exe, a .dll do rundll32, o atalho ou a imagem de um app da Store), em 32 px
/// e congelado, para ser lido fora da thread da tela. Fica em cache pelo caminho; sem ícone, null (a linha mostra o glifo neutro).
/// </summary>
public static class FileIcons
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Cache.GetOrAdd(path, Load);
    }

    private static ImageSource? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string ext = Path.GetExtension(path);
            if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 32;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                return image;
            }
            // Ícone 32 × 32 de dentro do arquivo (o primeiro grupo, o mesmo que o Explorer mostra); atalho: o ícone do destino
            var icons = new IntPtr[1];
            var ids = new int[1];
            uint got = PrivateExtractIcons(path, 0, 32, 32, icons, ids, 1, 0);
            if (got == 0 || got == uint.MaxValue || icons[0] == IntPtr.Zero) return ShellIcon(path);
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                source.Freeze();
                return source;
            }
            finally { DestroyIcon(icons[0]); }
        }
        catch { return null; }
    }

    /// <summary>Atalho ou arquivo sem ícone próprio: o que o Windows associa a ele.</summary>
    private static ImageSource? ShellIcon(string path)
    {
        var info = new SHFILEINFO();
        IntPtr ok = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 /* SHGFI_ICON */);
        if (ok == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        finally { DestroyIcon(info.hIcon); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint count, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
}
