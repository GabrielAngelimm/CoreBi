using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Performance.Stress;

namespace CoreBi.Desktop.Views;

public partial class StressView : UserControl, IDisposable
{
    private const int HistoryLength = 300; // 5 min a 1 amostra/s

    private FeatureContext? _context;
    private readonly StressTester _stress = new();
    private readonly GpuBurn _burn = new();
    private readonly List<double> _tempHistory = new();
    private readonly List<double> _gpuTempHistory = new();

    private IDisposable? _stressLease;
    private DateTime _stressStart;
    private TimeSpan _stressDuration;
    private double _tempLimit = 90;
    private float _stressMaxTemp;
    private bool _gpuBurnOn;
    private double _gpuTempLimit = 85;
    private float _gpuMaxTemp;
    /// <summary>Parada pedida enquanto as threads ou a janela da placa de vídeo ainda encerram; aplicada no próximo tique.</summary>
    private (string Reason, string Status, bool Aborted)? _pendingStop;

    public StressView() => InitializeComponent();

    internal bool IsRunning => _stress.Running || _gpuBurnOn || _burn.Running;

    internal void Initialize(FeatureContext context)
    {
        _context = context;
        UpdateStressColumns();
        StressDurationChanged();
    }

    private void BtnStress_Click(object sender, RoutedEventArgs e)
    {
        if (IsRunning) StopStress("Parado por você");
        else StartStress();
    }

    private void ChkCpu_Click(object sender, RoutedEventArgs e) => StressSelectionChanged();
    private void ChkRam_Click(object sender, RoutedEventArgs e) => StressSelectionChanged();
    private void ChkGpu_Click(object sender, RoutedEventArgs e) => StressSelectionChanged();
    private void StressDuration_Checked(object sender, RoutedEventArgs e) => StressDurationChanged();
    private void TempChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void StressDurationChanged()
    {
        if (!IsInitialized || StressDurationPanel == null || IsRunning) return;
        double minutes = double.Parse(Ui.CheckedTag(StressDurationPanel) ?? "10", CultureInfo.InvariantCulture);
        ShowStressTime(TimeSpan.Zero, TimeSpan.FromMinutes(minutes));
    }

    private void ShowStressTime(TimeSpan elapsed, TimeSpan duration)
    {
        bool hours = duration <= TimeSpan.Zero || duration.TotalHours >= 1;
        static string Clock(TimeSpan t, bool h) => h ? $"{(int)t.TotalHours}:{t:mm\\:ss}" : $"{(int)t.TotalMinutes:00}:{t:ss}";
        StElapsed.Text = Clock(elapsed, hours);
        StTotal.Text = duration > TimeSpan.Zero ? "de " + Clock(duration, hours) : "sem limite";
    }

    private void StressSelectionChanged()
    {
        if (!IsInitialized) return;
        UpdateStressColumns();
        DrawChart();
    }

    private void UpdateStressColumns()
    {
        bool cpu = ChkCpu.IsChecked == true, ram = ChkRam.IsChecked == true, gpu = ChkGpu.IsChecked == true;
        // O corte de temperatura do processador vale também para o teste só de RAM, que esquenta o processador.
        bool cpuSide = cpu || ram || !gpu;
        static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        CpuColumn.Visibility = Show(cpuSide);
        RamColumn.Visibility = Show(ram);
        GpuColumn.Visibility = Show(gpu);

        bool first = true;
        int columns = 0;
        foreach (var col in new[] { CpuColumn, RamColumn, GpuColumn })
        {
            if (col.Visibility != Visibility.Visible) continue;
            col.BorderThickness = first ? new Thickness(0) : new Thickness(1, 0, 0, 0);
            first = false;
            columns++;
        }
        CpuHead.Visibility = RamHead.Visibility = GpuHead.Visibility = Show(columns > 1);
        CpuLimitRow.Visibility = Show(cpuSide);
        GpuLimitRow.Visibility = Show(gpu);
        GpuLimitRow.Margin = new Thickness(0, cpuSide ? 8 : 0, 0, 0);
        RamSection.Visibility = Show(ram);
        GpuOptions.Visibility = Show(gpu);
    }

