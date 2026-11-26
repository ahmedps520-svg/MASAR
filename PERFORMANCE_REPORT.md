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

#### Current: after the realism pass, facade weathering and the MSAA fix (2026-11-20, 2420×1668)

Scene: scanned Poly Haven asphalt with detail maps, tire-wear overlays, worn paint, geometric date palms (6 variants × 3 LODs), HDRI sky, facade weathering overlays. MSAA off on every tier (KNOWN_ISSUES T16). STP upscaling on High and Ultra only.

| Tier | Render scale | Upscaler | Avg FPS (uncapped) | CPU main | GPU | Draw calls | Triangles | Unity alloc |
|---|---|---|---|---|---|---|---|---|
| Low | 0.60 | bilinear | 698 | 0.80 ms | **0.94 ms** | 416 | 0.42 M | 267 MB |
| Medium | 0.72 | bilinear | 537 | 1.04 ms | **1.48 ms** | 489 | 0.73 M | 260 MB |
| High | 0.82 | STP | 239 | 1.33 ms | **4.06 ms** | 905 | 1.56 M | 269 MB |
| Ultra | 1.00 | STP | 173 | 1.62 ms | **5.84 ms** | 1,166 | 2.14 M | 277 MB |

**Feature cost A/B at High** (`perf-tiers.ps1 -Tiers High -Off <feature>`, compared with a baseline from the same session; the first session's baseline was 4.56 ms, and run-to-run noise is about ±0.3 ms):

| Switched off | GPU | Saving | Verdict |
|---|---|---|---|
| STP upscaling | 3.33 ms | **≈1.2 ms** | The most expensive single feature. Removed from Medium as a result (Medium went 4.64 → 1.70 ms) |
| SSAO | 3.52 ms | **≈1.0 ms** | Second most expensive. High/Ultra only |
| Geometric palms | 4.54 ms | within noise | −0.6 M triangles and −150 draw calls, but no measurable GPU cost on PC. Watch the triangle cost on the iPad's TBDR GPU |
| Tire-wear overlays | 4.79 ms | within noise | Keep |
| Facade weathering overlays (separate session: 4.09 ms off vs 4.06 / 4.12 ms on) | 4.09 ms | within noise | Keep. Costs about 15–18 draw calls at High (one per nearby building) |
| Detail maps | 4.74 ms | within noise | Keep |
| HDRI sky | 4.81 ms | within noise | Keep |

**Per car at High** (2026-11-21, `perf-tiers.ps1 -Tiers High -Car <id>`, same session, same scripted drive):

| Car | Model triangles | Car draw calls | GPU | CPU main | Scene draws | Scene triangles | Unity alloc |
|---|---|---|---|---|---|---|---|
| Test sedan (procedural) | ≈60 k | ≈20 | 3.33 ms | 1.34 ms | 904 | 1.58 M | 302 MB |
| Jetta GLI (full interior) | 385 k | 65 | 3.68 ms | 1.40 ms | 1,002 | 2.79 M | 337 MB |
| Elantra N | 258 k | 40 | 3.45 ms | 1.41 ms | 888 | 2.11 M | 313 MB |
| Aventador SVJ | 32 k | 21 | 3.44 ms | 1.32 ms | 616 | 1.12 M | 302 MB |
| Aventador Mansory | 975 k | 141 | **4.02 ms** | 1.22 ms | 848 | 4.62 M | 352 MB |

On the PC every car costs at most about 0.7 ms of GPU time. Scene triangles count the car about three times: the camera, the shadow cascades and the depth prepass. The Mansory is the one to watch on the iPad: about 1 M triangles drawn several times per frame. Only the chosen car is loaded (catalog entries are Resources paths), so memory grows with the chosen car, not with the catalog. Draw-call differences between runs also reflect where the car ends up after 12 s (LOD and occlusion of the city).

#### Bigger Tahlia map, streamed (2026-11-23, `perf-tiers.ps1 -Map Tahlia -Seconds 30`)

The 4.4 × 2.6 km map (Tahlia to Madinah Road), with each 512 m block's buildings streamed as its own scene. Same 2420×1668 scripted drive, from the new spawn in the middle of Tahlia Street, traffic and pedestrians at Normal. PC: RTX 4060 Ti, Direct3D 12.

| Tier | Avg FPS | CPU main | GPU | Draw calls | Triangles | Unity alloc |
|---|---|---|---|---|---|---|
| Low | 195 | 4.5 ms | 1.8 ms | 2,094 | 2.9 M | 1,005 MB |
| Medium | 129 | 7.5 ms | 2.6 ms | 3,316 | 6.4 M | 1,022 MB |
| High | 116 | 7.9 ms | 4.2 ms | 3,270 | 6.9 M | 1,044 MB |
| Ultra | 115 | 7.9 ms | 5.4 ms | 3,417 | 7.6 M | 1,038 MB |
| Max | 108 | 8.3 ms | 5.5 ms | 3,751 | 10.5 M | 1,049 MB |

Findings:
- **Memory: about 1.0 GB at every tier, against 2.1 GB for the smaller map loaded whole.** 8 of 60 building blocks are loaded at the start, in 2–3 s (up to 10 s on a cold disk cache). From the menu, the map scene loads in 1.8 s and the drive starts 4.2 s after the load begins.
- **Build size: 3.4 GB of data, largest file 364 MB** (was 5.5 GB with one 4.5 GB file, which crashed on load). City vertices are 28 bytes (64 before).
- **Parked cars cost 95 ms per frame of CPU** before the fix (A/B, High): 10.7 fps with them, 120 fps without. They still had the vehicle script and rigidbody; they are now static copies.
- Draw calls are high (3,300 at High) and CPU main is about 8 ms, now the larger of CPU and GPU. Most is the city's per-material renderers; not optimized yet.
- Not measured yet: hitches when a block loads while driving fast, and the iPad.

#### Tahlia / Al Rawdah map (2026-11-22, OSM city, `perf-tiers.ps1 -Map Tahlia -Seconds 15`)

The same 2420×1668 scripted drive, starting on Tahlia Street at 60 % throttle (65 km/h reached), with traffic and pedestrians at Normal: 34 AI cars and about 30 Mixamo pedestrians live. These runs overlapped an iOS export on the same PC; repeated baselines agree within ±0.3 ms GPU.

| Run | Avg FPS | CPU main | GPU | Draw calls | Triangles | Unity alloc |
|---|---|---|---|---|---|---|
| High (baseline, 2 runs) | 117 | 3.4–3.5 ms | **8.1–8.6 ms** | 790–795 | 3.1 M | 2,085 MB |
| High, SSAO off | 156 | 3.1 ms | **6.3 ms** | 546 | 2.2 M | 2,084 MB |
| High, facade grime off | 117 | 3.3 ms | 8.4 ms | 771 | 3.1 M | 2,084 MB |
| High, palms off | 115 | 3.3 ms | 8.5 ms | 796 | 3.1 M | 2,085 MB |
| Ultra (first run, shaders cold) | 69 | 6.7 ms | 14.0 ms | 964 | 4.0 M | 2,084 MB |

Findings:
- **GPU:** the city costs about twice the test block at High (8.3 ms against 4.1 ms).
- **SSAO** is the largest single cost, about 2 ms. Its depth-normals prepass draws the city again (+250 draw calls, +0.9 M triangles).
- **Memory: 2.1 GB, against about 300 MB in the test block.** Causes:
  - 10.3 M triangles of near-detail building meshes, all loaded at once (no streaming yet).
  - A CPU copy of every generated mesh. Fixed after this run: render-only meshes are now non-readable, which needs a rebuild to confirm.
  - The Mixamo characters.
- **Max tier:** not in this player build (the tier setup now runs in the build commands); to be measured.

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

#### Benchmark drive on the lane autopilot (2026-11-26, `-masarSmokeTest -masarViaMenu -masarAutopilot`, High, 1600×1100, 60 fps cap)

The car drives itself through Tahlia (up to 80 km/h, stops at red lights) while every frame is timed. Same route each run.

| Run | Seconds | Avg fps | Median / 99 % / 99.9 % frame | Worst | Frames > 50 ms | Distance, avg / max speed | Collisions | Max lane error | AI lane changes / overlaps |
|---|---|---|---|---|---|---|---|---|---|
| Day, test sedan | 177 | 59.9 | 16.67 / 16.67 / 17.06 ms | 145 ms | 1 | 1.86 km, 37 / 78 km/h | 0 | 1.90 m (no seconds off lane) | 4 / 0 |
| Night, test sedan (first lighting) | 117 | 59.8 | 16.67 / 16.96 / 22.08 ms | 70 ms | 4 | 1.27 km, 38 / 78 km/h | 0 | 1.90 m | 4 / 0 |
| Sunset, test sedan | 57 | 60.0 | 16.67 / 16.86 / 20.27 ms | 24 ms | 0 | 0.53 km, 32 / 78 km/h | 0 | 0.19 m | 2 / 0 |
| Day, Aventador SVJ | 57 | 59.9 | 16.67 / 17.35 / 18.84 ms | 66 ms | 1 | 0.74 km, 44 / 80 km/h | 0 | 0.15 m | 4 / 1 |

On the PC the streamed city holds the 60 fps cap with at most a handful of frames over 50 ms in 3 minutes (block loads). The iPad numbers come from the same drive: **pause menu > Benchmark drive** (the report is saved in Files > MASAR > benchmarks).

### iPad Pro M5

| Date | Build | Tier | Scene | FPS avg / 1% | CPU main | GPU | Footprint / available | Thermal after 10 min | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 2026-11-22 | TestFlight build 19 | — | Tahlia + landscaping, occlusion off | to be measured by the owner | | | | | Uploaded 2026-11-22 (commit 2297ffe) |
| 2026-11-22 | TestFlight build 16 | Max / High / Medium | **Tahlia / Al Rawdah** with traffic and pedestrians | Max ≈ 60 fps ("smooth"), High ≈ 90 fps, Medium ≈ 70 fps (owner's observation; Medium below High is unexplained, maybe the 60/120 frame cap or the moment it was read) | not captured | not captured | not captured | not captured | First city run on the device. AI turns looked wrong and Mixamo pedestrians were white and wrongly sized (both fixed in build 19) |
| 2026-11-21 | TestFlight build 11 | Ultra | Test block, free drive | **≈90 fps** (owner's observation: "smooth") | not captured | not captured | not captured | not captured | First run on the device. Frame-cap setting not recorded. Overlay/CSV numbers still needed (diagnostics upload not configured yet) |

## Physics regression results

### EditMode: isolated vehicle model (straight-line harness, flat dry road)

Unity 6000.3.28f1, first run 2026-11-14 on the development PC. **Run 2026-11-26: 16/16 passed** (the Month 3 tests below added).

| Test | Result | Reference range |
|---|---|---|
| 0–100 km/h (test sedan, full throttle, automatic) | **9.47 s** | 6.5–13 s (class reference about 8–10 s) |
| Speed after 40 s at full throttle | 204 km/h, 5th gear, 5,697 rpm | — |
| 100–0 km/h braking, ABS | **39.0 m in 2.83 s** | 30–55 m (class reference about 35–45 m) |
| Idle in Park, 10 s | **750 rpm** (693 before 2026-11-26), no motion | idle ±25 rpm since the governor got an integral term (T8) |
| Braked (50 %) on a 10 % slope, uphill and downhill, 10 s | **0.00 cm** moved | < 1 cm (static friction at standstill, T3) |
| Brake released on a 10 % slope, 3 s | rolled back 3.77 m | must roll (static friction lets go) |
| Engine sound pitch, 4 cyl at 3,000 / 6,000 rpm, 12 cyl at 1,200 / 3,000 rpm | 100.0 / 200.0 / 120.0 / 300.0 Hz | firing frequency rpm × cylinders / 120, ±5 % |
| Tyre sound while gripping / sliding | rms 0.00000 / 0.066 | silent while gripping |
| Sound output at redline, full load, sliding, 80 m/s | peak 0.35, no NaN | within ±1 |
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
