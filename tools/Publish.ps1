param([string]$OutputDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $script:RepoRoot 'artifacts/publish/win-x64' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Push-Location $script:RepoRoot
try {
    Invoke-CoreBiDotnet publish 'src/CoreBi.Desktop/CoreBi.Desktop.csproj' -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:RestoreLockedMode=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' '-p:DebugSymbols=false' -o $OutputDirectory
    Move-Item -LiteralPath (Join-Path $OutputDirectory 'CoreBi.Desktop.exe') -Destination (Join-Path $OutputDirectory 'CoreBi.exe') -Force
    Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'licenses') -Destination $OutputDirectory -Recurse -Force
    foreach ($required in @('THIRD-PARTY.md','packages.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory "licenses/$required"))) { throw "Licença ausente no pacote: $required" }
    }
    Write-Host "CoreBi disponível em $OutputDirectory"
}
finally { Pop-Location }
