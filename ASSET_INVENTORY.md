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
| G003 | MASAR app icon (asphalt, lane line, kerb stripes, name) | 1024² PNG | Self-made: `tools/make-icon.ps1` → `Assets/Masar/Art/Icon/AppIcon.png` | Project-owned | — | 2026-11-21 | ✓ |
| G002 | Jeddah test block (roads, kerbs, buildings, villas, mosque, palms, lights, signal props) | Procedural meshes | Self-made: `TestBlockBuilder.cs`, `CityKit.cs` | Project-owned | — | 2026-11-14 | ✓ |

### Selectable cars (third-party, local only, private testing)

Added on 2026-11-20 at the owner's request: the owner downloaded these from Sketchfab, and the license file inside each download was read. **None of these files is in git.** `game/Assets/Masar/Art/Vehicles/` is gitignored, because the Sketchfab Standard license forbids redistributing the files and the repository is public. They reach the iPad only inside the compiled app, through the encrypted Xcode export. `ImportedCarBuilder.cs` converts them into prefabs (gitignored, under `Generated/` and `Prefabs/`).

**These cars break policy rule 3 (real brands). They are allowed in private TestFlight builds only.** Before any public release, each one must be replaced or licensed (KNOWN_ISSUES G1).

| ID | Car | Source | License | Attribution (shown in the selector) | Status |
|---|---|---|---|---|---|
| V001 | Volkswagen Jetta GLI (2025): 385k tris, full interior | https://sketchfab.com/3d-models/volkswagen-jetta-gli-2025-4adbe6ffcf5246ddac95921cb39bd2bf by geyges | Sketchfab Standard: may be used in a game; may not be redistributed | "Volkswagen Jetta GLI (2025)" by geyges | ⚠ Private only. Real brand. Material names (`Display_VW_..._AR_OK`) suggest a manufacturer AR asset, so the origin is unverified |
| V002 | Hyundai Elantra N (2024): 258k tris, interior with a fixed steering wheel | https://sketchfab.com/3d-models/2024-hyundai-elantra-n-4ba1b1b0eb844e318cc708ada1f2f51f by Ddiaz Design | CC BY-NC-SA 4.0 | This work is based on "2024 Hyundai Elantra N" by Ddiaz Design, licensed under CC-BY-NC-SA-4.0 | ⚠ Private only. **Non-commercial.** Real brand. Naming (`..._lodA`, `RewardRecycled`, `(Clone)`) suggests it was extracted from a game, so the origin is unverified |
| V003 | Lamborghini Aventador SVJ (LP 770-4): 32k tris, no interior | https://sketchfab.com/3d-models/lamborghini-aventador-lp-770-4-svj-71460dcc354045ddb1e7a8670678ffca by amogusstrikesback2 | CC BY 4.0 | This work is based on "lamborghini aventador lp 770-4 svj" by amogusstrikesback2, licensed under CC-BY-4.0 | ⚠ Private only. Real brand (the license itself allows commercial use) |
| V004 | Aventador SVJ Mansory twin-turbo: 975k tris, 85 textures | https://sketchfab.com/3d-models/7fc25aa2e7a24e28be35829d635e93fa by VTX | CC BY-NC-SA 4.0 | "Lamborghini Aventador SVJ Mansory Twin Turbo PBR" by VTX, CC BY-NC-SA 4.0 | ⚠ Private only. **Non-commercial.** Real brands. A kitbash: it contains parts named after other vehicles (`G800BRABUS4X4`) |
| — | 2011 Lamborghini Aventador LP700-4 (ActsOfDoge) | https://sketchfab.com/3d-models/2011-lamborghini-aventador-lp700-4-9d6ab953e2064bceb9cf8772bfef79bb | Sketchfab Standard | — | ❌ **Not used.** The author's own description calls it an "NFS port", meaning it was ripped from a Need for Speed game. Policy rule 2 |

## Textures & materials

T001–T005 were downloaded on 2026-11-14 from ambientCG as 1K-JPG sets; T006–T008 and H001 on 2026-11-19 from Poly Haven as 2K sets. Only Color, NormalGL, Roughness and AmbientOcclusion maps are kept; Displacement and NormalDX are dropped. `*_MetallicSmoothness.png` files are derived by `MaterialLibrary.PackSmoothness` (smoothness = 1 − roughness).

| ID | Asset | Resolution | Source | License | Attribution | Added | Verified |
|---|---|---|---|---|---|---|---|
| T001 | Asphalt031 (road surface) | 1024² | https://ambientcg.com/view?id=Asphalt031 | CC0 1.0 | Not required ("ambientCG.com" credited in credits) | 2026-11-14 | License page https://docs.ambientcg.com/license/ |
| T002 | Concrete034 (kerbs, stone cladding tint) | 1024² | https://ambientcg.com/view?id=Concrete034 | CC0 1.0 | Not required | 2026-11-14 | ✓ |
| T003 | PavingStones138 (interlocking sidewalk pavers) | 1024² | https://ambientcg.com/view?id=PavingStones138 | CC0 1.0 | Not required | 2026-11-14 | ✓ |
| T004 | PaintedPlaster017 (building walls, 4 tints) | 1024² | https://ambientcg.com/view?id=PaintedPlaster017 | CC0 1.0 | Not required | 2026-11-14 | ✓ |
| T005 | Ground093C (sand / empty plots / median) | 1024² | https://ambientcg.com/view?id=Ground093C | CC0 1.0 | Not required | 2026-11-14 | ✓ |
| T006 | Poly Haven `asphalt_02` (main road surface, also its own detail map) | 2048² | https://polyhaven.com/a/asphalt_02 | CC0 1.0 | Not required ("Poly Haven" credited in credits) | 2026-11-19 | License page https://polyhaven.com/license |
| T007 | Poly Haven `road_damaged` (patched / cracked road sections) | 2048² | https://polyhaven.com/a/road_damaged | CC0 1.0 | Not required | 2026-11-19 | ✓ |
| T008 | Poly Haven `palm_tree_bark` (date palm trunks) | 2048² | https://polyhaven.com/a/palm_tree_bark | CC0 1.0 | Not required | 2026-11-19 | ✓ |
| H001 | Poly Haven HDRI `kloofendal_43d_clear_puresky` (sky, reflections, sun direction) | 2K .hdr | https://polyhaven.com/a/kloofendal_43d_clear_puresky | CC0 1.0 | Not required | 2026-11-19 | ✓ |
| M001 | All other materials (car paint, glass, metals, lights, signals, palm leaflets, tire wear, worn paint) | — | Self-made in code (`MaterialLibrary`, `CityMaterials`) | Project-owned | — | 2026-11-14 | ✓ |
| M002 | Generated textures: detail albedo maps, palm leaflet palette, paint-wear noise, facade grime atlas (`Assets/Masar/Generated/*.png`, regenerated by the setup step, not committed) | 256²–1024² | Derived in code from T002/T006/T007 and procedural noise | Same as source (CC0) / project-owned | — | 2026-11-19 | ✓ |

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
| glTFast (`com.unity.cloud.gltfast`) | 6.20.0 | Unity Technologies | Apache 2.0 | Editor import of the glTF car models. The game ships the converted meshes, not the importer's output |
