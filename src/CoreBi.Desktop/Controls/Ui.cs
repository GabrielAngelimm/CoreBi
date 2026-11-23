using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CoreBi.Desktop;

/// <summary>
/// Propriedades anexadas do design system: ícone (glifo Segoe Fluent), legenda, texto de espera, sufixo de unidade, canal
/// do componente e a luz de um botão sob o mouse. Os templates de Theme.xaml leem estes valores; as páginas só os declaram.
/// </summary>
public static class Ui
{
    /// <summary>
    /// Troca os itens de uma lista só quando mudaram (registros comparam por valor): trocar por uma cópia igual refaz as linhas, e o
    /// que já estava na tela pisca a cada releitura.
    /// </summary>
    public static void SetItems<T>(System.Windows.Controls.ItemsControl list, IReadOnlyList<T>? items)
    {
        if (items != null && list.ItemsSource is IEnumerable<T> shown && shown.SequenceEqual(items)) return;
        if (items == null && list.ItemsSource == null) return;
        list.ItemsSource = items;
    }

    /// <summary>
    /// Interruptor (estilo Switch): o estado aparece na hora (setters do template) e o deslize só acontece no clique de quem usa.
    /// Com a animação presa ao IsChecked, cada linha recriada (voltar à página, reler a lista) ligava a chave de novo na tela.
    /// </summary>
    public static readonly DependencyProperty SwitchMotionProperty = DependencyProperty.RegisterAttached(
        "SwitchMotion", typeof(bool), typeof(Ui), new PropertyMetadata(false, (d, e) =>
        {
            if (d is not System.Windows.Controls.Primitives.ToggleButton box) return;
            box.Click -= SwitchClick;
            if ((bool)e.NewValue) box.Click += SwitchClick;
        }));
    public static bool GetSwitchMotion(DependencyObject d) => (bool)d.GetValue(SwitchMotionProperty);
    public static void SetSwitchMotion(DependencyObject d, bool value) => d.SetValue(SwitchMotionProperty, value);

