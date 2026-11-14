# MASAR: Technical Architecture

Status: **Phase 0 architecture. Engine choice confirmed by the owner (2026-11-14): Unity 6000.3.28f1 LTS + URP.** The evidence behind every decision is in `docs/research/PHASE0_RESEARCH.md`.

Labels used in this document:
- **[P0]**: built in Phase 0.
- **[Planned]**: designed now, built later.
- **[Risk]**: depends on an assumption that has not yet been proven.

---

## 1. Decision summary

| Area | Decision | Why |
|---|---|---|
| Engine | **Unity 6.3 LTS (6000.3.x), URP**. Move to the next LTS only at a milestone boundary | It is the only engine with a documented Windows editor → iPad path. Mature Metal backend. Custom physics in C#. Unreal's Lumen, Nanite and VSM don't run on iOS, and Unreal's C++ iOS builds need a Mac |
| Language | C# (Burst/Jobs for hot loops), plus small Objective-C/Swift iOS plugins | Unity standard. Burst gives near-native speed for vehicles and traffic |
| Physics | **Custom vehicle dynamics** (tire, suspension, drivetrain) on top of PhysX rigid bodies | WheelCollider is too limited. A custom model makes vehicles feel genuinely different |
| Rendering | URP **Forward+**, GPU Resident Drawer, GPU occlusion culling, STP upscaling, APV + baked reflection probes | Best quality-to-cost ratio on Apple GPUs |
| Input | Abstract `DriverInput` layer. Providers: keyboard, gamepad, touch, Windows wheel, **network wheel bridge** | The G923 can't be read on iPadOS through public game APIs (see research §2) |
| Build | Windows editor → Xcode project → **cloud macOS** (GitHub Actions or Unity Build Automation) → TestFlight | No Mac available |
| Data | Vehicles, rules, maps and settings are **data files** (JSON/ScriptableObjects), never hardcoded | Brief rule 5 |

---

## 2. Repository layout

```
E:\MASAR\                       ← git repo (moved off OneDrive on 2026-11-14)
├─ README.md
├─ PROJECT_STATUS.md  ROADMAP.md  TECHNICAL_ARCHITECTURE.md
├─ ASSET_INVENTORY.md PERFORMANCE_REPORT.md KNOWN_ISSUES.md BUILD_INSTRUCTIONS.md
├─ docs/
│   └─ research/                 ← Phase 0 research, environment inventory
├─ game/                         ← Unity project (created once the engine is approved)
│   ├─ Assets/Masar/
│   │   ├─ Code/
│   │   │   ├─ Core/             Masar.Core.asmdef        (bootstrap, services, settings, events)
│   │   │   ├─ Input/            Masar.Input.asmdef       (DriverInput, providers, calibration)
│   │   │   ├─ Vehicles/         Masar.Vehicles.asmdef    (simulation, no rendering code)
│   │   │   ├─ Vehicles.View/    Masar.Vehicles.View      (wheel meshes, lights, gauges)
│   │   │   ├─ Cameras/          Masar.Cameras.asmdef
│   │   │   ├─ World/            Masar.World.asmdef       (cells, streaming, roads)
│   │   │   ├─ Diagnostics/      Masar.Diagnostics.asmdef (perf monitor, iOS native metrics)
│   │   │   ├─ Game/             Masar.Game.asmdef        (composition root, settings, session, UI)
│   │   │   └─ Editor/           Masar.Editor.asmdef      (generators, project setup, build scripts)
│   │   ├─ Plugins/iOS/          MasarNative.mm (thermal state, available memory, footprint)
│   │   ├─ Tests/                EditMode + PlayMode tests (physics regression)
│   │   ├─ Data/                 VehicleDefinitions, RuleSets, Settings defaults
│   │   ├─ Art/                  Materials, Textures, Meshes, Prefabs
│   │   └─ Scenes/               Boot, MainMenu, TestBlock
│   ├─ Packages/manifest.json
│   └─ ProjectSettings/
├─ tools/
│   ├─ probes/ios-input-probe/   ← native iPad diagnostic app [P0]
│   ├─ wheel-bridge/             ← PC/Linux G923 → iPad UDP bridge [Planned]
│   ├─ blender/                  ← Blender Python generators (vehicles, building kits)
│   └─ osm/                      ← OSM → road graph converter (Python)
├─ data/
│   ├─ rules/                    ← Saudi traffic-rule data with citations [Planned]
│   └─ vehicles/                 ← source vehicle specs (JSON)
└─ .github/workflows/            ← cloud macOS builds
```

