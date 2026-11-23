using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CoreBi.Engine.Tools.Drivers;

namespace CoreBi.Desktop;

/// <summary>Uma leitura da placa de números da janela (o primeiro não leva o fio à esquerda; Tip, quando há, completa o número).</summary>
public sealed record DriverDialogCell(string Label, string Value, string? Tip, bool First);

/// <summary>
/// Janela de Fazer backup e Restaurar backup dos Drivers, do começo ao fim. Confirmar: o backup mostra a pasta onde salvar (trocável
/// aqui mesmo); a restauração mostra o backup achado (quantos drivers, de quando, de qual PC) e só libera o botão quando há um.
/// Depois do clique a janela não fecha: a placa vira o andamento (a contagem e a faixa de células, uma por driver) e, no fim, o
/// resultado com o visto. Enquanto trabalha, Esc e o clique fora não fazem nada; o rodapé tem só Cancelar (ou Parar depois do atual).
/// </summary>
public partial class DriverBackupDialog : UserControl
{
    private enum Stage { Ask, Work, Done }

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private TaskCompletionSource<bool>? _pending;
    private bool _restore;
    private string _folder = "";
    private DriverBackupInfo? _found;
    private int _count;
    private DriverBackupInfo? _last;
    private Stage _stage;
    private Action? _stop;
    private string? _openPath;
    private int _installed, _current, _failed;
    /// <summary>Confirmação simples (reinstalar o driver da placa, instalar do Windows Update): só a placa de números, e fecha ao confirmar.</summary>
    private bool _simple;
    private bool _closing;

    public DriverBackupDialog() => InitializeComponent();

    // ---------------- 1. Confirmar ----------------

    /// <summary>
    /// Backup: a pasta onde salvar (o CoreBi cria uma pasta datada dentro dela). Devolve a pasta escolhida, ou null; com a pasta,
    /// a janela continua aberta, já no andamento.
    /// </summary>
    public async Task<string?> AskBackupAsync(string folder, int count, DriverBackupInfo? last)
    {
        _restore = false;
        _simple = false;
        _folder = folder;
        _count = count;
        _last = last;
        BackupTexts();
        ShowState();
        return await OpenAsync() ? _folder : null;
    }

    /// <summary>Restaurar: o backup da pasta (ou o mais novo dentro dela). Devolve o backup escolhido, ou null; com ele, a janela segue aberta.</summary>
    public async Task<DriverBackupInfo?> AskRestoreAsync(string folder)
    {
        _restore = true;
        _simple = false;
        _folder = folder;
        _found = folder.Length > 0 ? await Task.Run(() => DriverBackup.Find(folder)) : null;
        RestoreTexts();
        ShowState();
        return await OpenAsync() ? _found : null;
    }

    private void BackupTexts()
    {
        SetStage(Stage.Ask);
        DialogTitle.Text = "Fazer backup dos drivers";
        Lead.Text = "Guarda uma cópia dos drivers deste PC, para reinstalar tudo depois de formatar.";
        FolderLabel.Text = "Onde salvar";
        BtnFolder.Content = "Trocar";
        BtnFolder.ToolTip = "Escolha outra pasta, de preferência em outro disco ou num pen drive.";
        BtnConfirm.Content = "Fazer backup";
        BtnConfirm.ToolTip = "Só copia: nada é instalado nem apagado. O CoreBi cria uma pasta nova, com a data, dentro da escolhida.";
        Order(folderFirst: false);
    }

    private void RestoreTexts()
    {
        SetStage(Stage.Ask);
        DialogTitle.Text = "Restaurar backup dos drivers";
        Lead.Text = "Reinstala os drivers de um backup do CoreBi.\nOnde você já tiver um driver mais novo, o Windows mantém o seu.";
        FolderLabel.Text = "Backup";
        BtnFolder.Content = "Escolher";
        BtnFolder.ToolTip = "Escolha a pasta do backup, ou a pasta que guarda vários (vale o mais novo).";
        Order(folderFirst: true);
    }

