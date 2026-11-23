using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoreBi.Desktop;

internal static partial class Program
{
    private static readonly int[] StripTimes = [0, 40, 80, 120, 170, 230, 300, 380, 480, 600, 800, 1100];
    private static readonly int[] StartupTimes = [0, 150, 300, 500, 750, 1000, 1500, 2000, 2500, 3000, 4000, 6000];

    /// <summary>
    /// Filma a abertura do CoreBi e a troca de páginas com dados reais, sem nada aparecer na tela (a mesma janela invisível dos
    /// prints). Para cada página, primeira visita e volta, salva uma tira de quadros (0 a 1,1 s depois do clique) e anota no
    /// relatório o que um olho atento veria como tranco: cartão que muda de tamanho ou de lugar depois do primeiro quadro, cartão
    /// que surge do nada e esqueleto que liga depois de a página já ter aparecido (o conteúdo vazio pisca antes dele).
    /// Uso: --motion pasta [páginas separadas por vírgula]
    /// </summary>
    private static int Motion(string[] args)
    {
        string folder = Path.GetFullPath(args.Length > 1 ? args[1] : "artifacts/motion");
        Directory.CreateDirectory(folder);
        string data = Path.Combine(folder, "settings");
        Directory.CreateDirectory(data);
        string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoreBi", "settings.json");
        if (File.Exists(real)) File.Copy(real, Path.Combine(data, "settings.json"), overwrite: true);
        Environment.SetEnvironmentVariable("COREBI_DATA_DIR", data);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");

        var app = new TestApplication();
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
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
        var report = new StringBuilder();
        report.AppendLine($"Animações do Windows: {(SystemParameters.ClientAreaAnimation ? "ligadas" : "DESLIGADAS (nada anima)")}");
        try
        {
            // Abertura: a Visão geral do primeiro quadro até 6 s, enquanto o hardware é lido (o Loaded da janela começa a leitura)
            var film = new Film(window, () => window.PageDash);
            window.Show();
            film.Run(6200, 90);
            film.Save(Path.Combine(folder, "00-abertura.png"), StartupTimes, "Abertura (Visão geral)");
            film.Report(report, "Abertura · Visão geral", settleMs: 0);
            Wait(3000);

            var navs = window.NavPanel.Children.OfType<RadioButton>().ToList();
            var wanted = args.Length > 2 && args[2].Length > 0 ? args[2].Split(',') : null;
            var order = navs.Where(n => wanted == null || wanted.Contains((string)n.Tag)).ToList();
            for (int round = 1; round <= 2; round++)
            {
                foreach (var nav in order)
                {
                    string page = (string)nav.Tag;
                    if (page == nameof(MainWindow.PageDash) && round == 1)
                    {
                        // A Visão geral já está aberta: sai e volta, para ela entrar como qualquer outra
                        navs.First(n => (string)n.Tag != page).IsChecked = true;
                        Wait(1500);
                    }
                    var target = (FrameworkElement)typeof(MainWindow).GetField(page, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                    Wait(1200);
                    var shot = new Film(window, () => target);
                    shot.Start();
                    nav.IsChecked = true;
                    shot.Run(1300, 15);
                    string name = $"{(round == 1 ? "1" : "2")}-{page}-{(round == 1 ? "primeira" : "volta")}";
                    shot.Save(Path.Combine(folder, name + ".png"), StripTimes, $"{page} · {(round == 1 ? "primeira visita" : "volta")}");
                    shot.Report(report, $"{page} · {(round == 1 ? "primeira visita" : "volta")}", settleMs: 0);
                    Console.WriteLine(name);
                    Wait(2500); // leituras lentas (discos, drivers) terminam antes da próxima página
                    if (round == 2) AuditPlans(target, report);
                }
            }
            File.WriteAllText(Path.Combine(folder, "motion.txt"), report.ToString());
            Console.WriteLine(report);
            return 0;
        }
        finally
        {
            window.Close();
            app.Shutdown();
        }
    }

    /// <summary>
    /// Confere a altura que cada planta de esqueleto reserva com a altura do painel já com os dados: diferença aqui é o painel
    /// mudando de tamanho (e empurrando o que vem embaixo) quando o esqueleto sai. Num painel esticado pela grade (linha *), a
    /// planta menor que o painel não importa; a maior, sim.
    /// </summary>
    private static void AuditPlans(FrameworkElement page, StringBuilder sb)
    {
        void Walk(DependencyObject node)
        {
            if (node is UIElement { Visibility: not Visibility.Visible }) return;
            if (node is Border b && Skeleton.GetLayout(b) is { Length: > 0 } plan && !Skeleton.GetActive(b))
            {
                double reserved = Skeleton.GetTop(b) + SkeletonPlan.Height(plan) + b.Padding.Top + b.Padding.Bottom + b.BorderThickness.Top + b.BorderThickness.Bottom;
                double real = b.ActualHeight, delta = reserved - real;
                string name = b.Name.Length > 0 ? b.Name : "(cartão sem nome)";
                sb.AppendLine($"  PLANTA {name}: esqueleto {reserved:0.#} px, com dados {real:0.#} px{(Math.Abs(delta) > 0.5 ? $"  Δ {delta:+0.#;-0.#}" : "  ok")}");
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(page);
    }

    /// <summary>Quadros de uma página e o estado dos cartões dela em cada quadro.</summary>
    private sealed class Film
    {
        private sealed record Card(string Name, Rect Box, bool Skeleton, double Opacity);
        private sealed record Frame(double Ms, BitmapSource? Image, List<Card> Cards, bool Visible);

        private const double Scale = 0.25;
        private readonly MainWindow _window;
        private readonly Func<FrameworkElement> _page;
        private readonly List<Frame> _frames = new();
        private readonly Stopwatch _clock = new();
        private double _lastShot = double.MinValue;
        private int _minGap;

        public Film(MainWindow window, Func<FrameworkElement> page)
        {
            _window = window;
            _page = page;
        }

        public void Start() => _clock.Restart();

        public void Run(int ms, int minGap)
        {
            if (!_clock.IsRunning) _clock.Restart();
            _minGap = minGap;
            CompositionTarget.Rendering += OnRendering;
            try { Wait(Math.Max(1, ms - (int)_clock.ElapsedMilliseconds)); }
            finally { CompositionTarget.Rendering -= OnRendering; }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            double t = _clock.Elapsed.TotalMilliseconds;
            if (t - _lastShot < _minGap) return;
            _lastShot = t;
            var page = _page();
            _frames.Add(new Frame(t, Capture(), Cards(page), page.IsVisible));
        }

        private BitmapSource? Capture()
        {
            if (_window.Content is not FrameworkElement root || root.ActualWidth < 1) return null;
            int w = (int)(root.ActualWidth * Scale), h = (int)(root.ActualHeight * Scale);
            var area = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(Scale, Scale));
                dc.DrawRectangle((Brush)Application.Current.FindResource("Bg"), null, area);
                dc.DrawRectangle(new VisualBrush(root) { Viewbox = area, ViewboxUnits = BrushMappingMode.Absolute }, null, area);
            }
            var image = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            image.Render(visual);
            image.Freeze();
            return image;
        }

        private static Style? _cardStyle;

        /// <summary>Cartões visíveis da página, com a caixa de layout (sem os deslocamentos da animação) relativa à página.</summary>
        private static List<Card> Cards(FrameworkElement page)
        {
            _cardStyle ??= Application.Current.TryFindResource("CardBorder") as Style;
            var list = new List<Card>();
            var counts = new Dictionary<string, int>();
            void Walk(DependencyObject node, Vector offset, double opacity)
            {
                if (node is UIElement { Visibility: not Visibility.Visible }) return;
                if (node is Visual v && !ReferenceEquals(node, page)) offset += VisualTreeHelper.GetOffset(v);
                if (node is UIElement ui) opacity *= ui.Opacity;
                if (node is Border b && IsCard(b.Style) && b.ActualWidth > 0)
                {
                    string key = b.Name.Length > 0 ? b.Name : $"cartão {Describe(b)}";
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                    if (counts[key] > 1) key += $" #{counts[key]}";
                    list.Add(new Card(key, new Rect(offset.X, offset.Y, b.ActualWidth, b.ActualHeight), Skeleton.GetActive(b), opacity));
                }
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i), offset, opacity);
            }
            Walk(page, default, 1);
            return list;
        }

        /// <summary>Um cartão sem nome é descrito pelo primeiro texto dentro dele (o título).</summary>
        private static string Describe(DependencyObject node)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n is TextBlock { Text.Length: > 0 } t) return $"\"{t.Text}\"";
                for (int i = VisualTreeHelper.GetChildrenCount(n) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(n, i));
            }
            return "sem texto";
        }

        private static bool IsCard(Style? style)
        {
            for (; style != null; style = style.BasedOn)
                if (ReferenceEquals(style, _cardStyle)) return true;
            return false;
        }

        public void Save(string file, int[] times, string title)
        {
            var shots = times.Select(t => _frames.Where(f => f.Image != null).OrderBy(f => Math.Abs(f.Ms - t)).FirstOrDefault()).Where(f => f != null).ToList();
            if (shots.Count == 0) return;
            int cols = 4, rows = (shots.Count + cols - 1) / cols;
            int w = shots[0]!.Image!.PixelWidth, h = shots[0]!.Image!.PixelHeight;
            const int gap = 8, label = 22;
            var visual = new DrawingVisual();
            int width = cols * w + (cols + 1) * gap, height = rows * (h + label) + (rows + 1) * gap + 30;
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
                dc.DrawText(Text(title, 16), new Point(gap, 6));
                for (int i = 0; i < shots.Count; i++)
                {
                    var f = shots[i]!;
                    double x = gap + i % cols * (w + gap), y = 30 + gap + i / cols * (h + label + gap);
                    dc.DrawText(Text($"{f.Ms:0} ms", 13), new Point(x, y));
                    dc.DrawImage(f.Image, new Rect(x, y + label, w, h));
                }
            }
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            image.Render(visual);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(file);
            png.Save(stream);
        }

        private static FormattedText Text(string s, double size) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.White, 1);

        /// <summary>
        /// O que mudou nos cartões depois do primeiro quadro em que a página aparece: caixa (tamanho ou lugar), cartão novo ou
        /// sumido, e esqueleto ligando ou desligando (ligar depois do primeiro quadro é o pisca do conteúdo vazio).
        /// </summary>
        public void Report(StringBuilder sb, string title, double settleMs)
        {
            sb.AppendLine();
            sb.AppendLine($"== {title} ({_frames.Count} quadros, intervalo máx. {MaxGap():0} ms)");
            var shown = _frames.Where(f => f.Visible && f.Cards.Count > 0).ToList();
            if (shown.Count == 0) { sb.AppendLine("  página sem cartões visíveis"); return; }
            int issues = 0;
            for (int i = 1; i < shown.Count; i++)
            {
                var before = shown[i - 1].Cards.ToDictionary(c => c.Name);
                var now = shown[i].Cards.ToDictionary(c => c.Name);
                double t = shown[i].Ms;
                foreach (var c in now.Values)
                {
                    if (!before.TryGetValue(c.Name, out var p)) { sb.AppendLine($"  {t,6:0} ms  SURGIU   {c.Name} {Fmt(c.Box)}"); issues++; continue; }
                    if (Moved(p.Box, c.Box)) { sb.AppendLine($"  {t,6:0} ms  MEXEU    {c.Name} {Fmt(p.Box)} -> {Fmt(c.Box)}"); issues++; }
                    if (c.Skeleton != p.Skeleton) sb.AppendLine($"  {t,6:0} ms  {(c.Skeleton ? "ESQ. LIGOU (pisca)" : "esq. saiu")} {c.Name}");
                    if (c.Skeleton && !p.Skeleton) issues++;
                }
                foreach (var c in before.Values.Where(c => !now.ContainsKey(c.Name))) { sb.AppendLine($"  {t,6:0} ms  SUMIU    {c.Name}"); issues++; }
            }
            var first = shown[0];
            sb.AppendLine($"  1º quadro ({first.Ms:0} ms): {first.Cards.Count} cartões, esqueleto em {first.Cards.Count(c => c.Skeleton)}: {string.Join(", ", first.Cards.Where(c => c.Skeleton).Select(c => c.Name))}");
            if (issues == 0) sb.AppendLine("  sem trancos");
        }

        private double MaxGap()
        {
            double gap = 0;
            for (int i = 1; i < _frames.Count; i++) gap = Math.Max(gap, _frames[i].Ms - _frames[i - 1].Ms);
            return gap;
        }

        private static bool Moved(Rect a, Rect b) =>
            Math.Abs(a.X - b.X) > 0.5 || Math.Abs(a.Y - b.Y) > 0.5 || Math.Abs(a.Width - b.Width) > 0.5 || Math.Abs(a.Height - b.Height) > 0.5;

        private static string Fmt(Rect r) => $"({r.X:0},{r.Y:0} {r.Width:0}×{r.Height:0})";
    }
}
