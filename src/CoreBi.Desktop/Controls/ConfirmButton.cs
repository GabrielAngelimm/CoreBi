using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CoreBi.Desktop;

/// <summary>
/// Confirmação no próprio botão, para ações que não se desfazem (limpar o histórico): o primeiro clique arma o botão, que troca o
/// texto ("Confirmar") e fica no tom de perigo (estilo <c>HistoryClear</c>, pela propriedade Armed); o segundo, em até 4 s,
/// executa. Sem clique, ou com o mouse saindo dele, o botão volta ao normal. Nada de janela de confirmação no meio da tela.
/// </summary>
public static class ConfirmButton
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(4);

    public static readonly DependencyProperty ArmedProperty = DependencyProperty.RegisterAttached(
        "Armed", typeof(bool), typeof(ConfirmButton), new FrameworkPropertyMetadata(false));
    public static bool GetArmed(DependencyObject d) => (bool)d.GetValue(ArmedProperty);
    public static void SetArmed(DependencyObject d, bool value) => d.SetValue(ArmedProperty, value);

    private static readonly DependencyProperty RestProperty = DependencyProperty.RegisterAttached(
        "Rest", typeof(object), typeof(ConfirmButton), new FrameworkPropertyMetadata(null));

    private static readonly DependencyProperty TimerProperty = DependencyProperty.RegisterAttached(
        "Timer", typeof(DispatcherTimer), typeof(ConfirmButton), new FrameworkPropertyMetadata(null));

    /// <summary>true no segundo clique (o botão volta ao normal e a ação segue); false no primeiro, que só arma o botão.</summary>
    public static bool Confirm(object sender, string armedText = "Confirmar")
    {
        if (sender is not Button button) return true;
        if (GetArmed(button))
        {
            Disarm(button);
            return true;
        }
        button.SetValue(RestProperty, button.Content);
        button.Content = armedText;
        SetArmed(button, true);
        if (button.GetValue(TimerProperty) is not DispatcherTimer timer)
        {
            // Construtor sem o tratador: com ele o temporizador já nasceria ligado
            timer = new DispatcherTimer { Interval = Window };
            timer.Tick += (_, _) => Disarm(button);
            button.SetValue(TimerProperty, timer);
            button.MouseLeave += (_, _) => { if (GetArmed(button)) Disarm(button); };
        }
        timer.Stop();
        timer.Start();
        return false;
    }

    private static void Disarm(Button button)
    {
        (button.GetValue(TimerProperty) as DispatcherTimer)?.Stop();
        if (!GetArmed(button)) return;
        SetArmed(button, false);
        button.Content = button.GetValue(RestProperty);
    }
}