    /// <summary>Backup: números, depois a pasta (o quê, depois onde). Restauração: a pasta, depois os números (de onde, depois o quê).</summary>
    private void Order(bool folderFirst)
    {
        Body.Children.Clear();
        FolderBlock.Visibility = Visibility.Visible;
        Body.Children.Add(folderFirst ? FolderBlock : Plate);
        Body.Children.Add(folderFirst ? Plate : FolderBlock);
        FolderBlock.Margin = new Thickness(0, folderFirst ? 0 : 20, 0, 0);
        Plate.Margin = new Thickness(0, folderFirst ? 20 : 0, 0, 0);
    }

    /// <summary>
    /// Confirmação de uma ação da página na mesma moldura (no lugar da caixa de mensagem do Windows): título, a frase do que
    /// acontece, a placa de números e o rodapé com Voltar e a ação. Devolve se confirmou; a janela fecha nos dois casos.
    /// </summary>
    public Task<bool> ConfirmAsync(string title, string lead, string action, string? actionTip, params (string Label, string Value, string? Tip)[] cells)
    {
        _simple = true;
        _restore = false;
        SetStage(Stage.Ask);
        DialogTitle.Text = title;
        Lead.Text = lead;
        Body.Children.Clear();
        Body.Children.Add(Plate);
        Plate.Margin = new Thickness(0);
        SetCells(cells);
        BtnConfirm.Content = action;
        BtnConfirm.ToolTip = actionTip;
        BtnConfirm.IsEnabled = true;
        return OpenAsync();
    }

