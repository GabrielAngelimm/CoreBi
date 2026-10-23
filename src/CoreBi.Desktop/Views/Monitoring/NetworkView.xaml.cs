using CoreBi.Desktop.Services;
using CoreBi.Engine.Monitoring.Network;
using CoreBi.Engine.Monitoring.Storage;
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
    internal FeatureContext Context { private get; set; } = null!;
    internal IReadOnlyList<DiskHealthCard>? LastResult { get; private set; }
    private NetworkResult? _result;
    private CancellationTokenSource? _cancellation;
    private bool _running;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public NetworkView()
    {
        InitializeComponent();
        DrawDial(null, null);
    }

    // Largura da página abaixo da qual o console desce para baixo do título.
    private const double NarrowHeader = 1040;
    private bool _narrowHeader;

    private void NetHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = NetHeader.ActualWidth < NarrowHeader;
        if (narrow == _narrowHeader) return;
        _narrowHeader = narrow;
        Grid.SetRow(NetConsole, narrow ? 1 : 0);
        Grid.SetColumn(NetConsole, narrow ? 0 : 1);
        Grid.SetColumnSpan(NetConsole, narrow ? 2 : 1);
        NetConsole.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(40, 0, 0, 0);
    }

    private async void BtnNetTest_Click(object sender, RoutedEventArgs e) => await RunOrCancelAsync();

    internal async Task RunOrCancelAsync()
    {
        if (Context.IsClosing) return;
        if (_running) { _cancellation?.Cancel(); return; }
        using var workload = Context.Workloads.TryBegin("Network test");
        if (workload == null) { NetStatus.Text = "Termine o outro teste antes."; NetDetail.Text = ""; return; }

        _running = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Context.Lifetime);
        _cancellation = cancellation;
        Ui.SetRunning(BtnNetTest, true, "Testar a rede", "Cancelar");
        ShowResult(null);
        SetProgress(0, animate: false);
        NetMeta.Text = "Medindo agora";
        try
        {
            var result = await NetworkTest.Run(p =>
            {
                if (Context.IsClosing) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Context.IsClosing || !_running) return;
                    ShowProgress(p);
                }));
            }, cancellation.Token);
            if (Context.IsClosing) return;
            LastResult = new[] { result.Card };
            ShowResult(result);
            NetStatus.Text = $"Concluído às {result.TestedAt:HH:mm:ss}";
            NetDetail.Text = "";
            SetStage(NetworkPhase.Done);
            SetProgress(1, animate: true);
        }
        catch (OperationCanceledException)
        {
            if (!Context.IsClosing) Reset("Teste cancelado", "");
        }
        catch (Exception ex)
        {
            if (!Context.IsClosing) Reset("O teste falhou", ex.Message);
        }
        finally
        {
            _running = false;
            _cancellation = null;
            if (!Context.IsClosing) Ui.SetRunning(BtnNetTest, false, LastResult != null ? "Testar de novo" : "Testar a rede");
        }
    }

    private void Reset(string status, string detail)
    {
        NetStatus.Text = status;
        NetDetail.Text = detail;
        SetStage(NetworkPhase.Preparing);
        SetProgress(0, animate: false);
        ShowResult(null);
        NetMeta.Text = "Sem resultado";
    }

    // ---------------- Progresso ----------------

    private void ShowProgress(NetworkProgress p)
    {
        NetStatus.Text = p.Text;
        NetDetail.Text = "";
        SetStage(p.Phase);
        SetProgress(p.Fraction, animate: true);
        // Leituras parciais: só o que já foi medido; o restante continua com travessão.
        SetReadout(NetLatency, p.LatencyMs);
        SetReadout(NetDown, p.DownloadMbps);
        SetReadout(NetUp, p.UploadMbps);
        NetLatencyCaption.Text = p.LatencyMs != null ? "ms até a internet" : "ms";
        DrawDial(p.DownloadMbps, p.UploadMbps);
    }

    /// <summary>
    /// A barra só anda quando o motor informa trabalho feito, e cada passo desliza até o novo valor com desaceleração
    /// (sem animação contínua). Recomeço e "Mostrar animações no Windows" desligado vão direto ao valor.
    /// </summary>
    private void SetProgress(double fraction, bool animate)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (!animate || !SystemParameters.ClientAreaAnimation || fraction < NetProgress.ScaleX)
        {
            NetProgress.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            NetProgress.ScaleX = fraction;
        }
        else
        {
            NetProgress.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(fraction, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        bool started = fraction > 0 || _running;
        NetPercent.Text = started ? (fraction * 100).ToString("0", PtBr) : "—";
        NetPercent.SetResourceReference(TextBlock.ForegroundProperty, started ? "Fg" : "Faint");
        NetPercentUnit.Visibility = started ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStage(NetworkPhase phase)
    {
        int current = phase switch
        {
            NetworkPhase.Latency => 1,
            NetworkPhase.Download => 2,
            NetworkPhase.Upload => 3,
            NetworkPhase.Analysis or NetworkPhase.Done => 4,
            _ => 0,
        };
        var stages = new[] { (NetStage1, NetStage1Check), (NetStage2, NetStage2Check), (NetStage3, NetStage3Check) };
        for (int i = 0; i < stages.Length; i++)
        {
            var (label, check) = stages[i];
            int n = i + 1;
            label.SetResourceReference(TextBlock.ForegroundProperty, n < current ? "Muted" : n == current ? "Accent" : "Faint");
            label.FontWeight = n == current ? FontWeights.SemiBold : FontWeights.Normal;
            check.Visibility = n < current ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---------------- Resultado ----------------

    private static string Format(double v) => v.ToString(v >= 100 ? "0" : "0.#", PtBr);

    private static void SetReadout(TextBlock block, double? value)
    {
        block.Text = value is { } v ? Format(v) : "—";
        block.SetResourceReference(TextBlock.ForegroundProperty, value != null ? "Fg" : "Faint");
    }

    private static string VerdictKey(DiskVerdict v) => v switch
    {
        DiskVerdict.Good => "Ok",
        DiskVerdict.Warning => "Warn",
        DiskVerdict.Critical => "Danger",
        _ => "Faint",
    };

    private static string SoftKey(string key) => key switch
    {
        "Ok" => "OkSoft",
        "Warn" => "WarnSoft",
        "Danger" => "DangerSoft",
        _ => "Raised",
    };

    private void ShowResult(NetworkResult? r)
    {
        _result = r;
        SetReadout(NetDown, r?.DownloadMbps);
        SetReadout(NetUp, r?.UploadMbps);
        SetReadout(NetLatency, r?.LatencyMs);
        NetLatencyCaption.Text = r?.LatencyMs != null ? "ms até a internet" : "ms";
        DrawDial(r?.DownloadMbps, r?.UploadMbps);

        SetReadout(NetJitter, r?.JitterMs);
        NetLoss.Text = r?.LossPct is { } loss ? loss.ToString("0.#", PtBr) : "—";
        NetLoss.SetResourceReference(TextBlock.ForegroundProperty, r?.LossPct == null ? "Faint" : r.LossPct > 1 ? "Warn" : "Fg");
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
            string key = VerdictKey(r.Card.Verdict == DiskVerdict.Unknown ? DiskVerdict.Warning : r.Card.Verdict);
            NetBadge.Visibility = Visibility.Visible;
            NetBadge.SetResourceReference(Border.BorderBrushProperty, key);
            NetBadge.SetResourceReference(Border.BackgroundProperty, SoftKey(key));
            NetBadgeText.SetResourceReference(TextBlock.ForegroundProperty, key);
            NetBadgeText.Text = new SentenceCaseConverter().Convert(r.Card.VerdictText, typeof(string), null!, PtBr) as string ?? "";
            ShowNotes(r, key);
        }

        ShowRoute(r);
        DrawTrace();
        if (r != null) ShowAdapter(r.Adapter);
    }

    private void ShowNotes(NetworkResult r, string key)
    {
        NetNotes.Children.Clear();
        bool good = r.Card.Verdict == DiskVerdict.Good;
        foreach (string note in r.Notes)
        {
            var row = new Grid { Margin = new Thickness(0, NetNotes.Children.Count == 0 ? 0 : 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            // Achado informativo (roteador que não responde a ping) não leva a cor do veredito.
            bool info = note.StartsWith("O roteador não responde", StringComparison.Ordinal);
            string noteKey = info ? "Steel" : key;
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            dot.SetResourceReference(Border.BackgroundProperty, info ? "Raised" : SoftKey(key));
            var glyph = new TextBlock
            {
                Text = good ? "" : info ? "" : "",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
            glyph.SetResourceReference(TextBlock.ForegroundProperty, noteKey);
            dot.Child = glyph;
            var text = new TextBlock { Text = note, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) };
            text.SetResourceReference(StyleProperty, "Body");
            Grid.SetColumn(text, 1);
            row.Children.Add(dot);
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
            DiskVerdict.Good => $"estável · jitter {Format(router!.Jitter)} ms",
            DiskVerdict.Warning => $"instável · perda {router!.LossPct.ToString("0.#", PtBr)} % · jitter {Format(router.Jitter)} ms",
            DiskVerdict.Critical => "sem conexão",
            _ => "não responde a ping",
        };
        string wanText = r.InternetVerdict switch
        {
            DiskVerdict.Good => $"estável · perda {r.LossPct?.ToString("0.#", PtBr) ?? "0"} %",
            DiskVerdict.Warning => r.LossPct > 1 ? $"perda de {r.LossPct?.ToString("0.#", PtBr)} %"
                                 : r.LatencyMs > 80 ? "latência alta" : "jitter alto",
            DiskVerdict.Critical => r.LatencyMs == null ? "sem resposta" : r.LossPct > 5 ? $"perda de {r.LossPct?.ToString("0.#", PtBr)} %" : "latência muito alta",
            _ => "sem resposta a ping",
        };
        string lk = VerdictKey(r.LocalVerdict), wk = VerdictKey(r.InternetVerdict);
        SetLink(NetLinkLocal, NetLinkLocalText, lk == "Faint" ? "LineStrong" : lk, lk == "Faint" ? "Muted" : lk, local);
        SetLink(NetLinkWan, NetLinkWanText, wk == "Faint" ? "LineStrong" : wk, wk == "Faint" ? "Muted" : wk, wanText);
    }

    private static void SetLink(Border line, TextBlock caption, string lineKey, string textKey, string text)
    {
        line.SetResourceReference(Border.BackgroundProperty, lineKey);
        caption.SetResourceReference(TextBlock.ForegroundProperty, textKey);
        caption.Text = text;
    }

    // ---------------- Conexão (lida sem tráfego, também antes do teste) ----------------

    private async void NetworkView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible || _running || _result != null || Context == null || Context.IsClosing) return;
        var adapter = await Task.Run(NetworkTest.ReadAdapter);
        if (Context.IsClosing || _running || _result != null) return;
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
        NetInfo.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("Adaptador", a.Name),
            new("Placa", a.Description),
            new("Tipo", a.Kind),
            new("Velocidade do link", a.LinkText),
            new("IP local", a.LocalIp),
            new("Gateway", a.Gateway.ToString()),
            new("Servidor DNS", a.Dns),
            new("Endereço físico", a.Mac),
        };
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

    // ---------------- Mostrador ----------------

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
        DrawSide(-1, down, "Accent");
        DrawSide(1, up, "Copper");
    }

    private void DrawSide(int dir, double? value, string channel)
    {
        double lit = value == null ? -1 : DialFraction(value);
        var on = new StreamGeometry();
        var off = new StreamGeometry();
        using (var onCtx = on.Open())
        using (var offCtx = off.Open())
        {
            for (int i = 0; i < DialSegments; i++)
            {
                double a0 = DialAngle(dir, (double)i / DialSegments), a1 = DialAngle(dir, (i + 0.7) / DialSegments);
                var ctx = (i + 0.5) / DialSegments <= lit ? onCtx : offCtx;
                var outer = dir < 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                var inner = dir < 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
                ctx.BeginFigure(DialPoint(a0, DialR1), true, true);
                ctx.LineTo(DialPoint(a0, DialR2), false, false);
                ctx.ArcTo(DialPoint(a1, DialR2), new Size(DialR2, DialR2), 0, false, outer, false, false);
                ctx.LineTo(DialPoint(a1, DialR1), false, false);
                ctx.ArcTo(DialPoint(a0, DialR1), new Size(DialR1, DialR1), 0, false, inner, false, false);
            }
        }
        on.Freeze(); off.Freeze();
        var offPath = new Path { Data = off };
        offPath.SetResourceReference(Shape.FillProperty, "Line");
        var onPath = new Path { Data = on };
        onPath.SetResourceReference(Shape.FillProperty, channel);
        NetDial.Children.Add(offPath);
        NetDial.Children.Add(onPath);
    }

    // ---------------- Traço de pings ----------------

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

        const double top = 20, bottom = 82, gapW = 18;
        double peak = idle.Concat(loaded).Where(v => v != null).Select(v => v!.Value).DefaultIfEmpty(10).Max();
        double max = new[] { 10.0, 20, 50, 100, 200, 500, 1000, 2000 }.FirstOrDefault(s => s >= peak * 1.1, 2000);
        int n = idle.Count + loaded.Count;
        double step = (w - (loaded.Count > 0 && idle.Count > 0 ? gapW : 0)) / n, bar = Math.Max(1.5, step * 0.62);

        AddTraceLine(0, w, top, "Line", dashed: true);
        AddTraceLine(0, w, bottom, "Line", dashed: false);
        AddTraceText($"{max:0} ms", w - 40, 0, "Faint", null, readout: true, align: TextAlignment.Right, width: 40);

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
        AddTracePath(loadGeo, "Accent");
        AddTracePath(lostGeo, "Danger");

        var idleRtts = idle.Where(v => v != null).Select(v => v!.Value).ToList();
        if (idle.Count > 0)
            AddTraceText("Em repouso · média ", 0, bottom + 4, "Muted", idleRtts.Count > 0 ? $"{idleRtts.Average():0} ms" : "—", readout: false);
        if (loaded.Count > 0)
        {
            double xs = idle.Count * step + (idle.Count > 0 ? gapW : 0);
            if (idle.Count > 0) AddTraceLine(xs - gapW / 2, xs - gapW / 2, top, "LineStrong", dashed: true, vertical: bottom);
            AddTraceText("Durante o download · média ", xs, bottom + 4, "Muted",
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
