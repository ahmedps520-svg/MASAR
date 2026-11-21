# MASAR: Project Status

**Phase:** Month 2 (approved 2026-11-21) · **Last updated:** 2026-11-21 · **Gate 0:** the iPad runs it (TestFlight); device performance numbers still to capture

## Summary

The Unity 6.3 LTS skeleton **compiles, passes its physics tests, and drives in the generated Jeddah test block** in the editor. The iPad input probe compiles on a cloud Mac. What's left for Gate 0 is **running on the actual iPad**, which waits on the Apple and GitHub secrets (BUILD_INSTRUCTIONS §4) and a TestFlight upload.

## Done (verified)

| Item | Evidence |
|---|---|
| Environment inventory, sourced research, engine decision | `docs/research/` |
| Project off OneDrive (`E:\MASAR`), public repo + CI | https://github.com/ahmedps520-svg/MASAR |
| **iPad input probe compiles for iOS** | GitHub Actions run 43627224740: BUILD SUCCEEDED (Xcode 26.6) |
| Unity 6000.3.28f1 + iOS Build Support installed, license active | Batch-mode runs succeed |
| **All 9 MASAR assemblies compile: 0 errors, 0 warnings** | `build/compile1.log` |
| **Physics regression tests: 6/6 pass** (0–100 km/h 9.47 s, 100–0 km/h 39.0 m) | PERFORMANCE_REPORT.md |
| **In-engine drive test: pass** (accelerate, brake, turn, stays upright, real PhysX scene) | PERFORMANCE_REPORT.md |
| Four real graphics tiers (Low/Medium/High/Ultra, each its own URP asset) | `Assets/Masar/Settings` |
| Generated test car + Jeddah-style block, rendered review screenshots | `build/screenshots/` (local), see the Phase 0 report |
| **Windows player build + unattended smoke test** | 194 MB, scripted drive passes |
| **Graphics tiers measured** (PC reference): GPU 0.68 → 2.49 ms, draw calls 380 → 1,262 | PERFORMANCE_REPORT.md |
| **Building LOD groups + baked Umbra occlusion culling** | Builder output, measured effect of LOD |
| **iOS Xcode export** (iPad only, iPadOS 17+, all four tiers available) | 1,071 MB, 0 errors |
| **UI layout verified at iPad aspect** (menu, settings, devices, touch HUD, pause) | `-masarUiCapture`, six layout bugs fixed |
| Phase 0 report page | https://claude.ai/artifact/HqkUAUW13RHuMrH6qP5ycx (private to the owner) |
| Realism pass (2026-11-19/20): scanned roads, HDRI lighting, geometric date palms, facade weathering, MSAA/STP fix | Review screenshots, PERFORMANCE_REPORT.md |
| **Car selector (2026-11-20):** 5 cars (test sedan + 4 imported Sketchfab models: Jetta GLI, Elantra N, Aventador SVJ, Aventador Mansory). Main-menu and pause-menu selector with rendered thumbnails and license credits. A generic importer finds wheels, steering, lamps and colliders, and measures wheelbase, track and tire radius | `ImportedCarBuilder.cs`. Imported cars are for **private testing only** (ASSET_INVENTORY V001–V004, KNOWN_ISSUES G1) |

| **Runs on the iPad Pro M5 (TestFlight build 11, 2026-11-21):** installs, launches, drives. Ultra at about 90 fps (owner's observation) | PERFORMANCE_REPORT.md (device numbers still to capture) |
| **Wheel bridge (2026-11-21):** G923 → Windows PC → Wi-Fi → iPad, with force feedback from the simulation. Loopback test: 250 Hz, 0 lost, 3–4 ms round trip | BUILD_INSTRUCTIONS §5a, `WheelBridgeSelfTest` |
| **Wheel bridge on the real G923 (2026-11-21):** the first owner test showed only Cross and Square working (wheel in PlayStation compatibility mode). Bridge rebuilt on DirectInput + G HUB's PC mode: steering read in the background, force feedback measured in both directions (+148° / −265° at 25 %), pedals 0 at rest | `MasarWheelBridge --test-ffb`, `--probe` |

## In progress (Month 2)

- **Tahlia / Al Rawdah map** from OpenStreetMap, with AI traffic and pedestrians (placeholder bodies): built and compiled, under review (screenshots, PC perf run, then TestFlight).
- **Max graphics tier** (above Ultra): to be measured on the iPad in the city.
- Waiting on the owner: Mixamo characters for the pedestrians (BUILD_INSTRUCTIONS §5b).

## Blockers (owner action needed)

1. ~~Apple and GitHub secrets for TestFlight~~: done 2026-11-20 (`tools/setup.ps1`).
2. **Hardware tests:** G923 on the PC (desktop axis mapping) and on the iPad (probe app via TestFlight).

## Next tasks

1. Secrets → TestFlight the probe → G923 result on the iPad.
2. TestFlight the game → performance numbers on the device (overlay + CSV) → Gate 0 review.
3. Month 2 planning, after approval.