    private void ShowState()
    {
        ShowFolder();
        if (!_restore)
        {
            SetCells(("Drivers para copiar", _count > 0 ? _count.ToString(PtBr) : "—", null),
                     ("Último backup", _last == null ? "Nenhum" : _last.Created.ToString("dd/MM/yyyy", PtBr), _last == null ? "Este é o primeiro." : $"Feito {Ago(_last.Created)}."));
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
        SetCells(("Drivers", _found.Count.ToString(PtBr), null),
                 ("Feito em", _found.Created.ToString("dd/MM/yyyy", PtBr), $"Feito {Ago(_found.Created)}."),
                 ("Do PC", _found.Computer.Length == 0 ? "—" : samePc ? "Este PC" : _found.Computer,
                  _found.Computer.Length == 0 ? null : samePc ? $"Feito neste PC ({_found.Computer})." : $"Feito em outro PC: {_found.Computer}."));
        BtnConfirm.Content = _found.Count == 1 ? "Restaurar 1 driver" : $"Restaurar {_found.Count} drivers";
        BtnConfirm.IsEnabled = true;
        BtnConfirm.ToolTip = "Antes de começar, o CoreBi pede ao Windows um ponto de restauração. Leva alguns minutos, e a tela pode piscar na vez do driver de vídeo.";
    }

    /// <summary>
    /// O cartão da pasta: o nome em destaque, o disco e o caminho até ela embaixo, e a dica que lê o disco (só na confirmação). No
    /// backup, a dica diz se a pasta sobrevive a uma formatação (só fora do disco do Windows); na restauração, onde procurar.
    /// </summary>
    private void ShowFolder(string? shown = null)
    {
        string path = (shown ?? (_restore && _found != null ? _found.Folder : _folder)).TrimEnd(Path.DirectorySeparatorChar);
        bool any = path.Length > 0;
        FolderName.Text = any ? Path.GetFileName(path) is { Length: > 0 } name ? name : path : "Nenhuma pasta escolhida";
        FolderName.SetResourceReference(TextBlock.ForegroundProperty, any ? "Fg" : "Faint");
        FolderName.ToolTip = any ? path : null;
        FolderWhere.Text = any ? Where(path) : "";
        FolderWhere.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        FolderGlyph.SetResourceReference(TextBlock.ForegroundProperty, any ? "Storage" : "Faint");

        if (_stage != Stage.Ask) { Hint.Visibility = Visibility.Collapsed; return; }
        if (_restore)
        {
            // Com o backup achado, a placa embaixo já responde; a dica só ajuda enquanto não há um
            Hint.Text = "Pode ser a pasta do backup ou a que guarda vários: vale o mais novo.";
            Hint.Visibility = _found == null ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        bool systemDisk = any && SameRoot(path, Environment.SystemDirectory);
        Hint.Text = systemDisk
            ? "Este disco é apagado ao formatar: prefira outro disco ou um pen drive."
            : "Fora do disco do Windows, o backup continua aqui depois de formatar.";
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

    private void SetCells(params (string Label, string Value, string? Tip)[] cells)
    {
        Cells.ItemsSource = cells.Select((c, i) => new DriverDialogCell(c.Label, c.Value, c.Tip, i == 0)).ToList();
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

    // ---------------- 2. Trabalhando ----------------

    /// <summary>
    /// A janela muda para o andamento: os números dão lugar à contagem e à faixa, a frase de cima diz o que acontece e o rodapé
    /// fica só com Cancelar (backup) ou Parar depois do atual (restauração). Quem roda a tarefa diz como parar em <see cref="OnStop"/>.
    /// </summary>
    private void BeginWork(int total) => Glide(() => StartWork(total));

    private void StartWork(int total)
    {
        _installed = _current = _failed = 0;
        SetStage(Stage.Work);
        WorkLabel.Text = _restore ? "Drivers devolvidos" : "Drivers copiados";
        Strip.Reset(total);
        SetCount(0, total);
        Legend.Visibility = _restore ? Visibility.Visible : Visibility.Collapsed;
        SwatchInstalled.SetResourceReference(Border.BackgroundProperty, "Signal");
        SwatchCurrent.SetResourceReference(Border.BackgroundProperty, "Signal");
        UpdateLegend();
        if (_restore)
        {
            Lead.Text = "Criando um ponto de restauração do Windows, para desfazer se algo der errado. Pode levar até 1 minuto…";
            BtnStop.Content = "Parar depois do atual";
            BtnStop.ToolTip = "Termina o driver que está sendo instalado e para. O que já entrou continua instalado.";
        }
        else
        {
            Lead.Text = "Copiando os drivers para a pasta abaixo. Pode continuar usando o PC.";
            BtnStop.Content = "Cancelar";
            BtnStop.ToolTip = "Para a cópia e apaga a pasta pela metade.";
        }
        ShowFolder(_restore ? _found?.Folder : _folder);
        CrossFade(WorkPane);
    }

    /// <summary>Quem roda a tarefa diz como parar (o botão do rodapé chama).</summary>
    public void OnStop(Action stop) => _stop = stop;

    /// <summary>Andamento: <paramref name="done"/> de <paramref name="total"/>; na restauração, o pacote da vez.</summary>
    public void Progress(int done, int total, string? item = null)
    {
        if (_stage != Stage.Work) return;
        if (total > 0 && total != Strip.Total) Strip.Reset(total);
        Strip.SetDone(done);
        SetCount(done, total);
        if (_restore && item != null)
        {
            Strip.SetCurrent(done < total ? done : -1);
            Lead.Text = done < total ? $"Devolvendo {(item.StartsWith("driver ", StringComparison.Ordinal) ? "o " : "")}{item} ao Windows…" : "Terminando…";
        }
    }

    /// <summary>O que aconteceu com o pacote da posição <paramref name="index"/>: pinta a célula e soma na legenda.</summary>
    public void Mark(int index, DriverRestoreOutcome outcome)
    {
        if (_stage != Stage.Work) return;
        Strip.Mark(index, outcome);
        switch (outcome)
        {
            case DriverRestoreOutcome.Installed: _installed++; break;
            case DriverRestoreOutcome.Current: _current++; break;
            default: _failed++; break;
        }
        UpdateLegend();
    }

    private void SetCount(int done, int total)
    {
        WorkDone.Text = done.ToString(PtBr);
        WorkOf.Text = total > 0 ? $" de {total.ToString(PtBr)}" : "";
    }

    private void UpdateLegend()
    {
        LegendInstalled.Text = _installed.ToString(PtBr);
        LegendCurrent.Text = _current.ToString(PtBr);
        LegendFailed.Text = _failed.ToString(PtBr);
        LegendFailedItem.Visibility = _failed > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- 3. Pronto ----------------

    /// <summary>Backup pronto: a faixa varre para o verde, o visto entra, a pasta nova aparece com o botão de abrir.</summary>
    public void BackupDone(DriverBackupInfo info, string size)
    {
        if (Strip.Total != info.Count) Strip.Reset(info.Count);
        Strip.SetDone(info.Count);
        SetCount(info.Count, info.Count);
        _openPath = info.Folder;
        bool systemDisk = SameRoot(info.Folder, Environment.SystemDirectory);
        Glide(() =>
        {
            Finish(ok: true, "Backup pronto",
                $"{info.Count} drivers copiados ({size}).\n" + (systemDisk
                    ? "Antes de formatar, leve a pasta para outro disco ou um pen drive."
                    : "A pasta fica fora do disco do Windows e sobrevive à formatação."));
            FolderLabel.Text = "Salvo em";
            ShowFolder(info.Folder);
        });
    }

    /// <summary>Restauração terminada (ou parada): as contas na legenda, o visto (ou o alerta) e o que falta fazer.</summary>
    public void RestoreDone(DriverRestoreResult result, string restorePoint)
    {
        int done = result.Installed + result.Current + result.Failed.Count;
        Strip.SetDone(done);
        SetCount(done, result.Total);
        string next = result.Reboot ? "\nReinicie o PC para terminar de instalar." : "";
        int failed = result.Failed.Count;
        Glide(() => RestoreFinish(result, failed, done, next));
        LegendFailedItem.ToolTip = result.Failed.Count > 0 ? string.Join("\n", result.Failed) : null;
        Lead.ToolTip = restorePoint.Length > 0 ? restorePoint : null;
    }

    private void RestoreFinish(DriverRestoreResult result, int failed, int done, string next)
    {
        if (result.Canceled)
            Finish(ok: failed == 0, "Restauração parada", $"Parou em {done} de {result.Total}. O que já entrou continua instalado.{next}", sweep: false);
        else if (failed > 0)
            Finish(ok: false, "Restauração terminou com erros",
                (failed == 1 ? "1 driver não entrou" : $"{failed} drivers não entraram") + " (passe o mouse em Com erro para ver)." + next);
        else
            Finish(ok: true, "Drivers restaurados", (result.Installed == 0 ? "Este PC já tinha todos os drivers do backup." : "Tudo certo: os drivers do backup estão no Windows.") + next);
    }

    /// <summary>A tarefa não terminou: a janela diz por quê, com o alerta, e fica só o Concluir.</summary>
    public void Failed(string title, string why) => Glide(() => Finish(ok: false, title, why, sweep: false));

    private void Finish(bool ok, string title, string lead, bool sweep = true)
    {
        SetStage(Stage.Done);
        DialogTitle.Text = title;
        Lead.Text = lead;
        TitleMark.Text = ok ? "" : "";
        TitleMark.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Ok" : "Warn");
        TitleMark.Visibility = Visibility.Visible;
        if (sweep)
        {
            // A legenda acompanha a varredura: as cores dela continuam sendo as da faixa
            Strip.Complete();
            SwatchInstalled.SetResourceReference(Border.BackgroundProperty, "Ok");
            SwatchCurrent.SetResourceReference(Border.BackgroundProperty, "Ok");
        }
        if (SystemParameters.ClientAreaAnimation && IsVisible)
        {
            var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
            TitleMarkScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            TitleMarkScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            TitleMark.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }
        Dispatcher.BeginInvoke(() => { BtnDone.Focus(); Keyboard.Focus(BtnDone); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    // ---------------- Momentos ----------------

    /// <summary>Cada momento mostra só os seus botões e a sua placa.</summary>
    private void SetStage(Stage stage)
    {
        _stage = stage;
        bool ask = stage == Stage.Ask, work = stage == Stage.Work, done = stage == Stage.Done;
        BtnBack.Visibility = ask ? Visibility.Visible : Visibility.Collapsed;
        BtnConfirm.Visibility = ask ? Visibility.Visible : Visibility.Collapsed;
        BtnStop.Visibility = work ? Visibility.Visible : Visibility.Collapsed;
        BtnStop.IsEnabled = true;
        BtnDone.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
        // Enquanto trabalha não há como fechar; o X guarda o lugar para o título não andar
        BtnClose.Visibility = work ? Visibility.Hidden : Visibility.Visible;
        AskPane.Visibility = ask ? Visibility.Visible : Visibility.Collapsed;
        WorkPane.Visibility = ask ? Visibility.Collapsed : Visibility.Visible;
        if (ask)
        {
            _openPath = null;
            _stop = null;
            TitleMark.Visibility = Visibility.Collapsed;
            Lead.ToolTip = null;
            LegendFailedItem.ToolTip = null;
        }
        else Hint.Visibility = Visibility.Collapsed;
        // A pasta: trocar na confirmação, nada durante o trabalho, abrir quando o backup fica pronto
        if (done && !_restore)
        {
            BtnFolder.Content = "Abrir";
            BtnFolder.ToolTip = "Abre a pasta do backup no Explorador de Arquivos.";
        }
        BtnFolder.Visibility = ask || (done && !_restore) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Troca de momento sem pulo: a moldura desliza da altura de antes até a nova (260 ms), centrada, em vez de saltar quando a
    /// dica some ou a frase ganha uma linha. O conteúdo fica recortado na moldura enquanto ela anda.
    /// </summary>
    private void Glide(Action change)
    {
        if (!SystemParameters.ClientAreaAnimation || !Panel.IsVisible || Panel.ActualHeight <= 0) { change(); return; }
        double from = Panel.ActualHeight;
        change();
        // A altura nova sai de um passo de layout síncrono, antes de qualquer quadro ser desenhado com ela
        Panel.BeginAnimation(HeightProperty, null);
        Panel.UpdateLayout();
        double to = Panel.ActualHeight;
        if (Math.Abs(to - from) < 1) return;
        PanelContent.ClipToBounds = Body.ClipToBounds = true;
        var glide = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        glide.Completed += (_, _) => PanelContent.ClipToBounds = Body.ClipToBounds = false;
        Panel.BeginAnimation(HeightProperty, glide);
    }

    private static void CrossFade(UIElement element)
    {
        if (!SystemParameters.ClientAreaAnimation || !element.IsVisible) return;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private async void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == Stage.Done)
        {
            if (_openPath == null) return;
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{_openPath}\"")
                { UseShellExecute = false })?.Dispose();
            }
            catch (Exception ex) { Lead.Text = "Não foi possível abrir a pasta: " + ex.Message; }
            return;
        }
        if (_stage != Stage.Ask) return;
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
        _closing = false;
        Panel.BeginAnimation(HeightProperty, null);
        Visibility = Visibility.Visible;
        PageMotion.EnterDialog(Scrim, Panel, PanelShift);
        Dispatcher.BeginInvoke(() => { if (BtnConfirm.IsEnabled) { BtnConfirm.Focus(); Keyboard.Focus(BtnConfirm); } else BtnFolder.Focus(); },
            System.Windows.Threading.DispatcherPriority.Input);
        return _pending.Task;
    }

    /// <summary>Confirmou: a janela fica aberta e passa ao andamento; quem chamou começa a tarefa.</summary>
    private void Confirm()
    {
        if (_stage != Stage.Ask || !BtnConfirm.IsEnabled) return;
        if (_simple) { Close(confirmed: true); return; }
        BeginWork(_restore ? _found?.Count ?? 0 : _count);
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(true);
    }

    /// <summary>Fecha a janela (fora do trabalho, ou pelo código quando a tarefa terminou de outro jeito).</summary>
    private void Close(bool confirmed = false, bool animate = true)
    {
        _stop = null;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(confirmed);
        if (!animate || !SystemParameters.ClientAreaAnimation || !IsVisible) { Visibility = Visibility.Collapsed; return; }
        // Sai como entrou, mais rápido: a moldura desce 6 px e some com o fundo (120 ms); quem abrir de novo antes disso cancela a saída
        _closing = true;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease };
        fade.Completed += (_, _) => { if (_closing) { _closing = false; Visibility = Visibility.Collapsed; } };
        Scrim.BeginAnimation(OpacityProperty, fade);
        PanelShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 6, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease });
    }

    /// <summary>Fecha na hora, sem a saída animada (as capturas de tela dos testes).</summary>
    internal void Cancel() => Close(animate: false);

    /// <summary>Fecha com a saída animada (a tarefa foi cancelada).</summary>
    internal void Dismiss() => Close();

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

    /// <summary>
    /// Capturas dos testes: o andamento (<paramref name="done"/> falso) ou o fim, com dados de exemplo, sem copiar nem instalar nada.
    /// A restauração pinta a faixa com instalados, já em dia e um erro, para mostrar as três cores.
    /// </summary>
    internal void PreviewStage(bool restore, bool done, string root, DriverBackupInfo backup)
    {
        Preview(restore, restore ? backup.Folder : root, backup.Count, restore ? backup : null);
        _pending = null;
        int total = backup.Count;
        BeginWork(total);
        int upTo = done ? total : total * 3 / 5;
        if (restore)
        {
            for (int i = 0; i < upTo; i++)
                Mark(i, i == 23 ? DriverRestoreOutcome.Failed : i % 3 == 0 ? DriverRestoreOutcome.Installed : DriverRestoreOutcome.Current);
            Strip.SetDone(upTo, animate: false);
            SetCount(upTo, total);
            if (!done)
            {
                Strip.SetCurrent(upTo);
                Progress(upTo, total, "driver de som da NVIDIA");
            }
            else RestoreDone(new DriverRestoreResult(total, _installed, _current, new[] { "driver de som da Realtek (rtkvhd64.inf, código 2)" }, true, false), "");
        }
        else if (!done)
        {
            Strip.SetDone(upTo, animate: false);
            SetCount(upTo, total);
        }
        else
        {
            string path = Path.Combine(root, $"Drivers {Environment.MachineName} {DateTime.Now:yyyy-MM-dd HH'h'mm}");
            BackupDone(backup with { Folder = path, Created = DateTime.Now }, "1,7 GB");
        }
        if (done)
        {
            Strip.SetDone(upTo, animate: false);
            Strip.Complete(animate: false);
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => Confirm();
    private void Cancel_Click(object sender, RoutedEventArgs e) { if (_stage != Stage.Work) Close(); }
    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_stage != Stage.Work) return;
        BtnStop.IsEnabled = false;
        BtnStop.Content = _restore ? "Parando depois do atual…" : "Cancelando…";
        _stop?.Invoke();
    }

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { if (_stage != Stage.Work) Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button)
        {
            if (_stage == Stage.Ask && BtnConfirm.IsEnabled) Confirm();
            else if (_stage == Stage.Done) Close();
            e.Handled = true;
        }
    }

    private void Scrim_MouseDown(object sender, MouseButtonEventArgs e) { if (_stage != Stage.Work) Close(); }
    private void Panel_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
