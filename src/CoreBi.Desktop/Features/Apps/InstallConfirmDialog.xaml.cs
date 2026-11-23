using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CoreBi.Desktop.Views;

namespace CoreBi.Desktop;

/// <summary>Um app na janela de confirmação: a posição na fila e a linha do catálogo (ícone, nome, categoria).</summary>
public sealed record InstallConfirmItem(int Order, AppTile Tile)
{
    public string Tip => Tile.Tip;
}

/// <summary>
/// Confirmação antes de instalar, sobre o app (como a janela de exportar relatório): o pacote inteiro, na ordem da fila, com o
/// ícone e a categoria de cada um, e o botão que diz quantos vão. Esc, Voltar ou clique fora cancelam; Enter confirma.
/// </summary>
public partial class InstallConfirmDialog : UserControl
{
    private TaskCompletionSource<bool>? _pending;

    public InstallConfirmDialog() => InitializeComponent();

    /// <summary>Abre com o pacote e espera a resposta: true para instalar.</summary>
    public Task<bool> AskAsync(IReadOnlyList<AppTile> package)
    {
        _pending?.TrySetResult(false);
        _pending = new TaskCompletionSource<bool>();
        int n = package.Count;
        Items.ItemsSource = package.Select((t, i) => new InstallConfirmItem(i + 1, t)).ToList();
        CountRun.Text = n.ToString();
        BtnConfirm.Content = n == 1 ? "Instalar 1 app" : $"Instalar {n} apps";
        Lead.Text = n == 1
            ? "Confira o app: ele instala sem abrir janelas."
            : $"Confira os {n} apps: eles instalam um por vez, nesta ordem, sem abrir janelas. Se parar, o app em andamento termina antes.";

        Visibility = Visibility.Visible;
        Enter();
        Dispatcher.BeginInvoke(() => { BtnConfirm.Focus(); Keyboard.Focus(BtnConfirm); }, System.Windows.Threading.DispatcherPriority.Input);
        return _pending.Task;
    }

    /// <summary>A mesma entrada da janela de relatório: opacidade e 10 px de subida.</summary>
    private void Enter()
    {
        PageMotion.EnterDialog(Scrim, Panel, PanelShift);
    }

    private void Close(bool confirmed)
    {
        Visibility = Visibility.Collapsed;
        Items.ItemsSource = null;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(confirmed);
    }

    /// <summary>Fecha sem instalar (Esc, clique fora, ou as capturas de tela dos testes).</summary>
    internal void Cancel() => Close(false);

    private void Confirm_Click(object sender, RoutedEventArgs e) => Close(true);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(false);

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(false); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button) { Close(true); e.Handled = true; }
    }

    // Clique fora do painel cancela; dentro dele, não
    private void Scrim_MouseDown(object sender, MouseButtonEventArgs e) => Close(false);
    private void Panel_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
