# MASAR · مسار

A realistic urban driving simulator set in modern Jeddah, Saudi Arabia, built for the **iPad Pro (M5)**.

> **Status: Phase 0, technical foundation.** This is not a playable game yet. See [PROJECT_STATUS.md](PROJECT_STATUS.md).

MASAR ("path" or "route" in Arabic) is a driving simulator, not a racing game. You learn Saudi traffic rules, pass a theory and practical driving test, and then drive freely around a living, realistic Jeddah.

## Documents

| Document | What's in it |
|---|---|
| [PROJECT_STATUS.md](PROJECT_STATUS.md) | Where the project is right now: done, blocked, next |
| [ROADMAP.md](ROADMAP.md) | Six-month milestone plan with acceptance criteria |
| [TECHNICAL_ARCHITECTURE.md](TECHNICAL_ARCHITECTURE.md) | Engine, rendering, physics, input, streaming, risks |
| [BUILD_INSTRUCTIONS.md](BUILD_INSTRUCTIONS.md) | Windows setup, cloud macOS builds, TestFlight |
| [PERFORMANCE_REPORT.md](PERFORMANCE_REPORT.md) | Measured numbers only, by device |
| [KNOWN_ISSUES.md](KNOWN_ISSUES.md) | Bugs, limitations, unimplemented systems |
| [ASSET_INVENTORY.md](ASSET_INVENTORY.md) | Every asset with its source and license |
| [docs/research/](docs/research/) | Phase 0 research with sources |

## Repository layout

```
game/        Unity 6.3 LTS project (URP)
tools/       iPad input probe, wheel bridge, Blender + OSM pipelines
data/        Vehicle definitions and traffic-rule data (with citations)
docs/        Research and design notes
.github/     Cloud macOS build workflows
```

## Map data

Road and building geometry will be derived from OpenStreetMap: © OpenStreetMap contributors, available under the Open Database License (https://www.openstreetmap.org/copyright).