Assembly definitions enforce the layering. `Vehicles` must not reference `UI`, `Cameras` or rendering. `Diagnostics` may read every layer, and no layer depends on `Diagnostics`.

---

## 3. Runtime architecture

```
            ┌────────────────────────── Boot scene ───────────────────────────┐
            │ GameServices: Settings, InputRouter, Diagnostics, SceneFlow      │
            └──────────────┬──────────────────────────────────┬──────────────┘
                           │                                  │
   InputProviders ──► InputRouter ──► DriverControls ──► VehicleController ──► Rigidbody (PhysX)
   (kbd, pad, touch,      (calibration,        (steer, throttle,   (VehicleSimulation:    │
    wheel, bridge)         deadzones)           brake, clutch…)     tires/susp/drivetrain)│
                                                        │                                 ▼
                                               VehicleTelemetry ──► Cameras, Audio, HUD, FFB sink
```

- **Update order.** Input is sampled in `Update`. It is consumed in `FixedUpdate`, where the vehicle runs at a fixed rate. Cameras run in `LateUpdate`, interpolated.
- **Services.** These are plain C# objects created by `GameBootstrap`. There are no static singletons beyond a single `GameServices` locator, which keeps unit testing possible.
- **Settings.** Settings are JSON in `Application.persistentDataPath`, versioned, covering graphics, controls, cameras and accessibility.

---

## 4. Rendering

### 4.1 Pipeline configuration (URP)

| Feature | Setting | Notes |
|---|---|---|
| Rendering path | **Forward+** | Needed for the GPU Resident Drawer. Many lights for the night city |
| Draw submission | **GPU Resident Drawer** + SRP Batcher | Large instanced city geometry |
| Culling | Frustum + **GPU occlusion culling** | CPU Umbra baking is the fallback |
| Resolution | Dynamic render scale 0.6–0.9 + **STP** upscaling | Native 2420×1668 at full cost is not sustainable |
| HDR / tonemapping | HDR, Neutral or ACES, exposure tuned per time of day | |
| Shadows | Main light: 2–4 cascades, 2048–4096 maps, soft. Additional lights: only a few shadowed | Cascade distance is tied to the draw-distance setting |
| Global illumination | **Adaptive Probe Volumes** (lighting scenarios for day/sunset/night) | No real-time GI on iPad |
| Reflections | Box-projected baked reflection probes. **One low-resolution, time-sliced real-time probe that follows the player car** | Car paint needs believable surroundings |
| Ambient occlusion | SSAO, half resolution (off on Low) | |
| Anti-aliasing | STP's temporal AA. FXAA on Low | MSAA conflicts with some passes |
| Post | Bloom (low), color grading LUT, motion blur off by default | |

### 4.2 Materials

| Surface | Approach |
|---|---|
| Car paint | URP **Complex Lit** with clear coat (base metallic flake + clear-coat layer) |
| Glass | Transparent Lit with reflection-probe contribution and a dirt mask |
| Asphalt / concrete / plaster | CC0 photo-scanned PBR sets (ambientCG / Poly Haven), ASTC compressed, detail-normal tiling, plus **macro variation** (world-space noise) to break repetition |
| Road markings | **Geometry plus a dedicated material** on the road mesh (crisp, cheap), not screen-space decals |

### 4.3 Level of detail and streaming

- **LOD groups** on every building and vehicle, with 3–4 levels.
- Distant city blocks use **HLOD proxies**: merged, simplified meshes with baked atlases.
- **Mipmap streaming** has a budget set per quality tier.
- **World cells** of 256 m × 256 m are additive scenes loaded through Addressables, in rings around the player:
  - Ring 0 is full detail.
  - Ring 1 has no small props.
  - Ring 2 is HLOD only.
