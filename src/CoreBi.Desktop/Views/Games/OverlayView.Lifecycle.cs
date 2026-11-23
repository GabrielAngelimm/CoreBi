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
    /// "Onde funciona": o estado agora de cada caso. O que está pronto é uma linha; o que pede ação vira um aviso com o que fazer
    /// e, quando dá, o botão que resolve. O RTSS é consultado a cada 3 s, só com a página à vista (abre a seção compartilhada
    /// dele e lê o cabeçalho, sem ocupar slot nem procurar processos).
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
        int pending = 0;
        if (_rtssShowing)
            SetReady(OvRtssBox, OvRtssGlyph, OvRtssState, OvRtssHint, OvRtssAction, Ready.Ok, "Dentro do jogo agora, pelo RTSS");
        else if (_rtssProbe is RtssStatus.Ready or RtssStatus.Busy)
            SetReady(OvRtssBox, OvRtssGlyph, OvRtssState, OvRtssHint, OvRtssAction, Ready.Ok, "Pelo RTSS, que está aberto");
        else if (_rtssProbe == RtssStatus.Incompatible)
            pending += SetReady(OvRtssBox, OvRtssGlyph, OvRtssState, OvRtssHint, OvRtssAction, Ready.Missing, "RTSS desatualizado",
                "Este RTSS é antigo demais para o CoreBi. Instale a versão 7.3.7 ou mais nova.", "Baixar o RTSS");
        else if (_rtssInstalled)
            pending += SetReady(OvRtssBox, OvRtssGlyph, OvRtssState, OvRtssHint, OvRtssAction, Ready.Missing, "RTSS fechado",
                "O RTSS está instalado, mas fechado. Abra antes do jogo para o overlay aparecer em tela cheia exclusiva.", "Abrir o RTSS");
        else
            pending += SetReady(OvRtssBox, OvRtssGlyph, OvRtssState, OvRtssHint, OvRtssAction, Ready.Optional, "Precisa do RTSS",
                "Nesse modo o Windows não deixa outra janela por cima do jogo. O RTSS, gratuito, desenha o overlay dentro dele.", "Baixar o RTSS");

        if (ProcessElevation.IsElevated)
            SetReady(OvAdminBox, OvAdminGlyph, OvAdminState, OvAdminHint, null, Ready.Ok, "Temperatura, clock e consumo");
        else
            pending += SetReady(OvAdminBox, OvAdminGlyph, OvAdminState, OvAdminHint, null, Ready.Missing, "Sem administrador",
                "Temperatura, clock e consumo da CPU aparecem com travessão. Feche o CoreBi e abra como administrador.");

        string? error = _context.Frames.Error;
        if (error == null)
            SetReady(OvFpsBox, OvFpsGlyph, OvFpsState, OvFpsHint, null, Ready.Ok, "Eventos do Windows, sem tocar no jogo");
        else
            pending += SetReady(OvFpsBox, OvFpsGlyph, OvFpsState, OvFpsHint, null, Ready.Failed, "Indisponível", error);
        OvFpsState.ToolTip = error == null ? "O FPS vem dos eventos de apresentação do Windows (ETW): o CoreBi não injeta nada nem lê a memória do jogo." : null;

        OvReadySummary.Text = pending switch { 0 => "Tudo pronto", 1 => "1 item pede atenção", _ => $"{pending} itens pedem atenção" };
        OvReadySummary.SetResourceReference(TextBlock.ForegroundProperty, pending == 0 ? "Ok" : "Muted");
    }

    private enum Ready { Ok, Optional, Missing, Failed }

    /// <summary>Pronto: só a linha. Pede ação: aviso no tom do veredito, com a frase do que fazer e o botão. Devolve 1 se pede ação.</summary>
    private static int SetReady(Border box, TextBlock glyph, TextBlock state, TextBlock hint, Button? action, Ready ready, string text,
        string? howTo = null, string? actionText = null)
    {
        (string icon, string fg, string? soft) = ready switch
        {
            Ready.Ok => ("\uE73E", "Ok", null),
            Ready.Optional => ("\uE946", "Steel", "SteelSoft"),
            Ready.Failed => ("\uE711", "Danger", "DangerSoft"),
            _ => ("\uE7BA", "Warn", "WarnSoft"),
        };
        glyph.Text = icon;
        glyph.SetResourceReference(TextBlock.ForegroundProperty, fg);
        if (soft != null)
        {
            box.SetResourceReference(Border.BackgroundProperty, soft);
            box.SetResourceReference(Border.BorderBrushProperty, soft);
            box.Padding = new Thickness(15, 10, 10, 10);
            box.Margin = new Thickness(0, 4, 0, 6);
        }
        else
        {
            box.ClearValue(Border.BackgroundProperty);
            box.ClearValue(Border.BorderBrushProperty);
            box.ClearValue(Border.PaddingProperty);
            box.ClearValue(FrameworkElement.MarginProperty);
        }
        // Pronto, a linha só indica onde funciona (o detalhe fica na dica); o estado escrito aparece só no aviso
        if (state.Text != text) state.Text = text;
        state.SetResourceReference(TextBlock.ForegroundProperty, fg);
        state.Visibility = soft == null ? Visibility.Collapsed : Visibility.Visible;
        box.ToolTip = soft == null ? text : null;
        hint.Text = howTo ?? "";
        hint.Visibility = howTo != null ? Visibility.Visible : Visibility.Collapsed;
        if (action != null)
        {
            action.Content = actionText;
            action.Visibility = actionText != null ? Visibility.Visible : Visibility.Collapsed;
        }
        return ready == Ready.Ok ? 0 : 1;
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
