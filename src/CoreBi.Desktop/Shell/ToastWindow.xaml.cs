using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CoreBi.Desktop;

/// <summary>Aviso no canto inferior direito da tela (dispositivo conectado/removido). Não rouba o foco.</summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(5) };

    public ToastWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
            Win32Window.AddExtendedStyles(new WindowInteropHelper(this).Handle, Win32Window.WsExToolWindow | Win32Window.WsExNoActivate);
        SizeChanged += (_, _) => Place();
        Closed += (_, _) => _hide.Stop();
        _hide.Tick += (_, _) => { _hide.Stop(); Fade(0, () => Hide()); };
    }

    public void Notify(string title, string text, string icon)
    {
        TTitle.Text = title;
        TText.Text = text;
        TIcon.Text = icon;
        if (!IsVisible) { Show(); Place(); }
        Fade(1, null);
        _hide.Stop();
        _hide.Start();
    }

    private void Place()
    {
        // A borda transparente de 12 px abriga a sombra; o cartão em si fica a 16 px da borda da tela.
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 4;
        Top = wa.Bottom - ActualHeight - 4;
    }

    private void Fade(double to, Action? done)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = to;
            done?.Invoke();
            return;
        }
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(180));
        if (done != null) anim.Completed += (_, _) => done();
        BeginAnimation(OpacityProperty, anim);
    }
}
