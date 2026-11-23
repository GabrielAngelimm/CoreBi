using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoreBi.Desktop;

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
                window.ApplyTheme(theme == "light");
                Wait(1500);
                foreach (string page in pages)
                {
                    string[] parts = page.Split(':', 2);
                    if (window.NavPanel.Children.OfType<RadioButton>().FirstOrDefault(n => n.Tag as string == parts[0]) is not { } nav)
                    {
                        Console.WriteLine("Página sem item no menu: " + parts[0]);
                        continue;
                    }
                    nav.IsChecked = false;
                    nav.IsChecked = true;
                    Wait(parts[0] is "PageDisks" or "PageEnergy" or "PageSystem" ? 4000 : 2500);
                    if (parts[0] == "PageNet") ShowNetworkSample(window);
                    // "PageSystem:report" abre a janela de exportação sobre a página (sem salvar nada) e fecha depois da captura
                    bool report = parts[0] == "PageSystem" && parts.Length == 2 && parts[1] == "report";
                    if (report) { _ = window.PageSystem.ExportReportAsync(); Wait(900); }
                    else if (parts[0] == "PageSystem" && parts.Length == 2) { window.PageSystem.Select(parts[1]); Wait(800); }
                    // "PageSensors:cpu" escreve na busca (sem dar foco), para conferir o texto digitado dentro do campo
                    else if (parts[0] == "PageSensors" && parts.Length == 2) { window.PageSensors.SensorSearch.Text = parts[1]; Wait(800); }
                    // "PageDisks:3" repete o disco em três teclas, para ver a faixa de discos num PC que só tem um
                    else if (parts[0] == "PageDisks" && parts.Length == 2 && int.TryParse(parts[1], out int copies)) { window.PageDisks.PreviewRack(copies); Wait(800); }
                    // "PageApps:pack" marca os kits Pós-formatação e Jogos (sem instalar nada), para ver o pacote e o filtro com apps
                    else if (parts[0] == "PageApps" && parts.Length == 2 && parts[1] == "pack")
                    {
                        foreach (var a in window.PageApps._installApps ?? [])
                            if (!a.Installed && (a.Kits & (CoreBi.Engine.Tools.Apps.AppKit.PostFormat | CoreBi.Engine.Tools.Apps.AppKit.Games)) != 0) a.Selected = true;
                        Wait(800);
                    }
                    // "PageApps:confirm" abre a confirmação com o pacote marcado antes (sem instalar) e fecha depois da captura
                    else if (parts[0] == "PageApps" && parts.Length == 2 && parts[1] == "confirm") { window.PageApps.PreviewConfirm(); Wait(900); }
                    // "PageApps:installing" mostra a fila no meio (um instalado, um instalando, o resto na fila), sem instalar nada
                    else if (parts[0] == "PageApps" && parts.Length == 2 && parts[1] == "installing") { window.PageApps.PreviewRun(true); Wait(900); }
                    // "PageApps:Jogos" filtra pela categoria, como o clique no título dela (volta a todas depois da captura)
                    else if (parts[0] == "PageApps" && parts.Length == 2) { window.PageApps.FilterCategory(parts[1]); Wait(800); }
                    // "PageStartup:demo" mostra três removidos de exemplo no filtro Removidos (nada é lido nem salvo)
                    else if (parts[0] == "PageStartup" && parts.Length == 2) { window.PageStartup.PreviewDemo(parts[1]); Wait(900); }
                    // "PageDriver:demo" mostra drivers achados no Windows Update; "PageDriver:searching", a busca em andamento; "PageDriver:backup"
                    // e "PageDriver:restore", as janelas de confirmação (nada é procurado, copiado nem instalado)
                    else if (parts[0] == "PageDriver" && parts.Length == 2) { window.PageDriver.PreviewDemo(parts[1]); Wait(900); }
                    Wait(600);
                    string file = Path.Combine(folder, $"{page.Replace(':', '-').Replace(' ', '-')}-{theme}.png");
                    SaveWindow(window, file);
                    Console.WriteLine(file);
                    // Quanto a página rola nesta janela (0 = cabe inteira): serve para conferir "tela cheia sem rolagem" em outro PC
                    if (typeof(MainWindow).GetField(parts[0], System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(window) is UserControl { Content: ScrollViewer scroll })
                        Console.WriteLine($"  {parts[0]}: rolagem {scroll.ScrollableHeight:0} px");
                    if (report) { window.ReportLayer.Cancel(); Wait(300); continue; }
                    if (parts[0] == "PageApps" && parts.Length == 2 && parts[1] == "confirm") { window.InstallLayer.Cancel(); Wait(300); continue; }
                    if (parts[0] == "PageApps" && parts.Length == 2 && parts[1] == "installing") window.PageApps.PreviewRun(false);
                    if (parts[0] == "PageApps" && parts.Length == 2 && parts[1] != "pack") window.PageApps.FilterCategory(null);
                    if (parts[0] == "PageStartup" && parts.Length == 2) window.PageStartup.PreviewDemo(null);
                    if (parts[0] == "PageDriver" && parts.Length == 2) window.PageDriver.PreviewDemo(null);
                    if (theme == themes[0]) SaveSkeletons(window, Path.Combine(folder, $"{page.Replace(':', '-').Replace(' ', '-')}-skeleton.png"));
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
            Card = new CoreBi.Engine.Monitoring.Health.HealthCard { Meta = "Ethernet · exemplo", Verdict = CoreBi.Engine.Monitoring.Health.HealthVerdict.Good, Label = "ESTÁVEL" },
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
            LocalVerdict = CoreBi.Engine.Monitoring.Health.HealthVerdict.Good,
            InternetVerdict = CoreBi.Engine.Monitoring.Health.HealthVerdict.Good,
        };
        // Histórico de exemplo só na memória: o de agora em cima, escolhido, e testes anteriores com um dia de bufferbloat
        var now = DateTime.Now;
        var runs = new List<CoreBi.Engine.Monitoring.Network.NetworkRun> { CoreBi.Engine.Monitoring.Network.NetworkHistory.ToRun(sample) };
        double[][] past = [[478, 208, 12], [491, 214, 11], [302, 96, 48], [469, 205, 13], [488, 211, 11], [455, 199, 14], [483, 210, 12], [476, 207, 12]];
        for (int i = 0; i < past.Length; i++)
            runs.Add(runs[0] with
            {
                Date = now.AddHours(-(i * 19 + 6)),
                DownloadMbps = past[i][0],
                UploadMbps = past[i][1],
                LatencyMs = past[i][2],
                Verdict = past[i][2] > 40 ? CoreBi.Engine.Monitoring.Health.HealthVerdict.Warning : CoreBi.Engine.Monitoring.Health.HealthVerdict.Good,
            });
        window.PageNet.UseSampleHistory(runs);
        window.PageNet.DisplayResult(sample);
        window.PageNet.NetHistory.SelectedIndex = 0;
        Wait(600);
    }

    /// <summary>
    /// O mesmo quadro com os esqueletos da página ligados por cima dos dados, para comparar a planta de cada painel com o
    /// conteúdo de verdade (o esqueleto tem que cair nas mesmas linhas).
    /// </summary>
    private static void SaveSkeletons(MainWindow window, string file)
    {
        var panels = new List<Border>();
        void Walk(DependencyObject node)
        {
            // Planta dada no próprio painel ou no modelo dos itens (os cartões de componente da Visão geral)
            if (node is Border b && b.IsVisible && (Skeleton.GetLayout(b).Length > 0 || b.ReadLocalValue(Skeleton.LayoutProperty) != DependencyProperty.UnsetValue
                || b.ReadLocalValue(Skeleton.ShapeProperty) != DependencyProperty.UnsetValue || b.ReadLocalValue(Skeleton.TopProperty) != DependencyProperty.UnsetValue
                || b.ReadLocalValue(Skeleton.RowHeightProperty) != DependencyProperty.UnsetValue))
                panels.Add(b);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(window);
        if (panels.Count == 0) return;
        // SetCurrentValue: um esqueleto ligado por binding (Skeleton.Active="{Binding Loading}") continua ligado ao binding depois
        foreach (var p in panels) p.SetCurrentValue(Skeleton.ActiveProperty, true);
        Wait(150);
        SaveWindow(window, file);
        Console.WriteLine(file);
        foreach (var p in panels) p.SetCurrentValue(Skeleton.ActiveProperty, false);
        Wait(300);
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
