$ErrorActionPreference = 'Stop'
$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:Dotnet = if ($env:COREBI_DOTNET) { $env:COREBI_DOTNET } else { (Get-Command dotnet -ErrorAction Stop).Source }
function Invoke-CoreBiDotnet {
    & $script:Dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet terminou com código $LASTEXITCODE" }
}
