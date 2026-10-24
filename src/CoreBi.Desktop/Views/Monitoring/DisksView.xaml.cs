using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Storage;
using LibreHardwareMonitor.Hardware;
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
    public sealed class DiskBay
    {
        public DiskBay(DiskHealthCard card)
        {
            Card = card;
            var d = card.Disk;
            Meta = d == null ? card.Meta
                : string.Join(" · ", new[] { d.Kind, d.Bus, FmtSize(d.SizeBytes), string.Join(" ", d.Letters) }.Where(s => s.Length > 0));
            _temp = d?.Temperature;
        }

        public DiskHealthCard Card { get; }
        public DiskDetail? Disk => Card.Disk;
        public string Title => Card.Title;
        public string Meta { get; }
        public string VerdictText => Sentence(Card.VerdictText);
        public string State => Card.Verdict switch { DiskVerdict.Good => "Ok", DiskVerdict.Warning => "Warn", DiskVerdict.Critical => "Danger", _ => "" };
        private double? _temp;
        /// <summary>Temperatura atual: a do sensor quando há, senão a lida pelo Windows.</summary>
        public double? Temperature
        {
            get => _temp;
            set
            {
                if (_temp == value) return;
                _temp = value;
            }
        }
    }

    public sealed record DiskCheck(string Title, string Value, string State, string? Tip = null);
    /// <summary>Unidade no espaço por unidade: Percent é a fração livre em destaque; Line, "usados de total".</summary>
    public sealed record VolumeRow(string Letter, string Label, string Line, string Percent, string PercentLabel, bool IsRest);

    /// <summary>Últimos 5 min de um disco no LibreHardwareMonitor: temperatura (1/s) e taxas atuais.</summary>
    private sealed class DiskTrace
    {
        public readonly List<float?> Temps = new();
        public float? Read, Write;
        /// <summary>Maior temperatura vista desde que o CoreBi abriu (inclui o que já saiu da janela de 5 min do gráfico).</summary>
        public float? Peak;
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
            var cardsTask = Task.Run(() => DiskHealth.Read(sensors, _monitor.NvmeLogs()));
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
            if (row.Type == SensorType.Temperature && trace.Temps[^1] == null && row.RawValue is > 0)
            {
                trace.Temps[^1] = row.RawValue;
                trace.Peak = Math.Max(trace.Peak ?? 0, row.RawValue.Value);
            }
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
        double? peak = trace?.Peak ?? d.TemperatureMax;
        TempPeak.Text = peak is double pk ? $"{pk:0} °C" : "—";
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
        // Saudável: uma linha discreta no pé da placa, só quando o veredito vem de leituras reais. Atenção, crítico e "sem dados" explicam o motivo.
        bool healthy = card.Verdict == DiskVerdict.Good;
        DiskReasons.Text = healthy ? "Nenhum problema encontrado nas leituras disponíveis" : card.Reasons;
        DiskReasons.Visibility = DiskReasonIcon.Visibility = Visibility.Visible;
        DiskReasonIcon.Text = healthy ? "\uE73E" : card.Verdict == DiskVerdict.Unknown ? "\uE946" : "\uE7BA";
        DiskReasonIcon.SetResourceReference(TextBlock.ForegroundProperty, tone.Length > 0 ? tone : "Faint");
        DiskReasons.SetResourceReference(TextBlock.ForegroundProperty, healthy ? "Muted" : "Fg");

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

        long total = mine.Sum(v => v.Total), free = mine.Sum(v => v.Free), used = total - free;
        bool known = total > 0;
        void Gb(TextBlock target, long bytes, double? share = null)
        {
            Readout(target, known ? $"{bytes / 1073741824.0:N1}" : "—", known ? "GB" : "");
            if (share is not double pct || !known) return;
            // "316,3 GB / 33 %": a barra separa os dois e a porcentagem usa a mesma fonte do valor
            var slash = new Run("  /  ") { FontWeight = FontWeights.Light };
            slash.SetResourceReference(TextElement.ForegroundProperty, "Faint");
            target.Inlines.Add(slash);
            target.Inlines.Add(new Run($"{pct:0}"));
            var unit = new Run(" %") { FontSize = Math.Max(12, Math.Round(target.FontSize * 0.4)) };
            unit.SetResourceReference(TextElement.ForegroundProperty, "Muted");
            unit.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
            target.Inlines.Add(unit);
        }
        double freeShare = known ? (double)free / total * 100 : 0;
        Gb(SpaceTotal, total);
        Gb(SpaceUsed, used, 100 - freeShare);
        Gb(SpaceFree, free, freeShare);

        // Mais de uma unidade no disco: uma linha por letra com quanto ainda está livre
        DiskVolumes.ItemsSource = mine.Count > 1
            ? mine.Select(v => new VolumeRow(v.Letter, string.IsNullOrWhiteSpace(v.Label) ? v.FileSystem : v.Label,
                $"{DriveCard.Fmt(v.Free)} livres de {DriveCard.Fmt(v.Total)}", "", "", false)).ToList()
            : null;
    }

    private void ShowLife(DiskDetail d)
    {
        LifeDial.Value = d.Wear is double w ? Math.Clamp(100 - w, 0, 100) : double.NaN;
        LifeDial.Caption = d.Wear != null ? "vida útil restante" : "não informada";
        Readout(LifeWear, d.Wear is double wear ? $"{wear:0}" : "—", d.Wear != null ? "%" : "");
        Readout(LifeSpare, d.AvailableSpare is double sp ? $"{sp:0}" : "—", d.AvailableSpare != null ? "%" : "");

        // Resistência: quanto o SSD aguenta gravar até 100 % de desgaste, no ritmo medido até aqui (estimativa linear).
        if (d.Wear is double ww && ww >= 1 && d.WrittenGb is double gb)
        {
            var (value, unit) = SplitData(gb * 100 / ww, decimals: false);
            Readout(LifeEndurance, "≈ " + value, unit);
            LifeEnduranceBox.ToolTip = $"Estimativa linear: {SplitData(gb).Value} {SplitData(gb).Unit} gravados para {ww:0} % de desgaste. " +
                                       "O fabricante informa a resistência real (TBW) na garantia.";
        }
        else
        {
            Readout(LifeEndurance, "—", "");
            LifeEnduranceBox.ToolTip = null;
        }
    }

    private void ShowTemperature(DiskBay bay, DiskDetail d)
    {
        TempScale.WarnAt = d.TempWarnAt;
        TempScale.CritAt = d.TempCritAt;
        TempTrace.WarnAt = d.TempWarnAt;
        TempTrace.EmptyText = d.SensorHardware == null
            ? "Sem sensor ao vivo para este disco: a temperatura acima é a leitura do Windows."
            : "Coletando as primeiras amostras…";
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

    /// <summary>
    /// Cada linha só ganha o verde quando o disco ou o Windows realmente informou o valor. Sem leitura aparece
    /// NotReported em tom apagado: dizer "normal" sem ter verificado esconderia um disco que não reporta nada.
    /// </summary>
    private const string NotReported = "Windows não informa";
    private const string NotReportedTip = "O Windows não tem este dado para este disco. Isso não indica falha: só que não há leitura para conferir.";

    private void ShowChecks(DiskDetail d)
    {
        static DiskCheck Count(string title, long? n, string good = "Nenhum") =>
            n is long v ? new(title, v == 0 ? good : $"{v:N0}", v == 0 ? "Ok" : "Warn") : new(title, NotReported, "", NotReportedTip);
        var rows = new List<DiskCheck>
        {
            d.WindowsHealth switch
            {
                0 => new("Saúde segundo o Windows", "Saudável", "Ok"),
                1 => new("Saúde segundo o Windows", "Atenção", "Warn"),
                2 => new("Saúde segundo o Windows", "Não saudável", "Danger"),
                _ => new("Saúde segundo o Windows", NotReported, "", NotReportedTip),
            },
        };

        // Falha prevista: o aviso S.M.A.R.T. do Windows ou, em NVMe, o "aviso crítico" do próprio relatório do disco
        bool? failing = d.PredictFailure ?? (d.Nvme?.CriticalWarning is ulong cw ? cw != 0 : null);
        rows.Add(failing switch
        {
            true => new("Falha prevista (S.M.A.R.T.)", "Sim", "Danger"),
            false => new("Falha prevista (S.M.A.R.T.)", "Não", "Ok"),
            _ => new("Falha prevista (S.M.A.R.T.)", NotReported, "", NotReportedTip),
        });

        if (d.Nvme?.MediaErrors is ulong media)
        {
            // NVMe não separa leitura e escrita: um contador só, de erros de mídia e integridade
            rows.Add(Count("Erros de mídia", (long)media));
            if (d.Nvme.UnsafeShutdowns is ulong unsafeCount)
            {
                // Quedas de energia e resets forçados acontecem em qualquer PC: o que preocupa é a frequência (por hora ligado), não o total.
                double perHour = d.PowerOnHours is > 0 ? (double)unsafeCount / d.PowerOnHours.Value : 0;
                (string text, string state) = unsafeCount >= 200 && perHour > 0.1 ? ("alto", "Danger")
                    : unsafeCount >= 50 && perHour > 0.02 ? ("atenção", "Warn")
                    : ("ok", "Ok");
                rows.Add(new("Desligamentos inesperados", $"{unsafeCount:N0} · {text}", state,
                    "Quedas de energia ou desligamentos sem avisar o SSD, somados desde a fábrica. Avaliado pela frequência: ok é raro, atenção é frequente, alto é muito frequente."));
            }
        }
        else
        {
            long? uncorrected = d.ReadUncorrected == null && d.WriteUncorrected == null ? null : (d.ReadUncorrected ?? 0) + (d.WriteUncorrected ?? 0);
            rows.Add(Count("Erros de leitura", d.ReadErrors));
            rows.Add(Count("Erros de escrita", d.WriteErrors));
            rows.Add(Count("Erros não corrigidos", uncorrected));
        }
        DiskChecks.ItemsSource = rows;
    }

    /// <summary>Só mostra o que o disco informa: um disco que não conta horas ou ciclos fica com menos células, não com traços.</summary>
    private void ShowOdometer(DiskDetail d)
    {
        var cells = new List<Border>();
        void Cell(Border cell, TextBlock target, string? value, string unit)
        {
            if (value == null) return;
            Readout(target, value, unit);
            cells.Add(cell);
        }
        Cell(OdoCell0, OdoHours, d.PowerOnHours is long h ? $"{h:N0}" : null, "h");
        Cell(OdoCell1, OdoCycles, d.StartStopCycles is long c ? $"{c:N0}" : null, "");
        var written = d.WrittenGb is double wg ? SplitData(wg) : default;
        Cell(OdoCell2, OdoWritten, d.WrittenGb != null ? written.Value : null, written.Unit);
        var read = d.ReadGb is double rg ? SplitData(rg) : default;
        Cell(OdoCell3, OdoRead, d.ReadGb != null ? read.Value : null, read.Unit);
        _odoCells = cells;
        LayoutOdometer();
    }

    private List<Border> _odoCells = new();

    private void LayoutOdometer()
    {
        OdoCard.Visibility = _odoCells.Count == 0 || DiskCards.SelectedItem is not DiskBay { Disk: not null } ? Visibility.Collapsed : Visibility.Visible;
        OdoGrid.Children.Clear();
        int columns = Math.Max(1, Math.Min(_odoCells.Count, _wide ? 4 : 2));
        OdoGrid.Columns = columns;
        for (int i = 0; i < _odoCells.Count; i++)
        {
            _odoCells[i].BorderThickness = new Thickness(i % columns == 0 ? 0 : 1, 0, 0, 0);
            _odoCells[i].Margin = new Thickness(0, i >= columns ? 18 : 0, 0, 0);
            OdoGrid.Children.Add(_odoCells[i]);
        }
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
        SpaceColumn.Width = new GridLength(wide ? 520 : 420);
        Grid.SetRow(CheckCard, wide ? 2 : 4);
        Grid.SetColumn(CheckCard, wide ? 4 : 0);
        Grid.SetColumn(OdoCard, wide ? 0 : 2);
        Grid.SetColumnSpan(OdoCard, wide ? 5 : 1);
        LayoutOdometer();
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
    private static (string Value, string Unit) SplitData(double gb, bool decimals = true) =>
        gb >= 1000 ? (decimals ? $"{gb / 1000:N1}" : $"{gb / 1000:N0}", "TB") : ($"{gb:N0}", "GB");

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

    private static string Sentence(string text) => text.Length == 0 ? text : text[..1].ToUpper() + text[1..].ToLower();
}
