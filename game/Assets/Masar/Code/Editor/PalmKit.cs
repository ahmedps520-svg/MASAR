using System.Collections.Generic;
using Masar.World;
using UnityEditor;
using UnityEngine;

namespace Masar.EditorTools
{
    /// <summary>
    /// Date palm (Phoenix dactylifera) generator, as planted on Jeddah medians.
    ///
    /// Leaflets are real geometry, not alpha cards: opaque, crisp at any distance, and cheap
    /// on Apple GPUs, where alpha-tested foliage defeats hidden-surface removal.
    ///  - Trunk: tapered, slightly leaning, with the diamond leaf-base ("boot") relief, plus the scanned bark.
    ///  - Crown: young fronds upright, mature ones spreading, old ones arching down. Each frond has a
    ///    curved rachis with V-folded leaflets, longest mid-frond, short and spine-like at the base.
    ///  - Dead fronds hanging under the crown, like untrimmed palms in the city.
    ///  - LOD0 full detail, LOD1 about 1/6 of the triangles, LOD2 a few broad fronds; then culled.
    /// A few variants are generated and instanced (rotation and scale vary per tree).
    /// </summary>
    public static class PalmKit
    {
        public const int Variants = 6;
        const string MeshDir = "Assets/Masar/Generated/Meshes";

        public sealed class Variant
        {
            public Mesh[] Trunk = new Mesh[3];
            public Mesh[] Leaves = new Mesh[3];
            public Mesh[] Dead = new Mesh[3];
            public float Height;
        }

        private static Variant[] s_Variants;

        public static void BuildVariants()
        {
            s_Variants = new Variant[Variants];
            for (int v = 0; v < Variants; v++) s_Variants[v] = BuildVariant(1000 + v * 37);
        }

        /// <summary>Places one palm (with LODGroup and trunk collider) under parent.</summary>
        public static GameObject Place(Transform parent, CityMaterials m, Vector3 basePos, int seed)
        {
            if (s_Variants == null) BuildVariants();
            var rnd = new System.Random(seed);
            var v = s_Variants[seed % Variants];
            var root = new GameObject($"DatePalm_{seed}");
            root.transform.SetParent(parent, false);
            root.transform.position = basePos;
            root.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            float s = 0.88f + (float)rnd.NextDouble() * 0.3f;
            root.transform.localScale = new Vector3(s, s, s);

            var lods = new LOD[3];
            float[] thresholds = { 0.30f, 0.09f, 0.012f };
            for (int l = 0; l < 3; l++)
            {
                var rs = new List<Renderer>();
                rs.Add(Part(root.transform, $"LOD{l}_Trunk", v.Trunk[l], m.PalmTrunk));
                rs.Add(Part(root.transform, $"LOD{l}_Leaves", v.Leaves[l], m.PalmFrond));
                if (v.Dead[l] != null) rs.Add(Part(root.transform, $"LOD{l}_Dead", v.Dead[l], m.PalmDead));
                lods[l] = new LOD(thresholds[l], rs.ToArray());
            }
            var group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(lods);
            group.RecalculateBounds();
            GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.3f;
            col.height = v.Height;
            col.center = new Vector3(0f, v.Height * 0.5f, 0f);
            return root;
        }

        private static Renderer Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            return r;
        }

        // ------------------------------------------------------------------ variant

