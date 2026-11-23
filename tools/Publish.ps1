param([string]$OutputDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
# A pasta padrão é recriada a cada publicação: o instalador empacota tudo o que estiver nela.
# Uma pasta passada em -OutputDirectory é usada como está.
$cleanOutput = -not $OutputDirectory
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $script:RepoRoot 'artifacts/publish/win-x64' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if ($cleanOutput -and (Test-Path -LiteralPath $OutputDirectory)) { Remove-Item -LiteralPath $OutputDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Push-Location $script:RepoRoot
try {
    # As DLLs nativas (WPF, DirectX, TraceEvent) ficam ao lado do CoreBi.exe, na pasta do programa, que só o
    # administrador altera. Extraídas pelo próprio executável elas iriam para o %TEMP% do usuário e seriam
    # carregadas de lá com elevação: um programa qualquer da mesma conta poderia trocá-las.
    Invoke-CoreBiDotnet publish 'src/CoreBi.Desktop/CoreBi.Desktop.csproj' -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:RestoreLockedMode=true' '-p:DebugType=None' '-p:DebugSymbols=false' -o $OutputDirectory
    Move-Item -LiteralPath (Join-Path $OutputDirectory 'CoreBi.Desktop.exe') -Destination (Join-Path $OutputDirectory 'CoreBi.exe') -Force
    $licensesTarget = Join-Path $OutputDirectory 'licenses'
    if (Test-Path -LiteralPath $licensesTarget) { Remove-Item -LiteralPath $licensesTarget -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'licenses') -Destination $OutputDirectory -Recurse -Force
    foreach ($required in @('THIRD-PARTY.md','packages.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory "licenses/$required"))) { throw "Licença ausente no pacote: $required" }
    }
    Write-Host "CoreBi disponível em $OutputDirectory"
}
finally { Pop-Location }
