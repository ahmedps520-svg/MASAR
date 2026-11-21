# MASAR: Known Issues, Limitations and Unimplemented Systems

Status labels: **Working**: implemented and tested. **Prototype**: implemented, limited testing. **Placeholder**: stand-in content. **Planned**: not implemented. **Blocked**: cannot proceed without an external change.

## Blockers and platform limitations

| # | Issue | Status | Detail |
|---|---|---|---|
| L1 | **Logitech G923 can't be read on iPadOS through public game APIs. Workaround built 2026-11-21: the wheel bridge** (G923 → Windows PC → Wi-Fi → iPad, `tools/start-wheel-bridge.ps1`). Tested end to end on the PC with a simulated wheel: 250 Hz, 0 packets lost, 3–4 ms round trip on loopback. First real-wheel test, 2026-11-21: only Cross and Square worked, because the G923 (PlayStation version) was in PlayStation compatibility mode (USB 046D:C267, a 14-button gamepad) and the bridge used Windows.Gaming.Input, which also returns no data while its window is in the background. Fixed the same day: the bridge reads the wheel through DirectInput in the background and starts G HUB to switch the wheel to PC mode (C266). Verified on the PC with the real G923: steering read live, force feedback in both directions with the direction measured, pedals at rest read 0. Still to confirm: driving on the iPad over Wi-Fi, pedal travel and the button legend. Owner confirmed 2026-11-21 that the wheel plugged into the iPad does nothing | Workaround (prototype) | `GCRacingWheel` is macOS/Mac Catalyst-only, and generic HID devices are filtered out on iPadOS. Sources in `docs/research/PHASE0_RESEARCH.md` §2. The probe app will confirm this on the device. Alternatives: the planned wheel bridge (PC or Linux board → network → iPad), supported gamepads, touch controls |
| L2 | **No force feedback through the iPad itself** | Workaround (prototype) | iPadOS has no public FFB API. With the wheel bridge, the force is computed by the game (aligning torque, scrub, steering lock) and played by the PC through DirectInput and the Logitech G HUB driver. Direct-to-iPad remains a research spike: iPadOS has USBDriverKit on M-series iPads but no HIDDriverKit, a USB driver for a Logitech device needs a vendor-ID entitlement from Apple for distribution, and Xcode can't currently sign USB DEXTs (Apple DTS, forum thread 847493) |
| L3 | No Mac | Limitation | No Xcode debugger or Instruments. Diagnostics come from the in-game overlay, CSV logs, the Unity Profiler over Wi-Fi (development builds) and TestFlight crash reports |
| L4 | The engine must be installed by the user | Resolved when done | Installing the Unity editor needs a Windows admin prompt and a Unity account sign-in (BUILD_INSTRUCTIONS §1) |
| L5 | **Resolved 2026-11-20:** Apple secrets configured | Done | The owner ran `tools/setup.ps1`: API key, issuer, team, P8 and passphrase secrets are stored, the bundle ID and app record exist, and the preflight passes |
| L6 | Diagnostics upload not configured yet | Blocked on user | The **Send diagnostics** button works and saves locally (Files app > MASAR > diagnostics), and it says "Upload isn't configured yet" until the ForgeBase project exists (setup step 4) |

## Phase 0 placeholders (not final quality)

| # | Item | Status | Plan |
|---|---|---|---|
| P1 | **Improved 2026-11-20:** a car selector (main menu and pause menu) offers imported third-party models next to the procedural test sedan. Wheels, steering, lamps, colliders and wheelbase/track/tire radius come from each model. Engine, gearing and mass are class estimates, not manufacturer data. Gaps: the Aventador SVJ has no interior at all; the Elantra's steering wheel is part of its interior mesh, so it doesn't turn; there's no paint-color choice and no per-car sound | Prototype (private testing only, see G1) | Month 2: a licensed or self-modelled hero vehicle with a cockpit-grade interior |
| P2 | Buildings are procedurally generated from a modular facade kit | Prototype | Month 3: Blender-authored modular kits + landmarks. The procedural kit stays for background fill |
| P3 | The test block is a hand-designed layout, not a real Jeddah location | Placeholder | Month 3: OSM-derived road network for the chosen neighborhood |
| P4 | Traffic-signal heads cycle on a timer | Placeholder (visual prop) | Month 3/4: real signal controllers bound to the road graph. Nothing reads these props |
| P5 | Shop signs are blank colored panels | Placeholder | Deliberate: no invented brands. Real signage art later |
| P6 | **Improved 2026-11-19:** date palms are now procedural geometry: scanned bark trunk with boot relief, 36–45 fronds with folded leaflets, dead-frond skirts, 6 variants × 3 LODs. No wind animation, no date clusters | Prototype | Month 3 vegetation pass: wind sway (vertex shader), date clusters, ground shrubs |
| P7 | **Improved 2026-11-19:** lighting is a CC0 HDRI sky (Poly Haven) with the sun aligned to it, trilight ambient with warm ground bounce, fog matched to the HDRI horizon, SSAO. Still a single time of day, no lightmaps or APV | Prototype | Month 2: APV lighting scenarios, time of day |
| P8 | **Improved 2026-11-20:** facades get weathering overlays placed where dirt really collects: rain drips under every sill, slab band and parapet, rust and condensate stains under split-AC units, a dust band at the base of walls, faint dirt patches across whole faces. Villa compound walls too. Still missing: sun bleaching, cracks, posters and wiring, shop-front grime | Prototype | Month 3: authored facade kits with baked weathering. The overlay system stays for variation |

