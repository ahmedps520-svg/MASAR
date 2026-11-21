using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Masar.World
{
    /// <summary>
    /// Small procedural mesh builder with world-scaled UVs, used by the city and vehicle
    /// generators. Quads, boxes, extrusions, cylinders and lofted surfaces.
    /// </summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>(1024);
        public readonly List<Vector3> N = new List<Vector3>(1024);
        public readonly List<Vector2> UV = new List<Vector2>(1024);
        public readonly List<Color> C = new List<Color>(1024);
        public readonly List<int> T = new List<int>(2048);
        public Color VertexColor = Color.white;
        public Matrix4x4 Transform = Matrix4x4.identity;

        public int Count => V.Count;

        public int AddVertex(Vector3 p, Vector3 n, Vector2 uv)
        {
            V.Add(Transform.MultiplyPoint3x4(p));
            N.Add(Transform.MultiplyVector(n).normalized);
            UV.Add(uv);
            C.Add(VertexColor);
            return V.Count - 1;
        }

        /// <summary>Quad from 4 corners (counter-clockwise when seen from the front, i.e. along -normal). UVs = world meters / uvScale.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvScale = 1f, Vector2 uvOffset = default)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            // Planar UV projection on the quad's own axes, in meters
            Vector3 ax = (b - a).normalized;
            Vector3 ay = Vector3.Cross(n, ax);
            Vector2 UVof(Vector3 p) => new Vector2(Vector3.Dot(p - a, ax), Vector3.Dot(p - a, ay)) / uvScale + uvOffset;
            int i0 = AddVertex(a, n, UVof(a));
            int i1 = AddVertex(b, n, UVof(b));
            int i2 = AddVertex(c, n, UVof(c));
            int i3 = AddVertex(d, n, UVof(d));
            T.Add(i0); T.Add(i1); T.Add(i2);
            T.Add(i0); T.Add(i2); T.Add(i3);
        }

        /// <summary>Quad with explicit UVs.</summary>
        public void QuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            int i0 = AddVertex(a, n, ua), i1 = AddVertex(b, n, ub), i2 = AddVertex(c, n, uc), i3 = AddVertex(d, n, ud);
            T.Add(i0); T.Add(i1); T.Add(i2);
            T.Add(i0); T.Add(i2); T.Add(i3);
        }

        /// <summary>Horizontal rectangle at height y, facing up. World-space UV.</summary>
        public void FloorRect(float x0, float z0, float x1, float z1, float y, float uvScale = 1f)
        {
            var a = new Vector3(x0, y, z0); var b = new Vector3(x0, y, z1); var c = new Vector3(x1, y, z1); var d = new Vector3(x1, y, z0);
            QuadUV(a, b, c, d, new Vector2(x0, z0) / uvScale, new Vector2(x0, z1) / uvScale, new Vector2(x1, z1) / uvScale, new Vector2(x1, z0) / uvScale);
        }

        /// <summary>
        /// Axis-aligned box (min/max), all 6 faces outward. The bottom face is on by default,
        /// because canopies, balcony slabs, sills and AC units are seen from below at street level.
        /// Pass bottom: false only for boxes that sit on the ground.
        /// </summary>
        public void Box(Vector3 min, Vector3 max, float uvScale = 1f, bool bottom = true, bool top = true)
        {
            Vector3 p000 = new Vector3(min.x, min.y, min.z), p100 = new Vector3(max.x, min.y, min.z);
            Vector3 p010 = new Vector3(min.x, max.y, min.z), p110 = new Vector3(max.x, max.y, min.z);
            Vector3 p001 = new Vector3(min.x, min.y, max.z), p101 = new Vector3(max.x, min.y, max.z);
            Vector3 p011 = new Vector3(min.x, max.y, max.z), p111 = new Vector3(max.x, max.y, max.z);
            Quad(p000, p010, p110, p100, uvScale); // -Z
            Quad(p101, p111, p011, p001, uvScale); // +Z
            Quad(p001, p011, p010, p000, uvScale); // -X
            Quad(p100, p110, p111, p101, uvScale); // +X
            if (top) Quad(p010, p011, p111, p110, uvScale);    // +Y
            if (bottom) Quad(p000, p100, p101, p001, uvScale); // -Y
        }

        /// <summary>Box centered at c with size s.</summary>
        public void BoxC(Vector3 c, Vector3 s, float uvScale = 1f, bool bottom = true) => Box(c - s * 0.5f, c + s * 0.5f, uvScale, bottom);

        /// <summary>Vertical cylinder (or cone/frustum) from y0 to y1.</summary>
        public void Cylinder(Vector3 baseCenter, float r0, float r1, float height, int segments, bool cap = true, float uvScale = 1f)
        {
            int start = V.Count;
            float circ = 2f * Mathf.PI * Mathf.Max(r0, r1);
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 n = new Vector3(dir.x * height, r0 - r1, dir.z * height).normalized;
                float u = i / (float)segments * circ / uvScale;
                AddVertex(baseCenter + dir * r0, n, new Vector2(u, 0f));
                AddVertex(baseCenter + dir * r1 + Vector3.up * height, n, new Vector2(u, height / uvScale));
            }
            for (int i = 0; i < segments; i++)
            {
                int k = start + i * 2;
                T.Add(k); T.Add(k + 1); T.Add(k + 3);
                T.Add(k); T.Add(k + 3); T.Add(k + 2);
            }
            if (cap && r1 > 0.001f)
            {
                int c = AddVertex(baseCenter + Vector3.up * height, Vector3.up, new Vector2(0.5f, 0.5f));
                int ring = V.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    AddVertex(baseCenter + new Vector3(Mathf.Cos(a) * r1, height, Mathf.Sin(a) * r1), Vector3.up,
                        new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                }
                for (int i = 0; i < segments; i++) { T.Add(c); T.Add(ring + i + 1); T.Add(ring + i); }
            }
        }

        /// <summary>Cylinder along an arbitrary axis (e.g. wheels: axis = X).</summary>
        public void CylinderAxis(Vector3 center, Vector3 axis, float radius, float width, int segments, bool caps)
        {
            axis.Normalize();
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(axis, u);
            Vector3 h = axis * (width * 0.5f);
            int start = V.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 d = u * Mathf.Cos(a) + w * Mathf.Sin(a);
                AddVertex(center + d * radius - h, d, new Vector2(i / (float)segments * 4f, 0f));
                AddVertex(center + d * radius + h, d, new Vector2(i / (float)segments * 4f, 1f));
            }
            for (int i = 0; i < segments; i++)
            {
                int k = start + i * 2;
                T.Add(k); T.Add(k + 3); T.Add(k + 1);
                T.Add(k); T.Add(k + 2); T.Add(k + 3);
            }
            if (!caps) return;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 n = axis * side;
                int c = AddVertex(center + h * side, n, new Vector2(0.5f, 0.5f));
                int ring = V.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    Vector3 d = u * Mathf.Cos(a) + w * Mathf.Sin(a);
                    AddVertex(center + d * radius + h * side, n, new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                }
                for (int i = 0; i < segments; i++)
                {
                    if (side > 0) { T.Add(c); T.Add(ring + i); T.Add(ring + i + 1); }
                    else { T.Add(c); T.Add(ring + i + 1); T.Add(ring + i); }
                }
            }
        }

        /// <summary>
        /// Lofted surface: rings[i] is a closed or open polyline (same vertex count per ring).
        /// Normals are computed smooth from the grid. The u coordinate runs along the ring, v along the loft.
        /// </summary>
        public void Loft(IList<Vector3[]> rings, bool closedRing, bool flip = false)
        {
            int rows = rings.Count;
            int cols = rings[0].Length;
            int start = V.Count;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vector3 p = rings[r][c];
                    Vector3 du = rings[r][(c + 1) % cols] - rings[r][(c - 1 + cols) % cols];
                    if (!closedRing && (c == 0 || c == cols - 1))
                        du = c == 0 ? rings[r][1] - rings[r][0] : rings[r][cols - 1] - rings[r][cols - 2];
                    Vector3 dv = rings[Mathf.Min(r + 1, rows - 1)][c] - rings[Mathf.Max(r - 1, 0)][c];
                    Vector3 n = Vector3.Cross(dv, du).normalized;
                    if (flip) n = -n;
                    AddVertex(p, n, new Vector2(c / (float)(cols - 1), r / (float)(rows - 1)));
                }
            }
            int segCols = closedRing ? cols : cols - 1;
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < segCols; c++)
                {
                    int c1 = (c + 1) % cols;
                    int a = start + r * cols + c, b = start + r * cols + c1;
                    int d = start + (r + 1) * cols + c, e = start + (r + 1) * cols + c1;
                    if (!flip) { T.Add(a); T.Add(d); T.Add(e); T.Add(a); T.Add(e); T.Add(b); }
                    else { T.Add(a); T.Add(e); T.Add(d); T.Add(a); T.Add(b); T.Add(e); }
                }
            }
        }

        /// <summary>
        /// Makes every triangle wind so its geometric normal agrees with its vertex normals.
        /// Unity's front faces are clockwise, i.e. normal = cross(b - a, c - a).
        /// With this, generators only need to get the normals right.
        /// </summary>
        public void FixWinding(int startTriangleIndex = 0)
        {
            for (int t = startTriangleIndex; t + 2 < T.Count; t += 3)
            {
                Vector3 a = V[T[t]], b = V[T[t + 1]], c = V[T[t + 2]];
                Vector3 fn = Vector3.Cross(b - a, c - a);
                Vector3 vn = N[T[t]] + N[T[t + 1]] + N[T[t + 2]];
                if (Vector3.Dot(fn, vn) < 0f) { int tmp = T[t + 1]; T[t + 1] = T[t + 2]; T[t + 2] = tmp; }
            }
        }

        /// <summary>Flips vertex normals in [startVertex, Count) that point toward the supplied interior center.</summary>
        public void OrientOutward(int startVertex, System.Func<Vector3, Vector3> centerOf)
        {
            for (int i = startVertex; i < V.Count; i++)
            {
                Vector3 c = centerOf(V[i]);
                if (Vector3.Dot(N[i], V[i] - c) < 0f) N[i] = -N[i];
            }
        }

        /// <summary>
        /// Surface of revolution around the X axis. profile[i] = (radius, axialX), ordered so that
        /// the outside of the shape is on the left of the direction of travel (see N = (dx, -dr)).
        /// </summary>
        public void RevolveX(Vector3 center, Vector2[] profile, int segments)
        {
            int start = V.Count;
            int rows = profile.Length;
            for (int r = 0; r < rows; r++)
            {
                Vector2 prev = profile[Mathf.Max(0, r - 1)], next = profile[Mathf.Min(rows - 1, r + 1)];
                float dr = next.x - prev.x, dx = next.y - prev.y;
                Vector2 n2 = new Vector2(dx, -dr).normalized; // (radial, axial)
                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    Vector3 radial = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                    Vector3 p = center + radial * profile[r].x + Vector3.right * profile[r].y;
                    Vector3 n = (radial * n2.x + Vector3.right * n2.y).normalized;
                    AddVertex(p, n, new Vector2(s / (float)segments * 6f, r / (float)(rows - 1)));
                }
            }
            int tStart = T.Count;
            int cols = segments + 1;
            for (int r = 0; r < rows - 1; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = start + r * cols + s, b = a + 1, d = a + cols, e = d + 1;
                    T.Add(a); T.Add(d); T.Add(e); T.Add(a); T.Add(e); T.Add(b);
                }
            FixWinding(tStart);
        }

        /// <summary>Triangle fan closing a polygon ring (cap). Normal given explicitly.</summary>
        public void Fan(Vector3[] ring, Vector3 normal)
        {
            Vector3 c = Vector3.zero;
            foreach (var p in ring) c += p;
            c /= ring.Length;
            int tStart = T.Count;
            int ci = AddVertex(c, normal, new Vector2(0.5f, 0.5f));
            int first = V.Count;
            foreach (var p in ring) AddVertex(p, normal, new Vector2(p.x, p.y));
            for (int i = 0; i < ring.Length; i++) { T.Add(ci); T.Add(first + i); T.Add(first + (i + 1) % ring.Length); }
            FixWinding(tStart);
        }

        public Mesh ToMesh(string name, bool recalcTangents = true)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetUVs(0, UV);
            m.SetColors(C);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
            if (recalcTangents) m.RecalculateTangents();
            return m;
        }

        /// <summary>Appends another builder's geometry (already in world space).</summary>
        public void Append(MeshBuilder o)
        {
            int baseIdx = V.Count;
            V.AddRange(o.V); N.AddRange(o.N); UV.AddRange(o.UV); C.AddRange(o.C);
            for (int i = 0; i < o.T.Count; i++) T.Add(o.T[i] + baseIdx);
        }

        public void Clear() { V.Clear(); N.Clear(); UV.Clear(); C.Clear(); T.Clear(); Transform = Matrix4x4.identity; VertexColor = Color.white; }
    }
}
