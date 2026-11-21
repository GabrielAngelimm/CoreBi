using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CoreBi.Desktop;

/// <summary>
/// Entrada das páginas, igual em todas: a página aparece no mesmo instante do clique, com 170 ms de opacidade e 230 ms de
/// deslocamento curto (10 px), e os painéis dela acendem em sequência, 30 ms um depois do outro (no máximo sete), na ordem
/// de leitura. Trabalho pesado de montagem espera a entrada terminar (<see cref="AfterEntrance"/>) para não engasgar o
/// movimento; até lá, os painéis que dependem dele mostram o esqueleto (<see cref="Skeleton"/>).
/// Respeita "Mostrar animações no Windows": sem animações, a página só aparece.
/// </summary>
public static class PageMotion
{
    public static readonly TimeSpan PageFade = TimeSpan.FromMilliseconds(170);
    public static readonly TimeSpan PageShift = TimeSpan.FromMilliseconds(230);
    private static readonly TimeSpan PanelFade = TimeSpan.FromMilliseconds(220);
    private const int StaggerMs = 30;
    private const int MaxStaggered = 7;

    private static readonly DependencyProperty EnteredAtProperty = DependencyProperty.RegisterAttached(
        "EnteredAt", typeof(long), typeof(PageMotion), new PropertyMetadata(0L));

    private static Style? _card;

    private static bool Animate => SystemParameters.ClientAreaAnimation;

    /// <summary>
    /// Anima a entrada de uma página que acabou de ficar visível. A página fica transparente até a primeira medição dela terminar
    /// e o movimento começa só então: numa página grande, a medição leva alguns quadros, e o movimento começado antes dela já
    /// apareceria pela metade.
    /// </summary>
    public static void Enter(FrameworkElement page)
    {
        page.SetValue(EnteredAtProperty, Environment.TickCount64);
        if (!Animate) return;
        if (page.RenderTransform is not TranslateTransform shift || shift.IsFrozen) page.RenderTransform = shift = new TranslateTransform();
        var panels = Panels(page).Take(MaxStaggered).ToList();
        page.BeginAnimation(UIElement.OpacityProperty, null);
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        page.Opacity = 0;
        shift.Y = 10;
        foreach (var panel in panels.Skip(1)) { panel.BeginAnimation(UIElement.OpacityProperty, null); panel.Opacity = 0; }

        page.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            page.SetValue(EnteredAtProperty, Environment.TickCount64);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            // Valor de base no destino e a animação saindo do começo, no mesmo passo: nenhum quadro sai entre os dois
            page.Opacity = 1;
            shift.Y = 0;
            page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, PageFade) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, PageShift) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            for (int i = 1; i < panels.Count; i++)
            {
                panels[i].Opacity = 1;
                Stagger(panels[i], i, ease);
            }
        });
    }

    /// <summary>Um painel acende atrasado: começa invisível e chega à opacidade normal sem segurar o valor no fim.</summary>
    private static void Stagger(UIElement panel, int index, IEasingFunction ease)
    {
        var delay = TimeSpan.FromMilliseconds(index * StaggerMs);
        var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(delay + PanelFade), ease));
        panel.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>
    /// Os painéis da página (Border no estilo de painel ou derivado dele) na ordem do XAML, que é a ordem de leitura; um painel
    /// dentro de outro acende junto com o de fora. Pela árvore lógica, que já existe antes da primeira medição da página.
    /// </summary>
    private static IEnumerable<FrameworkElement> Panels(FrameworkElement page)
    {
        _card ??= Application.Current?.TryFindResource("CardBorder") as Style;
        if (_card == null) yield break;
        var stack = new Stack<object>();
        stack.Push(page);
        while (stack.Count > 0)
        {
            object node = stack.Pop();
            if (node is FrameworkElement { Visibility: not Visibility.Visible } && !ReferenceEquals(node, page)) continue;
            if (node is Border border && IsCard(border.Style))
            {
                yield return border;
                continue;
            }
            if (node is not DependencyObject d) continue;
            var children = LogicalTreeHelper.GetChildren(d).Cast<object>().ToList();
            for (int i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
    }

    private static bool IsCard(Style? style)
    {
        for (; style != null; style = style.BasedOn)
            if (ReferenceEquals(style, _card)) return true;
        return false;
    }

    /// <summary>
    /// Roda <paramref name="work"/> depois que a entrada da página termina (ou já, se ela entrou há mais tempo ou sem animações),
    /// com a interface livre: montagem pesada no meio da entrada engasgaria o movimento.
    /// </summary>
    public static void AfterEntrance(FrameworkElement page, Action work)
    {
        long left = Animate ? (long)PageShift.TotalMilliseconds - (Environment.TickCount64 - (long)page.GetValue(EnteredAtProperty)) : 0;
        if (left <= 15)
        {
            page.Dispatcher.BeginInvoke(work, DispatcherPriority.Background);
            return;
        }
        var timer = new DispatcherTimer(DispatcherPriority.Background, page.Dispatcher) { Interval = TimeSpan.FromMilliseconds(left) };
        // A entrada pode ter recomeçado depois da primeira medição da página: confere de novo ao acordar
        timer.Tick += (_, _) => { timer.Stop(); AfterEntrance(page, work); };
        timer.Start();
    }
}
