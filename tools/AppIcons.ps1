<#
.SYNOPSIS
    Tira o ícone oficial de cada app do catálogo de "Instalar apps" e grava em src/CoreBi.Desktop/Assets/Apps/<nome>.png.

.DESCRIPTION
    Ferramenta de desenvolvimento, roda só no Windows. Para cada app:
      1. se ele já está instalado neste PC, usa o ícone do próprio app (DisplayIcon de "Aplicativos instalados");
      2. senão, baixa o instalador oficial pelo winget ("winget download", o mesmo arquivo conferido por hash que a página
         instala) e tira o ícone dele: do .exe, da tabela Icon do .msi ou do logotipo do pacote .msix.
    O ícone sai em PNG de 64 x 64 com transparência. No fim, grava uma folha com todos (artifacts/app-icons/folha.png) para
    conferir de olho. Os ícones e as marcas são dos respectivos donos; o CoreBi só os usa para identificar cada app
    (ver licenses/APP-ICONS.md).

.EXAMPLE
    .\tools\AppIcons.ps1                 # todos os que ainda não têm ícone
    .\tools\AppIcons.ps1 -Only steam,vlc -Force
#>
param(
    [string[]]$Only = @(),
    [switch]$Force,
    [switch]$NoDownload,
    [int]$Size = 64
)
$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Out = Join-Path $RepoRoot 'src\CoreBi.Desktop\Assets\Apps'
$Work = Join-Path ([IO.Path]::GetTempPath()) 'corebi-app-icons'
$Sheet = Join-Path $RepoRoot 'artifacts\app-icons'
New-Item -ItemType Directory -Force -Path $Out, $Work, $Sheet | Out-Null
Add-Type -AssemblyName System.Drawing

