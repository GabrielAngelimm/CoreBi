param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
. (Join-Path $PSScriptRoot 'Common.ps1')
Push-Location $script:RepoRoot
try { Invoke-CoreBiDotnet build 'CoreBi.slnx' -c $Configuration }
finally { Pop-Location }
