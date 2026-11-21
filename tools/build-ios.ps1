<#
.SYNOPSIS
  Exports the MASAR Unity project as an iOS Xcode project on Windows, encrypts it,
  uploads it to the "xcode-latest" pre-release, and starts the TestFlight workflow.

.DESCRIPTION
  Why encrypted: the GitHub repository is public, and the Xcode export contains
  Unity engine libraries that must not be redistributed publicly. The passphrase lives
  only in %USERPROFILE%\.masar\xcode_passphrase.txt (never in the repo) and in the
  repository secret XCODE_ZIP_PASSPHRASE.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\build-ios.ps1
  powershell -ExecutionPolicy Bypass -File tools\build-ios.ps1 -Development -NoUpload
#>
param(
  [switch]$Development,
  [switch]$NoUpload,
  [switch]$SkipExport,
  [string]$UnityExe = "E:\Unity\Editors\6000.3.28f1\Editor\Unity.exe"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "game"
$out = Join-Path $repo "build\ios-xcode"
$log = Join-Path $repo "build\unity-ios-export.log"
$passFile = Join-Path $env:USERPROFILE ".masar\xcode_passphrase.txt"
$openssl = "C:\Program Files\Git\usr\bin\openssl.exe"

if (-not (Test-Path $UnityExe)) { throw "Unity not found at $UnityExe" }
if (-not (Test-Path $passFile)) { throw "Missing $passFile. See BUILD_INSTRUCTIONS.md section 4 (one-time secrets setup)." }
New-Item -ItemType Directory -Force -Path (Join-Path $repo "build") | Out-Null

# Build number = commit count, so every TestFlight upload is unique and increasing
$buildNumber = (git -C $repo rev-list --count HEAD).Trim()
$commit = (git -C $repo rev-parse --short HEAD).Trim()

if ($SkipExport) {
  # Re-package and re-upload the existing export (e.g. after a CI-side failure)
  if (-not (Test-Path (Join-Path $out "Unity-iPhone.xcodeproj"))) { throw "No existing export in $out" }
  $info = Get-Content (Join-Path $out "MASAR_BUILD_INFO.txt") -ErrorAction SilentlyContinue
  $prev = ($info | Where-Object { $_ -like "build=*" }) -replace "build=", ""
  if ($prev) { $buildNumber = $prev }
  Write-Host "Reusing the existing Xcode export (build $buildNumber)"
} else {
  $unityArgs = @("-batchmode", "-quit", "-buildTarget", "iOS", "-projectPath", $project, "-logFile", $log,
            "-executeMethod", "Masar.EditorTools.MasarBuild.BuildIOSXcode", "-buildNumber", $buildNumber)
  if ($Development) { $unityArgs += "-development" }
  Write-Host "Exporting Xcode project (build $buildNumber, commit $commit)..."
  $p = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
  if ($p.ExitCode -ne 0) { Get-Content $log -Tail 40; throw "Unity export failed (exit $($p.ExitCode)). Log: $log" }
  Select-String -Path $log -Pattern "\[MASAR\] Build" | ForEach-Object { $_.Line }
}

"commit=$commit`nbuild=$buildNumber`ndevelopment=$Development`ndate=$(Get-Date -Format s)" | Set-Content -Encoding ascii (Join-Path $out "MASAR_BUILD_INFO.txt")

$zip = Join-Path $repo "build\masar-ios-xcode.zip"
$enc = "$zip.enc"
if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host "Zipping..."
# Windows tar (bsdtar) writes forward-slash paths. Compress-Archive in Windows PowerShell 5.1
# writes backslashes, which macOS unzip turns into unreadable directories.
& "$env:SystemRoot\System32\tar.exe" -a -c -f $zip -C $out .
if ($LASTEXITCODE -ne 0) { throw "tar failed (exit $LASTEXITCODE)" }
Write-Host ("Zip: {0:N0} MB" -f ((Get-Item $zip).Length / 1MB))

$env:MASAR_XCODE_PASS = (Get-Content -Raw $passFile).Trim()
& $openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt -pass env:MASAR_XCODE_PASS -in $zip -out $enc
Remove-Item Env:\MASAR_XCODE_PASS
Remove-Item $zip -Force
if ($NoUpload) { Write-Host "Encrypted export ready: $enc (not uploaded)"; exit 0 }

$exists = $true
try { gh release view xcode-latest --repo ahmedps520-svg/MASAR *> $null; if ($LASTEXITCODE -ne 0) { $exists = $false } } catch { $exists = $false }
if (-not $exists) {
  gh release create xcode-latest --repo ahmedps520-svg/MASAR --prerelease --title "Encrypted Xcode export (CI input)" `
    --notes "Encrypted CI input for the TestFlight workflow. Not a release; cannot be used without the repository secret."
}
gh release upload xcode-latest $enc --repo ahmedps520-svg/MASAR --clobber
gh workflow run ios-game.yml --repo ahmedps520-svg/MASAR
Write-Host "Uploaded and started the TestFlight workflow. Watch it with:  gh run list --repo ahmedps520-svg/MASAR"