# Nome do ícone, o pacote do winget e o começo do nome em "Aplicativos instalados" (vazio: não procura no registro).
# Mantenha igual a src/CoreBi.Engine/Tools/Apps/AppCatalog.cs.
$Apps = @(
    @{ Icon = 'chrome'; Id = 'Google.Chrome'; Name = 'Google Chrome' }
    @{ Icon = 'firefox'; Id = 'Mozilla.Firefox.pt-BR'; Name = 'Mozilla Firefox' }
    @{ Icon = 'brave'; Id = 'Brave.Brave'; Name = 'Brave' }
    @{ Icon = 'operagx'; Id = 'Opera.OperaGX'; Name = 'Opera GX' }
    @{ Icon = 'vivaldi'; Id = 'Vivaldi.Vivaldi'; Name = 'Vivaldi' }
    @{ Icon = 'opera'; Id = 'Opera.Opera'; Name = 'Opera Stable' }
    @{ Icon = 'steam'; Id = 'Valve.Steam'; Name = 'Steam' }
    @{ Icon = 'epic'; Id = 'EpicGames.EpicGamesLauncher'; Name = 'Epic Games Launcher' }
    @{ Icon = 'ea'; Id = 'ElectronicArts.EADesktop'; Name = 'EA app' }
    @{ Icon = 'ubisoft'; Id = 'Ubisoft.Connect'; Name = 'Ubisoft Connect' }
    @{ Icon = 'battlenet'; Id = 'Blizzard.BattleNet'; Name = 'Battle.net' }
    @{ Icon = 'rockstar'; Id = 'RockstarGames.Launcher'; Name = 'Rockstar Games Launcher' }
    @{ Icon = 'anydesk'; Id = 'AnyDesk.AnyDesk'; Name = 'AnyDesk' }
    @{ Icon = 'teamviewer'; Id = 'TeamViewer.TeamViewer'; Name = 'TeamViewer' }
    @{ Icon = 'parsec'; Id = 'Parsec.Parsec'; Name = 'Parsec' }
    @{ Icon = 'discord'; Id = 'Discord.Discord'; Name = 'Discord' }
    @{ Icon = 'telegram'; Id = 'Telegram.TelegramDesktop'; Name = 'Telegram Desktop' }
    @{ Icon = 'teamspeak'; Id = 'TeamSpeakSystems.TeamSpeakClient'; Name = 'TeamSpeak' }
    @{ Icon = 'zoom'; Id = 'Zoom.Zoom'; Name = 'Zoom' }
    @{ Icon = 'teams'; Id = 'Microsoft.Teams'; Name = '' }
    @{ Icon = 'slack'; Id = 'SlackTechnologies.Slack'; Name = 'Slack' }
    @{ Icon = 'obs'; Id = 'OBSProject.OBSStudio'; Name = 'OBS Studio' }
    @{ Icon = 'streamlabs'; Id = 'Streamlabs.Streamlabs'; Name = 'Streamlabs' }
    @{ Icon = 'lightshot'; Id = 'Skillbrains.Lightshot'; Name = 'Lightshot' }
    @{ Icon = 'spotify'; Id = 'Spotify.Spotify'; Name = 'Spotify' }
    @{ Icon = 'deezer'; Id = 'Deezer.Deezer'; Name = 'Deezer' }
    @{ Icon = 'vlc'; Id = 'VideoLAN.VLC'; Name = 'VLC media player' }
    @{ Icon = 'klite'; Id = 'CodecGuide.K-LiteCodecPack.Standard'; Name = 'K-Lite Codec Pack' }
    @{ Icon = 'itunes'; Id = 'Apple.iTunes'; Name = 'iTunes' }
    @{ Icon = 'libreoffice'; Id = 'TheDocumentFoundation.LibreOffice'; Name = 'LibreOffice' }
    @{ Icon = 'acrobat'; Id = 'Adobe.Acrobat.Reader.64-bit'; Name = 'Adobe Acrobat' }
    @{ Icon = 'notion'; Id = 'Notion.Notion'; Name = 'Notion' }
    @{ Icon = 'notepadpp'; Id = 'Notepad++.Notepad++'; Name = 'Notepad++' }
    @{ Icon = 'googledrive'; Id = 'Google.GoogleDrive'; Name = 'Google Drive' }
    @{ Icon = 'dropbox'; Id = 'Dropbox.Dropbox'; Name = 'Dropbox' }
    @{ Icon = 'evernote'; Id = 'Evernote.Evernote'; Name = 'Evernote' }
    @{ Icon = 'googleearth'; Id = 'Google.EarthPro'; Name = 'Google Earth Pro' }
    @{ Icon = 'onedrive'; Id = 'Microsoft.OneDrive'; Name = 'Microsoft OneDrive' }
    @{ Icon = 'chatgpt'; Id = 'OpenAI.ChatGPT'; Name = '' }
    @{ Icon = 'claude'; Id = 'Anthropic.Claude'; Name = 'Claude' }
    @{ Icon = 'ghub'; Id = 'Logitech.GHUB'; Name = 'Logitech G HUB' }
    @{ Icon = 'razer'; Id = 'RazerInc.RazerInstaller.Synapse4'; Name = 'Razer Synapse' }
    @{ Icon = 'icue'; Id = 'Corsair.iCUE.5'; Name = 'iCUE' }
    @{ Icon = 'steelseries'; Id = 'SteelSeries.GG'; Name = 'SteelSeries GG' }
    @{ Icon = 'gimp'; Id = 'GIMP.GIMP.3'; Name = 'GIMP' }
    @{ Icon = 'blender'; Id = 'BlenderFoundation.Blender'; Name = 'Blender' }
    @{ Icon = 'capcut'; Id = 'ByteDance.CapCut'; Name = 'CapCut' }
    @{ Icon = 'audacity'; Id = 'Audacity.Audacity'; Name = 'Audacity' }
    @{ Icon = 'figma'; Id = 'Figma.Figma'; Name = 'Figma' }
    @{ Icon = 'canva'; Id = 'Canva.Canva'; Name = 'Canva' }
    @{ Icon = '7zip'; Id = '7zip.7zip'; Name = '7-Zip' }
    @{ Icon = 'winrar'; Id = 'RARLab.WinRAR'; Name = 'WinRAR' }
    @{ Icon = 'revo'; Id = 'RevoUninstaller.RevoUninstaller'; Name = 'Revo Uninstaller' }
    @{ Icon = 'wiztree'; Id = 'AntibodySoftware.WizTree'; Name = 'WizTree' }
    @{ Icon = 'rufus'; Id = 'Rufus.Rufus'; Name = 'Rufus' }
    @{ Icon = 'qbittorrent'; Id = 'qBittorrent.qBittorrent'; Name = 'qBittorrent' }
    @{ Icon = 'bitwarden'; Id = 'Bitwarden.Bitwarden'; Name = 'Bitwarden' }
    @{ Icon = 'protonvpn'; Id = 'Proton.ProtonVPN'; Name = 'Proton VPN' }
    @{ Icon = 'malwarebytes'; Id = 'Malwarebytes.Malwarebytes'; Name = 'Malwarebytes' }
    @{ Icon = 'vscode'; Id = 'Microsoft.VisualStudioCode'; Name = 'Microsoft Visual Studio Code' }
    @{ Icon = 'python'; Id = 'Python.Python.3.14'; Name = 'Python 3.14' }
    @{ Icon = 'nodejs'; Id = 'OpenJS.NodeJS.LTS'; Name = 'Node.js' }
    @{ Icon = 'docker'; Id = 'Docker.DockerDesktop'; Name = 'Docker Desktop' }
    @{ Icon = 'sublime'; Id = 'SublimeHQ.SublimeText.4'; Name = 'Sublime Text' }
    @{ Icon = 'java'; Id = 'EclipseAdoptium.Temurin.21.JRE'; Name = 'Eclipse Temurin JRE' }
    @{ Icon = 'webview2'; Id = 'Microsoft.EdgeWebView2Runtime'; Name = 'Microsoft Edge WebView2 Runtime' }
    @{ Icon = 'dotnetfx'; Id = 'Microsoft.DotNet.Framework.DeveloperPack_4'; Name = '' }
    @{ Icon = 'directx'; Id = 'Microsoft.DirectX'; Name = '' }
    @{ Icon = 'git'; Id = 'Git.Git'; Name = 'Git' }
    @{ Icon = 'githubdesktop'; Id = 'GitHub.GitHubDesktop'; Name = 'GitHub Desktop' }
    @{ Icon = 'vcredist'; Id = 'Microsoft.VCRedist.2015+.x64'; Name = '' }
    @{ Icon = 'dotnet'; Id = 'Microsoft.DotNet.DesktopRuntime.8'; Name = '' }

)

