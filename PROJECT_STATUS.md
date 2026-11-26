# MASAR: Project Status

**Phase:** Month 3 (planned and started 2026-11-26) · **Last updated:** 2026-11-26 · **Gate 0:** the iPad runs it (TestFlight); device performance numbers still to capture

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
| **Main menu, intro and logo (2026-11-23):** the owner's logo as app icon, iPad launch screen and intro; menu over a 3D showroom of the selected car; loading screen with real progress | `-masarUiCapture` screens 00–10 |
| **Bigger city streams (2026-11-23):** 4.4 × 2.6 km map, buildings in 60 block scenes loaded around the camera; build 3.4 GB (was 5.5 GB and crashed on load); about 1.0 GB in memory; High 116 fps in the middle of the map on the PC | PERFORMANCE_REPORT.md |
| **TestFlight build 21 (2026-11-24):** the streamed bigger city, loading screen, new menu, intro and logo. Processed and available in TestFlight. Signing had hit Apple's certificate limit; each CI run now revokes the certificates it creates (KNOWN_ISSUES T18) | Actions run 45356612623 |
| **TestFlight build 27 (2026-11-24):** fixes the car stuck at the city spawn (an invisible 211 m building collision box on Tahlia Street; outline buildings now collide along their outline, KNOWN_ISSUES C13). On the PC three of the five cars were tested and drive off from the spawn | Actions run 45432443677 |
| **Buildings kept off the roads (2026-11-24):** 58 visible buildings stood on lanes (OSM outlines inside our road width, campus buildings, construction frames); they now step back from the carriageway (KNOWN_ISSUES C13). Lane samples inside a building: 278 → 1. All five cars drive off the city spawn on the PC. Shipped as TestFlight build 30 | Actions run 45478882095 |
| **Month 3 (2026-11-26):** lane autopilot + Benchmark drive (pause menu), synthesised engine/tyre/wind sound, day/sunset/night, static friction and idle fixes, AI lane changes with indicators. PC: 16/16 tests, 3-minute autopilot drive at 60 fps with 0 collisions | ROADMAP Month 3, PERFORMANCE_REPORT |

## In progress (Month 3)

- **Benchmark drive on the iPad:** the owner runs it once (pause menu, city map); the numbers go into PERFORMANCE_REPORT.md and close the open Gate 0 measurement.
- Listen to the engine sound and look at sunset and night on the iPad.
- One AI–AI overlap was counted in four PC runs: find the cause.

## Earlier (Month 2)

- **Tahlia / Al Rawdah map** from OpenStreetMap, with AI traffic and pedestrians (placeholder bodies): built and compiled, under review (screenshots, PC perf run, then TestFlight).
- **Max graphics tier** (above Ultra): to be measured on the iPad in the city.
- Waiting on the owner: Mixamo characters for the pedestrians (BUILD_INSTRUCTIONS §5b).

## Blockers (owner action needed)

1. ~~Apple and GitHub secrets for TestFlight~~: done 2026-11-20 (`tools/setup.ps1`).
2. **Hardware tests:** G923 on the PC (desktop axis mapping) and on the iPad (probe app via TestFlight).

## Next tasks

1. Secrets → TestFlight the probe → G923 result on the iPad.
2. TestFlight the game → performance numbers on the device (overlay + CSV) → Gate 0 review.
3. Month 3: device benchmark numbers, owner review of sound and night, then Month 4 (weather, driver profiles, rules research). Arabic UI waits for the owner's OK to download an Arabic font.
