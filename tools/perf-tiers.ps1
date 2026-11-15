<#
.SYNOPSIS
  Runs the Windows player's unattended smoke test once per graphics tier (frame rate uncapped)
  and collects the measured reports into build\perf\. This proves that each tier really changes
  the rendering cost: settings that don't change anything must not be exposed.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\perf-tiers.ps1
#>
param(
  [string]$Exe = "E:\MASAR\build\windows\MASAR.exe",
  [int]$Seconds = 12,
  [string[]]$Tiers = @("Low", "Medium", "High", "Ultra")
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repo "build\perf"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$data = Join-Path $env:USERPROFILE "AppData\LocalLow\MASAR Project\MASAR"

$rows = @()
foreach ($t in $Tiers) {
  Remove-Item (Join-Path $data "masar_smoke_$t.*") -ErrorAction SilentlyContinue
  $log = Join-Path $outDir "player_$t.log"
  $p = Start-Process -FilePath $Exe -PassThru -ArgumentList @(
    "-masarSmokeTest", "-masarQuality", $t, "-masarUncapped", "-masarSmokeSeconds", "$Seconds",
    "-screen-width", "2420", "-screen-height", "1668", "-screen-fullscreen", "0", "-logFile", $log)
  if (-not $p.WaitForExit(180000)) { $p.Kill(); Write-Warning "$t timed out"; continue }
  $rep = Join-Path $data "masar_smoke_$t.txt"
  if (-not (Test-Path $rep)) { Write-Warning "$t produced no report"; continue }
  Copy-Item $rep $outDir -Force
  Copy-Item (Join-Path $data "masar_smoke_$t.png") $outDir -Force -ErrorAction SilentlyContinue
  $kv = @{}
  Get-Content $rep | ForEach-Object { $k, $v = $_ -split "=", 2; $kv[$k] = $v }
  $rows += [pscustomobject]@{
    Tier = $t; Scale = $kv.renderScale; AvgFps = $kv.avgFps; CpuMainMs = $kv.avgCpuMainMs; GpuMs = $kv.avgGpuMs
    DrawCalls = $kv.drawCalls; Triangles = $kv.triangles; AllocMB = $kv.unityAllocMB; Res = $kv.resolution
  }
}
$rows | Format-Table -AutoSize | Out-String -Width 200
$rows | Export-Csv -NoTypeInformation -Path (Join-Path $outDir "tiers.csv")