    internal void StartStress()
    {
        if (_context == null) return;
        StressHint.Text = "";
        if (_context.Workloads.IsBusy) { StressHint.Text = "Termine o benchmark antes de iniciar o teste de carga."; return; }
        bool cpu = ChkCpu.IsChecked == true, ram = ChkRam.IsChecked == true, gpu = ChkGpu.IsChecked == true;
        if (!cpu && !ram && !gpu) { StressHint.Text = "Escolha pelo menos um componente."; return; }

        // Valida em variáveis locais: um valor recusado não pode virar a linha de corte desenhada no gráfico.
        double tempLimit = _tempLimit, gpuTempLimit = _gpuTempLimit;
        if ((cpu || ram) && (!double.TryParse(TempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out tempLimit)
                             || tempLimit < 40 || tempLimit > 110))
        {
            StressHint.Text = "O limite do processador vai de 40 a 110 °C.";
            return;
        }
        if (gpu && (!double.TryParse(GpuTempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out gpuTempLimit)
                    || gpuTempLimit < 40 || gpuTempLimit > 100))
        {
            StressHint.Text = "O limite da placa de vídeo vai de 40 a 100 °C.";
            return;
        }
        _tempLimit = tempLimit;
        _gpuTempLimit = gpuTempLimit;

        double minutes = double.Parse(Ui.CheckedTag(StressDurationPanel) ?? "10", CultureInfo.InvariantCulture);
        if ((cpu || ram) && _context.Sensors.CpuTemperature == null)
        {
            string why = SensorMonitor.IsPawnIOInstalled
                ? ""
                : "\n\nO driver PawnIO, que lê o sensor, não está instalado. Instale com:\n" +
                  "winget install namazso.PawnIO\ne abra o CoreBi de novo.";
            var r = MessageBox.Show(_context.Owner,
                "O processador não tem leitura de temperatura, então o teste não vai parar sozinho se ele esquentar demais. Continuar mesmo assim?" + why,
                "CoreBi", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }
        if (gpu && _context.PrimaryGpuTemp() == null)
        {
            var r = MessageBox.Show(_context.Owner,
                "A placa de vídeo não tem leitura de temperatura, então o teste não vai parar sozinho se ela esquentar demais. Continuar mesmo assim?",
                "CoreBi", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }

        double fraction = double.Parse(Ui.CheckedTag(RamFractionPanel) ?? "0.6", CultureInfo.InvariantCulture);
        _stressDuration = TimeSpan.FromMinutes(minutes);
        _stressStart = DateTime.Now;
        _stressMaxTemp = 0;
        _gpuMaxTemp = 0;
        _tempHistory.Clear();
        _gpuTempHistory.Clear();

        _stressLease = _context.Workloads.TryBegin(WorkloadOperations.Stress);
        if (_stressLease == null) { StressHint.Text = "Termine o outro teste antes."; return; }

        try
        {
            if (cpu || ram) _stress.Start(cpu, ram, fraction);

            var parts = new List<string>();
            if (cpu) parts.Add($"processador (corte em {_tempLimit:0} °C)");
            if (ram) parts.Add($"RAM ({fraction * 100:0} % da livre)");
            if (gpu)
            {
                string res = Ui.CheckedTag(GpuResPanel) ?? "1920x1080";
                int msaa = int.Parse(Ui.CheckedTag(GpuMsaaPanel) ?? "4", CultureInfo.InvariantCulture);
                var intensity = (Ui.CheckedTag(GpuIntensityPanel) ?? "128,48").Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                bool full = res == "full";
                var size = full ? new[] { 0, 0 } : res.Split('x').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                _burn.Start(new GpuBurnSettings
                {
                    Fullscreen = full,
                    Width = size[0],
                    Height = size[1],
                    Msaa = msaa,
                    Shells = intensity[0],
                    Iterations = intensity[1],
                    AdapterName = _context.PrimaryGpu?.Name ?? "",
                });
                _gpuBurnOn = true;
                parts.Add($"placa de vídeo ({Ui.CheckedText(GpuResPanel)}, {(msaa > 1 ? $"MSAA {msaa}x" : "sem MSAA")}, " +
                          $"{Ui.CheckedText(GpuIntensityPanel)}, corte em {_gpuTempLimit:0} °C)");
            }

            ResetStressReadings();
            SetStressControls(running: true);
            SetStatus("Rodando", aborted: false);
            StDetail.Text = minutes > 0 ? $"Termina às {_stressStart + _stressDuration:HH:mm}" : "";
            Log($"Iniciado: {string.Join(", ", parts)}; {(minutes > 0 ? DurationText(_stressDuration) : "sem limite de tempo")}");
        }
        catch (Exception ex)
        {
            _stress.Stop();
            _burn.Stop();
            _gpuBurnOn = false;
            if (_stress.Running || _burn.Running)
            {
                _pendingStop = ("Falha ao iniciar", "Interrompido", true);
                SetStressControls(true);
                return;
            }
            _stressLease?.Dispose();
            _stressLease = null;
            SetStressControls(false);
            StressHint.Text = "Não foi possível iniciar: " + ex.Message;
        }
    }

    private static string DurationText(TimeSpan d) =>
        d.TotalMinutes >= 60 && d.TotalMinutes % 60 == 0 ? $"{d.TotalHours:0} h" : $"{d.TotalMinutes:0} min";

    private void ResetStressReadings()
    {
        ShowStressTime(TimeSpan.Zero, _stressDuration);
        StProgress.ScaleX = 0;
        StTempMax.Text = StGpuTempMax.Text = StFps.Text = StFpsAvg.Text = StRam.Text = StRamSize.Text = "";
        StRamErrors.Text = "—";
        StRamErrors.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
    }

    internal void StopStress(string reason, string status = "Parado", bool aborted = false)
    {
        if (!IsRunning) { _stressLease?.Dispose(); _stressLease = null; return; }
        var errors = _stress.MemoryErrors;
        var passes = _stress.MemoryPasses;
        bool hadRam = _stress.MemoryAllocatedBytes > 0;
        bool hadCpu = _stress.Running;
        _stress.Stop();

        string gpuSummary = "";
        if (_gpuBurnOn)
        {
            _burn.Stop();
            if (_stress.Running || _burn.Running) { _pendingStop = (reason, status, aborted); SetStatus("Parando…", false); return; }
            _gpuBurnOn = false;
            gpuSummary = $"; placa de vídeo {_burn.Frames:N0} quadros, média de {_burn.AverageFps:0} FPS" +
                         (_gpuMaxTemp > 0 ? $", até {_gpuMaxTemp:0.#} °C" : "");
        }
        if (_stress.Running) { _pendingStop = (reason, status, aborted); SetStatus("Parando…", false); return; }
        _pendingStop = null;
        _stressLease?.Dispose();
        _stressLease = null;

        var elapsed = DateTime.Now - _stressStart;
        _context?.RecordEvent("", "Teste de carga", aborted ? "Interrompido. " + reason : reason, elapsed.ToString(@"hh\:mm\:ss"), "");
        SetStressControls(running: false);

        if (aborted) status = "Interrompido";
        SetStatus(status, aborted);
        StDetail.Text = reason;
        Log($"{reason}; duração {elapsed:hh\\:mm\\:ss}" +
            (hadCpu && _stressMaxTemp > 0 ? $"; processador até {_stressMaxTemp:0.#} °C" : "") +
            (hadRam ? $"; RAM {passes} passadas, {errors} erros" : "") + gpuSummary);
    }

    private void SetStressControls(bool running)
    {
        Ui.SetRunning(BtnStress, running, "Iniciar teste");
        ChkCpu.IsEnabled = ChkRam.IsEnabled = ChkGpu.IsEnabled = !running;
        StressDurationSection.IsEnabled = StressLimitSection.IsEnabled = RamSection.IsEnabled = GpuOptions.IsEnabled = !running;
    }

    private void SetStatus(string text, bool aborted)
    {
        StStatus.Text = text;
        StLed.Visibility = aborted || IsRunning ? Visibility.Visible : Visibility.Collapsed;
        StLed.SetResourceReference(Shape.FillProperty, aborted ? "Danger" : "Signal");
        StStatus.SetResourceReference(TextBlock.ForegroundProperty, aborted ? "Danger" : "Fg");
    }

    internal void Tick()
    {
        if (_context == null) return;
        if (_pendingStop is { } pending) { StopStress(pending.Reason, pending.Status, pending.Aborted); return; }
        if (_stress.Running && _stress.Error != null) { StopStress("Falha no teste: " + _stress.Error, aborted: true); return; }

        float? temp = _context.Sensors.CpuTemperature;
        float? load = _context.Sensors.CpuLoad;
        float? power = _context.Sensors.CpuPower;
        float? gTemp = _context.PrimaryGpuTemp();
        float? gPower = _context.PrimaryGpuPower();

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

        StTemp.Text = temp == null ? "—" : $"{temp:0.#}";
        // A leitura principal fica âmbar a 5 °C do corte.
        StTemp.SetResourceReference(TextBlock.ForegroundProperty, _stress.Running && temp >= _tempLimit - 5 ? "Warn" : "Fg");
        StTempMax.Text = _stressMaxTemp > 0 ? $"{_stressMaxTemp:0.#} °C" : "";
        StClock.Text = _context.ReadCpuLive().AvgClockMHz is float clock ? $"{clock:0} MHz" : "";
        StPower.Text = power == null ? "" : $"{power:0.#} W";
        StLoad.Text = load == null ? "" : $"{load:0} %";

        StGpuTemp.Text = gTemp == null ? "—" : $"{gTemp:0.#}";
        StGpuTemp.SetResourceReference(TextBlock.ForegroundProperty, _gpuBurnOn && gTemp >= _gpuTempLimit - 5 ? "Warn" : "Fg");
        StGpuTempMax.Text = _gpuMaxTemp > 0 ? $"{_gpuMaxTemp:0.#} °C" : "";
        StGpuPower.Text = gPower == null ? "" : $"{gPower:0.#} W";
        StGpuCap.Text = _context.PrimaryGpuPerfCap();
        StFps.Text = _gpuBurnOn && _burn.Fps > 0 ? $"{_burn.Fps:0}" : "";
        StFpsAvg.Text = _gpuBurnOn && _burn.AverageFps > 0 ? $"{_burn.AverageFps:0}" : "";

        if (IsRunning)
        {
            var elapsed = DateTime.Now - _stressStart;
            ShowStressTime(elapsed, _stressDuration);
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

        if (IsVisible) DrawChart();
    }

    private void Log(string msg)
    {
        StressLog.Items.Add($"{DateTime.Now:HH:mm:ss}  {msg}");
        StressLog.ScrollIntoView(StressLog.Items[^1]);
    }

    internal void DrawChart()
    {
        TempChart.Children.Clear();
        double w = TempChart.ActualWidth, h = TempChart.ActualHeight;
        if (w < 160 || h < 60) return;

        bool showGpu = ChkGpu.IsChecked == true;
        bool showCpu = ChkCpu.IsChecked == true || ChkRam.IsChecked == true || !showGpu;
        var ui = (FontFamily)FindResource("UiFont");
        var readout = (FontFamily)FindResource("Readout");
        var faint = ThemeManager.Brush("Faint");

        TextBlock Text(string text, Brush fg, double size = 10.5, FontFamily? font = null)
        {
            var tb = new TextBlock { Text = text, FontSize = size, Foreground = fg, FontFamily = font ?? readout };
            Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
            return tb;
        }
        static double Width(TextBlock tb)
        {
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return tb.DesiredSize.Width;
        }
        void Place(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); TempChart.Children.Add(e); }

        // Distância do topo da caixa ao meio das maiúsculas (base - 0,35 em na Segoe/Bahnschrift) para alinhar texto no eixo.
        double pixelsPerDip = VisualTreeHelper.GetDpi(TempChart).PixelsPerDip;
        double CapMiddle(TextBlock tb)
        {
            var ft = new FormattedText(tb.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.Black, null, TextFormattingMode.Display, pixelsPerDip);
            return ft.Baseline - tb.FontSize * 0.7 / 2;
        }

        var series = new List<(List<double> Data, string Channel, byte Alpha, TextBlock Label)>();
        if (showGpu && _gpuTempHistory.Count >= 2)
            series.Add((_gpuTempHistory, "Gpu", 0x1C, Text($"Placa de vídeo {_gpuTempHistory[^1]:0} °C", ThemeManager.Brush("Gpu"), 11.5, ui)));
        if (showCpu && _tempHistory.Count >= 2)
            series.Add((_tempHistory, "Cpu", 0x22, Text($"Processador {_tempHistory[^1]:0} °C", ThemeManager.Brush("Cpu"), 11.5, ui)));
        double right = series.Count == 0 ? 0 : series.Max(x => Width(x.Label)) + 10;

        double yMin = 20, yMax = Math.Max(110, Math.Max(_tempLimit, showGpu ? _gpuTempLimit : 0) + 10);
        var ticks = new List<(double Value, TextBlock Label)>();
        for (double v = yMin; v <= yMax; v += 20) ticks.Add((v, Text($"{v:0} °C", faint)));
        double left = ticks.Max(t => Width(t.Label)) + 8;
        const double top = 8, bottom = 22;
        double plotW = w - left - right, plotH = h - top - bottom;
        if (plotW < 40) return;
        double Y(double v) => top + plotH * (1 - (Math.Clamp(v, yMin, yMax) - yMin) / (yMax - yMin));

        var grid = ThemeManager.Brush("Line");
        foreach (var (v, label) in ticks)
        {
            TempChart.Children.Add(new Line { X1 = left, X2 = left + plotW, Y1 = Y(v), Y2 = Y(v), Stroke = grid, StrokeThickness = 1 });
            Place(label, 0, Y(v) - CapMiddle(label));
        }
        double axisY = top + plotH + 6;
        Place(Text("−5 min", faint, font: ui), left, axisY);
        var mid = Text("−2,5 min", faint, font: ui);
        Place(mid, left + (plotW - Width(mid)) / 2, axisY);
        var now = Text("agora", faint, font: ui);
        Place(now, left + plotW - Width(now), axisY);

        var warn = ThemeManager.Brush("Warn");
        var surface = ThemeManager.Brush("Surface");
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
                StrokeDashArray = new DoubleCollection { 4, 3 },
            });
            double ly = Y(value) - 16;
            if (lastLabelY is double prev && ly < prev + 15) ly = Y(value) + 3;
            var tb = Text(label, warn, font: ui);
            tb.Background = surface;
            tb.Padding = new Thickness(0, 0, 6, 0);
            Place(tb, left + 6, ly);
            lastLabelY = ly;
        }