    private static void SwitchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Control box || !SystemParameters.ClientAreaAnimation) return;
        // O clique chega com o IsChecked já trocado e o botão já no lado novo: o deslize parte do lado antigo e volta a 0
        bool on = box is System.Windows.Controls.Primitives.ToggleButton { IsChecked: true };
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var time = TimeSpan.FromMilliseconds(200);
        if (box.Template?.FindName("knob", box) is UIElement knob)
        {
            if (knob.RenderTransform is not TranslateTransform { IsFrozen: false } slide) knob.RenderTransform = slide = new TranslateTransform();
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? -18 : 18, 0, time) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }
        if (box.Template?.FindName("on", box) is UIElement fill)
            fill.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 0 : 1, on ? 1 : 0, time) { FillBehavior = FillBehavior.Stop });
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetIcon(DependencyObject d) => (string)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string value) => d.SetValue(IconProperty, value);

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.RegisterAttached(
        "Caption", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetCaption(DependencyObject d) => (string)d.GetValue(CaptionProperty);
    public static void SetCaption(DependencyObject d, string value) => d.SetValue(CaptionProperty, value);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string value) => d.SetValue(PlaceholderProperty, value);

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.RegisterAttached(
        "Suffix", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetSuffix(DependencyObject d) => (string)d.GetValue(SuffixProperty);
    public static void SetSuffix(DependencyObject d, string value) => d.SetValue(SuffixProperty, value);

    /// <summary>
    /// Componente de hardware (Cpu, Gpu, Memory, Virtual, Storage, Board, Net ou Signal; vazio = Steel): o ícone (ChannelIcon) e o
    /// fundo claro da ficha (ChannelWash) pintam por ele, para cada peça ter a mesma cor em todas as telas.
    /// </summary>
    public static readonly DependencyProperty ChannelProperty = DependencyProperty.RegisterAttached(
        "Channel", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetChannel(DependencyObject d) => (string)d.GetValue(ChannelProperty);
    public static void SetChannel(DependencyObject d, string value) => d.SetValue(ChannelProperty, value);

    /// <summary>Cor própria do glifo de um botão (vazio = a cor do texto): os botões fantasmas acendem o glifo no sinal.</summary>
    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.RegisterAttached(
        "IconBrush", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetIconBrush(DependencyObject d) => (Brush?)d.GetValue(IconBrushProperty);
    public static void SetIconBrush(DependencyObject d, Brush? value) => d.SetValue(IconBrushProperty, value);

    /// <summary>Medidor (ScaleTransform de 0 a 1) que desliza até a leitura nova em vez de saltar; sem animate, vai direto.</summary>
    public static void Glide(ScaleTransform meter, double to, bool animate)
    {
        to = Math.Clamp(double.IsFinite(to) ? to : 0, 0, 1);
        if (!animate)
        {
            meter.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            meter.ScaleX = to;
            return;
        }
        meter.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>
    /// A barra de progresso de um teste que acabou (concluído, cancelado ou interrompido) volta ao zero deslizando em 240 ms, sem
    /// pular: a mesma volta no Benchmark e no Teste de carga. O valor de base já fica em 0, então um teste novo começa limpo.
    /// </summary>
    public static void Rewind(ScaleTransform bar)
    {
        double from = bar.ScaleX;
        bar.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        bar.ScaleX = 0;
        if (from <= 0 || !SystemParameters.ClientAreaAnimation) return;
        bar.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    /// <summary>
    /// Fração de um medidor ligada por binding (<c>&lt;ScaleTransform local:Ui.Fill="{Binding Fraction}"/&gt;</c>): cada valor novo
    /// desliza até lá (<see cref="Glide"/>) quando a janela está à frente e nada pesado roda (MainWindow.LiveMotion); senão vai direto.
    /// </summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
        "Fill", typeof(double), typeof(Ui), new PropertyMetadata(0.0, (d, e) =>
        {
            if (d is ScaleTransform meter)
                Glide(meter, (double)e.NewValue, Application.Current?.MainWindow is ILiveMotionHost { LiveMotion: true });
        }));
    public static double GetFill(DependencyObject d) => (double)d.GetValue(FillProperty);
    public static void SetFill(DependencyObject d, double value) => d.SetValue(FillProperty, value);

    /// <summary>Camada que acende por cima de um botão quando o mouse passa (cada estilo de botão escolhe a sua).</summary>
    public static readonly DependencyProperty HoverFillProperty = DependencyProperty.RegisterAttached(
        "HoverFill", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetHoverFill(DependencyObject d) => (Brush?)d.GetValue(HoverFillProperty);
    public static void SetHoverFill(DependencyObject d, Brush? value) => d.SetValue(HoverFillProperty, value);

    /// <summary>
    /// Verdadeiro dentro de um SlidingSelectionPanel (os filhos herdam): o item marcado não pinta o próprio fundo, porque o
    /// painel desenha um destaque só e o desliza até ele.
    /// </summary>
    public static readonly DependencyProperty SlidingGroupProperty = DependencyProperty.RegisterAttached(
        "SlidingGroup", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetSlidingGroup(DependencyObject d) => (bool)d.GetValue(SlidingGroupProperty);
    public static void SetSlidingGroup(DependencyObject d, bool value) => d.SetValue(SlidingGroupProperty, value);

    /// <summary>
    /// Botão de iniciar/parar: o mesmo lugar troca de papel (ação principal ↔ interromper),
    /// com cor, glifo e texto coerentes. Assim nenhum teste depende só do texto para mostrar que está rodando.
    /// </summary>
    public static void SetRunning(System.Windows.Controls.Button button, bool running, string idleText, string runningText = "Parar", string idleIcon = "\uE768")
    {
        // O estilo de repouso é o que o botão tinha na primeira chamada: um botão de cabeçalho (HeaderPrimary, 40 px) para no
        // HeaderStop, do mesmo tamanho, e volta a ele, em vez de encolher para o PrimaryButton dos painéis.
        if (button.GetValue(IdleStyleProperty) is not Style idle)
        {
            idle = button.Style ?? (Style)button.FindResource("PrimaryButton");
            button.SetValue(IdleStyleProperty, idle);
        }
        bool header = button.TryFindResource("HeaderPrimary") is Style hp && ReferenceEquals(idle, hp);
        // O botão do pé das configurações (ConfigStart, 42 px) para no ConfigStop, da mesma altura
        bool config = button.TryFindResource("ConfigStart") is Style cs && ReferenceEquals(idle, cs);
        var target = running ? button.TryFindResource(header ? "HeaderStop" : config ? "ConfigStop" : "StopButton") as Style : idle;
        bool changed = !ReferenceEquals(button.Style, target) || !Equals(button.Content, running ? runningText : idleText);
        if (target != null) button.Style = target;
        SetIcon(button, running ? "\uE71A" : idleIcon);
        button.Content = running ? runningText : idleText;
        // A troca de texto e de cor acende em 180 ms, sem piscar
        if (changed) HeaderMotion.Soften(button);
    }

    private static readonly DependencyProperty IdleStyleProperty = DependencyProperty.RegisterAttached(
        "IdleStyle", typeof(Style), typeof(Ui), new PropertyMetadata(null));

    /// <summary>
    /// Selo de veredito (estilo Badge com um LED e um texto dentro): tone é "Ok", "Warn", "Danger" ou "" (sem dados).
    /// Saudável fica em tinta com o LED verde; atenção e crítico ganham fundo e texto na cor, porque pedem que alguém olhe.
    /// </summary>
    public static void SetTone(System.Windows.Controls.Border badge, string tone)
    {
        bool alert = tone is "Warn" or "Danger";
        string soft = tone + "Soft";
        if (alert)
        {
            badge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, soft);
            badge.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, soft);
        }
        else
        {
            badge.ClearValue(System.Windows.Controls.Border.BackgroundProperty); // o estilo Badge é transparente com fio
            badge.ClearValue(System.Windows.Controls.Border.BorderBrushProperty);
        }
        if (badge.Child is not System.Windows.Controls.Panel panel) return;
        foreach (var child in panel.Children)
        {
            if (child is System.Windows.Shapes.Ellipse led)
                led.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, tone.Length > 0 ? tone : "Faint");
            else if (child is System.Windows.Controls.TextBlock text)
                text.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, alert ? tone : tone == "Ok" ? "Fg" : "Muted");
        }
    }

    /// <summary>
    /// Copia um texto. A área de transferência às vezes está presa por outro programa, então tenta de novo antes de desistir
    /// (Sistema e Drivers).
    /// </summary>
    public static async Task<bool> TryCopyAsync(string text)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { Clipboard.SetText(text); return true; }
            catch (System.Runtime.InteropServices.COMException) { await Task.Delay(60); }
        }
        return false;
    }

    /// <summary>Raio dos painéis (RadiusPanel), para recortar cantos desenhados em código.</summary>
    public static double PanelRadius(FrameworkElement element) =>
        element.TryFindResource("RadiusPanel") is CornerRadius r ? r.TopLeft : 16;

    public static string? CheckedTag(System.Windows.Controls.Panel panel) =>
        panel.Children.OfType<System.Windows.Controls.RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;
}
