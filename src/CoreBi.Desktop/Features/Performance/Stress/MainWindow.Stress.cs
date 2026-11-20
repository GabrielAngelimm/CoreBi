using System.Runtime.InteropServices;
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

    /// <summary>Parada pedida enquanto as threads ou a janela da placa de vídeo ainda encerram; aplicada no próximo tique.</summary>
    private (string Reason, string Status, bool Aborted)? _pendingStressStop;
    internal bool TestRunning => _stress.Running || _gpuBurnOn || _burn.Running;

    internal void BtnStress_Click(object sender, RoutedEventArgs e)
    {
        if (TestRunning) StopStress("Parado por você");
        else StartStress();
    }

    internal void ChkCpu_Click(object sender, RoutedEventArgs e) => StressSelectionChanged();

    internal void ChkRam_Click(object sender, RoutedEventArgs e)
    {
        if (RamFractionPanel != null) RamFractionPanel.IsEnabled = ChkRam.IsChecked == true;
        StressSelectionChanged();
    }

    internal void ChkGpu_Click(object sender, RoutedEventArgs e)
    {
        if (GpuOptions != null) GpuOptions.IsEnabled = GpuTempLimit.IsEnabled = ChkGpu.IsChecked == true;
        StressSelectionChanged();
    }

    private void StressSelectionChanged()
    {
        if (PageStress == null) return;
        UpdateStressColumns();
        DrawChart(); // mostra só as séries e os limites dos componentes escolhidos
    }

    /// <summary>Uma coluna de leituras por componente escolhido; sem nada escolhido fica a do processador.
    /// Só a primeira coluna visível perde o fio da esquerda.</summary>
    internal void UpdateStressColumns()
    {
        bool cpu = ChkCpu.IsChecked == true, ram = ChkRam.IsChecked == true, gpu = ChkGpu.IsChecked == true;
        CpuColumn.Visibility = cpu || ram || !gpu ? Visibility.Visible : Visibility.Collapsed;
        RamColumn.Visibility = ram ? Visibility.Visible : Visibility.Collapsed;
        GpuColumn.Visibility = gpu ? Visibility.Visible : Visibility.Collapsed;
        bool first = true;
        foreach (var col in new[] { CpuColumn, RamColumn, GpuColumn })
        {
            if (col.Visibility != Visibility.Visible) continue;
            col.BorderThickness = first ? new Thickness(0) : new Thickness(1, 0, 0, 0);
            first = false;
        }
    }

    internal static string? CheckedTag(Panel panel)
        => panel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;

    internal static string CheckedText(Panel panel)
        => panel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content?.ToString() ?? "";

    internal void StartStress()
    {
        StressHint.Text = "";
        if (_workloads.IsBusy) { StressHint.Text = "Termine o benchmark antes de iniciar o teste de carga."; return; }
        bool cpu = ChkCpu.IsChecked == true, ram = ChkRam.IsChecked == true, gpu = ChkGpu.IsChecked == true;
        if (!cpu && !ram && !gpu) { StressHint.Text = "Escolha pelo menos um componente."; return; }

        if (!double.TryParse(TempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _tempLimit)
            || _tempLimit < 40 || _tempLimit > 110)
        {
            StressHint.Text = "O limite do processador vai de 40 a 110 °C.";
            return;
        }
        if (gpu && (!double.TryParse(GpuTempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _gpuTempLimit)
                    || _gpuTempLimit < 40 || _gpuTempLimit > 100))
        {
            StressHint.Text = "O limite da placa de vídeo vai de 40 a 100 °C.";
            return;
        }
        double minutes = double.Parse(CheckedTag(StressDurationPanel) ?? "10", CultureInfo.InvariantCulture);
        if ((cpu || ram) && _monitor.CpuTemperature == null)
        {
            string why = SensorMonitor.IsPawnIOInstalled
                ? ""
                : "\n\nO driver PawnIO, que lê o sensor, não está instalado. Instale com:\n" +
                  "winget install namazso.PawnIO\ne abra o CoreBi de novo.";
            var r = MessageBox.Show("O processador não tem leitura de temperatura, então o teste não vai parar sozinho se ele esquentar demais. " +
                                    "Continuar mesmo assim?" + why,
                                    "CoreBi", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }
        if (gpu && MainGpuTemp() == null)
        {
            var r = MessageBox.Show("A placa de vídeo não tem leitura de temperatura, então o teste não vai parar sozinho se ela esquentar demais. " +
                                    "Continuar mesmo assim?",
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
        if (_stressLease == null) { StressHint.Text = "Termine o outro teste antes."; return; }
        try
        {
            if (cpu || ram) _stress.Start(cpu, ram, fraction);

            var parts = new List<string>();
            if (cpu) parts.Add($"processador (corte em {_tempLimit:0} °C)");
            if (ram) parts.Add($"RAM ({fraction * 100:0} % da livre)");
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
                parts.Add($"placa de vídeo ({CheckedText(GpuResPanel)}, {(msaa > 1 ? $"MSAA {msaa}x" : "sem MSAA")}, " +
                          $"{CheckedText(GpuIntensityPanel)}, corte em {_gpuTempLimit:0} °C)");
            }

            ResetStressReadings();
            SetStressControls(running: true);
            SetStatus("Rodando", aborted: false);
            StDetail.Text = minutes > 0 ? $"Termina às {_stressStart + _stressDuration:HH:mm}" : "Sem limite de tempo";
            Log($"Iniciado: {string.Join(", ", parts)}; {(minutes > 0 ? DurationText(_stressDuration) : "sem limite de tempo")}");
        }
        catch (Exception ex)
        {
            _stress.Stop(); _burn.Stop(); _gpuBurnOn = false;
            if (_stress.Running || _burn.Running) { _pendingStressStop = ("Falha ao iniciar", "Interrompido", true); SetStressControls(true); return; }
            _stressLease?.Dispose(); _stressLease = null;
            SetStressControls(false); StressHint.Text = "Não foi possível iniciar: " + ex.Message;
        }
    }

    internal static string DurationText(TimeSpan d) => d.TotalMinutes >= 60 && d.TotalMinutes % 60 == 0 ? $"{d.TotalHours:0} h" : $"{d.TotalMinutes:0} min";

    /// <summary>Limpa as leituras do teste anterior: sem valor, a linha some até a primeira leitura.</summary>
    private void ResetStressReadings()
    {
        StElapsed.Text = "00:00:00";
        StProgress.ScaleX = 0;
        StTempMax.Text = StGpuTempMax.Text = StFps.Text = StFpsAvg.Text = StRam.Text = StRamSize.Text = "";
        StRamErrors.Text = "—";
        StRamErrors.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
    }

    /// <summary>Para o teste. A razão vai para o detalhe da placa, o registro e os eventos do painel; aborted marca corte automático ou falha.</summary>
    internal void StopStress(string reason, string status = "Parado", bool aborted = false)
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
            if (_stress.Running || _burn.Running) { _pendingStressStop = (reason, status, aborted); SetStatus("Parando…", false); return; }
            _gpuBurnOn = false;
            // "Pontuação" no estilo FurMark: frames renderizados e FPS médio.
            gpuSummary = $"; placa de vídeo {_burn.Frames:N0} quadros, média de {_burn.AverageFps:0} FPS" +
                         (_gpuMaxTemp > 0 ? $", até {_gpuMaxTemp:0.#} °C" : "");
        }
        if (_stress.Running) { _pendingStressStop = (reason, status, aborted); SetStatus("Parando…", false); return; }
        _pendingStressStop = null;
        _stressLease?.Dispose(); _stressLease = null;
        var elapsed = DateTime.Now - _stressStart;
        AddEvent("", "Teste de carga", aborted ? "Interrompido. " + reason : reason, elapsed.ToString(@"hh\:mm\:ss"));
        SetStressControls(running: false);

        if (aborted) status = "Interrompido";
        SetStatus(status, aborted);
        StDetail.Text = reason;
        Log($"{reason}; duração {elapsed:hh\\:mm\\:ss}" +
            (hadCpu && _stressMaxTemp > 0 ? $"; processador até {_stressMaxTemp:0.#} °C" : "") +
            (hadRam ? $"; RAM {passes} passadas, {errors} erros" : "") + gpuSummary);
    }

    internal void SetStressControls(bool running)
    {
        Ui.SetRunning(BtnStress, running, "Iniciar teste");
        ChkCpu.IsEnabled = ChkRam.IsEnabled = ChkGpu.IsEnabled = !running;
        RamFractionPanel.IsEnabled = !running && ChkRam.IsChecked == true;
        GpuOptions.IsEnabled = GpuTempLimit.IsEnabled = !running && ChkGpu.IsChecked == true;
        TempLimit.IsEnabled = StressDurationPanel.IsEnabled = !running;
    }

    /// <summary>Estado do teste: o LED fica no sinal enquanto roda e no vermelho depois de um corte; o texto só muda de cor no corte.</summary>
    internal void SetStatus(string text, bool aborted)
    {
        StStatus.Text = text;
        StLed.SetResourceReference(Shape.FillProperty, aborted ? "Danger" : TestRunning ? "Signal" : "Faint");
        StStatus.SetResourceReference(TextBlock.ForegroundProperty, aborted ? "Danger" : "Fg");
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

    /// <summary>Clock médio dos núcleos do processador; null quando o sensor não dá a leitura.</summary>
    internal double? CpuCoreClock()
    {
        var clocks = _monitor.Rows.Where(r => r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Clock &&
                                              r.Name.StartsWith("Core #") && r.RawValue > 0).Select(r => (double)r.RawValue!.Value).ToList();
        return clocks.Count > 0 ? clocks.Average() : null;
    }

    internal void UpdateStressPanel()
    {
        if (_pendingStressStop is { } pending) { StopStress(pending.Reason, pending.Status, pending.Aborted); return; }
        if (_stress.Running && _stress.Error != null) { StopStress("Falha no teste: " + _stress.Error, aborted: true); return; }
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

        SideTemp.Text = temp == null ? "—" : $"{temp:0.#} °C";
        SideLoad.Text = load == null ? "—" : $"{load:0} %";
        Ui.Glide(SideCpuBar, (load ?? 0) / 100.0, LiveMotion);

        // Processador: a leitura principal fica âmbar a 5 °C do corte; as secundárias somem sem sensor.
        StTemp.Text = temp == null ? "—" : $"{temp:0.#}";
        StTemp.SetResourceReference(TextBlock.ForegroundProperty, _stress.Running && temp >= _tempLimit - 5 ? "Warn" : "Fg");
        StTempMax.Text = _stressMaxTemp > 0 ? $"{_stressMaxTemp:0.#} °C" : "";
        StClock.Text = CpuCoreClock() is double clock ? $"{clock:0} MHz" : "";
        StPower.Text = power == null ? "" : $"{power:0.#} W";
        StLoad.Text = load == null ? "" : $"{load:0} %";

        StGpuTemp.Text = gTemp == null ? "—" : $"{gTemp:0.#}";
        StGpuTemp.SetResourceReference(TextBlock.ForegroundProperty, _gpuBurnOn && gTemp >= _gpuTempLimit - 5 ? "Warn" : "Fg");
        StGpuTempMax.Text = _gpuMaxTemp > 0 ? $"{_gpuMaxTemp:0.#} °C" : "";
        StGpuPower.Text = gPower == null ? "" : $"{gPower:0.#} W";
        StGpuCap.Text = MainGpuPerfCap();
        StFps.Text = _gpuBurnOn && _burn.Fps > 0 ? $"{_burn.Fps:0}" : "";
        StFpsAvg.Text = _gpuBurnOn && _burn.AverageFps > 0 ? $"{_burn.AverageFps:0}" : "";

        if (TestRunning)
        {
            var elapsed = DateTime.Now - _stressStart;
            StElapsed.Text = elapsed.ToString(@"hh\:mm\:ss");
            StProgress.ScaleX = _stressDuration > TimeSpan.Zero ? Math.Clamp(elapsed / _stressDuration, 0, 1) : 0;
            if (_stress.MemoryAllocatedBytes > 0)
            {
                StRamErrors.Text = $"{_stress.MemoryErrors:N0}";
                StRamErrors.SetResourceReference(TextBlock.ForegroundProperty, _stress.MemoryErrors > 0 ? "Danger" : "Fg");
                StRam.Text = $"{_stress.MemoryPasses:N0}";
                StRamSize.Text = $"{_stress.MemoryAllocatedBytes / 1e9:0.0} GB";
            }

            if (_stress.Running && temp >= _tempLimit)
                StopStress($"O processador chegou a {temp:0.#} °C, no limite de {_tempLimit:0} °C", aborted: true);
            else if (_gpuBurnOn && gTemp >= _gpuTempLimit)
                StopStress($"A placa de vídeo chegou a {gTemp:0.#} °C, no limite de {_gpuTempLimit:0} °C", aborted: true);
            else if (_stress.MemoryErrors > 0)
                StopStress($"{_stress.MemoryErrors} erros de memória encontrados", aborted: true);
            else if (_gpuBurnOn && !_burn.Running)
            {
                if (_burn.Error != null) StopStress($"Falha na placa de vídeo: {_burn.Error}", aborted: true);
                else StopStress("Janela da placa de vídeo fechada");
            }
            else if (_stressDuration > TimeSpan.Zero && elapsed >= _stressDuration)
            {
                StProgress.ScaleX = 1;
                StopStress("Tempo de teste concluído", "Concluído");
            }
        }

        if (PageStress.IsVisible) DrawChart();
    }

    internal void Log(string msg)
    {
        StressLog.Items.Add($"{DateTime.Now:HH:mm:ss}  {msg}");
        StressLog.ScrollIntoView(StressLog.Items[^1]);
    }

    internal void TempChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    /// <summary>Temperatura dos últimos 5 minutos: uma série por componente escolhido, com o nome e o valor no fim da linha
    /// e o limite de corte tracejado e rotulado. Sem legenda separada.</summary>
    internal void DrawChart()
    {
        TempChart.Children.Clear();
        double w = TempChart.ActualWidth, h = TempChart.ActualHeight;
        if (w < 160 || h < 60) return;

        bool showGpu = ChkGpu.IsChecked == true;
        bool showCpu = ChkCpu.IsChecked == true || ChkRam.IsChecked == true || !showGpu;
        const double left = 34, right = 132, top = 8, bottom = 22;
        double plotW = w - left - right, plotH = h - top - bottom;
        if (plotW < 40) return;
        double yMin = 20, yMax = Math.Max(110, Math.Max(_tempLimit, showGpu ? _gpuTempLimit : 0) + 10);
        double Y(double v) => top + plotH * (1 - (Math.Clamp(v, yMin, yMax) - yMin) / (yMax - yMin));
        var ui = (FontFamily)FindResource("UiFont");
        var readout = (FontFamily)FindResource("Readout");

        TextBlock Text(string text, Brush fg, double size = 10.5, FontFamily? font = null)
        {
            var tb = new TextBlock { Text = text, FontSize = size, Foreground = fg, FontFamily = font ?? readout };
            System.Windows.Documents.Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
            return tb;
        }
        void Place(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); TempChart.Children.Add(e); }

        // Grade fina e rótulos discretos
        var grid = Palette("Line");
        var faint = Palette("Faint");
        for (double v = yMin; v <= yMax; v += 20)
        {
            TempChart.Children.Add(new Line { X1 = left, X2 = left + plotW, Y1 = Y(v), Y2 = Y(v), Stroke = grid, StrokeThickness = 1 });
            Place(Text($"{v:0}°", faint), 0, Y(v) - 7);
        }
        Place(Text("−5 min", faint, font: ui), left, top + plotH + 6);
        var now = Text("agora", faint, font: ui);
        now.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Place(now, left + plotW - now.DesiredSize.Width, top + plotH + 6);

        // Limites de corte: tracejado âmbar com o valor escrito sobre a linha, à esquerda
        var warn = Palette("Warn");
        var surface = Palette("Surface");
        var limits = new List<(double Value, string Label)>();
        if (showCpu) limits.Add((_tempLimit, showGpu ? $"limite do processador {_tempLimit:0} °C" : $"limite {_tempLimit:0} °C"));
        if (showGpu) limits.Add((_gpuTempLimit, showCpu ? $"limite da placa de vídeo {_gpuTempLimit:0} °C" : $"limite {_gpuTempLimit:0} °C"));
        double? lastLabelY = null;
        foreach (var (value, label) in limits.OrderByDescending(l => l.Value))
        {
            TempChart.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotW,
                Y1 = Y(value),
                Y2 = Y(value),
                Stroke = warn,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            });
            double ly = Y(value) - 16;
            if (lastLabelY is double prev && ly < prev + 15) ly = Y(value) + 3; // dois limites próximos: o segundo vai para baixo da linha
            var tb = Text(label, warn, font: ui);
            tb.Background = surface;
            tb.Padding = new Thickness(0, 0, 6, 0);
            Place(tb, left + 6, ly);
            lastLabelY = ly;
        }

        // Séries, alinhadas à direita (a última amostra é agora)
        double step = plotW / (HistoryLength - 1);
        var ends = new List<(double Y, string Label, Brush Brush)>();
        void Series(List<double> data, string channel, byte areaAlpha, string name)
        {
            if (data.Count < 2) return;
            var stroke = Palette(channel);
            int offset = HistoryLength - data.Count;
            var line = new Polyline { Stroke = stroke, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < data.Count; i++)
                line.Points.Add(new Point(left + (offset + i) * step, Y(data[i])));
            var area = new Polygon { Fill = new SolidColorBrush(ThemeManager.WithAlpha(channel, areaAlpha)) };
            foreach (var p in line.Points) area.Points.Add(p);
            area.Points.Add(new Point(line.Points[^1].X, top + plotH));
            area.Points.Add(new Point(line.Points[0].X, top + plotH));
            TempChart.Children.Add(area);
            TempChart.Children.Add(line);
            var end = line.Points[^1];
            var dot = new Ellipse { Width = 6, Height = 6, Fill = stroke };
            Place(dot, end.X - 3, end.Y - 3);
            ends.Add((end.Y, $"{name} {data[^1]:0} °C", stroke));
        }
        if (showGpu) Series(_gpuTempHistory, "Gpu", 0x1C, "Placa de vídeo");
        if (showCpu) Series(_tempHistory, "Cpu", 0x22, "Processador");

        if (ends.Count == 0)
        {
            var empty = Text("Sem leitura de temperatura ainda.", faint, 12, ui);
            empty.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Place(empty, left + (plotW - empty.DesiredSize.Width) / 2, top + (plotH - empty.DesiredSize.Height) / 2);
            return;
        }

        // Rótulos diretos no fim de cada linha; se os dois ficam a menos de uma linha de texto, afasta um do outro
        const double labelH = 15;
        var placed = ends.OrderBy(e => e.Y).Select(e => (Y: e.Y - labelH / 2, e.Label, e.Brush)).ToList();
        if (placed.Count == 2 && placed[1].Y - placed[0].Y < labelH)
        {
            double mid = (placed[0].Y + placed[1].Y) / 2;
            placed[0] = (mid - labelH / 2 - 1, placed[0].Label, placed[0].Brush);
            placed[1] = (mid + labelH / 2 + 1, placed[1].Label, placed[1].Brush);
        }
        foreach (var (y, label, brush) in placed)
            Place(Text(label, brush, 11.5, ui), left + plotW + 10, Math.Clamp(y, 0, h - labelH));
    }

}