- Traffic and pedestrians use the same distance tiers (see §8).

### 4.4 Quality tiers [Planned for the settings UI; the values are tuned on device]

Low, Medium, High, Ultra and Custom each map to a single `QualityProfile` asset. Each setting drives a real URP or engine parameter. **A setting is exposed only once a profiler capture shows it changes the cost.**

---

## 5. Vehicle physics [P0 foundation, refined in Month 2]

### 5.1 Structure

- `VehicleDefinition` is a ScriptableObject that can also be imported from `data/vehicles/*.json`. It holds:
  - Dimensions, mass, center-of-mass offset, inertia scale
  - Engine torque curve, idle/redline, engine inertia, engine braking
  - Gearbox type (automatic/manual), gear ratios, final drive, shift logic
  - Drivetrain (FWD/RWD/AWD), differential type, AWD split
  - Per-axle suspension (spring rate, damping bump/rebound, travel, anti-roll bar)
  - Tire (radius, width, Pacejka-style coefficients, load sensitivity, relaxation length)
  - Steering (ratio, max lock, Ackermann)
  - Brakes (torque, bias, ABS on/off)
  - Fuel capacity
  - Asset references (exterior/interior prefabs, audio profile)
- `VehicleSimulation` is **pure C#**, with no `MonoBehaviour` and no rendering. Its only inputs are `DriverControls`, wheel contacts and the chassis state. Its outputs are forces and torques plus a `VehicleTelemetry` struct. This makes it unit-testable and profilable on its own.
- `VehicleController` is a `MonoBehaviour` adapter. It runs the ray or sphere casts, calls the simulation, and applies forces to the Rigidbody.

### 5.2 Model

| Subsystem | Model |
|---|---|
| Suspension | Per-wheel ray/sphere cast. Spring + damper (separate bump/rebound) + bump stop + anti-roll bars. Gives weight transfer without fakes |
| Tire | Slip ratio and slip angle with **relaxation length** (stable at low speed). Simplified Pacejka-style curves per axis, a **friction ellipse** for combined slip, load sensitivity, and surface grip multipliers (dry/wet/sand) |
| Engine | RPM-indexed torque curve, rotational inertia, idle governor, rev limiter, engine braking |
| Transmission | Automatic: torque-converter slip + shift schedule. Manual: clutch modeled as a torque-capacity limit. Reverse and park |
| Differential | Open / limited-slip / locked. AWD with a center split |
| Brakes | Torque per wheel, front/rear bias, handbrake, optional ABS assist |
| Steering | Steering ratio + max lock + Ackermann. Outputs **self-aligning torque** for force feedback |
| Resistance | Aerodynamic drag, rolling resistance |
| Timestep | `Time.fixedDeltaTime = 1/120 s`, with internal sub-steps for drivetrain and tires (4× → 480 Hz). The cost is measured on the iPad |

### 5.3 Damage [Planned]

The stages, in order:
1. Rigid collisions, with impulse recorded per zone.
2. Detachable parts (bumpers, mirrors), broken lights and glass.
3. Mesh deformation driven by vertex displacement from impulses.
4. Mechanical effects: wheel alignment, radiator, suspension.

**Soft-body (BeamNG-style) simulation is a stretch research item and won't be marketed as a feature unless it is built.**

---

## 6. Input

```
IInputProvider ─┬─ KeyboardMouseProvider      (PC)                       [P0]
                ├─ GamepadProvider            (Input System; iPad via GameController) [P0]
                ├─ TouchProvider              (iPad on-screen pedals/steer, tilt)    [Planned]
                ├─ WindowsWheelProvider       (Input System HID / DirectInput on PC) [P0 when wheel attached]
                └─ BridgeWheelProvider        (UDP from MASAR Wheel Bridge)           [Planned]
         ▼
   InputRouter: active-device arbitration, per-device calibration profile
   (rotation range, deadzones, sensitivity curves, inversion, pedal min/max)
         ▼
   DriverControls (plain struct, read by the vehicle at the fixed rate)

IForceFeedbackSink ─ capability flags (none / rumble / constant-force / full)
   ← driven by VehicleTelemetry (self-aligning torque, kerb rumble, collisions)
   The UI exposes FFB settings ONLY when a sink reports real capability.
```

