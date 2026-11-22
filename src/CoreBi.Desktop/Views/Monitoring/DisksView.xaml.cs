using CoreBi.Desktop.Services;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Health;
using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Monitoring.Storage;
using LibreHardwareMonitor.Hardware;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;

namespace CoreBi.Desktop.Views;

public partial class DisksView : UserControl
{
    public sealed class DiskBay
    {
        public DiskBay(DiskHealthCard card)
        {
            Card = card;
            var d = card.Disk;
            Meta = d == null ? card.Meta
                : string.Join(" · ", new[] { d.Kind, d.Bus, FmtSize(d.SizeBytes) }.Where(s => s.Length > 0));
            Letters = d == null ? "" : string.Join(" ", d.Letters);
            Temperature = d?.Temperature;
        }

        public DiskHealthCard Card { get; set; }
        public DiskDetail? Disk => Card.Disk;
        public string Title => Card.Title;
        public string Meta { get; }
        public string Letters { get; }
        public string VerdictText => HardwareNaming.Sentence(Card.VerdictText);
        public string State => Card.Verdict switch { HealthVerdict.Good => "Ok", HealthVerdict.Warning => "Warn", HealthVerdict.Critical => "Danger", _ => "" };
        public double? Temperature { get; set; }
    }

    public sealed record DiskCheck(string Title, string Value, string State, string? Tip = null);
    public sealed record VolumeRow(string Letter, string Label, string Line);

    private sealed class DiskTrace
    {
        public readonly List<float?> Temps = new();
        public float? Read, Write;
        public float? Peak;
        public float? Last => Temps.Count > 0 ? Temps[^1] : null;
    }

    private FeatureContext _context = null!;
    private SensorMonitor _monitor => _context.Sensors;
    private bool _refreshing;
    private List<DiskBay> _bays = new();
    private List<DriveCard> _drives = new();
    private readonly Dictionary<string, DiskTrace> _traces = new(StringComparer.Ordinal);
    private DateTime _readAt;
    private bool _wide = true;

    public DisksView() => InitializeComponent();

    internal void Initialize(FeatureContext context) => _context = context;

    private async void BtnDiskRefresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    internal async Task RefreshAsync(bool entering = false)
    {
        if (_refreshing || _context == null || _context.IsClosing) return;
        _refreshing = true;
        bool first = _readAt == default, quiet = entering && !first;
        if (!quiet)
        {
            BtnDiskRefresh.IsEnabled = false;
            HeaderMotion.Say(DiskHealthStatus, null, "Lendo os discos…");
        }
        if (first) Skeleton.Set(true, DiskPlate, LifeCard, TempCard, CheckCard); // primeira leitura: a ficha ainda não tem disco
        try
        {
            var sensors = _monitor.Snapshot();
            var cardsTask = Task.Run(() => DiskHealth.Read(sensors, _monitor.NvmeLogs()));
            var drivesTask = Task.Run(DriveMap.Read);
            var cards = await cardsTask;
            List<DriveCard> drives;
            try { drives = await drivesTask; } catch { drives = new(); }
            if (entering) await PageMotion.EntranceDone(this);
            if (_context.IsClosing) return;
            _readAt = DateTime.Now;
            _drives = drives;
            _context.Drives = drives;
            int keep = (DiskCards.SelectedItem as DiskBay)?.Card.Index ?? -1;
            var bays = cards.Select(c => new DiskBay(c)).ToList();
            // O mesmo rack da tela (mesmos discos, nomes, selos e unidades): os compartimentos ficam e só a ficha do escolhido
            // é reescrita; trocar a lista refazia o rack e a seleção a cada volta à página
            static string Key(DiskBay b) => $"{b.Card.Index}|{b.Title}|{b.Meta}|{b.Letters}|{b.VerdictText}";
            if (_bays.Count > 0 && _bays.Select(Key).SequenceEqual(bays.Select(Key)))
            {
                for (int i = 0; i < bays.Count; i++) { _bays[i].Card = bays[i].Card; _bays[i].Temperature = bays[i].Temperature; }
                if (DiskCards.SelectedItem is DiskBay shown) ShowDisk(shown);
            }
            else
            {
                _bays = bays;
                DiskCards.ItemsSource = _bays;
                DiskCards.SelectedItem = _bays.FirstOrDefault(b => b.Card.Index == keep) ?? _bays.FirstOrDefault();
            }
            ShowRack();

            // Uma coisa por vez: o que pede atenção, quando há; senão só a hora da leitura (a contagem fica no rack)
            int bad = cards.Count(c => c.Verdict is HealthVerdict.Warning or HealthVerdict.Critical);
            int unknown = cards.Count(c => c.Verdict == HealthVerdict.Unknown);
            string read = $"Lido às {_readAt:HH:mm}.";
            HeaderMotion.Say(DiskHealthStatus, null, cards.Count == 0 ? "Nenhum disco encontrado."
                : bad > 0 ? $"{(bad == 1 ? "1 disco precisa" : $"{bad} discos precisam")} de atenção. {read}"
                : unknown > 0 ? $"{(unknown == 1 ? "1 disco" : $"{unknown} discos")} sem dados de saúde. {read}"
                : $"Lido às {_readAt:HH:mm}");
            DiskPanel.Visibility = cards.Count == 0 ? Visibility.Hidden : Visibility.Visible;
        }
        catch (Exception ex) { if (!_context.IsClosing) HeaderMotion.Say(DiskHealthStatus, null, "Não foi possível ler os discos: " + ex.Message); }
        finally
        {
            _refreshing = false;
            if (first) Skeleton.Set(false, DiskPlate, LifeCard, TempCard, CheckCard);
            if (!_context.IsClosing) BtnDiskRefresh.IsEnabled = true;
        }
    }


