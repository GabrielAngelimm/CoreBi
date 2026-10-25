using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Power;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Storage;
using LibreHardwareMonitor.Hardware;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CoreBi.Desktop.Views;

/// <summary>
/// Energia: de onde vem a energia (tomada, bateria ou nobreak), o plano de energia com o essencial do que ele regula, o consumo
/// ao vivo de processador e placa de vídeo, as tensões e quando o PC ficou ligado na semana. Fonte, plano e histórico são lidos
/// do Windows ao abrir a página; consumo e tensões seguem os sensores a cada segundo, mesmo com a página fechada (para a sessão).
/// </summary>
public partial class PowerView : UserControl
{
    public sealed record SettingItem(string Title, string Value, string? Tip, string BeforeText = "", bool Changed = false);
    public sealed record PlanItem(Guid Id, string Name, bool IsActive, bool IsPending);

    /// <summary>Base das linhas que mudam a cada segundo: avisa a interface só do que mudou.</summary>
    public abstract class LiveItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class RailItem : LiveItem
    {
        private double _value = double.NaN, _min = double.NaN, _max = double.NaN;
        private string _valueText = "", _deviationText = "";
        private bool _off;
        public RailItem(string title, double nominal, bool first) { Title = title; Nominal = nominal; First = first; }
        public string Title { get; }
        public double Nominal { get; }
        public bool First { get; }
        public string Tip => $"Nominal {Nominal:0.0#} V. A especificação ATX aceita ±5 % ({Nominal * 0.95:0.00} a {Nominal * 1.05:0.00} V). A faixa embaixo é o que variou desde que o CoreBi abriu.";
        public double Value { get => _value; set => Set(ref _value, value); }
        public double Min { get => _min; set => Set(ref _min, value); }
        public double Max { get => _max; set => Set(ref _max, value); }
        public string ValueText { get => _valueText; set => Set(ref _valueText, value); }
        public string DeviationText { get => _deviationText; set => Set(ref _deviationText, value); }
        public bool Off { get => _off; set => Set(ref _off, value); }
    }

    public sealed class VoltItem : LiveItem
    {
        private double _value = double.NaN, _min = double.NaN, _max = double.NaN;
        private string _valueText = "";
        private bool _low;
        public VoltItem(string title, double lo, double hi) { Title = title; Lo = lo; Hi = hi; }
        public string Title { get; }
        public double Lo { get; }
        public double Hi { get; }
        public string Tip => $"Escala de {Lo:0.0#} a {Hi:0.0#} V. A faixa clara é o que variou desde que o CoreBi abriu.";
        public double Value { get => _value; set => Set(ref _value, value); }
        public double Min { get => _min; set => Set(ref _min, value); }
        public double Max { get => _max; set => Set(ref _max, value); }
        public string ValueText { get => _valueText; set => Set(ref _valueText, value); }
        public bool Low { get => _low; set => Set(ref _low, value); }
    }

    private FeatureContext _context = null!;
    private SensorMonitor _monitor = null!;
    private Func<GpuInfo?> _mainGpu = () => null;
    private Func<float?> _gpuPower = () => null;
    private bool _refreshing, _applyingPlan, _stacked, _dirty;
    private PowerPlan? _pendingPlan;

    // Sessão: consumo desde que o CoreBi abriu (1 amostra/s)
    private readonly List<(float? Cpu, float? Gpu)> _trace = new();
    private DateTime? _sessionStart, _lastSample, _peakAt;
    private double _energyWh, _sumW, _peakW;
    private int _countW;
    private bool? _lastAc;

    private readonly List<RailItem> _rails = new();
    private readonly List<VoltItem> _volts = new();

    /// <summary>Última leitura completa (fonte e baterias); nula até a primeira.</summary>
    internal PowerSnapshot? Snapshot { get; private set; }
    internal PlanDetail? Plan { get; private set; }
    internal PowerHistory? History { get; private set; }

    public PowerView() => InitializeComponent();