        if (series.Count == 0)
        {
            var empty = Text("Sem leitura de temperatura ainda.", ThemeManager.Brush("Muted"), 13, ui);
            empty.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Place(empty, left + (plotW - empty.DesiredSize.Width) / 2, top + (plotH - empty.DesiredSize.Height) / 2);
            return;
        }

        double step = plotW / (HistoryLength - 1);
        var ends = new List<(double Y, TextBlock Label)>();
        foreach (var (data, channel, areaAlpha, endLabel) in series)
        {
            var stroke = ThemeManager.Brush(channel);
            int offset = HistoryLength - data.Count;
            var line = new Polyline { Stroke = stroke, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < data.Count; i++)
                line.Points.Add(new Point(left + (offset + i) * step, Y(data[i])));
            var area = new Polygon { Fill = new SolidColorBrush(ThemeManager.WithAlpha(channel, areaAlpha)) };
            foreach (var pt in line.Points) area.Points.Add(pt);
            area.Points.Add(new Point(line.Points[^1].X, top + plotH));
            area.Points.Add(new Point(line.Points[0].X, top + plotH));
            TempChart.Children.Add(area);
            TempChart.Children.Add(line);
            var end = line.Points[^1];
            var dot = new Ellipse { Width = 6, Height = 6, Fill = stroke };
            Place(dot, end.X - 3, end.Y - 3);
            ends.Add((end.Y, endLabel));
        }

        const double labelH = 15;
        var placed = ends.OrderBy(e => e.Y).Select(e => (Y: e.Y - CapMiddle(e.Label), e.Label)).ToList();
        if (placed.Count == 2 && placed[1].Y - placed[0].Y < labelH)
        {
            double center = (placed[0].Y + placed[1].Y) / 2;
            placed[0] = (center - labelH / 2 - 1, placed[0].Label);
            placed[1] = (center + labelH / 2 + 1, placed[1].Label);
        }
        foreach (var (y, label) in placed)
            Place(label, left + plotW + 10, Math.Clamp(y, 0, h - labelH));
    }

    public void Dispose()
    {
        _stress.Stop();
        _burn.Stop();
        _stressLease?.Dispose();
        _stressLease = null;
    }
}
