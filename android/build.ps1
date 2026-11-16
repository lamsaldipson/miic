$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $root '.phonemic-tools'
$jdk = Get-ChildItem (Join-Path $toolRoot 'jdk') -Directory -ErrorAction SilentlyContinue | Where-Object {
    Test-Path (Join-Path $_.FullName 'bin\javac.exe')
} | Select-Object -First 1
$sdk = Join-Path $toolRoot 'sdk'
if (!$jdk -or !(Test-Path (Join-Path $sdk 'platforms\android-35\android.jar'))) {
    throw 'Install the local JDK 17 and Android SDK 35 before building.'
}
$env:JAVA_HOME = $jdk.FullName
$env:ANDROID_HOME = $sdk
$env:ANDROID_USER_HOME = Join-Path $toolRoot 'android-user'
$env:GRADLE_USER_HOME = Join-Path $toolRoot 'gradle'
Push-Location $PSScriptRoot
try {
    & .\gradlew.bat :app:assembleDebug
    if ($LASTEXITCODE -ne 0) { throw "Gradle exited with code $LASTEXITCODE." }
} finally {
    Pop-Location
}
