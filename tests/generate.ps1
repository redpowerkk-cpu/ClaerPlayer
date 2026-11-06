$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$ffmpeg = Join-Path $PWD 'tools/ffmpeg/ffmpeg.exe'
$folder = Join-Path $PWD 'tests/media'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
function Convert-Media([string[]]$Arguments) { & $ffmpeg -hide_banner -loglevel error -y @Arguments; if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed.' } }
Convert-Media @('-f','lavfi','-i','testsrc2=size=640x360:rate=30','-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-t','9','-c:v','libx264','-preset','ultrafast','-crf','24','-c:a','aac',"$folder/h264-aac.mp4")
foreach ($extension in @('mkv','mov','ts')) { Convert-Media @('-i',"$folder/h264-aac.mp4",'-c','copy',"$folder/h264.$extension") }
Convert-Media @('-i',"$folder/h264-aac.mp4",'-t','2','-c:v','mpeg4','-q:v','5','-c:a','libmp3lame',"$folder/mpeg4-mp3.avi")
Convert-Media @('-i',"$folder/h264-aac.mp4",'-t','2','-c:v','wmv2','-c:a','wmav2',"$folder/wmv2.wmv")
Convert-Media @('-i',"$folder/h264-aac.mp4",'-t','2','-c:v','libvpx-vp9','-deadline','realtime','-cpu-used','8','-c:a','libopus',"$folder/vp9-opus.webm")
Convert-Media @('-i',"$folder/h264-aac.mp4",'-t','2','-c:v','libx265','-preset','ultrafast','-x265-params','log-level=error','-pix_fmt','yuv420p10le','-an',"$folder/hevc-10bit.mp4")
Convert-Media @('-i',"$folder/h264-aac.mp4",'-t','2','-c:v','libaom-av1','-cpu-used','8','-row-mt','1','-crf','42','-an',"$folder/av1.mp4")
Convert-Media @('-i',"$folder/h264-aac.mp4",'-t','1','-vf','fps=10,scale=320:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse','-loop','0',"$folder/animated.gif")
Copy-Item -LiteralPath "$folder/h264-aac.mp4" -Destination "$folder/한글 파일 이름.mp4"
"1`n00:00:00,000 --> 00:00:08,000`n한글 자막 테스트 · ClaerPlayer`n" | Set-Content -LiteralPath "$folder/sample.srt" -Encoding UTF8
'This is deliberately invalid media.' | Set-Content -LiteralPath "$folder/broken.mp4" -Encoding UTF8
Get-ChildItem -LiteralPath $folder | Select-Object Name,Length
