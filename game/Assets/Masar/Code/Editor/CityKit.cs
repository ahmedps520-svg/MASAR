using System.Collections.Generic;
using Masar.World;
using UnityEditor;
using UnityEngine;

namespace Masar.EditorTools
{
    /// <summary>Materials used by the city generator.</summary>
    public sealed class CityMaterials
    {
        public Material Asphalt, Pavers, Concrete, Ground, KerbYellow, KerbBlack, PaintWhite, PaintYellow;
        public Material[] Plaster;          // color variants
        public Material Stone, WindowGlass, ShopGlass, Frame, FrameWhite, Metal, AcUnit, Tank, TankBlue;
        public Material[] Signs;
        public Material PalmTrunk, PalmFrond, PoleMetal, LampHead, SignalBox, SignalRed, SignalAmber, SignalGreen;
        public Material Gate, DomeGreen, Marble;
        public Material AsphaltPatch, Manhole, Bollard, Dish;
        public Material PalmDead, RoadWear, Grime;

        public static CityMaterials Create()
        {
            var m = new CityMaterials
            {
                Asphalt = MaterialLibrary.Scanned("City_Asphalt", "PH_Asphalt02", new Color(0.90f, 0.90f, 0.92f), 0.8f, 1.0f, 2048),
                Pavers = MaterialLibrary.Scanned("City_Pavers", "PavingStones138", new Color(0.92f, 0.86f, 0.78f), 0.8f),
                Concrete = MaterialLibrary.Scanned("City_Concrete", "Concrete034", new Color(0.86f, 0.84f, 0.80f), 0.8f),
                Ground = MaterialLibrary.Scanned("City_Ground", "Ground093C", new Color(0.95f, 0.88f, 0.76f), 0.8f),
                KerbYellow = MaterialLibrary.Solid("City_KerbYellow", new Color(0.86f, 0.66f, 0.08f), 0.35f),
                KerbBlack = MaterialLibrary.Solid("City_KerbBlack", new Color(0.04f, 0.04f, 0.04f), 0.3f),
                PaintWhite = MaterialLibrary.WornPaint("City_RoadPaintWhite", new Color(0.84f, 0.84f, 0.81f)),
                PaintYellow = MaterialLibrary.WornPaint("City_RoadPaintYellow", new Color(0.86f, 0.64f, 0.12f)),
                Stone = MaterialLibrary.Scanned("City_StoneCladding", "Concrete034", new Color(0.93f, 0.85f, 0.72f), 0.7f),
                // Slightly metallic so facades pick up sky and street reflections from the probes
                // (dielectric glass at 4% reflectance read as black holes in the first review renders)
                WindowGlass = MaterialLibrary.Solid("City_WindowGlass", new Color(0.30f, 0.38f, 0.44f), 0.92f, 0.55f),
                ShopGlass = MaterialLibrary.Solid("City_ShopGlass", new Color(0.22f, 0.27f, 0.30f), 0.94f, 0.45f),
                Frame = MaterialLibrary.Solid("City_FrameDark", new Color(0.16f, 0.16f, 0.17f), 0.55f, 0.6f),
                FrameWhite = MaterialLibrary.Solid("City_FrameWhite", new Color(0.86f, 0.86f, 0.84f), 0.5f, 0.2f),
                Metal = MaterialLibrary.Solid("City_RailingMetal", new Color(0.18f, 0.18f, 0.19f), 0.5f, 0.7f),
                AcUnit = MaterialLibrary.Solid("City_AcUnit", new Color(0.88f, 0.88f, 0.85f), 0.4f),
                Tank = MaterialLibrary.Solid("City_WaterTankWhite", new Color(0.9f, 0.9f, 0.88f), 0.35f),
                TankBlue = MaterialLibrary.Solid("City_WaterTankBlue", new Color(0.12f, 0.32f, 0.62f), 0.4f),
                PalmTrunk = MaterialLibrary.Scanned("City_PalmTrunk", "PH_PalmBark", new Color(0.80f, 0.68f, 0.54f), 0.6f, 1.3f, 2048),
                PalmFrond = MaterialLibrary.PalmLeaflets(),
                PalmDead = MaterialLibrary.PalmDeadFrond(),
                RoadWear = MaterialLibrary.RoadWear(),
                Grime = MaterialLibrary.FacadeGrime(),
                AsphaltPatch = MaterialLibrary.Scanned("City_AsphaltPatch", "PH_RoadDamaged", new Color(0.82f, 0.82f, 0.84f), 0.75f, 1.0f, 2048),
                Manhole = MaterialLibrary.Solid("City_Manhole", new Color(0.10f, 0.10f, 0.10f), 0.45f, 0.85f),
                Bollard = MaterialLibrary.Solid("City_Bollard", new Color(0.16f, 0.16f, 0.17f), 0.5f, 0.6f),
                Dish = MaterialLibrary.Solid("City_SatelliteDish", new Color(0.86f, 0.86f, 0.84f), 0.45f, 0.1f, true),
                PoleMetal = MaterialLibrary.Solid("City_PoleMetal", new Color(0.55f, 0.56f, 0.57f), 0.6f, 0.8f),
                LampHead = MaterialLibrary.Emissive("City_LampHead", new Color(0.85f, 0.85f, 0.8f), new Color(0.0f, 0.0f, 0.0f), 0.8f),
                SignalBox = MaterialLibrary.Solid("City_SignalBox", new Color(0.05f, 0.05f, 0.05f), 0.4f),
                SignalRed = MaterialLibrary.Emissive("City_SignalRed", new Color(0.25f, 0.02f, 0.02f), Color.black, 0.9f),
                SignalAmber = MaterialLibrary.Emissive("City_SignalAmber", new Color(0.25f, 0.14f, 0.02f), Color.black, 0.9f),
                SignalGreen = MaterialLibrary.Emissive("City_SignalGreen", new Color(0.02f, 0.2f, 0.08f), Color.black, 0.9f),
                Gate = MaterialLibrary.Solid("City_GateMetal", new Color(0.12f, 0.11f, 0.10f), 0.45f, 0.6f),
                DomeGreen = MaterialLibrary.Solid("City_DomeGreen", new Color(0.12f, 0.42f, 0.30f), 0.6f, 0.3f),
                Marble = MaterialLibrary.Solid("City_MarbleWhite", new Color(0.93f, 0.92f, 0.89f), 0.55f),
            };
            m.Plaster = new[]
            {
                MaterialLibrary.Scanned("City_Plaster_Cream", "PaintedPlaster017", new Color(0.97f, 0.92f, 0.82f), 0.6f, 0.6f),
                MaterialLibrary.Scanned("City_Plaster_White", "PaintedPlaster017", new Color(0.97f, 0.97f, 0.95f), 0.6f, 0.6f),
                MaterialLibrary.Scanned("City_Plaster_Sand", "PaintedPlaster017", new Color(0.90f, 0.80f, 0.64f), 0.6f, 0.6f),
                MaterialLibrary.Scanned("City_Plaster_Beige", "PaintedPlaster017", new Color(0.86f, 0.78f, 0.70f), 0.6f, 0.6f),
            };
            Color[] signColors = { new Color(0.06f, 0.33f, 0.22f), new Color(0.55f, 0.08f, 0.08f), new Color(0.08f, 0.18f, 0.42f), new Color(0.75f, 0.55f, 0.1f), new Color(0.12f, 0.12f, 0.13f) };
            m.Signs = new Material[signColors.Length];
            for (int i = 0; i < signColors.Length; i++)
                m.Signs[i] = MaterialLibrary.Emissive($"City_Sign{i}", signColors[i], signColors[i] * 0.35f, 0.5f);
            // Close-up surface detail: road grain at about 1.2 m, plaster and concrete grain on facades and kerbs
            MaterialLibrary.AddDetail(m.Asphalt, "PH_Asphalt02", 6f, 1f, 0.45f);
            MaterialLibrary.AddDetail(m.AsphaltPatch, "PH_Asphalt02", 5f, 1f, 0.4f);
            foreach (var p in m.Plaster) MaterialLibrary.AddDetail(p, "Concrete034", 3f, 0.7f, 0.5f);
            MaterialLibrary.AddDetail(m.Stone, "Concrete034", 2.5f, 0.8f, 0.6f);
            MaterialLibrary.AddDetail(m.Concrete, "Concrete034", 3f, 0.8f, 0.5f);
            return m;
        }
    }

