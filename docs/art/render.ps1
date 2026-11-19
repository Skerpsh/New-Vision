# Renders the README art (docs/art/*.html, cyberdeck look with the Tektur font) to PNG in docs/ with headless Edge.
#   .\docs\art\render.ps1            all images
#   .\docs\art\render.ps1 banner     one image
param([string]$Only = "")

$art = $PSScriptRoot
$docs = Split-Path $art -Parent
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { Write-Error "Microsoft Edge not found"; exit 1 }

# name = width x height of the picture
$images = [ordered]@{
    "banner" = "1600,600"
    "wrist-panel" = "1600,620"
    "stick" = "1600,560"
    "hud" = "1600,820"
}

foreach ($name in $images.Keys) {
    if ($Only -and $Only -ne $name) { continue }
    $html = "file:///" + (Join-Path $art "$name.html").Replace('\', '/')
    $png = Join-Path $docs "$name.png"
    & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
        "--window-size=$($images[$name])" --virtual-time-budget=3000 "--screenshot=$png" $html 2>&1 | Out-Null
    Write-Host "$name.png  $((Get-Item $png).Length / 1KB -as [int]) KB"
}
