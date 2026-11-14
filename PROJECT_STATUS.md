# MASAR: Project Status

**Phase:** 0, technical foundation · **Last updated:** 2026-11-14 · **Gate 0:** not yet passed

## Summary

The research, architecture and pipeline work is done, and the iPad input-probe app compiles on a cloud Mac. The Unity skeleton (physics, cameras, input, menus, performance overlay, test-block generator, build scripts) is **written but not yet compiled**: the Unity editor install needs the owner to sign in to Unity Hub and approve a Windows admin prompt.

## Done (verified)

| Item | Evidence |
|---|---|
| Development environment inventory | `docs/research/ENVIRONMENT_INVENTORY.md` (commands run on the PC) |
| Sourced technical research: engine, G923/iPadOS, build pipeline, licensing | `docs/research/PHASE0_RESEARCH.md`. The `GCRacingWheel` platform claim was checked directly against Apple's documentation metadata |
| Engine decision: Unity 6.3 LTS (6000.3.28f1) + URP | Decision record in PHASE0_RESEARCH §5 |
| Project moved off OneDrive to `E:\MASAR` | File-count and size check after the copy |
| Public GitHub repo + CI | https://github.com/ahmedps520-svg/MASAR |
| **iPad input probe compiles for iOS** | GitHub Actions run 43627224740: `** BUILD SUCCEEDED **` with Xcode 26.6, unsigned IPA artifact |
| CC0 textures acquired and logged | ASSET_INVENTORY.md (ambientCG, CC0) |

## Written, not yet verified

These must compile and pass testing before they count as working.

| Item | Location |
|---|---|
| Vehicle physics core (tires with relaxation + friction ellipse, suspension + anti-roll bars, engine/clutch/6-speed auto+manual, FWD/RWD/AWD differentials, ABS, Ackermann, self-aligning torque) | `game/Assets/Masar/Code/Vehicles` |
| Physics regression tests (0–100 km/h, braking, idle, stability, tire limits) | `game/Assets/Masar/Tests/EditMode` |
| Input abstraction (keyboard, gamepad, desktop HID wheel, touch) + calibration model | `game/Assets/Masar/Code/Input` |
| Six cameras with per-mode saved settings | `game/Assets/Masar/Code/Cameras` |
| Performance monitor + overlay + CSV recorder + iOS native metrics plugin | `Code/Diagnostics`, `Plugins/iOS/MasarNative.mm` |
| Main menu, settings, pause menu, HUD, touch controls, device/wheel test panel | `Code/Game` |
| Procedural test car + Jeddah-style test block generator | `Code/Editor/TestCarBuilder.cs`, `TestBlockBuilder.cs`, `CityKit.cs` |
| Project setup (four real graphics tiers) + build scripts | `MasarProjectSetup.cs`, `MasarBuild.cs`, `tools/build-ios.ps1` |
| Signed TestFlight workflows (probe + game) | `.github/workflows/*testflight*.yml`, `ios-game.yml` |

## Blockers (owner action needed)

1. **Install Unity:** sign in to Unity Hub, install 6000.3.28f1 with iOS Build Support, approve the admin prompt. See BUILD_INSTRUCTIONS §1.
2. **Apple and GitHub secrets for TestFlight:** bundle IDs, app records, App Store Connect API key, and five repository secrets. See BUILD_INSTRUCTIONS §4.
3. **Hardware tests:** connect the G923 to the PC (desktop axis mapping) and to the iPad (probe app).

## Next tasks (in order)

1. Compile the Unity project, fix errors, run the physics tests and record the results.
2. Generate the test block, capture screenshots, and run the Windows build to check driving, cameras and the overlay.
3. Once the secrets exist: TestFlight the probe → run the G923 test on the iPad.
4. TestFlight the game → first performance numbers on the device → Gate 0 review.
