<#
.SYNOPSIS
  MASAR one-time setup. Run once:
      powershell -ExecutionPolicy Bypass -File E:\MASAR\tools\setup.ps1

  It opens the right web pages, waits for you, and stores everything where the automation
  needs it. Only YOU handle your keys: the script reads them from your Downloads folder or
  from what you paste, and sends them straight to GitHub's encrypted secrets / Claude Code's
  MCP config. Nothing secret is written into the repository.

  Steps:
    1. App Store Connect API key  (pick an existing AuthKey_*.p8 or create a new one)
    2. Store GitHub secrets       (key, issuer, team, encryption passphrase)
    3. App record                 (bundle ID is registered for you; you click "New App" once)
    4. ForgeBase                  (token for the diagnostics backend, registered with Claude Code)
#>
$ErrorActionPreference = "Stop"
$repo = "ahmedps520-svg/MASAR"

function Step($t) { Write-Host ""; Write-Host "==== $t ====" -ForegroundColor Cyan }
function Info($t) { Write-Host "  $t" }
function Ask($t) { Read-Host "  $t" }

function Wait-Run($workflow) {
  Start-Sleep -Seconds 4
  $id = (gh run list --repo $repo --workflow $workflow --limit 1 --json databaseId --jq ".[0].databaseId")
  if ($id) { gh run watch $id --repo $repo --exit-status | Out-Null; gh run view $id --repo $repo --log | Select-String -Pattern "API key|Bundle ID|Registered|App record|APP_RECORD|error|warning" | ForEach-Object { Info (($_.Line -split "`t")[-1]) } }
}

# ---------------------------------------------------------------- 1. API key
Step "1/4  App Store Connect API key"
$downloads = Join-Path $env:USERPROFILE "Downloads"
$keys = @(Get-ChildItem $downloads -Filter "AuthKey_*.p8" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
$file = $null
if ($keys.Count -gt 0) {
  Info "Found these key files in Downloads:"
  for ($i = 0; $i -lt $keys.Count; $i++) { Info ("  [{0}] {1}   ({2:yyyy-MM-dd})" -f ($i + 1), $keys[$i].Name, $keys[$i].LastWriteTime) }
  Info "Use one only if it is a TEAM key with the ADMIN role (App Store Connect > Users and Access > Integrations)."
  $pick = Ask "Type a number to use it, or N to create a new key"
  if ($pick -match '^\d+$' -and [int]$pick -ge 1 -and [int]$pick -le $keys.Count) { $file = $keys[[int]$pick - 1] }
}
Start-Process "https://appstoreconnect.apple.com/access/integrations/api"
if (-not $file) {
  Info "In the page that just opened: Team Keys > + > Name 'MASAR CI', Access 'Admin' > Generate > Download."
  Info "Waiting for the new AuthKey_*.p8 to appear in Downloads..."
  $before = @($keys | ForEach-Object { $_.Name })
  while (-not $file) {
    Start-Sleep -Seconds 3
    $file = Get-ChildItem $downloads -Filter "AuthKey_*.p8" | Where-Object { $before -notcontains $_.Name } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  }
  Info "Got $($file.Name)"
}
$keyId = $file.BaseName -replace '^AuthKey_', ''
do { $issuer = (Ask "Paste the Issuer ID (shown above the keys list on that page)").Trim() } until ($issuer -match '^[0-9a-fA-F-]{36}$')
Start-Process "https://developer.apple.com/account#MembershipDetailsCard"
do { $team = (Ask "Paste your Team ID (10 characters, under Membership details)").Trim().ToUpper() } until ($team -match '^[A-Z0-9]{10}$')

# ---------------------------------------------------------------- 2. secrets
Step "2/4  Storing encrypted GitHub secrets"
gh secret set ASC_KEY_ID --repo $repo --body $keyId
gh secret set ASC_ISSUER_ID --repo $repo --body $issuer
gh secret set APPLE_TEAM_ID --repo $repo --body $team
Get-Content -Raw $file.FullName | gh secret set ASC_KEY_P8 --repo $repo
$bytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$pass = [Convert]::ToBase64String($bytes)
$passDir = Join-Path $env:USERPROFILE ".masar"
New-Item -ItemType Directory -Force -Path $passDir | Out-Null
Set-Content -NoNewline -Path (Join-Path $passDir "xcode_passphrase.txt") -Value $pass
$pass | gh secret set XCODE_ZIP_PASSPHRASE --repo $repo
$pass = $null
Info "Stored: ASC_KEY_ID, ASC_ISSUER_ID, APPLE_TEAM_ID, ASC_KEY_P8, XCODE_ZIP_PASSPHRASE"

# ---------------------------------------------------------------- 3. app record
Step "3/4  App record in App Store Connect"
Info "Checking the key and registering the bundle ID app.masar.game..."
gh workflow run asc-preflight.yml --repo $repo
Wait-Run "asc-preflight.yml"
Start-Process "https://appstoreconnect.apple.com/apps"
Info "In the page that just opened: Apps > + > New App"
Info "   Platform: iOS   Name: MASAR Drive (any free name)   Language: English"
Info "   Bundle ID: app.masar.game   SKU: masar-game   User access: Full Access   > Create"
Ask "Press Enter after you click Create" | Out-Null
gh workflow run asc-preflight.yml --repo $repo
Wait-Run "asc-preflight.yml"

# ---------------------------------------------------------------- 4. ForgeBase
Step "4/4  ForgeBase (where the iPad sends diagnostics)"
Start-Process "https://forgebase.art/app"
Info "In ForgeBase: Studio settings > AI assistants > New token > level 'write' > copy it."
$sec = Read-Host "  Paste the ForgeBase token (input hidden)" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
$tok = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
try { claude mcp remove --scope user forgebase 2>&1 | Out-Null } catch { }   # ignore "not found" on first run
claude mcp add --scope user --transport http forgebase https://forgebase.art/mcp --header "Authorization: Bearer $tok"
$tok = $null

Step "Done"
Info "Everything is in place. Go back to Claude and say:  setup done"
Info "Claude then creates the diagnostics backend, builds the game, uploads it to TestFlight"
Info "and invites your email. You'll get the TestFlight invite on your iPad."
