using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Masar.Cameras;
using Masar.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Masar.EditorTools
{
    /// <summary>
    /// Turns third-party glTF car models (imported by glTFast from
    /// Assets/Masar/Art/Vehicles/Imported, which is NOT committed: see ASSET_INVENTORY.md)
    /// into drivable MASAR vehicles, and writes the car catalog the selector menu reads.
    ///
    /// The models are all organized differently, so nothing relies on their hierarchy beyond a
    /// few name patterns per car:
    ///  1. Every mesh is baked into car space (forward +Z, ground at y = 0, origin at the axle midpoint).
    ///  2. Wheels are found from the tire geometry (pattern, or automatically from cylinder-shaped
    ///     parts). Every triangle that lies inside a wheel's cylinder spins with that wheel, so it works
    ///     even when a model puts wheel parts and body parts in the same mesh.
    ///  3. Triangles are regrouped into a few renderers (body, 4 wheels, 4 caliper carriers, steering
    ///     wheel, lamp groups) with one submesh per material, which also cuts draw calls.
    ///  4. Materials are converted to URP Lit / Complex Lit (clear-coat paint, tinted glass, lamps
    ///     whose emission the VehicleVisuals component drives).
    ///  5. The physics definition gets the measured wheelbase, track and tire radii, plus
    ///     class-typical engine, gearing and mass values (estimates, not manufacturer data).
    /// </summary>
    public static class ImportedCarBuilder
    {
        public const string SourceRoot = "Assets/Masar/Art/Vehicles/Imported";
        const string OutRoot = "Assets/Masar/Generated/Vehicles";
        // In Resources so a car is loaded only when chosen (the catalog holds paths, not references)
        const string PrefabRoot = "Assets/Masar/Resources/Cars";
        public const string CatalogPath = "Assets/Masar/Resources/MasarCarCatalog.asset";
        const int MaxTexture = 2048;

        // ------------------------------------------------------------------ per-car specs

        sealed class Spec
        {
            public string Id, Name, Summary, Source, Credit, License, Warning;
            // Regexes, matched against "node/path/of/part|materialName"
            public string Tire, Carrier, Steering, Head, Tail, Reverse, Position, Paint, Glass, Drop;
            public string FrontHint;   // parts that are at the front of the car (grille), when no lamp pattern says so
            public bool AutoLamps;
            public float Wheelbase;    // > 0: rescale the model so its wheelbase matches the real car
            public float FrontShare = 0.6f, ComHeight = 0.52f;
            public Action<VehicleDefinition> Tune;
        }

        static readonly Spec[] Specs =
        {
            new Spec
            {
                Id = "jetta_gli", Name = "Volkswagen Jetta GLI 2025", Source = "jetta_gli/scene.gltf",
                Summary = "Sport sedan · FWD · 2.0 turbo, ≈230 hp class · full interior",
                Credit = "\"Volkswagen Jetta GLI (2025)\" by geyges on Sketchfab (sketchfab.com/3d-models/volkswagen-jetta-gli-2025-4adbe6ffcf5246ddac95921cb39bd2bf)",
                License = "Sketchfab Standard (not redistributable)",
                Warning = "Private testing only: real brand, origin unverified",
                Tire = @"Tire_(FL|FR|RL|RR)", Carrier = @"#Caliper", Steering = @"INT_STEERING_WHEEL",
                Paint = @"Car_Paint", Glass = @"Windows_|#Windows|Glass_Clear|Glass_Red",
                Head = @"Ext_Headlight#Glow", Tail = @"Ext_Taillight#Glow_Red",
                FrontShare = 0.61f, ComHeight = 0.53f,
                Tune = d => Sedan(d, 1450f,
                    new float[] { 1000, 1700, 2500, 4000, 5000, 5800, 6500 },
                    new float[] { 220, 350, 350, 350, 330, 300, 250 }, 6500f,
                    new[] { 3.56f, 2.53f, 1.59f, 1.13f, 0.89f, 0.72f, 0.58f }, 3.95f,
                    DrivetrainLayout.FrontWheelDrive, 1.02f, 1.15f, 0.29f, 2.2f),
            },
            new Spec
            {
                Id = "elantra_n", Name = "Hyundai Elantra N 2024", Source = "elantra_n/scene.gltf",
                Summary = "Sport sedan · FWD · 2.0 turbo, ≈280 hp class · interior (static wheel)",
                Credit = "\"2024 Hyundai Elantra N\" by Ddiaz Design on Sketchfab (sketchfab.com/3d-models/2024-hyundai-elantra-n-4ba1b1b0eb844e318cc708ada1f2f51f), CC BY-NC-SA 4.0",
                License = "CC BY-NC-SA 4.0 (non-commercial)",
                Warning = "Private testing only: non-commercial license, real brand",
                Tire = @"3DWheel (Front|Rear) [LR]", Carrier = @"Calliper", Paint = @"Paint_Material",
                Glass = @"Window_Material|red_glass", Position = @"LightA_Material", FrontHint = @"Grille1_",
                FrontShare = 0.61f, ComHeight = 0.52f,
                Tune = d => Sedan(d, 1440f,
                    new float[] { 1000, 1500, 2100, 3000, 4000, 4700, 5500, 6300, 6750 },
                    new float[] { 200, 300, 392, 392, 392, 392, 360, 320, 290 }, 6500f,
                    new[] { 3.64f, 2.38f, 1.58f, 1.19f, 0.97f, 0.83f, 0.70f, 0.58f }, 4.0f,
                    DrivetrainLayout.FrontWheelDrive, 1.08f, 1.35f, 0.31f, 2.2f),
            },
            new Spec
            {
                Id = "aventador_svj", Name = "Lamborghini Aventador SVJ", Source = "aventador_svj/scene.gltf",
                Summary = "V12 supercar · AWD · ≈770 hp class · no interior",
                Credit = "\"lamborghini aventador lp 770-4 svj\" by amogusstrikesback2 on Sketchfab (sketchfab.com/3d-models/lamborghini-aventador-lp-770-4-svj-71460dcc354045ddb1e7a8670678ffca), CC BY 4.0",
                License = "CC BY 4.0",
                Warning = "Real brand · the model has no interior",
                Tire = @"Wheel(FL|FR|BL|BR)", Carrier = @"caliper", Paint = @"carpaint", Glass = @"glasses_glass",
                Tail = @"lights_brakes|lights_position_back", Position = @"lights_position_front_and_back", Reverse = @"lights_reverse",
                FrontShare = 0.43f, ComHeight = 0.42f,
                Tune = d => Supercar(d, 1525f, 1f),
            },
            new Spec
            {
                Id = "aventador_mansory", Name = "Aventador SVJ Mansory twin-turbo", Source = "aventador_mansory/aventador_mansory.glb",
                Summary = "Tuned V12 supercar · AWD · ≈1,000 hp class · very heavy model",
                Credit = "\"Lamborghini Aventador SVJ Mansory Twin Turbo PBR\" by VTX on Sketchfab (sketchfab.com/3d-models/7fc25aa2e7a24e28be35829d635e93fa), CC BY-NC-SA 4.0",
                License = "CC BY-NC-SA 4.0 (non-commercial)",
                Warning = "Private testing only: non-commercial license, real brands, ~1M triangles",
                AutoLamps = true, Drop = @"(^|/)Plane", Wheelbase = 2.70f,
                FrontShare = 0.42f, ComHeight = 0.42f,
                Tune = d => Supercar(d, 1600f, 1.4f),
            },
        };

        // ------------------------------------------------------------------ physics presets

        static void Sedan(VehicleDefinition d, float mass, float[] rpm, float[] nm, float redline,
            float[] ratios, float finalDrive, DrivetrainLayout layout, float grip, float stiffness, float cd, float area)
        {
            d.body.massKg = mass;
            d.engine.torqueCurveRpm = rpm;
            d.engine.torqueCurveNm = nm;
            d.engine.idleRpm = 780f;
            d.engine.cylinders = 4;
            d.engine.turbo = true;   // both sedans are 2.0 turbo
            d.engine.redlineRpm = redline;
            d.engine.revLimiterRpm = redline + 200f;
            d.transmission.forwardRatios = ratios;
            d.transmission.finalDrive = finalDrive;
            d.transmission.upshiftRpmFull = redline - 300f;
            d.transmission.shiftTime = 0.14f;
            d.transmission.clutchMaxTorque = 600f;
            d.drivetrain.layout = layout;
            d.drivetrain.differential = DifferentialKind.LimitedSlip;
            d.drivetrain.lsdMaxLockFraction = 0.45f;
            d.brakes.frontTorque = 2700f;
            d.brakes.rearTorque = 1500f;
            Springs(d, mass, stiffness);
            Grip(d, grip);
            d.body.dragCoefficient = cd;
            d.body.frontalArea = area;
        }

        static void Supercar(VehicleDefinition d, float mass, float torqueScale)
        {
            d.body.massKg = mass;
            d.engine.torqueCurveRpm = new float[] { 1000, 2000, 3000, 4000, 5000, 6000, 6750, 7500, 8350, 8700 };
            var nm = new float[] { 300, 420, 520, 600, 660, 700, 720, 705, 650, 600 };
            for (int i = 0; i < nm.Length; i++) nm[i] *= torqueScale;
            d.engine.torqueCurveNm = nm;
            d.engine.idleRpm = 950f;
            d.engine.cylinders = 12;
            d.engine.turbo = torqueScale > 1.2f;   // the Mansory is twin-turbo, the SVJ naturally aspirated
            d.engine.redlineRpm = 8500f;
            d.engine.revLimiterRpm = 8700f;
            d.engine.inertia = 0.24f;
            d.engine.engineBrakeTorqueAtRedline = 90f;
            d.transmission.forwardRatios = new[] { 3.91f, 2.44f, 1.81f, 1.46f, 1.19f, 0.97f, 0.84f };
            d.transmission.finalDrive = 3.4f;
            d.transmission.upshiftRpmLight = 3000f;
            d.transmission.upshiftRpmFull = 8300f;
            d.transmission.downshiftRpm = 2200f;
            d.transmission.shiftTime = 0.08f;
            d.transmission.efficiency = 0.88f;
            d.transmission.clutchMaxTorque = 1100f * torqueScale;
            d.transmission.launchFullEngageRpm = 3500f;
            d.drivetrain.layout = DrivetrainLayout.AllWheelDrive;
            d.drivetrain.awdFrontShare = 0.3f;
            d.drivetrain.differential = DifferentialKind.LimitedSlip;
            d.brakes.frontTorque = 4200f;
            d.brakes.rearTorque = 3000f;
            d.brakes.handbrakeTorque = 2200f;
            d.steering.maxSteerAngle = 32f;
            d.steering.steeringWheelLockDegrees = 400f;
            Springs(d, mass, 1.8f);
            Grip(d, 1.15f);
            d.body.dragCoefficient = 0.36f;
            d.body.frontalArea = 1.95f;
        }

        static void Springs(VehicleDefinition d, float mass, float stiffness)
        {
            float f = mass / 1480f;
            foreach (var s in new[] { d.frontSuspension, d.rearSuspension })
            {
                s.springRate *= f * stiffness;
                s.antiRollStiffness *= f * stiffness;
                s.bumpDamping *= f * Mathf.Sqrt(stiffness);
                s.reboundDamping *= f * Mathf.Sqrt(stiffness);
            }
        }

        static void Grip(VehicleDefinition d, float mu)
        {
            d.frontTires.peakFriction = mu;
            d.rearTires.peakFriction = mu;
        }

        // ------------------------------------------------------------------ entry points

        [MenuItem("MASAR/Build/Imported Cars + Catalog")]
        public static void BuildAll()
        {
            MaterialLibrary.EnsureFolders();
            EnsureFolder(OutRoot);
            EnsureFolder(PrefabRoot);
            EnsureFolder("Assets/Masar/Resources");

            var entries = new List<CarCatalog.Entry>();
            var sedan = AssetDatabase.LoadAssetAtPath<GameObject>(TestCarBuilder.PrefabPath);
            if (sedan != null)
                entries.Add(new CarCatalog.Entry
                {
                    id = "test_sedan", displayName = "MASAR test sedan", prefab = sedan,
                    summary = "Generic sedan · FWD · 2.5 L class · procedural placeholder",
                    credit = "Self-made (TestCarBuilder.cs)", license = "Project-owned",
                    triangles = CountTriangles(sedan), hasInterior = true,
                });

            // -masarCars id1,id2 rebuilds only those cars and reuses the existing prefabs of the others
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-masarCars");
            var only = ai >= 0 && ai + 1 < args.Length ? new HashSet<string>(args[ai + 1].Split(',')) : null;

            foreach (var spec in Specs)
            {
                if (only != null && !only.Contains(spec.Id))
                {
                    var existing = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{spec.Id}.prefab");
                    if (existing != null)
                        entries.Add(new CarCatalog.Entry
                        {
                            id = spec.Id, displayName = spec.Name, summary = spec.Summary, prefab = existing,
                            credit = spec.Credit, license = spec.License, warning = spec.Warning,
                            triangles = CountTriangles(existing),
                            hasInterior = existing.GetComponent<VehicleVisuals>()?.steeringWheel != null,
                        });
                    continue;
                }
                string src = $"{SourceRoot}/{spec.Source}";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(src);
                if (model == null)
                {
                    Debug.Log($"[MASAR] Car {spec.Id}: source not present ({src}), skipped");
                    continue;
                }
                try
                {
                    var e = Build(spec, model);
                    if (e != null) entries.Add(e);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MASAR] Car {spec.Id} failed: {ex}");
                }
            }

            // Opening the test scene for the thumbnails unloads assets, so keep paths and reload afterwards
            var prefabPaths = entries.ConvertAll(e => AssetDatabase.GetAssetPath(e.prefab));
            AssetDatabase.SaveAssets();
            var thumbs = RenderThumbnails(entries, prefabPaths);
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].thumbnail = thumbs[i] != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(thumbs[i]) : null;
                if (prefabPaths[i].StartsWith(PrefabRoot + "/"))
                {
                    entries[i].prefab = null;
                    entries[i].resourcePath = "Cars/" + Path.GetFileNameWithoutExtension(prefabPaths[i]);
                }
                else entries[i].prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[i]);
            }
            if (AssetDatabase.IsValidFolder("Assets/Masar/Prefabs/Cars")) AssetDatabase.DeleteAsset("Assets/Masar/Prefabs/Cars");   // old location

            var catalog = AssetDatabase.LoadAssetAtPath<CarCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CarCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.cars = entries.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MASAR] Car catalog written: {entries.Count} cars ({string.Join(", ", entries.ConvertAll(x => x.id))})");
        }

        // ------------------------------------------------------------------ build one car

        enum Group { Body, Steering, Head, Tail, Reverse, Wheel0, Wheel1, Wheel2, Wheel3, Carrier0, Carrier1, Carrier2, Carrier3 }

        sealed class Part
        {
            public string Path;
            public Material[] Mats;
            public Vector3[] V, N;
            public Vector4[] T;
            public Vector2[] UV;
            public int[][] Sub;
            public string[] Keys;
            public Bounds B;
        }

        struct WheelInfo { public Vector3 C; public float R, HalfW; public bool Found; }

        static CarCatalog.Entry Build(Spec spec, GameObject model)
        {
            string outDir = $"{OutRoot}/{spec.Id}";
            if (AssetDatabase.IsValidFolder(outDir)) AssetDatabase.DeleteAsset(outDir);
            EnsureFolder(outDir);
            EnsureFolder(outDir + "/Meshes");
            EnsureFolder(outDir + "/Materials");
            EnsureFolder(outDir + "/Textures");
            s_MatCache.Clear();
            s_TexCache.Clear();

            var inst = UnityEngine.Object.Instantiate(model);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            List<Part> parts;
            try { parts = CollectParts(inst, spec); }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
            if (parts.Count == 0) { Debug.LogError($"[MASAR] Car {spec.Id}: no meshes"); return null; }

            // ---- orientation and scale. Exporters disagree on up axis and units, so read them from the
            // shape: the longest extent is the length, the shortest is the height.
            var raw = AllBounds(parts);
            Vector3 size = raw.size;
            int lenAxis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            int upAxis = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            if (upAxis == lenAxis) upAxis = 1;
            Vector3 fAxis = Vector3.zero, uAxis = Vector3.zero;
            fAxis[lenAxis] = 1f; uAxis[upAxis] = 1f;
            float length = size[lenAxis];
            // Unit mistakes are powers of ten (cm/m/mm exports); anything else is fitted to a typical car length
            float scale = 1f;
            foreach (float k in new[] { 1f, 0.01f, 100f, 0.1f, 10f, 0.001f, 1000f })
                if (length * k >= 3f && length * k <= 6.5f) { scale = k; break; }
            if (length * scale < 3f || length * scale > 6.5f) scale = 4.6f / length;
            Debug.Log($"[MASAR] Car {spec.Id}: raw bounds {raw.size} at {raw.center}, length axis {lenAxis}, up axis {upAxis}, scale {scale}");
            ApplyTransform(parts, Matrix4x4.TRS(Vector3.zero, Quaternion.Inverse(Quaternion.LookRotation(fAxis, uAxis)), Vector3.one * scale));

            // Upside down? The tires (or the auto-detected wheels) are in the lower half.
            if (UpsideDown(parts, spec)) ApplyTransform(parts, Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 180f)));

            // Front = where the headlights are (or away from the tail lights)
            float front = FrontSign(parts, spec);
            if (front < 0f) ApplyTransform(parts, Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f)));

            // ---- wheels
            var all = AllBounds(parts);
            var wheels = spec.Tire != null ? WheelsFromPattern(parts, spec, all) : WheelsAuto(parts, all);
            // Some models are built at the wrong size: fit the measured wheelbase to the real car's
            if (spec.Wheelbase > 0f && wheels[0].Found && wheels[2].Found)
            {
                float k = spec.Wheelbase / Mathf.Abs(wheels[0].C.z - wheels[2].C.z);
                Debug.Log($"[MASAR] Car {spec.Id}: wheelbase {Mathf.Abs(wheels[0].C.z - wheels[2].C.z):F3} m, scaled by {k:F3} to {spec.Wheelbase} m");
                ApplyTransform(parts, Matrix4x4.Scale(Vector3.one * k));
                all = AllBounds(parts);
                wheels = spec.Tire != null ? WheelsFromPattern(parts, spec, all) : WheelsAuto(parts, all);
            }
            for (int i = 0; i < 4; i++)
                if (!wheels[i].Found)
                {
                    Debug.LogError($"[MASAR] Car {spec.Id}: wheel {i} not found. Bounds {all.size} at {all.center}. Largest parts:");
                    parts.Sort((x, y) => y.B.size.sqrMagnitude.CompareTo(x.B.size.sqrMagnitude));
                    for (int k = 0; k < Mathf.Min(25, parts.Count); k++)
                        Debug.Log($"[MASAR]   part '{parts[k].Path}' size {parts[k].B.size} center {parts[k].B.center} keys {string.Join(";", parts[k].Keys)}");
                    return null;
                }
            float groundY = 0f;
            for (int i = 0; i < 4; i++) groundY += (wheels[i].C.y - wheels[i].R) * 0.25f;
            var offset = new Vector3(
                -(wheels[0].C.x + wheels[1].C.x + wheels[2].C.x + wheels[3].C.x) * 0.25f,
                -groundY,
                -(wheels[0].C.z + wheels[1].C.z + wheels[2].C.z + wheels[3].C.z) * 0.25f);
            ApplyTransform(parts, Matrix4x4.Translate(offset));
            for (int i = 0; i < 4; i++) wheels[i].C += offset;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-masarCarDebug") >= 0)
            {
                var sorted = new List<Part>(parts);
                sorted.Sort((x, y) => y.B.size.sqrMagnitude.CompareTo(x.B.size.sqrMagnitude));
                foreach (var p in sorted)
                {
                    int t = 0; foreach (var s in p.Sub) t += s.Length / 3;
                    Debug.Log($"[MASAR]   part '{p.Path}' tris {t} min {p.B.min} max {p.B.max} mats {string.Join(",", Array.ConvertAll(p.Mats, m => m != null ? m.name : "-"))}");
                }
            }
            // Symmetrize: one track per axle, one height and radius per axle
            for (int axle = 0; axle < 2; axle++)
            {
                int l = axle * 2, r = l + 1;
                float hx = (Mathf.Abs(wheels[l].C.x) + Mathf.Abs(wheels[r].C.x)) * 0.5f;
                float z = (wheels[l].C.z + wheels[r].C.z) * 0.5f;
                float rad = (wheels[l].R + wheels[r].R) * 0.5f;
                float hw = (wheels[l].HalfW + wheels[r].HalfW) * 0.5f;
                wheels[l] = new WheelInfo { C = new Vector3(-hx, rad, z), R = rad, HalfW = hw, Found = true };
                wheels[r] = new WheelInfo { C = new Vector3(hx, rad, z), R = rad, HalfW = hw, Found = true };
            }

            // ---- regroup triangles
            var groups = new Dictionary<Group, GroupBuilder>();
            GroupBuilder G(Group g) { if (!groups.TryGetValue(g, out var b)) groups[g] = b = new GroupBuilder(); return b; }
            var reDrop = Rx(spec.Drop); var reSteer = Rx(spec.Steering); var reCarrier = Rx(spec.Carrier);
            var reHead = Rx(spec.Head); var reTail = Rx(spec.Tail); var reRev = Rx(spec.Reverse); var rePos = Rx(spec.Position);
            var rePaint = Rx(spec.Paint); var reGlass = Rx(spec.Glass);
            int dropped = 0;
            // Lamp meshes sometimes carry fake light-beam cards far outside the car: keep lamp
            // triangles only inside the bounds of everything that isn't a lamp
            bool IsLampKey(string k) => Match(reHead, k) || Match(reTail, k) || Match(reRev, k) || Match(rePos, k);
            Bounds core = default; bool coreSet = false;
            foreach (var p in parts)
                for (int s = 0; s < p.Sub.Length; s++)
                {
                    if (IsLampKey(p.Keys[s]) || Match(reDrop, p.Keys[s])) continue;
                    if (!coreSet) { core = p.B; coreSet = true; } else core.Encapsulate(p.B);
                }
            core.Expand(0.06f);
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var p = parts[pi];
                for (int s = 0; s < p.Sub.Length; s++)
                {
                    string key = p.Keys[s];
                    var srcMat = p.Mats[Mathf.Min(s, p.Mats.Length - 1)];
                    if (srcMat == null) continue;
                    if (Match(reDrop, key)) { dropped += p.Sub[s].Length / 3; continue; }
                    bool steer = Match(reSteer, key), carrier = Match(reCarrier, key);
                    bool head = Match(reHead, key), tail = Match(reTail, key), rev = Match(reRev, key), pos = Match(rePos, key);
                    if (spec.AutoLamps && !head && !tail && !rev && IsEmissive(srcMat))
                    {
                        if (IsReddish(srcMat)) tail = true; else pos = true;
                    }
                    var role = Match(reGlass, key) ? Role.Glass : Match(rePaint, key) ? Role.Paint : Role.Normal;
                    Material matMain = null, matLamp = null;
                    var subTris = p.Sub[s];
                    for (int t = 0; t + 2 < subTris.Length; t += 3)
                    {
                        int a = subTris[t], b = subTris[t + 1], c = subTris[t + 2];
                        Group g;
                        int w = steer ? -1 : InsideWheel(wheels, p.V[a], p.V[b], p.V[c]);
                        if (steer) g = Group.Steering;
                        else if (w >= 0) g = (carrier ? Group.Carrier0 : Group.Wheel0) + w;
                        else if (tail) g = Group.Tail;
                        else if (head) g = Group.Head;
                        else if (rev) g = Group.Reverse;
                        else if (pos) g = (p.V[a].z + p.V[b].z + p.V[c].z) > 0f ? Group.Head : Group.Tail;
                        else g = Group.Body;
                        // ...and drop big flare sprites (real lamp surfaces are made of small triangles)
                        if ((g == Group.Head || g == Group.Tail || g == Group.Reverse) &&
                            (!(core.Contains(p.V[a]) && core.Contains(p.V[b]) && core.Contains(p.V[c])) ||
                             Vector3.Cross(p.V[b] - p.V[a], p.V[c] - p.V[a]).magnitude * 0.5f > 0.03f))
                        { dropped++; continue; }
                        bool lamp = (g == Group.Head || g == Group.Tail || g == Group.Reverse) && role == Role.Normal;
                        var mat = lamp ? (matLamp ??= Convert(spec.Id, srcMat, Role.Lamp)) : (matMain ??= Convert(spec.Id, srcMat, role));
                        G(g).AddTri(p, pi, a, b, c, mat);
                    }
                }
            }

            // ---- definition
            var def = BuildDefinition(spec, wheels, $"{outDir}/{spec.Id}_Definition.asset");

            // ---- prefab
            var root = new GameObject("Car_" + spec.Id);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = def.body.massKg;
            var controller = root.AddComponent<VehicleController>();
            var so = new SerializedObject(controller);
            so.FindProperty("definition").objectReferenceValue = def;
            so.ApplyModifiedPropertiesWithoutUndo();
            var visuals = root.AddComponent<VehicleVisuals>();

            var bodyBounds = groups.TryGetValue(Group.Body, out var bodyG) ? bodyG.Bounds() : all;
            // meshOrigin: the car-space point that becomes the mesh's (0,0,0); the object sits at its parent's origin
            Renderer Make(Group g, Transform parent, string name, Vector3 meshOrigin)
            {
                if (!groups.TryGetValue(g, out var gb) || gb.TriangleCount == 0) return null;
                var mesh = gb.Build(spec.Id + "_" + name, meshOrigin, Quaternion.identity);
                AssetDatabase.CreateAsset(mesh, $"{outDir}/Meshes/{name}.asset");
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = gb.Materials.ToArray();
                return mr;
            }

            Make(Group.Body, root.transform, "Body", Vector3.zero);
            var headR = Make(Group.Head, root.transform, "Lamps_Head", Vector3.zero);
            var tailR = Make(Group.Tail, root.transform, "Lamps_Tail", Vector3.zero);
            var revR = Make(Group.Reverse, root.transform, "Lamps_Reverse", Vector3.zero);

            string[] wn = { "FL", "FR", "RL", "RR" };
            var wheelPivots = new Transform[4];
            var carriers = new Transform[4];
            bool anyCarrier = false;
            for (int i = 0; i < 4; i++)
            {
                var pivot = new GameObject("Wheel_" + wn[i]).transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = wheels[i].C;
                wheelPivots[i] = pivot;
                Make(Group.Wheel0 + i, pivot, "Wheel_" + wn[i] + "_Mesh", wheels[i].C);
                if (groups.ContainsKey(Group.Carrier0 + i))
                {
                    var cp = new GameObject("Carrier_" + wn[i]).transform;
                    cp.SetParent(root.transform, false);
                    cp.localPosition = wheels[i].C;
                    carriers[i] = cp;
                    anyCarrier = true;
                    Make(Group.Carrier0 + i, cp, "Carrier_" + wn[i] + "_Mesh", wheels[i].C);
                }
            }

            // ---- steering wheel: column (fixed tilt) -> pivot (rotates about local Z) -> mesh
            Transform steeringPivot = null;
            Vector3 steeringCenter = Vector3.zero;
            if (groups.TryGetValue(Group.Steering, out var sg) && sg.TriangleCount > 0)
            {
                steeringCenter = sg.Bounds().center;
                var axis = sg.ThinnestAxis();
                if (axis.z > 0f) axis = -axis;          // column axis points back toward the driver
                var column = new GameObject("SteeringColumn").transform;
                column.SetParent(root.transform, false);
                column.localPosition = steeringCenter;
                column.localRotation = Quaternion.LookRotation(axis, Vector3.up);
                steeringPivot = new GameObject("SteeringWheel").transform;
                steeringPivot.SetParent(column, false);
                var mesh = sg.Build(spec.Id + "_SteeringWheel", steeringCenter, column.localRotation);
                AssetDatabase.CreateAsset(mesh, $"{outDir}/Meshes/SteeringWheel.asset");
                var go = new GameObject("SteeringWheel_Mesh");
                go.transform.SetParent(steeringPivot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = sg.Materials.ToArray();
            }

            // ---- colliders
            float top = bodyBounds.max.y;
            float halfW = Mathf.Min(bodyBounds.extents.x, wheels[1].C.x + wheels[1].HalfW + 0.06f);
            float bottom = Mathf.Max(0.12f, wheels[0].R * 0.45f);
            float belt = bottom + (top - bottom) * 0.55f;
            var bodyCol = root.AddComponent<BoxCollider>();
            bodyCol.center = new Vector3(0f, (bottom + belt) * 0.5f, bodyBounds.center.z);
            bodyCol.size = new Vector3(halfW * 2f * 0.97f, belt - bottom, bodyBounds.size.z * 0.97f);
            float cabinZ = groups.TryGetValue(Group.Body, out _) ? GlassCenterZ(groups, bodyBounds.center.z - 0.1f) : 0f;
            var cabin = new GameObject("CabinCollider").AddComponent<BoxCollider>();
            cabin.transform.SetParent(root.transform, false);
            cabin.center = new Vector3(0f, (belt + top * 0.97f) * 0.5f, cabinZ);
            cabin.size = new Vector3(halfW * 2f * 0.8f, top * 0.97f - belt, bodyBounds.size.z * 0.42f);

            // ---- headlight beams
            var spots = new List<Light>();
            Bounds hb = headR != null ? headR.bounds : new Bounds(new Vector3(0f, belt * 0.8f, bodyBounds.max.z - 0.1f), new Vector3(halfW * 1.4f, 0.1f, 0.1f));
            foreach (float sx in new[] { -1f, 1f })
            {
                var lgo = new GameObject(sx < 0 ? "HeadlightBeam_L" : "HeadlightBeam_R");
                lgo.transform.SetParent(root.transform, false);
                lgo.transform.localPosition = new Vector3(sx * Mathf.Max(0.3f, hb.extents.x * 0.7f), hb.center.y, hb.max.z + 0.05f);
                lgo.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.range = 60f; l.spotAngle = 70f; l.innerSpotAngle = 30f; l.intensity = 30f;
                l.color = new Color(1f, 0.96f, 0.9f);
                l.shadows = LightShadows.None;
                spots.Add(l);
            }

            // ---- camera anchors
            var anchors = root.AddComponent<VehicleCameraAnchors>();
            Vector3 eye = steeringPivot != null
                ? new Vector3(steeringCenter.x, Mathf.Min(steeringCenter.y + 0.24f, top - 0.12f), steeringCenter.z - 0.46f)
                // No steering wheel to anchor on: a typical seating position, about 1.6 m behind the front axle
                : new Vector3(-wheels[1].C.x * 0.47f, Mathf.Min(top - 0.25f, belt + 0.42f), wheels[0].C.z - 1.6f);
            anchors.driverEye = Anchor(root.transform, "Cam_DriverEye", eye);
            anchors.passengerEye = Anchor(root.transform, "Cam_PassengerEye", new Vector3(-eye.x, eye.y - 0.02f, eye.z));
            anchors.hood = Anchor(root.transform, "Cam_Hood", new Vector3(0f, top * 0.745f, wheels[0].C.z * 0.92f));
            anchors.lookTarget = Anchor(root.transform, "Cam_LookTarget", new Vector3(0f, top * 0.69f, 0.2f));

            visuals.wheels = wheelPivots;
            visuals.wheelCarriers = anyCarrier ? carriers : new Transform[0];
            visuals.steeringWheel = steeringPivot;
            visuals.headlightRenderers = headR != null ? new[] { headR } : new Renderer[0];
            visuals.brakeLightRenderers = tailR != null ? new[] { tailR } : new Renderer[0];
            visuals.reverseLightRenderers = revR != null ? new[] { revR } : new Renderer[0];
            visuals.headlights = spots.ToArray();

            string prefabPath = $"{PrefabRoot}/{spec.Id}.prefab";
            bool hasSteer = steeringPivot != null;
            string lamps = $"{(headR != null ? 1 : 0)}/{(tailR != null ? 1 : 0)}/{(revR != null ? 1 : 0)}";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);

            int tris = 0, draws = 0;
            var summary = new System.Text.StringBuilder();
            foreach (var kv in groups)
            {
                tris += kv.Value.TriangleCount;
                draws += kv.Value.Materials.Count;
                summary.Append($" {kv.Key}={kv.Value.TriangleCount}/{kv.Value.Materials.Count}m");
            }
            Debug.Log($"[MASAR] Car {spec.Id}: length {bodyBounds.size.z:F2} m, width {bodyBounds.size.x:F2}, height {top:F2}; " +
                      $"wheelbase {wheels[0].C.z - wheels[2].C.z:F3}, track F {wheels[1].C.x * 2f:F3} R {wheels[3].C.x * 2f:F3}, " +
                      $"tire R F {wheels[0].R:F3} R {wheels[2].R:F3}; {tris:N0} tris, {draws} draws, {s_MatCache.Count} materials, " +
                      $"{s_TexCache.Count} textures, dropped {dropped}; steering {(hasSteer ? "yes" : "no")}, " +
                      $"lamps H/T/R {lamps};{summary}");

            return new CarCatalog.Entry
            {
                id = spec.Id, displayName = spec.Name, summary = spec.Summary, prefab = prefab,
                credit = spec.Credit, license = spec.License, warning = spec.Warning,
                triangles = tris, hasInterior = hasSteer,
            };
        }

        static float GlassCenterZ(Dictionary<Group, GroupBuilder> groups, float fallback)
        {
            if (groups.TryGetValue(Group.Body, out var b))
            {
                var gz = b.CenterZOfMaterials(m => m != null && m.name.Contains("_Glass"));
                if (gz.HasValue) return gz.Value;
            }
            return fallback;
        }

        static Transform Anchor(Transform parent, string name, Vector3 p)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = p;
            return t;
        }

        // ------------------------------------------------------------------ definition

        static VehicleDefinition BuildDefinition(Spec spec, WheelInfo[] w, string path)
        {
            var def = ScriptableObject.CreateInstance<VehicleDefinition>();
            def.displayName = spec.Name;
            def.notes = "Geometry (wheelbase, track, tire radii) measured from the 3D model. Engine, gearing, mass and " +
                        "chassis values are class-typical estimates for a driving simulator, not manufacturer data.";
            spec.Tune?.Invoke(def);
            float mass = def.body.massKg;
            float rf = w[0].R, rr = w[2].R;
            def.body.wheelbase = w[0].C.z - w[2].C.z;
            def.body.trackFront = w[1].C.x * 2f;
            def.body.trackRear = w[3].C.x * 2f;
            def.body.axleCenterOffset = 0f;
            def.body.centerOfMass = new Vector3(0f, spec.ComHeight, (spec.FrontShare - 0.5f) * def.body.wheelbase);
            def.frontTires.radius = rf;
            def.rearTires.radius = rr;
            def.frontTires.width = Mathf.Clamp(w[0].HalfW * 2f, 0.17f, 0.40f);
            def.rearTires.width = Mathf.Clamp(w[2].HalfW * 2f, 0.17f, 0.40f);
            def.frontTires.inertia *= (rf * rf) / (0.325f * 0.325f) * def.frontTires.width / 0.215f;
            def.rearTires.inertia *= (rr * rr) / (0.325f * 0.325f) * def.rearTires.width / 0.215f;
            // At rest the chassis origin sits on the ground and each wheel center is one radius up:
            //   mount - (restLength - staticCompression) = radius, per axle.
            const float g = 9.81f;
            float cf = mass * g * spec.FrontShare * 0.5f / def.frontSuspension.springRate;
            float cr = mass * g * (1f - spec.FrontShare) * 0.5f / def.rearSuspension.springRate;
            def.body.mountHeight = rf + def.frontSuspension.restLength - cf;
            def.rearSuspension.restLength = def.body.mountHeight - rr + cr;
            AssetDatabase.CreateAsset(def, path);
            return def;
        }

        // ------------------------------------------------------------------ parts

        static List<Part> CollectParts(GameObject inst, Spec spec)
        {
            var parts = new List<Part>();
            var root = inst.transform;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(false))
            {
                Mesh mesh = null;
                Matrix4x4 m = r.transform.localToWorldMatrix;
                if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }
                else if (r is SkinnedMeshRenderer smr)
                {
                    mesh = new Mesh();
                    smr.BakeMesh(mesh, true);
                }
                if (mesh == null || mesh.vertexCount == 0) continue;

                var p = new Part { Path = PathOf(r.transform, root), Mats = r.sharedMaterials };
                var v = mesh.vertices; var n = mesh.normals; var t = mesh.tangents; var uv = mesh.uv;
                if (n == null || n.Length != v.Length) { mesh = UnityEngine.Object.Instantiate(mesh); mesh.RecalculateNormals(); n = mesh.normals; }
                bool flip = m.determinant < 0f;
                var nm = m.inverse.transpose;
                p.V = new Vector3[v.Length]; p.N = new Vector3[v.Length];
                p.T = t != null && t.Length == v.Length ? new Vector4[v.Length] : null;
                for (int i = 0; i < v.Length; i++)
                {
                    p.V[i] = m.MultiplyPoint3x4(v[i]);
                    p.N[i] = nm.MultiplyVector(n[i]).normalized;
                    if (p.T != null)
                    {
                        var d = m.MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized;
                        p.T[i] = new Vector4(d.x, d.y, d.z, flip ? -t[i].w : t[i].w);
                    }
                }
                p.UV = uv != null && uv.Length == v.Length ? uv : new Vector2[v.Length];
                p.Sub = new int[mesh.subMeshCount][];
                p.Keys = new string[mesh.subMeshCount];
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (mesh.GetTopology(s) != MeshTopology.Triangles) { p.Sub[s] = new int[0]; p.Keys[s] = ""; continue; }
                    var tris = mesh.GetTriangles(s);
                    if (flip) for (int k = 0; k + 2 < tris.Length; k += 3) { int x = tris[k + 1]; tris[k + 1] = tris[k + 2]; tris[k + 2] = x; }
                    p.Sub[s] = tris;
                    var mat = p.Mats.Length > 0 ? p.Mats[Mathf.Min(s, p.Mats.Length - 1)] : null;
                    p.Keys[s] = p.Path + "|" + (mat != null ? mat.name : "");
                }
                p.B = Bounds(p.V);
                // Drop flat ground / shadow planes (any axis: the up axis isn't known yet). They would
                // wreck the size and orientation estimate.
                int triCount = 0; foreach (var st in p.Sub) triCount += st.Length / 3;
                var sz = p.B.size;
                float minExt = Mathf.Min(sz.x, Mathf.Min(sz.y, sz.z)), maxExt = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z));
                if (minExt < 0.02f * maxExt && maxExt > 2.5f && triCount <= 32)
                {
                    Debug.Log($"[MASAR] Car {spec.Id}: dropped ground plane '{p.Path}'");
                    continue;
                }
                parts.Add(p);
            }
            return parts;
        }

        static string PathOf(Transform t, Transform root)
        {
            var s = t.name;
            while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        static void ApplyTransform(List<Part> parts, Matrix4x4 m)
        {
            var rot = m.rotation;
            foreach (var p in parts)
            {
                for (int i = 0; i < p.V.Length; i++)
                {
                    p.V[i] = m.MultiplyPoint3x4(p.V[i]);
                    p.N[i] = rot * p.N[i];
                    if (p.T != null) { var d = rot * new Vector3(p.T[i].x, p.T[i].y, p.T[i].z); p.T[i] = new Vector4(d.x, d.y, d.z, p.T[i].w); }
                }
                p.B = Bounds(p.V);
            }
        }

        static Bounds Bounds(Vector3[] v)
        {
            if (v.Length == 0) return new Bounds();
            Vector3 mn = v[0], mx = v[0];
            for (int i = 1; i < v.Length; i++) { mn = Vector3.Min(mn, v[i]); mx = Vector3.Max(mx, v[i]); }
            var b = new Bounds(); b.SetMinMax(mn, mx); return b;
        }

        static Bounds AllBounds(List<Part> parts)
        {
            var b = parts[0].B;
            foreach (var p in parts) b.Encapsulate(p.B);
            return b;
        }

        static Regex Rx(string pattern) => string.IsNullOrEmpty(pattern) ? null : new Regex(pattern, RegexOptions.CultureInvariant);
        static bool Match(Regex r, string s) => r != null && r.IsMatch(s);

        static bool UpsideDown(List<Part> parts, Spec spec)
        {
            var all = AllBounds(parts);
            var tire = Rx(spec.Tire);
            double y = 0; long n = 0;
            if (tire != null)
            {
                foreach (var p in parts)
                    for (int s = 0; s < p.Keys.Length; s++)
                        if (Match(tire, p.Keys[s])) foreach (int vi in p.Sub[s]) { y += p.V[vi].y; n++; }
            }
            else
            {
                // Wheel-sized cylinders sit near the bottom of a car; try the auto detector the right way up first
                int up = CountWheelCandidates(parts, all, false), down = CountWheelCandidates(parts, all, true);
                Debug.Log($"[MASAR] up-axis check: wheel-like parts at bottom {up}, at top {down}");
                return down > up;
            }
            return n > 0 && y / n > all.center.y;
        }

        static int CountWheelCandidates(List<Part> parts, Bounds all, bool atTop)
        {
            int n = 0;
            foreach (var p in parts)
            {
                var e = p.B.extents;
                float r = Mathf.Max(e.y, e.z);
                if (r < 0.22f || r > 0.52f || Mathf.Abs(e.y - e.z) > 0.12f * r || e.x < 0.05f || e.x > 0.25f) continue;
                if (atTop ? p.B.max.y >= all.max.y - 0.12f : p.B.min.y <= all.min.y + 0.12f) n++;
            }
            return n;
        }

        static float FrontSign(List<Part> parts, Spec spec)
        {
            var head = Rx(spec.Head); var tail = Rx(spec.Tail);
            var hint = Rx(spec.FrontHint);
            if (hint != null)
            {
                float fz = 0f; int fn = 0;
                foreach (var p in parts)
                    for (int s = 0; s < p.Keys.Length; s++)
                        if (Match(hint, p.Keys[s])) { fz += p.B.center.z; fn++; }
                if (fn > 0) return Mathf.Sign(fz / fn - AllBounds(parts).center.z);
            }
            float hz = 0f, tz = 0f; int hn = 0, tn = 0;
            foreach (var p in parts)
                for (int s = 0; s < p.Keys.Length; s++)
                {
                    if (Match(head, p.Keys[s])) { hz += p.B.center.z; hn++; }
                    if (Match(tail, p.Keys[s])) { tz += p.B.center.z; tn++; }
                }
            if (hn > 0 && tn > 0) return Mathf.Sign(hz / hn - tz / tn);
            if (hn > 0) return Mathf.Sign(hz / hn - AllBounds(parts).center.z);
            if (tn > 0) return -Mathf.Sign(tz / tn - AllBounds(parts).center.z);
            // Fall back on red emissive materials (tail lamps) or the glTF convention (+Z forward)
            float rz = 0f; int rn = 0;
            foreach (var p in parts)
                foreach (var m in p.Mats)
                    if (m != null && IsEmissive(m) && IsReddish(m)) { rz += p.B.center.z; rn++; }
            if (rn > 0) return -Mathf.Sign(rz / rn - AllBounds(parts).center.z);
            return 1f;
        }

        // ------------------------------------------------------------------ wheel detection

        static int Corner(Vector3 c, Bounds all) => (c.z > all.center.z ? 0 : 2) + (c.x > all.center.x ? 1 : 0);

        static WheelInfo[] WheelsFromPattern(List<Part> parts, Spec spec, Bounds all)
        {
            var re = Rx(spec.Tire);
            var mn = new Vector3[4]; var mx = new Vector3[4]; var found = new bool[4];
            foreach (var p in parts)
                for (int s = 0; s < p.Sub.Length; s++)
                {
                    if (!Match(re, p.Keys[s])) continue;
                    foreach (int vi in p.Sub[s])
                    {
                        var v = p.V[vi];
                        // Each tire vertex votes for its own corner (models sometimes merge all four tires)
                        int k = Corner(v, all);
                        if (!found[k]) { mn[k] = mx[k] = v; found[k] = true; }
                        else { mn[k] = Vector3.Min(mn[k], v); mx[k] = Vector3.Max(mx[k], v); }
                    }
                }
            var w = new WheelInfo[4];
            for (int k = 0; k < 4; k++)
            {
                Debug.Log($"[MASAR] tire corner {k}: {(found[k] ? $"min {mn[k]} max {mx[k]}" : "none")}");
                if (!found[k]) continue;
                var c = (mn[k] + mx[k]) * 0.5f; var e = (mx[k] - mn[k]) * 0.5f;
                w[k] = new WheelInfo { C = c, R = Mathf.Max(e.y, e.z), HalfW = e.x, Found = true };
            }
            return w;
        }

        /// <summary>Finds cylinder-shaped, wheel-sized parts touching the ground near each corner.</summary>
        static WheelInfo[] WheelsAuto(List<Part> parts, Bounds all)
        {
            var w = new WheelInfo[4];
            float minY = all.min.y;
            foreach (var p in parts)
            {
                var e = p.B.extents;
                float r = Mathf.Max(e.y, e.z);
                if (r < 0.22f || r > 0.52f) continue;
                if (Mathf.Abs(e.y - e.z) > 0.12f * r) continue;
                if (e.x < 0.05f || e.x > 0.25f) continue;
                if (p.B.min.y > minY + 0.12f) continue;
                if (Mathf.Abs(p.B.center.x - all.center.x) < 0.4f) continue;
                int k = Corner(p.B.center, all);
                if (!w[k].Found || r > w[k].R)
                    w[k] = new WheelInfo { C = p.B.center, R = r, HalfW = e.x, Found = true };
            }
            // Mirror a missing side from the other one (many models instance the right wheels)
            for (int k = 0; k < 4; k++)
            {
                int o = k ^ 1;
                if (!w[k].Found && w[o].Found)
                {
                    var c = w[o].C; c.x = 2f * all.center.x - c.x;
                    w[k] = new WheelInfo { C = c, R = w[o].R, HalfW = w[o].HalfW, Found = true };
                }
            }
            for (int k = 0; k < 4; k++)
                if (w[k].Found) Debug.Log($"[MASAR] auto wheel {k}: center {w[k].C}, r {w[k].R:F3}, half width {w[k].HalfW:F3}");
            return w;
        }

        static int InsideWheel(WheelInfo[] w, Vector3 a, Vector3 b, Vector3 c)
        {
            for (int k = 0; k < 4; k++)
                if (In(w[k], a) && In(w[k], b) && In(w[k], c)) return k;
            return -1;
        }

        static bool In(WheelInfo w, Vector3 p)
        {
            if (Mathf.Abs(p.x - w.C.x) > w.HalfW + 0.03f) return false;
            float dy = p.y - w.C.y, dz = p.z - w.C.z;
            float r = w.R * 1.015f;
            return dy * dy + dz * dz <= r * r;
        }

        // ------------------------------------------------------------------ group mesh builder

        sealed class GroupBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
            readonly List<Vector4> _t = new List<Vector4>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly Dictionary<long, int> _map = new Dictionary<long, int>();
            readonly Dictionary<Material, List<int>> _sub = new Dictionary<Material, List<int>>();
            public readonly List<Material> Materials = new List<Material>();
            bool _missingTangents;
            public int TriangleCount { get; private set; }

            public void AddTri(Part p, int pi, int a, int b, int c, Material m)
            {
                if (!_sub.TryGetValue(m, out var list)) { _sub[m] = list = new List<int>(); Materials.Add(m); }
                list.Add(Index(p, pi, a)); list.Add(Index(p, pi, b)); list.Add(Index(p, pi, c));
                TriangleCount++;
            }

            int Index(Part p, int pi, int vi)
            {
                long key = ((long)pi << 32) | (uint)vi;
                if (_map.TryGetValue(key, out int idx)) return idx;
                idx = _v.Count;
                _v.Add(p.V[vi]); _n.Add(p.N[vi]); _uv.Add(p.UV[vi]);
                if (p.T != null) _t.Add(p.T[vi]); else { _t.Add(new Vector4(1, 0, 0, 1)); _missingTangents = true; }
                _map[key] = idx;
                return idx;
            }

            public Bounds Bounds()
            {
                if (_v.Count == 0) return new Bounds();
                Vector3 mn = _v[0], mx = _v[0];
                foreach (var v in _v) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
                var b = new Bounds(); b.SetMinMax(mn, mx); return b;
            }

            public float? CenterZOfMaterials(Func<Material, bool> pick)
            {
                double z = 0; int n = 0;
                foreach (var kv in _sub)
                {
                    if (!pick(kv.Key)) continue;
                    foreach (int i in kv.Value) { z += _v[i].z; n++; }
                }
                return n > 0 ? (float)(z / n) : (float?)null;
            }

            /// <summary>Direction of least spread (the steering wheel's column axis), by Jacobi eigen-decomposition.</summary>
            public Vector3 ThinnestAxis()
            {
                var c = Bounds().center;
                double xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
                foreach (var v in _v)
                {
                    var d = v - c;
                    xx += d.x * d.x; yy += d.y * d.y; zz += d.z * d.z; xy += d.x * d.y; xz += d.x * d.z; yz += d.y * d.z;
                }
                var a = new double[3, 3] { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } };
                var vec = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
                for (int sweep = 0; sweep < 50; sweep++)
                {
                    int p = 0, q = 1; double max = Math.Abs(a[0, 1]);
                    if (Math.Abs(a[0, 2]) > max) { p = 0; q = 2; max = Math.Abs(a[0, 2]); }
                    if (Math.Abs(a[1, 2]) > max) { p = 1; q = 2; max = Math.Abs(a[1, 2]); }
                    if (max < 1e-12) break;
                    double th = 0.5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
                    double cs = Math.Cos(th), sn = Math.Sin(th);
                    for (int k = 0; k < 3; k++)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = cs * akp - sn * akq; a[k, q] = sn * akp + cs * akq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double apk = a[p, k], aqk = a[q, k];
                        a[p, k] = cs * apk - sn * aqk; a[q, k] = sn * apk + cs * aqk;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = vec[k, p], vkq = vec[k, q];
                        vec[k, p] = cs * vkp - sn * vkq; vec[k, q] = sn * vkp + cs * vkq;
                    }
                }
                int min = 0;
                for (int i = 1; i < 3; i++) if (a[i, i] < a[min, min]) min = i;
                return new Vector3((float)vec[0, min], (float)vec[1, min], (float)vec[2, min]).normalized;
            }

            /// <summary>Builds the mesh in the local space of a pivot at <paramref name="origin"/> with rotation <paramref name="rot"/>.</summary>
            public Mesh Build(string name, Vector3 origin, Quaternion rot)
            {
                var inv = Quaternion.Inverse(rot);
                var v = new Vector3[_v.Count]; var n = new Vector3[_v.Count]; var t = new Vector4[_v.Count];
                for (int i = 0; i < v.Length; i++)
                {
                    v[i] = inv * (_v[i] - origin);
                    n[i] = inv * _n[i];
                    var d = inv * new Vector3(_t[i].x, _t[i].y, _t[i].z);
                    t[i] = new Vector4(d.x, d.y, d.z, _t[i].w);
                }
                var mesh = new Mesh { name = name };
                if (v.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices = v;
                mesh.normals = n;
                mesh.uv = _uv.ToArray();
                mesh.tangents = t;
                mesh.subMeshCount = Materials.Count;
                for (int s = 0; s < Materials.Count; s++) mesh.SetTriangles(_sub[Materials[s]], s, false);
                if (_missingTangents) mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                return mesh;
            }
        }

        // ------------------------------------------------------------------ materials

        enum Role { Normal, Paint, Glass, Lamp }

        static readonly Dictionary<(int, Role), Material> s_MatCache = new Dictionary<(int, Role), Material>();
        static readonly Dictionary<string, Texture2D> s_TexCache = new Dictionary<string, Texture2D>();

        static bool Has(Material m, string p) => m.HasProperty(p);
        static Texture Tex(Material m, params string[] names) { foreach (var n in names) if (m.HasProperty(n) && m.GetTexture(n) != null) return m.GetTexture(n); return null; }
        static Color Col(Material m, Color def, params string[] names) { foreach (var n in names) if (m.HasProperty(n)) return m.GetColor(n); return def; }
        static float Num(Material m, float def, params string[] names) { foreach (var n in names) if (m.HasProperty(n)) return m.GetFloat(n); return def; }

        static bool IsEmissive(Material m)
        {
            var e = Col(m, Color.black, "emissiveFactor", "_EmissionColor");
            return e.maxColorComponent > 0.05f;
        }

        static bool IsReddish(Material m)
        {
            var e = Col(m, Color.black, "emissiveFactor", "_EmissionColor");
            var b = Col(m, Color.white, "baseColorFactor", "_BaseColor");
            var c = e.maxColorComponent > 0.05f ? e : b;
            return c.r > 0.3f && c.r > c.g * 2f && c.r > c.b * 2f;
        }

        static bool IsTransparent(Material m) =>
            m.renderQueue >= 2900 || m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
            (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f);

        static bool IsCutout(Material m) => m.IsKeywordEnabled("_ALPHATEST_ON") || (m.renderQueue >= 2400 && m.renderQueue < 2900);

        static bool IsDoubleSided(Material m) =>
            (m.HasProperty("_DoubleSidedEnable") && m.GetFloat("_DoubleSidedEnable") > 0.5f) ||
            (m.HasProperty("_Cull") && m.GetFloat("_Cull") < 0.5f) ||
            (m.HasProperty("_CullMode") && m.GetFloat("_CullMode") < 0.5f);

        static Material Convert(string carId, Material src, Role role)
        {
            var key = (src.GetInstanceID(), role);
            if (s_MatCache.TryGetValue(key, out var cached)) return cached;

            var baseColor = Col(src, Color.white, "baseColorFactor", "_BaseColor", "_Color");
            var baseTex = Tex(src, "baseColorTexture", "_BaseMap", "_MainTex");
            float metallic = Num(src, 0f, "metallicFactor", "_Metallic");
            float roughness = Num(src, 0.5f, "roughnessFactor");
            if (!src.HasProperty("roughnessFactor") && src.HasProperty("_Smoothness")) roughness = 1f - src.GetFloat("_Smoothness");
            var mrTex = Tex(src, "metallicRoughnessTexture");
            var nTex = Tex(src, "normalTexture", "_BumpMap");
            float nScale = Num(src, 1f, "normalTexture_scale", "_BumpScale");
            var oTex = Tex(src, "occlusionTexture");
            var emis = Col(src, Color.black, "emissiveFactor", "_EmissionColor");
            var eTex = Tex(src, "emissiveTexture", "_EmissionMap");
            bool transparent = IsTransparent(src), cutout = !transparent && IsCutout(src);
            float cutoff = Num(src, 0.5f, "alphaCutoff", "_Cutoff");
            bool doubleSided = IsDoubleSided(src);
            // Untextured transparent surfaces are glass, whatever the model calls them
            if (role == Role.Normal && transparent && baseTex == null) role = Role.Glass;

            string safe = Regex.Replace(src.name, @"[^A-Za-z0-9_\-]+", "_");
            if (safe.Length > 48) safe = safe.Substring(0, 48);
            string path = $"{OutRoot}/{carId}/Materials/{safe}_{role}_{s_MatCache.Count}.mat";
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader complex = Shader.Find("Universal Render Pipeline/Complex Lit");
            var m = new Material(role == Role.Paint && complex != null ? complex : lit) { name = Path.GetFileNameWithoutExtension(path) };

            if (role == Role.Glass)
            {
                var c = baseColor;
                bool red = c.r > c.g * 1.6f && c.r > c.b * 1.6f;
                float alpha;
                if (red)
                    alpha = transparent ? Mathf.Clamp(c.a, 0.35f, 0.85f) : 0.6f;   // lamp lenses keep their color
                else
                {
                    // Light-colored "clear" glass reads as milky in URP: use a real automotive tint
                    // (very dark, slightly blue-green) and let the alpha set how much shows through
                    float k = 0.06f / Mathf.Max(0.06f, c.maxColorComponent);
                    c = new Color(c.r * k * 0.9f, c.g * k, c.b * k * 1.05f);
                    alpha = transparent ? Mathf.Clamp(baseColor.a, 0.22f, 0.6f) : 0.35f;
                }
                m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, alpha));
                m.SetFloat("_Smoothness", 0.97f);
                m.SetFloat("_Metallic", 0f);
                MakeTransparent(m);
                m.SetFloat("_Cull", 0f);
                m.name += "_Glass";
            }
            else
            {
                m.SetColor("_BaseColor", baseColor);
                if (baseTex != null) m.SetTexture("_BaseMap", ExportTex(carId, baseTex, "albedo"));
                CopyST(src, "baseColorTexture", m, "_BaseMap");
                if (mrTex != null)
                {
                    var mask = ExportMask(carId, mrTex, metallic, roughness);
                    m.SetTexture("_MetallicGlossMap", mask);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    m.SetFloat("_Smoothness", 1f);
                    m.SetFloat("_SmoothnessTextureChannel", 0f);
                }
                else
                {
                    m.SetFloat("_Metallic", metallic);
                    m.SetFloat("_Smoothness", Mathf.Clamp01(1f - roughness));
                }
                if (nTex != null)
                {
                    m.SetTexture("_BumpMap", ExportTex(carId, nTex, "normal"));
                    m.SetFloat("_BumpScale", nScale);
                    m.EnableKeyword("_NORMALMAP");
                }
                if (oTex != null && oTex != mrTex)
                {
                    m.SetTexture("_OcclusionMap", ExportTex(carId, oTex, "ao"));
                    m.SetFloat("_OcclusionStrength", 1f);
                    m.EnableKeyword("_OCCLUSIONMAP");
                }
                if (role == Role.Lamp)
                {
                    // Emission is driven at runtime by VehicleVisuals (property block), so the lamp is dark until lit
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", Color.black);
                    var lampTex = eTex != null ? eTex : baseTex;
                    if (lampTex != null) m.SetTexture("_EmissionMap", ExportTex(carId, lampTex, "albedo"));
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else if (emis.maxColorComponent > 0.01f || eTex != null)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", eTex != null && emis.maxColorComponent < 0.01f ? Color.white : emis);
                    if (eTex != null) m.SetTexture("_EmissionMap", ExportTex(carId, eTex, "albedo"));
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                if (role == Role.Paint)
                {
                    // Automotive finish: base coat (keep the model's color) under a glossy clear coat
                    if (mrTex == null)
                    {
                        m.SetFloat("_Metallic", Mathf.Min(metallic, 0.65f));
                        m.SetFloat("_Smoothness", Mathf.Clamp(1f - roughness, 0.5f, 0.8f));
                    }
                    if (m.shader == complex)
                    {
                        m.SetFloat("_ClearCoat", 1f);
                        m.SetFloat("_ClearCoatMask", 1f);
                        m.SetFloat("_ClearCoatSmoothness", 0.95f);
                        m.EnableKeyword("_CLEARCOAT");
                    }
                }
                if (transparent)
                {
                    MakeTransparent(m);
                    m.SetFloat("_ZWrite", 0f);
                }
                else if (cutout)
                {
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cutoff", cutoff);
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.renderQueue = (int)RenderQueue.AlphaTest;
                    m.SetOverrideTag("RenderType", "TransparentCutout");
                }
                m.SetFloat("_Cull", doubleSided ? 0f : 2f);
                m.doubleSidedGI = doubleSided;
            }
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
            s_MatCache[key] = m;
            return m;
        }

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
        }

        static void CopyST(Material src, string srcProp, Material dst, string dstProp)
        {
            string st = srcProp + "_ST";
            if (!src.HasProperty(st)) return;
            var v = src.GetVector(st);
            if (v == Vector4.zero) return;
            dst.SetTextureScale(dstProp, new Vector2(v.x, v.y));
            dst.SetTextureOffset(dstProp, new Vector2(v.z, v.w));
        }

        // ------------------------------------------------------------------ textures

        static Texture2D ExportTex(string carId, Texture src, string kind)
        {
            string key = src.GetInstanceID() + "_" + kind;
            if (s_TexCache.TryGetValue(key, out var t)) return t;
            var px = ReadPixels(src, kind == "albedo", out int w, out int h);
            t = SavePng(carId, src.name, kind, px, w, h, kind);
            s_TexCache[key] = t;
            return t;
        }

        /// <summary>glTF metallic-roughness (G = roughness, B = metallic) to URP's metallic (R) + smoothness (A).</summary>
        static Texture2D ExportMask(string carId, Texture src, float metallic, float roughness)
        {
            string key = $"{src.GetInstanceID()}_mask_{metallic:F3}_{roughness:F3}";
            if (s_TexCache.TryGetValue(key, out var t)) return t;
            var px = ReadPixels(src, false, out int w, out int h);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                byte met = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * metallic), 0, 255);
                byte smooth = (byte)Mathf.Clamp(255 - Mathf.RoundToInt(c.g * roughness), 0, 255);
                px[i] = new Color32(met, 0, 0, smooth);
            }
            t = SavePng(carId, src.name, "mask", px, w, h, "mask");
            s_TexCache[key] = t;
            return t;
        }

        /// <summary>Reads a texture's raw texels (color-space round trip preserved), downscaled to MaxTexture.</summary>
        static Color32[] ReadPixels(Texture src, bool color, out int w, out int h)
        {
            Texture source = src;
            Texture2D temp = null;
            string path = AssetDatabase.GetAssetPath(src);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsMainAsset(src) && (ext == ".png" || ext == ".jpg" || ext == ".jpeg"))
            {
                temp = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                temp.LoadImage(File.ReadAllBytes(path));
                source = temp;
            }
            w = source.width; h = source.height;
            while (w > MaxTexture || h > MaxTexture) { w = Mathf.Max(1, w / 2); h = Mathf.Max(1, h / 2); }
            bool srgb = temp == null && GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            Graphics.Blit(source, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = read.GetPixels32();
            UnityEngine.Object.DestroyImmediate(read);
            if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
            return px;
        }

        static Texture2D SavePng(string carId, string srcName, string suffix, Color32[] px, int w, int h, string kind)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply();
            string safe = Regex.Replace(srcName, @"[^A-Za-z0-9_\-]+", "_");
            if (safe.Length > 48) safe = safe.Substring(0, 48);
            string path = $"{OutRoot}/{carId}/Textures/{safe}_{suffix}_{s_TexCache.Count}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = kind == "normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = kind == "albedo";
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.mipmapEnabled = true;
            imp.streamingMipmaps = true;
            imp.maxTextureSize = MaxTexture;
            imp.anisoLevel = kind == "albedo" ? 4 : 1;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ thumbnails

        /// <summary>Renders each car in the real test block (HDRI sky, sun, post-processing) for the selector cards.</summary>
        static string[] RenderThumbnails(List<CarCatalog.Entry> entries, List<string> prefabPaths)
        {
            var result = new string[entries.Count];
            if (!File.Exists(TestBlockBuilder.ScenePath)) return result;
            string dir = $"{OutRoot}/Thumbnails";
            EnsureFolder(dir);
            var scene = EditorSceneManager.OpenScene(TestBlockBuilder.ScenePath, OpenSceneMode.Single);
            var spawn = GameObject.Find("Spawn_Avenue_Eastbound");
            foreach (var v in UnityEngine.Object.FindObjectsByType<VehicleController>(FindObjectsSortMode.None)) v.gameObject.SetActive(false);
            Vector3 p = spawn != null ? spawn.transform.position : Vector3.zero;
            Quaternion rot = spawn != null ? spawn.transform.rotation : Quaternion.identity;
            Physics.SyncTransforms();
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 10f)) p = hit.point;
            Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right;

            var camGo = new GameObject("ThumbCamera");
            var cam = camGo.AddComponent<Camera>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1500f;
            const int W = 768, H = 480;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            for (int ei = 0; ei < entries.Count; ei++)
            {
                var e = entries[ei];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[ei]);
                if (prefab == null) continue;
                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                car.transform.SetPositionAndRotation(p, rot);
                var b = new Bounds(p, Vector3.zero);
                foreach (var r in car.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                float d = Mathf.Max(b.size.x, b.size.z) * 1.48f;
                Vector3 look = b.center - Vector3.up * b.extents.y * 0.25f;
                camGo.transform.position = look + (fwd * 0.78f - right * 0.62f).normalized * d + Vector3.up * (0.35f * d * 0.35f + 0.6f);
                camGo.transform.rotation = Quaternion.LookRotation(look - camGo.transform.position, Vector3.up);
                cam.Render(); cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                string path = $"{dir}/{e.id}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                UnityEngine.Object.DestroyImmediate(car);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.mipmapEnabled = false;
                imp.maxTextureSize = 1024;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
                result[ei] = path;
                Debug.Log($"[MASAR] Thumbnail {path}");
            }
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(camGo);
            // Leave the saved scene untouched
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return result;
        }

        // ------------------------------------------------------------------ helpers

        static int CountTriangles(GameObject go)
        {
            int n = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) n += (int)(mf.sharedMesh.triangles.Length / 3);
            return n;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
