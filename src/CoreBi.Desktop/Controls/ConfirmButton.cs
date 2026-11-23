using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CoreBi.Desktop;

/// <summary>
/// Confirmação no próprio botão, para ações que não se desfazem (limpar o histórico): o primeiro clique arma o botão (Armed),
/// e o template troca "Limpar" por "Confirmar" no tom de perigo, com fundo, num esmaecer cruzado, sem mudar de tamanho (estilo
/// <c>HistoryClear</c>); o segundo clique, em até 5 s, executa. Sem o segundo clique, ou com Esc, ou com o foco indo para outro
/// lugar, o botão volta ao normal. O mouse pode sair e voltar: a confirmação espera. Nada de janela no meio da tela.
/// </summary>
public static class ConfirmButton
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    public static readonly DependencyProperty ArmedProperty = DependencyProperty.RegisterAttached(
        "Armed", typeof(bool), typeof(ConfirmButton), new FrameworkPropertyMetadata(false));
    public static bool GetArmed(DependencyObject d) => (bool)d.GetValue(ArmedProperty);
    public static void SetArmed(DependencyObject d, bool value) => d.SetValue(ArmedProperty, value);

    private static readonly DependencyProperty TimerProperty = DependencyProperty.RegisterAttached(
        "Timer", typeof(DispatcherTimer), typeof(ConfirmButton), new FrameworkPropertyMetadata(null));

    /// <summary>true no segundo clique (o botão volta ao normal e a ação segue); false no primeiro, que só arma o botão.</summary>
    public static bool Confirm(object sender)
    {
        if (sender is not Button button) return true;
        if (GetArmed(button))
        {
            Disarm(button);
            return true;
        }
        if (button.GetValue(TimerProperty) is not DispatcherTimer timer)
        {
            // Construtor sem o tratador: com ele o temporizador já nasceria ligado
            timer = new DispatcherTimer { Interval = Window };
            timer.Tick += (_, _) => Disarm(button);
            button.SetValue(TimerProperty, timer);
            button.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Escape || !GetArmed(button)) return;
                Disarm(button);
                e.Handled = true;
            };
            button.LostKeyboardFocus += (_, _) => { if (!button.IsMouseOver) Disarm(button); };
            button.IsVisibleChanged += (_, _) => { if (!button.IsVisible) Disarm(button); };
        }
        SetArmed(button, true);
        timer.Stop();
        timer.Start();
        return false;
    }

    private static void Disarm(Button button)
    {
        (button.GetValue(TimerProperty) as DispatcherTimer)?.Stop();
        SetArmed(button, false);
    }
}
