using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CoreBi.Desktop;

/// <summary>Forma do esqueleto: linhas de tabela (nome à esquerda, valor à direita), parágrafos ou uma área de gráfico.</summary>
public enum SkeletonShape { Rows, Text, Block }

/// <summary>
/// Esqueleto de carregamento de um painel (Border): enquanto <c>Skeleton.Active</c> é verdadeiro, uma camada no fundo do próprio
/// painel cobre o conteúdo e mostra barras no tom dos fios no lugar dos dados, com um pulso lento de opacidade. A moldura do
/// painel e o que fica acima de <c>Skeleton.Top</c> (o título fixo) continuam à vista, então só o que falta ler fica em espera.
/// Ao desligar, a camada some em 260 ms e o conteúdo aparece embaixo dela, sem salto: as barras têm a altura das linhas reais.
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
            border.ClearValue(CompanionProperty); // um cartão que acompanhava a página passa a esperar dado próprio
            layer ??= Inject(border);
            if (layer != null)
            {
                layer.Show();
                Hold(border);
            }
            // Num modelo de item (Skeleton.Active="{Binding Loading}"), o binding liga antes de o painel receber o conteúdo: tenta
            // de novo logo depois, ainda antes do primeiro quadro (prioridade acima da de desenho)
            else border.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                if (!GetActive(border) || border.GetValue(LayerProperty) != null) return;
                Inject(border)?.Show();
                Hold(border);
            });
            return;
        }
        // Página em primeira carga: o painel pronto continua em esqueleto até o último da página ficar pronto, e todos saem juntos
        if (PageOf(border) is { } page && page.GetValue(PageHoldProperty) is PageHold { Released: false } hold)
        {
            if (!Pending(page)) Release(page, hold);
            return;
        }
        layer?.Hide(Animate && border.IsVisible);
    }

    // ------------------------------------------------------------------------------------------- página inteira na primeira carga

    /// <summary>
    /// Primeira carga de uma página: quando um painel dela entra em esqueleto, a página inteira entra junto (os painéis que já têm
    /// o que mostrar ganham um esqueleto desenhado a partir do próprio conteúdo, <see cref="CompanionProperty"/>), e ninguém sai
    /// antes do último: a página passa de "carregando" a "pronta" de uma vez, em vez de uns cartões em barras e outros prontos.
    /// Vale para toda página, sem código nela: basta ligar o esqueleto dos painéis que esperam dado. Teto de 4 s: depois dele,
    /// o que já está pronto aparece e só o que ainda espera (uma consulta na internet) continua em esqueleto.
    /// </summary>
    private sealed class PageHold
    {
        public bool Released;
        public DispatcherTimer? Cap;
    }

    public static readonly TimeSpan PageHoldCap = TimeSpan.FromSeconds(4);

    private static readonly DependencyProperty PageHoldProperty = DependencyProperty.RegisterAttached(
        "PageHold", typeof(PageHold), typeof(Skeleton));

    /// <summary>Painel sem dado próprio que acompanha a página em esqueleto (planta automática, tirada do conteúdo).</summary>
    private static readonly DependencyProperty CompanionProperty = DependencyProperty.RegisterAttached(
        "Companion", typeof(bool), typeof(Skeleton), new FrameworkPropertyMetadata(false, Redraw));

    /// <summary>A página: o UserControl mais de fora acima do painel (as páginas são UserControls dentro da janela).</summary>
    private static FrameworkElement? PageOf(DependencyObject d)
    {
        FrameworkElement? page = null;
        for (DependencyObject? n = d; n != null && n is not Window; n = LogicalTreeHelper.GetParent(n) ?? (n is Visual ? VisualTreeHelper.GetParent(n) : null))
            if (n is UserControl uc) page = uc;
        return page;
    }

    /// <summary>Tudo o que está à vista na página, pelas duas árvores (a lógica existe antes da primeira medição; a visual tem os itens gerados).</summary>
    private static IEnumerable<DependencyObject> Shown(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!seen.Add(n) || n is UIElement { Visibility: not Visibility.Visible } && !ReferenceEquals(n, root)) continue;
            yield return n;
            foreach (var c in LogicalTreeHelper.GetChildren(n)) if (c is DependencyObject dc) stack.Push(dc);
            if (n is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(n); i++) stack.Push(VisualTreeHelper.GetChild(n, i));
        }
    }

    private static bool Pending(FrameworkElement page) => Shown(page).OfType<Border>().Any(GetActive);

    private static Style? _cardStyle;

    private static bool IsCard(Border b)
    {
        _cardStyle ??= Application.Current?.TryFindResource("CardBorder") as Style;
        for (var style = b.Style; style != null; style = style.BasedOn)
            if (ReferenceEquals(style, _cardStyle)) return true;
        return false;
    }

    private static void Hold(Border trigger)
    {
        if (PageOf(trigger) is not { } page || page.GetValue(PageHoldProperty) != null || !PageMotion.FirstLoad(page)) return;
        var hold = new PageHold();
        page.SetValue(PageHoldProperty, hold);
        // Os companheiros entram depois de todos os painéis que a página liga de uma vez (Skeleton.Set no construtor), ainda
        // antes do primeiro quadro: um painel que ligasse depois de virar companheiro não seria contado como pendente
        page.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (hold.Released) return;
            if (!Pending(page)) { Release(page, hold); return; }
            foreach (var card in TopCards(page))
            {
                if (GetActive(card) || card.GetValue(LayerProperty) is SkeletonLayer { Shown: true }) continue;
                card.SetValue(CompanionProperty, true);
                ((card.GetValue(LayerProperty) as SkeletonLayer) ?? Inject(card))?.Show();
            }
        });
        hold.Cap = new DispatcherTimer(PageHoldCap, DispatcherPriority.Background, (_, _) => Release(page, hold), page.Dispatcher);
        hold.Cap.Start();
    }

    /// <summary>Os painéis da página que não estão dentro de outro painel, na ordem do XAML.</summary>
    private static IEnumerable<Border> TopCards(FrameworkElement page)
    {
        var stack = new Stack<object>();
        stack.Push(page);
        while (stack.Count > 0)
        {
            object node = stack.Pop();
            if (node is UIElement { Visibility: not Visibility.Visible } && !ReferenceEquals(node, page)) continue;
            if (node is Border b && IsCard(b)) { yield return b; continue; }
            if (node is not DependencyObject d) continue;
            var children = LogicalTreeHelper.GetChildren(d).Cast<object>().ToList();
            for (int i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
    }

    /// <summary>A página toda sai do esqueleto de uma vez (no teto, só o que já está pronto).</summary>
    private static void Release(FrameworkElement page, PageHold hold)
    {
        if (hold.Released) return;
        hold.Released = true;
        hold.Cap?.Stop();
        bool animate = Animate && page.IsVisible;
        foreach (var border in Shown(page).OfType<Border>().ToList())
        {
            if (GetActive(border) || border.GetValue(LayerProperty) is not SkeletonLayer layer) continue;
            layer.Hide(animate);
        }
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
        private bool _leaving;
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

        /// <summary>À vista e não saindo.</summary>
        public bool Shown => Visibility == Visibility.Visible && !_leaving;

        public void Show()
        {
            _version++;
            _leaving = false;
            InvalidateMeasure();
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
            _border.ClearValue(CompanionProperty);
            // A altura reservada pela planta sai junto com o esqueleto, no mesmo quadro em que o conteúdo aparece: se a planta e o
            // conteúdo diferirem um pouco, o ajuste fica escondido na troca, e não num salto 200 ms depois, com tudo já à vista
            _leaving = true;
            InvalidateMeasure();
            if (!animate)
            {
                BeginAnimation(OpacityProperty, null);
                Visibility = Visibility.Collapsed;
                StopPulse();
                return;
            }
            // 260 ms, devagar no começo e no fim: o conteúdo embaixo vai aparecendo enquanto as barras somem (uma troca cruzada),
            // em vez de a camada sumir quase toda no primeiro quadro
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
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
            return _leaving || string.IsNullOrWhiteSpace(plan) ? default : new Size(0, GetTop(_border) + SkeletonPlan.Height(plan));
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

        private Visual? Content => (Parent as Grid)?.Children[0];

        /// <summary>
        /// Onde começa o esqueleto de um companheiro sem <c>Skeleton.Top</c>: abaixo do cabeçalho do cartão (a linha de 34 px do
        /// CardHeader, ou o título de seção do alto), que fica à vista como nos painéis que esperam dado.
        /// </summary>
        private double AutoTop()
        {
            if (Content is not { } content) return 0;
            var header = _border.TryFindResource("CardHeader") as Style;
            var title = _border.TryFindResource("SectionTitle") as Style;
            foreach (var node in Descendants(content))
            {
                if (node is not FrameworkElement { IsVisible: true } e) continue;
                var r = e.TransformToVisual(this).TransformBounds(new Rect(e.RenderSize));
                if (r.Top > 40) continue;
                if (header != null && ReferenceEquals(e.Style, header)) return r.Bottom;
                if (e is TextBlock && title != null && IsStyle(e.Style, title))
                    return e.Parent is Grid { ActualHeight: <= 44 } g && g != content ? g.TransformToVisual(this).TransformBounds(new Rect(g.RenderSize)).Bottom : r.Bottom + 6;
            }
            return 0;
        }

        private static bool IsStyle(Style? style, Style target)
        {
            for (; style != null; style = style.BasedOn) if (ReferenceEquals(style, target)) return true;
            return false;
        }

        /// <summary>Árvore visual em largura (o alto do cartão primeiro).</summary>
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                yield return n;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(n); i++) queue.Enqueue(VisualTreeHelper.GetChild(n, i));
            }
        }

        /// <summary>
        /// Esqueleto de um cartão que já tem o que mostrar (companheiro da página em primeira carga): as barras saem do próprio
        /// conteúdo, no lugar e no tamanho de cada texto, botão, ícone e medidor, então a troca para o conteúdo é exata e nenhuma
        /// página nova precisa escrever planta para os cartões que não esperam dado.
        /// </summary>
        private void DrawAuto(DrawingContext dc, double top, Brush bar, Brush strong)
        {
            if (Content is not { } content) return;
            var outline = new Pen(bar, 1);
            var stack = new Stack<DependencyObject>();
            stack.Push(content);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node is UIElement { Visibility: not Visibility.Visible } or UIElement { Opacity: < 0.05 }) continue;
                if (node is not FrameworkElement e || e.ActualWidth < 1 || e.ActualHeight < 1)
                {
                    for (int i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(node, i));
                    continue;
                }
                var r = e.TransformToVisual(this).TransformBounds(new Rect(e.RenderSize));
                if (r.Bottom <= top + 1) continue;
                switch (e)
                {
                    case TextBlock t:
                        DrawText(dc, t, r, bar, strong);
                        continue;
                    case System.Windows.Controls.Primitives.ButtonBase:
                        {
                            double radius = Math.Min(r.Height / 2, 12);
                            if (Math.Abs(r.Width - r.Height) < 2) dc.DrawEllipse(bar, null, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                            else dc.DrawRoundedRectangle(bar, null, r, radius, radius);
                            continue;
                        }
                    case TextBox or ComboBox:
                        dc.DrawRoundedRectangle(null, outline, new Rect(r.X + 0.5, r.Y + 0.5, Math.Max(0, r.Width - 1), Math.Max(0, r.Height - 1)), 12, 12);
                        continue;
                    case System.Windows.Shapes.Ellipse when r.Width <= 64 && r.Height <= 64:
                        dc.DrawEllipse(bar, null, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                        continue;
                    case HwIcon or Image or System.Windows.Shapes.Shape when r.Width <= 64 && r.Height <= 64:
                        dc.DrawRoundedRectangle(bar, null, r, Math.Min(12, r.Height / 3), Math.Min(12, r.Height / 3));
                        continue;
                    case Canvas or System.Windows.Shapes.Shape or Image when r.Height > 40:
                        dc.DrawRoundedRectangle(null, outline, new Rect(r.X + 0.5, r.Y + 0.5, Math.Max(0, r.Width - 1), Math.Max(0, r.Height - 1)), 6, 6);
                        continue;
                    case ProgressBar:
                        dc.DrawRoundedRectangle(bar, null, r, Math.Min(r.Height / 2, 4), Math.Min(r.Height / 2, 4));
                        continue;
                    case Border { Background: not null and not SolidColorBrush { Color.A: 0 } } b when b.Child == null || r.Height <= 12:
                        if (r.Height <= 2) dc.DrawRectangle(bar, null, new Rect(r.X, Math.Round(r.Y), r.Width, 1));
                        else if (r.Height <= 12) dc.DrawRoundedRectangle(bar, null, r, r.Height / 2, r.Height / 2);
                        else if (b.Child == null && r.Width <= 64 && r.Height <= 64) dc.DrawRoundedRectangle(bar, null, r, Math.Min(12, r.Height / 3), Math.Min(12, r.Height / 3));
                        continue;
                }
                for (int i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(node, i));
            }
        }

        /// <summary>Um texto vira uma barra por linha, da largura do texto, na altura do miolo das letras.</summary>
        private static void DrawText(DrawingContext dc, TextBlock t, Rect r, Brush bar, Brush strong)
        {
            if (string.IsNullOrWhiteSpace(t.Text) && t.Inlines.Count == 0) return;
            double fs = t.FontSize;
            double bh = Math.Clamp(Math.Round(fs * 0.58), 8, 26);
            double line = double.IsNaN(t.LineHeight) || t.LineHeight <= 0 ? fs * 1.33 : t.LineHeight;
            int lines = Math.Max(1, (int)Math.Round((r.Height - t.Padding.Top - t.Padding.Bottom) / line));
            double width = Math.Min(r.Width, Math.Max(8, t.DesiredSize.Width - t.Margin.Left - t.Margin.Right));
            if (lines > 1) width = r.Width;
            var brush = fs >= 17 || t.FontWeight.ToOpenTypeWeight() >= 600 ? strong : bar;
            double lineTop = r.Y + (r.Height - lines * line) / 2;
            for (int i = 0; i < lines; i++)
            {
                double w = i == lines - 1 && lines > 1 ? width * 0.6 : width;
                double x = t.TextAlignment switch
                {
                    TextAlignment.Right => r.Right - w,
                    TextAlignment.Center => r.X + (r.Width - w) / 2,
                    _ => r.X,
                };
                double mid = lineTop + line * i + line / 2;
                dc.DrawRoundedRectangle(brush, null, new Rect(x, Math.Round(mid - bh / 2), w, bh), bh / 2, bh / 2);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            string plan = GetLayout(_border), itemKey = GetItemStyle(_border);
            bool auto = (bool)_border.GetValue(CompanionProperty) && string.IsNullOrWhiteSpace(plan) && itemKey.Length == 0;
            double top = Math.Min(h, auto && GetTop(_border) <= 0 ? AutoTop() : GetTop(_border));
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

            if (auto)
            {
                dc.PushClip(new RectangleGeometry(new Rect(-pad.Left, top, w + pad.Left + pad.Right, Math.Max(0, h - top + pad.Bottom))));
                dc.PushOpacity(1, _pulse);
                DrawAuto(dc, top, bar, strong);
                dc.Pop();
                dc.Pop();
                return;
            }
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
