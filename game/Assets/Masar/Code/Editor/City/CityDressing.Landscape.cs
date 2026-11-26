using System.Collections.Generic;
using Masar.World;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// Landscaping, so the city never shows bare sand lots (owner feedback, 2026-11-22):
    /// - OSM land use: parks/gardens/grass (lawn, paths, trees, palms, benches, lamps, fountain,
    ///   playground), sports pitches (turf, lines, goals, fence), parking (asphalt, bays, parked cars),
    ///   schools and hospitals (building, grounds, trees), construction sites (hoarding, concrete frame,
    ///   tower crane), swimming pools.
    /// - Empty plots left after the buildings: pocket gardens, small car parks or tree groves.
    /// - Small sand gaps between buildings: trees and shrubs.
    /// - Street trees on wide sidewalks.
    /// Ground surfaces are signed-distance fields on the road grid (smooth edges, never over a road or
    /// sidewalk); trees and furniture are merged into the city cells like the buildings.
    /// </summary>
    public sealed partial class CityDressing
    {
        public sealed class LandMaterials
        {
            public Material Grass, Turf, Path, Gravel, Water, Leaves, LeavesDark, Trunk, Hedge, Wood, Fence, Hoarding, Crane, Concrete, Play1, Play2, Line;
        }

        LandMaterials _lm;
        readonly Dictionary<Material, float[]> _land = new Dictionary<Material, float[]>();
        readonly Dictionary<Material, float> _landY = new Dictionary<Material, float>();
        readonly List<(Vector2 a, Vector2 b, float w)> _lines = new List<(Vector2, Vector2, float)>();
        /// <summary>Parked car slots (position, yaw): the builder places traffic car bodies there.</summary>
        public readonly List<(Vector3 pos, float yaw)> ParkedCars = new List<(Vector3, float)>();
        public int Parks, Pitches, ParkingLots, Sites, Trees, PocketParks;

        LandMaterials Land()
        {
            if (_lm != null) return _lm;
            _lm = new LandMaterials
            {
                Grass = MaterialLibrary.Scanned("Land_Grass", "Grass004", new Color(0.86f, 0.92f, 0.78f), 0.5f),
                Turf = MaterialLibrary.Scanned("Land_Turf", "Grass004", new Color(0.70f, 1.0f, 0.62f), 0.6f),
                Path = MaterialLibrary.Scanned("Land_ParkPavers", "PavingStones130", new Color(0.95f, 0.90f, 0.84f), 0.7f),
                Gravel = MaterialLibrary.Scanned("Land_Gravel", "Gravel022", new Color(0.95f, 0.90f, 0.82f), 0.6f),
                Water = MaterialLibrary.Solid("Land_PoolWater", new Color(0.10f, 0.45f, 0.62f), 0.97f, 0.1f),
                Leaves = MaterialLibrary.Solid("Land_TreeLeaves", new Color(0.20f, 0.36f, 0.14f), 0.25f),
                LeavesDark = MaterialLibrary.Solid("Land_TreeLeavesDark", new Color(0.13f, 0.26f, 0.11f), 0.25f),
                Trunk = MaterialLibrary.Solid("Land_TreeTrunk", new Color(0.30f, 0.23f, 0.17f), 0.2f),
                Hedge = MaterialLibrary.Solid("Land_Hedge", new Color(0.17f, 0.32f, 0.12f), 0.2f),
                Wood = MaterialLibrary.Solid("Land_BenchWood", new Color(0.45f, 0.30f, 0.18f), 0.35f),
                Fence = MaterialLibrary.Solid("Land_FenceMetal", new Color(0.16f, 0.18f, 0.19f), 0.5f, 0.6f),
                Hoarding = MaterialLibrary.Solid("Land_Hoarding", new Color(0.86f, 0.87f, 0.85f), 0.35f),
                Crane = MaterialLibrary.Solid("Land_CraneYellow", new Color(0.92f, 0.70f, 0.08f), 0.45f, 0.3f),
                Concrete = _m.Concrete,
                Play1 = MaterialLibrary.Solid("Land_PlayRed", new Color(0.78f, 0.15f, 0.12f), 0.5f),
                Play2 = MaterialLibrary.Solid("Land_PlayBlue", new Color(0.12f, 0.35f, 0.75f), 0.5f),
                Line = _m.PaintWhite,
            };
            _landY[_lm.Grass] = RoadSurface.KerbHeight + 0.012f;
            _landY[_lm.Turf] = RoadSurface.KerbHeight + 0.014f;
            _landY[_lm.Gravel] = RoadSurface.KerbHeight + 0.010f;
            _landY[_m.Asphalt] = RoadSurface.KerbHeight + 0.011f;
            _landY[_lm.Path] = RoadSurface.KerbHeight + 0.022f;
            _landY[_lm.Water] = RoadSurface.KerbHeight + 0.008f;
            return _lm;
        }

        // ------------------------------------------------------------------ ground fields

        float[] Field(Material m)
        {
            if (!_land.TryGetValue(m, out var f))
            {
                f = new float[_surf.Nx * _surf.Nz];
                for (int i = 0; i < f.Length; i++) f[i] = 1000f;
                _land[m] = f;
            }
            return f;
        }

        void Stamp(Material m, Vector2 min, Vector2 max, System.Func<Vector2, float> sdf)
        {
            var f = Field(m);
            int i0 = Mathf.Max(0, Mathf.FloorToInt((min.x - 2f - _surf.Origin.x) / RoadSurface.Cell));
            int i1 = Mathf.Min(_surf.Nx - 1, Mathf.CeilToInt((max.x + 2f - _surf.Origin.x) / RoadSurface.Cell));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((min.y - 2f - _surf.Origin.y) / RoadSurface.Cell));
            int j1 = Mathf.Min(_surf.Nz - 1, Mathf.CeilToInt((max.y + 2f - _surf.Origin.y) / RoadSurface.Cell));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int k = j * _surf.Nx + i;
                    float d = sdf(_surf.GridPos(i, j));
                    if (d < f[k]) f[k] = d;
                }
        }

        void StampPolygon(Material m, List<Vector2> poly, float inset)
        {
            var min = poly[0]; var max = poly[0];
            foreach (var p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            Stamp(m, min, max, p =>
            {
                float d = float.MaxValue;
                for (int i = 0; i < poly.Count; i++) d = Mathf.Min(d, RoadNetwork.SegDist(p, poly[i], poly[(i + 1) % poly.Count], out _));
                return (Geometry.Inside(poly, p) ? -d : d) + inset;
            });
        }

        void StampRect(Material m, Vector2 c, Vector2 ax, float w, float d)
        {
            Vector2 ay = new Vector2(-ax.y, ax.x);
            float r = Mathf.Sqrt(w * w + d * d) * 0.5f;
            Stamp(m, c - Vector2.one * r, c + Vector2.one * r, p =>
            {
                Vector2 q = p - c;
                float u = Mathf.Abs(Vector2.Dot(q, ax)) - w * 0.5f, v = Mathf.Abs(Vector2.Dot(q, ay)) - d * 0.5f;
                return Mathf.Max(u, v) < 0 ? Mathf.Max(u, v) : new Vector2(Mathf.Max(u, 0), Mathf.Max(v, 0)).magnitude;
            });
        }

        void StampStrip(Material m, Vector2 a, Vector2 b, float width)
        {
            Stamp(m, Vector2.Min(a, b), Vector2.Max(a, b), p => RoadNetwork.SegDist(p, a, b, out _) - width * 0.5f);
        }

        /// <summary>Writes all ground surfaces (only off roads and sidewalks) and painted lines into chunk builders.</summary>
        public void EmitGround(System.Func<Vector2, MeshBatch> chunk)
        {
            foreach (var kv in _land)
            {
                var f = kv.Value; var mat = kv.Key;
                float y = _landY.TryGetValue(mat, out var yy) ? yy : RoadSurface.KerbHeight + 0.012f;
                float uv = mat == _lm.Path ? 2f : mat == _lm.Water ? 4f : 3f;
                _surf.Region(k => Mathf.Max(f[k], 0.15f - _surf.S[k]), y, uv, p => chunk(p)[mat]);
            }
            foreach (var (a, b, w) in _lines)
            {
                Vector2 d = (b - a).normalized, n = new Vector2(d.y, -d.x) * (w * 0.5f);
                var mb = chunk((a + b) * 0.5f)[_lm.Line];
                int v = mb.Count;
                float y = RoadSurface.KerbHeight + 0.03f;
                foreach (var p in new[] { a - n, b - n, b + n, a + n }) mb.AddVertex(new Vector3(p.x, y, p.y), Vector3.up, p * 0.25f);
                mb.T.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            }
        }

        // ------------------------------------------------------------------ props (merged into cells)

        void Tree(Vector2 p, float scale, bool dark = false)
        {
            var lm = Land();
            var batch = new MeshBatch();
            var rnd = new System.Random((int)(p.x * 73 + p.y * 151));
            float s = scale * (0.85f + (float)rnd.NextDouble() * 0.35f);
            Vector3 b = new Vector3(p.x, RoadSurface.KerbHeight, p.y);
            float h = 2.4f * s;
            batch[lm.Trunk].Cylinder(b, 0.17f * s, 0.11f * s, h + 0.6f * s, 7, false);
            var leaves = batch[dark ? lm.LeavesDark : lm.Leaves];
            int blobs = 4 + rnd.Next(3);
            for (int i = 0; i < blobs; i++)
            {
                float a = i * 2.39996f + (float)rnd.NextDouble();
                float rr = (i == 0 ? 0f : 0.9f) * s;
                var c = b + new Vector3(Mathf.Cos(a) * rr, h + (0.6f + (float)rnd.NextDouble() * 0.9f) * s, Mathf.Sin(a) * rr);
                Blob(leaves, c, (1.1f + (float)rnd.NextDouble() * 0.5f) * s, rnd);
            }
            Commit(batch, p);
            Trees++;
        }

        /// <summary>A lumpy low-poly sphere (icosahedron, displaced): reads as a leaf mass at street distance.</summary>
        static void Blob(MeshBuilder b, Vector3 c, float r, System.Random rnd)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                        3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var pts = new Vector3[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                float k = 0.82f + (float)rnd.NextDouble() * 0.3f;
                pts[i] = c + Vector3.Scale(v[i].normalized * r * k, new Vector3(1f, 0.78f, 1f));
            }
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = pts[f[i]], bb = pts[f[i + 1]], cc = pts[f[i + 2]];
                Vector3 n = ((a + bb + cc) / 3f - c).normalized;
                int s = b.Count;
                b.AddVertex(a, (a - c).normalized * 0.5f + n * 0.5f, new Vector2(0, 0));
                b.AddVertex(bb, (bb - c).normalized * 0.5f + n * 0.5f, new Vector2(1, 0));
                b.AddVertex(cc, (cc - c).normalized * 0.5f + n * 0.5f, new Vector2(0, 1));
                b.T.Add(s); b.T.Add(s + 1); b.T.Add(s + 2);
            }
        }

        void Bench(Vector2 p, Vector2 facing)
        {
            var lm = Land();
            var batch = new MeshBatch();
            float yaw = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg;
            var xf = Matrix4x4.TRS(new Vector3(p.x, RoadSurface.KerbHeight, p.y), Quaternion.Euler(0, yaw, 0), Vector3.one);
            var w = batch[lm.Wood]; w.Transform = xf;
            w.Box(new Vector3(-0.9f, 0.42f, -0.22f), new Vector3(0.9f, 0.47f, 0.22f));
            w.Box(new Vector3(-0.9f, 0.5f, -0.26f), new Vector3(0.9f, 0.85f, -0.21f));
            var m = batch[lm.Fence]; m.Transform = xf;
            foreach (float x in new[] { -0.75f, 0.75f }) m.Box(new Vector3(x - 0.04f, 0f, -0.25f), new Vector3(x + 0.04f, 0.45f, 0.2f));
            w.Transform = m.Transform = Matrix4x4.identity;
            Commit(batch, p);
        }

        void Lamp(Vector2 p)
        {
            var batch = new MeshBatch();
            var b = new Vector3(p.x, RoadSurface.KerbHeight, p.y);
            batch[_m.PoleMetal].Cylinder(b, 0.07f, 0.05f, 4f, 8, true);
            batch[_m.LampHead].Cylinder(b + Vector3.up * 4f, 0.22f, 0.22f, 0.35f, 10, true);
            Commit(batch, p);
        }

        // ------------------------------------------------------------------ the pass

        void Landscape()
        {
            Land();
            foreach (var w in _map.Ways)
            {
                if (w.Tag("building") != null) continue;
                string lu = w.Tag("landuse"), le = w.Tag("leisure"), am = w.Tag("amenity");
                var poly = _map.Points(w);
                if (poly.Count < 4) continue;
                if ((poly[0] - poly[poly.Count - 1]).sqrMagnitude < 0.01f) poly.RemoveAt(poly.Count - 1);
                if (Geometry.SignedArea(poly) < 0) poly.Reverse();
                float area = Geometry.SignedArea(poly);
                if (area < 60f || !_area.Contains(Geometry.Centroid(poly))) continue;
                if (le == "park" || le == "garden" || lu == "grass" || lu == "village_green" || le == "playground") Park(poly, area);
                else if (le == "pitch") Pitch(poly);
                else if (am == "parking") Parking(poly, 1f);
                else if (le == "swimming_pool") { StampPolygon(_lm.Water, poly, 0f); }
                else if (lu == "construction" || lu == "brownfield") Site(poly);
                else if (am == "school" || am == "hospital" || am == "university" || am == "college") Campus(poly, area, am);
            }
            FillLots();     // whole empty lots: parks, gardens, car parks
            EmptyPlots();   // remaining squares
            StreetTrees();
            GapTrees();
        }

        void Park(List<Vector2> poly, float area)
        {
            Parks++;
            StampPolygon(_lm.Grass, poly, 0f);
            var c = Geometry.Centroid(poly);
            // Paths from the centre to the middle of each long edge
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                if ((b - a).magnitude < 25f) continue;
                var mid = (a + b) * 0.5f;
                StampStrip(_lm.Path, c, Vector2.Lerp(c, mid, 0.97f), 2.6f);
                // Benches and lamps along the path
                for (float t = 0.25f; t < 0.9f; t += 0.3f)
                {
                    var p = Vector2.Lerp(c, mid, t);
                    var dir = (mid - c).normalized; var side = new Vector2(-dir.y, dir.x);
                    Bench(p + side * 2.1f, -side);
                    Lamp(p - side * 1.8f);
                }
            }
            if (area > 1500f)
            {
                // Fountain plaza at the centre
                StampRect(_lm.Path, c, Vector2.right, 12f, 12f);
                var batch = new MeshBatch();
                var b = new Vector3(c.x, RoadSurface.KerbHeight, c.y);
                batch[_m.Marble].Cylinder(b, 3.2f, 3.2f, 0.6f, 24, true);
                batch[_lm.Water].Cylinder(b + Vector3.up * 0.45f, 2.9f, 2.9f, 0.18f, 24, true);
                batch[_m.Marble].Cylinder(b, 0.5f, 0.35f, 1.8f, 12, true);
                Commit(batch, c);
            }
            if (area > 3000f) Playground(c + new Vector2(14f, 9f));
            // Trees and palms on the lawn, clear of the paths
            var min = poly[0]; var max = poly[0];
            foreach (var p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            for (float x = min.x + 4f; x < max.x - 2f; x += 8f)
                for (float z = min.y + 4f; z < max.y - 2f; z += 8f)
                {
                    var p = new Vector2(x + (float)(_rnd.NextDouble() - 0.5) * 4f, z + (float)(_rnd.NextDouble() - 0.5) * 4f);
                    if (!Geometry.Inside(poly, p) || EdgeDist(poly, p) < 2.5f) continue;
                    if (Field(_lm.Path)[GridIndex(p)] < 2f) continue;
                    if (_rnd.NextDouble() < 0.25) Palms.Add(new Vector3(p.x, RoadSurface.KerbHeight, p.y));
                    else Tree(p, 1.1f + (float)_rnd.NextDouble() * 0.5f, _rnd.NextDouble() < 0.3);
                }
            // Hedge along the edge
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                var dir = (b - a).normalized; var inward = new Vector2(-dir.y, dir.x);
                var batch = new MeshBatch();
                var h = batch[_lm.Hedge];
                Vector2 a2 = a + inward * 0.8f, b2 = b + inward * 0.8f;
                float len = (b2 - a2).magnitude;
                if (len < 2f) continue;
                h.Transform = Matrix4x4.TRS(new Vector3((a2.x + b2.x) * 0.5f, RoadSurface.KerbHeight, (a2.y + b2.y) * 0.5f), Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y)), Vector3.one);
                h.Box(new Vector3(-0.45f, 0f, -len * 0.5f), new Vector3(0.45f, 0.9f, len * 0.5f), 1.5f, bottom: false);
                h.Transform = Matrix4x4.identity;
                Commit(batch, (a2 + b2) * 0.5f);
            }
        }

        void Playground(Vector2 c)
        {
            StampRect(_lm.Gravel, c, Vector2.right, 10f, 8f);
            var batch = new MeshBatch();
            var b = new Vector3(c.x, RoadSurface.KerbHeight, c.y);
            var r = batch[_lm.Play1]; var bl = batch[_lm.Play2]; var mt = batch[_lm.Fence];
            // Slide tower and slide
            bl.Box(b + new Vector3(-1.5f, 0f, -1f), b + new Vector3(0f, 1.6f, 0.5f));
            r.Box(b + new Vector3(-1.6f, 1.6f, -1.1f), b + new Vector3(0.1f, 2.6f, 0.6f));
            r.Transform = Matrix4x4.TRS(b + new Vector3(1.2f, 0.85f, -0.25f), Quaternion.Euler(0, 0, -35f), Vector3.one);
            r.Box(new Vector3(-1.5f, -0.05f, -0.35f), new Vector3(1.5f, 0.05f, 0.35f));
            r.Transform = Matrix4x4.identity;
            // Swing frame
            foreach (float x in new[] { -3.5f, -0.5f })
            {
                mt.Cylinder(b + new Vector3(x, 0, 2.5f), 0.06f, 0.06f, 2.4f, 8, false);
                mt.Cylinder(b + new Vector3(x, 0, 3.5f), 0.06f, 0.06f, 2.4f, 8, false);
            }
            mt.Box(b + new Vector3(-3.6f, 2.35f, 2.9f), b + new Vector3(-0.4f, 2.45f, 3.1f));
            Commit(batch, c);
        }

        void Pitch(List<Vector2> poly)
        {
            Pitches++;
            Geometry.MinAreaRect(poly, out var c, out var ax, out float w, out float d);
            if (w < d) { ax = new Vector2(-ax.y, ax.x); (w, d) = (d, w); }
            StampRect(_lm.Turf, c, ax, w, d);
            Vector2 ay = new Vector2(-ax.y, ax.x);
            float lw = w - 3f, ld = d - 3f;
            Vector2 C(float u, float v) => c + ax * u + ay * v;
            // Boundary, halfway line, centre circle, goal boxes
            _lines.Add((C(-lw / 2, -ld / 2), C(lw / 2, -ld / 2), 0.12f)); _lines.Add((C(-lw / 2, ld / 2), C(lw / 2, ld / 2), 0.12f));
            _lines.Add((C(-lw / 2, -ld / 2), C(-lw / 2, ld / 2), 0.12f)); _lines.Add((C(lw / 2, -ld / 2), C(lw / 2, ld / 2), 0.12f));
            _lines.Add((C(0, -ld / 2), C(0, ld / 2), 0.12f));
            float cr = Mathf.Min(ld * 0.15f, 9f);
            for (int i = 0; i < 24; i++)
            {
                float a0 = i * Mathf.PI / 12f, a1 = (i + 1) * Mathf.PI / 12f;
                _lines.Add((C(Mathf.Cos(a0) * cr, Mathf.Sin(a0) * cr), C(Mathf.Cos(a1) * cr, Mathf.Sin(a1) * cr), 0.12f));
            }
            foreach (int s in new[] { -1, 1 })
            {
                float gx = s * lw / 2, bx = gx - s * Mathf.Min(lw * 0.15f, 12f), bd = Mathf.Min(ld * 0.4f, 20f);
                _lines.Add((C(gx, -bd / 2), C(bx, -bd / 2), 0.12f)); _lines.Add((C(gx, bd / 2), C(bx, bd / 2), 0.12f)); _lines.Add((C(bx, -bd / 2), C(bx, bd / 2), 0.12f));
                // Goal frame
                var batch = new MeshBatch();
                var g = batch[_m.FrameWhite];
                var gp = C(gx + s * 0.5f, 0);
                g.Transform = Matrix4x4.TRS(new Vector3(gp.x, RoadSurface.KerbHeight, gp.y), Quaternion.LookRotation(new Vector3(ay.x, 0, ay.y)), Vector3.one);
                float gw = Mathf.Min(ld * 0.15f, 7.3f) * 0.5f;
                g.Box(new Vector3(-0.06f, 0, -gw), new Vector3(0.06f, 2.44f, -gw + 0.12f));
                g.Box(new Vector3(-0.06f, 0, gw - 0.12f), new Vector3(0.06f, 2.44f, gw));
                g.Box(new Vector3(-0.06f, 2.32f, -gw), new Vector3(0.06f, 2.44f, gw));
                g.Transform = Matrix4x4.identity;
                Commit(batch, gp);
            }
            // Perimeter fence: posts and rails
            {
                var batch = new MeshBatch();
                var f = batch[_lm.Fence];
                Vector2[] corners = { C(-w / 2, -d / 2), C(w / 2, -d / 2), C(w / 2, d / 2), C(-w / 2, d / 2) };
                for (int i = 0; i < 4; i++)
                {
                    Vector2 a = corners[i], b = corners[(i + 1) % 4];
                    float len = (b - a).magnitude;
                    for (float t = 0; t <= len; t += 3f)
                    {
                        var p = Vector2.Lerp(a, b, t / len);
                        f.Cylinder(new Vector3(p.x, RoadSurface.KerbHeight, p.y), 0.04f, 0.04f, 3f, 6, true);
                    }
                    foreach (float y in new[] { 1.2f, 2.95f })
                    {
                        f.Transform = Matrix4x4.TRS(new Vector3((a.x + b.x) / 2, RoadSurface.KerbHeight + y, (a.y + b.y) / 2), Quaternion.LookRotation(new Vector3(b.x - a.x, 0, b.y - a.y)), Vector3.one);
                        f.Box(new Vector3(-0.03f, -0.03f, -len / 2), new Vector3(0.03f, 0.03f, len / 2));
                        f.Transform = Matrix4x4.identity;
                    }
                }
                Commit(batch, c);
            }
        }

        void Parking(List<Vector2> poly, float fill)
        {
            ParkingLots++;
            StampPolygon(_m.Asphalt, poly, 0f);
            Geometry.MinAreaRect(poly, out var c, out var ax, out float w, out float d);
            if (w < d) { ax = new Vector2(-ax.y, ax.x); (w, d) = (d, w); }
            Vector2 ay = new Vector2(-ax.y, ax.x);
            const float bayW = 2.6f, bayD = 5.4f, aisle = 6.5f;
            // Double rows of bays with aisles between, along the long axis
            float rowPitch = bayD * 2f + aisle;
            for (float v = -d / 2 + 1f; v + bayD * 2f <= d / 2 - 0.5f; v += rowPitch)
                for (int row = 0; row < 2; row++)
                {
                    float v0 = v + row * bayD, vc = v0 + bayD * 0.5f;
                    for (float u = -w / 2 + 1.5f; u + bayW <= w / 2 - 1.5f; u += bayW)
                    {
                        var p0 = c + ax * u + ay * v0;
                        if (!Geometry.Inside(poly, c + ax * (u + bayW / 2) + ay * vc)) continue;
                        _lines.Add((p0, p0 + ay * bayD, 0.1f));
                        if (_rnd.NextDouble() < 0.55 * fill && ParkedCars.Count < 220)
                        {
                            var cp = c + ax * (u + bayW / 2) + ay * vc;
                            float yaw = Mathf.Atan2((row == 0 ? -ay : ay).x, (row == 0 ? -ay : ay).y) * Mathf.Rad2Deg;
                            ParkedCars.Add((new Vector3(cp.x, RoadSurface.KerbHeight + 0.01f, cp.y), yaw));
                        }
                    }
                }
            // Shade trees along the edges
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                float len = (b - a).magnitude;
                var inward = new Vector2(-(b - a).y, (b - a).x).normalized;
                for (float t = 6f; t < len - 4f; t += 14f) Tree(Vector2.Lerp(a, b, t / len) + inward * 1.2f, 1.0f);
            }
        }

        void Site(List<Vector2> poly)
        {
            Sites++;
            StampPolygon(_lm.Gravel, poly, 0f);
            var batch = new MeshBatch();
            var hoard = batch[_lm.Hoarding];
            var accent = batch[_m.Signs[0]];
            for (int i = 0; i < poly.Count; i++)
            {
                // Hoarding along the site edge, with gaps where a road crosses it
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                float edge = (b - a).magnitude;
                int k = Mathf.Max(1, Mathf.CeilToInt(edge));
                int run = -1;
                for (int j = 0; j <= k; j++)
                {
                    bool clear = j < k && Street(Vector2.Lerp(a, b, (j + 0.5f) / k)) >= StreetClear;
                    if (clear && run < 0) run = j;
                    if (clear || run < 0) continue;
                    Vector2 p0 = Vector2.Lerp(a, b, run / (float)k), p1 = Vector2.Lerp(a, b, j / (float)k);
                    run = -1;
                    float len = (p1 - p0).magnitude;
                    if (len < 1f) continue;
                    hoard.Transform = Matrix4x4.TRS(new Vector3((p0.x + p1.x) / 2, RoadSurface.KerbHeight, (p0.y + p1.y) / 2), Quaternion.LookRotation(new Vector3(p1.x - p0.x, 0, p1.y - p0.y)), Vector3.one);
                    hoard.Box(new Vector3(-0.05f, 0, -len / 2), new Vector3(0.05f, 2.4f, len / 2), 2f);
                    accent.Transform = hoard.Transform;
                    accent.Box(new Vector3(-0.07f, 2.0f, -len / 2), new Vector3(0.07f, 2.4f, len / 2), 2f);
                    hoard.Transform = accent.Transform = Matrix4x4.identity;
                }
            }
            // A concrete frame under construction (fw along ax, fd across) and a tower crane, clear of site roads
            Geometry.MinAreaRect(poly, out var c, out var ax, out float w, out float d);
            float fw = Mathf.Min(w * 0.6f, 40f), fd = Mathf.Min(d * 0.6f, 30f);
            if (!PullOffRoad(ref c, ax, ref fw, ref fd, 8f)) { Commit(batch, c); return; }
            var xf = Matrix4x4.TRS(new Vector3(c.x, RoadSurface.KerbHeight, c.y), Quaternion.LookRotation(new Vector3(ax.x, 0, ax.y)), Vector3.one);
            var con = batch[_m.Concrete]; con.Transform = xf;
            int floors = 3 + _rnd.Next(4);
            for (int f = 0; f <= floors; f++) con.Box(new Vector3(-fd / 2, f * 3.4f, -fw / 2), new Vector3(fd / 2, f * 3.4f + 0.3f, fw / 2), 3f);
            for (float x = -fd / 2; x <= fd / 2 + 0.1f; x += 6f)
                for (float z = -fw / 2; z <= fw / 2 + 0.1f; z += 6f)
                    con.Box(new Vector3(x - 0.25f, 0, z - 0.25f), new Vector3(x + 0.25f, floors * 3.4f, z + 0.25f), 1f);
            con.Transform = Matrix4x4.identity;
            float yaw = Mathf.Atan2(ax.x, ax.y) * Mathf.Rad2Deg + 90f;   // collider local X runs along ax
            // The crane mast stands beside the frame (local +X, or -X when a road is there)
            var across = new Vector2(ax.y, -ax.x);
            float mastH = floors * 3.4f + 14f, mx = fd / 2 + 4f;
            if (!RectClear(c + across * mx, ax, 2f, 2f)) mx = -mx;
            if (RectClear(c + across * mx, ax, 2f, 2f))
            {
                var cr = batch[_lm.Crane]; cr.Transform = xf;
                cr.Box(new Vector3(mx - 0.8f, 0, -0.8f), new Vector3(mx + 0.8f, mastH, 0.8f), 2f);
                cr.Box(new Vector3(mx - 0.6f, mastH, -32f), new Vector3(mx + 0.6f, mastH + 1.4f, 12f), 2f);
                cr.Box(new Vector3(mx - 1.5f, mastH - 2f, 10f), new Vector3(mx + 1.5f, mastH, 13f), 2f);   // counterweight
                cr.Transform = Matrix4x4.identity;
                var mp = c + across * mx;
                Colliders.Add((new Vector3(mp.x, RoadSurface.KerbHeight + mastH * 0.5f, mp.y), new Vector3(1.6f, mastH, 1.6f), yaw));
            }
            Commit(batch, c);
            // Frame: fw along ax, fd across (the collider used to have them swapped, a strip of invisible wall)
            Colliders.Add((new Vector3(c.x, RoadSurface.KerbHeight + floors * 1.7f, c.y), new Vector3(fw, floors * 3.4f, fd), yaw));
        }

        void Campus(List<Vector2> poly, float area, string kind)
        {
            // Grounds: lawn with paths, a main building if OSM maps none inside, trees, and parking
            StampPolygon(_lm.Grass, poly, 2f);
            Geometry.MinAreaRect(poly, out var c, out var ax, out float w, out float d);
            bool hasBuilding = false;
            foreach (var b in _map.Ways)
            {
                if (b.Tag("building") == null) continue;
                var bp = _map.Points(b);
                if (bp.Count > 2 && Geometry.Inside(poly, Geometry.Centroid(bp))) { hasBuilding = true; break; }
            }
            float bw = Mathf.Min(w * 0.55f, 70f), bd = Mathf.Min(d * 0.45f, 30f);
            // Campus roads run through the grounds: the main building steps off them, or is left out
            if (!hasBuilding && area > 1500f && PullOffRoad(ref c, ax, ref bw, ref bd, 12f))
            {
                var batch = new MeshBatch();
                float yaw = Mathf.Atan2(ax.x, ax.y) * Mathf.Rad2Deg + 90f;
                int fl = kind == "hospital" ? 5 + _rnd.Next(4) : 2 + _rnd.Next(2);
                float h = ModernKit.Apartment(batch, new Vector3(c.x, RoadSurface.KerbHeight, c.y), yaw, bw, bd, fl, false, _seed++);
                Commit(batch, c);
                Colliders.Add((new Vector3(c.x, RoadSurface.KerbHeight + h / 2, c.y), new Vector3(bw, h, bd), yaw));
                Boxes.Add((new Vector3(c.x, RoadSurface.KerbHeight + h / 2, c.y), new Vector3(bw, h, bd), yaw, ModernKit.Materials().Cladding[0]));
                Modern++;
            }
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                float len = (b - a).magnitude;
                var inward = new Vector2(-(b - a).y, (b - a).x).normalized;
                for (float t = 5f; t < len - 3f; t += 10f) Tree(Vector2.Lerp(a, b, t / len) + inward * 3f, 1.2f);
            }
        }

        // ------------------------------------------------------------------ leftovers

        int GridIndex(Vector2 p)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt((p.x - _surf.Origin.x) / RoadSurface.Cell), 0, _surf.Nx - 1);
            int j = Mathf.Clamp(Mathf.RoundToInt((p.y - _surf.Origin.y) / RoadSurface.Cell), 0, _surf.Nz - 1);
            return j * _surf.Nx + i;
        }

        static float EdgeDist(List<Vector2> poly, Vector2 p)
        {
            float d = float.MaxValue;
            for (int i = 0; i < poly.Count; i++) d = Mathf.Min(d, RoadNetwork.SegDist(p, poly[i], poly[(i + 1) % poly.Count], out _));
            return d;
        }

        /// <summary>
        /// Connected empty areas left after the buildings (flood fill on the occupancy grid). Large ones
        /// become parks covering the whole lot (lawn, crossing paths, trees, palms, benches, lamps,
        /// fountain, playground), medium ones gardens or car parks.
        /// </summary>
        void FillLots()
        {
            int nx = _occ.Nx, nz = _occ.Nz;
            float cs = _occ.CellSize;
            var seen = new bool[nx * nz];
            bool Free(int i, int j) => !_occ.GetCell(i, j) && _surf.Sample(_surf.S, _occ.CellCenter(i, j)) > 1.2f;
            var queue = new Queue<Vector2Int>();
            var comp = new List<Vector2Int>();
            for (int j0 = 0; j0 < nz; j0++)
                for (int i0 = 0; i0 < nx; i0++)
                {
                    if (seen[j0 * nx + i0] || !Free(i0, j0)) continue;
                    comp.Clear(); queue.Enqueue(new Vector2Int(i0, j0)); seen[j0 * nx + i0] = true;
                    while (queue.Count > 0)
                    {
                        var c = queue.Dequeue(); comp.Add(c);
                        foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                        {
                            var n = c + d;
                            if (n.x < 0 || n.y < 0 || n.x >= nx || n.y >= nz || seen[n.y * nx + n.x] || !Free(n.x, n.y)) continue;
                            seen[n.y * nx + n.x] = true; queue.Enqueue(n);
                        }
                    }
                    float area = comp.Count * cs * cs;
                    if (area < 250f) continue;
                    LotFill(comp, area);
                }
        }

        void LotFill(List<Vector2Int> cells, float area)
        {
            float cs = _occ.CellSize;
            var set = new HashSet<Vector2Int>(cells);
            bool In(Vector2 p) => set.Contains(_occ.CellOf(p));
            Vector2 centroid = Vector2.zero; var min = new Vector2(float.MaxValue, float.MaxValue); var max = -min;
            foreach (var c in cells) { var p = _occ.CellCenter(c.x, c.y); centroid += p; min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            centroid /= cells.Count;
            if (!In(centroid)) centroid = _occ.CellCenter(cells[cells.Count / 2].x, cells[cells.Count / 2].y);
            bool park = area >= 1100f || _rnd.NextDouble() < 0.6;
            Material ground = park ? _lm.Grass : _m.Asphalt;
            // Ground: one stamp per occupancy cell (2 m squares; marching squares smooths the outline)
            var field = Field(ground);
            int gi0 = Mathf.Max(0, Mathf.FloorToInt((min.x - cs - _surf.Origin.x) / RoadSurface.Cell)), gi1 = Mathf.Min(_surf.Nx - 1, Mathf.CeilToInt((max.x + cs - _surf.Origin.x) / RoadSurface.Cell));
            int gj0 = Mathf.Max(0, Mathf.FloorToInt((min.y - cs - _surf.Origin.y) / RoadSurface.Cell)), gj1 = Mathf.Min(_surf.Nz - 1, Mathf.CeilToInt((max.y + cs - _surf.Origin.y) / RoadSurface.Cell));
            for (int j = gj0; j <= gj1; j++)
                for (int i = gi0; i <= gi1; i++)
                {
                    var p = _surf.GridPos(i, j);
                    if (In(p)) { int k = j * _surf.Nx + i; field[k] = Mathf.Min(field[k], -0.6f); }
                }
            foreach (var c in cells) _occ.SetCell(c.x, c.y);

            if (!park)
            {
                // Car park: bays along the lot's long axis, cars in about half of them, trees at the edges
                ParkingLots++;
                Vector2 axis = (max.x - min.x) >= (max.y - min.y) ? Vector2.right : Vector2.up, side = new Vector2(-axis.y, axis.x);
                for (float v = Vector2.Dot(min, side) + 1f; v < Vector2.Dot(max, side) - 5f; v += 17.3f)
                    for (int row = 0; row < 2; row++)
                        for (float u = Vector2.Dot(min, axis) + 1f; u < Vector2.Dot(max, axis) - 2f; u += 2.6f)
                        {
                            Vector2 p0 = axis * u + side * (v + row * 5.4f), pc = p0 + axis * 1.3f + side * 2.7f;
                            if (!In(pc) || !In(p0) || !In(p0 + side * 5.4f + axis * 2.6f)) continue;
                            _lines.Add((p0, p0 + side * 5.4f, 0.1f));
                            if (_rnd.NextDouble() < 0.5 && ParkedCars.Count < 320)
                            {
                                var f = row == 0 ? -side : side;
                                ParkedCars.Add((new Vector3(pc.x, RoadSurface.KerbHeight + 0.01f, pc.y), Mathf.Atan2(f.x, f.y) * Mathf.Rad2Deg));
                            }
                        }
                return;
            }

            // Park
            if (area >= 1100f) Parks++; else PocketParks++;
            float rx = (max.x - min.x) * 0.5f, rz = (max.y - min.y) * 0.5f;
            // Crossing paths through the centre, clipped to the lot
            foreach (var dir in new[] { Vector2.right, Vector2.up })
            {
                float reach = dir.x != 0 ? rx : rz;
                if (reach < 8f) continue;
                Vector2 last = centroid; bool open = false; Vector2 start = centroid;
                for (float t = -reach; t <= reach; t += 1f)
                {
                    var p = centroid + dir * t;
                    bool inside = In(p);
                    if (inside && !open) { start = p; open = true; }
                    if ((!inside || t + 1f > reach) && open) { if ((last - start).magnitude > 4f) StampStrip(_lm.Path, start, last, 2.4f); open = false; }
                    if (inside) last = p;
                }
            }
            if (area > 2500f)
            {
                StampRect(_lm.Path, centroid, Vector2.right, 10f, 10f);
                var batch = new MeshBatch();
                var b = new Vector3(centroid.x, RoadSurface.KerbHeight, centroid.y);
                batch[_m.Marble].Cylinder(b, 2.8f, 2.8f, 0.55f, 24, true);
                batch[_lm.Water].Cylinder(b + Vector3.up * 0.4f, 2.5f, 2.5f, 0.18f, 24, true);
                batch[_m.Marble].Cylinder(b, 0.45f, 0.3f, 1.6f, 12, true);
                Commit(batch, centroid);
            }
            if (area > 2000f)
            {
                var pg = centroid + new Vector2(rx * 0.45f, rz * 0.4f);
                if (In(pg) && In(pg + new Vector2(5f, 4f)) && In(pg - new Vector2(5f, 4f))) Playground(pg);
            }
            // Trees and palms on the lawn, clear of the paths; benches and lamps along the paths
            var pathField = Field(_lm.Path);
            for (float x = min.x + 3f; x <= max.x - 2f; x += 7f)
                for (float z = min.y + 3f; z <= max.y - 2f; z += 7f)
                {
                    var p = new Vector2(x + (float)(_rnd.NextDouble() - 0.5) * 3.5f, z + (float)(_rnd.NextDouble() - 0.5) * 3.5f);
                    if (!In(p) || !In(p + Vector2.right * 2f) || !In(p - Vector2.right * 2f) || !In(p + Vector2.up * 2f) || !In(p - Vector2.up * 2f)) continue;
                    float dp = pathField[GridIndex(p)];
                    if (dp < 1.8f)
                    {
                        if (dp > 0.6f && _rnd.NextDouble() < 0.25) { if (_rnd.NextDouble() < 0.5) Bench(p, (centroid - p).normalized); else Lamp(p); }
                        continue;
                    }
                    double r = _rnd.NextDouble();
                    if (r < 0.2) Palms.Add(new Vector3(p.x, RoadSurface.KerbHeight, p.y));
                    else if (r < 0.85) Tree(p, 1.0f + (float)_rnd.NextDouble() * 0.6f, _rnd.NextDouble() < 0.35);
                    else
                    {
                        var batch = new MeshBatch();
                        Blob(batch[_lm.Hedge], new Vector3(p.x, RoadSurface.KerbHeight + 0.5f, p.y), 0.9f, _rnd);
                        Commit(batch, p);
                    }
                }
        }

        /// <summary>Plots still empty after all buildings: pocket gardens, small car parks or tree groves.</summary>
        void EmptyPlots()
        {
            const float step = 22f;
            for (float x = _area.xMin + 12f; x < _area.xMax - 12f; x += step)
                for (float z = _area.yMin + 12f; z < _area.yMax - 12f; z += step)
                {
                    var c = new Vector2(x, z);
                    if (_surf.Sample(_surf.S, c) < 3f || _occ.Get(c)) continue;
                    float gx = _surf.Sample(_surf.R, c + Vector2.right) - _surf.Sample(_surf.R, c - Vector2.right);
                    float gz = _surf.Sample(_surf.R, c + Vector2.up) - _surf.Sample(_surf.R, c - Vector2.up);
                    var toRoad = -new Vector2(gx, gz).normalized;
                    if (toRoad.sqrMagnitude < 0.5f) toRoad = Vector2.up;
                    var ax = new Vector2(toRoad.y, -toRoad.x);
                    float w = 18f, d = 18f;
                    if (!Fits(c, ax, w, d, 0.3f)) { w = d = 12f; if (!Fits(c, ax, w, d, 0.3f)) continue; }
                    MarkRect(c, ax, w, d);
                    double r = _rnd.NextDouble();
                    var rect = new List<Vector2>();
                    Vector2 ay = new Vector2(-ax.y, ax.x);
                    rect.Add(c - ax * w / 2 - ay * d / 2); rect.Add(c + ax * w / 2 - ay * d / 2); rect.Add(c + ax * w / 2 + ay * d / 2); rect.Add(c - ax * w / 2 + ay * d / 2);
                    if (Geometry.SignedArea(rect) < 0) rect.Reverse();
                    if (r < 0.55)
                    {
                        // Pocket garden: lawn, a path across, trees, benches
                        PocketParks++;
                        StampRect(_lm.Grass, c, ax, w, d);
                        StampStrip(_lm.Path, c - ay * d / 2, c + ay * d / 2, 2f);
                        Tree(c + ax * (w * 0.28f) + ay * (d * 0.25f), 1.2f);
                        Tree(c - ax * (w * 0.28f) - ay * (d * 0.22f), 1.1f, true);
                        if (w > 14f) { Tree(c + ax * (w * 0.3f) - ay * (d * 0.28f), 1.0f); Palms.Add(new Vector3(c.x - ax.x * w * 0.3f + ay.x * d * 0.3f, RoadSurface.KerbHeight, c.y - ax.y * w * 0.3f + ay.y * d * 0.3f)); }
                        Bench(c + ax * 1.8f, -ax);
                        Bench(c - ax * 1.8f + ay * 3f, ax);
                    }
                    else if (r < 0.85) Parking(rect, 0.8f);
                    else
                    {
                        StampRect(_lm.Grass, c, ax, w, d);   // tree grove on grass (gravel read as more sand)
                        for (int k = 0; k < 4; k++) Tree(c + new Vector2((float)(_rnd.NextDouble() - 0.5) * w * 0.7f, (float)(_rnd.NextDouble() - 0.5) * d * 0.7f), 1.0f + (float)_rnd.NextDouble() * 0.4f);
                    }
                }
        }

        /// <summary>Trees on wide sidewalks of tertiary and bigger roads, alternating with palms.</summary>
        void StreetTrees()
        {
            foreach (var road in _net.Roads)
            {
                if (road.Class < RoadClass.Tertiary || road.Sidewalk < 2.9f) continue;
                foreach (int side in road.Oneway ? new[] { 1 } : new[] { 1, -1 })
                {
                    var line = RoadNetwork.Offset(road.P, side * (road.HalfWidth + road.Sidewalk * 0.7f));
                    float len = RoadNetwork.Length(line);
                    int k = 0;
                    for (float s = 9f; s < len; s += 17f, k++)
                    {
                        Sample(line, s, out var p, out _);
                        if (!_area.Contains(p)) continue;
                        float r = _surf.Sample(_surf.R, p), sw = _surf.Sample(_surf.S, p);
                        if (r < 1.2f || sw > -0.4f) continue;   // in a junction, or off the sidewalk
                        if (_surf.Sample(_surf.E, p) < 4f) continue;   // under a flyover deck or at a trench edge
                        if (k % 3 == 2) Palms.Add(new Vector3(p.x, RoadSurface.KerbHeight, p.y));
                        else Tree(p, 0.9f);
                    }
                }
            }
        }

        /// <summary>Remaining sand gaps between buildings: scattered trees and shrubs.</summary>
        void GapTrees()
        {
            const float step = 7f;
            int placed = 0;
            for (float x = _area.xMin + 4f; x < _area.xMax - 4f; x += step)
                for (float z = _area.yMin + 4f; z < _area.yMax - 4f; z += step)
                {
                    var p = new Vector2(x + (float)(_rnd.NextDouble() - 0.5) * 3f, z + (float)(_rnd.NextDouble() - 0.5) * 3f);
                    if (_occ.Get(p) || _surf.Sample(_surf.S, p) < 1.5f || _surf.Sample(_surf.E, p) < 4f) continue;
                    if (_rnd.NextDouble() > 0.4) continue;
                    if (_rnd.NextDouble() < 0.6) Tree(p, 0.8f + (float)_rnd.NextDouble() * 0.5f, _rnd.NextDouble() < 0.4);
                    else
                    {
                        var batch = new MeshBatch();
                        Blob(batch[_lm.Hedge], new Vector3(p.x, RoadSurface.KerbHeight + 0.5f, p.y), 0.8f, _rnd);
                        Commit(batch, p);
                    }
                    _occ.Set(p);
                    placed++;
                }
        }
    }
}