    /// <summary>A set of mesh builders keyed by material. Flushed into one renderer per material.</summary>
    public sealed class MeshBatch
    {
        private readonly Dictionary<Material, MeshBuilder> _byMat = new Dictionary<Material, MeshBuilder>();
        public MeshBuilder this[Material m]
        {
            get
            {
                if (!_byMat.TryGetValue(m, out var b)) { b = new MeshBuilder(); _byMat[m] = b; }
                return b;
            }
        }

        public List<GameObject> Flush(Transform parent, string name, string meshDir, bool collider)
        {
            var list = new List<GameObject>();
            foreach (var kv in _byMat)
            {
                if (kv.Value.Count == 0) continue;
                kv.Value.FixWinding();
                var go = new GameObject($"{name}_{kv.Key.name}");
                go.transform.SetParent(parent, false);
                var mesh = kv.Value.ToMesh($"{name}_{kv.Key.name}");
                string path = $"{meshDir}/{mesh.name}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = kv.Key;
                if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                list.Add(go);
            }
            _byMat.Clear();
            return list;
        }
    }

    /// <summary>
    /// Procedural building generator for modern Jeddah-style residential and commercial
    /// buildings: recessed windows with frames, balconies, split-AC units, shopfronts
    /// with sign bands, parapets, rooftop water tanks and stair bulkheads.
    /// Facades are built in local space (front faces +Z) and transformed into place.
    /// </summary>
    public static class BuildingKit
    {
        public struct Spec
        {
            public float Width, Depth;
            public int Floors;
            public float FloorHeight, GroundHeight;
            public bool Shops;
            public int PlasterIndex;
            public bool StoneGround;
            public int Seed;
            public float BalconyChance;
            public float AcChance;
        }

