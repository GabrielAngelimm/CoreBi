$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$folder = Join-Path $repo 'artifacts/tools/gitleaks'
$executable = Join-Path $folder 'gitleaks.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $archive = Join-Path $folder 'gitleaks.zip'
    Invoke-WebRequest 'https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_windows_x64.zip' -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne 'D29144DEFF3A68AA93CED33DDDF84B7FDC26070ADD4AA0F4513094C8332AFC4E') {
        throw 'O checksum do Gitleaks não corresponde ao pacote oficial fixado.'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $folder -Force
}
& $executable git $repo --redact --report-format json --report-path (Join-Path $repo 'artifacts/secrets.json') '--log-opts=--all'
if ($LASTEXITCODE -ne 0) { throw "A verificação de segredos falhou: $LASTEXITCODE" }
