# MASAR: Roadmap

This is a six-month plan run as **milestone gates**. Each gate needs passed acceptance criteria and the owner's approval before the next phase starts. Stability beats feature count. Anything that can't be finished is documented and staged, never faked.

Legend: ✅ done · 🟡 in progress · ⬜ not started · ⛔ blocked

## Phase 0 / Month 1: Technical foundation

| Deliverable | Status | Acceptance criteria |
|---|---|---|
| Environment inventory | ✅ | `docs/research/ENVIRONMENT_INVENTORY.md` |
| Engine research and selection | ✅ | Sourced comparison. Unity 6.3 LTS URP selected (PHASE0_RESEARCH §3) |
| Technical architecture | ✅ (draft) | TECHNICAL_ARCHITECTURE.md, including the risk register |
| Repository and docs | ✅ | Public GitHub repo, all seven documents |
| Windows → TestFlight pipeline | 🟡 | Probe compiles on a cloud Mac ✅. iOS Xcode export ✅. Signed TestFlight upload ⛔ needs the Apple secrets (owner) |
| G923 iPadOS investigation | 🟡 | Research ✅. **On-device probe run** ⛔ pending (needs TestFlight) |
| Playable skeleton (menu, car, cameras, input, overlay) | ✅ | Compiles clean, runs in editor + Windows player, UI verified at iPad aspect |
| Vehicle physics foundation | ✅ | Pure-C# model, 6/6 EditMode + 1/1 PlayMode tests |
| Visual benchmark block | ✅ | Generated Jeddah-style block, reviewed and fixed over several render passes |
| Graphics tiers + LOD/occlusion | ✅ | Four tiers measured (PC). Building LOD + baked occlusion |
| **Runs on the iPad** | ⛔ | TestFlight build launches on the iPad Pro M5; overlay numbers recorded in PERFORMANCE_REPORT.md |
| Performance profiling | 🟡 | Overlay + CSV + smoke-test reports working. Device numbers pending |

**Gate 0 exit criteria:** the skeleton runs on the iPad from TestFlight, at least 30 fps sustained on High in the test block for 10 minutes with no thermal `critical` state; G923-on-iPad result recorded; owner approves Month 2 scope.

## Month 2: Vehicles, rendering, and the first real city (approved 2026-11-21)

**Owner approval 2026-11-21.** The owner approved Month 2 and pulled the first real map, AI traffic and pedestrians forward from Months 3–4, with a heavier graphics tier. (On the iPad, Ultra ran at about 90 fps in the test block.) Owner choices:
- Map: Tahlia / Al Rawdah, from OpenStreetMap.
- OSM download: approved.
- Pedestrians: the owner provides Mixamo characters; placeholders until then.

| Deliverable | Status | Acceptance criteria |
|---|---|---|
| Wheel bridge v1 (G923 → PC → Wi-Fi → iPad, FFB) | ✅ | Owner confirmed it works with minimal delay (2026-11-21). A wheel without the PC is to be revisited later |
| First real map: Tahlia / Al Rawdah from OSM (`tools/osm`, `OsmCityBuilder`) | 🟡 | Real road network at real scale (lanes, kerbs, sidewalks, markings, signals). Surveyed + generated buildings. Drivable on the iPad |
| AI traffic v1 (lane graph, IDM, signals, junction conflicts) | 🟡 | Cars follow lanes, stop at red, yield at junctions, react to the player. Lane changes are v2 |
| Pedestrians v1 | 🟡 | Walk the sidewalks with placeholder bodies. Mixamo characters once provided. Road crossing is v2 |
| Max graphics tier | 🟡 | Measured on the iPad in the city |
| City streaming (2026-11-23) | 🟡 | Each 512 m block's buildings is its own scene, loaded around the camera (far boxes stay up until the detail is in). The loading screen waits for the blocks around the car. Done on PC; to be measured on the iPad (memory, hitches while driving) |
| Main menu, intro and logo (2026-11-23) | ✅ | The owner's logo is the app icon, the iPad launch screen and the intro. Menu over a 3D showroom of the selected car. All screens checked at iPad aspect |
| Items below (hero vehicle, physics, audio, APV lighting, Arabic) | ⬜ | As listed |

Dependencies: Gate 0.
- Hero test vehicle in Blender: exterior LOD0–3, cockpit-grade interior, working gauges. A generic design.
- Physics: proper static friction, tire temperature not needed yet, suspension geometry (camber/caster), tuning against reference data, PhysX-collision feel.
- Audio foundation: engine RPM/load blend, interior/exterior mix, tires, wind.
- Rendering: APV lighting scenarios (day/sunset/night), headlights at night, graphics settings validated on the device (each tier measured).
- **Wheel bridge v1** (Windows → iPad over UDP, real FFB driven on the PC). DriverKit spike (go/no-go).
- Arabic text shaping + RTL prototype.
- Acceptance: three distinct vehicle feels from three definitions alone; 60 fps target on High, or a documented reason why not.

## Month 3: Driving feel, a living city, and device numbers (planned 2026-11-26)

