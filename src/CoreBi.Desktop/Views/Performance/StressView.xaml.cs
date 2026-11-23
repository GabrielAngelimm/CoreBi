using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring.Memory;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Performance;
using CoreBi.Engine.Performance.Stress;

namespace CoreBi.Desktop.Views;

/// <summary>Linha do histórico do Teste de carga: título, motivo (vazio quando foi o tempo), máximas, data e tom do LED.</summary>
/// <summary>Linha do histórico: o título, as máximas por sigla e, na dica, o motivo e os números completos (Detail).</summary>
public sealed record StressHistoryRow(string Title, string Detail, string Facts, string Date, string Tone)
{
    public string? Tip => Detail.Length > 0 ? Detail : null;
}

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
    // O que o teste em andamento carrega, para o histórico (as teclas podem mudar depois do fim)
    private bool _runCpu, _runRam, _runGpu;
    // Prévia das capturas de tela: um teste de exemplo que o Tick não sobrescreve (nada é salvo)
    private bool _demo;

    public StressView()
    {
        InitializeComponent();
        // Primeira visita: a página espera a primeira leitura dos sensores (a temperatura é o centro dela)
        Skeleton.SetActive(StressConfigCard, true);
    }

    internal bool IsRunning => _stress.Running || _gpuBurnOn || _burn.Running;

    internal void Initialize(FeatureContext context)
    {
        _context = context;
        UpdateStressColumns();
        StressDurationChanged();
        ShowLimits();
        ShowHistory();
    }

    /// <summary>Zerar painel: o resultado do último teste sai da placa e das colunas; o histórico guarda o teste.</summary>
    private void BtnStressReset_Click(object sender, RoutedEventArgs e)
    {
        if (IsRunning || _demo) return;
        _stressMaxTemp = 0;
        _gpuMaxTemp = 0;
        ResetStressReadings();
        StressDurationChanged();
        SetStatus("", aborted: false);
        StDetail.Text = "";
        StressHint.Text = "";
        BtnStressReset.IsEnabled = false;
    }

    private void BtnStress_Click(object sender, RoutedEventArgs e)
    {
        if (IsRunning) StopStress("Parado por você");
        else StartStress();
    }

    private void ChkCpu_Click(object sender, RoutedEventArgs e) => StressSelectionChanged();
    private void ChkRam_Click(object sender, RoutedEventArgs e) => StressSelectionChanged();
    private void ChkGpu_Click(object sender, RoutedEventArgs e) => StressSelectionChanged();
    private void StressDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => StressDurationChanged();
    private void TempLimit_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ShowLimits();
        // Parado, o tracejado do corte no gráfico acompanha o controle
        if (!IsInitialized || IsRunning || _demo) return;
        _tempLimit = TempLimit.Value;
        _gpuTempLimit = GpuTempLimit.Value;
        DrawChart();
    }

    /// <summary>Os passos do controle de duração, em minutos (0 = sem limite): curtos no começo, onde a escolha fina importa.</summary>
    private static readonly int[] DurationSteps = { 5, 10, 15, 20, 30, 45, 60, 90, 120, 0 };

    private int ChosenMinutes => DurationSteps[Math.Clamp((int)Math.Round(StressDuration.Value), 0, DurationSteps.Length - 1)];

    private static string DurationName(int minutes) => minutes switch
    {
        0 => "Sem limite",
        < 60 => $"{minutes} min",
        _ when minutes % 60 == 0 => $"{minutes / 60} h",
        _ => $"{minutes / 60} h {minutes % 60}",
    };

    /// <summary>As paradas da resolução da janela 3D; a última é a tela cheia, no tamanho do monitor.</summary>
    private static readonly (string Name, int Width, int Height)[] GpuResolutions =
    {
        ("720p", 1280, 720), ("1080p", 1920, 1080), ("1440p", 2560, 1440), ("Tela cheia", 0, 0),
    };

    private static readonly (string Name, int Samples)[] GpuMsaa = { ("Desligado", 1), ("4x", 4), ("8x", 8) };

    /// <summary>A carga do desenho: camadas de pelo (overdraw) e iterações do shader por pixel.</summary>
    private static readonly (string Name, int Shells, int Iterations)[] GpuLoads =
    {
        ("Normal", 96, 32), ("Alta", 128, 48), ("Extrema", 160, 64),
    };

    private static readonly double[] RamFractions = { 0.4, 0.5, 0.6, 0.7, 0.8 };

    /// <summary>A opção escolhida numa fileira de pílulas: a Tag de cada uma é a posição dela na lista do código.</summary>
    private static T Chosen<T>(T[] options, Panel panel, int fallback) =>
        options[int.TryParse(Ui.CheckedTag(panel), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? Math.Clamp(i, 0, options.Length - 1) : fallback];

    /// <summary>O valor de cada controle deslizante ao lado do nome dele.</summary>
    private void ShowLimits()
    {
        if (CpuLimitText == null || GpuLimitText == null) return;
        CpuLimitText.Text = $"{TempLimit.Value:0} °C";
        GpuLimitText.Text = $"{GpuTempLimit.Value:0} °C";
    }
    private void TempChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    // Com o mouse em cima, o gráfico mostra o valor exato daquele segundo, como os gráficos da Visão geral e dos Discos
    private double? _hoverX;

    private void TempChart_MouseMove(object sender, MouseEventArgs e)
    {
        _hoverX = e.GetPosition(TempChart).X;
        DrawChart();
    }

    private void TempChart_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverX = null;
        DrawChart();
    }

    private void StressDurationChanged()
    {
        if (!IsInitialized || StressDurationText == null) return;
        StressDurationText.Text = DurationName(ChosenMinutes);
        if (!IsRunning && !_demo) ShowStressTime(TimeSpan.Zero, TimeSpan.FromMinutes(ChosenMinutes));
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
        UpdateConfigEnabled(running: IsRunning);
        UpdateColumnStates();
    }

    /// <summary>
    /// Os ajustes ficam sempre à vista (o cartão não muda de tamanho); o de uma peça fora do teste esmaece e trava, e durante o
    /// teste tudo trava. O corte do processador vale também para o teste só de memória, que o esquenta.
    /// </summary>
    private void UpdateConfigEnabled(bool running)
    {
        bool cpu = ChkCpu.IsChecked == true, ram = ChkRam.IsChecked == true, gpu = ChkGpu.IsChecked == true;
        // Nenhuma peça marcada, nada para ajustar: a duração e o corte do processador também se apagam
        bool any = cpu || ram || gpu;
        StressDurationSection.IsEnabled = !running && any;
        BtnStress.IsEnabled = running || any;
        BtnStress.ToolTip = running || any ? "Põe as peças marcadas no máximo e acompanha a temperatura, para ver se o PC aguenta sem travar." : "Marque uma peça em cima para começar.";
        CpuLimitSection.IsEnabled = !running && (cpu || ram);
        GpuLimitSection.IsEnabled = GpuResSection.IsEnabled = GpuMsaaSection.IsEnabled = GpuOptions.IsEnabled = !running && gpu;
        RamSection.IsEnabled = !running && ram;
        const string Off = "Marque a peça em cima para ajustar.";
        StressDurationSection.ToolTip = any || running ? null : "Marque uma peça em cima para ajustar.";
        CpuLimitSection.ToolTip = CpuLimitSection.IsEnabled || running ? null : "Marque o processador ou a memória em cima para ajustar.";
        GpuLimitSection.ToolTip = GpuResSection.ToolTip = GpuMsaaSection.ToolTip = GpuOptions.ToolTip = GpuOptions.IsEnabled || running ? null : Off;
        RamSection.ToolTip = RamSection.IsEnabled || running ? null : Off;
    }

    /// <summary>
    /// As três colunas ficam sempre na tela, do mesmo tamanho; a que não entra no teste fica um tom abaixo, com "Fora do teste"
    /// no cabeçalho (as leituras do sensor continuam ao vivo). A que entra não diz nada: a tecla acesa em cima já diz.
    /// </summary>
    private void UpdateColumnStates()
    {
        bool running = IsRunning || _demo;
        bool cpu = running ? _runCpu : ChkCpu.IsChecked == true;
        bool ram = running ? _runRam : ChkRam.IsChecked == true;
        bool gpu = running ? _runGpu : ChkGpu.IsChecked == true;
        SetColumn(CpuColumn, CpuState, cpu);
        SetColumn(GpuColumn, GpuState, gpu);
        SetColumn(RamColumn, RamState, ram);
    }

    private static void SetColumn(Border column, TextBlock state, bool on)
    {
        column.Opacity = on ? 1 : 0.35;
        state.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    internal void StartStress()
    {
        if (_context == null) return;
        StressHint.Text = "";
        if (_context.Workloads.IsBusy) { StressHint.Text = "Termine o benchmark antes de iniciar o teste de carga."; return; }
        bool cpu = ChkCpu.IsChecked == true, ram = ChkRam.IsChecked == true, gpu = ChkGpu.IsChecked == true;
        if (!cpu && !ram && !gpu) { StressHint.Text = "Escolha pelo menos um componente."; return; }

        // Os controles deslizantes só param dentro da faixa, então o corte já chega válido
        _tempLimit = TempLimit.Value;
        _gpuTempLimit = GpuTempLimit.Value;
        double minutes = ChosenMinutes;
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

        double fraction = Chosen(RamFractions, RamPanel, 2);
        _stressDuration = TimeSpan.FromMinutes(minutes);
        _stressStart = DateTime.Now;
        _stressMaxTemp = 0;
        _gpuMaxTemp = 0;
        _tempHistory.Clear();
        _gpuTempHistory.Clear();

        _stressLease = _context.Workloads.TryBegin(WorkloadOperations.Stress);
        if (_stressLease == null) { StressHint.Text = "Termine o outro teste antes."; return; }
        (_runCpu, _runRam, _runGpu) = (cpu, ram, gpu);

        try
        {
            if (cpu || ram) _stress.Start(cpu, ram, fraction);

            if (gpu)
            {
                // Em janela, a temperatura continua à vista no CoreBi; em tela cheia, o corte por temperatura continua valendo
                var res = Chosen(GpuResolutions, GpuResPanel, 1);
                var load = Chosen(GpuLoads, GpuLoadPanel, 1);
                _burn.Start(new GpuBurnSettings
                {
                    Fullscreen = res.Width == 0,
                    Width = res.Width,
                    Height = res.Height,
                    Msaa = Chosen(GpuMsaa, GpuMsaaPanel, 1).Samples,
                    Shells = load.Shells,
                    Iterations = load.Iterations,
                    AdapterName = _context.PrimaryGpu?.Name ?? "",
                });
                _gpuBurnOn = true;
            }

            ResetStressReadings();
            BtnStressReset.IsEnabled = false;
            SetStressControls(running: true);
            UpdateColumnStates();
            SetStatus("Rodando", aborted: false);
            StDetail.Text = minutes > 0 ? $"Termina às {_stressStart + _stressDuration:HH:mm}" : "";
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

    private static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t:mm\\:ss}" : $"{(int)t.TotalMinutes}:{t:ss}";

    private void ResetStressReadings()
    {
        ShowStressTime(TimeSpan.Zero, _stressDuration);
        StProgress.ScaleX = 0;
        StTempMax.Text = StGpuTempMax.Text = StFps.Text = StRam.Text = StRamSize.Text = "—";
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

        if (_gpuBurnOn)
        {
            _burn.Stop();
            if (_stress.Running || _burn.Running) { _pendingStop = (reason, status, aborted); SetStatus("Parando…", false); return; }
            _gpuBurnOn = false;
        }
        if (_stress.Running) { _pendingStop = (reason, status, aborted); SetStatus("Parando…", false); return; }
        _pendingStop = null;
        _stressLease?.Dispose();
        _stressLease = null;

        var elapsed = DateTime.Now - _stressStart;
        _context?.RecordEvent("", "Teste de carga", aborted ? "Interrompido. " + reason : reason, elapsed.ToString(@"hh\:mm\:ss"), "");
        SetStressControls(running: false);
        UpdateColumnStates();

        if (aborted) status = "Interrompido";
        SetStatus(status, aborted);
        // "Parado" e "Concluído" já dizem tudo; o motivo só aparece quando acrescenta algo (o corte, a janela fechada, a falha)
        StDetail.Text = reason is "Parado por você" or "Tempo de teste concluído" ? "" : reason;
        BtnStressReset.IsEnabled = true;

        var run = new StressRun
        {
            Date = DateTime.Now,
            Cpu = _runCpu,
            Ram = _runRam,
            Gpu = _runGpu,
            PlannedMinutes = _stressDuration.TotalMinutes,
            Seconds = Math.Round(elapsed.TotalSeconds),
            Outcome = aborted ? StressOutcome.Aborted : status == "Concluído" ? StressOutcome.Completed : StressOutcome.Stopped,
            Reason = reason,
            CpuMaxC = hadCpu && _stressMaxTemp > 0 ? Math.Round(_stressMaxTemp, 1) : null,
            GpuMaxC = _runGpu && _gpuMaxTemp > 0 ? Math.Round(_gpuMaxTemp, 1) : null,
            RamPasses = hadRam ? passes : null,
            RamErrors = hadRam ? errors : null,
            GpuAvgFps = _runGpu && _burn.AverageFps > 0 ? Math.Round(_burn.AverageFps, 1) : null,
        };
        if (_context != null)
        {
            var runs = _context.Settings.StressRuns;
            runs.Insert(0, run);
            if (runs.Count > StressRun.Capacity) runs.RemoveRange(StressRun.Capacity, runs.Count - StressRun.Capacity);
            _context.SaveSettings();
        }
        ShowHistory();
    }

    private void ShowHistory(IEnumerable<StressRun>? runs = null)
    {
        var rows = (runs ?? _context?.Settings.StressRuns ?? []).Select(HistoryRow).ToList();
        StressHistory.ItemsSource = rows;
        StressHistoryClear.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StressHistoryClear_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null || _demo || !ConfirmButton.Confirm(sender)) return;
        _context.Settings.StressRuns.Clear();
        _context.SaveSettings();
        ShowHistory();
    }

    /// <summary>
    /// Um teste encerrado em duas linhas curtas: como terminou ("Estável por 10 min", "Parado aos 3:12", "Interrompido aos
    /// 4:05") e a máxima de cada peça por sigla ("CPU 82 °C · GPU 85 °C · RAM sem erros"). O motivo e os números completos (as
    /// passadas, o FPS médio) ficam na dica da linha.
    /// </summary>
    private static StressHistoryRow HistoryRow(StressRun r)
    {
        var took = TimeSpan.FromSeconds(r.Seconds);
        string title = r.Outcome switch
        {
            StressOutcome.Completed => $"Estável por {DurationText(TimeSpan.FromMinutes(r.PlannedMinutes))}",
            StressOutcome.Aborted => $"Interrompido aos {Clock(took)}",
            _ => $"Parado aos {Clock(took)}",
        };
        var facts = new List<string>();
        var detail = new List<string>();
        if (r.Outcome != StressOutcome.Completed && r.Reason.Length > 0) detail.Add(r.Reason + ".");
        if ((r.Cpu || r.Ram) && r.CpuMaxC is double c) facts.Add($"CPU {c:0} °C");
        else if (r.Cpu) facts.Add("CPU");
        if (r.Gpu)
        {
            facts.Add(r.GpuMaxC is double g ? $"GPU {g:0} °C" : "GPU");
            if (r.GpuAvgFps is double f) detail.Add($"Placa de vídeo: {f:0} FPS em média.");
        }
        if (r.Ram)
        {
            facts.Add(r.RamErrors is > 0 ? $"RAM {r.RamErrors:N0} {(r.RamErrors == 1 ? "erro" : "erros")}" : "RAM sem erros");
            if (r.RamPasses is long p) detail.Add($"Memória: {p:N0} {(p == 1 ? "passada" : "passadas")}.");
        }
        string tone = r.Outcome switch { StressOutcome.Aborted => "Danger", StressOutcome.Stopped => "Warn", _ => "Ok" };
        return new StressHistoryRow(title, string.Join("\n", detail), string.Join(" · ", facts), ReadoutFormat.When(r.Date, compact: true), tone);
    }

    private void SetStressControls(bool running)
    {
        Ui.SetRunning(BtnStress, running, "Iniciar teste");
        ChkCpu.IsEnabled = ChkRam.IsEnabled = ChkGpu.IsEnabled = !running;
        UpdateConfigEnabled(running);
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
        if (_context == null || _demo) return;
        if (_context.SensorsRead && Skeleton.GetActive(StressConfigCard)) Skeleton.SetActive(StressConfigCard, false);
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

        // Toda linha sempre com valor ou travessão: as colunas não mudam de tamanho nem de lugar.
        StTemp.Text = temp == null ? "—" : $"{temp:0.#}";
        // A leitura principal fica âmbar a 5 °C do corte.
        StTemp.SetResourceReference(TextBlock.ForegroundProperty, temp == null ? "Faint" : _stress.Running && temp >= _tempLimit - 5 ? "Warn" : "Fg");
        StTempMax.Text = _stressMaxTemp > 0 ? $"{_stressMaxTemp:0.#} °C" : "—";
        StClock.Text = _context.ReadCpuLive().AvgClockMHz is float clock ? $"{clock:0} MHz" : "—";
        StPower.Text = power == null ? "—" : $"{power:0.#} W";
        StLoad.Text = load == null ? "—" : $"{load:0} %";

        StGpuTemp.Text = gTemp == null ? "—" : $"{gTemp:0.#}";
        StGpuTemp.SetResourceReference(TextBlock.ForegroundProperty, gTemp == null ? "Faint" : _gpuBurnOn && gTemp >= _gpuTempLimit - 5 ? "Warn" : "Fg");
        StGpuTempMax.Text = _gpuMaxTemp > 0 ? $"{_gpuMaxTemp:0.#} °C" : "—";
        StGpuPower.Text = gPower == null ? "—" : $"{gPower:0.#} W";
        StGpuCap.Text = _context.PrimaryGpuPerfCap() is { Length: > 0 } cap ? cap : "—";
        StFps.Text = _gpuBurnOn && _burn.Fps > 0 ? $"{_burn.Fps:0} · {_burn.AverageFps:0}" : "—";

        var memory = MemoryInfo.ReadPhysicalUsage();
        StRamUse.Text = memory is { } m ? $"{m.UsedPercent:0} %" : "—";
        StRamFree.Text = memory is { } f ? $"{f.Available / 1e9:0.0} GB" : "—";

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

    /// <summary>
    /// Prévia para as capturas de tela: um teste do processador em andamento (6 min de 10, temperatura subindo) e um histórico
    /// de exemplo, sem carregar nada nem salvar. false volta à página real.
    /// </summary>
    internal void PreviewDemo(bool on)
    {
        _demo = on;
        _tempHistory.Clear();
        _gpuTempHistory.Clear();
        Ui.SetRunning(BtnStress, on, "Iniciar teste");
        if (!on)
        {
            ChkRam.IsChecked = ChkGpu.IsChecked = false;
            ChkCpu.IsChecked = true;
            UpdateColumnStates();
            ResetStressReadings();
            StressDurationChanged();
            SetStatus("", false);
            StDetail.Text = "";
            StTemp.Text = "—";
            ShowHistory();
            DrawChart();
            return;
        }

        var rng = new Random(7);
        for (int i = 0; i < HistoryLength; i++)
        {
            // Ociosa nos primeiros 2 min, sobe quando a carga começa e assenta perto de 81 °C
            double t = i < 120 ? 44 + rng.NextDouble() * 2 : 81 - 35 * Math.Exp(-(i - 120) / 22.0) + rng.NextDouble() * 1.6;
            _tempHistory.Add(Math.Round(t, 1));
        }
        ShowStressTime(TimeSpan.FromSeconds(372), TimeSpan.FromMinutes(10));
        StProgress.ScaleX = 0.62;
        StLed.Visibility = Visibility.Visible;
        StLed.SetResourceReference(Shape.FillProperty, "Signal");
        StStatus.Text = "Rodando";
        StStatus.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        StDetail.Text = $"Termina às {DateTime.Now.AddSeconds(228):HH:mm}";
        StTemp.Text = $"{_tempHistory[^1]:0.#}";
        StTempMax.Text = $"{_tempHistory.Max():0.#} °C";
        StClock.Text = "4.410 MHz";
        StPower.Text = "118 W";
        StLoad.Text = "100 %";
        // As três peças no teste, o caso mais cheio: placa de vídeo e memória também
        (_runCpu, _runRam, _runGpu) = (true, true, true);
        ChkRam.IsChecked = ChkGpu.IsChecked = true;
        _gpuTempHistory.Clear();
        for (int i = 0; i < HistoryLength; i++)
        {
            double g = i < 120 ? 39 + rng.NextDouble() * 1.5 : 74 - 33 * Math.Exp(-(i - 120) / 30.0) + rng.NextDouble() * 1.2;
            _gpuTempHistory.Add(Math.Round(g, 1));
        }
        StGpuTemp.Text = $"{_gpuTempHistory[^1]:0.#}";
        StGpuTempMax.Text = $"{_gpuTempHistory.Max():0.#} °C";
        StGpuPower.Text = "182 W";
        StFps.Text = "141 · 138";
        StGpuCap.Text = "Energia";
        StRamErrors.Text = "0";
        StRam.Text = "37";
        StRamSize.Text = "9,6 GB";
        StRamUse.Text = "78 %";
        StRamFree.Text = "6,8 GB";
        UpdateColumnStates();
        var now = DateTime.Now;
        ShowHistory(new[]
        {
            new StressRun { Date = now.AddDays(-1).AddHours(-2), Cpu = true, PlannedMinutes = 10, Seconds = 600, Outcome = StressOutcome.Completed, Reason = "Tempo de teste concluído", CpuMaxC = 82 },
            new StressRun { Date = now.AddDays(-2), Cpu = true, Ram = true, PlannedMinutes = 30, Seconds = 1800, Outcome = StressOutcome.Completed, Reason = "Tempo de teste concluído", CpuMaxC = 84, RamPasses = 37, RamErrors = 0 },
            new StressRun { Date = now.AddDays(-3), Gpu = true, PlannedMinutes = 10, Seconds = 247, Outcome = StressOutcome.Aborted, Reason = "A placa de vídeo chegou a 85 °C, no limite de 85 °C", GpuMaxC = 85, GpuAvgFps = 141 },
            new StressRun { Date = now.AddDays(-5), Cpu = true, PlannedMinutes = 0, Seconds = 192, Outcome = StressOutcome.Stopped, Reason = "Parado por você", CpuMaxC = 79 },
        });
        DrawChart();
    }

    /// <summary>Redesenha o gráfico com as cores do tema atual.</summary>
    internal void RedrawTheme() => DrawChart();

    private void DrawChart()
    {
        TempChart.Children.Clear();
        bool showGpu = ChkGpu.IsChecked == true;
        bool showCpu = ChkCpu.IsChecked == true || ChkRam.IsChecked == true || !showGpu;
        DrawLegend(showCpu, showGpu);
        double w = TempChart.ActualWidth, h = TempChart.ActualHeight;
        if (w < 160 || h < 60) return;

        var ui = (FontFamily)FindResource("UiFont");
        var readout = (FontFamily)FindResource("Readout");
        var faint = ThemeManager.Brush("Faint");

        TextBlock Text(string text, Brush fg, double size = 12, FontFamily? font = null)
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

        // Só as linhas, os pontos de agora e os cortes tracejados na cor de cada peça: os nomes, as leituras e os valores dos
        // cortes ficam na legenda, em cima, então o gráfico não ganha rótulos sobrepostos com as três peças no teste.
        var series = new List<(List<double> Data, string Channel, byte Alpha)>();
        if (showGpu && _gpuTempHistory.Count >= 2) series.Add((_gpuTempHistory, "Gpu", 0x1C));
        if (showCpu && _tempHistory.Count >= 2) series.Add((_tempHistory, "Cpu", 0x22));

        double yMin = 20, yMax = Math.Max(110, Math.Max(_tempLimit, showGpu ? _gpuTempLimit : 0) + 10);
        var ticks = new List<(double Value, TextBlock Label)>();
        for (double v = yMin; v <= yMax; v += 20) ticks.Add((v, Text($"{v:0} °C", faint)));
        double left = ticks.Max(t => Width(t.Label)) + 8;
        const double top = 8, bottom = 22, right = 8;
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

        // Os dois cortes na mesma altura (o mesmo valor, ou a menos de 3 px) viram uma linha só, em traços alternados nas duas
        // cores: um não some atrás do outro
        var limits = Limits(showCpu, showGpu).ToList();
        bool shared = limits.Count == 2 && Math.Abs(Y(limits[0].Value) - Y(limits[1].Value)) < 3;
        for (int i = 0; i < limits.Count; i++)
        {
            var (value, channel) = limits[i];
            double y = shared ? Math.Round((Y(limits[0].Value) + Y(limits[1].Value)) / 2) : Y(value);
            TempChart.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotW,
                Y1 = y,
                Y2 = y,
                Stroke = ThemeManager.Brush(channel),
                StrokeThickness = 1.2,
                StrokeDashArray = shared ? new DoubleCollection { 4, 4 } : new DoubleCollection { 4, 3 },
                StrokeDashOffset = shared && i == 1 ? 4 : 0,
                Opacity = 0.9,
            });
        }

        if (series.Count == 0)
        {
            var empty = Text(EmptyChartText(showCpu, showGpu), ThemeManager.Brush("Muted"), 13, ui);
            empty.TextAlignment = TextAlignment.Center;
            empty.TextWrapping = TextWrapping.Wrap;
            empty.MaxWidth = Math.Min(520, plotW - 40);
            empty.Background = ThemeManager.Brush("Surface"); // a frase não cruza a linha de grade que passa no meio
            empty.Padding = new Thickness(12, 4, 12, 4);
            empty.Measure(new Size(empty.MaxWidth, double.PositiveInfinity));
            Place(empty, left + (plotW - empty.DesiredSize.Width) / 2, top + (plotH - empty.DesiredSize.Height) / 2);
            return;
        }

        double step = plotW / (HistoryLength - 1);
        foreach (var (data, channel, areaAlpha) in series)
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
            var dot = new Ellipse { Width = 7, Height = 7, Fill = stroke, Stroke = ThemeManager.Brush("Surface"), StrokeThickness = 1.5 };
            Place(dot, end.X - 3.5, end.Y - 3.5);
        }
        DrawHover(series, left, top, plotW, plotH, step, Y, Place);
    }

    /// <summary>
    /// A leitura sob o mouse: fio tracejado na vertical, um ponto em cada linha e a caixa com quando foi e a temperatura de cada
    /// peça (o mesmo desenho da dica do gráfico da Visão geral).
    /// </summary>
    private void DrawHover(List<(List<double> Data, string Channel, byte Alpha)> series, double left, double top, double plotW, double plotH,
        double step, Func<double, double> y, Action<UIElement, double, double> place)
    {
        if (_hoverX is not double mx || mx < left - 4 || mx > left + plotW + 4) return;
        int at = (int)Math.Round(Math.Clamp((mx - left) / step, 0, HistoryLength - 1));
        var values = new List<(string Channel, double Value)>();
        foreach (var (data, channel, _) in series)
        {
            int i = at - (HistoryLength - data.Count);
            if (i >= 0 && i < data.Count) values.Add((channel, data[i]));
        }
        if (values.Count == 0) return;

        double px = left + at * step;
        var wire = new Line { X1 = Math.Round(px) + 0.5, X2 = Math.Round(px) + 0.5, Y1 = top, Y2 = top + plotH, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, Stroke = ThemeManager.Brush("Faint"), SnapsToDevicePixels = true };
        TempChart.Children.Add(wire);
        foreach (var (channel, value) in values)
        {
            var dot = new Ellipse { Width = 11, Height = 11, StrokeThickness = 2.5, Stroke = ThemeManager.Brush(channel), Fill = ThemeManager.Brush("Surface") };
            place(dot, px - 5.5, y(value) - 5.5);
        }

        var ui = (FontFamily)FindResource("UiFont");
        var readout = (FontFamily)FindResource("Readout");
        var rows = new StackPanel();
        var when = new TextBlock { Text = ChartScale.RelativeAgo(HistoryLength - 1 - at), FontFamily = ui, FontSize = 12, Foreground = ThemeManager.Brush("Faint") };
        Typography.SetNumeralAlignment(when, FontNumeralAlignment.Tabular);
        rows.Children.Add(when);
        // Processador primeiro, na mesma ordem da legenda
        foreach (var (channel, value) in values.OrderBy(v => v.Channel == "Cpu" ? 0 : 1))
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var swatch = new Border { Width = 10, Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Background = ThemeManager.Brush(channel) };
            var name = new TextBlock { Text = channel == "Cpu" ? "Processador" : "Placa de vídeo", FontFamily = ui, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeManager.Brush("Muted") };
            var number = new TextBlock { Text = $"{value:0.#} °C", FontFamily = readout, FontStretch = FontStretches.SemiCondensed, FontSize = 15, Margin = new Thickness(16, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Foreground = ThemeManager.Brush("Fg") };
            Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
            Grid.SetColumn(number, 1);
            row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, name } });
            row.Children.Add(number);
            rows.Children.Add(row);
        }
        var tip = new Border
        {
            CornerRadius = (CornerRadius)FindResource("RadiusControl"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8),
            Background = ThemeManager.Brush("Raised"),
            BorderBrush = ThemeManager.Brush("LineStrong"),
            IsHitTestVisible = false,
            Child = rows,
        };
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tw = tip.DesiredSize.Width, th = tip.DesiredSize.Height;
        double tx = px + 14 + tw > TempChart.ActualWidth ? px - 14 - tw : px + 14;
        double anchor = values.Min(v => y(v.Value));
        double ty = Math.Clamp(anchor - th / 2, top, Math.Max(top, top + plotH - th));
        Panel.SetZIndex(tip, 10);
        place(tip, tx, ty);
    }

    private IEnumerable<(double Value, string Channel)> Limits(bool cpu, bool gpu)
    {
        if (cpu) yield return (_tempLimit, "Cpu");
        if (gpu) yield return (_gpuTempLimit, "Gpu");
    }

    /// <summary>
    /// Legenda do gráfico, à direita do título: por peça, o traço na cor dela com o nome e a temperatura de agora, e o tracejado
    /// com o corte. Com as duas peças, uma depois da outra, separadas por um vão.
    /// </summary>
    private void DrawLegend(bool cpu, bool gpu)
    {
        TempLegend.Children.Clear();
        var ui = (FontFamily)FindResource("UiFont");
        var readout = (FontFamily)FindResource("Readout");
        void Item(string channel, string name, List<double> data, double limit, bool first)
        {
            var color = ThemeManager.Brush(channel);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(first ? 0 : 28, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Line { X1 = 0, X2 = 14, Y1 = 0, Y2 = 0, Stroke = color, StrokeThickness = 2, VerticalAlignment = VerticalAlignment.Center });
            var label = new TextBlock { FontFamily = ui, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            label.Inlines.Add(new Run(name + " "));
            label.Inlines.Add(new Run(data.Count > 0 ? $"{data[^1]:0} °C" : "—") { FontFamily = readout, FontSize = 13 });
            row.Children.Add(label);
            row.Children.Add(new Line { X1 = 0, X2 = 14, Y1 = 0, Y2 = 0, Stroke = color, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 3, 2 }, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var cut = new TextBlock { Text = $"corte {limit:0} °C", FontFamily = ui, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            cut.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            row.Children.Add(cut);
            TempLegend.Children.Add(row);
        }
        if (cpu) Item("Cpu", "Processador", _tempHistory, _tempLimit, true);
        if (gpu) Item("Gpu", "Placa de vídeo", _gpuTempHistory, _gpuTempLimit, !cpu);
    }

    /// <summary>Por que o gráfico está vazio, em palavras simples: ainda lendo ou o sensor que falta e como resolver.</summary>
    private string EmptyChartText(bool cpu, bool gpu)
    {
        if (_context is not { SensorsRead: true }) return "Lendo os sensores…";
        if (cpu && _context.Sensors.CpuTemperature == null)
            return _context.Sensors.DiagnoseCpuSensors() switch
            {
                SensorMonitor.CpuSensorProblem.NotElevated => "Sem a temperatura do processador: o Windows só libera esse sensor com o CoreBi aberto como administrador.",
                SensorMonitor.CpuSensorProblem.PawnIOMissing => "Sem a temperatura do processador: falta o driver PawnIO, que lê o sensor. Use Corrigir, no pé do menu.",
                _ => "O sensor de temperatura do processador não respondeu.",
            };
        if (gpu && _context.PrimaryGpuTemp() == null) return "A placa de vídeo não informa a temperatura.";
        return "A temperatura aparece aqui segundo a segundo, com o limite de corte.";
    }

    public void Dispose()
    {
        _stress.Stop();
        _burn.Stop();
        _stressLease?.Dispose();
        _stressLease = null;
    }
}
