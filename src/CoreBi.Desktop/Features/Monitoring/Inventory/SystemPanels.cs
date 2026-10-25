using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop.Features.Monitoring.Inventory;

/// <summary>
/// Colunas de grupos da ficha: cada grupo entra na coluna mais curta, na ordem (como uma página de catálogo),
/// então grupos curtos não deixam buraco embaixo de uma coluna enquanto a outra cresce.
/// </summary>
public sealed class ColumnsPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(nameof(Columns), typeof(int), typeof(ColumnsPanel),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ColumnGapProperty = DependencyProperty.Register(nameof(ColumnGap), typeof(double), typeof(ColumnsPanel),
        new FrameworkPropertyMetadata(44.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(nameof(RowGap), typeof(double), typeof(ColumnsPanel),
        new FrameworkPropertyMetadata(26.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public double ColumnGap { get => (double)GetValue(ColumnGapProperty); set => SetValue(ColumnGapProperty, value); }
    public double RowGap { get => (double)GetValue(RowGapProperty); set => SetValue(RowGapProperty, value); }

    private readonly List<(UIElement Child, int Column, double Y)> _slots = new();

    private double ColumnWidth(double width, int cols) => double.IsInfinity(width) ? 360 : Math.Max(0, (width - ColumnGap * (cols - 1)) / cols);

    protected override Size MeasureOverride(Size available)
    {
        int cols = Math.Max(1, Columns);
        // A largura da coluna não depende de quantos grupos há: um grupo só fica do tamanho dos outros, não estica na tela toda.
        double w = ColumnWidth(available.Width, cols);
        var heights = new double[cols];
        _slots.Clear();
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(w, double.PositiveInfinity));
            if (child.DesiredSize.Height <= 0) { _slots.Add((child, 0, 0)); continue; }
            int c = Array.IndexOf(heights, heights.Min());
            double y = heights[c] > 0 ? heights[c] + RowGap : 0;
            _slots.Add((child, c, y));
            heights[c] = y + child.DesiredSize.Height;
        }
        return new Size(double.IsInfinity(available.Width) ? w * cols + ColumnGap * (cols - 1) : available.Width, heights.Max());
    }

    protected override Size ArrangeOverride(Size final)
    {
        int cols = Math.Max(1, Columns);
        double w = ColumnWidth(final.Width, cols);
        foreach (var (child, c, y) in _slots)
            child.Arrange(new Rect(c * (w + ColumnGap), y, w, child.DesiredSize.Height));
        return final;
    }
}

/// <summary>Fila horizontal em que cada filho ocupa a fração do seu peso (unidades dentro do disco).</summary>
public sealed class WeightPanel : Panel
{
    public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached("Weight", typeof(double), typeof(WeightPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentArrange | FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static double GetWeight(DependencyObject o) => (double)o.GetValue(WeightProperty);
    public static void SetWeight(DependencyObject o, double v) => o.SetValue(WeightProperty, v);

    public double Gap { get; set; } = 6;

    /// <summary>Soma dos pesos que equivale à largura inteira (1 = o disco todo); o resto fica vazio à direita.</summary>
    public double Total { get; set; } = 1;

    protected override Size MeasureOverride(Size available)
    {
        double h = 0;
        foreach (UIElement c in InternalChildren) { c.Measure(new Size(double.IsInfinity(available.Width) ? 100 : available.Width, available.Height)); h = Math.Max(h, c.DesiredSize.Height); }
        return new Size(double.IsInfinity(available.Width) ? 0 : available.Width, h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        int n = InternalChildren.Count;
        double usable = Math.Max(0, final.Width - Gap * Math.Max(0, n - 1)), x = 0;
        double total = Math.Max(Total, InternalChildren.Cast<UIElement>().Sum(GetWeight));
        foreach (UIElement c in InternalChildren)
        {
            double w = usable * GetWeight(c) / total;
            c.Arrange(new Rect(x, 0, w, final.Height));
            x += w + Gap;
        }
        return final;
    }
}
