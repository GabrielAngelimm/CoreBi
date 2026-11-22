using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CoreBi.Engine.Persistence;

namespace CoreBi.Desktop;

/// <summary>Uma parte do relatório que a pessoa pode tirar ou pôr: uma ficha de Sistema ou um bloco do diagnóstico.</summary>
public sealed class ReportPart : ObservableObject
{
    private bool _included;

    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Caption { get; init; }
    public required string Icon { get; init; }
    public string Channel { get; init; } = "Steel";
    /// <summary>Sem dado para levar (rede sem teste, nenhum benchmark): o cartão fica apagado e a parte não entra.</summary>
    public bool Available { get; init; } = true;
    public string? Tip { get; init; }

    public bool Included { get => _included; set => Set(ref _included, value); }
}

/// <summary>O que a janela devolve: as partes escolhidas, o formato e o arquivo.</summary>
public sealed record ReportChoice(IReadOnlySet<string> Parts, string Format, string Path, bool Open = true);

/// <summary>
/// Janela de exportação do relatório, sobre o app (como a paleta Ctrl + K): o que entra (fichas e diagnóstico, em cartões de
/// seleção) e o formato (HTML, PDF ou JSON). Onde salvar vem depois, na janela do Windows, com o nome pronto e a última pasta.
/// Abre com tudo o que tem dado marcado; lembra o formato, a pasta e "abrir depois de salvar" em AppSettings.
/// </summary>
public partial class ReportDialog : UserControl
{
    private TaskCompletionSource<ReportChoice?>? _pending;
    private List<ReportPart> _sheets = new();
    private List<ReportPart> _diag = new();
    private string _format = "html";
    private AppSettings? _settings;

    public ReportDialog() => InitializeComponent();

    /// <summary>Abre a janela e espera a escolha; null quando a pessoa cancela.</summary>
    public Task<ReportChoice?> AskAsync(AppSettings settings, IReadOnlyList<ReportPart> sheets, IReadOnlyList<ReportPart> diagnostics)
    {
        _pending?.TrySetResult(null);
        _pending = new TaskCompletionSource<ReportChoice?>();
        _settings = settings;

        _sheets = sheets.ToList();
        _diag = diagnostics.ToList();
        foreach (var p in _sheets.Concat(_diag))
        {
            // Abre sempre com tudo o que tem dado marcado; o que não tem fica apagado
            p.Included = p.Available;
            p.PropertyChanged += (_, _) => Refresh();
        }
        SheetParts.ItemsSource = _sheets;
        DiagParts.ItemsSource = _diag;

        OpenAfter.IsChecked = settings.ReportOpen;
        (settings.ReportFormat switch { "pdf" => FmtPdf, "json" => FmtJson, _ => FmtHtml }).IsChecked = true;
        SetFormat(settings.ReportFormat);
        Refresh();

        Visibility = Visibility.Visible;
        Enter();
        Dispatcher.BeginInvoke(() => { BtnExport.Focus(); Keyboard.Focus(BtnExport); }, System.Windows.Threading.DispatcherPriority.Input);
        return _pending.Task;
    }

    /// <summary>Entrada curta, a mesma das páginas: opacidade e 10 px de subida, uma vez só.</summary>
    private void Enter()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        PanelShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease });
    }

    private void Close(ReportChoice? choice)
    {
        Visibility = Visibility.Collapsed;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(choice);
    }

    private void SetFormat(string format)
    {
        _format = format is "pdf" or "json" ? format : "html";
    }

    /// <summary>O texto dos atalhos de seção e se dá para exportar.</summary>
    private void Refresh()
    {
        int sheets = _sheets.Count(p => p.Included), diag = _diag.Count(p => p.Included);
        ToggleSheets.Content = _sheets.Where(p => p.Available).All(p => p.Included) ? "Desmarcar todas" : "Marcar todas";
        ToggleDiag.Content = _diag.Where(p => p.Available).All(p => p.Included) ? "Desmarcar todos" : "Marcar todos";
        ToggleDiag.Visibility = _diag.Any(p => p.Available) ? Visibility.Visible : Visibility.Collapsed;

        bool any = sheets + diag > 0;
        BtnExport.IsEnabled = any;
        BtnExport.ToolTip = any ? null : "Escolha pelo menos uma parte";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!BtnExport.IsEnabled) return;
        // Onde salvar: a janela do Windows, só com o formato escolhido, o nome pronto e a pasta da última vez.
        // Nome que se lê na pasta: o que é, de qual computador e de quando ("Relatório CoreBi · PC-ANGELIM · 22-11-2026 02h07")
        string folder = _settings?.ReportFolder is { Length: > 0 } f && Directory.Exists(f) ? f : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Salvar relatório",
            Filter = _format switch { "pdf" => "PDF (A4)|*.pdf", "json" => "Dados (JSON)|*.json", _ => "Página para ler e imprimir (HTML)|*.html" },
            DefaultExt = "." + _format,
            AddExtension = true,
            InitialDirectory = folder,
            FileName = $"Relatório CoreBi · {Environment.MachineName} · {DateTime.Now:dd-MM-yyyy HH'h'mm}",
        };
        if (save.ShowDialog(Window.GetWindow(this)) != true) return;   // volta para a janela de exportação, com a escolha intacta
        string path = save.FileName;
        if (!path.EndsWith("." + _format, StringComparison.OrdinalIgnoreCase)) path += "." + _format;

        var parts = _sheets.Concat(_diag).Where(p => p.Included).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        if (_settings != null)
        {
            _settings.ReportFormat = _format;
            _settings.ReportFolder = Path.GetDirectoryName(path);
            _settings.ReportOpen = OpenAfter.IsChecked == true;
        }
        Close(new ReportChoice(parts, _format, path, OpenAfter.IsChecked == true));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(null);

    /// <summary>Fecha sem exportar (Esc, clique fora, ou as capturas de tela dos testes).</summary>
    internal void Cancel() => Close(null);

    private void Format_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string format }) SetFormat(format);
    }

    private void ToggleSheets_Click(object sender, RoutedEventArgs e) => ToggleAll(_sheets);
    private void ToggleDiag_Click(object sender, RoutedEventArgs e) => ToggleAll(_diag);

    private static void ToggleAll(List<ReportPart> parts)
    {
        var open = parts.Where(p => p.Available).ToList();
        bool value = !open.All(p => p.Included);
        foreach (var p in open) p.Included = value;
    }

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button and not CheckBox and not RadioButton) { Export_Click(this, e); e.Handled = true; }
    }

    // Clique fora do painel fecha; dentro dele, não
    private void Scrim_MouseDown(object sender, MouseButtonEventArgs e) => Close(null);
    private void Panel_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
