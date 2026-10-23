using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Storage;
using LibreHardwareMonitor.Hardware;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace CoreBi.Desktop.Views;

/// <summary>
/// Discos: rack com uma baia por disco físico e, para o disco escolhido, placa de identificação, vida útil, temperatura
/// ao vivo com histórico, integridade (S.M.A.R.T.) e odômetro. A leitura completa vem do Windows ao abrir a página;
/// temperatura e atividade seguem os sensores a cada segundo.
/// </summary>
public partial class DisksView : UserControl
{
    /// <summary>Baia do rack: o cartão lido e a temperatura ao vivo (quando o disco tem sensor).</summary>
    public sealed class DiskBay : INotifyPropertyChanged
    {
        public DiskBay(DiskHealthCard card)
        {
            Card = card;
            var d = card.Disk;
            Meta = d == null ? card.Meta
                : string.Join(" · ", new[] { d.Kind, d.Bus, FmtSize(d.SizeBytes), string.Join(" ", d.Letters) }.Where(s => s.Length > 0));
            if (d?.Wear is double wear)
            {
                double rem = Math.Clamp(100 - wear, 0, 100);
                LifeFraction = rem / 100;
                LifeText = $"{rem:0} % vida";
                LifeState = wear >= DiskDetail.WearCritAt ? "Danger" : wear >= DiskDetail.WearWarnAt ? "Warn" : "Ok";
            }
            else LifeText = d == null ? "" : d.IsSsd ? "sem dado de desgaste" : "HDD, sem desgaste";
            _temp = d?.Temperature;
        }

        public DiskHealthCard Card { get; }
        public DiskDetail? Disk => Card.Disk;
        public string Title => Card.Title;
        public string Meta { get; }
        public string VerdictText => Sentence(Card.VerdictText);
        public string State => Card.Verdict switch { DiskVerdict.Good => "Ok", DiskVerdict.Warning => "Warn", DiskVerdict.Critical => "Danger", _ => "" };
        public bool HasLife => Disk?.Wear != null;
        public double LifeFraction { get; }
        public string LifeText { get; } = "";
        public string LifeState { get; } = "Ok";

