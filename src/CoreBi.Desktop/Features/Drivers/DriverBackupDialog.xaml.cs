using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CoreBi.Engine.Tools.Drivers;

namespace CoreBi.Desktop;

/// <summary>Uma leitura da placa de números da janela (o primeiro não leva o fio à esquerda).</summary>
public sealed record DriverDialogCell(string Label, string Value, bool First);

/// <summary>
/// Janela de Fazer backup e Restaurar dos Drivers. O backup confirma a pasta onde salvar (trocável aqui mesmo); a restauração
/// mostra o backup achado na pasta (quantos drivers, de quando, de qual PC) e só libera o botão quando há um. Esc, Voltar ou clique
/// fora cancelam; Enter confirma.
/// </summary>
public partial class DriverBackupDialog : UserControl
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private TaskCompletionSource<bool>? _pending;
    private bool _restore;
    private string _folder = "";
    private DriverBackupInfo? _found;
    private int _count;
    private DriverBackupInfo? _last;

    public DriverBackupDialog() => InitializeComponent();

    /// <summary>Backup: a pasta onde salvar (o CoreBi cria uma pasta datada dentro dela). Devolve a pasta escolhida, ou null.</summary>
    public async Task<string?> AskBackupAsync(string folder, int count, DriverBackupInfo? last)
    {
        _restore = false;
        _folder = folder;
        _count = count;
        _last = last;
        DialogTitle.Text = "Fazer backup dos drivers";
        Lead.Text = "Copia os drivers que vieram dos fabricantes das suas peças (vídeo, áudio, rede, chipset). Nada é instalado nem apagado.";
        FolderLabel.Text = "Salvar em";
        BtnFolder.Content = "Trocar pasta";
        Promise.Text = "O CoreBi cria uma pasta nova, com a data, dentro da escolhida.";
        BtnConfirm.Content = "Fazer backup";
        SetIcon(BtnConfirm, "");
        ShowState();
        return await OpenAsync() ? _folder : null;
    }

    /// <summary>Restaurar: o backup da pasta (ou o mais novo dentro dela). Devolve o backup escolhido, ou null.</summary>
    public async Task<DriverBackupInfo?> AskRestoreAsync(string folder)
    {
        _restore = true;
        _folder = folder;
        _found = folder.Length > 0 ? await Task.Run(() => DriverBackup.Find(folder)) : null;
        DialogTitle.Text = "Restaurar drivers";
        Lead.Text = "Reinstala os drivers de um backup. O Windows só usa um driver do backup quando ele é melhor que o que já está no PC.";
        FolderLabel.Text = "Backup";
        BtnFolder.Content = "Escolher pasta";
        Promise.Text = "Antes de começar, o CoreBi cria um ponto de restauração do Windows.";
        SetIcon(BtnConfirm, "");
        ShowState();
        return await OpenAsync() ? _found : null;
    }

    private static void SetIcon(Button button, string glyph) => Ui.SetIcon(button, glyph);

    private void ShowState()
    {
        FolderText.Text = _folder.Length > 0 ? _folder : "Nenhuma pasta escolhida";
        FolderText.ToolTip = _folder.Length > 0 ? _folder : null;
        FolderText.Foreground = (Brush)FindResource(_folder.Length > 0 ? "Fg" : "Faint");
        if (!_restore)
        {
            SetCells(("Drivers para copiar", _count > 0 ? _count.ToString(PtBr) : "—"),
                     ("Último backup", _last == null ? "nenhum" : Day(_last.Created)));
            Hint.Text = "Vai formatar? Salve em outro disco ou num pen drive: o disco do Windows é apagado junto.";
            BtnConfirm.IsEnabled = _folder.Length > 0;
            return;
        }
        if (_found == null)
        {
            SetCells();
            CellsEmpty.Text = _folder.Length == 0 ? "Escolha a pasta de um backup feito pelo CoreBi." : "Essa pasta não tem um backup de drivers do CoreBi.";
            Hint.Text = "Pode escolher a própria pasta do backup ou a pasta que guarda vários: vale o mais novo.";
            BtnConfirm.Content = "Restaurar";
            BtnConfirm.IsEnabled = false;
            BtnConfirm.ToolTip = "Escolha a pasta de um backup primeiro.";
            return;
        }
        SetCells(("Drivers", _found.Count.ToString(PtBr)), ("Feito em", Day(_found.Created)),
                 ("PC", _found.Computer.Length > 0 ? _found.Computer : "—"));
        Hint.Text = "Leva alguns minutos. A tela pode piscar quando chegar a vez do driver de vídeo.";
        BtnConfirm.Content = _found.Count == 1 ? "Restaurar 1 driver" : $"Restaurar {_found.Count} drivers";
        BtnConfirm.IsEnabled = true;
        BtnConfirm.ToolTip = null;
    }

    private void SetCells(params (string Label, string Value)[] cells)
    {
        Cells.ItemsSource = cells.Select((c, i) => new DriverDialogCell(c.Label, c.Value, i == 0)).ToList();
        Cells.Visibility = cells.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CellsEmpty.Visibility = cells.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string Day(DateTime at) =>
        at.Date == DateTime.Today ? $"hoje, {at:HH:mm}" : at.Date == DateTime.Today.AddDays(-1) ? $"ontem, {at:HH:mm}" : at.ToString("dd/MM/yyyy", PtBr);

    private async void Folder_Click(object sender, RoutedEventArgs e)
    {
        string start = _folder.Length > 0 && Directory.Exists(_folder) ? _folder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var pick = new Microsoft.Win32.OpenFolderDialog
        {
            Title = _restore ? "Escolha a pasta do backup de drivers" : "Onde salvar o backup dos drivers",
            InitialDirectory = start,
        };
        if (pick.ShowDialog(Window.GetWindow(this)) != true) return;
        _folder = pick.FolderName;
        if (_restore) _found = await Task.Run(() => DriverBackup.Find(_folder));
        ShowState();
    }

    private Task<bool> OpenAsync()
    {
        _pending?.TrySetResult(false);
        _pending = new TaskCompletionSource<bool>();
        Visibility = Visibility.Visible;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        PanelShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease });
        Dispatcher.BeginInvoke(() => { if (BtnConfirm.IsEnabled) { BtnConfirm.Focus(); Keyboard.Focus(BtnConfirm); } else BtnFolder.Focus(); },
            System.Windows.Threading.DispatcherPriority.Input);
        return _pending.Task;
    }

    private void Close(bool confirmed)
    {
        Visibility = Visibility.Collapsed;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(confirmed);
    }

    /// <summary>Fecha sem fazer nada (Esc, clique fora, ou as capturas de tela dos testes).</summary>
    internal void Cancel() => Close(false);

    /// <summary>Capturas dos testes: a janela de backup ou de restauração com dados de exemplo, sem esperar resposta.</summary>
    internal void Preview(bool restore, string folder, int count, DriverBackupInfo? backup)
    {
        if (restore)
        {
            _restore = true;
            _folder = folder;
            _found = backup;
            DialogTitle.Text = "Restaurar drivers";
            Lead.Text = "Reinstala os drivers de um backup. O Windows só usa um driver do backup quando ele é melhor que o que já está no PC.";
            FolderLabel.Text = "Backup";
            BtnFolder.Content = "Escolher pasta";
            Promise.Text = "Antes de começar, o CoreBi cria um ponto de restauração do Windows.";
            SetIcon(BtnConfirm, "");
            ShowState();
        }
        else
        {
            _ = AskBackupAsync(folder, count, backup);
            return;
        }
        _ = OpenAsync();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) { if (BtnConfirm.IsEnabled) Close(true); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(false);

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(false); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button && BtnConfirm.IsEnabled) { Close(true); e.Handled = true; }
    }

    private void Scrim_MouseDown(object sender, MouseButtonEventArgs e) => Close(false);
    private void Panel_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
