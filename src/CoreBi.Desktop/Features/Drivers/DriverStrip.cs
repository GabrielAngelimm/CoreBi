using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CoreBi.Engine.Tools.Drivers;

namespace CoreBi.Desktop;

/// <summary>
/// A faixa do backup e da restauração: uma célula por driver (com muitos drivers, cada célula junta alguns, para nenhuma ficar com
/// menos de 4 px), apagada no trilho da placa e acesa no sinal quando o driver passa. A frente acende em vez de pular: a contagem
/// desliza até o número novo e a célula da vez sobe de 0 a 100 % junto. Na restauração cada célula ganha a cor do que aconteceu
/// (instalado no sinal, já em dia no sinal a meia força, com erro em vermelho) e a da vez fica marcada de leve. Ao terminar, uma
/// varredura da esquerda para a direita passa as células para o verde de concluído: é a confirmação.
/// </summary>
public sealed class DriverStrip : InstrumentBase
{
    public static readonly DependencyProperty ShownProperty = Number(nameof(Shown), typeof(DriverStrip), 0);
    public static readonly DependencyProperty SettleProperty = Number(nameof(Settle), typeof(DriverStrip), 0);
    public static readonly DependencyProperty OkBrushProperty = DependencyProperty.Register(nameof(OkBrush), typeof(Brush), typeof(DriverStrip),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Quantos drivers aparecem acesos (fracionário durante o deslize).</summary>
    public double Shown { get => (double)GetValue(ShownProperty); set => SetValue(ShownProperty, value); }

    /// <summary>A varredura de concluído, de 0 a 1.</summary>
    public double Settle { get => (double)GetValue(SettleProperty); set => SetValue(SettleProperty, value); }

    public Brush OkBrush { get => (Brush)GetValue(OkBrushProperty); set => SetValue(OkBrushProperty, value); }

    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    private DriverRestoreOutcome?[] _marks = [];
    private int _total, _current = -1;

    public DriverStrip()
    {
        Height = 10;
        SetResourceReference(TrackBrushProperty, "PlateTrack");
        SetResourceReference(OkBrushProperty, "Ok");
    }

    public int Total => _total;

    /// <summary>Recomeça com <paramref name="total"/> células apagadas.</summary>
    public void Reset(int total)
    {
        _total = Math.Max(0, total);
        _marks = new DriverRestoreOutcome?[_total];
        _current = -1;
        BeginAnimation(ShownProperty, null);
        BeginAnimation(SettleProperty, null);
        Shown = 0;
        Settle = 0;
        InvalidateVisual();
    }

    /// <summary>Leva a frente até <paramref name="done"/> drivers, deslizando a partir de onde ela está.</summary>
    public void SetDone(int done, bool animate = true)
    {
        double to = Math.Clamp(done, 0, _total);
        if (!animate || !SystemParameters.ClientAreaAnimation || !IsVisible)
        {
            BeginAnimation(ShownProperty, null);
            Shown = to;
            return;
        }
        BeginAnimation(ShownProperty, new DoubleAnimation(Shown, to, TimeSpan.FromMilliseconds(320)) { EasingFunction = Ease });
    }

    /// <summary>A célula do driver que está sendo instalado agora (-1: nenhuma).</summary>
    public void SetCurrent(int index)
    {
        _current = index;
        InvalidateVisual();
    }

    /// <summary>O que aconteceu com o driver da posição <paramref name="index"/> (restauração).</summary>
    public void Mark(int index, DriverRestoreOutcome outcome)
    {
        if (index < 0 || index >= _marks.Length) return;
        _marks[index] = outcome;
        InvalidateVisual();
    }

    /// <summary>Terminou: a frente vai até onde chegou e, logo depois, a varredura passa tudo para o verde.</summary>
    public void Complete(bool animate = true)
    {
        _current = -1;
        if (!animate || !SystemParameters.ClientAreaAnimation || !IsVisible)
        {
            BeginAnimation(SettleProperty, null);
            Settle = 1;
            InvalidateVisual();
            return;
        }
        BeginAnimation(SettleProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(650))
        {
            BeginTime = TimeSpan.FromMilliseconds(260),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    protected override Size MeasureOverride(Size available) => new(0, 10);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        int n = _total;
        if (n <= 0 || w < 20) return;
        const double gap = 2, minCell = 4;
        int cells = n;
        if ((w - gap * (cells - 1)) / cells < minCell) cells = Math.Max(1, (int)Math.Floor((w + gap) / (minCell + gap)));
        double cw = (w - gap * (cells - 1)) / cells;
        var current = Alpha(SignalBrush, 0x48);
        var currentDone = Alpha(SignalBrush, 0x78);
        var okDim = Alpha(OkBrush, 0x78);

        for (int k = 0; k < cells; k++)
        {
            double a = (double)k * n / cells, b = (double)(k + 1) * n / cells;
            double x0 = Math.Round(k * (cw + gap)), x1 = Math.Round(k * (cw + gap) + cw);
            var rect = new Rect(x0, 0, Math.Max(1, x1 - x0), h);
            dc.DrawRectangle(TrackBrush, null, rect);

            int first = (int)Math.Floor(a), last = Math.Min(n - 1, (int)Math.Ceiling(b) - 1);
            bool failed = false, allCurrent = true;
            for (int i = first; i <= last; i++)
            {
                if (_marks[i] == DriverRestoreOutcome.Failed) failed = true;
                if (_marks[i] != DriverRestoreOutcome.Current) allCurrent = false;
            }
            double fill = Math.Clamp((Shown - a) / (b - a), 0, 1);
            if (fill <= 0)
            {
                if (_current >= first && _current <= last) dc.DrawRectangle(current, null, rect);
                continue;
            }
            bool settled = Settle >= (k + 0.5) / cells;
            Brush brush = failed ? DangerBrush
                : settled ? (allCurrent ? okDim : OkBrush)
                : allCurrent ? currentDone : SignalBrush;
            dc.PushOpacity(fill);
            dc.DrawRectangle(brush, null, rect);
            dc.Pop();
        }
    }
}
