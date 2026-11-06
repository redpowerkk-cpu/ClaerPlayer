param([switch]$Ui)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
$player = Join-Path $projectRoot 'dist/ClaerPlayer/ClaerPlayer.exe'
$media = Join-Path $PSScriptRoot 'media'
$results = Join-Path $PSScriptRoot 'results'
if (!(Test-Path -LiteralPath $player)) { throw 'Run build.ps1 first.' }
if (!(Test-Path -LiteralPath (Join-Path $media 'h264-aac.mp4'))) { throw 'Run tests/generate.ps1 first.' }
New-Item -ItemType Directory -Force -Path $results | Out-Null
$engineReport = Join-Path $results 'engine.json'
$process = Start-Process -FilePath $player -ArgumentList @('--test', ('"' + $media + '"'), ('"' + $engineReport + '"')) -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Engine checks timed out.' }
$report = Get-Content -LiteralPath $engineReport -Raw | ConvertFrom-Json
if ($process.ExitCode -ne 0 -or $report.failures -ne 0) { $report.tests | Where-Object { !$_.passed } | Format-Table; throw 'Engine checks failed.' }
Write-Output ('Engine checks passed: ' + $report.tests.Count)
if ($Ui) {
    $uiOutput = Join-Path $results ('ui-' + [Guid]::NewGuid().ToString('N'))
    $originalDataDir = $env:CLAERPLAYER_DATA_DIR
    try {
        $env:CLAERPLAYER_DATA_DIR = Join-Path $results 'settings'
        # This optional check displays its own application window and exercises fullscreen.
        $process = Start-Process -FilePath $player -ArgumentList @('--ui-smoke', ('"' + $media + '"'), ('"' + $uiOutput + '"')) -PassThru
        if (!$process.WaitForExit(90000)) { $process.Kill(); throw 'UI checks timed out.' }
        $report = Get-Content -LiteralPath (Join-Path $uiOutput 'results.json') -Raw | ConvertFrom-Json
        if ($report.failures -ne 0) { $report.tests | Where-Object { !$_.passed } | Format-Table; throw 'UI checks failed.' }
        Write-Output ('UI checks passed: ' + $report.tests.Count)
    } finally { $env:CLAERPLAYER_DATA_DIR = $originalDataDir }
}