        private double? _temp;
        /// <summary>Temperatura atual: a do sensor quando há, senão a lida pelo Windows.</summary>
        public double? Temperature
        {
            get => _temp;
            set
            {
                if (_temp == value) return;
                _temp = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TempText)));
            }
        }
        public string TempText => _temp is double t ? $"{t:0} °C" : "— °C";
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed record DiskCheck(string Title, string Detail, string Value, string State);
    public sealed record VolumeRow(string Letter, string Label, string Detail, bool IsRest);

    /// <summary>Últimos 5 min de um disco no LibreHardwareMonitor: temperatura (1/s) e taxas atuais.</summary>
    private sealed class DiskTrace
    {
        public readonly List<float?> Temps = new();
        public float? Read, Write;
        public float? Last => Temps.Count > 0 ? Temps[^1] : null;
    }

    private FeatureContext _context = null!;
    private SensorMonitor _monitor = null!;
    private bool _refreshing;
    private List<DiskBay> _bays = new();
    private List<DriveCard> _drives = new();
    private readonly Dictionary<string, DiskTrace> _traces = new(StringComparer.Ordinal);
    private DateTime _readAt;
    private bool _wide = true;

    public DisksView() => InitializeComponent();

    internal void Initialize(FeatureContext context, SensorMonitor monitor)
    {
        _context = context;
        _monitor = monitor;
    }

    private async void BtnDiskRefresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    internal async Task RefreshAsync()
    {
        if (_refreshing || _context.IsClosing) return;
        _refreshing = true;
        BtnDiskRefresh.IsEnabled = false;
        DiskHealthStatus.Text = "Lendo os discos…";
        try
        {
            var sensors = _monitor.Snapshot();
            var cardsTask = Task.Run(() => DiskHealth.Read(sensors));
            var drivesTask = Task.Run(DriveMap.Read);
            var cards = await cardsTask;
            List<DriveCard> drives;
            try { drives = await drivesTask; } catch { drives = new(); }
            if (_context.IsClosing) return;
            _readAt = DateTime.Now;
            _drives = drives;
            int keep = (DiskCards.SelectedItem as DiskBay)?.Card.Index ?? -1;
            _bays = cards.Select(c => new DiskBay(c)).ToList();
            DiskCards.ItemsSource = _bays;
            DiskCards.SelectedItem = _bays.FirstOrDefault(b => b.Card.Index == keep) ?? _bays.FirstOrDefault();
            RackCount.Text = _bays.Count.ToString();
            ShowSummary();

            int bad = cards.Count(c => c.Verdict is DiskVerdict.Warning or DiskVerdict.Critical);
            int unknown = cards.Count(c => c.Verdict == DiskVerdict.Unknown);
            string summary = bad > 0 ? $"{bad} precisa(m) de atenção"
                : unknown > 0 ? $"{unknown} sem dados suficientes" : "nenhum problema encontrado";
            DiskHealthStatus.Text = cards.Count == 0 ? "Nenhum disco encontrado."
                : $"{cards.Count} disco(s) · {summary} · lido às {_readAt:HH:mm:ss}";
            DiskPanel.Visibility = cards.Count == 0 ? Visibility.Hidden : Visibility.Visible;
        }
        catch (Exception ex) { if (!_context.IsClosing) DiskHealthStatus.Text = "Falhou: " + ex.Message; }
        finally
        {
            _refreshing = false;
            if (!_context.IsClosing) BtnDiskRefresh.IsEnabled = true;
        }
    }

    // ---------------- ao vivo (a cada segundo, mesmo com a página fechada) ----------------

    /// <summary>Guarda temperatura e taxas de cada disco do LibreHardwareMonitor; com a página aberta, atualiza as leituras.</summary>
    internal void Sample()
    {
        if (_monitor == null || !_monitor.IsOpen) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in _monitor.Rows)
        {
            if (row.HardwareType != HardwareType.Storage) continue;
            if (!_traces.TryGetValue(row.Hardware, out var trace)) _traces[row.Hardware] = trace = new DiskTrace();
            if (seen.Add(row.Hardware))
            {
                trace.Temps.Add(null);
                if (trace.Temps.Count > TraceChart.Capacity) trace.Temps.RemoveAt(0);
                trace.Read = trace.Write = null;
            }
            if (row.Type == SensorType.Temperature && trace.Temps[^1] == null && row.RawValue is > 0) trace.Temps[^1] = row.RawValue;
            else if (row.Type == SensorType.Throughput && row.Name.Contains("Read", StringComparison.OrdinalIgnoreCase)) trace.Read = row.RawValue;
            else if (row.Type == SensorType.Throughput && row.Name.Contains("Write", StringComparison.OrdinalIgnoreCase)) trace.Write = row.RawValue;
        }
        if (IsVisible) ShowLive();
    }

    private DiskTrace? TraceOf(DiskBay bay) =>
        bay.Disk?.SensorHardware is string hw && _traces.TryGetValue(hw, out var t) ? t : null;

    private void ShowLive()
    {
        foreach (var bay in _bays)
            if (TraceOf(bay)?.Last is float t) bay.Temperature = t;

        var hottest = _bays.Where(b => b.Temperature != null).OrderByDescending(b => b.Temperature).FirstOrDefault();
        SumHottest.Text = hottest == null ? "—" : $"{ShortName(hottest.Title)} · {hottest.Temperature:0} °C";

        if (DiskCards.SelectedItem is not DiskBay sel || sel.Disk is not DiskDetail d) return;
        var trace = TraceOf(sel);
        bool live = trace?.Last != null;
        double? now = sel.Temperature;
        TempNow.Text = now is double v ? $"{v:0}" : "—";
        TempScale.Value = now ?? double.NaN;
        ShowTempState(now, d);
        TempLiveText.Text = live ? "Ao vivo" : $"Leitura do Windows às {_readAt:HH:mm}";
        TempLiveDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, live ? "Accent" : "Faint");
        TempTrace.Samples = trace?.Temps.ToList() ?? new List<float?>();
        DiskActivity.Text = trace?.Read == null && trace?.Write == null ? "sem sensor de atividade para este disco"
            : $"Leitura {Rate(trace!.Read)}     Escrita {Rate(trace.Write)}";
    }

    // ---------------- disco escolhido ----------------

    private void DiskCards_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiskCards.SelectedItem is DiskBay bay) ShowDisk(bay);
    }

    private void ShowDisk(DiskBay bay)
    {
        var card = bay.Card;
        var d = card.Disk;
        DiskModel.Text = card.Title;
        DiskBadgeText.Text = bay.VerdictText;
        string tone = bay.State;
        string soft = tone switch { "Ok" => "OkSoft", "Warn" => "WarnSoft", "Danger" => "DangerSoft", _ => "Raised" };
        DiskBadge.SetResourceReference(Border.BackgroundProperty, soft);
        DiskBadge.SetResourceReference(Border.BorderBrushProperty, tone.Length > 0 ? tone : "LineStrong");
        DiskBadgeText.SetResourceReference(TextBlock.ForegroundProperty, tone.Length > 0 ? tone : "Muted");
        DiskReasons.Text = card.Reasons;
        DiskReasonIcon.Text = card.Verdict switch { DiskVerdict.Good => "", DiskVerdict.Unknown => "", _ => "" };
        DiskReasonIcon.SetResourceReference(TextBlock.ForegroundProperty, tone.Length > 0 ? tone : "Faint");

        // Erro de leitura: só a placa com o motivo
        var instruments = d == null ? Visibility.Collapsed : Visibility.Visible;
        foreach (var el in new UIElement[] { LifeCard, TempCard, CheckCard, OdoCard, DiskSpace, SpaceDivider }) el.Visibility = instruments;
        if (d == null) { DiskSpecs.ItemsSource = null; return; }

        var specs = new List<KeyValuePair<string, string>>();
        void Spec(string key, string value) { if (value.Length > 0) specs.Add(new(key, value)); }
        Spec("", d.Kind);
        Spec("", d.Bus);
        Spec("", FmtSize(d.SizeBytes));
        Spec("Unidades ", string.Join(" ", d.Letters));
        Spec("Série ", d.Serial);
        Spec("Firmware ", d.Firmware);
        DiskSpecs.ItemsSource = specs;

        ShowSpace(d);
        ShowLife(d);
        ShowTemperature(bay, d);
        ShowChecks(d);
        ShowOdometer(d);
        ShowLive();
    }

    private void ShowSpace(DiskDetail d)
    {
        var mine = _drives.Where(v => d.Letters.Contains(v.Letter, StringComparer.OrdinalIgnoreCase)).ToList();
        double rest = Math.Max(0, d.SizeBytes - mine.Sum(v => (double)v.Total));
        if (mine.Count == 0 || rest < d.SizeBytes * 0.02) rest = mine.Count == 0 ? d.SizeBytes : 0;
        DiskCapacity.SetVolumes(mine.Select(v => ((double)v.Total, (double)(v.Total - v.Free))).ToList(), rest);
        var rows = mine.Select(v => new VolumeRow(v.Letter, string.IsNullOrWhiteSpace(v.Label) ? v.FileSystem : v.Label,
            $"{DriveCard.Fmt(v.Free)} livres · {v.UsedFraction * 100:0} %", false)).ToList();
        if (rest > 0) rows.Add(new VolumeRow("", mine.Count == 0 ? "Sem unidade com letra neste disco" : "Sem letra (sistema, recuperação, não alocado)",
            DriveCard.Fmt((long)rest), true));
        DiskVolumes.ItemsSource = rows;
        long used = mine.Sum(v => v.Total - v.Free), total = mine.Sum(v => v.Total);
        DiskSpaceSummary.Text = total > 0 ? $"{DriveCard.Fmt(used)} usados de {DriveCard.Fmt(total)}" : "";
    }

    private void ShowLife(DiskDetail d)
    {
        LifeDial.Value = d.Wear is double w ? Math.Clamp(100 - w, 0, 100) : double.NaN;
        LifeDial.Caption = d.Wear != null ? "vida útil restante" : "não informada";
        LifeCaption.Text = d.Wear != null ? "Desgaste informado pelo próprio SSD"
            : d.IsSsd ? "Este SSD não informa o desgaste ao Windows" : "HDDs não informam desgaste; veja erros e horas ligado";

        Readout(LifeWear, d.Wear is double wear ? $"{wear:0}" : "—", d.Wear != null ? "%" : "");
        LifeWearSub.Text = d.Wear != null ? $"atenção a partir de {DiskDetail.WearWarnAt:0} %" : "não informado";
        Readout(LifeSpare, d.AvailableSpare is double sp ? $"{sp:0}" : "—", d.AvailableSpare != null ? "%" : "");
        LifeSpareSub.Text = d.SpareThreshold is double min && min > 0 ? $"mínimo {min:0} %" : d.AvailableSpare != null ? "blocos de reserva" : "não informada";

        // Resistência: quanto o SSD aguenta gravar até 100 % de desgaste, no ritmo medido até aqui (estimativa linear).
        if (d.Wear is double ww && ww >= 1 && d.WrittenGb is double gb)
        {
            var (value, unit) = SplitData(gb * 100 / ww);
            Readout(LifeEndurance, "≈ " + value, unit);
            LifeEnduranceSub.Text = "gravados até 100 %";
            LifeEnduranceBox.ToolTip = $"Estimativa linear: {SplitData(gb).Value} {SplitData(gb).Unit} gravados para {ww:0} % de desgaste. " +
                                       "O fabricante informa a resistência real (TBW) na garantia.";
        }
        else
        {
            Readout(LifeEndurance, "—", "");
            LifeEnduranceSub.Text = d.Wear is 0 ? "desgaste ainda em 0 %" : "não informada";
            LifeEnduranceBox.ToolTip = null;
        }
    }

    private void ShowTemperature(DiskBay bay, DiskDetail d)
    {
        string kind = d.IsSsd ? (d.Bus.Length > 0 ? $"SSD {d.Bus}" : "SSD") : d.Kind == "HDD" ? "HDD" : "este disco";
        TempCaption.Text = $"Faixa ideal até {d.TempWarnAt:0} °C para {kind}";
        TempScale.WarnAt = d.TempWarnAt;
        TempScale.CritAt = d.TempCritAt;
        TempScale.Peak = d.TemperatureMax ?? double.NaN;
        TempTrace.WarnAt = d.TempWarnAt;
        TempTrace.EmptyText = d.SensorHardware == null
            ? "Sem sensor ao vivo para este disco: a temperatura acima é a leitura do Windows."
            : "Coletando as primeiras amostras…";
        TempPeak.Text = d.TemperatureMax is double max ? $"{max:0} °C" : "—";
        TempLimits.Text = $"{d.TempWarnAt:0} · {d.TempCritAt:0} °C";
    }

    private void ShowTempState(double? t, DiskDetail d)
    {
        (string text, string key) = t switch
        {
            null => ("Sem leitura", "Faint"),
            double v when v >= d.TempCritAt => ("Crítica", "Danger"),
            double v when v >= d.TempWarnAt => ("Alta", "Warn"),
            _ => ("Na faixa ideal", "Ok"),
        };
        TempState.Text = text;
        TempState.SetResourceReference(TextBlock.ForegroundProperty, key);
    }

    private void ShowChecks(DiskDetail d)
    {
        static string Count(long? n) => n is long v ? $"{v:N0}" : "—";
        long? uncorrected = d.ReadUncorrected == null && d.WriteUncorrected == null ? null : (d.ReadUncorrected ?? 0) + (d.WriteUncorrected ?? 0);
        DiskChecks.ItemsSource = new List<DiskCheck>
        {
            new("Falha prevista (S.M.A.R.T.)", "Aviso do próprio disco de que vai falhar",
                d.PredictFailure switch { true => "Sim", false => "Não", _ => "—" },
                d.PredictFailure switch { true => "Danger", false => "Ok", _ => "" }),
            new("Saúde segundo o Windows", "Classificação da API de armazenamento",
                d.WindowsHealth switch { 0 => "Saudável", 1 => "Atenção", 2 => "Não saudável", _ => "—" },
                d.WindowsHealth switch { 0 => "Ok", 1 => "Warn", 2 => "Danger", _ => "" }),
            new("Erros de leitura", "Total, inclusive os corrigidos pelo disco", Count(d.ReadErrors), d.ReadErrors is 0 ? "Ok" : ""),
            new("Erros de escrita", "Total, inclusive os corrigidos pelo disco", Count(d.WriteErrors), d.WriteErrors is 0 ? "Ok" : ""),
            new("Erros não corrigidos", "Leituras e gravações que falharam de vez", Count(uncorrected),
                uncorrected switch { 0 => "Ok", > 0 => "Warn", _ => "" }),
        };
    }

    private void ShowOdometer(DiskDetail d)
    {
        Readout(OdoHours, d.PowerOnHours is long h ? $"{h:N0}" : "—", d.PowerOnHours != null ? "h" : "");
        OdoHoursSub.Text = d.PowerOnHours is long hh ? $"≈ {hh / 24.0:N0} dias ligado" : "não informado";
        Readout(OdoCycles, d.StartStopCycles is long c ? $"{c:N0}" : "—", "");
        OdoCyclesSub.Text = d.StartStopCycles is long cc && cc > 0 && d.PowerOnHours is long ch ? $"≈ {(double)ch / cc:0.#} h por ciclo"
            : d.StartStopCycles != null ? "desde a fábrica" : "não informado";
        var written = d.WrittenGb is double wg ? SplitData(wg) : ("—", "");
        Readout(OdoWritten, written.Item1, written.Item2);
        OdoWrittenSub.Text = d.WrittenGb is double wgb && d.PowerOnHours is long wh && wh > 0 ? $"≈ {wgb / wh:0.#} GB por hora ligado"
            : d.WrittenGb != null ? "desde a fábrica" : "não informado";
        var read = d.ReadGb is double rg ? SplitData(rg) : ("—", "");
        Readout(OdoRead, read.Item1, read.Item2);
        OdoReadSub.Text = d.ReadGb is double rgb && d.WrittenGb is double wgb2 && wgb2 > 0 ? $"{rgb / wgb2:0.#}× o que foi gravado"
            : d.ReadGb != null ? "desde a fábrica" : "não informado";
    }

    private void ShowSummary()
    {
        var disks = _bays.Select(b => b.Disk).OfType<DiskDetail>().ToList();
        RackSummary.Visibility = disks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        int ssd = disks.Count(d => d.IsSsd), hdd = disks.Count(d => d.Kind == "HDD");
        string mix = string.Join(", ", new[] { ssd > 0 ? $"{ssd} SSD" : "", hdd > 0 ? $"{hdd} HDD" : "" }.Where(s => s.Length > 0));
        SumCapacity.Text = FmtSize(disks.Sum(d => d.SizeBytes)) + (mix.Length > 0 ? $" · {mix}" : "");
        var letters = disks.SelectMany(d => d.Letters).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mounted = _drives.Where(v => letters.Contains(v.Letter)).ToList();
        SumUsed.Text = mounted.Count == 0 ? "—"
            : $"{DriveCard.Fmt(mounted.Sum(v => v.Total - v.Free))} de {DriveCard.Fmt(mounted.Sum(v => v.Total))}";
        SumHottest.Text = "—";
    }

    // ---------------- layout ----------------

    /// <summary>Larga: integridade ao lado da temperatura. Estreita: integridade desce para o lado do odômetro (2 × 2).</summary>
    private void DiskBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        bool wide = e.NewSize.Width >= 1320;
        if (wide == _wide && e.PreviousSize.Width > 0) return;
        _wide = wide;
        RackColumn.Width = new GridLength(wide ? 300 : 260);
        LifeColumn.Width = new GridLength(wide ? 370 : 330);
        CheckGap.Width = new GridLength(wide ? 16 : 0);
        CheckColumn.Width = new GridLength(wide ? 330 : 0);
        SpaceColumn.Width = new GridLength(wide ? 420 : 330);
        Grid.SetRow(CheckCard, wide ? 2 : 4);
        Grid.SetColumn(CheckCard, wide ? 4 : 0);
        Grid.SetColumn(OdoCard, wide ? 0 : 2);
        Grid.SetColumnSpan(OdoCard, wide ? 5 : 1);
        OdoGrid.Columns = wide ? 4 : 2;
        OdoCell2.BorderThickness = new Thickness(wide ? 1 : 0, 0, 0, 0);
        OdoCell2.Margin = OdoCell3.Margin = new Thickness(0, wide ? 0 : 18, 0, 0);
        LifeDial.MinHeight = wide ? 210 : 190;
    }

    // ---------------- formatação ----------------

    /// <summary>Leitura com a unidade menor e em tom secundário ao lado (como nos mostradores da Visão geral).</summary>
    private static void Readout(TextBlock target, string value, string unit)
    {
        target.Inlines.Clear();
        target.Inlines.Add(new Run(value));
        if (unit.Length == 0) return;
        var u = new Run(" " + unit) { FontSize = Math.Max(12, Math.Round(target.FontSize * 0.4)) };
        u.SetResourceReference(TextElement.ForegroundProperty, "Muted");
        u.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        target.Inlines.Add(u);
    }

    /// <summary>GB (decimais, como o S.M.A.R.T. informa) → valor e unidade; TB a partir de 1000 GB.</summary>
    private static (string Value, string Unit) SplitData(double gb) =>
        gb >= 1000 ? ($"{gb / 1000:N1}", "TB") : ($"{gb:N0}", "GB");

    private static string Rate(float? bytesPerSecond) => bytesPerSecond switch
    {
        null => "—",
        >= 1048576f * 1000 => $"{bytesPerSecond / 1073741824f:0.0} GB/s",
        >= 1048576f => $"{bytesPerSecond / 1048576f:0.0} MB/s",
        _ => $"{bytesPerSecond / 1024f:0} KB/s",
    };

    /// <summary>Tamanho anunciado pelo fabricante (base 1000), como na caixa do disco.</summary>
    private static string FmtSize(long bytes)
    {
        double v = bytes; string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0;
        while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
        return bytes > 0 ? $"{v:0.#} {u[i]}" : "";
    }

    private static string ShortName(string model)
    {
        var first = model.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? model;
        return first.Length > 14 ? first[..14] : first;
    }

    private static string Sentence(string text) => text.Length == 0 ? text : text[..1].ToUpper() + text[1..].ToLower();
}