    internal void Initialize(FeatureContext context, SensorMonitor monitor, Func<GpuInfo?> mainGpu, Func<float?> gpuPower)
    {
        _context = context;
        _monitor = monitor;
        _mainGpu = mainGpu;
        _gpuPower = gpuPower;
        RailRows.ItemsSource = _rails;
        VoltRows.ItemsSource = _volts;
    }

    private async void BtnEnergyRefresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    internal async Task RefreshAsync()
    {
        if (_refreshing || _context.IsClosing) return;
        _refreshing = true;
        _dirty = false;
        BtnEnergyRefresh.IsEnabled = false;
        EnergyStatus.Text = "Lendo bateria e energia…";
        try
        {
            var power = Task.Run(PowerStatus.Read);
            var plan = Task.Run(PowerPlans.Read);
            var history = Task.Run(() => PowerHistoryReader.Read());
            await Task.WhenAll(power, plan, history);
            if (_context.IsClosing) return;
            Snapshot = power.Result;
            Plan = plan.Result;
            History = history.Result;
            _lastAc = Snapshot.OnAc;
            ShowSource(Snapshot);
            ShowPlan(Plan);
            ShowHistory(History);
            ShowLive();
            EnergyStatus.Text = $"Lido às {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { if (!_context.IsClosing) EnergyStatus.Text = "Falhou: " + ex.Message; }
        finally
        {
            _refreshing = false;
            if (!_context.IsClosing) BtnEnergyRefresh.IsEnabled = true;
        }
    }

    private static string? SourceWord(PowerSnapshot s) => s.Batteries.Any(b => !b.IsUps)
        ? s.OnAc == false ? "Na bateria" : "Na tomada"
        : s.OnAc == false ? "Alimentado pelo nobreak" : "Na tomada";

    // ---------------- fonte e bateria ----------------

    private void ShowSource(PowerSnapshot s)
    {
        var battery = s.Batteries.FirstOrDefault(b => !b.IsUps);
        var ups = s.Batteries.FirstOrDefault(b => b.IsUps);
        bool onBattery = s.OnAc == false;
        SourceTitle.Text = SourceWord(s);
        SourceGlyph.Text = battery == null ? "" : battery.Charging ? "" : "";
        SourceCaption.Text = battery != null
            ? string.Join(" · ", new[] { s.Batteries.Count(b => !b.IsUps) > 1 ? $"{s.Batteries.Count(b => !b.IsUps)} baterias" : "Notebook", s.BatterySaver ? "economia de bateria ligada" : null }.Where(x => x != null))
            : ups != null ? "PC de mesa · nobreak conectado por USB"
            : s.UpsPresent ? "PC de mesa · nobreak detectado"
            : "PC de mesa · nenhuma bateria";

        var shown = battery ?? ups;
        BatteryPanel.Visibility = shown == null ? Visibility.Collapsed : Visibility.Visible;
        if (shown == null) return;
        Readout(BatteryPct, shown.Percent is int p ? $"{p}" : "—", shown.Percent != null ? "%" : "");
        BatteryGauge.Value = shown.Percent ?? double.NaN;
        BatteryGauge.OnPower = !onBattery;
        BatteryState.Text = shown.StatusText ?? (onBattery ? "Descarregando" : "Na tomada");
        var time = new List<string>();
        if (shown.ToFullMin is int tf) time.Add($"cheia em ≈ {Duration(tf)}");
        if (shown.RuntimeMin is int rt) time.Add($"≈ {Duration(rt)} restantes");
        if (shown.RateW is double r) time.Add(r > 0 ? $"+{r:0.0} W" : $"−{-r:0.0} W");
        BatteryTime.Text = string.Join(" · ", time);

        // Saúde: só com as capacidades informadas (nobreak e alguns notebooks não informam)
        bool health = shown.DesignWh != null || shown.FullWh != null;
        BatteryHealth.Visibility = health ? Visibility.Visible : Visibility.Collapsed;
        if (!health) return;
        string tone = shown.Verdict switch { DiskVerdict.Good => "Ok", DiskVerdict.Warning => "Warn", DiskVerdict.Critical => "Danger", _ => "" };
        BatteryBadgeText.Text = shown.VerdictText;
        string soft = tone switch { "Ok" => "OkSoft", "Warn" => "WarnSoft", "Danger" => "DangerSoft", _ => "Raised" };
        BatteryBadge.SetResourceReference(Border.BackgroundProperty, soft);
        BatteryBadge.SetResourceReference(Border.BorderBrushProperty, tone.Length > 0 ? tone : "LineStrong");
        BatteryBadgeText.SetResourceReference(TextBlock.ForegroundProperty, tone.Length > 0 ? tone : "Muted");
        BatteryCapacity.Design = shown.DesignWh ?? double.NaN;
        BatteryCapacity.Full = shown.FullWh ?? double.NaN;
        BatteryCapacity.Now = shown.RemainingWh ?? double.NaN;
        BatteryCapacityText.Text = shown.FullWh is double f && shown.DesignWh is double d
            ? $"{f:0.0} Wh de {d:0.0} Wh originais" + (shown.RemainingWh is double now ? $" · {now:0.0} Wh agora" : "")
            : shown.FullWh is double f2 ? $"{f2:0.0} Wh de capacidade máxima" : "";
        BatteryStats.Children.Clear();
        void Stat(string label, string? value, string unit)
        {
            if (value == null) return;
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("StatLabel") });
            var t = new TextBlock { Style = (Style)FindResource("ReadoutText"), FontSize = 21, Margin = new Thickness(0, 3, 0, 0) };
            Readout(t, value, unit);
            box.Children.Add(t);
            BatteryStats.Children.Add(box);
        }
        Stat("Desgaste", shown.WearPct is double w ? $"{w:0.0}" : null, "%");
        Stat("Ciclos", shown.Cycles?.ToString("N0"), "");
        BatteryStats.Columns = Math.Max(1, BatteryStats.Children.Count);
        BatteryReason.Text = shown.Reason;
        BatteryReason.ToolTip = Join(shown.Manufacturer, shown.Model, shown.Chemistry) is { Length: > 0 } id ? id : null;
    }

    // ---------------- plano de energia ----------------

    /// <summary>
    /// Plano ativo e o que ele define. Com um plano escolhido que ainda não foi aplicado, a mesma área vira a prévia da troca:
    /// mostra os valores do novo plano, com o atual esmaecido ao lado do que muda, e os botões Cancelar e Aplicar.
    /// </summary>
    private void ShowPlan(PlanDetail p)
    {
        if (_pendingPlan is { } pending && (p.Plans.All(x => x.Id != pending.Id) || p.Active?.Id == pending.Id)) _pendingPlan = null;
        var preview = _pendingPlan is { } pp ? PowerPlans.ReadValues(pp.Id) : null;
        PlanList.ItemsSource = p.Plans.Select(x => new PlanItem(x.Id, x.Name, x.IsActive, x.Id == _pendingPlan?.Id)).ToList();

        bool battery = Snapshot?.Batteries.Any(b => !b.IsUps) == true;
        bool previewing = preview != null;
        RegSection.Visibility = p.Active == null ? Visibility.Collapsed : Visibility.Visible;
        if (previewing) RegSection.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        else RegSection.ClearValue(Border.BackgroundProperty);
        RegTitle.Text = previewing ? $"Ao trocar para {_pendingPlan!.Name}" : "O que o plano define";
        RegCaption.Visibility = battery && !previewing ? Visibility.Visible : Visibility.Collapsed;
        PlanConfirm.Visibility = previewing ? Visibility.Visible : Visibility.Collapsed;
        PlanApply.IsEnabled = !_applyingPlan;

        var now = p.Values;
        var shown = preview ?? now;
        string Range(PlanValues v, bool dc) => (dc ? v.ProcessorMinDc : v.ProcessorMinAc) is int lo && (dc ? v.ProcessorMaxDc : v.ProcessorMaxAc) is int hi ? lo == hi ? $"{hi} %" : $"{lo} – {hi} %" : "";
        string Proc(PlanValues v) => battery && Range(v, true) is { Length: > 0 } onDc && onDc != Range(v, false) ? $"{Range(v, false)} · {onDc}" : Range(v, false);

        ProcBox.Visibility = shown.ProcessorMinAc != null && shown.ProcessorMaxAc != null ? Visibility.Visible : Visibility.Collapsed;
        if (shown.ProcessorMinAc is int min && shown.ProcessorMaxAc is int max)
        {
            ProcMeter.From = min;
            ProcMeter.To = max;
            string current = Proc(now), target = Proc(shown);
            ProcText.Inlines.Clear();
            if (previewing && current != target)
            {
                var old = new Run(current + "  →  ");
                old.SetResourceReference(TextElement.ForegroundProperty, "Faint");
                ProcText.Inlines.Add(old);
                var fresh = new Run(target);
                fresh.SetResourceReference(TextElement.ForegroundProperty, "Accent");
                ProcText.Inlines.Add(fresh);
            }
            else ProcText.Inlines.Add(new Run(target));
            // Só o caso que pede atenção ganha texto: máximo abaixo de 100 % segura o clock com carga
            ProcNote.Text = $"Máximo em {max} %: o processador não chega ao clock total neste plano.";
            ProcNote.Visibility = max < 100 ? Visibility.Visible : Visibility.Collapsed;
        }

        string Text(PlanSetting s) => battery && s.Dc != s.Ac ? $"{s.Ac} · {s.Dc}" : s.Ac;
        // O turbo é "Agressivo" em todos os planos de fábrica, então só ganha uma linha quando alguém o mudou (aqui ou no plano escolhido)
        bool OffDefault(PlanValues v) => v.Settings.Any(x => x.Key == "boost" && (x.Ac != "Agressivo" || x.Dc != "Agressivo"));
        bool showBoost = OffDefault(now) || (preview != null && OffDefault(preview));
        PlanSettings.ItemsSource = shown.Settings.Where(s => s.Key != "boost" || showBoost).Select(s =>
        {
            string before = now.Settings.FirstOrDefault(x => x.Key == s.Key) is { } cur ? Text(cur) : "";
            bool changed = previewing && before.Length > 0 && before != Text(s);
            return new SettingItem(s.Title, Text(s), SettingTip(s.Key), changed ? before + "  →" : "", changed);
        }).ToList();
    }

    private static string? SettingTip(string key) => key switch
    {
        "boost" => "Quanto o processador pode subir acima do clock base quando há carga.",
        "epp" => "Se o processador, ao escolher o clock, favorece desempenho ou economia de energia. É o que mais diferencia os planos de fábrica.",
        "display" => "Tempo sem uso até o Windows apagar a tela.",
        "sleep" => "Tempo sem uso até o PC suspender (desliga quase tudo e volta de onde parou).",
        _ => null,
    };

    /// <summary>Clicar em outro plano só o escolhe: a troca acontece em Aplicar. Clicar de novo no plano em uso (ou no escolhido) desfaz a escolha.</summary>
    private void PlanRow_Click(object sender, RoutedEventArgs e)
    {
        if (_applyingPlan || Plan == null || ((FrameworkElement)sender).Tag is not PlanItem item) return;
        _pendingPlan = item.IsActive || item.IsPending ? null : Plan.Plans.FirstOrDefault(x => x.Id == item.Id);
        ShowPlan(Plan);
    }

    private void PlanCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_applyingPlan || Plan == null) return;
        _pendingPlan = null;
        ShowPlan(Plan);
    }

    private async void PlanApply_Click(object sender, RoutedEventArgs e)
    {
        if (_applyingPlan || _pendingPlan is not { } plan || Plan == null) return;
        _applyingPlan = true;
        PlanApply.IsEnabled = false;
        try
        {
            uint error = await Task.Run(() => PowerPlans.SetActive(plan.Id));
            if (_context.IsClosing) return;
            _pendingPlan = null;
            Plan = await Task.Run(PowerPlans.Read);
            if (_context.IsClosing) return;
            ShowPlan(Plan);
            EnergyStatus.Text = error == 0 ? $"Plano {plan.Name} ativado às {DateTime.Now:HH:mm:ss}" : $"O Windows recusou a troca de plano (erro {error}).";
        }
        finally { _applyingPlan = false; }
    }

    // ---------------- ligado nos últimos 7 dias ----------------

    private void ShowHistory(PowerHistory h)
    {
        bool failed = h.Error != null || h.LogStart == null;
        Uptime.Visibility = failed ? Visibility.Collapsed : Visibility.Visible;
        DaysEmpty.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        DaysEmpty.Text = h.Error != null ? "Não foi possível ler o registro de eventos do Windows: " + h.Error : "O registro de eventos do Windows está vazio.";
        if (failed) { DaysToday.Text = DaysAverage.Text = DaysCrashes.Text = "—"; return; }

        var days = new List<UptimeChart.Day>();
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        for (int i = 0; i < 7; i++)
        {
            DateTime day = h.Since.AddDays(i), next = day.AddDays(1);
            bool covered = h.Covers(day) || (i == 6 && h.LogStart <= DateTime.Now);
            var spans = h.Spans.Where(s => s.To > day && s.From < next)
                .Select(s => ((s.From < day ? day : s.From).Subtract(day).TotalHours, (s.To > next ? next : s.To).Subtract(day).TotalHours, s.Current && s.To <= next))
                .ToList();
            var crashes = h.Unexpected.Where(c => c >= day && c < next).Select(c => c.Subtract(day).TotalHours).ToList();
            var detail = h.Spans.Where(s => s.To > day && s.From < next)
                .Select(s => $"{(s.From < day ? "00:00" : s.From.ToString("HH:mm"))} – {(s.Current && s.To <= next ? "agora" : s.To >= next ? "24:00" : s.To.ToString("HH:mm"))}")
                .Concat(h.Unexpected.Where(c => c >= day && c < next).Select(c => $"{c:HH:mm} desligamento inesperado"))
                .ToList();
            string label = $"{culture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek).TrimEnd('.')} {day.Day}";
            days.Add(new UptimeChart.Day(label, i == 6, covered, spans, crashes, covered ? Hours(h.OnTime(day)) : "", detail));
        }
        days.Reverse(); // hoje no topo, depois do mais recente para o mais antigo
        Uptime.Days = days;

        Readout(DaysToday, h.OnTime(DateTime.Today));
        var past = Enumerable.Range(0, 6).Select(i => h.Since.AddDays(i)).Where(h.Covers).ToList();
        if (past.Count > 0) Readout(DaysAverage, TimeSpan.FromTicks((long)past.Average(d => h.OnTime(d).Ticks)));
        else DaysAverage.Text = "—";
        var known = Enumerable.Range(0, 7).Select(i => h.Since.AddDays(i)).Where(d => d.Date == DateTime.Today || h.Covers(d)).ToList();
        if (known.Count > 0) Readout(DaysWeek, TimeSpan.FromTicks(known.Sum(d => h.OnTime(d).Ticks)));
        else DaysWeek.Text = "—";
        var longest = h.Spans.Where(sp => sp.To > h.Since).Select(sp => sp.To - (sp.From < h.Since ? h.Since : sp.From)).DefaultIfEmpty(TimeSpan.Zero).Max();
        if (longest > TimeSpan.Zero) Readout(DaysLongest, longest);
        else DaysLongest.Text = "—";
        int turnOns = h.Spans.Count(sp => sp.To > h.Since);
        DaysTurnOns.Text = turnOns.ToString();
        int crashCount = h.Unexpected.Count;
        DaysCrashes.Text = crashCount.ToString();
        DaysCrashes.SetResourceReference(TextBlock.ForegroundProperty, crashCount >= 3 ? "Danger" : "Warn");
        CrashBox.Visibility = crashCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        DaysCells.Columns = crashCount > 0 ? 6 : 5;
        // Só avisa quando o registro não cobre a semana inteira (o Windows descarta eventos antigos quando o log enche)
        bool shortLog = h.LogStart is DateTime start && start > h.Since;
        DaysCaption.Text = shortLog ? $"O registro do Windows só guarda desde {h.LogStart:dd/MM HH:mm}" : "";
        DaysCaption.Visibility = shortLog ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- ao vivo (a cada segundo, mesmo com a página fechada) ----------------

    /// <summary>Guarda o consumo de processador e placa de vídeo e soma a energia da sessão; com a página aberta, atualiza as leituras.</summary>
    internal void Sample()
    {
        if (_monitor == null || !_monitor.IsOpen) return;
        DateTime now = DateTime.Now;
        float? cpu = _monitor.CpuPower, gpu = _gpuPower();
        _trace.Add((cpu, gpu));
        if (_trace.Count > PowerTrace.Capacity) _trace.RemoveAt(0);
        if (cpu != null || gpu != null)
        {
            double w = (cpu ?? 0) + (gpu ?? 0);
            _sessionStart ??= now;
            if (_lastSample is DateTime last) _energyWh += w * Math.Min(5, (now - last).TotalSeconds) / 3600;
            _lastSample = now;
            _sumW += w; _countW++;
            if (w > _peakW) { _peakW = w; _peakAt = now; }
        }
        else _lastSample = null;

        // Tomada ↔ bateria: a próxima visita (ou esta, se aberta) relê tudo
        var (ac, _, _) = PowerStatus.Line();
        if (Snapshot != null && ac != _lastAc) { _lastAc = ac; _dirty = true; }
        if (!IsVisible) return;
        if (_dirty && !_refreshing) _ = RefreshAsync();
        ShowLive();
    }

    private void ShowLive()
    {
        var (cpu, gpu) = _trace.Count > 0 ? _trace[^1] : (null, null);
        bool any = cpu != null || gpu != null;
        LiveDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, any ? "Accent" : "Faint");
        LiveText.Text = any ? "Ao vivo" : _monitor.IsOpen ? "Sem leitura de consumo" : "Aguardando os sensores";
        Readout(TotalNow, any ? $"{(cpu ?? 0) + (gpu ?? 0):0}" : "—", any ? "W" : "");
        TotalCaption.Text = cpu != null && gpu != null ? "processador + placa de vídeo"
            : gpu != null ? "só a placa de vídeo"
            : cpu != null ? "só o processador"
            : "sem leitura de consumo";
        Split.First = cpu ?? 0;
        Split.Second = gpu ?? 0;
        Readout(CpuNow, cpu is float c ? $"{c:0.0}" : "—", cpu != null ? "W" : "");
        Readout(GpuNow, gpu is float g ? $"{g:0.0}" : "—", gpu != null ? "W" : "");
        ShowCpuProblem(cpu);
        GpuCaption.Visibility = gpu == null && _mainGpu() != null ? Visibility.Visible : Visibility.Collapsed;
        GpuCaption.Text = "sem leitura do sensor de consumo";
        Trace.EmptyText = _monitor.IsOpen ? "Nenhum sensor de consumo do processador ou da placa de vídeo respondeu." : "Coletando as primeiras amostras…";
        if (any || _trace.Count > 0) Trace.Samples = _trace.ToList();
        ShowSession();
        ShowVoltages();
    }

    /// <summary>Sem leitura do processador: diz por quê (administrador, PawnIO) em vez de deixar só o traço.</summary>
    private void ShowCpuProblem(float? cpu)
    {
        CpuCaption.Visibility = cpu == null && _monitor.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        if (cpu != null || !_monitor.IsOpen) return;
        var problem = _monitor.DiagnoseCpuSensors();
        CpuCaption.Text = problem switch
        {
            SensorMonitor.CpuSensorProblem.NotElevated => "sem leitura: abra como administrador",
            SensorMonitor.CpuSensorProblem.PawnIOMissing => "sem leitura: falta o driver PawnIO",
            _ => "sem leitura do sensor de consumo",
        };
        CpuCaption.ToolTip = SensorMonitor.Explain(problem) is { Length: > 0 } why ? why : null;
    }

    private void ShowSession()
    {
        if (_sessionStart is not DateTime start || _countW == 0)
        {
            SessionEnergy.Text = SessionAverage.Text = SessionPeak.Text = "—";
            AverageCell.ToolTip = PeakCell.ToolTip = EnergyCell.ToolTip = null;
            SessionAge.Text = "";
            return;
        }
        // Sessão: desde que o CoreBi abriu; tudo é a soma processador + placa de vídeo, como a leitura grande
        var length = DateTime.Now - start;
        string span = length.TotalMinutes < 1 ? "menos de 1 min" : Hours(length, minutesWord: true);
        SessionAge.Text = "sessão de " + span;
        SessionAge.ToolTip = $"Desde que o CoreBi abriu, às {start:HH:mm}. Média, pico e energia valem para este período; o gráfico mostra só os últimos 5 minutos.";
        Readout(SessionAverage, $"{_sumW / _countW:0}", "W");
        AverageCell.ToolTip = $"Consumo médio de processador + placa de vídeo desde {start:HH:mm} ({span}).";
        Readout(SessionPeak, $"{_peakW:0}", "W");
        PeakCell.ToolTip = _peakAt is DateTime at
            ? $"A maior soma de processador + placa de vídeo num mesmo segundo, {(at.Date == DateTime.Today ? "" : $"em {at:dd/MM} ")}às {at:HH:mm:ss}."
            : null;
        if (_energyWh >= 1000) Readout(SessionEnergy, $"{_energyWh / 1000:0.00}", "kWh");
        else Readout(SessionEnergy, $"{_energyWh:0}", "Wh");
        EnergyCell.ToolTip = $"Quanto processador e placa de vídeo gastaram desde {start:HH:mm}. 1000 Wh = 1 kWh, a unidade da conta de luz.";
    }

    private void ShowVoltages()
    {
        var map = VoltageMapper.Map(_monitor.Rows.Where(r => r.Type == SensorType.Voltage)
            .Select(r => new VoltageInput(r.HardwareType, r.Name, r.RawValue, r.RawMin, r.RawMax)));

        if (_rails.Count != map.Rails.Count || _rails.Zip(map.Rails).Any(p => p.First.Title != p.Second.Title))
        {
            _rails.Clear();
            _rails.AddRange(map.Rails.Select((r, i) => new RailItem(r.Title, r.Nominal, i == 0)));
            RailRows.Items.Refresh();
        }
        for (int i = 0; i < _rails.Count; i++)
        {
            var (item, r) = (_rails[i], map.Rails[i]);
            item.Value = r.Value;
            item.Min = r.Min ?? double.NaN;
            item.Max = r.Max ?? double.NaN;
            item.ValueText = $"{r.Value:0.000}";
            double dev = r.Deviation * 100;
            item.DeviationText = $"{(dev >= 0 ? "+" : "−")}{Math.Abs(dev):0.0} %";
            item.Off = !r.InTolerance;
        }

        if (_volts.Count != map.Components.Count || _volts.Zip(map.Components).Any(p => p.First.Title != p.Second.Title))
        {
            _volts.Clear();
            _volts.AddRange(map.Components.Select(c => new VoltItem(c.Title, c.ScaleLo, c.ScaleHi)));
            VoltRows.Items.Refresh();
        }
        for (int i = 0; i < _volts.Count; i++)
        {
            var (item, c) = (_volts[i], map.Components[i]);
            item.Value = c.Value;
            item.Min = c.Min ?? double.NaN;
            item.Max = c.Max ?? double.NaN;
            item.ValueText = $"{c.Value:0.000}";
            item.Low = c.Low;
        }

        bool rails = _rails.Count > 0, comps = _volts.Count > 0;
        RailRows.Visibility = rails ? Visibility.Visible : Visibility.Collapsed;
        VoltDivider.Visibility = rails && comps ? Visibility.Visible : Visibility.Collapsed;
        VoltComponentsTitle.Visibility = rails && comps ? Visibility.Visible : Visibility.Collapsed;
        VoltRows.Visibility = comps ? Visibility.Visible : Visibility.Collapsed;
        VoltRows.Margin = new Thickness(0, rails ? 0 : 12, 0, 0);
        VoltEmpty.Visibility = rails || comps ? Visibility.Collapsed : Visibility.Visible;
        VoltEmpty.Text = !_monitor.IsOpen ? "Aguardando os sensores…"
            : _monitor.DiagnoseCpuSensors() is var p && p != SensorMonitor.CpuSensorProblem.None ? "Os sensores da placa-mãe e do processador não responderam. " + SensorMonitor.Explain(p)
            : "A placa-mãe não expõe as tensões para a biblioteca de sensores.";
        VoltAll.Visibility = map.Total > 0 ? Visibility.Visible : Visibility.Collapsed;
        VoltAllText.Text = map.Total == 1 ? "A tensão em Sensores" : $"Todas as {map.Total} tensões em Sensores";
    }

    private void VoltAll_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main) main.NavSensors.IsChecked = true;
    }

    // ---------------- layout ----------------

    /// <summary>
    /// Larga: alimentação e consumo em cima, na mesma altura; tensões e histórico embaixo, o histórico esticado até o fim da página.
    /// Estreita: um painel por linha (consumo, alimentação, tensões, histórico).
    /// </summary>
    private void EnergyBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        double width = e.NewSize.Width;
        bool stacked = width < 1000;
        if (stacked != _stacked || e.PreviousSize.Width == 0)
        {
            _stacked = stacked;
            var rows = EnergyBody.RowDefinitions;
            var cards = stacked ? new FrameworkElement[] { PowerCard, FeedCard, VoltCard, DaysCard } : new FrameworkElement[] { FeedCard, PowerCard, VoltCard, DaysCard };
            // Larga: (0,0) (0,2) (2,0) (2,2). Estreita: uma coluna, linhas 0, 2, 4, 6.
            for (int k = 0; k < cards.Length; k++)
            {
                Grid.SetRow(cards[k], stacked ? k * 2 : k / 2 * 2);
                Grid.SetColumn(cards[k], stacked ? 0 : k % 2 * 2);
                Grid.SetColumnSpan(cards[k], stacked ? 3 : 1);
            }
            for (int r = 0; r < rows.Count; r++)
                rows[r].Height = stacked ? (r % 2 == 1 ? new GridLength(16) : GridLength.Auto)
                    : r == 0 ? GridLength.Auto : r == 1 ? new GridLength(16) : r == 2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }
        FeedColumn.Width = stacked ? new GridLength(0) : new GridLength(width < 1360 ? 372 : 420);
        FeedGap.Width = new GridLength(stacked ? 0 : 16);

        double right = stacked ? width : width - FeedColumn.Width.Value - 16;
        ReadoutColumn.Width = new GridLength(right < 760 ? 270 : 320);
        TotalNow.FontSize = right < 760 ? 72 : 88;
    }

    // ---------------- formatação ----------------

    /// <summary>Leitura com a unidade menor e em tom secundário ao lado (como nos mostradores da Visão geral).</summary>
    private static void Readout(TextBlock target, string value, string unit)
    {
        target.Inlines.Clear();
        target.Inlines.Add(new Run(value));
        if (unit.Length == 0) return;
        var u = new Run(" " + unit) { FontSize = Math.Max(12, Math.Round(target.FontSize * 0.3)) };
        u.SetResourceReference(TextElement.ForegroundProperty, "Muted");
        u.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        target.Inlines.Add(u);
    }

    /// <summary>"4 h 33 min" com as unidades menores.</summary>
    private static void Readout(TextBlock target, TimeSpan span)
    {
        target.Inlines.Clear();
        int h = (int)span.TotalHours, m = span.Minutes;
        void Part(string value, string unit)
        {
            target.Inlines.Add(new Run(value));
            var u = new Run(" " + unit + " ") { FontSize = 12.5 };
            u.SetResourceReference(TextElement.ForegroundProperty, "Muted");
            u.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
            target.Inlines.Add(u);
        }
        if (h > 0) Part($"{h}", "h");
        Part($"{m}", "min");
    }

    private static string Hours(TimeSpan t, bool minutesWord = false)
    {
        int h = (int)t.TotalHours, m = t.Minutes;
        return h == 0 ? $"{m} min" : minutesWord ? $"{h} h {m:00} min" : $"{h} h {m:00}";
    }

    private static string Duration(int minutes) => minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min";

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