**G923 strategy** (research §2):
1. **On-device truth.** Run the `ios-input-probe` app with the wheel plugged into the iPad. If iPadOS exposes nothing, which is expected, record that as a fact.
2. **MASAR Wheel Bridge [Planned, Month 2].**
   - A small desktop app on the PC reads the G923 and drives **real force feedback** through DirectInput / SDL3 haptics.
   - It streams `DriverControls` to the iPad over UDP, ideally with the iPad on wired Ethernet through a USB-C adapter.
   - The iPad streams back the force-feedback parameters from the physics.
   - A Linux single-board computer could replace the PC later. That would cost hardware money, so it needs approval.
3. **DriverKit research [Planned, needs a paid Apple account]:** a USBDriverKit spike that tries to claim the wheel. Kill it if Apple's HID driver can't be displaced.
4. Gamepads (DualSense/Xbox) and touch are first-class, always-available options.

---

## 7. Cameras [P0: chase, cockpit, hood, free; Planned: cinematic, passenger polish]

- Each `ICameraMode` has its own serializable settings record: FOV, offset, height, rotation, follow distance, sensitivity, smoothing, shake, motion effects.
- `CameraRig` switches modes with a short blend.
- Settings are saved per mode in JSON.
- Cockpit mode uses a head anchor on the vehicle, plus G-force head motion clamped by a setting.

---

## 8. World, roads and city [Planned beyond the Phase 0 test block]

| Layer | Approach |
|---|---|
| Source data | **OpenStreetMap (ODbL)** extract of the chosen Jeddah area. Real road geometry is used as reference. See research §4 for the attribution and share-alike duties |
| Road graph | Offline Python tool (`tools/osm`) → normalized JSON: nodes, directed **lanes** with widths, turn connectors, **U-turn slots**, roundabouts, signal groups, speed limits |
| Road meshes | Unity editor generator. Spline road surfaces, kerbs, medians, sidewalks, markings as geometry, intersection patches |
| Buildings | Blender-made **modular kits** (facade bays, balconies, AC units, water tanks, gates, villa walls, shopfronts) + procedural assembly from footprints + hand-built landmarks |
| Streaming | 256 m cells (Addressables), HLOD per cell, LOD rings |
| Traffic AI [Month 4] | Lane-graph navigation, **IDM** car-following + **MOBIL** lane changes, signal controllers, driver-profile parameters (careful → aggressive), simulation tiers by distance |
| Rules [Month 4] | Data-driven rule sets in `data/rules`. **Every legal figure carries a source citation**. Nothing is hardcoded |

---

## 9. Diagnostics and performance monitoring [P0]

| Metric | Source | Honesty rule |
|---|---|---|
| FPS / frame time | Unscaled delta, rolling 1 s average + 1% low | — |
| CPU main / render thread time | `FrameTimingManager` | Shown as "n/a" if the platform returns no data |
| GPU frame time | `FrameTimingManager` GPU time (Metal command-buffer timestamps) | "n/a" if unavailable. **We never estimate it** |
| Memory | `Profiler.GetTotalAllocatedMemoryLong`, Mono heap. On iOS: `phys_footprint` + `os_proc_available_memory()` via the plugin | — |
| Thermal | iOS: `ProcessInfo.thermalState` via the plugin. PC: "n/a" | **There is no GPU temperature: the OS does not expose it** |
| Counts | Active vehicles, pedestrians, renderers visible, draw/batch counts (`ProfilerRecorder`) | Dev builds only where the counter needs it |
| Settings | Current quality tier, render scale, resolution | — |

A toggle shows an overlay HUD, and the same data goes to a CSV recorder for soak and thermal tests.

---

## 10. UI and localization [Planned beyond the P0 menu]

