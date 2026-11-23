using System.Windows;

namespace CoreBi.Desktop;

/// <summary>
/// Ocupa o espaço que a página der sem pedir altura própria (além do MinHeight). Serve para um Viewbox numa linha "*":
/// sozinho, ele pediria a altura da largura toda e faria a página rolar com a janela maximizada.
/// </summary>
public sealed class FillBox : System.Windows.Controls.Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        // Mede o filho no mínimo; no arranjo um Viewbox cresce sozinho até o espaço que a página der.
        Child?.Measure(new Size(constraint.Width, Math.Min(constraint.Height, MinHeight)));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }
}

/// <summary>
/// Pilha vertical que reparte a sobra de altura igualmente entre os filhos (cada um cresce e centraliza o conteúdo),
/// em vez de deixar um vão vazio no fim do painel. Sem sobra, comporta-se como um StackPanel.
/// </summary>
public sealed class SpreadPanel : System.Windows.Controls.Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double w = 0, h = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            w = Math.Max(w, child.DesiredSize.Width);
            h += child.DesiredSize.Height;
        }
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double used = visible.Sum(c => c.DesiredSize.Height);
        double extra = visible.Count > 0 ? Math.Max(0, final.Height - used) / visible.Count : 0;
        double y = 0;
        foreach (var child in visible)
        {
            double h = child.DesiredSize.Height + extra;
            child.Arrange(new Rect(0, y, final.Width, h));
            y += h;
        }
        return final;
    }
}

/// <summary>
/// Linhas de itens que dividem a sobra de largura em vãos iguais, inclusive o vão antes do primeiro item (o último encosta na direita);
/// quando não cabem, quebram em mais linhas, cada uma distribuída do mesmo jeito.
/// </summary>
public sealed class JustifyPanel : System.Windows.Controls.Panel
{
    private List<List<UIElement>> Lines(double width)
    {
        var lines = new List<List<UIElement>> { new() };
        double x = 0;
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) continue;
            double w = c.DesiredSize.Width;
            if (lines[^1].Count > 0 && x + w > width) { lines.Add(new()); x = 0; }
            lines[^1].Add(c);
            x += w;
        }
        return lines;
    }

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement c in InternalChildren) c.Measure(new Size(double.PositiveInfinity, available.Height));
        var lines = Lines(available.Width);
        double h = lines.Sum(l => l.Count == 0 ? 0 : l.Max(c => c.DesiredSize.Height));
        double w = lines.Max(l => l.Sum(c => c.DesiredSize.Width));
        return new Size(double.IsInfinity(available.Width) ? w : Math.Min(w, available.Width), h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        foreach (var line in Lines(final.Width))
        {
            if (line.Count == 0) continue;
            double lineH = line.Max(c => c.DesiredSize.Height);
            double gap = line.Count > 1 ? Math.Max(0, (final.Width - line.Sum(c => c.DesiredSize.Width)) / line.Count) : 0;
            double x = gap;
            foreach (var c in line)
            {
                c.Arrange(new Rect(x, y, c.DesiredSize.Width, lineH));
                x += c.DesiredSize.Width + gap;
            }
            y += lineH;
        }
        return final;
    }
}

/// <summary>Grid que não pede altura aos filhos: ocupa só o espaço que sobra, para a lista decidir quantas linhas cabem.</summary>
public sealed class FitHost : System.Windows.Controls.Grid
{
    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(new Size(constraint.Width, double.PositiveInfinity));
        return new Size(double.IsInfinity(constraint.Width) ? 0 : constraint.Width, 0);
    }
}

