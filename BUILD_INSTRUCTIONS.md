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

### Selectable cars (local files, not in git)

The car selector lists the procedural test sedan plus every imported car whose source files are present:

| Car id | Put the Sketchfab glTF download here (unzipped) |
|---|---|
| `jetta_gli` | `game/Assets/Masar/Art/Vehicles/Imported/jetta_gli/scene.gltf` (+ `scene.bin`, `textures/`) |
| `elantra_n` | `game/Assets/Masar/Art/Vehicles/Imported/elantra_n/scene.gltf` |
| `aventador_svj` | `game/Assets/Masar/Art/Vehicles/Imported/aventador_svj/scene.gltf` |
| `aventador_mansory` | `game/Assets/Masar/Art/Vehicles/Imported/aventador_mansory/aventador_mansory.glb` |

The whole `Art/Vehicles/` folder is gitignored, because some of these licenses forbid redistribution and the repository is public (ASSET_INVENTORY V001–V004). `ConfigureAndBuildScenes` runs `ImportedCarBuilder.BuildAll`, which converts the models into prefabs and writes the catalog. To rebuild only the cars:

```bash
"E:/Unity/Editors/6000.3.28f1/Editor/Unity.exe" -batchmode -quit -projectPath E:/MASAR/game -executeMethod Masar.EditorTools.ImportedCarBuilder.BuildAll [-masarCars jetta_gli,elantra_n] -logFile E:/MASAR/build/cars_import.log
```

Adding another car means adding one `Spec` entry in `ImportedCarBuilder.cs`: name patterns for the tires, calipers, steering wheel, lamps, paint and glass, plus the class of engine and chassis. Review a car with `MasarScreenshots.CaptureAll -masarCar <id> -masarViews 01,02,10`, or measure it with the player smoke test plus `-masarCar <id>`.

### Automated checks on Windows