## Not implemented (planned)

Traffic AI, pedestrians, audio, damage, weather, day/night cycle, driving school, theory test, practical exam, traffic rules and enforcement, OSM import, city streaming (cells), LOD/HLOD for the city, occlusion-culling bake, Arabic localization and RTL UI, wheel-bridge app, wheel calibration UI (the data model exists in `InputCalibration`).

## Known technical issues

| # | Issue | Detail |
|---|---|---|
| T1 | **G923 pedal axis mapping on Windows is unverified** | `WheelMapping` defaults (steer `stick/x`, pedals `z`/`rz`/`slider`) are guesses, because the wheel was not connected during Phase 0. Use **Input devices & wheel test** to read the real axes, then update the mapping |
| T2 | The UI font is Latin-only | The built-in font cannot shape Arabic. Arabic shaping and RTL need a font and shaping solution (Month 2 risk item R6) |
| T3 | The vehicle model's static hold at standstill is a viscous approximation | On slopes the car can creep slowly while braking. Fine on the flat test block. Month 2 adds proper static friction |
| T4 | Tire relaxation at very low speed uses a speed floor (0.6 m/s) | Prevents jitter, but slightly softens parking-speed response. Revisit in Month 2 |
| T5 | Glass is transparent (sorted). Cabin glass is visible from inside, but the interior has no reflections | Acceptable for Phase 0 |
| T6 | "Visible renderers" in the overlay counts `Renderer.isVisible` | Includes objects rendered only for shadows (Unity's definition) |
| T7 | Generated meshes, scenes, prefab and materials are not in git (about 132 MB per regeneration) | After a fresh clone, run **MASAR → Setup → Configure Project** and **MASAR → Build → Test Block Scene**, or `-executeMethod Masar.EditorTools.MasarProjectSetup.ConfigureAndBuildScenes`. The builders are the source of truth |
| T8 | Idle settles at about 693 rpm instead of the 750 target | The idle governor's throttle authority is slightly low. Tuning item for Month 2 |
| T9 | Test-car cockpit is crude | Very dark untextured dashboard, a boxy rear-view mirror, the gauge cluster is an emissive panel (no working needles). Phase 0 placeholder (P1) |
| T11 | **Fixed 2026-11-15:** quality tiers were excluded per platform | Inherited from the URP template: Medium/High/Ultra were excluded on iOS and Low on Windows, so the iPad would only have offered Low. `MasarProjectSetup` now clears the exclusions. Found by the per-tier measurement, before any device build was installed |
| T12 | GPU Resident Drawer not yet verified as active | It's enabled on Medium and above, but draw counts don't prove it is working (every building mesh is unique, so there's little instancing to gain). Verify with the Frame Debugger or Profiler. City-scale draw-call reduction will come from per-cell mesh merging + HLOD (Month 3) |
| T13 | Occlusion culling effect not isolated | The data bakes (457 KB), but its draw-call saving hasn't been measured separately |
| T14 | **Fixed 2026-11-15:** UI layout bugs found by `-masarUiCapture` | The HUD overlapped the gas pedal, panels were see-through, the perf overlay drew over panels and the title, "HANDBRAKE" wrapped mid-word, and the touch gear selector needed three taps to reach D. All fixed and recaptured |
| T15 | Touch controls are layout-verified only | The PC has no touchscreen. Multi-touch (steer + pedal at once), pedal feel and button sizes need testing on the iPad |
| T16 | **Fixed 2026-11-20:** MSAA was 2x on every tier, which silently disabled STP | The setup helper wrote enum fields by declaration index, and URP's `MsaaQuality` starts at `Disabled = 1`, so "1" meant 2x. The player logged "Disabling TAA and STP because MSAA is on". Enums are now written by value. Earlier PC tier numbers were measured with MSAA 2x and no STP |
| T17 | PC GPU timings at 200+ fps are noisy (±0.3–0.5 ms, one outlier run at 7.3 ms vs 4.6 ms typical) | The GPU isn't fully loaded and its clocks vary. Compare runs from the same session only. The iPad numbers are the ones that count |
| T10 | Review screenshots are edit-mode renders | `MasarScreenshots` renders the scene at rest. The PlayMode drive test and the player smoke test cover motion |

## Licensing and legal

| # | Item | Status |
|---|---|---|
| G1 | Real vehicle brands, models and logos | **Used in private TestFlight builds only (since 2026-11-20, owner's decision).** The selectable cars V001–V004 are real brands (Volkswagen, Hyundai, Lamborghini, Mansory), shown with their names and badges. Apple guideline 5.2.1 and trademark law require licenses for any public release, so each car must be licensed or replaced by a generic design first. Two of them are also CC BY-NC-SA (non-commercial) and one has an unverified origin (ASSET_INVENTORY) |
| G2 | OpenStreetMap data | Not yet used. When used: on-screen and credits attribution "© OpenStreetMap contributors", plus ODbL share-alike for any distributed derivative database |
| G3 | Saudi traffic rules, fines and exam procedures | Not yet modelled. Must come from official sources (Ministry of Interior / Moroor, Absher, official driving-school material), with every figure cited |
