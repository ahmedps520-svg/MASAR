# MASAR: Build Instructions

Development happens on **Windows 11**. There is **no Mac**: every iPadOS compile and signing step runs on **GitHub Actions macOS runners**. They're free because the repository is public. Installation on the iPad is through **TestFlight**, using the paid Apple Developer membership.

```
Windows PC (Unity 6.3 LTS)                     GitHub (public repo)                          iPad Pro M5
──────────────────────────                     ────────────────────                          ───────────
tools/build-ios.ps1
  ├─ Unity batch export → Xcode project
  ├─ zip + AES-256 encrypt  ────────────────▶  release "xcode-latest" (encrypted asset)
  └─ gh workflow run ios-game.yml ─────────▶  macOS runner: decrypt → xcodebuild archive
                                                 → sign (App Store Connect API key)
                                                 → upload ─────────────────────────────────▶ TestFlight app
```

---

## 1. Windows setup (one time)

| Step | Who | How |
|---|---|---|
| Git, GitHub CLI | done | `gh auth status` shows you're logged in |
| Unity Hub | done | Installed with winget. Editor install path: `E:\Unity\Editors` |
| **Unity account sign-in** | **you** | Open Unity Hub → sign in. The free Personal license is fine |
| **Unity 6000.3.28f1 + iOS Build Support** | **you** | Unity Hub → Installs → Install Editor → 6000.3.28f1 (LTS) → tick **iOS Build Support** → approve the Windows admin prompt |
| Blender 5.2 | done | Asset tooling (later phases) |

The repository lives at **`E:\MASAR`**, a local drive outside OneDrive. Keep it off cloud-synced folders, because Unity's `Library/` folder and git don't survive sync conflicts.

## 2. Open and run the game on Windows

1. Unity Hub → **Add** → `E:\MASAR\game`. The first open imports the packages and takes several minutes.
2. Menu **MASAR → Setup → Configure Project**. This sets the player settings, the four graphics tiers and the quality levels.
3. Menu **MASAR → Build → Test Block Scene**. This generates the test car, the Jeddah test block, the main menu and the baked reflections.
4. Open `Assets/Masar/Scenes/MainMenu.unity` and press **Play**.

Command-line equivalents (used by Claude Code):

```bash
"E:/Unity/Editors/6000.3.28f1/Editor/Unity.exe" -batchmode -quit -projectPath E:/MASAR/game -executeMethod Masar.EditorTools.MasarProjectSetup.ConfigureAndBuildScenes -logFile E:/MASAR/build/setup.log
```

```bash
"E:/Unity/Editors/6000.3.28f1/Editor/Unity.exe" -batchmode -quit -projectPath E:/MASAR/game -executeMethod Masar.EditorTools.MasarBuild.BuildWindows -logFile E:/MASAR/build/windows.log
```

```bash
"E:/Unity/Editors/6000.3.28f1/Editor/Unity.exe" -batchmode -projectPath E:/MASAR/game -runTests -testPlatform EditMode -testResults E:/MASAR/build/tests.xml -logFile E:/MASAR/build/tests.log
```

### Generated content is not in git

The generators output about 132 MB of meshes, plus the scenes, the test-car prefab and the materials. All of it is **gitignored**, because the builders in `Assets/Masar/Code/Editor` are the source of truth. After a fresh clone, or after changing a generator, run `ConfigureAndBuildScenes` (above) before playing or building.

### Automated checks on Windows

