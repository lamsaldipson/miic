$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework 4.x compiler is required.'
}
$root = Split-Path -Parent $PSScriptRoot
$icon = Join-Path $PSScriptRoot 'PhoneMic.ico'
$dashboard = Join-Path $root 'dashboard.html'
$output = Join-Path $PSScriptRoot 'PhoneMic.exe'
$arguments = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', "/out:$output",
    "/win32icon:$icon", "/resource:$icon,PhoneMic.ico", "/resource:$dashboard,dashboard.html",
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll',
    (Join-Path $PSScriptRoot 'Program.cs'), (Join-Path $PSScriptRoot 'Receiver.cs'), (Join-Path $PSScriptRoot 'Playback.cs'))
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Windows compilation failed ($LASTEXITCODE)." }