/// <summary>
/// Cabeçalho de página, o mesmo em todas as telas. O primeiro filho é o título, o segundo (opcional) a barra de ações (com o
/// estado vivo à esquerda dos botões, na mesma linha), e o filho marcado com <see cref="SubtitleProperty"/> (opcional, em qualquer
/// posição) o subtítulo de explicação, embaixo do título. A barra fica à direita, centrada na linha do título, e nunca muda a
/// altura do cabeçalho. Quando ela não cabe ao lado do título, desce para baixo do cabeçalho, encostada à esquerda.
/// </summary>
public sealed class PageHeaderPanel : System.Windows.Controls.Panel
{
    /// <summary>Menor vão entre o título e a barra de ações; abaixo dele a barra desce.</summary>
    public double Gap { get; set; } = 40;

    /// <summary>Vão acima da barra quando ela desce para baixo do cabeçalho.</summary>
    public double WrapGap { get; set; } = 16;

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.RegisterAttached("Subtitle", typeof(bool), typeof(PageHeaderPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static bool GetSubtitle(UIElement e) => (bool)e.GetValue(SubtitleProperty);
    public static void SetSubtitle(UIElement e, bool value) => e.SetValue(SubtitleProperty, value);

    private bool _wide = true;

    private (UIElement? Title, UIElement? Bar, UIElement? Sub) Parts()
    {
        UIElement? title = null, bar = null, sub = null;
        foreach (UIElement child in InternalChildren)
        {
            if (GetSubtitle(child)) sub ??= child;
            else if (title == null) title = child;
            else bar ??= child;
        }
        return (title, bar, sub);
    }

    private static Size Desired(UIElement? e) => e?.DesiredSize ?? default;

    protected override Size MeasureOverride(Size available)
    {
        var (title, bar, sub) = Parts();
        var free = new Size(double.PositiveInfinity, double.PositiveInfinity);
        bar?.Measure(free);
        title?.Measure(free);
        Size t = Desired(title), b = Desired(bar);
        bool hasBar = b.Width > 0;
        _wide = !hasBar || double.IsInfinity(available.Width) || t.Width + Gap + b.Width <= available.Width;
        if (_wide && hasBar && !double.IsInfinity(available.Width))
            title?.Measure(new Size(Math.Max(0, available.Width - b.Width - Gap), double.PositiveInfinity));
        sub?.Measure(new Size(available.Width, double.PositiveInfinity));
        t = Desired(title);
        Size s = Desired(sub);

        double width = _wide ? Math.Max(t.Width + (hasBar ? Gap + b.Width : 0), s.Width) : Math.Max(Math.Max(t.Width, b.Width), s.Width);
        // Descida: a barra não tem a folga que a fonte deixa embaixo das letras, então ganha 6 para o primeiro painel ficar a 20 dela
        double height = _wide ? Math.Max(t.Height, b.Height) + s.Height : t.Height + s.Height + (hasBar ? WrapGap + b.Height + 6 : 0);
        return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (title, bar, sub) = Parts();
        Size t = Desired(title), b = Desired(bar), s = Desired(sub);
        if (_wide)
        {
            double row = Math.Max(t.Height, b.Height);
            double titleW = b.Width > 0 ? Math.Max(0, final.Width - b.Width - Gap) : final.Width;
            title?.Arrange(new Rect(0, (row - t.Height) / 2, Math.Min(titleW, t.Width), t.Height));
            bar?.Arrange(new Rect(Math.Max(0, final.Width - b.Width), (row - b.Height) / 2, b.Width, b.Height));
            sub?.Arrange(new Rect(0, row, final.Width, s.Height));
        }
        else
        {
            title?.Arrange(new Rect(0, 0, Math.Min(final.Width, t.Width), t.Height));
            sub?.Arrange(new Rect(0, t.Height, final.Width, s.Height));
            bar?.Arrange(new Rect(0, t.Height + s.Height + WrapGap, Math.Min(final.Width, b.Width), b.Height));
        }
        return final;
    }
}

/// <summary>
/// Uma linha de leituras de ponta a ponta: a primeira encosta na esquerda, a última na direita e os vãos entre elas são iguais,
/// então o respiro do texto até a borda do cartão é o mesmo dos dois lados (o recuo do cartão). Com um item só, ele fica à esquerda.
/// </summary>
public sealed class EdgeRowPanel : System.Windows.Controls.Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double w = 0, h = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            if (child.Visibility == Visibility.Collapsed) continue;
            w += child.DesiredSize.Width;
            h = Math.Max(h, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(available.Width) ? w : Math.Min(w, available.Width), h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double used = visible.Sum(c => c.DesiredSize.Width);
        double gap = visible.Count > 1 ? Math.Max(0, final.Width - used) / (visible.Count - 1) : 0;
        double x = 0;
        foreach (var child in visible)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, final.Height));
            x += child.DesiredSize.Width + gap;
        }
        return final;
    }
}