- **UI Toolkit**, laid out for an 11-inch landscape screen with safe areas.
- Localization through the Unity Localization package, **English + Arabic**.
- **[Risk]** Arabic text shaping and right-to-left layout in Unity UI must be prototyped before UI production (Month 2). The candidates are UI Toolkit's own text engine and a vetted open-source shaping library.
- Typefaces will be openly licensed fonts with Arabic coverage, recorded in ASSET_INVENTORY.md.

---

## 11. Audio [Planned, Month 2]

- Engine sound: RPM- and load-crossfaded loops (granular later), separate interior and exterior mixes, and a low-pass filter when the camera is in the cabin.
- Event sounds: tire noise driven by slip, surface noise from road material, wind from speed.
- Source budget: a fixed pool of voices, with distance-based culling of traffic engine sounds.

---

## 12. Asset pipelines

| Pipeline | Steps |
|---|---|
| Vehicle | Blender (reference blueprints from public-domain or self-taken photos) → exterior LOD0–3 + interior (cockpit-grade) → named pivots (wheels, doors, steering wheel, needles) → FBX/glTF → Unity prefab + `VehicleDefinition` |
| Building kit | Blender Python generators for modular facade pieces, with a texel-density standard (1024 px/m near, atlas far) |
| Roads | OSM → `tools/osm` → JSON → Unity generator |
| Textures | CC0 sources only, logged in ASSET_INVENTORY.md, ASTC 6×6 (albedo) / 4×4 (normals) on iOS |
| Audio | Self-recorded or CC0, normalized and loop-trimmed in Audacity |

---

## 13. iPad optimization strategy and budgets

These are starting budgets to validate on the device in Phase 0/1. They aren't measurements yet.

| Budget | Target |
|---|---|
| Frame time | 16.6 ms at 60 fps (fallback 25 ms at 40 fps). GPU ≤ 12 ms with 4 ms headroom for thermals |
| Memory | **≤ 3 GB** total app footprint initially. Raise it only after measuring `os_proc_available_memory()` |
| Draw calls / batches | ≤ 1,500 (Resident Drawer instancing reduces this) |
| Triangles on screen | ≤ 3–5 M |
| Texture memory | ≤ 1 GB streamed pool (High) |
| Vehicle physics | ≤ 0.3 ms per player car per fixed step. AI cars use a simplified model |
| Thermal | 30-minute soak test at High without reaching `serious`. If it does, drop the tier automatically |

---

## 14. Build and distribution

See BUILD_INSTRUCTIONS.md. In summary:
1. The Windows Unity editor builds an **Xcode project** with a batch-mode script.
2. A cloud macOS runner archives and signs it, then **uploads to TestFlight** with an App Store Connect API key.
3. The iPad installs the build from TestFlight.
4. Before a paid account exists, the iPad can only be reached by building an unsigned IPA and having the user re-sign it with a free Apple ID. That needs a third-party tool and the profile expires after 7 days.

---

## 15. High-risk assumptions register

| # | Assumption | Risk | How we retire it |
|---|---|---|---|
| R1 | The G923 can be used with the iPad | **Very high**: official docs say no public API | Probe app on the device (P0). Wheel bridge (Month 2). DriverKit spike (paid account) |
| R2 | Genuine FFB on the iPad | **Very high** | Only through the bridge or DriverKit. Otherwise no FFB UI |
| R3 | We can build and install on the iPad without a Mac at $0 | High | Cloud macOS CI. The Apple Developer Program ($99/yr) is effectively needed for TestFlight (needs approval) |
| R4 | URP reaches the photoreal target at 60 fps on the M5 | Medium-high | Phase 0 test block measured on the device. Fallback is 40 fps + lower render scale |
| R5 | A city block fits the memory budget | Medium | Streaming cells + measured `phys_footprint` |
| R6 | Arabic RTL text quality in Unity UI | Medium | Month 2 UI prototype |
| R7 | Real-car branding | Legal | Generic vehicles until licensing is resolved |
| R8 | Saudi traffic rules data accuracy | Medium | Only cite official sources (Ministry of Interior / Moroor, Absher, Saudi Driving School materials). Every figure is cited |
| R9 | The Windows Unity Profiler can attach to iPad builds over Wi-Fi | Low-medium | Test with the first device build |