        public static void Apartment(MeshBatch batch, CityMaterials m, Vector3 position, float yawDeg, Spec s)
        {
            var rnd = new System.Random(s.Seed);
            var plaster = m.Plaster[s.PlasterIndex % m.Plaster.Length];
            Matrix4x4 xf = Matrix4x4.TRS(position, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one);
            float W = s.Width, D = s.Depth;
            float H = s.GroundHeight + s.Floors * s.FloorHeight;
            float hw = W * 0.5f, hd = D * 0.5f;

            // Ground floor
            if (s.Shops) ShopFront(batch, m, xf, W, s.GroundHeight, hd, rnd);
            else SolidFace(batch[s.StoneGround ? m.Stone : plaster], xf, -hw, hw, 0f, s.GroundHeight, hd, Vector3.forward);

            // Upper floors on the front: bays of windows or balconies
            int bays = Mathf.Max(2, Mathf.RoundToInt(W / 3.6f));
            float bayW = W / bays;
            for (int f = 0; f < s.Floors; f++)
            {
                float y0 = s.GroundHeight + f * s.FloorHeight;
                bool balconyRow = rnd.NextDouble() < s.BalconyChance;
                for (int b = 0; b < bays; b++)
                {
                    float x0 = -hw + b * bayW;
                    bool balcony = balconyRow && (b % 2 == 1 || bays <= 3);
                    WindowCell(batch, m, plaster, xf, x0, x0 + bayW, y0, y0 + s.FloorHeight, hd, balcony ? 2.2f : 1.5f, balcony ? 2.3f : 1.45f, balcony ? 0.1f : 0.95f);
                    if (balcony) Balcony(batch, m, plaster, xf, x0 + 0.2f, x0 + bayW - 0.2f, y0, hd);
                    else if (rnd.NextDouble() < s.AcChance) AcUnit(batch, m, xf, x0 + bayW * 0.5f + 1.05f, y0 + 0.5f, hd, s.GroundHeight + 0.2f);
                }
                // Floor band (slab edge) for horizontal rhythm
                batch[plaster].Transform = xf;
                batch[plaster].Box(new Vector3(-hw - 0.05f, y0 - 0.12f, hd), new Vector3(hw + 0.05f, y0 + 0.06f, hd + 0.08f), 2f);
                // Rain drips washed off the slab edge (kept above the window heads of the floor below,
                // and never over the shop sign band)
                if ((f > 0 || !s.Shops) && rnd.NextDouble() < 0.75)
                {
                    var g = batch[m.Grime]; g.Transform = xf;
                    float len = 0.3f + 0.35f * (float)rnd.NextDouble(), u = (float)rnd.NextDouble() * 8f;
                    Grime(g, -hw, hw, y0 - 0.12f - len, y0 - 0.12f, hd + GrimeOffset + 0.003f, GrimeDrip, u, u + W / 8f);
                }
            }
            WeatherFace(batch, m, xf, W, s.Shops ? s.GroundHeight : 0f, H, hd, !s.Shops, s.Seed);

            // Side and back faces: fewer, smaller windows
            SideFaces(batch, m, plaster, xf, W, D, s, rnd);

            // Roof: slab, parapet, tanks, bulkhead
            var p = batch[plaster];
            p.Transform = xf;
            p.FloorRect(-hw, -hd, hw, hd, H, 2f);
            p.Box(new Vector3(-hw, H, hd - 0.25f), new Vector3(hw, H + 1.0f, hd), 2f);
            p.Box(new Vector3(-hw, H, -hd), new Vector3(hw, H + 1.0f, -hd + 0.25f), 2f);
            p.Box(new Vector3(-hw, H, -hd), new Vector3(-hw + 0.25f, H + 1.0f, hd), 2f);
            p.Box(new Vector3(hw - 0.25f, H, -hd), new Vector3(hw, H + 1.0f, hd), 2f);
            p.Box(new Vector3(-2.2f, H, -hd + 1.5f), new Vector3(1.2f, H + 2.8f, -hd + 5f), 2f);       // stair bulkhead
            int tanks = 2 + rnd.Next(4);
            for (int t = 0; t < tanks; t++)
            {
                var mat = rnd.NextDouble() < 0.35 ? m.TankBlue : m.Tank;
                var tb = batch[mat];
                tb.Transform = xf;
                float tx = Mathf.Lerp(-hw + 2f, hw - 2f, (float)rnd.NextDouble());
                float tz = Mathf.Lerp(-hd + 2f, hd - 3f, (float)rnd.NextDouble());
                tb.Cylinder(new Vector3(tx, H + 0.3f, tz), 0.7f, 0.7f, 1.5f, 14);
                batch[m.Metal].Transform = xf;
                batch[m.Metal].Box(new Vector3(tx - 0.8f, H, tz - 0.8f), new Vector3(tx + 0.8f, H + 0.3f, tz + 0.8f));
            }
            // Satellite dishes (a fixture of Jeddah rooftops), on short masts
            int dishes = rnd.Next(4);
            for (int d = 0; d < dishes; d++)
            {
                float dx = Mathf.Lerp(-hw + 1.5f, hw - 1.5f, (float)rnd.NextDouble());
                float dz = Mathf.Lerp(-hd + 1.2f, hd - 1.2f, (float)rnd.NextDouble());
                var mast = batch[m.Metal]; mast.Transform = xf;
                mast.Box(new Vector3(dx - 0.03f, H, dz - 0.03f), new Vector3(dx + 0.03f, H + 1.1f, dz + 0.03f));
                var dish = batch[m.Dish];
                dish.Transform = xf * Matrix4x4.TRS(new Vector3(dx, H + 1.1f, dz), Quaternion.Euler(-55f, (float)rnd.NextDouble() * 60f + 150f, 0f), Vector3.one);
                dish.Cylinder(Vector3.zero, 0.06f, 0.42f + (float)rnd.NextDouble() * 0.2f, 0.16f, 18, false);
                dish.Transform = Matrix4x4.identity;
            }
            // A few AC condensers on the roof
            for (int a = 0; a < 3 + rnd.Next(4); a++)
            {
                var ac = batch[m.AcUnit];
                ac.Transform = xf;
                float ax = Mathf.Lerp(-hw + 1.5f, hw - 1.5f, (float)rnd.NextDouble());
                float az = Mathf.Lerp(-hd + 1f, hd - 1.5f, (float)rnd.NextDouble());
                ac.Box(new Vector3(ax - 0.45f, H, az - 0.3f), new Vector3(ax + 0.45f, H + 0.7f, az + 0.3f));
            }
            foreach (var mat in new[] { plaster, m.Stone, m.WindowGlass, m.Frame, m.Metal, m.AcUnit, m.Tank, m.TankBlue, m.ShopGlass, m.Grime })
                batch[mat].Transform = Matrix4x4.identity;
        }

