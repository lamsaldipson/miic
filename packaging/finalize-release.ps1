$ErrorActionPreference = 'Stop'
$release = Join-Path (Split-Path -Parent $PSScriptRoot) 'release'
$docs = Join-Path $release 'documentation'
$checksums = Join-Path $release 'checksums'
New-Item -ItemType Directory -Force $docs, $checksums | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'documentation\*.txt') $docs -Force
Push-Location $release
try {
    $files = @('windows/PhoneMicSetup.exe', 'windows/PhoneMic.exe', 'android/PhoneMic.apk')
    $lines = foreach ($file in $files) {
        $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
        '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $file
    }
    Set-Content -Encoding ascii (Join-Path $checksums 'SHA256SUMS.txt') $lines
    foreach ($file in $files) { Get-Item -LiteralPath $file | Select-Object Name, Length }
    $lines
} finally { Pop-Location }
