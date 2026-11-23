using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CoreBi.Engine.Tools.Drivers;

namespace CoreBi.Desktop;

/// <summary>Uma leitura da placa de números da janela, com a linha embaixo (o primeiro não leva o fio à esquerda).</summary>
public sealed record DriverDialogCell(string Label, string Value, string Sub, bool First);

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
        Lead.Text = "Uma cópia dos drivers das suas peças, para reinstalar tudo depois de formatar.";
        FolderLabel.Text = "Onde salvar";
        BtnFolder.Content = "Trocar";
        BtnFolder.ToolTip = "Escolha outra pasta, de preferência em outro disco ou num pen drive.";
        Promise.Text = "Só copia: nada é instalado nem apagado.";
        BtnConfirm.Content = "Fazer backup";
        BtnConfirm.ToolTip = "O CoreBi cria uma pasta nova, com a data, dentro da escolhida.";
        SetIcon(BtnConfirm, "");
        Order(folderFirst: false);
        ShowState();
        return await OpenAsync() ? _folder : null;
    }

    /// <summary>Restaurar: o backup da pasta (ou o mais novo dentro dela). Devolve o backup escolhido, ou null.</summary>
    public async Task<DriverBackupInfo?> AskRestoreAsync(string folder)
    {
        _restore = true;
        _folder = folder;
        _found = folder.Length > 0 ? await Task.Run(() => DriverBackup.Find(folder)) : null;
        RestoreTexts();
        ShowState();
        return await OpenAsync() ? _found : null;
    }

    private static void SetIcon(Button button, string glyph) => Ui.SetIcon(button, glyph);

    private void RestoreTexts()
    {
        DialogTitle.Text = "Restaurar drivers";
        Lead.Text = "Reinstala os drivers de um backup. O Windows só troca um driver quando o do backup é melhor.";
        FolderLabel.Text = "Backup";
        BtnFolder.Content = "Escolher";
        BtnFolder.ToolTip = "Escolha a pasta do backup, ou a pasta que guarda vários (vale o mais novo).";
        Promise.Text = "Antes de começar, o CoreBi cria um ponto de restauração.";
        SetIcon(BtnConfirm, "");
        Order(folderFirst: true);
    }

    /// <summary>Backup: números, depois a pasta (o quê, depois onde). Restauração: a pasta, depois os números (de onde, depois o quê).</summary>
    private void Order(bool folderFirst)
    {
        Body.Children.Clear();
        Body.Children.Add(folderFirst ? FolderBlock : Plate);
        Body.Children.Add(folderFirst ? Plate : FolderBlock);
        FolderBlock.Margin = new Thickness(0, folderFirst ? 0 : 20, 0, 0);
        Plate.Margin = new Thickness(0, folderFirst ? 20 : 0, 0, 0);
    }

    private void ShowState()
    {
        ShowFolder();
        if (!_restore)
        {
            SetCells(("Drivers para copiar", _count > 0 ? _count.ToString(PtBr) : "—", "das suas peças"),
                     ("Último backup", _last == null ? "Nenhum" : _last.Created.ToString("dd/MM/yyyy", PtBr), _last == null ? "este é o primeiro" : Ago(_last.Created)));
            BtnConfirm.IsEnabled = _folder.Length > 0;
            return;
        }
        if (_found == null)
        {
            SetCells();
            CellsEmpty.Text = _folder.Length == 0 ? "Escolha a pasta de um backup feito pelo CoreBi." : "Essa pasta não tem um backup de drivers do CoreBi.";
            BtnConfirm.Content = "Restaurar";
            BtnConfirm.IsEnabled = false;
            BtnConfirm.ToolTip = "Escolha a pasta de um backup primeiro.";
            return;
        }
        bool samePc = _found.Computer.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        SetCells(("Drivers", _found.Count.ToString(PtBr), "no backup"),
                 ("Feito em", _found.Created.ToString("dd/MM/yyyy", PtBr), Ago(_found.Created)),
                 ("PC", _found.Computer.Length > 0 ? _found.Computer : "—", _found.Computer.Length == 0 ? "" : samePc ? "este PC" : "outro PC"));
        BtnConfirm.Content = _found.Count == 1 ? "Restaurar 1 driver" : $"Restaurar {_found.Count} drivers";
        BtnConfirm.IsEnabled = true;
        BtnConfirm.ToolTip = "Leva alguns minutos. A tela pode piscar quando chegar a vez do driver de vídeo.";
    }

    /// <summary>
    /// O cartão da pasta: o nome em destaque, o disco e o caminho até ela embaixo, e a dica que lê o disco. No backup, a dica diz
    /// se a pasta sobrevive a uma formatação (só fora do disco do Windows); na restauração, onde procurar.
    /// </summary>
    private void ShowFolder()
    {
        string path = (_restore && _found != null ? _found.Folder : _folder).TrimEnd(Path.DirectorySeparatorChar);
        bool any = path.Length > 0;
        FolderName.Text = any ? Path.GetFileName(path) is { Length: > 0 } name ? name : path : "Nenhuma pasta escolhida";
        FolderName.SetResourceReference(TextBlock.ForegroundProperty, any ? "Fg" : "Faint");
        FolderName.ToolTip = any ? path : null;
        FolderWhere.Text = any ? Where(path) : "";
        FolderWhere.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        FolderGlyph.SetResourceReference(TextBlock.ForegroundProperty, any ? "Storage" : "Faint");

        Hint.Inlines.Clear();
        if (_restore)
        {
            Hint.Inlines.Add(new Run("Pode escolher a própria pasta do backup ou a pasta que guarda vários: vale o mais novo."));
            Hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            Hint.Visibility = Visibility.Visible;
            return;
        }
        bool systemDisk = any && SameRoot(path, Environment.SystemDirectory);
        var lead = new Run(systemDisk ? "Vai formatar? " : "Fora do disco do Windows: ") { FontWeight = FontWeights.SemiBold };
        lead.SetResourceReference(TextElement.ForegroundProperty, systemDisk ? "Warn" : "Ok");
        Hint.Inlines.Add(lead);
        Hint.Inlines.Add(new Run(systemDisk
            ? "Este disco é apagado junto. Troque para outro disco ou um pen drive."
            : "o backup continua aqui depois de formatar."));
        Hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Hint.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"Disco do Windows (C:) · Users › gabri › Documentos": o disco em palavras e as pastas até a escolhida.</summary>
    internal static string Where(string path)
    {
        string root = Path.GetPathRoot(path) ?? "";
        string parent = Path.GetDirectoryName(path) ?? "";
        string trail = parent.Length > root.Length ? parent[root.Length..].Replace(Path.DirectorySeparatorChar.ToString(), " › ") : "";
        string disk;
        try
        {
            var drive = new DriveInfo(root);
            string letter = root.TrimEnd(Path.DirectorySeparatorChar);
            disk = SameRoot(path, Environment.SystemDirectory) ? $"Disco do Windows ({letter})"
                : drive.DriveType == DriveType.Removable ? $"Pen drive {(drive.IsReady && drive.VolumeLabel.Length > 0 ? drive.VolumeLabel + " " : "")}({letter})"
                : drive.DriveType == DriveType.Network ? $"Rede ({letter})"
                : drive.IsReady && drive.VolumeLabel.Length > 0 ? $"{drive.VolumeLabel} ({letter})" : $"Disco {letter}";
        }
        catch { disk = root; }
        return trail.Length > 0 ? $"{disk} · {trail}" : disk;
    }

    private static bool SameRoot(string a, string b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    private void SetCells(params (string Label, string Value, string Sub)[] cells)
    {
        Cells.ItemsSource = cells.Select((c, i) => new DriverDialogCell(c.Label, c.Value, c.Sub, i == 0)).ToList();
        Cells.Visibility = cells.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CellsEmpty.Visibility = cells.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>"hoje", "ontem", "há 3 dias", "há 2 meses".</summary>
    internal static string Ago(DateTime at)
    {
        int days = (DateTime.Today - at.Date).Days;
        return days <= 0 ? "hoje" : days == 1 ? "ontem" : days < 31 ? $"há {days} dias" : days < 60 ? "há 1 mês" : days < 365 ? $"há {days / 30} meses"
            : days < 730 ? "há 1 ano" : $"há {days / 365} anos";
    }

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
            RestoreTexts();
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
