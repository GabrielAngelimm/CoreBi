using System.Windows;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>
/// Histórico curto de um instrumento da placa de vídeo: área chapada e traço no canal Gpu sobre uma linha de base, sem eixos.
/// Amostras nulas (sem leitura ou página escondida) interrompem o traço. Sem pulso: são sete instrumentos lado a lado e o app fica
/// aberto em jogos, então nada se mexe além do próprio valor.
/// </summary>
public sealed class GpuTrace : InstrumentBase
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(nameof(Samples), typeof(IReadOnlyList<float?>), typeof(GpuTrace),
        new FrameworkPropertyMetadata(Array.Empty<float?>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaxProperty = Number(nameof(Max), typeof(GpuTrace), 100);

    public IReadOnlyList<float?> Samples { get => (IReadOnlyList<float?>)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public double Max { get => (double)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }

    public GpuTrace()
    {
        SetResourceReference(ChannelBrushProperty, "Gpu");
        Height = 40;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 10) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        double baseY = Math.Round(h) - 0.5;
        dc.DrawLine(Stroke(TrackBrush, 1), new Point(0, baseY), new Point(w, baseY));

        var samples = Samples ?? Array.Empty<float?>();
        double max = double.IsNaN(Max) || Max <= 0 ? 1 : Max;
        double X(int i) => (w - 3) * (i + GpuBoard.Window - samples.Count) / (GpuBoard.Window - 1);
        double Y(double v) => 2 + (baseY - 3) * (1 - Math.Clamp(v / max, 0, 1));

        var fill = Alpha(ChannelBrush, 0x24);
        var pen = new Pen(ChannelBrush, 1.5) { LineJoin = PenLineJoin.Round };
        pen.Freeze();

        var run = new List<Point>();
        void Flush()
        {
            if (run.Count >= 2)
            {
                dc.DrawGeometry(fill, null, CreateAreaGeometry(run, baseY));
                dc.DrawGeometry(null, pen, CreatePolylineGeometry(run));
            }
            run.Clear();
        }
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i] is float v) run.Add(new Point(X(i), Y(v)));
            else Flush();
        }
        Flush();
        if (samples.Count > 0 && samples[^1] is float last)
            dc.DrawEllipse(ChannelBrush, null, new Point(X(samples.Count - 1), Y(last)), 2.5, 2.5);
    }
}