| Check | Command |
|---|---|
| Physics unit tests (EditMode) | `-runTests -testPlatform EditMode` (above) |
| Full-stack drive test in the real scene (PlayMode) | `"E:/Unity/Editors/6000.3.28f1/Editor/Unity.exe" -batchmode -projectPath E:/MASAR/game -runTests -testPlatform PlayMode -testResults E:/MASAR/build/playtests.xml` |
| Review screenshots at iPad resolution | `-executeMethod Masar.EditorTools.MasarScreenshots.CaptureAll -quality High`, written to `build/screenshots/` |
| Player smoke test (unattended drive + perf report) | `build\windows\MASAR.exe -masarSmokeTest [-masarQuality High] [-masarUncapped] [-masarSmokeSeconds 12]`. The report is written to `%USERPROFILE%\AppData\LocalLow\MASAR Project\MASAR\` |
| All graphics tiers, measured | `powershell -ExecutionPolicy Bypass -File tools\perf-tiers.ps1`, written to `build/perf/tiers.csv` |
| UI layout capture at iPad aspect (menu, settings, devices, touch HUD, pause) | `build\windows\MASAR.exe -masarUiCapture -screen-width 1934 -screen-height 1333 -screen-fullscreen 0`, written to `ui_*.png` in the same LocalLow folder |

## 3. The iPad input probe (native, no Unity)

`tools/probes/ios-input-probe` is a small SwiftUI app. It reports what iPadOS exposes for the G923, plus thermal state, memory and GPU details.

- Every push that touches it is built automatically, unsigned, by `.github/workflows/ios-input-probe.yml`. That's a compile check only.
- After the secrets in section 4 are set: GitHub → Actions → **iOS Input Probe → TestFlight** → Run workflow. Or run:

```bash
gh workflow run ios-input-probe-testflight.yml --repo ahmedps520-svg/MASAR
```

## 4. One-time setup: one command (`tools/setup.ps1`)

```bash
powershell -ExecutionPolicy Bypass -File E:\MASAR\tools\setup.ps1
```

The script opens each page for you, waits, and stores everything:
1. **App Store Connect API key.** Pick an existing `AuthKey_*.p8` from Downloads (a Team key with the Admin role), or create one on the page it opens. Then paste the Issuer ID and your Team ID.
2. **GitHub secrets.** `ASC_KEY_ID`, `ASC_ISSUER_ID`, `APPLE_TEAM_ID`, `ASC_KEY_P8`, plus a freshly generated `XCODE_ZIP_PASSPHRASE`. These go into GitHub's encrypted secrets; nothing secret goes into the repo.
3. **App record.** The `asc-preflight` workflow registers the bundle ID `app.masar.game` through Apple's API. You click *New App* once, because Apple has no API for creating app records.
4. **ForgeBase token** for the diagnostics backend. It's registered with Claude Code as the `forgebase` MCP server, so Claude can create the project and read the reports.

`TESTER_EMAIL` is already stored as a secret. After each upload, CI waits for Apple's processing, sets export compliance, ensures the **MASAR Internal** TestFlight group, and invites that email (`tools/ci/testflight_invite.py`). Internal testers must be users of the App Store Connect team. If `TESTER_EMAIL` isn't one, the script adds the team's account holder instead, without printing the address, because the logs are public. To redo only this step: `gh workflow run testflight-invite.yml --repo ahmedps520-svg/MASAR`.

First successful upload: **build 11, 2026-11-21** (after two fixes: a Windows-made zip with backslash paths, and the missing 1024 px App Store icon).

Why there's still a human step: Apple requires the account owner's own sign-in to create keys and app records. Claude doesn't sign in to accounts or type API keys or tokens into anything.

### 4.0 Reference: what the setup script automates (manual equivalent)

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

## 5a. Racing wheel on the iPad: the wheel bridge

iPadOS gives apps no access to a racing wheel plugged into the iPad (KNOWN_ISSUES L1). Plug the G923 into the Windows PC instead. The bridge streams it to the iPad over Wi-Fi and plays the game's force feedback on the wheel.

1. Plug the G923 into the PC (USB) and power it. Close any other game using the wheel. **Logitech G HUB must be installed.** The PlayStation version of the G923 powers up in PlayStation compatibility mode (USB ID `046D:C267`): a 14-button gamepad with no steering, pedals or force feedback. G HUB switches it to PC mode (`046D:C266`). The bridge starts G HUB in the background when it sees compatibility mode. The switch lasts until the wheel is unplugged.
2. On the iPad, open MASAR → **Input devices & wheel test**. It shows the address to use, for example `192.168.1.23`. Allow **Local Network** when iPadOS asks; if you missed the prompt, use Settings → Privacy & Security → Local Network → MASAR.
3. On the PC, run (the address is remembered after the first time):

```powershell
powershell -ExecutionPolicy Bypass -File E:\MASAR\tools\start-wheel-bridge.ps1 192.168.1.23
```

4. The panel on the iPad turns **Connected**. Turn the wheel or press a pedal and it becomes the active input.

G923 buttons in the game: paddles = gear up/down, Cross = camera, Square = handbrake, Circle = lights, Options = pause, D-pad up = performance overlay, D-pad down = automatic/manual. The bridge prints this legend when it starts.

Checks:
- `start-wheel-bridge.ps1 -TestForce` (hands off the rim) pushes it right, then left, at 25 % for half a second each. It measures where the rim went and saves the correct force direction.
- `MasarWheelBridge.exe --probe 30` prints every axis and button as it changes (turn the wheel, press each pedal, press each button).
- `-Gain 0.6` gives weaker force; `-Simulate` tests the network without the wheel.
- If the steering is too fast or too slow, the operating range in G HUB isn't 900°: set it to 900°, or pass `--range <degrees>` to the bridge.
- Windows may ask to allow the bridge on private networks: allow it.

The wheel is read through DirectInput, which keeps working while the bridge window is in the background, and the force feedback goes through Logitech's driver. Settings are kept in `%APPDATA%\MASAR\wheel-bridge.json`: iPad address, gain, force direction, range, and the axis layout (`X,Y,Rz,S0` = wheel, throttle, brake, clutch).

How it works: UDP on port 7780. The bridge sends wheel angle (degrees), pedals and buttons at 250 Hz. The game maps the rim 1:1 onto the current car's steering lock and sends back force feedback at about 100 Hz: self-aligning torque from the front tires, parking-speed scrub, and a soft stop at the car's steering lock. Forces drop to zero if the game pauses or the link goes quiet for 0.3 s. Wire format: `tools/wheel-bridge/Protocol.cs` and `game/Assets/Masar/Code/Input/NetworkWheel.cs`. Loopback self-test: `-executeMethod Masar.EditorTools.WheelBridgeSelfTest.Run`, with the bridge running in `-Simulate` mode against `127.0.0.1`.

## 6. Known pipeline limits

| Limit | Impact | Mitigation |
|---|---|---|
| No Mac | No Xcode debugger, no Instruments, no local device logs | Unity Profiler over Wi-Fi (development builds), the on-device performance overlay and CSV logs, TestFlight crash reports in App Store Connect |
| GitHub-hosted runner disk (14 GB documented) | A large Unity export may run out of space | The workflow prints `df -h`. Fallback: Codemagic's free tier (500 macOS minutes a month, about 156 GB disk) |
| App Store Connect API key needs the Admin role | Required for automatic signing | Keep the key's scope to the MASAR team and revoke it if leaked |
| TestFlight builds expire after 90 days | Old builds stop launching | Upload regularly |
| Build processing on Apple's side | 5–30 minutes before the iPad can install | — |