    private UniformGrid? _rackGrid;

    private void RackGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _rackGrid = (UniformGrid)sender;
        ShowRack();
    }

    /// <summary>
    /// A faixa de teclas só existe com mais de um disco (com um, a tecla repetiria a placa). As teclas dividem a largura em uma
    /// fila; de seis discos em diante viram duas filas, para o nome não ficar cortado.
    /// </summary>
    private void ShowRack()
    {
        int n = _bays.Count;
        DiskCards.Visibility = n > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (_rackGrid == null) return;
        _rackGrid.Rows = n > 5 ? 2 : 1;
        _rackGrid.Columns = n > 5 ? (n + 1) / 2 : Math.Max(1, n);
    }

    /// <summary>
    /// Só para as capturas (CoreBi.Tests --shots "PageDisks:3"): repete o primeiro disco para ver a faixa de teclas num PC com
    /// um disco só. Não é chamado pelo app; a próxima leitura volta aos discos reais.
    /// </summary>
    internal void PreviewRack(int count)
    {
        if (_bays.Count == 0 || count < 1) return;
        var first = _bays[0].Card;
        _bays = Enumerable.Range(0, count).Select(_ => new DiskBay(first)).ToList();
        DiskCards.ItemsSource = _bays;
        DiskCards.SelectedIndex = 0;
        ShowRack();
    }

    /// <summary>Guarda temperatura e taxas de cada disco do LibreHardwareMonitor; com a página aberta, atualiza as leituras.</summary>
    internal void Tick()
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
        ShowTempNow(now);
        TempScale.Value = now ?? double.NaN;
        ShowTempState(now, d);
        TempLiveText.Text = live ? "Ao vivo" : $"Leitura do Windows às {_readAt:HH:mm}";
        TempLiveDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, live ? "Signal" : "Faint");
        TempTrace.Samples = trace?.Temps.ToList() ?? new List<float?>();
        double? peak = trace?.Peak ?? d.TemperatureMax;
        if (peak is double pk) Readout(TempPeak, $"{pk:0}", "°C");
        else { TempPeak.Inlines.Clear(); TempPeak.Text = "—"; }
        TempPeak.SetResourceReference(TextBlock.ForegroundProperty, peak != null ? "Fg" : "Faint");
        DiskActivity.Text = trace?.Read == null && trace?.Write == null ? "Sem sensor de atividade"
            : $"Leitura {Rate(trace!.Read)}     Escrita {Rate(trace.Write)}";
    }

    /// <summary>
    /// Leitura grande com a unidade na mesma linha de base. Sem leitura, um travessão curto no tom apagado (o da Bahnschrift
    /// Light a 64 px viraria uma barra larga), na mesma altura de linha.
    /// </summary>
    private void ShowTempNow(double? now)
    {
        TempNow.Inlines.Clear();
        if (now is double v)
        {
            TempNow.FontSize = 64;
            TempNow.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            TempNow.Inlines.Add(new Run($"{v:0}"));
            var unit = new Run(" °C") { FontSize = 20 };
            unit.SetResourceReference(TextElement.ForegroundProperty, "Muted");
            unit.SetResourceReference(TextElement.FontFamilyProperty, "LabelFont");
            TempNow.Inlines.Add(unit);
        }
        else
        {
            TempNow.FontSize = 28;
            TempNow.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
            TempNow.Inlines.Add(new Run("—"));
        }
    }


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
        Ui.SetTone(DiskBadge, tone);
        // Saudável é silencioso: o selo já diz "Saudável" (a ressalva das leituras disponíveis fica na dica dele). Atenção,
        // crítico e "sem dados" ganham a linha com o motivo no pé da placa.
        bool healthy = card.Verdict == HealthVerdict.Good;
        DiskBadge.ToolTip = healthy ? "Nenhum problema encontrado nas leituras disponíveis." : null;
        DiskReasons.Text = healthy ? "" : card.Reasons;
        DiskReasonRow.Visibility = healthy || card.Reasons.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        DiskReasonIcon.Text = healthy ? "\uE73E" : card.Verdict == HealthVerdict.Unknown ? "\uE946" : "\uE7BA";
        DiskReasonIcon.SetResourceReference(TextBlock.ForegroundProperty, healthy ? "Muted" : tone.Length > 0 ? tone : "Faint");
        DiskReasons.SetResourceReference(TextBlock.ForegroundProperty, healthy ? "Muted" : "Fg");

        // Erro de leitura: só a placa com o motivo
        var instruments = d == null ? Visibility.Collapsed : Visibility.Visible;
        foreach (var el in new UIElement[] { LifeCard, TempCard, CheckCard, OdoCard, DiskSpace }) el.Visibility = instruments;
        // Sem o espaço embaixo, a placa é o painel inteiro: cantos de baixo redondos e sem o fio que a separava dele
        PlateBand.CornerRadius = PlateWash.CornerRadius = d == null ? new CornerRadius(15) : (CornerRadius)FindResource("RadiusPanelTop");
        PlateBand.BorderThickness = new Thickness(0, 0, 0, d == null ? 0 : 1);
        DiskKind.Text = d == null ? "" : string.Join(" · ", new[] { d.Kind, d.Bus }.Where(s => s.Length > 0));
        if (d == null) { DiskSpecs.Inlines.Clear(); DiskSpecs.ToolTip = null; return; }
        ShowSpecs(d);

        ShowSpace(d);
        ShowLife(d);
        ShowTemperature(d);
        ShowChecks(d);
        ShowOdometer(d);
        ShowLive();
    }

    /// <summary>
    /// A especificação numa frase sob o modelo, como em Placa de vídeo: "953,9 GB · Unidade C: · Firmware 3.2.F.83 · Série …",
    /// chave em tom secundário e valor em tinta. O tipo e a conexão já estão no sobretítulo; a série, longa, vai por último
    /// (cortada com reticências) e inteira na dica, com a do tamanho.
    /// </summary>
    private void ShowSpecs(DiskDetail d)
    {
        DiskSpecs.Inlines.Clear();
        void Part(string key, string value)
        {
            if (value.Length == 0) return;
            if (DiskSpecs.Inlines.Count > 0)
            {
                var sep = new Run("   ·   ");
                sep.SetResourceReference(TextElement.ForegroundProperty, "Faint");
                DiskSpecs.Inlines.Add(sep);
            }
            if (key.Length > 0) DiskSpecs.Inlines.Add(new Run(key + " "));
            var v = new Run(value);
            v.SetResourceReference(TextElement.ForegroundProperty, "Fg");
            DiskSpecs.Inlines.Add(v);
        }
        Part("", FmtSize(d.SizeBytes));
        Part(d.Letters.Count > 1 ? "Unidades" : "Unidade", string.Join(" ", d.Letters));
        Part("Firmware", d.Firmware);
        Part("Série", d.Serial);
        var tips = new[] { SizeTip(d), d.Serial.Length > 0 ? "Série: " + d.Serial : null }.Where(t => t != null);
        DiskSpecs.ToolTip = tips.Any() ? string.Join("\n\n", tips) : null;
    }

    /// <summary>
    /// Dica do tamanho: o selo e o painel Espaço contam na mesma base do Windows (1024); o fabricante anuncia em base 1000, e
    /// as unidades podem somar menos que o disco (o resto fica em partições do sistema, sem letra).
    /// </summary>
    private string? SizeTip(DiskDetail d)
    {
        if (d.SizeBytes <= 0) return null;
        string tip = $"Tamanho do disco inteiro, contado como no Windows. O fabricante anuncia {FmtDecimal(d.SizeBytes)} (base 1000).";
        long volumes = _drives.Where(v => d.Letters.Contains(v.Letter, StringComparer.OrdinalIgnoreCase)).Sum(v => v.Total);
        if (volumes > 0 && volumes < d.SizeBytes * 0.99)
            tip += $" As unidades somam {DriveCard.Fmt(volumes)}; o resto fica em partições do sistema, sem letra.";
        return tip;
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

        Ui.SetItems(DiskVolumes, mine.Count > 1
            ? mine.Select(v => new VolumeRow(v.Letter, string.IsNullOrWhiteSpace(v.Label) ? v.FileSystem : v.Label,
                $"{DriveCard.Fmt(v.Free)} livres de {DriveCard.Fmt(v.Total)}")).ToList()
            : null);
    }

    private void ShowLife(DiskDetail d)
    {
        LifeDial.Value = d.Wear is double w ? Math.Clamp(100 - w, 0, 100) : double.NaN;
        LifeDial.Caption = d.Wear != null ? "restante" : "não informada";
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

    private void ShowTemperature(DiskDetail d)
    {
        TempScale.WarnAt = d.TempWarnAt;
        TempScale.CritAt = d.TempCritAt;
        TempTrace.WarnAt = d.TempWarnAt;
        TempTrace.EmptyText = d.SensorHardware == null
            ? "Sem sensor ao vivo para este disco."
            : "Coletando as primeiras amostras…";
    }

    private void ShowTempState(double? t, DiskDetail d)
    {
        // Na faixa ideal fica em tinta: a cor só aparece quando a temperatura pede atenção
        (string text, string key) = t switch
        {
            null => ("Sem leitura", "Faint"),
            double v when v >= d.TempCritAt => ("Crítica", "Danger"),
            double v when v >= d.TempWarnAt => ("Alta", "Warn"),
            _ => ("Na faixa ideal", "Fg"),
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
                (string text, string state) = DiskHealth.RateUnsafeShutdowns(unsafeCount, d.PowerOnHours) switch
                {
                    HealthVerdict.Critical => ("alto", "Danger"),
                    HealthVerdict.Warning => ("atenção", "Warn"),
                    _ => ("ok", "Ok"),
                };
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
        Ui.SetItems(DiskChecks, GroupUnknown(rows));
    }

    /// <summary>
    /// O que o Windows não informa vira um item só ("Falha prevista, erros de leitura e de escrita · Windows não informa"), em
    /// vez de repetir a mesma frase em cada um; sem nenhuma leitura, uma frase só. Assim a faixa cabe numa linha.
    /// </summary>
    private static List<DiskCheck> GroupUnknown(List<DiskCheck> rows)
    {
        var known = rows.Where(r => r.State.Length > 0).ToList();
        var unknown = rows.Where(r => r.State.Length == 0).ToList();
        if (unknown.Count <= 1) return rows;
        if (known.Count == 0) return new() { new("O Windows não informa a saúde nem os contadores de erro deste disco.", "", "", NotReportedTip) };
        var names = unknown.Select((r, i) => i == 0 ? r.Title : char.ToLower(r.Title[0]) + r.Title[1..]).ToList();
        string joined = string.Join(", ", names.Take(names.Count - 1)) + " e " + names[^1];
        known.Add(new(joined, NotReported, "", NotReportedTip));
        return known;
    }

    /// <summary>Só mostra o que o disco informa: um disco que não conta horas ou ciclos fica com menos células, não com traços.</summary>
    private void ShowOdometer(DiskDetail d)
    {
        var cells = new List<Border>();
        void Cell(Border cell, TextBlock target, string? value, string unit)
        {
            cell.Visibility = value == null ? Visibility.Collapsed : Visibility.Visible;
            if (value == null) return;
            Readout(target, value, unit, balanced: true);
            cells.Add(cell);
        }
        Cell(OdoCell0, OdoHours, d.PowerOnHours is long h ? $"{h:N0}" : null, "h");
        Cell(OdoCell1, OdoCycles, d.StartStopCycles is long c ? $"{c:N0}" : null, "");
        var written = d.WrittenGb is double wg ? SplitData(wg) : default;
        Cell(OdoCell2, OdoWritten, d.WrittenGb != null ? written.Value : null, written.Unit);
        var read = d.ReadGb is double rg ? SplitData(rg) : default;
        Cell(OdoCell3, OdoRead, d.ReadGb != null ? read.Value : null, read.Unit);
        // Linha de contexto sob cada leitura, derivada só do que o disco informa
        double? days = d.PowerOnHours is > 0 ? d.PowerOnHours.Value / 24.0 : null;
        OdoHoursNote.Text = days is double dd ? (dd >= 730 ? $"≈ {dd / 365:N1} anos ligado" : $"≈ {dd:N0} dias ligado") : "";
        OdoCyclesNote.Text = d.StartStopCycles is > 0 && d.PowerOnHours is > 0 ? $"≈ {(double)d.PowerOnHours.Value / d.StartStopCycles.Value:N1} h por ciclo" : "";
        string PerDay(double? gb) => gb is double g && days is double dy ? PerDayText(g / dy) : "";
        OdoWrittenNote.Text = PerDay(d.WrittenGb);
        OdoReadNote.Text = PerDay(d.ReadGb);
        _odoCells = cells;
        LayoutOdometer();
    }

    private static string PerDayText(double gbPerDay) =>
        gbPerDay >= 1000 ? $"≈ {gbPerDay / 1000:N1} TB por dia" : $"≈ {gbPerDay:N0} GB por dia";

    private List<Border> _odoCells = new();

    /// <summary>
    /// O odômetro fica na placa: larga, à direita do nome; estreita, numa linha embaixo dele. A primeira célula presente não
    /// tem fio à esquerda (como a primeira leitura da placa de Placa de vídeo). Sem nenhuma, some.
    /// </summary>
    private void LayoutOdometer()
    {
        OdoCard.Visibility = _odoCells.Count == 0 || DiskCards.SelectedItem is not DiskBay { Disk: not null } ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(OdoCard, _wide ? 0 : 1);
        Grid.SetColumn(OdoCard, _wide ? 2 : 1);
        Grid.SetColumnSpan(OdoCard, _wide ? 1 : 2);
        OdoCard.HorizontalAlignment = _wide ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        OdoCard.Margin = new Thickness(_wide ? 0 : 20, _wide ? 0 : 20, 0, 0);
        for (int i = 0; i < _odoCells.Count; i++)
        {
            var cell = _odoCells[i];
            cell.BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0);
            cell.Padding = new Thickness(i == 0 ? 0 : 28, 0, 0, 0);
            cell.Margin = new Thickness(i == 0 ? 0 : 28, 0, 0, 0);
        }
    }


    /// <summary>Larga: odômetro à direita do nome. Estreita: odômetro desce para baixo do nome e a vida útil estreita.</summary>
    private void DiskBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        bool wide = e.NewSize.Width >= 1320;
        if (wide == _wide && e.PreviousSize.Width > 0) return;
        _wide = wide;
        LifeColumn.Width = new GridLength(wide ? 420 : 340);
        LayoutOdometer();
        LifeDial.MinHeight = wide ? 210 : 190;
    }


    private static void Readout(TextBlock target, string value, string unit, bool balanced = false) =>
        ReadoutFormat.Set(target, value, unit, unitRatio: 0.4, balanced: balanced);

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

    private static string FmtSize(long bytes) => bytes > 0 ? DriveCard.Fmt(bytes) : "";

    private static string FmtDecimal(long bytes)
    {
        double v = bytes; string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0;
        while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
        return bytes > 0 ? $"{v:0.#} {u[i]}" : "";
    }
}
