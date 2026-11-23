using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Games;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Desktop.Views;

public partial class OverlayView : IDisposable
{
    private readonly RtssOverlayClient _rtssOverlay = new();
    private OverlayWindow? _overlay;
    private DispatcherTimer? _overlayTimer;
    private DispatcherTimer? _opacitySave;
    private RtssStatus _rtssStatus = RtssStatus.Unavailable;
    private bool _rtssShowing;
    private bool _suspendedForBenchmark;
    private int _rtssProcess;
    private long _rtssLastUpdate, _rtssLastAck, _rtssRetryAfter, _rtssPendingSince;
    private uint _rtssPublishedFrame;
    private long _rtssProbeAfter;
    private RtssStatus _rtssProbe = RtssStatus.Unavailable;
    private bool _rtssInstalled;

    internal void SuspendForBenchmark(bool suspended)
    {
        if (_suspendedForBenchmark == suspended) return;
        _suspendedForBenchmark = suspended;
        if (!IsOverlayEnabled) return;
        if (suspended) StopLiveOverlay();
        else StartLiveOverlay();
    }

    private void ChkOverlay_Changed(object sender, RoutedEventArgs e)
    {
        _context?.SyncFrameMonitoring();
        if (IsOverlayEnabled && !_suspendedForBenchmark) StartLiveOverlay();
        else StopLiveOverlay();
        UpdatePreview();
    }

