# MASAR: Asset Inventory

Every asset in the repository must have a row here before it is committed. If an asset's source or license is unknown, it does not ship.

## Policy

1. **Allowed sources, in priority order:**
   1. Self-created: Blender, procedural generation in code or shaders, our own photos and recordings.
   2. CC0 / public domain: Poly Haven, ambientCG and similar. Verify on the asset's own page.
   3. Permissive licenses (CC-BY, MIT) **with the attribution recorded below**.
   4. OpenStreetMap-derived geometry (ODbL). Attribution and share-alike obligations are recorded in `docs/research/PHASE0_RESEARCH.md`.
2. **Not allowed:** ripped game assets, paid assets redistributed without a license, models of unknown origin from free-model sites, commercial map tiles or satellite imagery, and engine-locked content used outside its engine (for example, assets licensed only for Unreal).
3. **Real vehicle brands:** use generic, unbranded vehicles until trademark and trade-dress licensing is resolved (see KNOWN_ISSUES.md).
4. **Columns:** every row records the source URL (or "self-made" plus the tool and file), the license, the attribution text, the date added, and who verified it.

## Models

| ID | Asset | Type | Source | License | Attribution | Added | Verified |
|---|---|---|---|---|---|---|---|
| G001 | MASAR Test Sedan (generic, unbranded) | Procedural mesh (placeholder) | Self-made: `Assets/Masar/Code/Editor/TestCarBuilder.cs` | Project-owned | — | 2026-11-14 | ✓ |
| G002 | Jeddah test block (roads, kerbs, buildings, villas, mosque, palms, lights, signal props) | Procedural meshes | Self-made: `TestBlockBuilder.cs`, `CityKit.cs` | Project-owned | — | 2026-11-14 | ✓ |

## Textures & materials

All maps were downloaded on 2026-11-14 from ambientCG as 1K-JPG sets. Only Color, NormalGL, Roughness and AmbientOcclusion maps are kept; Displacement and NormalDX are dropped. `*_MetallicSmoothness.png` files are derived by `MaterialLibrary.PackSmoothness` (smoothness = 1 − roughness).

| ID | Asset | Resolution | Source | License | Attribution | Added | Verified |
|---|---|---|---|---|---|---|---|
| T001 | Asphalt031 (road surface) | 1024² | https://ambientcg.com/view?id=Asphalt031 | CC0 1.0 | Not required ("ambientCG.com" credited in credits) | 2026-11-14 | License page https://docs.ambientcg.com/license/ |
| T002 | Concrete034 (kerbs, stone cladding tint) | 1024² | https://ambientcg.com/view?id=Concrete034 | CC0 1.0 | Not required | 2026-11-14 | ✓ |
| T003 | PavingStones138 (interlocking sidewalk pavers) | 1024² | https://ambientcg.com/view?id=PavingStones138 | CC0 1.0 | Not required | 2026-11-14 | ✓ |
| T004 | PaintedPlaster017 (building walls, 4 tints) | 1024² | https://ambientcg.com/view?id=PaintedPlaster017 | CC0 1.0 | Not required | 2026-11-14 | ✓ |
| T005 | Ground093C (sand / empty plots / median) | 1024² | https://ambientcg.com/view?id=Ground093C | CC0 1.0 | Not required | 2026-11-14 | ✓ |
| M001 | All other materials (car paint, glass, metals, lights, signals, palms) | — | Self-made in code (`MaterialLibrary`, `CityMaterials`) | Project-owned | — | 2026-11-14 | ✓ |

## Audio

| ID | Asset | Format | Source | License | Attribution | Added | Verified |
|---|---|---|---|---|---|---|---|
| — | *(none yet)* | | | | | | |

## Fonts

| ID | Font | Scripts | Source | License | Added | Verified |
|---|---|---|---|---|---|---|
| — | *(none yet)* | | | | | |

## Geographic data

| ID | Dataset | Area | Source | License | Attribution required | Added |
|---|---|---|---|---|---|---|
| — | *(none yet)* | | | | | |

## Third-party code / packages

| Package | Version | Source | License | Notes |
|---|---|---|---|---|
| Unity 6.3 LTS engine + URP + Input System + uGUI | 6000.3.28f1 / bundled | Unity Technologies | Unity Personal (free under US$200k revenue/funding) | Engine runtime. The Xcode export is encrypted before upload because the repo is public |
| XcodeGen (CI only) | latest via Homebrew | https://github.com/yonaskolb/XcodeGen | MIT | Generates the probe's Xcode project on the macOS runner. Not shipped |
| Unity built-in font (LegacyRuntime) | bundled | Unity | Unity EULA | Phase 0 UI font (Latin only) |