        /// <summary>A wall cell with a recessed window opening, reveals, glass and frame.</summary>
        private static void WindowCell(MeshBatch batch, CityMaterials m, Material wall, Matrix4x4 xf,
                                       float x0, float x1, float y0, float y1, float z, float openW, float openH, float sill)
        {
            var w = batch[wall]; w.Transform = xf;
            float cx = 0.5f * (x0 + x1);
            float ox0 = cx - openW * 0.5f, ox1 = cx + openW * 0.5f;
            float oy0 = y0 + sill, oy1 = Mathf.Min(y1 - 0.25f, oy0 + openH);
            const float depth = 0.18f;
            // Wall pieces around the opening (front plane z)
            FaceRect(w, x0, ox0, y0, y1, z);
            FaceRect(w, ox1, x1, y0, y1, z);
            FaceRect(w, ox0, ox1, y0, oy0, z);
            FaceRect(w, ox0, ox1, oy1, y1, z);
            // Reveals
            w.Quad(new Vector3(ox0, oy0, z - depth), new Vector3(ox0, oy1, z - depth), new Vector3(ox0, oy1, z), new Vector3(ox0, oy0, z), 1f);
            w.Quad(new Vector3(ox1, oy0, z), new Vector3(ox1, oy1, z), new Vector3(ox1, oy1, z - depth), new Vector3(ox1, oy0, z - depth), 1f);
            w.Quad(new Vector3(ox0, oy1, z - depth), new Vector3(ox1, oy1, z - depth), new Vector3(ox1, oy1, z), new Vector3(ox0, oy1, z), 1f); // head (faces down)
            w.Quad(new Vector3(ox0, oy0, z), new Vector3(ox1, oy0, z), new Vector3(ox1, oy0, z - depth), new Vector3(ox0, oy0, z - depth), 1f); // sill (faces up)
            // Glass
            var g = batch[m.WindowGlass]; g.Transform = xf;
            FaceRect(g, ox0, ox1, oy0, oy1, z - depth);
            // Frame: perimeter + one mullion (sliding window)
            var f = batch[m.Frame]; f.Transform = xf;
            const float t = 0.05f;
            f.Box(new Vector3(ox0, oy0, z - depth), new Vector3(ox1, oy0 + t, z - depth + 0.06f));
            f.Box(new Vector3(ox0, oy1 - t, z - depth), new Vector3(ox1, oy1, z - depth + 0.06f));
            f.Box(new Vector3(ox0, oy0, z - depth), new Vector3(ox0 + t, oy1, z - depth + 0.06f));
            f.Box(new Vector3(ox1 - t, oy0, z - depth), new Vector3(ox1, oy1, z - depth + 0.06f));
            f.Box(new Vector3(cx - t * 0.5f, oy0, z - depth), new Vector3(cx + t * 0.5f, oy1, z - depth + 0.06f));
            // Sill
            w.Box(new Vector3(ox0 - 0.08f, oy0 - 0.06f, z - 0.02f), new Vector3(ox1 + 0.08f, oy0, z + 0.06f));
            // Dust washed off the sill by rain: the classic dark streaks below every window
            if (sill > 0.3f)
            {
                var gr = batch[m.Grime]; gr.Transform = xf;
                float r = Rand01(cx, oy0 + z, 7), len = 0.35f + 0.6f * r;
                Grime(gr, ox0 - 0.08f, ox1 + 0.08f, Mathf.Max(y0 + 0.02f, oy0 - 0.06f - len), oy0 - 0.06f, z + GrimeOffset,
                      GrimeDrip, r * 7f, r * 7f + (openW + 0.16f) / 8f);
            }
        }

        private static void Balcony(MeshBatch batch, CityMaterials m, Material wall, Matrix4x4 xf, float x0, float x1, float y, float z)
        {
            var w = batch[wall]; w.Transform = xf;
            w.Box(new Vector3(x0, y - 0.15f, z), new Vector3(x1, y + 0.02f, z + 1.25f), 2f);         // slab
            w.Box(new Vector3(x0, y + 0.02f, z + 1.15f), new Vector3(x1, y + 0.55f, z + 1.25f), 2f);  // parapet
            var r = batch[m.Metal]; r.Transform = xf;
            r.Box(new Vector3(x0, y + 1.02f, z + 1.17f), new Vector3(x1, y + 1.07f, z + 1.23f));     // handrail
            int posts = Mathf.Max(2, Mathf.RoundToInt((x1 - x0) / 0.12f));
            for (int i = 0; i <= posts; i += 1)
            {
                float px = Mathf.Lerp(x0 + 0.03f, x1 - 0.03f, i / (float)posts);
                r.Box(new Vector3(px - 0.012f, y + 0.55f, z + 1.19f), new Vector3(px + 0.012f, y + 1.02f, z + 1.21f));
            }
        }

