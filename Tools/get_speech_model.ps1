# Downloads the Russian streaming speech model for the live subtitles (sherpa-onnx T-One, Apache-2.0, ~123 MB)
# into Assets/StreamingAssets/Speech/t-one-ru (not in git: too big). Run once before building.
#   .\Tools\get_speech_model.ps1
$ErrorActionPreference = "Stop"
$name = "sherpa-onnx-streaming-t-one-russian-2025-09-08"
$url = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/$name.tar.bz2"
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root "Assets\StreamingAssets\Speech\t-one-ru"
$temp = Join-Path ([IO.Path]::GetTempPath()) "newvision-speech-model"

if (Test-Path (Join-Path $target "model.onnx")) { Write-Host "Already there: $target"; exit 0 }
New-Item -ItemType Directory -Force $temp, $target | Out-Null
Write-Host "Downloading $name ..."
curl.exe -sSL -o (Join-Path $temp "model.tar.bz2") $url
tar -xjf (Join-Path $temp "model.tar.bz2") -C $temp
foreach ($file in "model.onnx", "tokens.txt", "LICENSE") {
    Copy-Item (Join-Path $temp "$name\$file") $target
}
Remove-Item $temp -Recurse -Force
Write-Host "Done: $target"
