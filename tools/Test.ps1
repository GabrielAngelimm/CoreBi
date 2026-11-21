param(
    [ValidateSet('Logic','Hardware','Bench','Startup','Integrations','OverlayTelemetry')][string]$Mode = 'Logic',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$NoBuild
)
. (Join-Path $PSScriptRoot 'Common.ps1')
Push-Location $script:RepoRoot
try {
    $testArgs = @('run','--project','tests/CoreBi.Tests/CoreBi.Tests.csproj','-c',$Configuration)
    if ($NoBuild) { $testArgs += '--no-build' } else {
        # Mesma restauração do Build.ps1: falha se os locks não baterem, em vez de reescrevê-los.
        Invoke-CoreBiDotnet restore 'tests/CoreBi.Tests/CoreBi.Tests.csproj' --locked-mode
        $testArgs += '--no-restore'
    }
    $testArgs += @('--', (Join-Path $script:RepoRoot 'artifacts/tests'))
    if ($Mode -eq 'Hardware') { $testArgs += '--hardware' }
    if ($Mode -eq 'Bench') { $testArgs += '--bench' }
    if ($Mode -eq 'Startup') { $testArgs += '--startup' }
    if ($Mode -eq 'Integrations') { $testArgs += '--integrations' }
    if ($Mode -eq 'OverlayTelemetry') { $testArgs += '--overlay-telemetry' }
    Invoke-CoreBiDotnet @testArgs
}
finally { Pop-Location }
