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

### Development PC (reference only, not the target)

| Date | Build | Settings | Scene | FPS | CPU main | GPU | Memory | Notes |
|---|---|---|---|---|---|---|---|---|
| — | — | — | — | — | — | — | — | Pending: Unity not yet installed |

PC: i5-14600K, RTX 4060 Ti, 32 GB, Windows 11.

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

Run 2026-11-14 in the Unity editor on the development PC. **1/1 passed.**

| Phase | Result |
|---|---|
| Settle on suspension | upright, 4 wheels grounded |
| 0–60 km/h, full throttle | **5.43 s** |
| Speed after 8 s | 88 km/h (2nd gear, 6,045 rpm), lateral drift 0.00 m with neutral steering |
| Full braking from 88 km/h | **32.5 m in 2.65 s** (an ideal μ = 1 stop is 30.5 m) |
| Left turn, steer −0.6, throttle 0.3, 3.5 s | heading −52° at 12 km/h |
| Minimum upright (up.y) over the whole run | 0.999 |

Observation: at full throttle the automatic holds each gear close to the 6,000 rpm shift point (1st to about 59 km/h, 2nd to about 92 km/h), which is realistic for wide-open throttle. Part-throttle shift points are lower (2,300 rpm at light throttle).

## Bottlenecks found

None yet: nothing has been measured on the device.
