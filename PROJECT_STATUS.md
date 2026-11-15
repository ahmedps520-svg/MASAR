# MASAR: Project Status

**Phase:** 0, technical foundation · **Last updated:** 2026-11-14 · **Gate 0:** not yet passed (needs the iPad run)

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

## In progress

Nothing. Phase 0 work that doesn't need the iPad is complete.

## Blockers (owner action needed)

1. **Apple and GitHub secrets for TestFlight:** bundle IDs, app records, App Store Connect API key, five repository secrets (BUILD_INSTRUCTIONS §4).
2. **Hardware tests:** G923 on the PC (desktop axis mapping) and on the iPad (probe app via TestFlight).

## Next tasks

1. Secrets → TestFlight the probe → G923 result on the iPad.
2. TestFlight the game → performance numbers on the device (overlay + CSV) → Gate 0 review.
3. Month 2 planning, after approval.
