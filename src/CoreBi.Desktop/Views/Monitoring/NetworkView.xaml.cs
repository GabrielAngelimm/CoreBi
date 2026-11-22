using CoreBi.Desktop.Services;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Health;
using CoreBi.Engine.Monitoring.Network;
using CoreBi.Engine.Performance;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace CoreBi.Desktop.Views;

public partial class NetworkView : UserControl
{
    private FeatureContext _context = null!;
    internal IReadOnlyList<HealthCard>? LastResult { get; private set; }
    private NetworkResult? _result;
    private CancellationTokenSource? _cancellation;
    private bool _running;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public NetworkView()
    {
        InitializeComponent();
        DrawDial(null, null);
    }

    internal void Initialize(FeatureContext context) => _context = context;

    // Larguras do painel de diagnóstico: abaixo de NarrowDiag a latência e as grandezas de apoio descem para uma faixa sob o
    // instrumento (a latência à esquerda e as quatro grandezas em fila); abaixo de CompactDiag a faixa não comporta cinco
    // colunas e volta ao arranjo do painel largo (latência em cima, grandezas em grade 2 × 2).
    private const double NarrowDiag = 1200, CompactDiag = 900;
    private int _diagState = -1; // 0 largo, 1 estreito, 2 compacto

    private void NetDiagCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double width = NetDiagCard.ActualWidth;
        int state = width < CompactDiag ? 2 : width < NarrowDiag ? 1 : 0;
        if (state == _diagState) return;
        _diagState = state;
        bool narrow = state > 0, inRow = state == 1;

        NetMetricsColumn.MinWidth = narrow ? 0 : 380;
        NetMetricsColumn.MaxWidth = narrow ? double.PositiveInfinity : 520;
        Grid.SetColumnSpan(NetInstrument, narrow ? 2 : 1);
        NetInstrument.MaxHeight = narrow ? 170 : 350;
        NetInstrument.Margin = narrow ? new Thickness(24, 0, 24, 6) : new Thickness(24, 4, 16, 8);
        NetInstrument.HorizontalAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        // A fonte do teste já fica na linha do título, à direita; estreito, só a latência encolhe: a página continua numa tela só.

        Grid.SetRow(NetMetricsPanel, narrow ? 1 : 0);
        Grid.SetColumn(NetMetricsPanel, narrow ? 0 : 1);
        Grid.SetColumnSpan(NetMetricsPanel, narrow ? 2 : 1);
        NetMetricsPanel.BorderThickness = narrow ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        NetMetricsPanel.Margin = narrow ? new Thickness(0) : new Thickness(0, 8, 0, 16);
        NetMetricsPanel.Padding = !narrow ? new Thickness(32, 0, 24, 0) : inRow ? new Thickness(24, 8, 24, 8) : new Thickness(24, 12, 24, 12);

