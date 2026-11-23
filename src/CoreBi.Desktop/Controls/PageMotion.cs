using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CoreBi.Desktop;

/// <summary>
/// Entrada das páginas, igual em todas: a página aparece no mesmo instante do clique, com 200 ms de opacidade e 280 ms de
/// deslocamento curto (8 px), e os painéis dela acendem em sequência, 35 ms um depois do outro (no máximo sete), na ordem
/// de leitura, subindo 6 px enquanto acendem. Trabalho pesado de montagem espera a entrada terminar (<see cref="AfterEntrance"/>) para não engasgar o
/// movimento; até lá, os painéis que dependem dele mostram o esqueleto (<see cref="Skeleton"/>).
/// Respeita "Mostrar animações no Windows": sem animações, a página só aparece.
/// </summary>
public static class PageMotion
{
    // Um pouco mais longa e com desaceleração mais macia que a primeira versão (170/230 ms, 10 px, cúbica): a página assenta em
    // vez de "chegar", sem ficar lenta para quem troca de página rápido
    public static readonly TimeSpan PageFade = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan PageShift = TimeSpan.FromMilliseconds(280);
    private const double PageRise = 8;
    private static readonly TimeSpan PanelFade = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan PanelShift = TimeSpan.FromMilliseconds(320);
    private const double PanelRise = 6;
    private const int StaggerMs = 35;
    private const int MaxStaggered = 7;

    private static readonly DependencyProperty EnteredAtProperty = DependencyProperty.RegisterAttached(
        "EnteredAt", typeof(long), typeof(PageMotion), new PropertyMetadata(0L));

    /// <summary>
    /// Marca um elemento que não é painel mas acende como um na cascata: uma lista de cartões gerados (ItemsControl), cujos
    /// itens não aparecem na árvore lógica e ficariam de fora da cascata, entrando todos de uma vez com a página.
    /// </summary>
    public static readonly DependencyProperty GroupProperty = DependencyProperty.RegisterAttached(
        "Group", typeof(bool), typeof(PageMotion), new PropertyMetadata(false));
    public static bool GetGroup(DependencyObject d) => (bool)d.GetValue(GroupProperty);
    public static void SetGroup(DependencyObject d, bool value) => d.SetValue(GroupProperty, value);

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
        page.SetValue(EntriesProperty, (int)page.GetValue(EntriesProperty) + 1);
        if (!Animate) return;
        if (page.RenderTransform is not TranslateTransform shift || shift.IsFrozen) page.RenderTransform = shift = new TranslateTransform();
        var panels = Panels(page).Take(MaxStaggered).ToList();
        page.BeginAnimation(UIElement.OpacityProperty, null);
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        page.Opacity = 0;
        shift.Y = PageRise;
        foreach (var panel in panels.Skip(1)) { panel.BeginAnimation(UIElement.OpacityProperty, null); panel.Opacity = 0; }

        page.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            page.SetValue(EnteredAtProperty, Environment.TickCount64);
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            // Valor de base no destino e a animação saindo do começo, no mesmo passo: nenhum quadro sai entre os dois
            page.Opacity = 1;
            shift.Y = 0;
            page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, PageFade) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(PageRise, 0, PageShift) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            for (int i = 1; i < panels.Count; i++)
            {
                panels[i].Opacity = 1;
                Stagger(panels[i], i, ease);
            }
        });
    }

    /// <summary>
    /// Um painel acende atrasado e sobe 6 px enquanto acende: começa invisível e chega à opacidade e ao lugar normais sem segurar
    /// o valor no fim. A subida curta faz a página assentar em cascata, na ordem de leitura, em vez de só clarear.
    /// </summary>
    private static void Stagger(UIElement panel, int index, IEasingFunction ease)
    {
        var delay = TimeSpan.FromMilliseconds(index * StaggerMs);
        var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(delay + PanelFade), ease));
        panel.BeginAnimation(UIElement.OpacityProperty, fade);
        if (panel.RenderTransform is TranslateTransform { IsFrozen: false } || ReferenceEquals(panel.RenderTransform, Transform.Identity) || panel.RenderTransform == null)
        {
            if (panel.RenderTransform is not TranslateTransform rise || rise.IsFrozen) panel.RenderTransform = rise = new TranslateTransform();
            var lift = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            lift.KeyFrames.Add(new DiscreteDoubleKeyFrame(PanelRise, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            lift.KeyFrames.Add(new DiscreteDoubleKeyFrame(PanelRise, KeyTime.FromTimeSpan(delay)));
            lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay + PanelShift), ease));
            rise.BeginAnimation(TranslateTransform.YProperty, lift);
        }
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
            if (node is Border border && IsCard(border.Style) || node is FrameworkElement group && GetGroup(group))
            {
                yield return (FrameworkElement)node;
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
    /// Leituras ao vivo de uma página (instrumentos, gráfico do consumo, tensões): a leitura nova entra no instante em que a página
    /// aparece, antes do primeiro quadro, e sobe junto com os outros cartões na mesma cascata. Antes, o tique trocava os valores da
    /// visita anterior de uma vez, logo depois do movimento, e só aquela área parecia saltar.
    /// </summary>
    public static void RevealLive(FrameworkElement page, Action sample)
    {
        page.IsVisibleChanged += (_, _) => { if (page.IsVisible) sample(); };
    }

    /// <summary>
    /// A página ainda está entrando (deslocamento e cascata dos painéis, ~530 ms): o tique de 1 s pula o redesenho dela nesse
    /// intervalo, para um gráfico refeito não cair no meio do movimento (a amostra continua sendo guardada).
    /// </summary>
    public static bool IsEntering(FrameworkElement page) =>
        Animate && Environment.TickCount64 - (long)page.GetValue(EnteredAtProperty) < 530;

    private static readonly DependencyProperty EntriesProperty = DependencyProperty.RegisterAttached(
        "Entries", typeof(int), typeof(PageMotion), new PropertyMetadata(0));

    /// <summary>
    /// Primeira carga da página: ainda não entrou (montada no construtor, ou a Visão geral na abertura) ou está na primeira
    /// entrada. Só nela o esqueleto cobre a página inteira; uma releitura pedida depois (um botão Atualizar) fica no cartão dela.
    /// </summary>
    public static bool FirstLoad(FrameworkElement page)
    {
        int entries = (int)page.GetValue(EntriesProperty);
        return entries == 0 || entries == 1 && Environment.TickCount64 - (long)page.GetValue(EnteredAtProperty) < 600;
    }

    /// <summary>O mesmo que <see cref="AfterEntrance"/>, para esperar com await: a leitura corre durante a entrada e só o que
    /// mexe na tela espera ela terminar.</summary>
    public static Task EntranceDone(FrameworkElement page)
    {
        var done = new TaskCompletionSource();
        AfterEntrance(page, () => done.TrySetResult());
        return done.Task;
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
