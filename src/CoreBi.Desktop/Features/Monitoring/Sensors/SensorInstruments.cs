using System.Windows;
using System.Windows.Media;

namespace CoreBi.Desktop;

/// <summary>
/// Faixa de um sensor: trilho fino de 0 ao topo da escala, o trecho entre o mínimo e o máximo da sessão aceso na cor do
/// canal (verde-água, cobre ou aço) e um traço na leitura atual (âmbar ou vermelho quando a temperatura passa do limite).
/// </summary>
public sealed class SensorSpan : EnergyInstrument
{
    public static readonly DependencyProperty ValueProperty = Number(nameof(Value), typeof(SensorSpan));
    public static readonly DependencyProperty MinProperty = Number(nameof(Min), typeof(SensorSpan));
    public static readonly DependencyProperty MaxProperty = Number(nameof(Max), typeof(SensorSpan));
    public static readonly DependencyProperty ChannelProperty =
        DependencyProperty.Register(nameof(Channel), typeof(int), typeof(SensorSpan), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(int), typeof(SensorSpan), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Min { get => (double)GetValue(MinProperty); set => SetValue(MinProperty, value); }
    public double Max { get => (double)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public int Channel { get => (int)GetValue(ChannelProperty); set => SetValue(ChannelProperty, value); }
    public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    public SensorSpan() => Height = 12;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, cy = Math.Round(ActualHeight / 2);
        if (w < 20) return;
        double X(double f) => Math.Round(Math.Clamp(f, 0, 1) * (w - 2)) + 1;
        var channel = Channel switch { 0 => AccentBrush, 1 => CopperBrush, _ => SteelBrush };
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, cy - 1, w, 2), 1, 1);
        if (!double.IsNaN(Min) && !double.IsNaN(Max))
            dc.DrawRoundedRectangle(Alpha(channel, 0x66), null, new Rect(X(Min) - 1, cy - 2, Math.Max(2, X(Max) - X(Min) + 2), 4), 2, 2);
        if (!double.IsNaN(Value))
        {
            var tick = Level switch { 2 => DangerBrush, 1 => WarnBrush, _ => channel };
            dc.DrawRoundedRectangle(tick, null, new Rect(X(Value) - 1.5, cy - 5, 3, 10), 1.5, 1.5);
        }
    }
}