| Check | Command |
|---|---|
| Physics unit tests (EditMode) | `-runTests -testPlatform EditMode` (above) |
| Full-stack drive test in the real scene (PlayMode) | `"E:/Unity/Editors/6000.3.28f1/Editor/Unity.exe" -batchmode -projectPath E:/MASAR/game -runTests -testPlatform PlayMode -testResults E:/MASAR/build/playtests.xml` |
| Review screenshots at iPad resolution | `-executeMethod Masar.EditorTools.MasarScreenshots.CaptureAll -quality High`, written to `build/screenshots/` |
| Player smoke test (unattended drive + perf report) | `build\windows\MASAR.exe -masarSmokeTest [-masarQuality High] [-masarUncapped] [-masarSmokeSeconds 12]`. The report is written to `%USERPROFILE%\AppData\LocalLow\MASAR Project\MASAR\` |
| All graphics tiers, measured | `powershell -ExecutionPolicy Bypass -File tools\perf-tiers.ps1`, written to `build/perf/tiers.csv` |

## 3. The iPad input probe (native, no Unity)

`tools/probes/ios-input-probe` is a small SwiftUI app. It reports what iPadOS exposes for the G923, plus thermal state, memory and GPU details.

- Every push that touches it is built automatically, unsigned, by `.github/workflows/ios-input-probe.yml`. That's a compile check only.
- After the secrets in section 4 are set: GitHub → Actions → **iOS Input Probe → TestFlight** → Run workflow. Or run:

```bash
gh workflow run ios-input-probe-testflight.yml --repo ahmedps520-svg/MASAR
```

## 4. One-time Apple + GitHub secrets setup (you)

Claude can't do these steps: they involve your Apple account, keys and 2FA. They take about 15 minutes in a browser on Windows.

### 4.1 Register the bundle IDs

developer.apple.com → Account → **Certificates, Identifiers & Profiles → Identifiers → +** → App IDs → App → *Explicit*:

| Description | Bundle ID |
|---|---|
| MASAR Input Probe | `app.masar.inputprobe` |
| MASAR | `app.masar.game` |

No extra capabilities are needed.

### 4.2 Create the two app records

appstoreconnect.apple.com → **Apps → + → New App**. Platform iOS, any SKU, then pick the bundle ID.

- App names must be unique across the App Store, so "MASAR" may be taken. Use something like **"MASAR Drive"** and **"MASAR Probe"**. You can rename them later.
- These are for internal TestFlight only. Nothing is published.

### 4.3 Create an App Store Connect API key

App Store Connect → **Users and Access → Integrations → App Store Connect API → Team Keys → +**

- Name it `MASAR CI`. Access: **Admin**, which signing needs.
- **Download the `.p8` file. Apple lets you download it only once.** Keep it outside the repo.
- Write down the **Key ID** and the **Issuer ID** shown on that page.
- Find your **Team ID** at developer.apple.com → Account → Membership details.

### 4.4 Store the secrets in GitHub

Run these in PowerShell. Each `gh secret set` command prompts you to paste the value, so it never appears in your shell history:

```bash
gh secret set ASC_KEY_ID --repo ahmedps520-svg/MASAR
```

```bash
gh secret set ASC_ISSUER_ID --repo ahmedps520-svg/MASAR
```

```bash
gh secret set APPLE_TEAM_ID --repo ahmedps520-svg/MASAR
```

```bash
Get-Content -Raw "C:\path\to\AuthKey_XXXXXXXXXX.p8" | gh secret set ASC_KEY_P8 --repo ahmedps520-svg/MASAR
```

Finally, create the passphrase that encrypts the Xcode export. This one PowerShell command generates a random passphrase, saves it to `%USERPROFILE%\.masar\` (outside the repo), and stores it in GitHub:

```bash
$p=[Convert]::ToBase64String((1..48|%{[byte](Get-Random -Max 256)})); New-Item -ItemType Directory -Force "$env:USERPROFILE\.masar" | Out-Null; Set-Content -NoNewline "$env:USERPROFILE\.masar\xcode_passphrase.txt" $p; $p | gh secret set XCODE_ZIP_PASSPHRASE --repo ahmedps520-svg/MASAR
```

### 4.5 TestFlight on the iPad

1. Install **TestFlight** from the App Store on the iPad. Sign in with the same Apple ID as your developer account.
2. After the first successful upload, open App Store Connect → your app → **TestFlight**. Wait until the build has finished processing, usually 5–30 minutes. Answer the export-compliance question if asked; the build declares that it uses no non-exempt encryption.
3. Create an **Internal Testing** group, add yourself, and enable the build. It then appears in the TestFlight app on the iPad.

## 5. Build the game for the iPad

```bash
powershell -ExecutionPolicy Bypass -File E:\MASAR\tools\build-ios.ps1
```

This script:
1. Exports the Xcode project with Unity, using the commit count as the build number.
2. Zips and encrypts it, then uploads it to the `xcode-latest` pre-release.
3. Starts `.github/workflows/ios-game.yml`.

The workflow compiles, signs and uploads the build to TestFlight. Watch it with:

```bash
gh run list --repo ahmedps520-svg/MASAR --limit 5
```

Add `-Development` to the script for a development build. That build can attach the Unity Profiler over Wi-Fi from the Windows editor, which still needs confirming on the device.

## 6. Known pipeline limits

| Limit | Impact | Mitigation |
|---|---|---|
| No Mac | No Xcode debugger, no Instruments, no local device logs | Unity Profiler over Wi-Fi (development builds), the on-device performance overlay and CSV logs, TestFlight crash reports in App Store Connect |
| GitHub-hosted runner disk (14 GB documented) | A large Unity export may run out of space | The workflow prints `df -h`. Fallback: Codemagic's free tier (500 macOS minutes a month, about 156 GB disk) |
| App Store Connect API key needs the Admin role | Required for automatic signing | Keep the key's scope to the MASAR team and revoke it if leaked |
| TestFlight builds expire after 90 days | Old builds stop launching | Upload regularly |
| Build processing on Apple's side | 5–30 minutes before the iPad can install | — |
