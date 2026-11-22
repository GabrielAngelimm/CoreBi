using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop;

/// <summary>Forma do esqueleto: linhas de tabela (nome à esquerda, valor à direita), parágrafos ou uma área de gráfico.</summary>
public enum SkeletonShape { Rows, Text, Block }

/// <summary>
/// Esqueleto de carregamento de um painel (Border): enquanto <c>Skeleton.Active</c> é verdadeiro, uma camada no fundo do próprio
/// painel cobre o conteúdo e mostra barras no tom dos fios no lugar dos dados, com um pulso lento de opacidade. A moldura do
/// painel e o que fica acima de <c>Skeleton.Top</c> (o título fixo) continuam à vista, então só o que falta ler fica em espera.
/// Ao desligar, a camada some em 200 ms e o conteúdo aparece embaixo dela, sem salto: as barras têm a altura das linhas reais.
/// A camada fica dentro do painel, então entra junto com a página (opacidade e deslocamento) e some quando a página some.
/// Sem degradê nem brilho correndo: o pulso é o único movimento e só existe enquanto há algo carregando.
/// Respeita "Mostrar animações no Windows".
/// </summary>
public static class Skeleton
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active", typeof(bool), typeof(Skeleton), new FrameworkPropertyMetadata(false, OnActiveChanged));
    public static bool GetActive(DependencyObject d) => (bool)d.GetValue(ActiveProperty);
    public static void SetActive(DependencyObject d, bool value) => d.SetValue(ActiveProperty, value);

    public static readonly DependencyProperty ShapeProperty = DependencyProperty.RegisterAttached(
        "Shape", typeof(SkeletonShape), typeof(Skeleton), new FrameworkPropertyMetadata(SkeletonShape.Rows, Redraw));
    public static SkeletonShape GetShape(DependencyObject d) => (SkeletonShape)d.GetValue(ShapeProperty);
    public static void SetShape(DependencyObject d, SkeletonShape value) => d.SetValue(ShapeProperty, value);

    /// <summary>Altura de cada linha do esqueleto (a mesma das linhas reais do painel, para nada pular na troca).</summary>
    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.RegisterAttached(
        "RowHeight", typeof(double), typeof(Skeleton), new FrameworkPropertyMetadata(32.0, Redraw));
    public static double GetRowHeight(DependencyObject d) => (double)d.GetValue(RowHeightProperty);
    public static void SetRowHeight(DependencyObject d, double value) => d.SetValue(RowHeightProperty, value);

    /// <summary>Altura do topo do conteúdo que fica à vista (título fixo do painel); o esqueleto começa abaixo dela.</summary>
    public static readonly DependencyProperty TopProperty = DependencyProperty.RegisterAttached(
        "Top", typeof(double), typeof(Skeleton), new FrameworkPropertyMetadata(0.0, Redraw));
    public static double GetTop(DependencyObject d) => (double)d.GetValue(TopProperty);
    public static void SetTop(DependencyObject d, double value) => d.SetValue(TopProperty, value);

    /// <summary>
    /// Planta do painel, desenhada no lugar das formas genéricas para o esqueleto ter a estrutura exata do conteúdo
    /// (<see cref="SkeletonPlan"/>). Vazia, vale <see cref="ShapeProperty"/>.
    /// </summary>
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.RegisterAttached(
        "Layout", typeof(string), typeof(Skeleton), new FrameworkPropertyMetadata("", Redraw));
    public static string GetLayout(DependencyObject d) => (string)d.GetValue(LayoutProperty);
    public static void SetLayout(DependencyObject d, string value) => d.SetValue(LayoutProperty, value);

    /// <summary>Chave do estilo dos itens que já existem sem dados (cartões); cada um ganha contorno e <see cref="ItemLayoutProperty"/>.</summary>
    public static readonly DependencyProperty ItemStyleProperty = DependencyProperty.RegisterAttached(
        "ItemStyle", typeof(string), typeof(Skeleton), new FrameworkPropertyMetadata("", Redraw));
    public static string GetItemStyle(DependencyObject d) => (string)d.GetValue(ItemStyleProperty);
    public static void SetItemStyle(DependencyObject d, string value) => d.SetValue(ItemStyleProperty, value);

    /// <summary>Planta de dentro de cada item (<see cref="ItemStyleProperty"/>), com <c>pad</c> para o recuo dele.</summary>
    public static readonly DependencyProperty ItemLayoutProperty = DependencyProperty.RegisterAttached(
        "ItemLayout", typeof(string), typeof(Skeleton), new FrameworkPropertyMetadata("", Redraw));
    public static string GetItemLayout(DependencyObject d) => (string)d.GetValue(ItemLayoutProperty);
    public static void SetItemLayout(DependencyObject d, string value) => d.SetValue(ItemLayoutProperty, value);

    private static readonly DependencyProperty LayerProperty = DependencyProperty.RegisterAttached(
        "Layer", typeof(SkeletonLayer), typeof(Skeleton));

    /// <summary>Liga ou desliga o esqueleto de vários painéis de uma vez.</summary>
    public static void Set(bool active, params Border[] panels)
    {
        foreach (var p in panels) SetActive(p, active);
    }

    private static bool Animate => SystemParameters.ClientAreaAnimation;

    private static void Redraw(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d.GetValue(LayerProperty) is not SkeletonLayer layer) return;
        layer.InvalidateMeasure();
        layer.InvalidateVisual();
    }

    private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border) return;
        var layer = border.GetValue(LayerProperty) as SkeletonLayer;
        if ((bool)e.NewValue)
        {
            layer ??= Inject(border);
            layer?.Show();
        }
        else layer?.Hide(Animate && border.IsVisible);
    }

    /// <summary>
    /// Põe a camada no painel uma vez só: o filho do Border passa a dividir uma grade com ela (a camada por cima). Os nomes do
    /// XAML continuam apontando para os mesmos elementos.
    /// </summary>
    private static SkeletonLayer? Inject(Border border)
    {
        if (border.Child is not { } child) return null;
        var layer = new SkeletonLayer(border);
        border.Child = null;
        var host = new Grid();
        host.Children.Add(child);
        host.Children.Add(layer);
        border.Child = host;
        border.SetValue(LayerProperty, layer);
        return layer;
    }

    /// <summary>A camada do esqueleto: cobre o conteúdo com o fundo do painel e desenha as barras.</summary>
    private sealed class SkeletonLayer : FrameworkElement
    {
        // Larguras das barras: variadas mas fixas, para o esqueleto parecer dados e não sortear formas novas a cada vez
        private static readonly double[] Widths = { 0.62, 0.84, 0.48, 0.74, 0.56, 0.80, 0.42, 0.70, 0.52, 0.66 };

        private static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            "Fill", typeof(Brush), typeof(SkeletonLayer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly DependencyProperty BarProperty = DependencyProperty.Register(
            "Bar", typeof(Brush), typeof(SkeletonLayer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly DependencyProperty StrongProperty = DependencyProperty.Register(
            "Strong", typeof(Brush), typeof(SkeletonLayer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private readonly Border _border;
        private int _version;
        private AnimationClock? _pulse;

        public SkeletonLayer(Border border)
        {
            _border = border;
            Visibility = Visibility.Collapsed;
            // Cores pelo tema: o fundo é o do painel; as barras, os fios
            SetBinding(FillProperty, new Binding(nameof(Border.Background)) { Source = border });
            SetResourceReference(BarProperty, "Line");
            SetResourceReference(StrongProperty, "LineStrong");
        }

        public void Show()
        {
            _version++;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            Visibility = Visibility.Visible;
            IsHitTestVisible = true; // o conteúdo embaixo ainda não vale: nada de clicar no que não aparece
            _pulse?.Controller?.Stop();
            _pulse = null;
            if (Animate)
            {
                // Pulso lento (1 → 0,55 → 1, 1,8 s) só nas barras, depois de um instante parado: carregamento curto nem chega a
                // pulsar. O fundo que cobre o conteúdo fica sempre opaco (pulsar a camada inteira deixava o conteúdo de baixo
                // aparecer como um fantasma)
                _pulse = new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(900))
                {
                    BeginTime = TimeSpan.FromMilliseconds(500),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                }.CreateClock();
            }
            InvalidateVisual();
        }

        private void StopPulse()
        {
            _pulse?.Controller?.Stop();
            _pulse = null;
        }

        public void Hide(bool animate)
        {
            int version = ++_version;
            IsHitTestVisible = false;
            if (!animate)
            {
                BeginAnimation(OpacityProperty, null);
                Visibility = Visibility.Collapsed;
                StopPulse();
                return;
            }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (_, _) =>
            {
                if (version != _version) return; // ligou de novo durante o fade
                BeginAnimation(OpacityProperty, null);
                Visibility = Visibility.Collapsed;
                StopPulse();
            };
            BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>
        /// Com planta, a camada pede a altura dela: o painel ainda vazio (lista sem itens, ficha sem linhas) já fica do tamanho que
        /// vai ter com os dados, e nada embaixo dele pula quando o esqueleto sai.
        /// </summary>
        protected override Size MeasureOverride(Size availableSize)
        {
            string plan = GetLayout(_border);
            return string.IsNullOrWhiteSpace(plan) ? default : new Size(0, GetTop(_border) + SkeletonPlan.Height(plan));
        }

        /// <summary>
        /// Cada item já montado no estilo pedido (os cartões de Instalar apps existem antes de saber o que está instalado) ganha o
        /// contorno dele e a planta de dentro, no lugar exato em que está.
        /// </summary>
        private void DrawItems(DrawingContext dc, string key, Brush bar, Brush strong)
        {
            if (_border.TryFindResource(key) is not Style style || ((Grid)Parent).Children[0] is not Visual content) return;
            string plan = GetItemLayout(_border);
            var outline = new Pen(bar, 1);
            var stack = new Stack<DependencyObject>();
            stack.Push(content);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node is FrameworkElement { IsVisible: true } item && ReferenceEquals(item.Style, style))
                {
                    var bounds = item.TransformToVisual(this).TransformBounds(new Rect(item.RenderSize));
                    dc.DrawRoundedRectangle(null, outline, new Rect(bounds.X + 0.5, bounds.Y + 0.5, Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1)), 6, 6);
                    if (plan.Length > 0) SkeletonPlan.Draw(dc, plan, bounds, bar, strong);
                    continue;
                }
                for (int i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(node, i));
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double top = Math.Min(h, GetTop(_border));
            var fill = (Brush?)GetValue(FillProperty) ?? Brushes.Transparent;
            var bar = (Brush?)GetValue(BarProperty) ?? Brushes.Gray;
            var strong = (Brush?)GetValue(StrongProperty) ?? Brushes.Gray;
            // A camada cobre também o recuo do painel (sem passar da moldura), para nada do conteúdo vazar pelas bordas
            var pad = _border.Padding;
            // Sem cabeçalho preservado (Top 0), cobre também o recuo de cima: o acento de um título (o "ú" de "últimos") passa do
            // topo do conteúdo e vazava como um pontinho
            double coverTop = top <= 0 ? -pad.Top : top;
            var cover = new Rect(-pad.Left, coverTop, w + pad.Left + pad.Right, Math.Max(0, h - coverTop + pad.Bottom));
            dc.PushClip(new RectangleGeometry(new Rect(-pad.Left, -pad.Top, w + pad.Left + pad.Right, h + pad.Top + pad.Bottom),
                Math.Max(0, _border.CornerRadius.BottomLeft - 1), Math.Max(0, _border.CornerRadius.BottomLeft - 1)));
            dc.DrawRectangle(fill, null, cover);
            dc.Pop();

            string plan = GetLayout(_border), itemKey = GetItemStyle(_border);
            if (!string.IsNullOrWhiteSpace(plan) || itemKey.Length > 0)
            {
                var box = new Rect(0, top, w, Math.Max(0, h - top));
                dc.PushClip(new RectangleGeometry(new Rect(-pad.Left, top, w + pad.Left + pad.Right, Math.Max(0, h - top + pad.Bottom))));
                dc.PushOpacity(1, _pulse);
                if (!string.IsNullOrWhiteSpace(plan)) SkeletonPlan.Draw(dc, plan, box, bar, strong);
                if (itemKey.Length > 0) DrawItems(dc, itemKey, bar, strong);
                dc.Pop();
                dc.Pop();
                return;
            }

            // Painel sem recuo próprio (Padding 0, o conteúdo traz o dele): as barras ficam no recuo padrão dos painéis
            var inset = pad == default ? new Thickness(24) : default;
            double y0 = top + (top > 0 ? 8 : 0) + inset.Top;
            var area = new Rect(inset.Left, y0, Math.Max(0, w - inset.Left - inset.Right), Math.Max(0, h - y0 - inset.Bottom));
            if (area.Width < 24 || area.Height < 10) return;
            dc.PushClip(new RectangleGeometry(area));
            dc.PushOpacity(1, _pulse);
            switch (GetShape(_border))
            {
                case SkeletonShape.Block:
                    dc.DrawRoundedRectangle(bar, null, area, 8, 8);
                    break;
                case SkeletonShape.Text:
                    {
                        if (top <= 0) dc.DrawRoundedRectangle(strong, null, new Rect(area.X, area.Y + 2, Math.Min(area.Width * 0.6, 180), 14), 7, 7);
                        int i = 0;
                        for (double y = area.Y + (top <= 0 ? 34 : 4); y + 10 <= area.Bottom; y += 24, i++)
                            dc.DrawRoundedRectangle(bar, null, new Rect(area.X, y, area.Width * Widths[i % Widths.Length], 10), 5, 5);
                        break;
                    }
                default:
                    {
                        double row = Math.Max(20, GetRowHeight(_border));
                        int i = 0;
                        for (double y = area.Y; y + row <= area.Bottom + 0.5; y += row, i++)
                        {
                            double mid = y + row / 2;
                            if (i > 0) dc.DrawRectangle(bar, null, new Rect(area.X, Math.Round(y), area.Width, 1));
                            double name = Math.Min(area.Width * 0.45, 300) * Widths[i % Widths.Length] + 36;
                            dc.DrawRoundedRectangle(bar, null, new Rect(area.X + 4, mid - 5, Math.Min(name, area.Width * 0.6), 10), 5, 5);
                            double value = 40 + 20 * Widths[(i + 3) % Widths.Length];
                            if (area.Width > 240) dc.DrawRoundedRectangle(strong, null, new Rect(area.Right - 4 - value, mid - 5, value, 10), 5, 5);
                        }
                        break;
                    }
            }
            dc.Pop();
            dc.Pop();
        }
    }
}
