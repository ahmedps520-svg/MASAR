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

PC: i5-14600K, RTX 4060 Ti, 32 GB, Windows 11, Direct3D 12. Windows player build, `-masarSmokeTest -masarUncapped`: a 12 s scripted drive down the avenue at 60% throttle (chase camera), with the first 2 s excluded. Measured with `tools/perf-tiers.ps1`.

#### Current: after the realism pass and the MSAA fix (2026-11-20, 2420×1668)

Scene: scanned Poly Haven asphalt with detail maps, tire-wear overlays, worn paint, geometric date palms (6 variants × 3 LODs), HDRI sky. MSAA off on every tier (KNOWN_ISSUES T16). STP upscaling on High and Ultra only.

| Tier | Render scale | Upscaler | Avg FPS (uncapped) | CPU main | GPU | Draw calls | Triangles | Unity alloc |
|---|---|---|---|---|---|---|---|---|
| Low | 0.60 | bilinear | 581 | 0.95 ms | **0.82 ms** | 398 | 0.39 M | 266 MB |
| Medium | 0.72 | bilinear | 451 | 1.12 ms | **1.70 ms** | 473 | 0.72 M | 259 MB |
| High | 0.82 | STP | 207 | 1.67 ms | **4.81 ms** | 890 | 1.39 M | 280 MB |
| Ultra | 1.00 | STP | 145 | 1.69 ms | **6.29 ms** | 1,154 | 2.13 M | 272 MB |

**Feature cost A/B at High** (same session, `perf-tiers.ps1 -Tiers High -Off <feature>`; the baseline in that session was 4.56 ms, and run-to-run noise is about ±0.3 ms):

| Switched off | GPU | Saving | Verdict |
|---|---|---|---|
| STP upscaling | 3.33 ms | **≈1.2 ms** | The most expensive single feature. Removed from Medium as a result (Medium went 4.64 → 1.70 ms) |
| SSAO | 3.52 ms | **≈1.0 ms** | Second most expensive. High/Ultra only |
| Geometric palms | 4.54 ms | within noise | −0.6 M triangles and −150 draw calls, but no measurable GPU cost on PC. Watch the triangle cost on the iPad's TBDR GPU |
| Tire-wear overlays | 4.79 ms | within noise | Keep |
| Detail maps | 4.74 ms | within noise | Keep |
| HDRI sky | 4.81 ms | within noise | Keep |

#### Before the realism pass (2026-11-15, 2420×1440, MSAA 2x on every tier by mistake, so STP never ran)

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
| Render scale + STP upscaling per tier | Configured (STP on High/Ultra) | Render scale scales GPU time (tables above). STP costs ≈1.2 ms on the PC. Its cost and value on the iPad must be checked on the device |
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
