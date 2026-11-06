param([switch]$IncludeTestTools)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$lock = Get-Content -LiteralPath 'dependency-lock.json' -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path 'deps/mpv', 'tools/ffmpeg' | Out-Null
$items = @($lock.mpv)
if ($IncludeTestTools) { $items += $lock.testFfmpeg }
foreach ($item in $items) {
    $target = Join-Path 'deps' $item.file
    if (!(Test-Path -LiteralPath $target)) { Invoke-WebRequest -Uri $item.url -OutFile $target }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLower() -ne $item.sha256) { throw ('Checksum mismatch: ' + $item.file) }
    $destination = if ($item -eq $lock.mpv) { 'deps/mpv' } else { 'tools/ffmpeg' }
    tar -xf $target -C $destination
    if ($LASTEXITCODE -ne 0) { throw 'Extraction failed; a tar build with 7z support is required.' }
}
