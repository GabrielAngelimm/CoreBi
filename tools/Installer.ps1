param(
    [switch]$SkipPublish,
    [string]$Iscc
)
. (Join-Path $PSScriptRoot 'Common.ps1')
Push-Location $script:RepoRoot
try {
    $props = [xml](Get-Content -LiteralPath (Join-Path $script:RepoRoot 'Directory.Build.props') -Raw)
    $version = [string]($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
    if (-not $version) { throw 'Versão não encontrada em Directory.Build.props.' }

    if (-not $SkipPublish) { & (Join-Path $PSScriptRoot 'Publish.ps1') }
    $publish = Join-Path $script:RepoRoot 'artifacts/publish/win-x64'
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'CoreBi.exe'))) { throw "Publicação ausente em $publish. Rode tools/Publish.ps1." }

    if (-not $Iscc) { $Iscc = $env:COREBI_ISCC }
    if (-not $Iscc) {
        $Iscc = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
        ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (-not $Iscc) { throw 'ISCC.exe não encontrado. Instale o Inno Setup 6 ou defina $env:COREBI_ISCC.' }

    $output = Join-Path $script:RepoRoot 'artifacts/installer'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    & $Iscc "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$output" (Join-Path $script:RepoRoot 'installer/CoreBi.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC terminou com código $LASTEXITCODE" }

    $setup = Join-Path $output "CoreBi-$version-win-x64-setup.exe"
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Instalador disponível em $setup"
    Write-Host "$hash  $(Split-Path $setup -Leaf)"
}
finally { Pop-Location }