        private static Variant BuildVariant(int seed)
        {
            var rnd = new System.Random(seed);
            var v = new Variant();
            float height = 6.8f + (float)rnd.NextDouble() * 2.6f;
            Vector2 lean = new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 0.55f;
            v.Height = height;

            // Shared frond layout so all LODs show the same tree
            int count = 36 + rnd.Next(10);
            var fronds = new List<(float yaw, float pitch, float len, float droop)>();
            float golden = 137.508f;
            for (int i = 0; i < count; i++)
            {
                float age = i / (float)(count - 1);                         // 0 = youngest (top), 1 = oldest
                float yaw = i * golden + (float)rnd.NextDouble() * 12f;
                float pitch = Mathf.Lerp(-62f, 28f, age) + ((float)rnd.NextDouble() - 0.5f) * 14f; // negative = upward
                float len = Mathf.Lerp(2.6f, 4.3f, Mathf.Sin(Mathf.Clamp01(age * 1.2f) * Mathf.PI * 0.5f)) * (0.9f + 0.2f * (float)rnd.NextDouble());
                float droop = Mathf.Lerp(18f, 70f, age);
                fronds.Add((yaw, pitch, len, droop));
            }
            int deadCount = 5 + rnd.Next(5);

            for (int l = 0; l < 3; l++)
            {
                var trunk = new MeshBuilder();
                var leaves = new MeshBuilder();
                var dead = new MeshBuilder();
                Vector3 crown = BuildTrunk(trunk, height, lean, l);
                var lr = new System.Random(seed + 7);
                int step = l == 0 ? 1 : l == 1 ? 2 : 4;
                for (int i = 0; i < fronds.Count; i += step)
                {
                    var f = fronds[i];
                    Frond(leaves, lr, crown, f.yaw, f.pitch, f.len, f.droop, l, false);
                }
                if (l < 2)
                    for (int d = 0; d < deadCount; d++)
                    {
                        float yaw = d * (360f / deadCount) + (float)lr.NextDouble() * 25f;
                        Frond(dead, lr, crown - Vector3.up * 0.35f, yaw, 62f + (float)lr.NextDouble() * 20f, 2.6f + (float)lr.NextDouble() * 0.8f, 25f, l + 1, true);
                    }
                // Leaf-base cluster ("boots") where the fronds meet the trunk
                dead.Cylinder(crown - Vector3.up * 0.9f, 0.34f, 0.42f, 0.95f, l == 0 ? 14 : 8, false);

                trunk.FixWinding(); dead.FixWinding();
                v.Trunk[l] = Save(trunk, $"Palm{seed}_LOD{l}_Trunk", tangents: true);
                v.Leaves[l] = Save(leaves, $"Palm{seed}_LOD{l}_Leaves");
                v.Dead[l] = dead.Count > 0 ? Save(dead, $"Palm{seed}_LOD{l}_Dead") : null;
            }
            return v;
        }