        private static void AcUnit(MeshBatch batch, CityMaterials m, Matrix4x4 xf, float x, float y, float z, float minY)
        {
            var a = batch[m.AcUnit]; a.Transform = xf;
            a.Box(new Vector3(x - 0.42f, y, z + 0.02f), new Vector3(x + 0.42f, y + 0.58f, z + 0.32f));
            var b = batch[m.Metal]; b.Transform = xf;
            b.Box(new Vector3(x - 0.38f, y - 0.06f, z), new Vector3(x - 0.34f, y, z + 0.32f));
            b.Box(new Vector3(x + 0.34f, y - 0.06f, z), new Vector3(x + 0.38f, y, z + 0.32f));
            // Condensate and rust stain running down the wall from the unit
            var g = batch[m.Grime]; g.Transform = xf;
            float r = Rand01(x, y + z, 3), len = 1.0f + 1.4f * r;
            float x0 = x - 0.12f - 0.1f * r, x1 = x + 0.12f + 0.1f * r;
            Grime(g, x0, x1, Mathf.Max(minY, y - len), y + 0.1f, z + GrimeOffset, GrimeStreak, 0f, 1f);
        }

        // ------------------------------------------------------------------ weathering
        // Alpha-blended overlay quads on the facade grime atlas (MaterialLibrary.FacadeGrime).
        // Weathering follows where water and dust actually go: down from every ledge, up from the ground.

        private const int GrimeBase = 0, GrimeDrip = 1, GrimeStreak = 2, GrimeMacro = 3;
        /// <summary>Distance in front of the wall: enough to avoid z-fighting at range, too small to see parallax.</summary>
        private const float GrimeOffset = 0.025f;

        private static float Rand01(float a, float b, int s)
        {
            float v = Mathf.Sin(a * 12.9898f + b * 78.233f + s * 37.719f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        /// <summary>Front-facing (+Z) overlay rectangle mapped onto one atlas row, U from u0 to u1.</summary>
        private static void Grime(MeshBuilder g, float x0, float x1, float y0, float y1, float z, int row, float u0, float u1)
        {
            if (x1 - x0 < 1e-3f || y1 - y0 < 1e-3f) return;
            float v0 = (row + 0.025f) * 0.25f, v1 = (row + 0.975f) * 0.25f;
            g.QuadUV(new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), new Vector3(x0, y0, z),
                     new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), new Vector2(u0, v0));
        }

        /// <summary>
        /// Whole-face weathering (face-local +Z at depth z, centered on x = 0): faint dirt patches,
        /// a dust band at the base, rain streaks hanging from the parapet top at H + 1.
        /// </summary>
        private static void WeatherFace(MeshBatch batch, CityMaterials m, Matrix4x4 xf, float width, float yBottom, float H, float z, bool baseBand, int seed)
        {
            var g = batch[m.Grime]; g.Transform = xf;
            float hw = width * 0.5f, zf = z + GrimeOffset;
            float r = Rand01(width, z, seed), r2 = Rand01(z, width, seed + 1);
            Grime(g, -hw, hw, yBottom, H + 1f, zf, GrimeMacro, r, r + width / 16f);
            if (baseBand) Grime(g, -hw, hw, 0f, 1.0f + 0.6f * r2, zf + 0.002f, GrimeBase, r * 3f, r * 3f + width / 6f);
            float len = 1.1f + 0.7f * r2;
            Grime(g, -hw, hw, H + 1f - len, H + 1f, zf + 0.004f, GrimeDrip, r * 5f, r * 5f + width / 8f);
            g.Transform = Matrix4x4.identity;
        }

        private static void ShopFront(MeshBatch batch, CityMaterials m, Matrix4x4 xf, float W, float h, float z, System.Random rnd)
        {
            float hw = W * 0.5f;
            int units = Mathf.Max(1, Mathf.RoundToInt(W / 7f));
            float uw = W / units;
            var stone = batch[m.Stone]; stone.Transform = xf;
            for (int u = 0; u < units; u++)
            {
                float x0 = -hw + u * uw, x1 = x0 + uw;
                // Piers
                stone.Box(new Vector3(x0, 0f, z - 0.05f), new Vector3(x0 + 0.5f, h, z + 0.15f), 1f);
                // Glass storefront
                var g = batch[m.ShopGlass]; g.Transform = xf;
                FaceRect(g, x0 + 0.5f, x1, 0.15f, h - 1.25f, z - 0.25f);
                var f = batch[m.Frame]; f.Transform = xf;
                f.Box(new Vector3(x0 + 0.5f, h - 1.3f, z - 0.25f), new Vector3(x1, h - 1.2f, z - 0.15f));
                f.Box(new Vector3(x0 + 0.5f, 0f, z - 0.25f), new Vector3(x1, 0.15f, z - 0.1f));
                float door = Mathf.Lerp(x0 + 1.5f, x1 - 1.8f, (float)rnd.NextDouble());
                f.Box(new Vector3(door, 0.15f, z - 0.25f), new Vector3(door + 0.06f, h - 1.3f, z - 0.15f));
                f.Box(new Vector3(door + 1.6f, 0.15f, z - 0.25f), new Vector3(door + 1.66f, h - 1.3f, z - 0.15f));
                // Sign band (blank panels: no invented brands)
                var sign = batch[m.Signs[rnd.Next(m.Signs.Length)]]; sign.Transform = xf;
                sign.Box(new Vector3(x0 + 0.7f, h - 1.1f, z + 0.05f), new Vector3(x1 - 0.2f, h - 0.25f, z + 0.25f));
                // Fill above the glass behind the sign
                FaceRect(stone, x0 + 0.5f, x1, h - 1.2f, h, z - 0.2f);
                // Recess side walls
                stone.Quad(new Vector3(x1, 0f, z - 0.25f), new Vector3(x1, h, z - 0.25f), new Vector3(x1, h, z - 0.05f), new Vector3(x1, 0f, z - 0.05f));
            }
            stone.Box(new Vector3(hw - 0.5f, 0f, z - 0.05f), new Vector3(hw, h, z + 0.15f), 1f);
            // Canopy slab over the shops
            stone.Box(new Vector3(-hw, h - 0.2f, z), new Vector3(hw, h, z + 1.6f), 2f);
        }