Add-Type -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class CoreBiIcons
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    // O ícone do índice pedido no tamanho pedido (o Windows escolhe a imagem mais próxima e escala), com transparência
    public static Bitmap Extract(string file, int index, int size)
    {
        var icons = new IntPtr[1];
        var ids = new uint[1];
        uint got = PrivateExtractIcons(file, index, size, size, icons, ids, 1, 0);
        if (got == 0 || got == 0xFFFFFFFF || icons[0] == IntPtr.Zero) return null;
        try
        {
            using (var icon = Icon.FromHandle(icons[0]))
                return icon.ToBitmap();
        }
        finally { DestroyIcon(icons[0]); }
    }

    // Reduz para o tamanho final com filtro de alta qualidade, sem perder a transparência
    public static void SavePng(Image source, string path, int size)
    {
        using (var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
            float w = source.Width * scale, h = source.Height * scale;
            g.DrawImage(source, (size - w) / 2, (size - h) / 2, w, h);
            canvas.Save(path, ImageFormat.Png);
        }
    }

    // Uma imagem "de verdade": nem vazia nem o ícone genérico de programa (quase tudo transparente)
    public static bool LooksReal(Bitmap b)
    {
        if (b == null) return false;
        int opaque = 0;
        for (int y = 0; y < b.Height; y += 2)
            for (int x = 0; x < b.Width; x += 2)
                if (b.GetPixel(x, y).A > 40) opaque++;
        return opaque > (b.Width * b.Height / 4) / 20;
    }
}
"@ -ReferencedAssemblies System.Drawing

