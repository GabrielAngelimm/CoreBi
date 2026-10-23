using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Sensors;

using CoreBi.Engine.Performance.Stress;

using System.Globalization;

using System.Windows;
using System.Windows.Controls;

using System.Windows.Media;
using System.Windows.Shapes;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    // Stress test
    internal readonly StressTester _stress = new();
    internal readonly List<double> _tempHistory = new();
    internal const int HistoryLength = 300; // 5 min a 1 amostra/s
    internal DateTime _stressStart;
    internal TimeSpan _stressDuration;
    internal double _tempLimit = 90;
    internal float _stressMaxTemp;

    // Stress de GPU (estilo FurMark)
    internal readonly GpuBurn _burn = new();
    internal bool _gpuBurnOn;
    internal double _gpuTempLimit = 85;
    internal float _gpuMaxTemp;
    internal readonly List<double> _gpuTempHistory = new();

    // ---------------- Stress test ----------------

    private string? _pendingStressStop;
    internal bool TestRunning => _stress.Running || _gpuBurnOn || _burn.Running;

    internal void BtnStress_Click(object sender, RoutedEventArgs e)
    {
        if (TestRunning) StopStress("Parado pelo usuário");
        else StartStress();
    }

    internal void ChkRam_Click(object sender, RoutedEventArgs e)
    {
        if (RamFractionPanel != null) RamFractionPanel.IsEnabled = ChkRam.IsChecked == true;
    }

    internal void ChkGpu_Click(object sender, RoutedEventArgs e)
    {
        if (GpuOptions != null) GpuOptions.IsEnabled = GpuTempLimit.IsEnabled = ChkGpu.IsChecked == true;
        if (PageStress != null) DrawChart(); // mostra/oculta a linha de limite da GPU
    }

    internal static string? CheckedTag(Panel panel)
        => panel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;

    internal void StartStress()
    {
        StressHint.Text = "";
        if (_workloads.IsBusy) { StressHint.Text = "Termine o benchmark antes de iniciar o stress."; return; }
        bool cpu = ChkCpu.IsChecked == true, ram = ChkRam.IsChecked == true, gpu = ChkGpu.IsChecked == true;
        if (!cpu && !ram && !gpu) { StressHint.Text = "Ative CPU, RAM ou GPU."; return; }

        if (!double.TryParse(TempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _tempLimit)
            || _tempLimit < 40 || _tempLimit > 110)
        {
            StressHint.Text = "Limite da CPU deve estar entre 40 e 110 °C.";
            return;
        }
        if (gpu && (!double.TryParse(GpuTempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _gpuTempLimit)
                    || _gpuTempLimit < 40 || _gpuTempLimit > 100))
        {
            StressHint.Text = "Limite da GPU deve estar entre 40 e 100 °C.";
            return;
        }
        if (!double.TryParse(TxtDuration.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double minutes) || minutes < 0)
        {
            StressHint.Text = "Duração inválida.";
            return;
        }
        if ((cpu || ram) && _monitor.CpuTemperature == null)
        {
            string why = SensorMonitor.IsPawnIOInstalled
                ? ""
                : "\n\nCausa provável: o driver PawnIO não está instalado. Instale com:\n" +
                  "winget install namazso.PawnIO\ne reabra o CoreBi.";
            var r = MessageBox.Show("Nenhum sensor de temperatura da CPU foi encontrado, então o corte automático " +
                                    "por temperatura da CPU NÃO funcionará. Continuar mesmo assim?" + why,
                                    "CoreBi", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }
        if (gpu && MainGpuTemp() == null)
        {
            var r = MessageBox.Show("Nenhum sensor de temperatura da GPU foi encontrado, então o corte automático " +
                                    "por temperatura da GPU NÃO funcionará. Continuar mesmo assim?",
                                    "CoreBi", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }

        double fraction = double.Parse(CheckedTag(RamFractionPanel) ?? "0.6", CultureInfo.InvariantCulture);

        _stressDuration = TimeSpan.FromMinutes(minutes);
        _stressStart = DateTime.Now;
        _stressMaxTemp = 0;
        _gpuMaxTemp = 0;
        _tempHistory.Clear();
        _gpuTempHistory.Clear();

        _stressLease = _workloads.TryBegin("Stress");
        if (_stressLease == null) { StressHint.Text = "Há outro teste em execução."; return; }
        try
        {
            if (cpu || ram) _stress.Start(cpu, ram, fraction);

            string gpuDesc = "não";
            if (gpu)
            {
                string res = CheckedTag(GpuResPanel) ?? "1920x1080";
                int msaa = int.Parse(CheckedTag(GpuMsaaPanel) ?? "4");
                var intensity = (CheckedTag(GpuIntensityPanel) ?? "128,48").Split(',').Select(int.Parse).ToArray();
                bool full = res == "full";
                var size = full ? new[] { 0, 0 } : res.Split('x').Select(int.Parse).ToArray();
                _burn.Start(new GpuBurnSettings
                {
                    Fullscreen = full,
                    Width = size[0],
                    Height = size[1],
                    Msaa = msaa,
                    Shells = intensity[0],
                    Iterations = intensity[1],
                    AdapterName = _gpus.Count > 0 ? _gpus[0].Name : ""
                });
                _gpuBurnOn = true;
                gpuDesc = $"sim ({(full ? "tela cheia" : res)}, {(msaa > 1 ? $"MSAA {msaa}x" : "sem MSAA")}, " +
                          $"{GpuIntensityPanel.Children.OfType<RadioButton>().First(r => r.IsChecked == true).Content}, limite {_gpuTempLimit:0} °C)";
            }

            SetStressControls(running: true);
            SetStatus("Rodando", alert: false);
            Log($"Iniciado · CPU {(cpu ? $"sim (limite {_tempLimit:0} °C)" : "não")} · RAM {(ram ? $"sim ({fraction:P0} da livre)" : "não")} · " +
                $"GPU {gpuDesc} · {(minutes > 0 ? $"{minutes:0.##} min" : "sem limite de tempo")}");
        }
        catch (Exception ex)
        {
            _stress.Stop(); _burn.Stop(); _gpuBurnOn = false;
            if (_stress.Running || _burn.Running) { _pendingStressStop = "ABORTADO: falha na inicialização"; SetStressControls(true); return; }
            _stressLease?.Dispose(); _stressLease = null;
            SetStressControls(false); StressHint.Text = "Não foi possível iniciar: " + ex.Message;
        }
    }

    internal void StopStress(string reason)
    {
        if (!TestRunning) { _stressLease?.Dispose(); _stressLease = null; return; }
        var errors = _stress.MemoryErrors;
        var passes = _stress.MemoryPasses;
        bool hadRam = _stress.MemoryAllocatedBytes > 0;
        bool hadCpu = _stress.Running;
        _stress.Stop();

        string gpuSummary = "";
        if (_gpuBurnOn)
        {
            _burn.Stop();
            if (_stress.Running || _burn.Running) { _pendingStressStop = reason; SetStatus("Parando…", false); return; }
            _gpuBurnOn = false;
            // "Pontuação" no estilo FurMark: frames renderizados e FPS médio.
            gpuSummary = $" · GPU: {_burn.Frames:N0} frames, média {_burn.AverageFps:0} FPS, temp. máx. {_gpuMaxTemp:0.#} °C";
        }
        if (_stress.Running) { _pendingStressStop = reason; SetStatus("Parando…", false); return; }
        _pendingStressStop = null;
        _stressLease?.Dispose(); _stressLease = null;
        AddEvent("", "Stress test", reason, reason.StartsWith("ABORTADO") ? "abortado" : "ok");
        SetStressControls(running: false);

        bool aborted = reason.StartsWith("ABORTADO");
        SetStatus(aborted ? "Abortado" : "Parado", alert: aborted);
        Log($"{reason} · duração {DateTime.Now - _stressStart:hh\\:mm\\:ss}" +
            (hadCpu ? $" · CPU temp. máx. {_stressMaxTemp:0.#} °C" : "") +
            (hadRam ? $" · RAM {passes} passes, {errors} erros" : "") + gpuSummary);
    }

    internal void SetStressControls(bool running)
    {
        Ui.SetRunning(BtnStress, running, "Iniciar teste");
        ChkCpu.IsEnabled = ChkRam.IsEnabled = ChkGpu.IsEnabled = !running;
        RamFractionPanel.IsEnabled = !running && ChkRam.IsChecked == true;
        GpuOptions.IsEnabled = GpuTempLimit.IsEnabled = !running && ChkGpu.IsChecked == true;
        TempLimit.IsEnabled = TxtDuration.IsEnabled = !running;
        SideStress.Text = running ? "rodando" : "parado";
        SideStressDot.SetResourceReference(Shape.FillProperty, running ? "Accent" : "Faint");
    }

    /// <summary>Estado do teste: em andamento ganha o tom do canal ao vivo; corte automático ganha o tom de perigo.</summary>
    internal void SetStatus(string text, bool alert)
    {
        StStatus.Text = text;
        bool live = !alert && TestRunning;
        StatusCard.SetResourceReference(Border.BackgroundProperty, alert ? "DangerSoft" : live ? "AccentSoft" : "Surface");
        StatusCard.SetResourceReference(Border.BorderBrushProperty, alert ? "Danger" : live ? "Accent" : "Line");
        StStatus.SetResourceReference(TextBlock.ForegroundProperty, alert ? "Danger" : live ? "Accent" : "Fg");
        StStatusLabel.SetResourceReference(TextBlock.ForegroundProperty, alert ? "Danger" : "Muted");
        if (alert) SideStressDot.SetResourceReference(Shape.FillProperty, "Danger");
    }

    /// <summary>Temperatura do núcleo da GPU principal (dedicada).</summary>
    internal float? MainGpuTemp()
    {
        if (_gpus.Count == 0) return null;
        var temps = _monitor.Rows.Where(r => r.Hardware == _gpus[0].Name && r.Type == SensorType.Temperature).ToList();
        return (temps.FirstOrDefault(r => r.Name == "GPU Core") ?? temps.FirstOrDefault())?.RawValue;
    }

    /// <summary>Consumo da GPU principal: NVML quando disponível, senão LibreHardwareMonitor.</summary>
    internal float? MainGpuPower()
    {
        if (_gpus.Count == 0) return null;
        var g = _gpus[0];
        if (g.NvmlHandle != IntPtr.Zero && Nvml.PowerUsageMw(g.NvmlHandle) is uint mw) return mw / 1000f;
        return _monitor.Rows.FirstOrDefault(r => r.Hardware == g.Name && r.Type == SensorType.Power)?.RawValue;
    }

    internal string MainGpuPerfCap()
    {
        if (_gpus.Count == 0) return "";
        return _gpus[0].LiveExtras().FirstOrDefault(kv => kv.Key.StartsWith("PerfCap")).Value ?? "";
    }

    internal void UpdateStressPanel()
    {
        if (_pendingStressStop != null) { StopStress(_pendingStressStop); return; }
        if (_stress.Running && _stress.Error != null) { StopStress("ABORTADO: " + _stress.Error); return; }
        float? temp = _monitor.CpuTemperature;
        float? load = _monitor.CpuLoad;
        float? power = _monitor.CpuPower;
        float? gTemp = MainGpuTemp();
        float? gPower = MainGpuPower();

        if (temp is float t)
        {
            _tempHistory.Add(t);
            if (_tempHistory.Count > HistoryLength) _tempHistory.RemoveAt(0);
            if (_stress.Running && t > _stressMaxTemp) _stressMaxTemp = t;
        }
        if (gTemp is float gt)
        {
            _gpuTempHistory.Add(gt);
            if (_gpuTempHistory.Count > HistoryLength) _gpuTempHistory.RemoveAt(0);
            if (_gpuBurnOn && gt > _gpuMaxTemp) _gpuMaxTemp = gt;
        }

        StTemp.Text = SideTemp.Text = temp == null ? "—" : $"{temp:0.#} °C";
        StTempMax.Text = _stressMaxTemp > 0 ? $"máx. {_stressMaxTemp:0.#} °C" : "máx. —";
        SideLoad.Text = load == null ? "—" : $"{load:0} %";
        SideCpuBar.ScaleX = Math.Clamp((load ?? 0) / 100.0, 0, 1);
        StLoad.Text = load == null ? "carga —" : $"carga {load:0} %";
        StPower.Text = power == null ? "—" : $"{power:0.#} W";

        StGpuTemp.Text = gTemp == null ? "—" : $"{gTemp:0.#} °C";
        StGpuTempMax.Text = _gpuMaxTemp > 0 ? $"máx. {_gpuMaxTemp:0.#} °C" : "máx. —";
        StGpuPower.Text = gPower == null ? "—" : $"{gPower:0.#} W";
        string cap = MainGpuPerfCap();
        StGpuCap.Text = cap.Length > 0 ? $"limitador: {cap}" : "—";
        StFps.Text = _gpuBurnOn && _burn.Fps > 0 ? $"{_burn.Fps:0}" : "—";
        StFpsAvg.Text = _gpuBurnOn && _burn.AverageFps > 0 ? $"média {_burn.AverageFps:0}" : "média —";

        if (TestRunning)
        {
            var elapsed = DateTime.Now - _stressStart;
            StElapsed.Text = elapsed.ToString(@"hh\:mm\:ss");
            StRam.Text = _stress.MemoryAllocatedBytes > 0
                ? $"{_stress.MemoryPasses} / {_stress.MemoryErrors}"
                : "—";

            if (_stress.Running && temp >= _tempLimit)
                StopStress($"ABORTADO: CPU a {temp:0.#} °C atingiu o limite de {_tempLimit:0} °C");
            else if (_gpuBurnOn && gTemp >= _gpuTempLimit)
                StopStress($"ABORTADO: GPU a {gTemp:0.#} °C atingiu o limite de {_gpuTempLimit:0} °C");
            else if (_stress.MemoryErrors > 0)
                StopStress($"ABORTADO: {_stress.MemoryErrors} erros de memória detectados");
            else if (_gpuBurnOn && !_burn.Running)
                StopStress(_burn.Error != null ? $"ABORTADO: falha na GPU ({_burn.Error})"
                                               : "Janela da GPU fechada pelo usuário");
            else if (_stressDuration > TimeSpan.Zero && elapsed >= _stressDuration)
                StopStress("Concluído (tempo esgotado)");
        }

        if (PageStress.IsVisible) DrawChart();
    }

    internal void Log(string msg)
    {
        StressLog.Items.Add($"{DateTime.Now:HH:mm:ss}  {msg}");
        StressLog.ScrollIntoView(StressLog.Items[^1]);
    }

    internal void TempChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    /// <summary>Histórico de temperatura: CPU no canal A (verde-água, com área), GPU no canal B (cobre), limites tracejados.</summary>
    internal void DrawChart()
    {
        TempChart.Children.Clear();
        double w = TempChart.ActualWidth, h = TempChart.ActualHeight;
        if (w < 50 || h < 40) return;

        const double left = 36, top = 6, bottom = 6;
        double plotW = w - left, plotH = h - top - bottom;
        double yMin = 20, yMax = Math.Max(110, Math.Max(_tempLimit, _gpuTempLimit) + 10);
        double Y(double v) => top + plotH * (1 - (Math.Clamp(v, yMin, yMax) - yMin) / (yMax - yMin));

        var gridBrush = Palette("Line");
        for (double v = yMin; v <= yMax; v += 20)
        {
            TempChart.Children.Add(new Line { X1 = left, X2 = w, Y1 = Y(v), Y2 = Y(v), Stroke = gridBrush });
            var lbl = new TextBlock
            {
                Text = $"{v:0}°",
                FontSize = 10,
                Foreground = Palette("Faint"),
                FontFamily = (FontFamily)FindResource("Readout")
            };
            Canvas.SetLeft(lbl, 0); Canvas.SetTop(lbl, Y(v) - 7);
            TempChart.Children.Add(lbl);
        }

        void LimitLine(double v, Brush b) => TempChart.Children.Add(new Line
        {
            X1 = left,
            X2 = w,
            Y1 = Y(v),
            Y2 = Y(v),
            Stroke = b,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 }
        });
        if (ChkCpu.IsChecked == true || ChkRam.IsChecked == true) LimitLine(_tempLimit, Palette("Warn"));
        if (ChkGpu.IsChecked == true) LimitLine(_gpuTempLimit, new SolidColorBrush(ThemeManager.WithAlpha("Warn", 0x99)));

        double step = plotW / (HistoryLength - 1);
        void Series(List<double> data, Brush stroke, bool fill)
        {
            if (data.Count < 2) return;
            int offset = HistoryLength - data.Count; // alinha à direita
            var line = new Polyline { Stroke = stroke, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < data.Count; i++)
                line.Points.Add(new Point(left + (offset + i) * step, Y(data[i])));
            if (fill)
            {
                var area = new Polygon { Fill = new SolidColorBrush(ThemeManager.WithAlpha("Accent", 0x22)) };
                foreach (var p in line.Points) area.Points.Add(p);
                area.Points.Add(new Point(line.Points[^1].X, top + plotH));
                area.Points.Add(new Point(line.Points[0].X, top + plotH));
                TempChart.Children.Add(area);
            }
            TempChart.Children.Add(line);
        }
        Series(_gpuTempHistory, Palette("Copper"), fill: false);
        Series(_tempHistory, Palette("Accent"), fill: true);
    }

}
