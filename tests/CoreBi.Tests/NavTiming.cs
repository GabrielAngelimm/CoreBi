using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CoreBi.Desktop;

internal static partial class Program
{
    /// <summary>
    /// Mede a navegação com dados reais, sem nada aparecer na tela (mesma janela invisível dos prints): para cada página, quanto
    /// tempo a troca segura a interface, quando sai o primeiro quadro e o maior intervalo entre quadros no primeiro segundo
    /// (um intervalo grande é a animação engasgando). Mede também quanto custa a leitura de 1 s dos sensores e o quanto o tique de 1 s congela a interface.
    /// Uso: --nav-timing pasta [segundos de amostra] [voltas]
    /// </summary>
    private static int NavTiming(string[] args)
    {
        string folder = Path.GetFullPath(args.Length > 1 ? args[1] : "artifacts/nav-timing");
        int seconds = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 15;
        int rounds = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 2;
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
        var frames = new List<long>();
        var clock = Stopwatch.StartNew();
        CompositionTarget.Rendering += (_, _) => frames.Add(clock.ElapsedTicks);
        window.Show();
        var results = new List<object>();
        try
        {
            var load = Stopwatch.StartNew();
            PumpDispatcher(window.LoadHardwareAsync());
            double loadMs = load.Elapsed.TotalMilliseconds;
            Wait(seconds * 1000);

            // Leitura de 1 s: só os sensores e o tique inteiro (todas as páginas), com a Visão geral à vista
            var refresh = new List<double>();
            for (int i = 0; i < 10; i++)
            {
                var w = Stopwatch.StartNew();
                window._monitor.Poll();
                refresh.Add(w.Elapsed.TotalMilliseconds);
                Wait(200);
            }
            // Congelamento da interface pelo tique de 1 s: maior intervalo entre quadros em 4 s parados na Visão geral
            frames.Clear();
            long idleStart = clock.ElapsedTicks;
            Wait(4000);
            double tickGap = 0;
            long last = idleStart;
            foreach (long f in frames.ToList()) { tickGap = Math.Max(tickGap, Ms(f - last)); last = f; }
            Console.WriteLine($"Carga do hardware: {loadMs:0} ms");
            Console.WriteLine($"Leitura dos sensores (Poll, fora da janela): média {refresh.Average():0.0} ms, máx. {refresh.Max():0.0} ms");
            Console.WriteLine($"Maior intervalo entre quadros em 4 s parado (tique de 1 s): {tickGap:0} ms");
            results.Add(new { kind = "tick", loadMs, refreshAvg = refresh.Average(), refreshMax = refresh.Max(), idleMaxFrameGapMs = tickGap });

            var navs = window.NavPanel.Children.OfType<RadioButton>().ToList();
            Console.WriteLine();
            Console.WriteLine($"{"página",-16} {"volta",5} {"bloqueio",9} {"1º quadro",10} {"ocioso",8} {"maior intervalo",16}");
            for (int round = 1; round <= rounds; round++)
            {
                foreach (var nav in navs.Prepend(navs[^1]))
                {
                    string page = (string)nav.Tag;
                    Wait(1200);
                    frames.Clear();
                    long start = clock.ElapsedTicks;
                    nav.IsChecked = true;
                    double block = Ms(clock.ElapsedTicks - start);
                    Drain();
                    double idle = Ms(clock.ElapsedTicks - start);
                    Wait(1000);
                    var after = frames.Where(f => f >= start).ToList();
                    double first = after.Count > 0 ? Ms(after[0] - start) : -1;
                    double gap = 0;
                    long prev = start;
                    foreach (long f in after) { gap = Math.Max(gap, Ms(f - prev)); prev = f; }
                    Console.WriteLine($"{page,-16} {round,5} {block,7:0} ms {first,8:0} ms {idle,6:0} ms {gap,14:0} ms");
                    results.Add(new { kind = "nav", page, round, blockMs = block, firstFrameMs = first, idleMs = idle, maxFrameGapMs = gap, frames = after.Count });
                }
            }
            File.WriteAllText(Path.Combine(folder, "nav-timing.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally
        {
            window.Close();
            app.Shutdown();
        }

        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>Espera a fila da interface esvaziar até a prioridade de ociosidade (layout, render e trabalho adiado já rodaram).</summary>
    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