function Find-DisplayIcon([string]$Name) {
    if (-not $Name) { return $null }
    $roots = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
             'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        foreach ($key in Get-ChildItem $root -ErrorAction SilentlyContinue) {
            $p = Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue
            if (-not $p.DisplayName -or -not $p.DisplayName.StartsWith($Name, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $icon = [string]$p.DisplayIcon
            if (-not $icon) { continue }
            $icon = $icon.Trim('"')
            $index = 0
            if ($icon -match '^(.*?),(-?\d+)$') { $icon = $Matches[1].Trim('"'); $index = [int]$Matches[2] }
            $icon = [Environment]::ExpandEnvironmentVariables($icon)
            if (Test-Path -LiteralPath $icon) { return @{ File = $icon; Index = $index } }
        }
    }
    return $null
}

function Get-Installer([string]$Id) {
    $dir = Join-Path $Work ($Id -replace '[^A-Za-z0-9.+_-]', '_')
    $existing = Get-ChildItem -LiteralPath $dir -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -match '^\.(exe|msi|msix|msixbundle|appx|appxbundle|zip)$' }
    if ($existing) { return $existing[0].FullName }
    if ($NoDownload) { return $null }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    # O instalador .exe costuma ter o ícone do app; se o pacote não tiver .exe, o winget escolhe o que tiver
    foreach ($type in @('exe', 'nullsoft', 'inno', 'burn', $null)) {
        $args = @('download', '--id', $Id, '--exact', '--source', 'winget', '--download-directory', $dir,
                  '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity', '--skip-license')
        if ($type) { $args += @('--installer-type', $type) }
        & winget @args *> $null
        $file = Get-ChildItem -LiteralPath $dir -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -match '^\.(exe|msi|msix|msixbundle|appx|appxbundle|zip)$' } | Select-Object -First 1
        if ($file) { return $file.FullName }
    }
    return $null
}

# Ícone de um .msi: a tabela Icon guarda o .ico (ou um .exe) que o Windows mostra em "Aplicativos instalados"
function Get-MsiIconFile([string]$Msi) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($Msi, 0))
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @('SELECT `Name`, `Data` FROM `Icon`'))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
    $best = $null
    while ($true) {
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if (-not $record) { break }
        $name = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1))
        $size = $record.GetType().InvokeMember('DataSize', 'GetProperty', $null, $record, @(2))
        $bytes = $record.GetType().InvokeMember('ReadStream', 'InvokeMethod', $null, $record, @(2, $size, 1))
        $target = Join-Path $Work ("msi-" + [guid]::NewGuid().ToString('N') + [IO.Path]::GetExtension($name))
        if (-not [IO.Path]::GetExtension($name)) { $target += '.ico' }
        [IO.File]::WriteAllBytes($target, [Text.Encoding]::Default.GetBytes($bytes))
        if (-not $best -or (Get-Item $target).Length -gt (Get-Item $best).Length) { $best = $target }
    }
    return $best
}

# Logotipo de um .msix/.appx: o maior PNG de Square44x44Logo ou Square150x150Logo dentro do pacote
function Get-MsixLogo([string]$Package) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $inner = $zip.Entries | Where-Object { $_.FullName -match '\.(msix|appx)$' -and $_.FullName -match 'x64' } | Select-Object -First 1
        if ($inner) {
            $nested = Join-Path $Work ([guid]::NewGuid().ToString('N') + '.msix')
            [IO.Compression.ZipFileExtensions]::ExtractToFile($inner, $nested)
            $zip.Dispose(); $zip = [IO.Compression.ZipFile]::OpenRead($nested)
        }
        $logo = $zip.Entries | Where-Object { $_.FullName -match '(Square44x44Logo|Square150x150Logo|AppList|StoreLogo).*\.png$' -and $_.FullName -notmatch 'contrast' } |
            Sort-Object { if ($_.FullName -match 'targetsize-256') { 0 } elseif ($_.FullName -match 'scale-400') { 1 } else { 2 } }, Length -Descending |
            Select-Object -First 1
        if (-not $logo) { return $null }
        $target = Join-Path $Work ([guid]::NewGuid().ToString('N') + '.png')
        [IO.Compression.ZipFileExtensions]::ExtractToFile($logo, $target)
        return $target
    }
    finally { $zip.Dispose() }
}

