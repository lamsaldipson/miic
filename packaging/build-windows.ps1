param([string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $root '.phonemic-tools\packaging'
New-Item -ItemType Directory -Force $stage | Out-Null
if (!$Compiler) {
    $Compiler = @(
        (Join-Path $stage 'Inno\ISCC.exe'),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (!$Compiler) { throw 'Install Inno Setup 6.7 or pass its ISCC.exe path with -Compiler.' }
& "$root\windows\build.ps1" -OutputPath (Join-Path $stage 'PhoneMic.exe')
& $Compiler (Join-Path $PSScriptRoot 'PhoneMic.iss')
if ($LASTEXITCODE) { throw "Inno Setup compilation failed ($LASTEXITCODE)." }
Copy-Item -LiteralPath (Join-Path $stage 'PhoneMic.exe') -Destination (Join-Path $root 'release\windows\PhoneMic.exe') -Force
