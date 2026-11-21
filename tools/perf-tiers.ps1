<#
.SYNOPSIS
  Runs the Windows player's unattended smoke test once per graphics tier (frame rate uncapped)
  and collects the measured reports into build\perf\. This proves that each tier really changes
  the rendering cost: settings that don't change anything must not be exposed.

.PARAMETER Off
  Optional A/B run: comma-separated features to switch off (stp, ssao, palms, wear, detail, sky).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\perf-tiers.ps1
  # From a PowerShell prompt (so the array parses):
  & tools\perf-tiers.ps1 -Tiers High,Ultra -Off stp
#>
param(
  [string]$Exe = "E:\MASAR\build\windows\MASAR.exe",
  [int]$Seconds = 12,
  [string[]]$Tiers = @("Low", "Medium", "High", "Ultra"),
  [string]$Off = "",
  [string]$Car = ""
)
$ErrorActionPreference = "Stop"
# "-File script -Tiers High,Ultra" passes one string; split it here so both call styles work
$Tiers = @($Tiers | ForEach-Object { $_ -split "," } | Where-Object { $_ })
$repo = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repo "build\perf"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$data = Join-Path $env:USERPROFILE "AppData\LocalLow\MASAR Project\MASAR"
$suffix = if ($Off) { "_no-" + ($Off -replace ",", "-") } else { "" }
if ($Car) { $suffix += "_car-$Car" }

$rows = @()
foreach ($t in $Tiers) {
  $name = "masar_smoke_$t$suffix"
  Remove-Item (Join-Path $data "$name.*") -ErrorAction SilentlyContinue
  $log = Join-Path $outDir "player_$t$suffix.log"
  $argList = @("-masarSmokeTest", "-masarQuality", $t, "-masarUncapped", "-masarSmokeSeconds", "$Seconds",
    "-screen-width", "2420", "-screen-height", "1668", "-screen-fullscreen", "0", "-logFile", $log)
  if ($Off) { $argList += @("-masarOff", $Off) }
  if ($Car) { $argList += @("-masarCar", $Car) }
  $p = Start-Process -FilePath $Exe -PassThru -ArgumentList $argList
  if (-not $p.WaitForExit(180000)) { $p.Kill(); Write-Warning "$t timed out"; continue }
  $rep = Join-Path $data "$name.txt"
  if (-not (Test-Path $rep)) { Write-Warning "$t produced no report"; continue }
  Copy-Item $rep $outDir -Force
  Copy-Item (Join-Path $data "$name.png") $outDir -Force -ErrorAction SilentlyContinue
  $kv = @{}
  Get-Content $rep | ForEach-Object { $k, $v = $_ -split "=", 2; $kv[$k] = $v }
  $rows += [pscustomobject]@{
    Tier = "$t$suffix"; Scale = $kv.renderScale; AvgFps = $kv.avgFps; CpuMainMs = $kv.avgCpuMainMs; GpuMs = $kv.avgGpuMs
    DrawCalls = $kv.drawCalls; Triangles = $kv.triangles; AllocMB = $kv.unityAllocMB; Res = $kv.resolution
  }
}
$rows | Format-Table -AutoSize | Out-String -Width 200
$rows | Export-Csv -NoTypeInformation -Path (Join-Path $outDir "tiers$suffix.csv")