        /// <summary>Trunk with leaf-base relief. Returns the crown point.</summary>
        private static Vector3 BuildTrunk(MeshBuilder b, float height, Vector2 lean, int lod)
        {
            int seg = lod == 0 ? 18 : lod == 1 ? 10 : 6;
            int rows = lod == 0 ? Mathf.CeilToInt(height * 7f) : lod == 1 ? Mathf.CeilToInt(height * 2f) : 4;
            Vector3 Axis(float t) => new Vector3(lean.x * t * t, t * height, lean.y * t * t);
            int start = b.Count;
            for (int r = 0; r <= rows; r++)
            {
                float t = r / (float)rows;
                float y = t * height;
                Vector3 c = Axis(t);
                float baseR = Mathf.Lerp(0.30f, 0.23f, t) * (1f + 0.25f * Mathf.Exp(-y * 3f));  // flared base
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    // Diamond lattice of cut leaf bases: two opposing helices
                    float d1 = Mathf.Sin(a * 4f + y * 7.5f), d2 = Mathf.Sin(-a * 4f + y * 7.5f);
                    float boot = lod == 0 ? 0.035f * Mathf.Max(0f, Mathf.Max(d1, d2)) : 0f;
                    float r0 = baseR + boot;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    b.AddVertex(c + dir * r0, dir, new Vector2(i / (float)seg * 2f, y / 1.4f));
                }
            }
            int cols = seg + 1;
            for (int r = 0; r < rows; r++)
                for (int i = 0; i < seg; i++)
                {
                    int k = start + r * cols + i;
                    b.T.Add(k); b.T.Add(k + cols); b.T.Add(k + cols + 1);
                    b.T.Add(k); b.T.Add(k + cols + 1); b.T.Add(k + 1);
                }
            return Axis(1f);
        }

        /// <summary>One frond: curved rachis plus two rows of V-folded leaflets.</summary>
        private static void Frond(MeshBuilder b, System.Random rnd, Vector3 crown, float yawDeg, float pitchDeg, float len, float droopDeg, int lod, bool deadFrond)
        {
            int samples = lod == 0 ? 24 : lod == 1 ? 12 : 6;
            var pts = new Vector3[samples + 1];
            var tan = new Vector3[samples + 1];
            Quaternion yaw = Quaternion.Euler(0f, yawDeg, 0f);
            Vector3 p = crown;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float pitch = pitchDeg + droopDeg * t * t;
                Vector3 d = yaw * Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward;
                pts[i] = p;
                tan[i] = d;
                p += d * (len / samples);
            }
            Vector3 side0 = yaw * Vector3.right;
            Vector3 crownCenter = crown + Vector3.up * 0.2f;

            // Rachis: a thin tapered triangular prism
            for (int i = 0; i < samples; i++)
            {
                float w0 = Mathf.Lerp(0.045f, 0.008f, i / (float)samples), w1 = Mathf.Lerp(0.045f, 0.008f, (i + 1) / (float)samples);
                Vector3 up0 = Vector3.Cross(tan[i], side0).normalized, up1 = Vector3.Cross(tan[i + 1], side0).normalized;
                Vector3 a0 = pts[i] - side0 * w0, b0 = pts[i] + side0 * w0, c0 = pts[i] - up0 * w0;
                Vector3 a1 = pts[i + 1] - side0 * w1, b1 = pts[i + 1] + side0 * w1, c1 = pts[i + 1] - up1 * w1;
                var uv = new Vector2(0.2f, 0.5f);
                Tri(b, a0, b0, b1, -up0, uv); Tri(b, a0, b1, a1, -up0, uv);
                Tri(b, b0, c0, c1, side0, uv); Tri(b, b0, c1, b1, side0, uv);
                Tri(b, c0, a0, a1, -side0, uv); Tri(b, c0, a1, c1, -side0, uv);
            }

            // Leaflets
            float spacing = lod == 0 ? 0.034f : lod == 1 ? 0.085f : 0.22f;
            float widthMul = lod == 0 ? 1f : lod == 1 ? 1.9f : 4.5f;
            float vAngle = deadFrond ? 15f : 38f;                    // leaflets rise into a V
            float sweep = deadFrond ? 55f : 32f;                     // leaflets point toward the tip
            for (float s = 0.06f; s < len - 0.05f; s += spacing)
            {
                float t = s / len;
                int i = Mathf.Min(samples - 1, Mathf.FloorToInt(t * samples));
                float ft = t * samples - i;
                Vector3 at = Vector3.Lerp(pts[i], pts[i + 1], ft);
                Vector3 tg = Vector3.Lerp(tan[i], tan[i + 1], ft).normalized;
                Vector3 up = Vector3.Cross(tg, side0).normalized;
                if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
                // Leaflet length: short spines near the base, longest in the middle, short at the tip
                float lenL = (t < 0.15f ? Mathf.Lerp(0.10f, 0.32f, t / 0.15f) : Mathf.Lerp(0.55f, 0.18f, Mathf.Pow((t - 0.15f) / 0.85f, 1.4f)))
                             * (0.9f + 0.2f * (float)rnd.NextDouble());
                float width = (t < 0.15f ? 0.012f : 0.028f) * widthMul;
                float row = (float)rnd.NextDouble();
                foreach (int sd in new[] { -1, 1 })
                {
                    float jitter = ((float)rnd.NextDouble() - 0.5f) * 10f;
                    Vector3 dir = Quaternion.AngleAxis(sd * (vAngle + jitter), tg) * (side0 * sd);
                    dir = Vector3.Slerp(dir, tg, sweep / 90f).normalized;
                    Vector3 tip = at + dir * lenL - Vector3.up * lenL * (deadFrond ? 0.35f : 0.10f);
                    Vector3 mid = Vector3.Lerp(at, tip, 0.45f);
                    Vector3 across = Vector3.Cross(dir, up).normalized * width;
                    Vector3 fold = up * width * 0.45f;                 // raised edges: the leaflet fold
                    Vector3 l1 = mid + across + fold, r1 = mid - across + fold;
                    // Normal: mostly "away from the crown" (soft foliage shading), partly the leaflet's own face
                    Vector3 sph = (mid - crownCenter).normalized;
                    Vector3 n = Vector3.Normalize(sph * 0.55f + up * 0.45f);
                    var uvB = new Vector2(0.02f, row); var uvM = new Vector2(0.45f, row); var uvT = new Vector2(0.98f, row);
                    int bi = b.AddVertex(at, n, uvB);
                    int li = b.AddVertex(l1, n, uvM);
                    int ri = b.AddVertex(r1, n, uvM);
                    int ti = b.AddVertex(tip, n, uvT);
                    b.T.Add(bi); b.T.Add(li); b.T.Add(ti);
                    b.T.Add(bi); b.T.Add(ti); b.T.Add(ri);
                }
            }
        }

        private static void Tri(MeshBuilder b, Vector3 a, Vector3 c, Vector3 d, Vector3 n, Vector2 uv)
        {
            int i0 = b.AddVertex(a, n, uv), i1 = b.AddVertex(c, n, uv), i2 = b.AddVertex(d, n, uv);
            b.T.Add(i0); b.T.Add(i1); b.T.Add(i2);
        }

        private static Mesh Save(MeshBuilder b, string name, bool tangents = false)
        {
            var mesh = b.ToMesh(name, recalcTangents: tangents);
            string path = $"{MeshDir}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
