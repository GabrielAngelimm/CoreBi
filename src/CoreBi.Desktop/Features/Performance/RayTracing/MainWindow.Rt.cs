using System.Windows.Threading;
using CoreBi.Engine.Performance.Benchmarks;
using System.Windows;

namespace CoreBi.Desktop;

/// <summary>Benchmark · Ray tracing (DXR 1.1): aquecimento + 3 passadas, nota pela mediana e variação entre passadas.</summary>
public partial class MainWindow
{
    internal GpuRtBench? _rtBench;

    internal static string PresetName(string p) => p switch { "normal" => "Normal", "extrema" => "Extrema", _ => "Alta" };

    /// <summary>Régua do ray tracing; a variação entre passadas diz se o resultado serve para comparar (abaixo de 3 %).</summary>
    internal static List<BenchStat> RtStats(GpuRtResult? r, string vs = "")
    {
        string passes = r is null ? "" : string.Join("\n", r.Passes.Select((p, i) => $"Passada {i + 1}: {p.AvgFps:0.0} FPS, 1% low {p.Low1Fps:0.0}"));
        string reliability = r is null ? ""
            : r.Reliable ? "\nAbaixo de 3 %: as passadas concordam e o resultado serve para comparar."
            : "\nAcima de 3 %: algo atrapalhou (programa em segundo plano, temperatura ou limite de energia). Feche outros programas e rode de novo antes de comparar.";
        return new()
        {
            new("Nota", r is null ? "—" : $"{r.Score:N0}", "pts", vs,
                Tip: "Referência medida: AMD Radeon RX 9070, preset Alta em 1440p, modo Padrão: 878 pts (87,8 FPS, 1% low 77), variação de 0,5 %."),
            new("FPS, mediana", r is null ? "—" : $"{r.MedianFps:0.0}", "FPS"),
            new("1% low, mediana", r is null ? "—" : $"{r.MedianLow1:0.0}", "FPS"),
            new("Variação entre passadas", r is null ? "—" : $"{r.VariationPct:0.0}", "%",
                r is null ? "" : r.Reliable ? "Confiável" : "Instável, rode de novo", r is null ? "" : r.Reliable ? "Ok" : "Warn",
                r is null ? null : passes + reliability),
            new("Raios por segundo", r is null ? "—" : $"{r.GigaRaysPerSec:0.0}", "bilhões", r is null ? "" : "estimado",
                Tip: "Estimado pelas amostras e rebatidas por pixel do preset."),
        };
    }

    internal async void BtnRtBench_Click(object sender, RoutedEventArgs e)
    {
        if (_workloads.ActiveOperation == "GPU ray tracing") { _rtBench?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { RtStatus.Text = "Termine o outro teste antes."; RtDetail.Text = ""; return; }
        using var workload = _workloads.TryBegin("GPU ray tracing");
        if (workload == null) { RtStatus.Text = "Termine o outro teste antes."; RtDetail.Text = ""; return; }

        string adapterName = _gpus.Count > 0 ? _gpus[0].Name : "";
        RtStatus.Text = "Verificando suporte a ray tracing…";
        RtDetail.Text = "";
        string? why = await Task.Run(() => GpuRtBench.CheckSupport(adapterName));
        if (why != null) { RtStatus.Text = why; return; }

        var size = (CheckedTag(RtResPanel) ?? "2560x1440").Split('x').Select(int.Parse).ToArray();
        string preset = CheckedTag(RtPresetPanel) ?? "alta";
        bool quick = CheckedTag(RtModePanel) == "quick";
        _rtBench = new GpuRtBench
        {
            Width = size[0],
            Height = size[1],
            Preset = preset,
            AdapterName = adapterName,
            Passes = 3,
            Warmup = TimeSpan.FromSeconds(quick ? 5 : 20),
            PassDuration = TimeSpan.FromSeconds(quick ? 10 : 30),
        };
        var bench = _rtBench;
        SetGameBenchRunning(BtnRtBench, true);
        RtResult.ItemsSource = RtStats(null);
        RtTrace.Clear();
        RtStatus.Text = "Rodando";
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) =>
        {
            RtFpsNow.Text = bench.CurrentFps > 0 ? $"{bench.CurrentFps:0}" : "—";
            RtProgress.ScaleX = bench.Progress;
            RtTrace.Add(bench.Progress, bench.CurrentFps);
            RtStatus.Text = bench.Phase.Length > 0 ? bench.Phase : "Rodando";
            RtDetail.Text = $"{bench.Progress:P0}";
        }, Dispatcher);
        timer.Start();

        // Igual à cena de jogo: sensores e overlay pausados (consultas ao driver geram engasgos no frametime).
        _gpuBenchActive = true;
        bool overlayWasOn = ChkOverlay.IsChecked == true;
        if (overlayWasOn) ChkOverlay.IsChecked = false;
        GpuRtResult? r = null;
        try
        {
            r = await Task.Factory.StartNew(bench.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch (Exception ex) { RtStatus.Text = "Falhou: " + ex.Message; RtDetail.Text = ""; return; }
        finally
        {
            timer.Stop();
            _gpuBenchActive = false;
            if (overlayWasOn) ChkOverlay.IsChecked = true;
            SetGameBenchRunning(BtnRtBench, false);
            RtProgress.ScaleX = 0;
            RtFpsNow.Text = "—";
        }

        if (r == null)
        {
            RtStatus.Text = bench.Cancelled ? "Cancelado" : $"Falhou: {bench.Error}";
            RtDetail.Text = "";
            return;
        }
        string key = $"rt:{r.Preset}:{r.Height}";
        string vs = VsBest(r.Score, _settings.BenchRecords.Where(x => x.Test == key).Select(x => x.Score));
        string driver = await Task.Run(() => DriverVersionOf(r.Adapter));
        RtStatus.Text = "Concluído";
        RtDetail.Text = driver.Length > 0 ? $"Driver {driver}" : "";
        RtResult.ItemsSource = RtStats(r, vs);
        SaveBenchRecord(key, r.Score, $"{r.Score:N0} pts · {r.MedianFps:0} FPS · {PresetName(r.Preset)} {r.Height}p" + (r.Reliable ? "" : " · instável"));
    }

    internal static string DriverVersionOf(string adapter)
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
            foreach (System.Management.ManagementObject mo in s.Get())
                if (mo["Name"] is string n && adapter.Contains(n.Trim(), StringComparison.OrdinalIgnoreCase) || mo["Name"] is string n2 && n2.Contains(adapter, StringComparison.OrdinalIgnoreCase))
                    return mo["DriverVersion"]?.ToString() ?? "";
        }
        catch { }
        return "";
    }
}
