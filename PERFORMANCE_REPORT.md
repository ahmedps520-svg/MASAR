# MASAR: Performance Report

**Rule:** only measured numbers go here, with the device, build, settings and date. Anything the OS doesn't expose (for example GPU temperature on iPadOS) is recorded as "not available", never estimated.

## Target device

| Device | Status |
|---|---|
| iPad Pro 11-inch (M5, 256 GB, 12 GB RAM), iPadOS 26.x | **Not yet measured**: waiting for the first TestFlight build |

## Budgets (targets to validate, not measurements)

See TECHNICAL_ARCHITECTURE.md §13: a 16.6 ms frame (60 fps) with a 25 ms fallback, ≤ 3 GB footprint at first, a 30-minute soak at High without `serious` thermal state.

## How to measure

1. In game: **F3**, the gamepad D-pad up, or Settings → Performance overlay. It shows FPS, frame/worst time, CPU main/render ms, GPU ms (`FrameTimingManager`), memory footprint and headroom (iOS native plugin), thermal state (iOS), draw calls, batches and triangles.
2. Settings → **Start CSV performance recording**. The file appears in the app's Documents folder, reachable through the Files app on the iPad. Send it back for this report.
3. Development builds: Unity Profiler over Wi-Fi.

## Measurements

### Development PC (reference only, NOT the target)

PC: i5-14600K, RTX 4060 Ti, 32 GB, Windows 11, Direct3D 12. Windows player build, `-masarSmokeTest -masarUncapped`: a 12 s scripted drive down the avenue at 60% throttle (chase camera), with the first 2 s excluded. Window 2420×1440 (the native iPad height of 1668 is clamped by the PC monitor). Measured 2026-11-15 with `tools/perf-tiers.ps1`.

| Tier | Render scale | Avg FPS (uncapped) | CPU main | GPU | Draw calls | Triangles | Unity alloc |
|---|---|---|---|---|---|---|---|
| Low | 0.60 | 599 | 1.09 ms | **0.68 ms** | 380 | 0.54 M | 230 MB |
| Medium | 0.72 | 533 | 1.51 ms | **0.91 ms** | 468 | 0.86 M | 234 MB |
| High | 0.82 | 414 | 1.81 ms | **1.74 ms** | 869 | 1.53 M | 225 MB |
| Ultra | 1.00 | 358 | 1.88 ms | **2.49 ms** | 1,262 | 2.12 M | 222 MB |

What this proves and what it doesn't:
- ✅ **Each tier changes the real cost.** GPU time rises 3.7× and draw calls 3.3× from Low to Ultra. Draw calls and triangles change because of the per-building LOD groups responding to each tier's LOD bias (0.6 / 1.0 / 1.5 / 2.0) and the shadow cascade count (1 / 2 / 3 / 4).
- ✅ The full stack runs in a player build: scripted drive, car upright, HUD and overlay working.
- ❌ **These numbers say nothing about the iPad.** The RTX 4060 Ti is roughly an order of magnitude faster than the M5's 10-core GPU in raw throughput, with a different architecture (tile-based deferred rendering) and different thermals. The tier defaults (High on iPad) are placeholders until device measurements exist.
- Earlier run (before LOD/occlusion/polish), Ultra at 1920×1320, frame rate capped: 60 fps, CPU 1.68 ms, GPU 4.10 ms, 1,359 draw calls, 1.99 M triangles.

### Optimization features verified in Phase 0

| Feature | Status | Evidence |
|---|---|---|
| Per-building LOD (details culled at distance, then whole building culled) | Working | Draw calls and triangles scale with each tier's LOD bias (table above) |
| Umbra occlusion culling (baked) | Baked: 457 KB of data, 5 s bake | Effect not yet isolated in a measurement (to do on the device) |
| Render scale + STP upscaling per tier | Configured | Render scale scales GPU time (table above). STP on iPad must be checked on the device |
| GPU Resident Drawer | Enabled Medium+ | Not yet verified as active (draw-call counts don't show instancing gains, because every building mesh is unique) |
| Mipmap streaming budgets per tier | Configured | Not yet measured |

### iPad Pro M5

| Date | Build | Tier | Scene | FPS avg / 1% | CPU main | GPU | Footprint / available | Thermal after 10 min | Notes |
|---|---|---|---|---|---|---|---|---|---|
| — | — | — | — | — | — | — | — | — | Pending: first TestFlight build |

## Physics regression results

### EditMode: isolated vehicle model (straight-line harness, flat dry road)

Unity 6000.3.28f1, run 2026-11-14 on the development PC. **6/6 passed.**

| Test | Result | Reference range |
|---|---|---|
| 0–100 km/h (test sedan, full throttle, automatic) | **9.47 s** | 6.5–13 s (class reference about 8–10 s) |
| Speed after 40 s at full throttle | 204 km/h, 5th gear, 5,697 rpm | — |
| 100–0 km/h braking, ABS | **39.0 m in 2.83 s** | 30–55 m (class reference about 35–45 m) |
| Idle in Park, 10 s | 693 rpm, no motion | idle ±200 rpm. Target idle is 750; the governor settles slightly low (tuning item) |
| Coasting stability at 45 m/s, 5 s | wheel slip < 1 m/s throughout | — |
| Tire force never exceeds μ·Fz (full slip sweep) | pass | — |

### PlayMode: full stack in the TestBlock scene (PhysX, road meshes, kerbs)

Run in the Unity editor on the development PC. **1/1 passed** on both runs. The second run was on the final polished scene, with bollards, manholes and patches.

| Phase | Run 1 (2026-11-14) | Run 2 (2026-11-15, final scene) |
|---|---|---|
| Settle on suspension | upright, 4 wheels grounded | upright, 4 wheels grounded |
| 0–60 km/h, full throttle | **5.43 s** | **5.65 s** |
| Speed after 8 s | 88 km/h (2nd, 6,045 rpm) | 87 km/h (2nd, 5,981 rpm) |
| Lateral drift, neutral steering | 0.00 m | 0.00 m |
| Full braking | **32.5 m from 88 km/h** (ideal μ = 1: 30.5 m) | **31.8 m from 87 km/h** |
| Left turn, steer −0.6, throttle 0.3, 3.5 s | −52° at 12 km/h | −52° at 12 km/h |
| Minimum upright (up.y) | 0.999 | 0.999 |

Observation: at full throttle the automatic holds each gear close to the 6,000 rpm shift point (1st to about 59 km/h, 2nd to about 92 km/h), which is realistic for wide-open throttle. Part-throttle shift points are lower (2,300 rpm at light throttle).

## Bottlenecks found

None yet: nothing has been measured on the device.
