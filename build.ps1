$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler is missing.' }
New-Item -ItemType Directory -Force -Path 'dist/ClaerPlayer', 'artifacts' | Out-Null
Add-Type -AssemblyName System.Drawing
if (!(Test-Path -LiteralPath 'src/ClaerPlayer.ico')) {
    $bitmap = New-Object System.Drawing.Bitmap 64,64
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.Clear([System.Drawing.Color]::FromArgb(25,29,32))
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(139,226,194))
    $points = [System.Drawing.Point[]]@((New-Object System.Drawing.Point 24,16),(New-Object System.Drawing.Point 48,32),(New-Object System.Drawing.Point 24,48))
    $graphics.FillPolygon($brush, $points)
    $icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
    $stream = [System.IO.File]::Create((Join-Path $PSScriptRoot 'src/ClaerPlayer.ico'))
    $icon.Save($stream); $stream.Dispose(); $icon.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$sources = Get-ChildItem -LiteralPath 'src' -Filter '*.cs' | Where-Object Name -ne 'Setup.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /win32manifest:src/app.manifest /win32icon:src/ClaerPlayer.ico /out:dist/ClaerPlayer/ClaerPlayer.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll @sources
if ($LASTEXITCODE -ne 0) { throw 'Player build failed.' }
Copy-Item -LiteralPath 'deps/mpv/libmpv-2.dll' -Destination 'dist/ClaerPlayer/libmpv-2.dll'
Write-Output 'Built ClaerPlayer.exe'
