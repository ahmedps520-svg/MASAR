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

## Physics regression results (EditMode tests)

| Date | Test | Result | Reference range |
|---|---|---|---|
| — | 0–100 km/h (test sedan, full throttle) | pending | 6.5–13 s (class reference about 8–10 s) |
| — | 100–0 km/h braking, ABS | pending | 30–55 m (class reference about 35–45 m) |
| — | Idle in Park, 10 s | pending | idle ±200 rpm, no motion |
| — | Coasting stability at 45 m/s | pending | wheel slip below 1 m/s |

## Bottlenecks found

None yet: nothing has been measured on the device.
