param([ValidateSet('Logic','Hardware','Bench','Startup','Integrations')][string]$Mode = 'Logic')
. (Join-Path $PSScriptRoot 'Common.ps1')
Push-Location $script:RepoRoot
try {
    $testArgs = @('run','--project','tests/CoreBi.Tests/CoreBi.Tests.csproj','-c','Release','--', (Join-Path $script:RepoRoot 'artifacts/tests'))
    if ($Mode -eq 'Hardware') { $testArgs += '--hardware' }
    if ($Mode -eq 'Bench') { $testArgs += '--bench' }
    if ($Mode -eq 'Startup') { $testArgs += '--startup' }
    if ($Mode -eq 'Integrations') { $testArgs += '--integrations' }
    Invoke-CoreBiDotnet @testArgs
}
finally { Pop-Location }
