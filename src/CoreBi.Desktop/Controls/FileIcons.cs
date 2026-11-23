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
    private static readonly ConcurrentDictionary<string, bool> Glyphs = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Cache.GetOrAdd(path, Load);
    }

    /// <summary>
    /// O ícone é um glifo de uma cor só (os apps da Store sem placa, como o do ChatGPT, vêm brancos e somem no tema claro)? Aí a tela
    /// pinta o desenho com a cor do texto, como o Windows faz na barra de tarefas.
    /// </summary>
    public static bool IsGlyph(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return Glyphs.GetOrAdd(path, p => Get(p) is BitmapSource b && Monochrome(b));
    }

    /// <summary>Quase todo pixel visível sem cor (cinza) e com o mesmo brilho: um desenho só, não um ícone colorido.</summary>
    internal static bool Monochrome(BitmapSource source)
    {
        try
        {
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            int seen = 0, gray = 0;
            double sum = 0, sumSq = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                byte a = px[i + 3];
                if (a < 96) continue;
                // Pré-multiplicado não: Bgra32 é direto; ainda assim, o brilho vem da cor sem o alfa
                int b = px[i], g = px[i + 1], r = px[i + 2];
                int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                seen++;
                if (max - min < 28) gray++;
                double l = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
                sum += l;
                sumSq += l * l;
            }
            if (seen < 24) return false;
            double mean = sum / seen, spread = Math.Sqrt(Math.Max(0, sumSq / seen - mean * mean));
            return gray >= seen * 0.9 && spread < 0.12;
        }
        catch { return false; }
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
