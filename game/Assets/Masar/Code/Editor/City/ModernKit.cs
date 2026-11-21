using Masar.World;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// Contemporary Jeddah architecture (Tahlia Street and the newer parts of Al Rawdah are full of it):
    /// - Glass towers: curtain walls with a mullion grid, cladding corner fins, a showroom podium with a
    ///   canopy and a rooftop crown.
    /// - Modern apartments: aluminium-composite cladding (white, light grey, champagne, charcoal), ribbon
    ///   windows, coloured accent fins, glass balconies, shops at street level.
    /// - Modern villas: offset cubic volumes, large glazing, cantilevered roof slabs, slatted compound walls.
    /// Built in local space with the front facing +Z, like <see cref="BuildingKit"/>.
    /// </summary>
    public static class ModernKit
    {
        public sealed class Palette
        {
            public Material[] Glass, Cladding, Accent;
            public Material Mullion, Slab, ShopGlass, Railing, Slats;
        }

        static Palette s_P;

        public static Palette Materials()
        {
            if (s_P != null) return s_P;
            s_P = new Palette
            {
                Glass = new[]
                {
                    MaterialLibrary.Solid("Modern_Glass_BlueGreen", new Color(0.16f, 0.30f, 0.34f), 0.96f, 0.85f),
                    MaterialLibrary.Solid("Modern_Glass_Silver", new Color(0.42f, 0.46f, 0.50f), 0.97f, 0.9f),
                    MaterialLibrary.Solid("Modern_Glass_Bronze", new Color(0.30f, 0.24f, 0.18f), 0.96f, 0.85f),
                    MaterialLibrary.Solid("Modern_Glass_DeepBlue", new Color(0.08f, 0.16f, 0.30f), 0.96f, 0.85f),
                },
                Cladding = new[]
                {
                    MaterialLibrary.Solid("Modern_Clad_White", new Color(0.93f, 0.93f, 0.92f), 0.55f, 0.25f),
                    MaterialLibrary.Solid("Modern_Clad_LightGrey", new Color(0.72f, 0.73f, 0.74f), 0.55f, 0.35f),
                    MaterialLibrary.Solid("Modern_Clad_Champagne", new Color(0.78f, 0.70f, 0.58f), 0.6f, 0.5f),
                    MaterialLibrary.Solid("Modern_Clad_Charcoal", new Color(0.20f, 0.21f, 0.22f), 0.55f, 0.4f),
                },
                Accent = new[]
                {
                    MaterialLibrary.Solid("Modern_Accent_Teak", new Color(0.45f, 0.29f, 0.17f), 0.45f, 0f),
                    MaterialLibrary.Solid("Modern_Accent_Teal", new Color(0.05f, 0.42f, 0.42f), 0.6f, 0.3f),
                    MaterialLibrary.Solid("Modern_Accent_Copper", new Color(0.62f, 0.36f, 0.22f), 0.6f, 0.7f),
                    MaterialLibrary.Solid("Modern_Accent_Graphite", new Color(0.12f, 0.12f, 0.13f), 0.6f, 0.5f),
                },
                Mullion = MaterialLibrary.Solid("Modern_Mullion", new Color(0.10f, 0.11f, 0.12f), 0.6f, 0.8f),
                Slab = MaterialLibrary.Solid("Modern_Slab", new Color(0.88f, 0.88f, 0.86f), 0.4f),
                ShopGlass = MaterialLibrary.Solid("Modern_ShopGlass", new Color(0.18f, 0.22f, 0.24f), 0.95f, 0.6f),
                Railing = MaterialLibrary.Solid("Modern_GlassRailing", new Color(0.35f, 0.48f, 0.50f), 0.95f, 0.7f),
                Slats = MaterialLibrary.Solid("Modern_Slats", new Color(0.24f, 0.24f, 0.25f), 0.5f, 0.6f),
            };
            return s_P;
        }

        // ------------------------------------------------------------------ helpers

        static void Box(MeshBuilder b, Matrix4x4 xf, Vector3 min, Vector3 max, float uv = 2f, bool bottom = true)
        {
            b.Transform = xf;
            b.Box(min, max, uv, bottom);
            b.Transform = Matrix4x4.identity;
        }

        /// <summary>A flat facade panel on the plane z = zFace (local), facing +Z rotated by rot around Y.</summary>
        static void Panel(MeshBuilder b, Matrix4x4 xf, float x0, float x1, float y0, float y1, float zFace, float uv)
        {
            b.Transform = xf;
            // Normal = cross(b - a, d - a) = +Z
            b.Quad(new Vector3(x0, y0, zFace), new Vector3(x1, y0, zFace), new Vector3(x1, y1, zFace), new Vector3(x0, y1, zFace), uv);
            b.Transform = Matrix4x4.identity;
        }

        /// <summary>Transform for the face of a W x D box: face 0 = front (+Z), 1 = right (+X), 2 = back, 3 = left.</summary>
        static Matrix4x4 Face(Matrix4x4 xf, int face, float W, float D, out float halfWidth, out float offset)
        {
            float yaw = face * 90f;
            halfWidth = face % 2 == 0 ? W * 0.5f : D * 0.5f;
            offset = face % 2 == 0 ? D * 0.5f : W * 0.5f;
            return xf * Matrix4x4.Rotate(Quaternion.Euler(0f, yaw, 0f));
        }

        // ------------------------------------------------------------------ glass tower

        public static float GlassTower(MeshBatch batch, Vector3 pos, float yawDeg, float W, float D, int floors, int seed)
        {
            var p = Materials();
            var rnd = new System.Random(seed);
            var glass = p.Glass[rnd.Next(p.Glass.Length)];
            var clad = p.Cladding[rnd.Next(3)];
            var xf = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one);
            const float podiumH = 9f, fh = 3.7f;
            float towerH = floors * fh;
            float H = podiumH + towerH;
            float inset = Mathf.Min(2.5f, Mathf.Min(W, D) * 0.12f);
            float tw = W - inset * 2f, td = D - inset * 2f;

            // Podium: showroom glass between cladding piers, fascia band, canopy over the front
            var pg = batch[p.ShopGlass];
            var pc = batch[clad];
            for (int f = 0; f < 4; f++)
            {
                var fx = Face(xf, f, W, D, out float hw, out float off);
                Panel(pg, fx, -hw, hw, 0.2f, podiumH - 2.2f, off, 3f);
                Panel(pc, fx, -hw, hw, podiumH - 2.2f, podiumH, off + 0.02f, 2f);
                int piers = Mathf.Max(2, Mathf.RoundToInt(hw * 2f / 6f));
                for (int k = 0; k <= piers; k++)
                {
                    float x = -hw + k * (hw * 2f / piers);
                    Box(pc, fx, new Vector3(x - 0.3f, 0f, off - 0.1f), new Vector3(x + 0.3f, podiumH - 2.2f, off + 0.25f));
                }
            }
            Box(batch[p.Slab], xf, new Vector3(-W * 0.5f - 0.5f, podiumH - 2.6f, D * 0.5f), new Vector3(W * 0.5f + 0.5f, podiumH - 2.3f, D * 0.5f + 3f));
            Box(pc, xf, new Vector3(-W * 0.5f, podiumH - 0.01f, -D * 0.5f), new Vector3(W * 0.5f, podiumH + 0.3f, D * 0.5f), 4f, bottom: false);

            // Tower: curtain wall + mullion grid on every face
            var gm = batch[glass];
            var mm = batch[p.Mullion];
            float y0 = podiumH + 0.3f, y1 = H;
            for (int f = 0; f < 4; f++)
            {
                var fx = Face(xf * Matrix4x4.Translate(Vector3.zero), f, tw, td, out float hw, out float off);
                Panel(gm, fx, -hw, hw, y0, y1, off, 6f);
                int cols = Mathf.Max(2, Mathf.RoundToInt(hw * 2f / 1.6f));
                for (int k = 1; k < cols; k++)
                {
                    float x = -hw + k * (hw * 2f / cols);
                    Box(mm, fx, new Vector3(x - 0.04f, y0, off), new Vector3(x + 0.04f, y1, off + 0.12f), 1f, bottom: false);
                }
                for (int fl = 1; fl < floors; fl++)
                {
                    float y = y0 + fl * fh;
                    Box(mm, fx, new Vector3(-hw, y - 0.06f, off), new Vector3(hw, y + 0.06f, off + 0.14f), 1f);
                }
                // Cladding fins at the corners
                Box(pc, fx, new Vector3(hw - 0.7f, y0, off - 0.3f), new Vector3(hw + 0.15f, y1 + 1.2f, off + 0.35f), 2f);
            }
            // Roof and crown
            Box(pc, xf, new Vector3(-tw * 0.5f, y1, -td * 0.5f), new Vector3(tw * 0.5f, y1 + 1.2f, td * 0.5f), 4f, bottom: false);
            Box(batch[p.Cladding[3]], xf, new Vector3(-tw * 0.25f, y1 + 1.2f, -td * 0.25f), new Vector3(tw * 0.25f, y1 + 4.2f, td * 0.25f), 3f, bottom: false);
            return H + 4.2f;
        }

        // ------------------------------------------------------------------ modern apartments

        public static float Apartment(MeshBatch batch, Vector3 pos, float yawDeg, float W, float D, int floors, bool shops, int seed)
        {
            var p = Materials();
            var rnd = new System.Random(seed);
            var clad = p.Cladding[rnd.Next(p.Cladding.Length)];
            var clad2 = p.Cladding[(System.Array.IndexOf(p.Cladding, clad) + 1 + rnd.Next(2)) % p.Cladding.Length];
            var accent = p.Accent[rnd.Next(p.Accent.Length)];
            var glass = p.Glass[rnd.Next(p.Glass.Length)];
            var xf = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one);
            const float fh = 3.4f;
            float gH = shops ? 5f : 3.6f;
            float H = gH + floors * fh;

            // Body
            var body = batch[clad];
            Box(body, xf, new Vector3(-W * 0.5f, 0f, -D * 0.5f), new Vector3(W * 0.5f, H, D * 0.5f), 3f, bottom: false);
            // Ground floor: shops or a glazed lobby, on the front
            Panel(batch[p.ShopGlass], xf, -W * 0.5f + 0.4f, W * 0.5f - 0.4f, 0.15f, gH - 0.8f, D * 0.5f + 0.02f, 3f);
            Box(batch[clad2], xf, new Vector3(-W * 0.5f, gH - 0.8f, D * 0.5f), new Vector3(W * 0.5f, gH, D * 0.5f + 0.5f));
            // Ribbon windows on all faces: a glass band per floor with dark frames
            var gm = batch[glass];
            var fr = batch[p.Mullion];
            for (int f = 0; f < 4; f++)
            {
                var fx = Face(xf, f, W, D, out float hw, out float off);
                for (int fl = 0; fl < floors; fl++)
                {
                    float y = gH + fl * fh;
                    float x0 = -hw + 0.8f, x1 = hw - 0.8f;
                    if (f == 0) { x0 = -hw + 0.8f; x1 = hw * 0.35f; }   // front: windows left, accent bay right
                    Panel(gm, fx, x0, x1, y + 0.7f, y + fh - 0.5f, off + 0.03f, 3f);
                    Box(fr, fx, new Vector3(x0, y + 0.6f, off), new Vector3(x1, y + 0.7f, off + 0.1f), 1f);
                    Box(fr, fx, new Vector3(x0, y + fh - 0.5f, off), new Vector3(x1, y + fh - 0.42f, off + 0.1f), 1f);
                }
            }
            // Front accent bay: vertical fins in the accent colour with glass balconies
            {
                var fx = Face(xf, 0, W, D, out float hw, out float off);
                float bx0 = hw * 0.35f + 0.4f, bx1 = hw - 0.4f;
                Box(batch[accent], fx, new Vector3(bx0, gH, off), new Vector3(bx1, H, off + 0.15f), 2f);
                int fins = Mathf.Max(2, Mathf.RoundToInt((bx1 - bx0) / 1.1f));
                for (int k = 0; k <= fins; k++)
                {
                    float x = bx0 + k * (bx1 - bx0) / fins;
                    Box(batch[clad2], fx, new Vector3(x - 0.08f, gH, off + 0.15f), new Vector3(x + 0.08f, H + 0.6f, off + 0.75f), 1f);
                }
                for (int fl = 0; fl < floors; fl++)
                {
                    float y = gH + fl * fh;
                    Box(batch[p.Slab], fx, new Vector3(-hw * 0.6f, y, off), new Vector3(hw * 0.3f, y + 0.18f, off + 1.4f), 2f);
                    Box(batch[p.Railing], fx, new Vector3(-hw * 0.6f, y + 0.18f, off + 1.35f), new Vector3(hw * 0.3f, y + 1.15f, off + 1.4f), 2f);
                }
            }
            // Parapet + rooftop units
            Box(batch[clad2], xf, new Vector3(-W * 0.5f, H, -D * 0.5f), new Vector3(W * 0.5f, H + 0.9f, D * 0.5f), 3f, bottom: false);
            Box(batch[p.Cladding[1]], xf, new Vector3(-W * 0.2f, H, -D * 0.2f), new Vector3(W * 0.15f, H + 2.6f, D * 0.1f), 2f, bottom: false);
            return H + 0.9f;
        }

        // ------------------------------------------------------------------ modern villa

        public static void Villa(MeshBatch batch, Vector3 plotCenter, float yawDeg, float plotW, float plotD, int seed)
        {
            var p = Materials();
            var rnd = new System.Random(seed);
            var clad = p.Cladding[rnd.Next(2)];          // white or light grey
            var clad2 = p.Cladding[2 + rnd.Next(2)];     // champagne or charcoal
            var accent = p.Accent[rnd.Next(p.Accent.Length)];
            var xf = Matrix4x4.TRS(plotCenter, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one);
            float hw = plotW * 0.5f, hd = plotD * 0.5f;
            // Compound wall: solid base with horizontal slat panels, gate opening on the front
            var wall = batch[clad];
            var slats = batch[p.Slats];
            Box(wall, xf, new Vector3(-hw, 0f, hd - 0.3f), new Vector3(-2.4f, 1.2f, hd), 2f);
            Box(wall, xf, new Vector3(2.4f, 0f, hd - 0.3f), new Vector3(hw, 1.2f, hd), 2f);
            for (float y = 1.35f; y < 3.0f; y += 0.3f)
            {
                Box(slats, xf, new Vector3(-hw, y, hd - 0.2f), new Vector3(-2.4f, y + 0.16f, hd - 0.1f), 1f);
                Box(slats, xf, new Vector3(2.4f, y, hd - 0.2f), new Vector3(hw, y + 0.16f, hd - 0.1f), 1f);
            }
            Box(wall, xf, new Vector3(-hw, 0f, -hd), new Vector3(hw, 3f, -hd + 0.3f), 2f);
            Box(wall, xf, new Vector3(-hw, 0f, -hd), new Vector3(-hw + 0.3f, 3f, hd), 2f);
            Box(wall, xf, new Vector3(hw - 0.3f, 0f, -hd), new Vector3(hw, 3f, hd), 2f);
            Box(batch[p.Mullion], xf, new Vector3(-2.4f, 0f, hd - 0.15f), new Vector3(2.4f, 2.6f, hd - 0.05f), 1f);   // gate

            // House: two offset volumes
            float hx = plotW * 0.62f, hz = plotD * 0.5f, back = -hd + 2.5f;
            float v1w = hx * 0.6f, v2w = hx * 0.5f;
            var c1 = new Vector3(-hx * 0.2f, 0f, back + hz * 0.5f);
            var c2 = new Vector3(hx * 0.25f, 0f, back + hz * 0.6f);
            Box(batch[clad], xf, c1 + new Vector3(-v1w * 0.5f, 0f, -hz * 0.5f), c1 + new Vector3(v1w * 0.5f, 3.6f, hz * 0.5f), 3f, bottom: false);
            Box(batch[clad2], xf, c2 + new Vector3(-v2w * 0.5f, 3.6f, -hz * 0.45f), c2 + new Vector3(v2w * 0.5f, 7.0f, hz * 0.45f), 3f, bottom: false);
            Box(batch[clad], xf, c2 + new Vector3(-v2w * 0.5f, 0f, -hz * 0.45f), c2 + new Vector3(v2w * 0.5f, 3.6f, hz * 0.4f), 3f, bottom: false);
            // Big glazing on the front faces
            var g = batch[p.Glass[1]];
            Panel(g, xf, c1.x - v1w * 0.4f, c1.x + v1w * 0.4f, 0.3f, 3.1f, c1.z + hz * 0.5f + 0.03f, 3f);
            Panel(g, xf, c2.x - v2w * 0.4f, c2.x + v2w * 0.3f, 4.0f, 6.4f, c2.z + hz * 0.45f + 0.03f, 3f);
            // Cantilevered roof slab and an accent panel
            Box(batch[p.Slab], xf, c1 + new Vector3(-v1w * 0.5f - 0.6f, 3.6f, -hz * 0.5f), c1 + new Vector3(v1w * 0.5f + 0.4f, 3.9f, hz * 0.5f + 1.8f), 3f);
            Box(batch[p.Slab], xf, c2 + new Vector3(-v2w * 0.5f - 0.3f, 7.0f, -hz * 0.45f - 0.3f), c2 + new Vector3(v2w * 0.5f + 0.3f, 7.3f, hz * 0.45f + 1.2f), 3f);
            Box(batch[accent], xf, c2 + new Vector3(v2w * 0.3f, 3.9f, hz * 0.45f), c2 + new Vector3(v2w * 0.5f, 7.0f, hz * 0.45f + 0.1f), 2f);
        }
    }
}