function Get-IconBitmap([string]$File, [int]$Index = 0) {
    $ext = [IO.Path]::GetExtension($File).ToLowerInvariant()
    if ($ext -eq '.png') { return [Drawing.Bitmap]::new($File) }
    if ($ext -eq '.msi') { $ico = Get-MsiIconFile $File; if ($ico) { return [CoreBiIcons]::Extract($ico, 0, 256) } return $null }
    if ($ext -match '^\.(msix|msixbundle|appx|appxbundle)$') { $png = Get-MsixLogo $File; if ($png) { return [Drawing.Bitmap]::new($png) } return $null }
    if ($ext -eq '.zip') {
        $dir = Join-Path $Work ([guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $File -DestinationPath $dir
        $exe = Get-ChildItem -LiteralPath $dir -Recurse -Filter *.exe | Sort-Object Length -Descending | Select-Object -First 1
        if ($exe) { return [CoreBiIcons]::Extract($exe.FullName, 0, 256) } return $null
    }
    return [CoreBiIcons]::Extract($File, $Index, 256)
}

$done = @(); $missing = @()
foreach ($app in $Apps) {
    if ($Only.Count -gt 0 -and $Only -notcontains $app.Icon) { continue }
    $target = Join-Path $Out ($app.Icon + '.png')
    if ((Test-Path -LiteralPath $target) -and -not $Force) { $done += $app; continue }
    $bitmap = $null; $source = ''
    try {
        $installed = Find-DisplayIcon $app.Name
        if ($installed) { $bitmap = Get-IconBitmap $installed.File $installed.Index; $source = 'instalado' }
        if (-not [CoreBiIcons]::LooksReal($bitmap)) {
            $file = Get-Installer $app.Id
            if ($file) { $bitmap = Get-IconBitmap $file; $source = 'instalador' }
        }
    }
    catch { Write-Warning "$($app.Icon): $($_.Exception.Message)" }
    if ([CoreBiIcons]::LooksReal($bitmap)) {
        [CoreBiIcons]::SavePng($bitmap, $target, $Size)
        Write-Host ("{0,-16} {1}" -f $app.Icon, $source)
        $done += $app
    }
    else { Write-Warning "$($app.Icon): sem ícone ($($app.Id))"; $missing += $app }
    if ($bitmap) { $bitmap.Dispose() }
}

# Folha para conferir: claro e escuro lado a lado, com o nome embaixo de cada ícone
$all = @($Apps | Where-Object { Test-Path -LiteralPath (Join-Path $Out ($_.Icon + '.png')) })
$cols = 12; $cell = 96; $rows = [Math]::Ceiling($all.Count / $cols)
$sheetFile = Join-Path $Sheet 'folha.png'
$bmp = New-Object Drawing.Bitmap ($cols * $cell * 2 + 24), ($rows * $cell + 16)
$g = [Drawing.Graphics]::FromImage($bmp)
$g.Clear([Drawing.Color]::FromArgb(255, 21, 25, 28))
$g.FillRectangle((New-Object Drawing.SolidBrush ([Drawing.Color]::White)), ($cols * $cell + 24), 0, ($cols * $cell), $bmp.Height)
$font = New-Object Drawing.Font 'Segoe UI', 8
for ($i = 0; $i -lt $all.Count; $i++) {
    $img = [Drawing.Image]::FromFile((Join-Path $Out ($all[$i].Icon + '.png')))
    foreach ($half in 0, 1) {
        $x = $half * ($cols * $cell + 24) + ($i % $cols) * $cell + 24; $y = [Math]::Floor($i / $cols) * $cell + 12
        $g.DrawImage($img, $x, $y, 48, 48)
        $brush = if ($half -eq 0) { [Drawing.Brushes]::Gainsboro } else { [Drawing.Brushes]::DimGray }
        $g.DrawString($all[$i].Icon, $font, $brush, ($x - 12), ($y + 54))
    }
    $img.Dispose()
}
$bmp.Save($sheetFile, [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
Write-Host "`n$($all.Count) ícones em $Out; folha em $sheetFile"
if ($missing.Count -gt 0) { Write-Warning ("Sem ícone: " + (($missing | ForEach-Object Icon) -join ', ')) }