/// <summary>
/// Fila de fichas numa linha só (a faixa "No pacote" de Instalar apps): põe as fichas da esquerda para a direita enquanto couberem
/// e, quando não cabem todas, o último filho (o indicador "+N") logo depois da última que coube. <see cref="HiddenCount"/> diz
/// quantas ficaram de fora, para o indicador mostrar. Pede só a largura usada, então o que vem depois fica colado à fila.
/// </summary>
public sealed class OverflowStrip : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(OverflowStrip), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    public static readonly DependencyProperty HiddenCountProperty = DependencyProperty.Register(
        nameof(HiddenCount), typeof(int), typeof(OverflowStrip), new PropertyMetadata(0));
    /// <summary>Fichas que não couberam (o indicador mostra "+N").</summary>
    public int HiddenCount { get => (int)GetValue(HiddenCountProperty); private set => SetValue(HiddenCountProperty, value); }

    private int _shown;

    // O que fica de fora é posto além da borda e cortado aqui (arranjar com tamanho zero ainda deixaria o desenho vazar)
    public OverflowStrip() => ClipToBounds = true;

    private static void Hide(UIElement child, Size final) =>
        child.Arrange(new Rect(final.Width + 10000, 0, child.DesiredSize.Width, child.DesiredSize.Height));

    protected override Size MeasureOverride(Size available)
    {
        int n = InternalChildren.Count - 1;
        if (n < 0) return default;
        var any = new Size(double.PositiveInfinity, available.Height);
        foreach (UIElement child in InternalChildren) child.Measure(any);
        var indicator = InternalChildren[n];
        double used = 0, height = 0;
        int shown = 0;
        for (; shown < n; shown++)
        {
            var size = InternalChildren[shown].DesiredSize;
            double next = used + (shown > 0 ? Spacing : 0) + size.Width;
            // Se esta não é a última, precisa sobrar lugar para o indicador
            double reserve = shown < n - 1 ? Spacing + indicator.DesiredSize.Width : 0;
            if (next + reserve > available.Width && !(shown == n - 1 && next <= available.Width)) break;
            used = next;
            height = Math.Max(height, size.Height);
        }
        _shown = shown;
        int hidden = n - shown;
        if (HiddenCount != hidden) HiddenCount = hidden;
        if (hidden > 0)
        {
            used += (shown > 0 ? Spacing : 0) + indicator.DesiredSize.Width;
            height = Math.Max(height, indicator.DesiredSize.Height);
        }
        return new Size(used, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        int n = InternalChildren.Count - 1;
        double x = 0;
        for (int i = 0; i < n; i++)
        {
            var child = InternalChildren[i];
            if (i >= _shown) { Hide(child, final); continue; }
            var size = child.DesiredSize;
            child.Arrange(new Rect(x, (final.Height - size.Height) / 2, size.Width, size.Height));
            x += size.Width + Spacing;
        }
        if (n >= 0)
        {
            var indicator = InternalChildren[n];
            if (n - _shown > 0)
                indicator.Arrange(new Rect(x, (final.Height - indicator.DesiredSize.Height) / 2, indicator.DesiredSize.Width, indicator.DesiredSize.Height));
            else Hide(indicator, final);
        }
        return final;
    }
}
