# Roda qualquer comando do dotnet com o SDK exato do global.json: o do sistema ou a cópia baixada pelos scripts.
# Exemplo: .\tools\Dotnet.ps1 format whitespace CoreBi.slnx --no-restore --verify-no-changes
. (Join-Path $PSScriptRoot 'Common.ps1')
Invoke-CoreBiDotnet @args
