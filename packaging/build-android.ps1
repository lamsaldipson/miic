param([string]$Keystore = (Join-Path $env:LOCALAPPDATA 'PhoneMicSigning\release.p12'),
      [string]$Alias = 'phonemic')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $root '.phonemic-tools'
$jdk = Get-ChildItem (Join-Path $tools 'jdk') -Directory | Where-Object {
    Test-Path (Join-Path $_.FullName 'bin\javac.exe')
} | Select-Object -First 1
if (!$jdk -or !(Test-Path -LiteralPath $Keystore)) { throw 'A local JDK and release keystore are required.' }
$env:JAVA_HOME = $jdk.FullName
$env:ANDROID_HOME = Join-Path $tools 'sdk'
$env:ANDROID_USER_HOME = Join-Path $tools 'android-user'
$env:GRADLE_USER_HOME = Join-Path $tools 'gradle'
$buildTools = Join-Path $env:ANDROID_HOME 'build-tools\34.0.0'
$output = Join-Path $root 'release\android'
New-Item -ItemType Directory -Force $output | Out-Null
$unsigned = Join-Path $root 'android\app\build\outputs\apk\release\app-release-unsigned.apk'
$aligned = Join-Path $tools 'PhoneMic-release-aligned.apk'
$apk = Join-Path $output 'PhoneMic.apk'
$staged = Join-Path $tools 'PhoneMic-release-signed.apk'
if (!$env:PHONEMIC_KEY_PASSWORD) {
    $encrypted = Join-Path (Split-Path -Parent $Keystore) 'release-password.dpapi'
    $secure = if (Test-Path -LiteralPath $encrypted) {
        Get-Content -LiteralPath $encrypted | ConvertTo-SecureString
    } else {
        Read-Host 'Android release keystore password' -AsSecureString
    }
    $env:PHONEMIC_KEY_PASSWORD = [System.Net.NetworkCredential]::new('', $secure).Password
    $removePassword = $true
}
try {
    Push-Location (Join-Path $root 'android')
    try {
        & .\gradlew.bat :app:assembleRelease
        if ($LASTEXITCODE) { throw "Gradle release build failed ($LASTEXITCODE)." }
    } finally { Pop-Location }
    & "$buildTools\zipalign.exe" -f 4 $unsigned $aligned
    if ($LASTEXITCODE) { throw 'APK alignment failed.' }
    & "$buildTools\apksigner.bat" sign --ks $Keystore --ks-key-alias $Alias --ks-pass env:PHONEMIC_KEY_PASSWORD --key-pass env:PHONEMIC_KEY_PASSWORD --v4-signing-enabled false --out $staged $aligned
    if ($LASTEXITCODE) { throw 'APK signing failed.' }
    & "$buildTools\apksigner.bat" verify --verbose --print-certs $staged
    if ($LASTEXITCODE) { throw 'APK signature verification failed.' }
    Copy-Item -LiteralPath $staged -Destination $apk -Force
} finally {
    if ($removePassword) { Remove-Item Env:PHONEMIC_KEY_PASSWORD -ErrorAction SilentlyContinue }
}