    private void StartLiveOverlay()
    {
        _rtssRetryAfter = 0;
        _overlay ??= new OverlayWindow();
        ApplyOverlayOptions();
        _overlay.Show();
        _overlayTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal,
            (_, _) => UpdateOverlay(), Dispatcher);
        _overlayTimer.Start();
        UpdateOverlay();
    }

    private void StopLiveOverlay()
    {
        _overlayTimer?.Stop();
        ResetRtssSession();
        _overlay?.Hide();
        UpdateStatus(null);
    }

    private void ResetRtssSession(bool clearProcess = false)
    {
        _rtssOverlay.Clear();
        _rtssShowing = false;
        _rtssPublishedFrame = 0;
        _rtssLastAck = 0;
        _rtssPendingSince = 0;
        if (clearProcess)
        {
            _rtssLastUpdate = 0;
            _rtssProcess = 0;
        }
    }

    private void OvOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _context == null) return;
        _context.Settings.OverlayCorner = Ui.CheckedTag(OvCornerPanel) ?? "TL";
        _context.Settings.OverlayScale = double.Parse(Ui.CheckedTag(OvScalePanel) ?? "1", CultureInfo.InvariantCulture);
        _context.SaveSettings();
        ApplyOverlayOptions();
        LayoutPreview(_context.Settings.OverlayCorner, _context.Settings.OverlayScale);
    }

    private void OnOpacityChanged(double opacity)
    {
        if (!_ready || _context == null) return;
        _context.Settings.OverlayOpacity = opacity;
        if (_overlay != null) _overlay.Opacity = opacity;
        // Arrastar dispara dezenas de mudanças por segundo: o overlay acompanha na hora, mas o disco só é gravado quando o movimento para.
        _opacitySave ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _opacitySave.Tick -= OpacitySave_Tick;
        _opacitySave.Tick += OpacitySave_Tick;
        _opacitySave.Stop();
        _opacitySave.Start();
    }

    private void OpacitySave_Tick(object? sender, EventArgs e)
    {
        _opacitySave?.Stop();
        _context?.SaveSettings();
    }

    private void ApplyOverlayOptions()
    {
        if (_overlay == null || _context == null) return;
        _overlay.Corner = _context.Settings.OverlayCorner;
        _overlay.SetScale(_context.Settings.OverlayScale);
        _overlay.Opacity = _context.Settings.OverlayOpacity;
        _overlay.Reposition();
    }

    private void UpdateOverlay()
    {
        if (_context == null || !IsOverlayEnabled || _suspendedForBenchmark) return;
        var frames = _context.Frames.GetForegroundStats();
        var lines = BuildLines();
        string? extra = ShowItems.Contains("clock") ? DateTime.Now.ToString("HH:mm") : null;
        _overlay?.Update(frames, lines, ShowItems, extra);
        UpdateFullscreenOverlay(frames);
        if (_overlay != null)
        {
            if (_rtssShowing)
            {
                if (_overlay.IsVisible) _overlay.Hide();
            }
            else
            {
                if (!_overlay.IsVisible) _overlay.Show();
                _overlay.KeepOnTop();
            }
        }
        UpdateStatus(frames);
    }

    private void UpdateFullscreenOverlay(FrameStats? frames)
    {
        if (_context == null) return;
        long now = Environment.TickCount64;
        int pid = OverlayTarget.FullscreenForegroundProcess(out int viewportWidth, out int viewportHeight);
        if (pid == 0 || pid != _rtssProcess)
        {
            ResetRtssSession(clearProcess: true);
            _rtssProcess = pid;
        }
        if (pid == 0 || now < _rtssRetryAfter) return;

        var status = _rtssOverlay.Inspect(pid, out var target);
        if (status == RtssStatus.Ready && target.Active)
        {
            if (frames?.ProcessId != pid) frames = null;
            if (_overlay == null) return;
            status = _rtssOverlay.Publish(RtssOverlayFormatter.Format(_overlay.Panel,
                _context.Settings.OverlayCorner, _context.Settings.OverlayScale, _context.Settings.OverlayOpacity,
                viewportWidth, viewportHeight, frames == null));
            if (status == RtssStatus.Ready)
            {
                _rtssLastUpdate = now;
                if (_rtssPendingSince == 0) _rtssPendingSince = now;
                // Só oculta a janela WPF depois que o jogo consumiu uma atualização OSD anterior.
                if (_rtssPublishedFrame != 0 && unchecked((int)(target.RenderedFrame - _rtssPublishedFrame)) >= 0)
                {
                    _rtssLastAck = now;
                    _rtssPublishedFrame = _rtssOverlay.PublishedFrame;
                }
                else if (_rtssLastAck != 0 && now - _rtssLastAck > 2000)
                {
                    // O servidor pode ter reiniciado entre duas consultas sem passar por Unavailable.
                    _rtssLastAck = 0;
                    _rtssPublishedFrame = 0;
                }
                if (_rtssPublishedFrame == 0) _rtssPublishedFrame = _rtssOverlay.PublishedFrame;
                // Esconde a janela WPF já na primeira publicação (carência de 2 s até o jogo consumir o OSD)
                // para não exibir os dois painéis sobrepostos; sem confirmação, a janela WPF volta.
                _rtssShowing = _rtssLastAck != 0 ? now - _rtssLastAck <= 2000 : now - _rtssPendingSince <= 2000;
            }
        }
        else if (status == RtssStatus.Ready)
        {
            ResetRtssSession();
        }

        _rtssStatus = status;
        // Uma disputa curta pelo lock não alterna os renderizadores nem bloqueia o dispatcher.
        if (status != RtssStatus.Ready && (status != RtssStatus.Busy || now - _rtssLastUpdate > 2000))
        {
            ResetRtssSession();
            if (status is RtssStatus.Unavailable or RtssStatus.Incompatible) _rtssRetryAfter = now + 5000;
        }
    }

    private void UpdateStatus(FrameStats? frames)
    {
        if (_context == null) return;
        (string text, string brushKey) = _rtssShowing ? ("Overlay dentro do jogo, em tela cheia", "Ok")
            : _context.Frames.Error != null ? ($"Detecção de FPS indisponível: {_context.Frames.Error}", "Danger")
            : !_context.Frames.Running ? ("", "Faint")
            : frames != null ? ($"Medindo {frames.ProcessName}", "Signal")
            : ("", "Faint"); // sem jogo, nada a dizer: a prévia já avisa que o FPS é de exemplo
        OvStatus.Text = text;
        OvStatusDot.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        OvStatus.ToolTip = _rtssShowing ? null : _rtssStatus switch
        {
            RtssStatus.Unavailable => "RTSS não encontrado: instale e abra o RTSS 7.3.7+ antes do jogo para ver o overlay em tela cheia exclusiva.",
            RtssStatus.Incompatible => "RTSS: protocolo incompatível ou ainda não inicializado. Atualize o RTSS.",
            RtssStatus.SlotsFull => "RTSS: todos os slots de OSD estão ocupados por outros programas.",
            RtssStatus.ContentTooLarge => "RTSS: os itens escolhidos excedem a capacidade do OSD.",
            _ => _context.Frames.Error != null ? text : null,
        };
        OvStatusDot.SetResourceReference(Shape.FillProperty, brushKey);
    }

    /// <summary>
    /// "Onde funciona": janela e tela cheia sem borda sempre funcionam (o overlay é uma janela por cima do jogo); a tela cheia
    /// exclusiva depende do RTSS. Um tópico que pede ação ganha o que fazer e o botão na própria linha. O RTSS é consultado a cada 3 s, só com a página
    /// à vista (abre a seção compartilhada dele e lê o cabeçalho, sem ocupar slot nem procurar processos).
    /// </summary>
    private void UpdateReady()
    {
        if (_context == null) return;
        long now = Environment.TickCount64;
        if (now >= _rtssProbeAfter)
        {
            _rtssProbeAfter = now + 3000;
            _rtssProbe = _rtssOverlay.Probe();
            _rtssInstalled = _rtssProbe != RtssStatus.Unavailable || RtssOverlayClient.IsInstalled();
        }

        // Tela cheia exclusiva: depende do RTSS. Os outros tópicos só mudam quando falta algo.
        if (_rtssShowing || _rtssProbe is RtssStatus.Ready or RtssStatus.Busy)
            SetTopic(OvRtssLed, OvRtssState, OvRtssHint, OvRtssAction, "Ok", "Pelo RTSS");
        else if (_rtssProbe == RtssStatus.Incompatible)
            SetTopic(OvRtssLed, OvRtssState, OvRtssHint, OvRtssAction, "Warn", "RTSS desatualizado",
                "Este RTSS é antigo demais para o CoreBi. Instale a versão 7.3.7 ou mais nova.", "Baixar o RTSS");
        else if (_rtssInstalled)
            SetTopic(OvRtssLed, OvRtssState, OvRtssHint, OvRtssAction, "Warn", "RTSS fechado",
                "Abra o RTSS antes do jogo para o overlay aparecer em tela cheia exclusiva.", "Abrir o RTSS");
        else
            SetTopic(OvRtssLed, OvRtssState, OvRtssHint, OvRtssAction, "Faint", "Precisa do RTSS",
                "Nesse modo o Windows não deixa outra janela por cima do jogo. O RTSS, gratuito, desenha o overlay dentro dele.", "Baixar o RTSS");

        if (ProcessElevation.IsElevated)
            SetTopic(OvAdminLed, OvAdminState, OvAdminHint, null, "Ok", "Funciona");
        else
            SetTopic(OvAdminLed, OvAdminState, OvAdminHint, null, "Warn", "Sem administrador",
                "Temperatura, clock e consumo da CPU aparecem com travessão. Abra o CoreBi como administrador.");

        string? error = _context.Frames.Error;
        if (error == null)
            SetTopic(OvFpsLed, OvFpsState, OvFpsHint, null, "Ok", "Funciona");
        else
            SetTopic(OvFpsLed, OvFpsState, OvFpsHint, null, "Danger", "Indisponível", error);
    }

    /// <summary>Um tópico de "Onde funciona": o LED e o estado no tom do veredito; quando falta algo, o que fazer embaixo e o botão.</summary>
    private static void SetTopic(System.Windows.Shapes.Ellipse led, TextBlock state, TextBlock hint, Button? action, string tone, string text,
        string? howTo = null, string? actionText = null)
    {
        led.SetResourceReference(Shape.FillProperty, tone);
        if (state.Text != text) state.Text = text;
        state.SetResourceReference(TextBlock.ForegroundProperty, tone is "Ok" or "Faint" ? "Muted" : tone);
        if (hint.Text != (howTo ?? "")) hint.Text = howTo ?? "";
        hint.Visibility = howTo != null ? Visibility.Visible : Visibility.Collapsed;
        if (action != null)
        {
            action.Content = actionText;
            action.Visibility = actionText != null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>O botão do aviso do RTSS: abre o RTSS instalado ou a página oficial de download (Guru3D).</summary>
    private void OvReadyAction_Click(object sender, RoutedEventArgs e)
    {
        string? exe = null;
        try
        {
            string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            exe = System.IO.Path.Combine(x86, "RivaTuner Statistics Server", "RTSS.exe");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (_rtssProbe != RtssStatus.Incompatible && exe != null && File.Exists(exe))
        {
            try { using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
            catch (Exception ex) { DiagnosticLog.Write("rtss-open", ex); }
            _rtssProbeAfter = 0; // confere de novo no próximo segundo
            return;
        }
        ExternalLinks.OpenHttps("https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/");
    }

    public void Dispose()
    {
        _overlayTimer?.Stop();
        if (_opacitySave?.IsEnabled == true)
        {
            _opacitySave.Stop();
            _context?.SaveSettings();
        }
        ResetRtssSession(clearProcess: true);
        _rtssOverlay.Dispose();
        _overlay?.Close();
        _overlay = null;
    }
}
