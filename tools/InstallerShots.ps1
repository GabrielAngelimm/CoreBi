# Capturas das telas do instalador, em claro e escuro, para conferir o visual sem rodar o instalador no PC de ninguém.
# Feito para a CI (máquina descartável): a primeira volta passa pelas telas com o padrão e fecha antes de instalar; a
# segunda instala só o app, para mostrar a tela de conclusão, e desinstala em silêncio. Os botões são acionados pela
# própria janela (WM_COMMAND), sem teclado nem foco, e a imagem vem do PrintWindow, mesmo com a janela coberta.
# Uso (Windows PowerShell): .\tools\InstallerShots.ps1 -Setup artifacts\installer\CoreBi-1.0.0-win-x64-setup.exe
param(
    [Parameter(Mandatory)][string]$Setup,
    [string]$Output = 'artifacts/shots'
)
$ErrorActionPreference = 'Stop'
$Setup = (Resolve-Path -LiteralPath $Setup).Path
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$Output = (Resolve-Path -LiteralPath $Output).Path

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public static class InstallerWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect rect, int size);

    // O botão de avançar muda de nome a cada tela (Avançar, Instalar, Concluir); os outros botões têm nome fixo
    private static readonly Regex Other = new Regex("Voltar|Cancelar|Procurar|Back|Cancel|Browse", RegexOptions.IgnoreCase);

    // O assistente roda num processo à parte (o .tmp que o setup extrai), então a janela é achada pela classe da VCL
    public static IntPtr FindWizard()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, data) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var name = new StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() != "TWizardForm") return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static string ClickNext(IntPtr wizard)
    {
        string clicked = null;
        EnumChildWindows(wizard, (hwnd, data) =>
        {
            var name = new StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            string cls = name.ToString();
            if (cls.IndexOf("Button", StringComparison.OrdinalIgnoreCase) < 0 || cls.Contains("Radio") || cls.Contains("Check")) return true;
            if (!IsWindowVisible(hwnd) || !IsWindowEnabled(hwnd)) return true;
            var text = new StringBuilder(128);
            GetWindowText(hwnd, text, text.Capacity);
            if (text.Length == 0 || Other.IsMatch(text.ToString())) return true;
            clicked = text.ToString();
            // BN_CLICKED (0) para o pai, com o próprio botão no lParam: é como a VCL do Inno entrega o clique ao botão
            SendMessage(GetParent(hwnd), 0x0111, IntPtr.Zero, hwnd);
            return false;
        }, IntPtr.Zero);
        return clicked;
    }

    public static void Save(IntPtr hwnd, string path)
    {
        Rect window, frame;
        GetWindowRect(hwnd, out window);
        if (DwmGetWindowAttribute(hwnd, 9, out frame, Marshal.SizeOf(typeof(Rect))) != 0) frame = window;
        using (var full = new Bitmap(window.Right - window.Left, window.Bottom - window.Top))
        {
            using (var g = Graphics.FromImage(full))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(hwnd, dc, 2); // PW_RENDERFULLCONTENT: inclui a barra de título desenhada pelo instalador
                g.ReleaseHdc(dc);
            }
            var crop = new Rectangle(frame.Left - window.Left, frame.Top - window.Top, frame.Right - frame.Left, frame.Bottom - frame.Top);
            using (var shot = full.Clone(crop, full.PixelFormat)) shot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
'@

$appDir = Join-Path $env:ProgramFiles 'CoreBi'

function Set-AppsTheme([bool]$Light) {
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    Set-ItemProperty -Path $key -Name AppsUseLightTheme -Value ([int]$Light) -Type DWord
}

function Stop-Setup {
    # O setup.exe e o .tmp que ele extrai e que mostra o assistente
    Get-Process | Where-Object { $_.ProcessName -like 'CoreBi-*-setup*' } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

function Wait-Wizard {
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while ([DateTime]::UtcNow -lt $deadline) {
        $wizard = [InstallerWindow]::FindWizard()
        if ($wizard -ne [IntPtr]::Zero) { Start-Sleep -Milliseconds 1500; return $wizard }
        Start-Sleep -Milliseconds 250
    }
    throw 'A janela do instalador não apareceu.'
}

function Save-Page([IntPtr]$Wizard, [string]$Name) {
    $path = Join-Path $Output "$Name.png"
    [InstallerWindow]::Save($Wizard, $path)
    Write-Host "Captura: $path"
}

function Next-Page([IntPtr]$Wizard) {
    $clicked = [InstallerWindow]::ClickNext($Wizard)
    if (-not $clicked) { throw 'Botão de avançar não encontrado.' }
    Start-Sleep -Milliseconds 1200
}

function Remove-App {
    $uninstaller = Join-Path $appDir 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller)) { return }
    Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ((Test-Path -LiteralPath $uninstaller) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
}

try {
    foreach ($theme in @(@{ Name = 'claro'; Light = $true }, @{ Name = 'escuro'; Light = $false })) {
        Set-AppsTheme $theme.Light
        $prefix = "instalador-$($theme.Name)"

        # Volta 1: as telas com o padrão (instalação completa), até a de pronto para instalar
        Start-Process -FilePath $Setup
        $wizard = Wait-Wizard
        $pages = @('1-boas-vindas', '2-pasta', '3-componentes', '4-atalhos', '5-pronto')
        for ($i = 0; $i -lt $pages.Count; $i++) {
            Save-Page $wizard "$prefix-$($pages[$i])"
            if ($i -lt $pages.Count - 1) { Next-Page $wizard }
        }
        Stop-Setup

        # Volta 2: só o app, para chegar à conclusão sem baixar PawnIO e RTSS pelo winget
        Start-Process -FilePath $Setup -ArgumentList '/COMPONENTS="app"'
        $wizard = Wait-Wizard
        for ($i = 0; $i -lt 5; $i++) { Next-Page $wizard }
        $deadline = [DateTime]::UtcNow.AddSeconds(150)
        while (-not (Test-Path -LiteralPath (Join-Path $appDir 'unins000.dat')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
        Start-Sleep -Seconds 3
        Save-Page $wizard "$prefix-6-concluido"
        Stop-Setup
        Remove-App
    }
}
finally {
    Stop-Setup
    Set-AppsTheme $true
}
