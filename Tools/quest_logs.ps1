# New Vision: headset logs over Wi-Fi (no cable needed while testing with the stick on the headset).
#
# Once per headset reboot, with the headset on the USB cable:   .\Tools\quest_logs.ps1 -Setup
#   (switches adb to Wi-Fi, remembers the headset IP, sets a 16 MB log buffer)
# Then, without the cable:
#   .\Tools\quest_logs.ps1            last New Vision / stick / USB lines
#   .\Tools\quest_logs.ps1 -Follow    live stream
#   .\Tools\quest_logs.ps1 -All       everything from the app, incl. inference timings
param([switch]$Setup, [switch]$Follow, [switch]$All, [int]$Lines = 80)

$adb = "C:\Program Files\Unity\Hub\Editor\6000.0.66f2\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$ipFile = Join-Path $env:LOCALAPPDATA "NewVision_quest_ip.txt"
$pattern = if ($All) { "NewVision|Unity  " } else { "\[NewVision\] (stick|object info)|NewVisionBle|AndroidRuntime|FATAL" }

if ($Setup) {
    $ip = (& $adb -d shell ip -f inet addr show wlan0 | Select-String "inet (\d+\.\d+\.\d+\.\d+)").Matches[0].Groups[1].Value
    if (-not $ip) { Write-Error "Headset not on USB or no Wi-Fi"; exit 1 }
    & $adb -d tcpip 5555 | Out-Null
    Start-Sleep -Seconds 3
    & $adb connect "${ip}:5555"
    & $adb -s "${ip}:5555" logcat -G 16M
    Set-Content -Path $ipFile -Value $ip
    Write-Host "Headset at $ip. The USB cable can go now."
    exit 0
}

if (-not (Test-Path $ipFile)) { Write-Error "Run with -Setup first (headset on USB)"; exit 1 }
$target = "$((Get-Content $ipFile).Trim()):5555"
& $adb connect $target | Out-Null

if ($Follow) {
    & $adb -s $target logcat -v time | Select-String $pattern
} else {
    & $adb -s $target logcat -d -v time | Select-String $pattern | Select-Object -Last $Lines | ForEach-Object Line
}
