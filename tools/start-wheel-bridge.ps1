<#
.SYNOPSIS
  Starts the MASAR Wheel Bridge: the G923 plugged into this PC is streamed to the game on the
  iPad over Wi-Fi, with force feedback coming back. Builds the bridge first if needed.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File E:\MASAR\tools\start-wheel-bridge.ps1 192.168.1.23
  powershell -ExecutionPolicy Bypass -File E:\MASAR\tools\start-wheel-bridge.ps1            # reuses the last address
  powershell -ExecutionPolicy Bypass -File E:\MASAR\tools\start-wheel-bridge.ps1 -TestForce # checks the force direction
#>
param(
  [string]$IpadAddress = "",
  [double]$Gain = -1,
  [switch]$TestForce,
  [switch]$InvertForce,
  [switch]$Simulate
)
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo "build\wheel-bridge\MasarWheelBridge.exe"
$src = Join-Path $repo "tools\wheel-bridge"
$newest = (Get-ChildItem $src -Filter *.cs | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
if (-not (Test-Path $exe) -or (Get-Item $exe).LastWriteTime -lt $newest) {
  Write-Host "Building the wheel bridge..."
  dotnet publish $src -c Release -o (Join-Path $repo "build\wheel-bridge") --nologo | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Build failed (is the .NET SDK installed?)" }
}
$a = @()
if ($IpadAddress) { $a += $IpadAddress }
if ($Gain -ge 0) { $a += @("--gain", "$Gain") }
if ($TestForce) { $a += "--test-ffb" }
if ($InvertForce) { $a += "--invert-force" }
if ($Simulate) { $a += "--simulate" }
& $exe @a
