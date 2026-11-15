using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoreBi.Desktop;
using CoreBi.Engine.Monitoring.Storage;

internal static partial class Program
{
    /// <summary>
    /// Prints das páginas com dados reais, sem nada aparecer na tela: a janela abre fora da área visível, transparente, sem
    /// ativar e fora da barra de tarefas, e cada página é desenhada num PNG nos temas pedidos. Usa uma cópia das configurações
    /// do usuário, que nunca é gravada de volta. Elevado (Start-Process -Verb RunAs), os sensores do processador também aparecem.
    /// Uso: --shots pasta [segundos de amostra] [páginas separadas por vírgula, "PageSystem:Memória RAM" abre uma ficha] [dark,light]
    /// (a lista de páginas vai entre aspas quando tem espaço, como em "PageDash,PageSystem:Memória RAM").
    /// </summary>
    private static int Shots(string[] args)
    {
        string folder = Path.GetFullPath(args.Length > 1 ? args[1] : "artifacts/shots");
        int seconds = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 20;
        string[] pages = args.Length > 3 && args[3].Length > 0 ? args[3].Split(',')
            : ["PageDash", "PageSystem", "PageSensors", "PageDisks", "PageEnergy", "PageNet", "PageOverlay"];
        string[] themes = args.Length > 4 ? args[4].Split(',') : ["dark", "light"];
        Directory.CreateDirectory(folder);
        string data = Path.Combine(folder, "settings");
        Directory.CreateDirectory(data);
        string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoreBi", "settings.json");
        if (File.Exists(real)) File.Copy(real, Path.Combine(data, "settings.json"), overwrite: true);
        Environment.SetEnvironmentVariable("COREBI_DATA_DIR", data);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");

        var app = new TestApplication();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var window = new MainWindow
        {
            WindowState = WindowState.Normal,
            WindowStartupLocation = WindowStartupLocation.Manual,
            AllowsTransparency = true,
            Opacity = 0,
            ShowActivated = false,
            ShowInTaskbar = false,
            Left = -30000,
            Top = -30000,
            Width = 1920,
            Height = 1032,
        };
        window.Show();
        try
        {
            PumpDispatcher(window.LoadHardwareAsync());
            Wait(seconds * 1000);
            foreach (string theme in themes)
            {
                ThemeManager.Apply(theme == "light");
                window.UpdateThemeButton();
                window.RedrawThemedVisuals();
                Wait(1500);
                foreach (string page in pages)
                {
                    string[] parts = page.Split(':', 2);
                    if (typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                            .Select(f => f.GetValue(window)).OfType<RadioButton>().FirstOrDefault(n => n.Tag as string == parts[0]) is not { } nav)
                    {
                        Console.WriteLine("Página sem item no menu: " + parts[0]);
                        continue;
                    }
                    nav.IsChecked = false;
                    nav.IsChecked = true;
                    Wait(parts[0] is "PageDisks" or "PageEnergy" or "PageSystem" ? 4000 : 2500);
                    if (parts[0] == "PageNet") ShowNetworkSample(window);
                    if (parts[0] == "PageSystem" && parts.Length == 2) { window.PageSystem.Select(parts[1]); Wait(800); }
                    Wait(600);
                    string file = Path.Combine(folder, $"{page.Replace(':', '-').Replace(' ', '-')}-{theme}.png");
                    SaveWindow(window, file);
                    Console.WriteLine(file);
                }
            }
            return 0;
        }
        finally
        {
            window.Close();
            app.Shutdown();
        }
    }

    /// <summary>Rede: um resultado de exemplo (o teste de verdade consome dados e leva um minuto).</summary>
    private static void ShowNetworkSample(MainWindow window)
    {
        var internet = new CoreBi.Engine.Monitoring.Network.PingResult("Internet", "1.1.1.1", Enumerable.Range(0, 20).Select(i => (double?)(9 + i % 4)).ToArray());
        var router = new CoreBi.Engine.Monitoring.Network.PingResult("Roteador", "192.168.0.1", Enumerable.Range(0, 20).Select(i => (double?)(1 + i % 3 * 0.4)).ToArray());
        var sample = new CoreBi.Engine.Monitoring.Network.NetworkResult
        {
            Card = new DiskHealthCard { Meta = "Ethernet · exemplo", Verdict = DiskVerdict.Good, Label = "ESTÁVEL" },
            Adapter = new CoreBi.Engine.Monitoring.Network.NetworkAdapterInfo("Ethernet", "Adaptador de rede", "Ethernet (cabo)", 1000,
                "192.168.0.12", System.Net.IPAddress.Parse("192.168.0.1"), "1.1.1.1", "00-00-00-00-00-00"),
            Router = router,
            Internet = [internet],
            Notes = ["Latência baixa e sem perda nos alvos testados."],
            DownloadMbps = 486,
            UploadMbps = 212,
            LatencyMs = 11,
            JitterMs = 1.4,
            LossPct = 0,
            DnsMs = 14,
            LoadedSamples = Enumerable.Range(0, 20).Select(i => (double?)(16 + i % 5 * 2)).ToArray(),
            LoadedLatencyMs = 20,
            BufferbloatMs = 9,
            LocalVerdict = DiskVerdict.Good,
            InternetVerdict = DiskVerdict.Good,
        };
        window.PageNet.GetType().GetMethod("ShowResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window.PageNet, [sample]);
        Wait(400);
    }

    private static void SaveWindow(MainWindow window, string file)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var area = new Rect(0, 0, width, height);
            context.DrawRectangle((Brush)Application.Current.FindResource("Bg"), null, area);
            // Viewbox fixo na área da janela: um brilho que vaza da borda não encolhe nem desloca o print
            context.DrawRectangle(new VisualBrush(root) { Viewbox = area, ViewboxUnits = BrushMappingMode.Absolute }, null, area);
        }
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(file);
        png.Save(stream);
    }

    private static void Wait(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