The original Month 3 (OSM road tool, road meshes, first real neighbourhood, cell streaming) was pulled into Month 2 and is built: the Tahlia / Al Rawdah map streams in 512 m blocks with AI traffic and pedestrians. Month 3 now takes the Month 2 items still open, plus the parts of Months 3–4 that make the city feel alive. The plan was written and started on 2026-11-26 at the owner's request ("plan month 3 fully and build on it").

| # | Deliverable | Status | Acceptance criteria |
|---|---|---|---|
| M3-1 | **Lane autopilot and benchmark drive.** The car steers itself along the lane graph, keeps to the speed limit (80 km/h at most), stops for red lights and for cars ahead. Used by the smoke test, and by a **Benchmark drive** button on the iPad that records frame times, frames over 50 ms, memory and thermal state and saves a report | 🟡 PC ✅ (177 s, 1.86 km, 0 collisions, 1 frame over 50 ms); iPad run pending | On the PC: a 3-minute drive through Tahlia without leaving the road or hitting anything; the report counts frames over 50 ms. On the iPad: the owner taps the button once and the numbers go into PERFORMANCE_REPORT.md (Gate 0 and the Month 3 streaming criterion) |
| M3-2 | **Audio foundation.** The engine is synthesised from the simulation (firing frequency from rpm and cylinder count, load from throttle and torque), plus turbo whine, tyre squeal from slip, rolling noise and wind (gear-change and indicator clicks: not built). Inside cameras hear a muffled mix. Volume setting. No downloaded sound files | 🟡 Built; tests ✅ (pitch exact at fixed rpm), recorded drive 7 of 12 moments within 5 % (misses under hard acceleration); owner to listen | A recorded smoke-test drive: the engine's main frequency follows rpm × cylinders / 120 within 5 %, squeal only while the tyres slide. Owner listens on the iPad |
| M3-3 | **Time of day: day, sunset, night.** Sun angle, colour and sky exposure per setting. At night: street lamps light the road near the car, the player's headlights (low and high beam), traffic head- and tail-lights, lit windows | 🟡 Built, without lit windows (needs a city rebuild); PC 60 fps on High in all three; shots in `build/screenshots/m3` | Review shots of the same spots in all three; PC frame cost measured; Night playable on High |
| M3-4 | **Physics fixes T3 and T8.** Real static friction when stopped (no creeping on slopes while braking), idle holds 750 rpm | ✅ 0.00 cm on a 10 % slope; idle 750 rpm | New EditMode tests: a braked car on a 10 % slope moves < 1 cm in 10 s; idle settles within ±25 rpm |
| M3-5 | **AI traffic lane changes (MOBIL) with indicators.** Cars move to a faster or freer lane when it is safe, and leave a lane blocked by a stopped car; the indicator blinks before the move | 🟡 2–4 lane changes per PC run; 1 AI–AI overlap in 4 runs (to find); no indicator review shot yet | Counted lane changes in a PC run, no AI-AI collisions, review shot of a car changing lanes with its indicator on |
| M3-6 | **TestFlight build and docs** | ✅ TestFlight build 32 (2026-11-26) | Shipped, ROADMAP / PROJECT_STATUS / KNOWN_ISSUES / PERFORMANCE_REPORT updated |
| M3-7 | **City fixes from the owner's drive (2026-11-26).** Medians of divided roads were flat asphalt with no kerb and no lanes ("open spaces like an 8 lane road"), and pedestrians walked through buildings that stood on the sidewalk | ✅ PC; TestFlight build 34 uploading | Medians raised, kerbed and planted, opening only where a turning path crosses; every junction connector on asphalt (0 of 227,172 m off it); walking positions inside a building 2.7 % → 0.1 % (KNOWN_ISSUES C14, C15). Benchmark drive: 0 collisions, 0 frames over 50 ms |

Not in Month 3, and why:
- **Arabic text and right-to-left UI.** Needs a font that has Arabic (for example Noto Sans Arabic, SIL Open Font License). That is a download, so it waits for the owner's OK.
- **Hero vehicle with a cockpit interior.** Needs modelling in Blender. Not possible without an artist's time or a licensed model.
- **Mesh merging and HLOD.** Wait for the device numbers from M3-1, so the work goes where the iPad actually needs it.
- **Roundabouts, weather, more vehicle types, sourced traffic rules.** Stay in Month 4.

## Month 4: Simulation

- Traffic AI (lane graph, IDM + MOBIL, driver profiles, signals, U-turns, roundabout yielding), with distance-based simulation tiers.
- Pedestrian foundation. Rules data (sourced) and violation detection foundation.
- Weather (rain + wet roads, haze, dust), day/night cycle, environment audio, more vehicles.

## Month 5: Gameplay

- Driving school flow, theory exam (sourced content), practical exam maneuvers, scoring, licence unlock.
- Vehicle selection, vehicle settings, free roam, extra areas.

## Month 6: Polish and TestFlight

- Optimization, thermal soak tests, memory tests, UI polish, wheel calibration UX, bug fixing, external TestFlight build.

## Dependency notes

- The **city scale** depends on the **memory/thermal budget measured on the device**. Gate 0 must produce real numbers first.
- **G923 support on the iPad** depends on the probe result plus the bridge or DriverKit spikes. No gameplay system may require the wheel.
- **Traffic rules and the driving exam** depend on sourced legal research, scheduled in Month 4/5.
