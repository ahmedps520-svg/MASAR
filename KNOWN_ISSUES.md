# MASAR: Known Issues, Limitations and Unimplemented Systems

Status labels: **Working**: implemented and tested. **Prototype**: implemented, limited testing. **Placeholder**: stand-in content. **Planned**: not implemented. **Blocked**: cannot proceed without an external change.

## Blockers and platform limitations

| # | Issue | Status | Detail |
|---|---|---|---|
| L1 | **Logitech G923 can't be read on iPadOS through public game APIs** | Blocked (platform) | `GCRacingWheel` is macOS/Mac Catalyst-only, and generic HID devices are filtered out on iPadOS. Sources in `docs/research/PHASE0_RESEARCH.md` §2. The probe app will confirm this on the device. Alternatives: the planned wheel bridge (PC or Linux board → network → iPad), supported gamepads, touch controls |
| L2 | **No genuine force feedback on iPad** | Blocked (platform) | iPadOS has no public FFB API. The game shows **no** FFB settings anywhere. A DriverKit (USBDriverKit) research spike needs the paid account and is unproven: Apple's HID driver may claim the wheel first |
| L3 | No Mac | Limitation | No Xcode debugger or Instruments. Diagnostics come from the in-game overlay, CSV logs, the Unity Profiler over Wi-Fi (development builds) and TestFlight crash reports |
| L4 | The engine must be installed by the user | Resolved when done | Installing the Unity editor needs a Windows admin prompt and a Unity account sign-in (BUILD_INSTRUCTIONS §1) |
| L5 | Apple secrets not yet configured | Blocked on user | TestFlight workflows fail fast with a clear message until the §4 secrets exist |

## Phase 0 placeholders (not final quality)

| # | Item | Status | Plan |
|---|---|---|---|
| P1 | Test car is a procedural loft: generic, unbranded, simplified interior | Placeholder | Month 2: Blender-modelled hero vehicle with a cockpit-grade interior |
| P2 | Buildings are procedurally generated from a modular facade kit | Prototype | Month 3: Blender-authored modular kits + landmarks. The procedural kit stays for background fill |
| P3 | The test block is a hand-designed layout, not a real Jeddah location | Placeholder | Month 3: OSM-derived road network for the chosen neighborhood |
| P4 | Traffic-signal heads cycle on a timer | Placeholder (visual prop) | Month 3/4: real signal controllers bound to the road graph. Nothing reads these props |
| P5 | Shop signs are blank colored panels | Placeholder | Deliberate: no invented brands. Real signage art later |
| P6 | Palms use solid-color fronds (no leaf texture) | Placeholder | Month 3 vegetation pass |
| P7 | Lighting is a single afternoon setup: sun, procedural sky, fog, baked reflection probes. No lightmaps or APV | Prototype | Month 2: APV lighting scenarios, time of day |

## Not implemented (planned)

Traffic AI, pedestrians, audio, damage, weather, day/night cycle, driving school, theory test, practical exam, traffic rules and enforcement, vehicle selection, OSM import, city streaming (cells), LOD/HLOD for the city, occlusion-culling bake, Arabic localization and RTL UI, wheel-bridge app, wheel calibration UI (the data model exists in `InputCalibration`).

## Known technical issues

| # | Issue | Detail |
|---|---|---|
| T1 | **G923 pedal axis mapping on Windows is unverified** | `WheelMapping` defaults (steer `stick/x`, pedals `z`/`rz`/`slider`) are guesses, because the wheel was not connected during Phase 0. Use **Input devices & wheel test** to read the real axes, then update the mapping |
| T2 | The UI font is Latin-only | The built-in font cannot shape Arabic. Arabic shaping and RTL need a font and shaping solution (Month 2 risk item R6) |
| T3 | The vehicle model's static hold at standstill is a viscous approximation | On slopes the car can creep slowly while braking. Fine on the flat test block. Month 2 adds proper static friction |
| T4 | Tire relaxation at very low speed uses a speed floor (0.6 m/s) | Prevents jitter, but slightly softens parking-speed response. Revisit in Month 2 |
| T5 | Glass is transparent (sorted). Cabin glass is visible from inside, but the interior has no reflections | Acceptable for Phase 0 |
| T6 | "Visible renderers" in the overlay counts `Renderer.isVisible` | Includes objects rendered only for shadows (Unity's definition) |
| T7 | Generated meshes and scenes depend on running the builders | After a fresh clone, run **MASAR → Setup → Configure Project** and **MASAR → Build → Test Block Scene** (or the command-line method). Generated assets may be committed for convenience; the builders are the source of truth |

## Licensing and legal

| # | Item | Status |
|---|---|---|
| G1 | Real vehicle brands, models and logos | Not used. Apple guideline 5.2.1 and trademark law require licenses. Generic designs only |
| G2 | OpenStreetMap data | Not yet used. When used: on-screen and credits attribution "© OpenStreetMap contributors", plus ODbL share-alike for any distributed derivative database |
| G3 | Saudi traffic rules, fines and exam procedures | Not yet modelled. Must come from official sources (Ministry of Interior / Moroor, Absher, official driving-school material), with every figure cited |