        private static void SideFaces(MeshBatch batch, CityMaterials m, Material plaster, Matrix4x4 xf, float W, float D, Spec s, System.Random rnd)
        {
            float hw = W * 0.5f, hd = D * 0.5f;
            float H = s.GroundHeight + s.Floors * s.FloorHeight;
            // Back face: plain with small windows
            Matrix4x4 back = xf * Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 180f, 0f), Vector3.one);
            SimpleWindowedFace(batch, m, plaster, back, W, H, hd, s, 0.9f, 1.1f);
            // Left and right faces
            Matrix4x4 left = xf * Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, -90f, 0f), Vector3.one);
            Matrix4x4 right = xf * Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one);
            SimpleWindowedFace(batch, m, plaster, left, D, H, hw, s, 1.0f, 1.2f);
            SimpleWindowedFace(batch, m, plaster, right, D, H, hw, s, 1.0f, 1.2f);
        }

        private static void SimpleWindowedFace(MeshBatch batch, CityMaterials m, Material wall, Matrix4x4 xf, float width, float H, float z, Spec s, float ow, float oh)
        {
            float hw = width * 0.5f;
            WeatherFace(batch, m, xf, width, 0f, H, z, true, s.Seed + Mathf.RoundToInt(z * 10f + width));
            var w = batch[wall]; w.Transform = xf;
            FaceRect(w, -hw, hw, 0f, s.GroundHeight, z);
            int bays = Mathf.Max(1, Mathf.RoundToInt(width / 4.5f));
            float bw = width / bays;
            for (int f = 0; f < s.Floors; f++)
            {
                float y0 = s.GroundHeight + f * s.FloorHeight;
                for (int b = 0; b < bays; b++)
                {
                    float x0 = -hw + b * bw;
                    if ((b + f) % 3 == 2) { var ww = batch[wall]; ww.Transform = xf; FaceRect(ww, x0, x0 + bw, y0, y0 + s.FloorHeight, z); continue; }
                    WindowCell(batch, m, wall, xf, x0, x0 + bw, y0, y0 + s.FloorHeight, z, ow, oh, 1.1f);
                }
            }
        }

        /// <summary>Front-facing (+Z) rectangle at depth z.</summary>
        public static void FaceRect(MeshBuilder b, float x0, float x1, float y0, float y1, float z)
        {
            if (x1 - x0 < 1e-3f || y1 - y0 < 1e-3f) return;
            b.Quad(new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), new Vector3(x0, y0, z), 2f, new Vector2(x0 * 0.5f, y0 * 0.5f));
        }

        private static void SolidFace(MeshBuilder b, Matrix4x4 xf, float x0, float x1, float y0, float y1, float z, Vector3 _)
        {
            b.Transform = xf;
            FaceRect(b, x0, x1, y0, y1, z);
        }

        // ------------------------------------------------------------------ villa

        public static void Villa(MeshBatch batch, CityMaterials m, Vector3 plotCenter, float yawDeg, float plotW, float plotD, int seed)
        {
            var rnd = new System.Random(seed);
            var plaster = m.Plaster[rnd.Next(m.Plaster.Length)];
            Matrix4x4 xf = Matrix4x4.TRS(plotCenter, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one);
            float hw = plotW * 0.5f, hd = plotD * 0.5f;
            const float wallH = 2.9f, wallT = 0.25f;
            var w = batch[plaster]; w.Transform = xf;
            // Compound wall with a gate opening on the front (+Z)
            float gate0 = -2.2f, gate1 = 2.2f;
            w.Box(new Vector3(-hw, 0f, hd - wallT), new Vector3(gate0, wallH, hd), 2f);
            w.Box(new Vector3(gate1, 0f, hd - wallT), new Vector3(hw, wallH, hd), 2f);
            w.Box(new Vector3(-hw, 0f, -hd), new Vector3(hw, wallH, -hd + wallT), 2f);
            w.Box(new Vector3(-hw, 0f, -hd), new Vector3(-hw + wallT, wallH, hd), 2f);
            w.Box(new Vector3(hw - wallT, 0f, -hd), new Vector3(hw, wallH, hd), 2f);
            // Coping on the wall
            var st = batch[m.Stone]; st.Transform = xf;
            st.Box(new Vector3(-hw - 0.05f, wallH, hd - wallT - 0.05f), new Vector3(gate0, wallH + 0.12f, hd + 0.05f));
            st.Box(new Vector3(gate1, wallH, hd - wallT - 0.05f), new Vector3(hw + 0.05f, wallH + 0.12f, hd + 0.05f));
            // Gate
            var g = batch[m.Gate]; g.Transform = xf;
            g.Box(new Vector3(gate0, 0.05f, hd - 0.16f), new Vector3(gate1, wallH - 0.1f, hd - 0.08f));
            // Gate pillars
            st.Box(new Vector3(gate0 - 0.45f, 0f, hd - 0.35f), new Vector3(gate0, wallH + 0.5f, hd + 0.1f));
            st.Box(new Vector3(gate1, 0f, hd - 0.35f), new Vector3(gate1 + 0.45f, wallH + 0.5f, hd + 0.1f));
            // Compound wall weathering on the four outer faces: dust splash at the base, drips under
            // the coping. The front skips the gate opening.
            var gr = batch[m.Grime];
            for (int side = 0; side < 4; side++)
            {
                float yaw = side * 90f;
                float half = side % 2 == 0 ? hw : hd, depth = side % 2 == 0 ? hd : hw;
                gr.Transform = xf * Matrix4x4.Rotate(Quaternion.Euler(0f, yaw, 0f));
                float r = Rand01(half, depth + side, seed), zf = depth + GrimeOffset;
                float splash = 0.8f + 0.5f * r, drip = 0.5f + 0.6f * Rand01(depth, half + side, seed + 5);
                var spans = side == 0 ? new[] { (-half, gate0 - 0.45f), (gate1 + 0.45f, half) } : new[] { (-half, half) };
                foreach (var (a, b) in spans)
                {
                    Grime(gr, a, b, 0f, splash, zf, GrimeBase, a / 6f + r, b / 6f + r);
                    Grime(gr, a, b, wallH - drip, wallH, zf + 0.002f, GrimeDrip, a / 8f + r * 3f, b / 8f + r * 3f);
                }
            }

            // House: two floors, set back from the gate
            float vw = plotW * 0.62f, vd = plotD * 0.48f;
            var spec = new Spec { Width = vw, Depth = vd, Floors = 1, FloorHeight = 3.4f, GroundHeight = 3.6f, Shops = false, PlasterIndex = System.Array.IndexOf(m.Plaster, plaster), Seed = seed + 7, BalconyChance = 0.6f, AcChance = 0.8f, StoneGround = rnd.NextDouble() < 0.5 };
            Vector3 housePos = plotCenter + Quaternion.Euler(0f, yawDeg, 0f) * new Vector3(0f, 0f, -plotD * 0.12f);
            Apartment(batch, m, housePos, yawDeg, spec);
            foreach (var mat in new[] { plaster, m.Stone, m.Gate, m.Grime }) batch[mat].Transform = Matrix4x4.identity;
        }

        // ------------------------------------------------------------------ mosque

        public static void Mosque(MeshBatch batch, CityMaterials m, Vector3 center, float yawDeg)
        {
            Matrix4x4 xf = Matrix4x4.TRS(center, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one);
            var w = batch[m.Plaster[1]]; w.Transform = xf;
            const float W = 22f, D = 24f, H = 7.5f;
            // Prayer hall with tall windows on the front and sides
            var spec = new Spec { Width = W, Depth = D, Floors = 1, FloorHeight = H - 1.5f, GroundHeight = 1.5f, Shops = false, PlasterIndex = 1, Seed = 99, BalconyChance = 0f, AcChance = 0f, StoneGround = true };
            Apartment(batch, m, center, yawDeg, spec);
            // Dome on a drum
            var mar = batch[m.Marble]; mar.Transform = xf;
            mar.Cylinder(new Vector3(0f, H + 1.0f, -1f), 6.2f, 6.2f, 1.6f, 40, false);
            var dome = batch[m.DomeGreen]; dome.Transform = xf;
            var rings = new List<Vector3[]>();
            for (int r = 0; r <= 12; r++)
            {
                float a = r / 12f * Mathf.PI * 0.5f;
                float rad = Mathf.Cos(a) * 6.0f, y = H + 2.6f + Mathf.Sin(a) * 5.2f;
                var ring = new Vector3[41];
                for (int i = 0; i <= 40; i++)
                {
                    float t = i / 40f * Mathf.PI * 2f;
                    ring[i] = new Vector3(Mathf.Cos(t) * Mathf.Max(0.02f, rad), y, -1f + Mathf.Sin(t) * Mathf.Max(0.02f, rad));
                }
                rings.Add(ring);
            }
            int s = dome.Count;
            dome.Loft(rings, true);
            dome.OrientOutward(s, p => xf.MultiplyPoint3x4(new Vector3(0f, H + 2.6f, -1f)));
            // Minaret: square base, octagonal shaft, two balconies, crown and finial
            Vector3 mpos = new Vector3(W * 0.5f + 3.2f, 0f, D * 0.5f - 3f);
            mar.Box(mpos + new Vector3(-1.8f, 0f, -1.8f), mpos + new Vector3(1.8f, 9f, 1.8f), 2f);
            mar.Cylinder(mpos + new Vector3(0f, 9f, 0f), 1.45f, 1.3f, 16f, 8, false);
            mar.Cylinder(mpos + new Vector3(0f, 25f, 0f), 2.2f, 2.2f, 0.5f, 16, true);                 // balcony 1
            mar.Cylinder(mpos + new Vector3(0f, 25.5f, 0f), 1.15f, 1.05f, 7f, 8, false);
            mar.Cylinder(mpos + new Vector3(0f, 32.5f, 0f), 1.8f, 1.8f, 0.4f, 16, true);               // balcony 2
            mar.Cylinder(mpos + new Vector3(0f, 32.9f, 0f), 0.95f, 0.9f, 3f, 8, false);
            dome.Cylinder(mpos + new Vector3(0f, 35.9f, 0f), 1.1f, 0.05f, 3.2f, 16, false);              // crown
            var gold = batch[m.PoleMetal]; gold.Transform = xf;
            gold.Cylinder(mpos + new Vector3(0f, 39.1f, 0f), 0.06f, 0.03f, 1.6f, 8, false);              // finial
            gold.Cylinder(new Vector3(0f, H + 7.8f, -1f), 0.08f, 0.03f, 2.2f, 8, false);
            foreach (var mat in new[] { m.Plaster[1], m.Marble, m.DomeGreen, m.PoleMetal }) batch[mat].Transform = Matrix4x4.identity;
        }

        // ------------------------------------------------------------------ street furniture

        public static void DatePalm(MeshBatch batch, CityMaterials m, Vector3 basePos, int seed)
        {
            var rnd = new System.Random(seed);
            float height = 6.5f + (float)rnd.NextDouble() * 3f;
            float lean = ((float)rnd.NextDouble() - 0.5f) * 0.5f;
            var trunk = batch[m.PalmTrunk];
            var rings = new List<Vector3[]>();
            const int seg = 10, rows = 28;
            for (int r = 0; r <= rows; r++)
            {
                float t = r / (float)rows;
                float rad = Mathf.Lerp(0.3f, 0.2f, t) * (1f + 0.07f * Mathf.Sin(t * height * 9f)); // leaf-base rings
                Vector3 c = basePos + new Vector3(lean * t * t, t * height, lean * 0.5f * t * t);
                var ring = new Vector3[seg + 1];
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    ring[i] = c + new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
                }
                rings.Add(ring);
            }
            int st = trunk.Count;
            trunk.Loft(rings, true);
            trunk.OrientOutward(st, p => new Vector3(basePos.x + lean * Mathf.Pow((p.y - basePos.y) / height, 2f), p.y, basePos.z + lean * 0.5f * Mathf.Pow((p.y - basePos.y) / height, 2f)));

            Vector3 crown = basePos + new Vector3(lean, height, lean * 0.5f);
            var frond = batch[m.PalmFrond];
            int fronds = 18 + rnd.Next(6);
            for (int f = 0; f < fronds; f++)
            {
                float yaw = f / (float)fronds * 360f + (float)rnd.NextDouble() * 15f;
                float pitch = Mathf.Lerp(-35f, 55f, (float)rnd.NextDouble());   // up-pointing to drooping
                float len = 3.2f + (float)rnd.NextDouble() * 1.2f;
                var q = Quaternion.Euler(0f, yaw, 0f);
                var strip = new List<Vector3[]>();
                const int n = 12;
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    float droop = pitch + t * t * 70f;                         // curves downward toward the tip
                    Vector3 dir = q * Quaternion.Euler(droop, 0f, 0f) * Vector3.forward;
                    Vector3 p = crown + dir * (len * t);
                    if (i > 0)
                    {
                        // integrate along the curve for a smooth arc
                        var prev = strip[i - 1][1];
                        p = prev + dir * (len / n);
                    }
                    // Half-width of the frond card: the leaflet texture fills it, alpha-clipped
                    float width = Mathf.Sin(Mathf.Clamp01(t * 1.1f) * Mathf.PI) * 0.85f + 0.08f;
                    Vector3 side = q * Vector3.right * width;
                    Vector3 fold = Vector3.up * width * 0.3f;                  // V-shaped leaflet fold
                    strip.Add(new[] { p - side + fold, p, p + side + fold });
                }
                frond.Loft(strip, false);
            }
            // Leaf-base "skirt" under the crown
            trunk.Cylinder(crown - new Vector3(0f, 0.6f, 0f), 0.42f, 0.3f, 0.7f, 10, false);
        }

        public static void StreetLight(MeshBatch batch, CityMaterials m, Vector3 basePos, bool doubleArm, float yawDeg)
        {
            var pole = batch[m.PoleMetal];
            var q = Quaternion.Euler(0f, yawDeg, 0f);
            pole.Cylinder(basePos, 0.14f, 0.08f, 10f, 10, true);
            pole.Cylinder(basePos, 0.22f, 0.22f, 0.4f, 10, true);
            foreach (int side in doubleArm ? new[] { -1, 1 } : new[] { 1 })
            {
                Vector3 dir = q * new Vector3(0f, 0f, side);
                Vector3 a0 = basePos + Vector3.up * 9.6f, a1 = a0 + dir * 2.4f + Vector3.up * 0.3f;
                pole.Transform = Matrix4x4.TRS((a0 + a1) * 0.5f, Quaternion.LookRotation(a1 - a0), Vector3.one);
                pole.Box(new Vector3(-0.04f, -0.04f, -1.25f), new Vector3(0.04f, 0.04f, 1.25f));
                pole.Transform = Matrix4x4.identity;
                var head = batch[m.LampHead];
                head.Transform = Matrix4x4.TRS(a1 + dir * 0.35f - Vector3.up * 0.05f, Quaternion.LookRotation(dir), Vector3.one);
                head.Box(new Vector3(-0.22f, -0.08f, -0.45f), new Vector3(0.22f, 0.06f, 0.45f));
                head.Transform = Matrix4x4.identity;
            }
        }
    }
}
