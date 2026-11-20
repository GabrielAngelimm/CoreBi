$ErrorActionPreference = 'Stop'
$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:Dotnet = $null

# SDK exato do global.json. O rollForward fica desligado porque a CI e os locks de publicação dependem dele.
function Get-CoreBiSdkVersion {
    [string](Get-Content -LiteralPath (Join-Path $script:RepoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
}

# Verdadeiro se este dotnet tem exatamente o SDK pedido. O --list-sdks responde mesmo quando o global.json não é atendido.
function Test-CoreBiSdk([string]$Dotnet, [string]$Version) {
    if (-not $Dotnet -or -not (Test-Path -LiteralPath $Dotnet)) { return $false }
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $sdks = @(& $Dotnet --list-sdks 2>$null) } catch { $sdks = @() } finally { $ErrorActionPreference = $previous }
    return [bool]($sdks | Where-Object { "$_".StartsWith("$Version ") })
}

function Assert-MicrosoftSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or "$($signature.SignerCertificate.Subject)" -notmatch 'O=Microsoft Corporation') {
        throw "$Path não tem assinatura válida da Microsoft."
    }
}

# Instala o SDK numa pasta do usuário com o script oficial da Microsoft (sem instalador nem UAC).
# O script e o dotnet.exe instalado só são aceitos com assinatura válida da Microsoft.
function Install-CoreBiSdk([string]$Version, [string]$Root) {
    Write-Host "O SDK $Version não está instalado no sistema. Baixando uma cópia só para o CoreBi em $Root..."
    $installer = Join-Path ([IO.Path]::GetTempPath()) ('dotnet-install-' + [guid]::NewGuid().ToString('N') + '.ps1')
    $previousProgress = $ProgressPreference; $ProgressPreference = 'SilentlyContinue'
    try {
        # Windows PowerShell 5.1 em sistemas antigos ainda pode negociar TLS 1.0 por padrão.
        if ($PSVersionTable.PSEdition -eq 'Desktop') { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 }
        Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
        Assert-MicrosoftSignature $installer
        & $installer -Version $Version -InstallDir $Root -Architecture x64 -NoPath
        Assert-MicrosoftSignature (Join-Path $Root 'dotnet.exe')
    }
    finally {
        $ProgressPreference = $previousProgress
        Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
    }
}

# dotnet dos scripts: $env:COREBI_DOTNET; senão o do sistema, se tiver o SDK exato; senão a cópia em
# %LOCALAPPDATA%\CoreBiDev\<versão>, baixada na primeira vez. Atualizações do .NET pelo Windows não mexem nela.
function Resolve-CoreBiDotnet {
    if ($env:COREBI_DOTNET) { return $env:COREBI_DOTNET }
    $version = Get-CoreBiSdkVersion
    $system = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($system -and (Test-CoreBiSdk $system.Source $version)) { return $system.Source }
    $root = Join-Path (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CoreBiDev') $version
    $local = Join-Path $root 'dotnet.exe'
    if (-not (Test-CoreBiSdk $local $version)) { Install-CoreBiSdk $version $root }
    if (-not (Test-CoreBiSdk $local $version)) { throw "O SDK $version não ficou disponível em $root." }
    # Testes e app iniciados a partir destes scripts usam o runtime desta cópia.
    $env:DOTNET_ROOT = $root
    return $local
}

function Invoke-CoreBiDotnet {
    if (-not $script:Dotnet) { $script:Dotnet = Resolve-CoreBiDotnet }
    & $script:Dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet terminou com código $LASTEXITCODE" }
}