        // A unidade é um Run do mesmo TextBlock, com tamanho próprio: só o número muda de tamanho e a base continua comum
        NetLatencyLine.FontSize = inRow ? 44 : 64;
        NetLatencyLine.Margin = inRow ? new Thickness(0, 4, 0, 0) : new Thickness(0, 4, 0, 16);
        Grid.SetColumnSpan(NetHero, inRow ? 1 : 2);
        NetHero.VerticalAlignment = inRow ? VerticalAlignment.Center : VerticalAlignment.Top;
        Grid.SetRow(NetCells, inRow ? 0 : 1);
        Grid.SetColumn(NetCells, inRow ? 1 : 0);
        Grid.SetColumnSpan(NetCells, inRow ? 1 : 2);
        NetCells.Columns = inRow ? 4 : 2;
        NetCells.Margin = inRow ? new Thickness(28, 0, 0, 0) : new Thickness(0);
        NetCells.VerticalAlignment = inRow ? VerticalAlignment.Center : VerticalAlignment.Stretch;
        var cells = new[] { NetCell1, NetCell2, NetCell3, NetCell4 };
        for (int i = 0; i < cells.Length; i++)
        {
            bool second = i % 2 == 1;
            cells[i].BorderThickness = inRow ? new Thickness(1, 0, 0, 0) : new Thickness(second ? 1 : 0, 1, 0, 0);
            cells[i].Padding = inRow ? new Thickness(20, 0, 8, 0) : new Thickness(second ? 20 : 0, 14, second ? 0 : 16, 14);
        }
    }

    private async void BtnNetTest_Click(object sender, RoutedEventArgs e) => await RunOrCancelAsync();

    internal async Task RunOrCancelAsync()
    {
        if (_context == null || _context.IsClosing) return;
        if (_running) { _cancellation?.Cancel(); return; }
        using var workload = _context.Workloads.TryBegin(WorkloadOperations.Network);
        if (workload == null) { Say("Termine o outro teste antes."); return; }
        _running = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_context.Lifetime);
        _cancellation = cancellation;
        Ui.SetRunning(BtnNetTest, true, "Testar a rede", "Cancelar");
        LastResult = null;
        DisplayResult(null);
        SetProgress(0, animate: false);
        ShowStages(NetworkPhase.Latency);
        NetMeta.Text = "Medindo agora";
        bool completed = false;
        try
        {
            int attempt = 0;
            Action<NetworkProgress> Progress(int generation) => p =>
            {
                if (_context.IsClosing) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_context.IsClosing || !_running || _cancellation != cancellation || attempt != generation) return;
                    ShowProgress(p);
                }));
            };
            var result = await NetworkTest.RunAutomaticAsync(
                selected => NetworkTest.Run(Progress(attempt), cancellation.Token, selected, mlabConsent: true),
                cancellation.Token, () =>
                {
                    attempt++;
                    Say("Tentando outro servidor");
                    SetProgress(0, animate: false);
                });
            if (_context.IsClosing) return;
            LastResult = new[] { result.Card };
            DisplayResult(result);
            if (result.InterruptedPhase != null)
            {
                Say(result.RetryAfterUtc != null ? "Aguardando o servidor" : "Teste incompleto");
                SetProgress(result.InterruptedFraction, animate: false);
            }
            else
            {
                Say($"Concluído às {result.TestedAt:HH:mm:ss}");
                ShowStages(NetworkPhase.Done);
                SetProgress(1, animate: true);
                completed = true;
            }
        }
        catch (OperationCanceledException)
        {
            if (!_context.IsClosing) Reset("Teste cancelado", "");
        }
        catch (Exception ex)
        {
            if (!_context.IsClosing) Reset("O teste falhou", ex.Message);
        }
        finally
        {
            _running = false;
            _cancellation = null;
            if (!_context.IsClosing)
            {
                Ui.SetRunning(BtnNetTest, false, LastResult != null ? "Testar de novo" : "Testar a rede");
                // Concluído, o medidor já segura o 100 % e fecha sozinho; parado no meio, fecha agora
                if (!completed) HeaderMotion.EndProgress(NetProgressFill);
            }
        }
    }

    /// <summary>Estado da barra de ações, com a troca suave de <see cref="HeaderMotion.Say"/>.</summary>
    private void Say(string status, string detail = "") => HeaderMotion.Say(NetStatus, NetDetail, status, detail);

    private void Reset(string status, string detail)
    {
        Say(status, detail);
        SetProgress(0, animate: false);
        DisplayResult(null);
        NetMeta.Text = "Sem resultado";
    }

    private void ShowProgress(NetworkProgress p)
    {
        Say(p.Text); // o detalhe fica vazio: a etapa está escrita sobre a barra
        ShowStages(p.Phase);
        SetProgress(p.Fraction, animate: true);
        // Leituras parciais: só o que já foi medido; o restante continua com travessão.
        SetReadout(NetLatency, p.LatencyMs);
        SetReadout(NetDown, p.DownloadMbps);
        SetReadout(NetUp, p.UploadMbps);
        NetLatencyCaption.Text = p.LatencyMs != null ? "\u2002ms até a internet" : "\u2002ms";
        DrawDial(p.DownloadMbps, p.UploadMbps);
    }

    /// <summary>
    /// A barra só anda quando o motor informa trabalho feito; o movimento (abrir, deslizar, contar a porcentagem, segurar o
    /// 100 % e fechar) é o de todo medidor de cabeçalho (<see cref="HeaderMotion"/>). Sem animate, vai direto ao valor.
    /// </summary>
    private void SetProgress(double fraction, bool animate)
    {
        if (!animate) { NetProgress.BeginAnimation(ScaleTransform.ScaleXProperty, null); NetProgress.ScaleX = 0; }
        HeaderMotion.SetProgress(NetProgressFill, fraction);
    }

    /// <summary>
    /// Pinta as três estações escritas sobre a barra: a da vez no sinal e em seminegrito, as já feitas no tom do texto e as que
    /// faltam apagadas. Preparando, a primeira já acende; na análise e no fim, as três ficam feitas.
    /// </summary>
    private void ShowStages(NetworkPhase phase)
    {
        int current = phase switch { NetworkPhase.Latency => 1, NetworkPhase.Download => 2, NetworkPhase.Upload => 3, NetworkPhase.Analysis or NetworkPhase.Done => 4, _ => 1 };
        TextBlock[] stages = [NetStage1, NetStage2, NetStage3];
        for (int i = 0; i < stages.Length; i++)
        {
            int n = i + 1;
            stages[i].SetResourceReference(TextBlock.ForegroundProperty, n == current ? "Signal" : n < current ? "Fg" : "Faint");
            stages[i].FontWeight = n == current ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static string Format(double v) => v.ToString(v >= 100 ? "0" : "0.#", PtBr);

    private static void SetReadout(TextBlock block, double? value)
    {
        block.Text = value is { } v ? Format(v) : "—";
        block.SetResourceReference(TextBlock.ForegroundProperty, value != null ? "Fg" : "Faint");
    }

    /// <summary>O mesmo, para uma leitura que é um Run (a latência, que divide o TextBlock com a unidade).</summary>
    private static void SetReadout(Run run, double? value)
    {
        run.Text = value is { } v ? Format(v) : "—";
        run.SetResourceReference(TextElement.ForegroundProperty, value != null ? "Fg" : "Faint");
    }

    private static string VerdictKey(HealthVerdict v) => v switch
    {
        HealthVerdict.Good => "Ok",
        HealthVerdict.Warning => "Warn",
        HealthVerdict.Critical => "Danger",
        _ => "Faint",
    };

    internal void DisplayResult(NetworkResult? r)
    {
        _result = r;
        SetReadout(NetDown, r?.DownloadMbps);
        SetReadout(NetUp, r?.UploadMbps);
        NetUp.ToolTip = r?.UploadIncludesProtocol == true ? "Upload medido pelo contador TCP do servidor; inclui a sobrecarga do protocolo." : null;
        SetReadout(NetLatency, r?.LatencyMs);
        NetLatencyCaption.Text = r?.LatencyMs != null ? "\u2002ms até a internet" : "\u2002ms";
        DrawDial(r?.DownloadMbps, r?.UploadMbps);

        SetReadout(NetJitter, r?.JitterMs);
        NetLoss.Text = r?.LossPct is { } loss ? loss.ToString("0.#", PtBr) : "—";
        NetLoss.SetResourceReference(TextBlock.ForegroundProperty, r?.LossPct == null ? "Faint" : r.LossPct > NetworkThresholds.InternetLossWarnPct ? "Warn" : "Fg");
        SetReadout(NetLoaded, r?.LoadedLatencyMs);
        if (r?.Bufferbloat == true) NetLoaded.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
        NetLoadedSub.Text = r?.BufferbloatMs is { } bloat ? $"{(bloat >= 0 ? "+" : "−")}{Math.Abs(bloat):0} ms durante o download" : "";
        SetReadout(NetDns, r?.DnsMs is { } dns ? Math.Round(dns) : null);

        if (r == null)
        {
            NetBadge.Visibility = Visibility.Collapsed;
            NetVerdict.Visibility = Visibility.Collapsed;
            NetMeta.Text = LastResult == null ? "Ainda não testado nesta sessão" : NetMeta.Text;
        }
        else
        {
            NetMeta.Text = r.Card.Meta;
            string key = VerdictKey(r.Card.Verdict == HealthVerdict.Unknown ? HealthVerdict.Warning : r.Card.Verdict);
            // Estável: a linha do veredito ("Latência baixa e sem perda…", com o visto) já diz isso; o selo repetiria (U14).
            // Atenção e ruim mantêm o selo, que resume a gravidade dos achados listados embaixo.
            NetBadge.Visibility = r.Card.Verdict == HealthVerdict.Good ? Visibility.Collapsed : Visibility.Visible;
            Ui.SetTone(NetBadge, key);
            NetBadgeText.Text = HardwareNaming.Sentence(r.Card.VerdictText);
            ShowNotes(r, key);
        }

        ShowRoute(r);
        DrawTrace();
        if (r != null) ShowAdapter(r.Adapter);
    }

    private void ShowNotes(NetworkResult r, string key)
    {
        NetNotes.Children.Clear();
        bool good = r.Card.Verdict == HealthVerdict.Good;
        foreach (string note in r.Notes)
        {
            var row = new Grid { Margin = new Thickness(0, NetNotes.Children.Count == 0 ? 0 : 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            // Achado informativo (roteador que não responde a ping) não leva a cor do veredito. O glifo fica solto, sem bolha
            // colorida em volta: a cor está no sinal, não num enfeite (como a saúde do PC na Visão geral).
            bool info = note.StartsWith(NetworkThresholds.RouterSilentNotePrefix, StringComparison.Ordinal);
            string noteKey = info ? "Muted" : key;
            var glyph = new TextBlock
            {
                Text = good ? "" : info ? "" : "",
                FontSize = 12,
                Width = 22,
                Margin = new Thickness(0, 3, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
            glyph.SetResourceReference(TextBlock.ForegroundProperty, noteKey);
            var text = new TextBlock { Text = note, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) };
            text.SetResourceReference(StyleProperty, "Body");
            Grid.SetColumn(text, 1);
            row.Children.Add(glyph);
            row.Children.Add(text);
            NetNotes.Children.Add(row);
        }
        NetVerdict.Visibility = Visibility.Visible;
    }

    private void ShowRoute(NetworkResult? r)
    {
        var router = r?.Router;
        NetRouterMs.Text = router is { Answered: true } ? Format(router.Avg) : "—";
        NetRouterMs.SetResourceReference(TextBlock.ForegroundProperty, router is { Answered: true } ? "Fg" : "Faint");
        NetWanMs.Text = r?.LatencyMs is { } wan ? Format(wan) : "—";
        NetWanMs.SetResourceReference(TextBlock.ForegroundProperty, r?.LatencyMs != null ? "Fg" : "Faint");
        var answered = r?.Internet.Where(p => p.Answered).ToList() ?? [];
        NetWanDetail.ToolTip = answered.Count > 0
            ? string.Join("\n", answered.Select(p => $"{p.Name.Replace("Internet · ", "")} {p.Address}: {Format(p.Avg)} ms"))
            : null;

        if (r == null)
        {
            SetLink(NetLinkLocal, NetLinkLocalText, "LineStrong", "Faint", "—");
            SetLink(NetLinkWan, NetLinkWanText, "LineStrong", "Faint", "—");
            return;
        }
        string local = r.LocalVerdict switch
        {
            HealthVerdict.Good => $"estável · jitter {Format(router!.Jitter)} ms",
            HealthVerdict.Warning => $"instável · perda {router!.LossPct.ToString("0.#", PtBr)} % · jitter {Format(router.Jitter)} ms",
            HealthVerdict.Critical => "sem conexão",
            _ => router == null ? "não medido" : "não responde a ping",
        };
        string wanText = r.InternetVerdict switch
        {
            HealthVerdict.Good => $"estável · perda {r.LossPct?.ToString("0.#", PtBr) ?? "0"} %",
            HealthVerdict.Warning => r.LossPct > NetworkThresholds.InternetLossWarnPct ? $"perda de {r.LossPct?.ToString("0.#", PtBr)} %"
                                 : r.LatencyMs > NetworkThresholds.InternetLatencyWarnMs ? "latência alta" : r.JitterMs > NetworkThresholds.InternetJitterWarnMs ? "jitter alto" : "um alvo sem resposta",
            HealthVerdict.Critical => r.LatencyMs == null ? "sem resposta" : r.LossPct > NetworkThresholds.InternetLossCritPct ? $"perda de {r.LossPct?.ToString("0.#", PtBr)} %" : "latência muito alta",
            _ => r.Internet.Count == 0 ? "não medido" : "sem resposta a ping",
        };
        // Trecho estável fica neutro (um fio cheio, mais claro que o de "não medido"); só o trecho com problema ganha cor,
        // então o atraso salta aos olhos no ponto exato do caminho.
        string lk = VerdictKey(r.LocalVerdict), wk = VerdictKey(r.InternetVerdict);
        SetLink(NetLinkLocal, NetLinkLocalText, LinkLine(lk), LinkText(lk), local);
        SetLink(NetLinkWan, NetLinkWanText, LinkLine(wk), LinkText(wk), wanText);
    }

    private static string LinkLine(string key) => key switch { "Ok" => "Faint", "Faint" => "LineStrong", _ => key };
    private static string LinkText(string key) => key switch { "Ok" or "Faint" => "Muted", _ => key };

    private static void SetLink(Border line, TextBlock caption, string lineKey, string textKey, string text)
    {
        line.SetResourceReference(Border.BackgroundProperty, lineKey);
        caption.SetResourceReference(TextBlock.ForegroundProperty, textKey);
        caption.Text = text;
    }

    private async void NetworkView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible || _running || _result != null || _context == null || _context.IsClosing) return;
        if (NetInfo.ItemsSource == null) Skeleton.SetActive(NetInfoCard, true); // primeira leitura do adaptador
        NetworkAdapterInfo? adapter;
        try { adapter = await Task.Run(NetworkTest.ReadAdapter); }
        finally { Skeleton.SetActive(NetInfoCard, false); }
        if (_context.IsClosing || _running || _result != null) return;
        ShowAdapter(adapter);
    }

    private void ShowAdapter(NetworkAdapterInfo? a)
    {
        if (a == null)
        {
            NetInfo.ItemsSource = new[] { new KeyValuePair<string, string>("Adaptador", "nenhuma conexão ativa") };
            NetPcLink.Text = "—"; NetPcLinkUnit.Text = "de link"; NetPcDetail.Text = "sem conexão"; NetRouterIp.Text = "—";
            return;
        }
        // Linha que não acrescenta nada some: o nome que o Windows dá à conexão quando é só o tipo ("Ethernet", "Wi-Fi") e a
        // descrição genérica da placa ou igual ao nome
        var rows = new List<KeyValuePair<string, string>>();
        if (!IsGenericName(a.Name, a.Kind)) rows.Add(new("Adaptador", a.Name));
        if (!IsGenericName(a.Description, a.Kind) && !a.Description.Equals(a.Name, StringComparison.OrdinalIgnoreCase)) rows.Add(new("Placa", a.Description));
        NetInfo.ItemsSource = rows.Concat(new KeyValuePair<string, string>[]
        {
            new("Tipo", a.Kind),
            new("Velocidade do link", a.LinkText),
            new("IP local", a.LocalIp),
            new("Gateway", a.Gateway.ToString()),
            new("Servidor DNS", a.Dns),
            new("Endereço físico", a.Mac),
        }).ToList();
        if (a.LinkMbps is { } mbps)
        {
            bool giga = mbps >= 1000;
            NetPcLink.Text = (giga ? mbps / 1000 : mbps).ToString("0.#", PtBr);
            NetPcLinkUnit.Text = giga ? "Gbps de link" : "Mbps de link";
        }
        else { NetPcLink.Text = "—"; NetPcLinkUnit.Text = "de link"; }
        NetPcDetail.Text = $"{(a.Kind == "Ethernet (cabo)" ? "Cabo" : a.Kind)} · {a.LocalIp}";
        NetRouterIp.Text = a.Gateway.ToString();
    }

    private static readonly string[] GenericAdapterNames = { "Adaptador de rede", "Network adapter", "Ethernet", "Wi-Fi", "WiFi", "Conexão local", "Local Area Connection" };

    /// <summary>Vazio, um nome genérico ou só o tipo da conexão (o Tipo já diz "Ethernet (cabo)" ou "Wi-Fi").</summary>
    private static bool IsGenericName(string? value, string kind)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return true;
        return GenericAdapterNames.Any(g => g.Equals(value, StringComparison.OrdinalIgnoreCase))
            || kind.StartsWith(value, StringComparison.OrdinalIgnoreCase);
    }

    // Dois arcos de 136° espelhados (esquerda = download, direita = upload), de baixo (1 Mbps) para cima (1 Gbps), em escala
    // logarítmica: cada década ocupa um terço do arco. Segmentos na proporção 70/30 dos medidores do app.
    private const double DialCx = 160, DialCy = 130, DialR1 = 121, DialR2 = 138, DialGap = 22;
    private const int DialSegments = 36;

    private static double DialAngle(int dir, double t) => dir < 0
        ? 180 + DialGap + (180 - 2 * DialGap) * t
        : 180 - DialGap - (180 - 2 * DialGap) * t;

    private static Point DialPoint(double deg, double r) =>
        new(DialCx + r * Math.Sin(deg * Math.PI / 180), DialCy - r * Math.Cos(deg * Math.PI / 180));

    internal static double DialFraction(double? mbps) => mbps is not { } v ? 0 : Math.Clamp(Math.Log10(Math.Max(1, v)) / 3, 0, 1);

    private void DrawDial(double? down, double? up)
    {
        NetDial.Children.Clear();
        DrawSide(-1, down, "NetDown");
        DrawSide(1, up, "NetUp");
    }

    private void DrawSide(int dir, double? value, string channel)
    {
        double lit = value == null ? -1 : DialFraction(value);
        var outer = dir < 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
        var inner = dir < 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
        void Segment(StreamGeometryContext ctx, int i)
        {
            double a0 = DialAngle(dir, (double)i / DialSegments), a1 = DialAngle(dir, (i + 0.7) / DialSegments);
            ctx.BeginFigure(DialPoint(a0, DialR1), true, true);
            ctx.LineTo(DialPoint(a0, DialR2), false, false);
            ctx.ArcTo(DialPoint(a1, DialR2), new Size(DialR2, DialR2), 0, false, outer, false, false);
            ctx.LineTo(DialPoint(a1, DialR1), false, false);
            ctx.ArcTo(DialPoint(a0, DialR1), new Size(DialR1, DialR1), 0, false, inner, false, false);
        }

        var off = new StreamGeometry();
        using (var offCtx = off.Open())
            for (int i = 0; i < DialSegments; i++)
                if ((i + 0.5) / DialSegments > lit) Segment(offCtx, i);
        off.Freeze();
        var offPath = new Path { Data = off };
        offPath.SetResourceReference(Shape.FillProperty, "Line");
        NetDial.Children.Add(offPath);

        var onGeo = new StreamGeometry();
        using (var ctx = onGeo.Open())
            for (int i = 0; i < DialSegments && (i + 0.5) / DialSegments <= lit; i++) Segment(ctx, i);
        onGeo.Freeze();
        var onPath = new Path { Data = onGeo };
        onPath.SetResourceReference(Shape.FillProperty, channel);
        NetDial.Children.Add(onPath);
    }

    private void NetTrace_SizeChanged(object sender, SizeChangedEventArgs e) => DrawTrace();

    private void DrawTrace()
    {
        NetTrace.Children.Clear();
        var idle = _result?.Internet.FirstOrDefault(p => p.Address == "1.1.1.1")?.Samples ?? [];
        var loaded = _result?.LoadedSamples ?? [];
        bool has = idle.Count + loaded.Count > 0;
        NetTraceEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        double w = NetTrace.ActualWidth;
        if (w <= 0) return;
        AddTraceText("Pings até 1.1.1.1", 0, 0, "Muted", null, readout: false);
        if (!has) return;

        const double top = 16, gapW = 18;
        double bottom = Math.Max(top + 5, NetTrace.ActualHeight - 18);
        double peak = idle.Concat(loaded).Where(v => v != null).Select(v => v!.Value).DefaultIfEmpty(10).Max();
        double max = new[] { 10.0, 20, 50, 100, 200, 500, 1000, 2000 }.FirstOrDefault(s => s >= peak * 1.1, 2000);
        int n = idle.Count + loaded.Count;
        double step = (w - (loaded.Count > 0 && idle.Count > 0 ? gapW : 0)) / n, bar = Math.Max(1.5, step * 0.62);

        AddTraceLine(0, w, top, "Line", dashed: true);
        AddTraceLine(0, w, bottom, "Line", dashed: false);
        // 3 px abaixo: a escala (Bahnschrift 10,5) fica na mesma base do rótulo "Pings até 1.1.1.1" (Segoe 11,5) à esquerda (medido)
        AddTraceText($"{max:0} ms", w - 40, 3, "Faint", null, readout: true, align: TextAlignment.Right, width: 40);

        var idleGeo = new StreamGeometry();
        var loadGeo = new StreamGeometry();
        var lostGeo = new StreamGeometry();
        using (var ic = idleGeo.Open())
        using (var lc = loadGeo.Open())
        using (var xc = lostGeo.Open())
        {
            void Bars(IReadOnlyList<double?> samples, double x0, StreamGeometryContext ctx)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    double x = x0 + i * step;
                    if (samples[i] is { } v)
                    {
                        double h = Math.Max(2, Math.Min(1, v / max) * (bottom - top));
                        Rect(ctx, x, bottom - h, bar, h);
                    }
                    else Rect(xc, x, top, bar, 3); // pacote perdido: marca no topo da escala
                }
            }
            Bars(idle, 0, ic);
            Bars(loaded, idle.Count * step + (idle.Count > 0 ? gapW : 0), lc);
        }
        AddTracePath(idleGeo, "Steel");
        AddTracePath(loadGeo, "NetDown");
        AddTracePath(lostGeo, "Danger");

        var idleRtts = idle.Where(v => v != null).Select(v => v!.Value).ToList();
        if (idle.Count > 0)
            AddTraceText("Em repouso · média ", 0, bottom + 2, "Muted", idleRtts.Count > 0 ? $"{idleRtts.Average():0} ms" : "—", readout: false);
        if (loaded.Count > 0)
        {
            double xs = idle.Count * step + (idle.Count > 0 ? gapW : 0);
            if (idle.Count > 0) AddTraceLine(xs - gapW / 2, xs - gapW / 2, top, "LineStrong", dashed: true, vertical: bottom);
            AddTraceText("Durante o download · média ", xs, bottom + 2, "Muted",
                _result?.LoadedLatencyMs is { } l ? $"{l:0} ms" : "—", readout: false, valueKey: _result?.Bufferbloat == true ? "Warn" : "Fg");
        }
    }

    private static void Rect(StreamGeometryContext ctx, double x, double y, double w, double h)
    {
        ctx.BeginFigure(new Point(x, y), true, true);
        ctx.LineTo(new Point(x + w, y), false, false);
        ctx.LineTo(new Point(x + w, y + h), false, false);
        ctx.LineTo(new Point(x, y + h), false, false);
    }

    private void AddTracePath(StreamGeometry geometry, string key)
    {
        geometry.Freeze();
        var path = new Path { Data = geometry };
        path.SetResourceReference(Shape.FillProperty, key);
        NetTrace.Children.Add(path);
    }

    private void AddTraceLine(double x0, double x1, double y, string key, bool dashed, double? vertical = null)
    {
        var line = new Line { X1 = x0, X2 = x1, Y1 = y + 0.5, Y2 = (vertical ?? y) + 0.5, StrokeThickness = 1, SnapsToDevicePixels = true };
        if (dashed) line.StrokeDashArray = new DoubleCollection { 2, 4 };
        line.SetResourceReference(Shape.StrokeProperty, key);
        NetTrace.Children.Add(line);
    }

    private void AddTraceText(string text, double x, double y, string key, string? value, bool readout, string valueKey = "Fg",
        TextAlignment align = TextAlignment.Left, double width = double.NaN)
    {
        var block = new TextBlock { FontSize = readout ? 10.5 : 11.5, TextAlignment = align, Width = width };
        if (readout)
        {
            block.SetResourceReference(TextBlock.FontFamilyProperty, "Readout");
            block.FontStretch = FontStretches.SemiCondensed;
        }
        block.SetResourceReference(TextBlock.ForegroundProperty, key);
        block.Inlines.Add(new Run(text));
        if (value != null)
        {
            var run = new Run(value) { FontStretch = FontStretches.SemiCondensed };
            run.SetResourceReference(TextElement.FontFamilyProperty, "Readout");
            run.SetResourceReference(TextElement.ForegroundProperty, valueKey);
            block.Inlines.Add(run);
        }
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        NetTrace.Children.Add(block);
    }
}
