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
| Items below (hero vehicle, physics, audio, APV lighting, Arabic) | ⬜ | As listed |

Dependencies: Gate 0.
- Hero test vehicle in Blender: exterior LOD0–3, cockpit-grade interior, working gauges. A generic design.
- Physics: proper static friction, tire temperature not needed yet, suspension geometry (camber/caster), tuning against reference data, PhysX-collision feel.
- Audio foundation: engine RPM/load blend, interior/exterior mix, tires, wind.
- Rendering: APV lighting scenarios (day/sunset/night), headlights at night, graphics settings validated on the device (each tier measured).
- **Wheel bridge v1** (Windows → iPad over UDP, real FFB driven on the PC). DriverKit spike (go/no-go).
- Arabic text shaping + RTL prototype.
- Acceptance: three distinct vehicle feels from three definitions alone; 60 fps target on High, or a documented reason why not.

## Month 3: City foundation

Dependencies: Month 2 rendering and memory budget.
- OSM → road graph tool (`tools/osm`), lanes, turn connectors, U-turns, roundabouts.
- Road mesh generator from the graph: markings, kerbs, islands.
- First real Jeddah neighborhood. The owner picks it: Al Rawdah, Al Zahra, Al Salamah, Tahlia, the Corniche, etc.
- Building kits from Blender, landmark pass, cell streaming (256 m cells), HLOD, occlusion bake.
- Acceptance: the neighborhood streams within the memory budget measured on the device, with no hitches over 50 ms while driving at 80 km/h.

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
