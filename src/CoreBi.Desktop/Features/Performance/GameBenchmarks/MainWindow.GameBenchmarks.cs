using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CoreBi.Engine.Games;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Performance.Benchmarks;
using CoreBi.Engine.Persistence;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    // ---------------- Benchmark: palco, régua e histórico comuns ----------------

    /// <summary>Célula da régua do resultado (e da coluna ao vivo): rótulo, número, unidade e uma linha de contexto (Tone = Ok ou Warn acende um LED).</summary>
    public sealed record BenchStat(string Label, string Value, string Unit = "", string Sub = "", string Tone = "", string? Tip = null);
    /// <summary>Medição de memória e disco: nome, leitura, unidade, contexto visível e a explicação (dica).</summary>
    public sealed record MemRow(string Label, string Value, string Unit, string Sub = "", string? Hint = null);
    /// <summary>Linha do histórico: resultado principal, detalhe, data e a fração do melhor resultado comparável (0–1).</summary>
    public sealed record BenchHistoryRow(string Date, string Value, string Detail, double Fraction);

    internal bool _gameBenchRunning;
    internal GpuGameBench? _gpuBench;
    internal bool _gpuBenchActive;
    internal CancellationTokenSource? _gameBenchCts;
    internal System.Windows.Media.Imaging.WriteableBitmap? _simBitmap;
    internal int[] _simPixels = Array.Empty<int>();
    internal const int SimView = 440;

    internal bool AnyBenchRunning => _workloads.IsBusy || BenchRunning || _gameBenchRunning;

    internal void BenchTest_Checked(object sender, RoutedEventArgs e)
    {
        if (BenchPanelRender == null) return; // durante InitializeComponent
        string test = (string)((FrameworkElement)sender).Tag;
        BenchPanelRender.Visibility = test == "render" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelGpu.Visibility = test == "gpu" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelRt.Visibility = test == "rt" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelSim.Visibility = test == "sim" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelMem.Visibility = test == "mem" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelDisk.Visibility = test == "disk" ? Visibility.Visible : Visibility.Collapsed;
        if (test == "disk") InitDiskBench();
        UpdateBenchPlates();
        UpdateGameBenchHistory();
    }

    /// <summary>Réguas e tabelas com os rótulos e travessões antes do primeiro teste, para o palco não mudar de altura ao terminar.</summary>
    internal void ResetBenchStages()
    {
        GbResult.ItemsSource = GpuStats(null);
        RtResult.ItemsSource = RtStats(null);
        SimResult.ItemsSource = SimStats(null);
        MemResult.ItemsSource = MemRows(null, null, null);
        DiskResult.ItemsSource = DiskRows(null);
    }

    /// <summary>Placa de cada palco: a peça que o teste mede e o que a identifica.</summary>
    internal void UpdateBenchPlates()
    {
        string cpu = HardwareCatalog.ShortCpuName(_cpuName);
        if (cpu.Length == 0) cpu = _cpuName.Length > 0 ? _cpuName : "Processador";
        BenchDevice.Text = SimDevice.Text = cpu;
        BenchDeviceSpec.Text = SimDeviceSpec.Text = $"{Environment.ProcessorCount} threads";
        GbDevice.Text = RtDevice.Text = _gpus.Count > 0 ? _gpus[0].Name : "Placa de vídeo";

        string temp = System.IO.Path.GetPathRoot(System.IO.Path.GetTempPath())?.TrimEnd('\\') ?? "";
        MemDeviceSpec.Text = MemoryInfo.ReadPhysicalUsage() is { } ram
            ? $"{ram.Total / 1073741824.0:0} GB instalados, carregamento pela unidade {temp}"
            : $"Carregamento pela unidade {temp}";
        UpdateDiskPlate();
    }

    /// <summary>Partes do resumo salvo ("valor · detalhe · detalhe"), o mesmo formato que a Visão geral lê.</summary>
    internal static string[] SummaryParts(string summary) =>
        summary.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Comparação com o melhor resultado anterior comparável; vazio quando não há anterior.</summary>
    internal static string VsBest(double score, IEnumerable<double> previous)
    {
        var prev = previous.Where(v => v > 0 && double.IsFinite(v)).ToList();
        if (prev.Count == 0 || score <= 0) return "";
        double pct = (score / prev.Max() - 1) * 100;
        if (Math.Abs(pct) < 0.5) return "igual ao melhor anterior";
        return pct > 0 ? $"{pct:0} % acima do melhor anterior" : $"{-pct:0} % abaixo do melhor anterior";
    }

    internal void SaveBenchRecord(string test, double score, string summary)
    {
        var kind = BenchEvent(new BenchRecord(DateTime.Now, test, score, summary)); // ícone e canal do teste
        AddEvent(kind.Icon, "Benchmark concluído", summary, "", kind.Channel);
        _settings.BenchRecords.Insert(0, new BenchRecord(DateTime.Now, test, score, summary));
        // Mantém os 10 mais recentes de cada teste.
        _settings.BenchRecords = _settings.BenchRecords.GroupBy(r => r.Test).SelectMany(g => g.Take(10))
                                          .OrderByDescending(r => r.Date).ToList();
        SaveSettings();
        UpdateGameBenchHistory();
    }

    internal void UpdateGameBenchHistory()
    {
        IEnumerable<BenchRecord> Of(Func<BenchRecord, bool> test) => _settings.BenchRecords.Where(test);
        // A barra só compara o que é comparável: a cena de jogo na mesma resolução, o ray tracing no mesmo preset e resolução,
        // o disco na mesma unidade.
        GbHistory.ItemsSource = History(Of(r => r.Test == "gpu"), r => SummaryParts(r.Summary).LastOrDefault() ?? "");
        RtHistory.ItemsSource = History(Of(r => r.Test.StartsWith("rt:")), r => r.Test);
        SimHistory.ItemsSource = History(Of(r => r.Test == "sim"), _ => "");
        MemHistory.ItemsSource = History(Of(r => r.Test == "mem"), _ => "");
        DiskHistory.ItemsSource = History(Of(r => r.Test == "disk"), r => SummaryParts(r.Summary).FirstOrDefault() ?? "", valueIndex: 1);
    }

    internal static List<BenchHistoryRow> History(IEnumerable<BenchRecord> records, Func<BenchRecord, string> group, int valueIndex = 0)
    {
        var list = records.OrderByDescending(r => r.Date).ToList();
        var best = list.GroupBy(group).ToDictionary(g => g.Key, g => g.Max(r => r.Score));
        return list.Select(r =>
        {
            var parts = SummaryParts(r.Summary);
            string value = valueIndex < parts.Length ? parts[valueIndex] : r.Summary;
            string detail = string.Join(", ", parts.Where((_, i) => i != valueIndex));
            double b = best[group(r)];
            return new BenchHistoryRow($"{r.Date:dd/MM HH:mm}", value, detail, b > 0 ? r.Score / b : 0);
        }).ToList();
    }

    internal void SetGameBenchRunning(Button btn, bool running)
    {
        _gameBenchRunning = running;
        Ui.SetRunning(btn, running, "Iniciar teste", "Cancelar");
        BenchTestPanel.IsEnabled = !running;
    }

    // ----- Cena de jogo (GPU) -----

    internal static List<BenchStat> GpuStats(GpuGameResult? r, string vs = "") => new()
    {
        new("Nota", r is null ? "—" : $"{r.Score:N0}", "pts", vs),
        new("FPS médio", r is null ? "—" : $"{r.AvgFps:0.0}", "FPS"),
        new("1% low", r is null ? "—" : $"{r.Low1Fps:0.0}", "FPS", Tip: "Média do 1% de quadros mais lentos: mostra os engasgos que o FPS médio esconde."),
        new("Quadros", r is null ? "—" : $"{r.Frames:N0}"),
        new("Resolução", r is null ? "—" : $"{r.Width} × {r.Height}"),
    };

    internal async void BtnGpuBench_Click(object sender, RoutedEventArgs e)
    {
        if (_workloads.ActiveOperation == "GPU raster") { _gpuBench?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { GbStatus.Text = "Termine o outro teste antes."; GbDetail.Text = ""; return; }
        using var workload = _workloads.TryBegin("GPU raster");
        if (workload == null) { GbStatus.Text = "Termine o outro teste antes."; GbDetail.Text = ""; return; }

        var size = (CheckedTag(GbResPanel) ?? "1920x1080").Split('x').Select(int.Parse).ToArray();
        int secs = int.Parse(CheckedTag(GbDurPanel) ?? "60");
        _gpuBench = new GpuGameBench
        {
            Width = size[0],
            Height = size[1],
            Duration = TimeSpan.FromSeconds(secs),
            AdapterName = _gpus.Count > 0 ? _gpus[0].Name : ""
        };
        SetGameBenchRunning(BtnGpuBench, true);
        GbResult.ItemsSource = GpuStats(null);
        GbTrace.Clear();
        GbStatus.Text = $"Rodando em {size[0]} × {size[1]}";
        GbDetail.Text = "";
        var bench = _gpuBench;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) =>
        {
            GbFpsNow.Text = bench.CurrentFps > 0 ? $"{bench.CurrentFps:0}" : "—";
            GbProgress.ScaleX = bench.Progress;
            GbTrace.Add(bench.Progress, bench.CurrentFps);
            GbDetail.Text = $"{bench.Progress * 100:0} %";
        }, Dispatcher);
        timer.Start();
        _gpuBenchActive = true;
        GbGpuNow.Text = "Sensores pausados durante o teste";
        bool overlayWasOn = ChkOverlay.IsChecked == true;
        if (overlayWasOn) ChkOverlay.IsChecked = false; // o overlay também consulta a GPU

        // Thread dedicada: a cena roda num loop próprio com prioridade alta.
        GpuGameResult? r = null;
        try
        {
            r = await Task.Factory.StartNew(bench.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch (Exception ex) { GbStatus.Text = "Falhou: " + ex.Message; GbDetail.Text = ""; return; }
        finally
        {
            timer.Stop();
            _gpuBenchActive = false;
            if (overlayWasOn) ChkOverlay.IsChecked = true;
            GbGpuNow.Text = "";
            SetGameBenchRunning(BtnGpuBench, false);
            GbProgress.ScaleX = 0;
            GbFpsNow.Text = "—";
        }

        if (r == null)
        {
            GbStatus.Text = bench.Cancelled ? "Cancelado" : $"Falhou: {bench.Error}";
            GbDetail.Text = "";
            return;
        }
        string vs = VsBest(r.Score, _settings.BenchRecords
            .Where(x => x.Test == "gpu" && SummaryParts(x.Summary).LastOrDefault() == $"{r.Height}p").Select(x => x.Score));
        GbStatus.Text = $"Concluído em {TimeSpan.FromSeconds(r.Seconds):m\\:ss}";
        GbDetail.Text = "";
        GbResult.ItemsSource = GpuStats(r, vs);
        SaveBenchRecord("gpu", r.Score, $"{r.Score:N0} pts · {r.AvgFps:0} FPS · {r.Height}p");
    }

    // ----- Simulação de jogo (CPU) -----

    internal static List<BenchStat> SimStats(GameSimResult? r, string vs = "") => new()
    {
        new("FPS médio", r is null ? "—" : $"{r.AvgFps:0}", "FPS", vs, Tip: "A nota do teste é o FPS médio."),
        new("1% low", r is null ? "—" : $"{r.Low1Fps:0}", "FPS", Tip: "Média do 1% de quadros mais lentos."),
        new("Thread principal", r is null ? "—" : $"{r.MainThreadMs:0.00}", "ms/quadro", Tip: "Grade espacial, lógica e preparo do desenho, numa thread só."),
        new("Física e IA", r is null ? "—" : $"{r.ParallelMs:0.00}", "ms/quadro", Tip: "Física, colisão e IA das unidades, em paralelo nas outras threads."),
    };

    internal async void BtnSimBench_Click(object sender, RoutedEventArgs e)
    {
        if (_workloads.ActiveOperation == "CPU simulation") { _gameBenchCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { SimStatus.Text = "Termine o outro teste antes."; SimDetail.Text = ""; return; }
        using var workload = _workloads.TryBegin("CPU simulation");
        if (workload == null) { SimStatus.Text = "Termine o outro teste antes."; SimDetail.Text = ""; return; }

        int secs = int.Parse(CheckedTag(SimDurPanel) ?? "20");
        _simBitmap ??= new System.Windows.Media.Imaging.WriteableBitmap(SimView, SimView, 96, 96, PixelFormats.Bgra32, null);
        if (_simPixels.Length == 0) _simPixels = new int[SimView * SimView];
        SimImage.Source = _simBitmap;
        SimIdle.Visibility = Visibility.Collapsed;

        var sim = new GameSimBench();
        _gameBenchCts = CancellationTokenSource.CreateLinkedTokenSource(_features.Lifetime);
        var ct = _gameBenchCts.Token;
        SetGameBenchRunning(BtnSimBench, true);
        SimResult.ItemsSource = SimStats(null);
        SimStatus.Text = "Aquecendo";
        SimDetail.Text = "";
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) =>
        {
            DrawSim(sim);
            SimProgress.ScaleX = sim.Progress;
            if (sim.FramesDone > 0) { SimStatus.Text = "Simulando"; SimDetail.Text = $"{sim.Progress * 100:0} %"; }
            SimFpsNow.Text = sim.CurrentFps > 0 ? $"{sim.CurrentFps:0}" : "—";
        }, Dispatcher);
        timer.Start();

        GameSimResult r;
        try
        {
            r = await Task.Run(() => sim.Run(TimeSpan.FromSeconds(secs), ct));
            if (ct.IsCancellationRequested) { SimStatus.Text = "Cancelado"; SimDetail.Text = ""; return; }
        }
        catch (OperationCanceledException) { SimStatus.Text = "Cancelado"; SimDetail.Text = ""; return; }
        catch (Exception ex) { SimStatus.Text = "Falhou: " + ex.Message; SimDetail.Text = ""; return; }
        finally
        {
            timer.Stop();
            SetGameBenchRunning(BtnSimBench, false);
            SimProgress.ScaleX = 0;
            SimFpsNow.Text = "—";
            _gameBenchCts?.Dispose();
            _gameBenchCts = null;
        }

        string vs = VsBest(r.Score, _settings.BenchRecords.Where(x => x.Test == "sim").Select(x => x.Score));
        SimStatus.Text = $"Concluído em {TimeSpan.FromSeconds(r.Seconds):m\\:ss}";
        SimDetail.Text = $"{r.Frames:N0} quadros";
        SimResult.ItemsSource = SimStats(r, vs);
        SaveBenchRecord("sim", r.Score, $"{r.AvgFps:0} FPS · 1% low {r.Low1Fps:0}");
    }

    /// <summary>Vista de cima da batalha: time A em branco, time B em cinza.</summary>
    internal void DrawSim(GameSimBench sim)
    {
        Array.Fill(_simPixels, unchecked((int)0xFF050505));
        float[] xs = sim.ViewX, ys = sim.ViewY; byte[] team = sim.ViewTeam;
        float k = SimView / GameSimBench.World;
        for (int i = 0; i < GameSimBench.Entities; i++)
        {
            int px = (int)(xs[i] * k), py = (int)(ys[i] * k);
            if ((uint)px < SimView && (uint)py < SimView)
                _simPixels[py * SimView + px] = team[i] == 0 ? unchecked((int)0xFFF5F5F5) : unchecked((int)0xFF5A5A5A);
        }
        _simBitmap!.WritePixels(new Int32Rect(0, 0, SimView, SimView), _simPixels, SimView * 4, 0);
    }

    // ----- Memória e carregamento -----

    /// <summary>As seis medições, com travessão nas que ainda não foram feitas (a tabela já nasce com a altura final).</summary>
    internal static List<MemRow> MemRows(double? lat, (double Read, double Copy)? bw, LoadResult? load)
    {
        static string F(double? v, string format) => v is double d ? d.ToString(format, CultureInfo.CurrentCulture) : "—";
        string drive = load?.Drive.TrimEnd('\\') ?? "";
        return new()
        {
            new("Latência da RAM", F(lat, "0.0"), "ns", "", "Acesso aleatório à memória. Quanto menor, melhor."),
            new("Leitura da RAM", F(bw?.Read, "0.0"), "GB/s", "", "Todas as threads lendo em sequência."),
            new("Cópia da RAM", F(bw?.Copy, "0.0"), "GB/s", "", "Leitura e escrita ao mesmo tempo."),
            new("Leitura do pacote do mapa", F(load?.SeqReadMBs, "N0"), "MiB/s", load is null ? "" : $"512 MB em sequência na unidade {drive}",
                "Leitura sequencial, sem o cache do Windows."),
            new("Arquivos pequenos", F(load?.FilesPerSec, "N0"), "arq/s", load is null ? "" : $"{load.SmallFilesMBs:N0} MiB/s",
                "Mil arquivos de 64 KB lidos por 8 threads, sem o cache do Windows."),
            new("Carregamento do mapa", F(load?.MapLoadSeconds, "0.00"), "s", "", "Pacote e arquivos pequenos juntos. Quanto menor, mais rápido o jogo carrega."),
        };
    }

    internal async void BtnMemBench_Click(object sender, RoutedEventArgs e)
    {
        if (_workloads.ActiveOperation == "RAM and loading") { _gameBenchCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { MemStatus.Text = "Termine o outro teste antes."; MemDetail.Text = ""; return; }
        using var workload = _workloads.TryBegin("RAM and loading");
        if (workload == null) { MemStatus.Text = "Termine o outro teste antes."; MemDetail.Text = ""; return; }

        _gameBenchCts = CancellationTokenSource.CreateLinkedTokenSource(_features.Lifetime);
        var ct = _gameBenchCts.Token;
        SetGameBenchRunning(BtnMemBench, true);
        double? lat = null;
        (double Read, double Copy)? bw = null;
        LoadResult? load = null;
        void Show() => MemResult.ItemsSource = MemRows(lat, bw, load);
        Show();
        (string Phase, double Frac) progress = ("", 0);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) =>
        {
            MemStatus.Text = progress.Phase;
            MemDetail.Text = $"{progress.Frac * 100:0} %";
            MemProgress.ScaleX = progress.Frac;
        }, Dispatcher);
        timer.Start();

        try
        {
            progress = ("Medindo a latência da RAM", 0.05);
            lat = await Task.Run(() => MemoryBench.MeasureLatencyNs(ct));
            ct.ThrowIfCancellationRequested();
            Show();

            progress = ("Medindo a banda da RAM", 0.2);
            bw = await Task.Run(() => MemoryBench.MeasureBandwidth(ct));
            ct.ThrowIfCancellationRequested();
            Show();

            load = await Task.Run(() => MemoryBench.MeasureLoading((phase, f) => progress = (phase, 0.35 + f * 0.6), ct));
            Show();

            MemStatus.Text = "Concluído";
            MemDetail.Text = "Arquivos de teste apagados";
            // Nota para a barra do histórico: mais rápido = maior (inverso da latência × velocidade de carga).
            SaveBenchRecord("mem", 1000 / lat.Value + 10 / load.MapLoadSeconds,
                            $"{lat:0} ns · {bw.Value.Read:0} GB/s · mapa {load.MapLoadSeconds:0.00} s");
        }
        catch (OperationCanceledException) { MemStatus.Text = "Cancelado"; MemDetail.Text = "Arquivos de teste apagados"; }
        catch (Exception ex) { MemStatus.Text = $"Falhou: {ex.Message}"; MemDetail.Text = ""; }
        finally
        {
            timer.Stop();
            MemProgress.ScaleX = 0;
            SetGameBenchRunning(BtnMemBench, false);
        }
    }

}
