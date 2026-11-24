using System.Collections.Generic;
using Masar.World;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// Buildings and street furniture for an OSM map.
    /// - Surveyed buildings: every OSM building footprint. Near-rectangular ones use the detailed
    ///   BuildingKit apartment / villa / mosque, the rest are extruded footprints with windows.
    /// - Generated buildings: OSM maps only a fraction of Jeddah's buildings, so empty plots are filled
    ///   with buildings matching the street: shops + apartments on main roads, villas and walk-ups on
    ///   residential streets, villas inside blocks. These are not surveyed (documented in KNOWN_ISSUES).
    /// - Street lights along lit roads, date palms on medians and main-road sidewalks.
    /// Geometry is merged per 64 m cell into a near mesh (full detail) and a far mesh (walls, glass and
    /// roofs only), switched by distance at runtime (<see cref="CellLod"/>).
    /// </summary>
    public sealed partial class CityDressing
    {
        public const float CellSize = 128f;

        public sealed class Cell
        {
            public MeshBatch Near = new MeshBatch(), Far = new MeshBatch();
            public Vector2Int Key;
        }

        public readonly Dictionary<Vector2Int, Cell> Cells = new Dictionary<Vector2Int, Cell>();
        public readonly List<(Vector3 center, Vector3 size, float yaw)> Colliders = new List<(Vector3, Vector3, float)>();
        /// <summary>Buildings drawn from their OSM outline (not a rectangle): collision walls follow the outline, height h above the kerb.</summary>
        public readonly List<(List<Vector2> poly, float h)> Walls = new List<(List<Vector2>, float)>();
        public readonly List<Vector3> Palms = new List<Vector3>();
        /// <summary>Simplified building volumes for the far blocks: center, size, yaw, plaster index.</summary>
        public readonly List<(Vector3 center, Vector3 size, float yaw, Material mat)> Boxes = new List<(Vector3, Vector3, float, Material)>();
        public int Modern;
        public int Surveyed, SurveyedKit, SurveyedTrimmed, SurveyedDropped, Generated, GeneratedVillas, Lights;

        readonly OsmMap _map;
        readonly RoadNetwork _net;
        readonly RoadSurface _surf;
        readonly CityMaterials _m;
        readonly Rect _area;
        readonly Occupancy _occ;
        readonly System.Random _rnd = new System.Random(2026);
        int _seed = 5000;

        public CityDressing(OsmMap map, RoadNetwork net, RoadSurface surf, CityMaterials m, Rect area)
        {
            _map = map; _net = net; _surf = surf; _m = m; _area = area;
            _occ = new Occupancy(area, 2f);
        }

        public Cell CellAt(Vector2 p)
        {
            var key = new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize));
            if (!Cells.TryGetValue(key, out var c)) { c = new Cell { Key = key }; Cells[key] = c; }
            return c;
        }

        public void Build()
        {
            ReserveLanduse();
            SurveyedBuildings();
            FrontageFill();
            InteriorFill();
            StreetLightsAndPalms();
            Landscape();   // parks, pitches, parking, sites, empty plots, street trees (CityDressing.Landscape.cs)
        }

        // ------------------------------------------------------------------ helpers

        static bool IsDetail(Material mat)
        {
            if (mat == null) return false;
            string n = mat.name;
            return n.StartsWith("City_Frame") || n.StartsWith("City_RailingMetal") || n.StartsWith("City_AcUnit") ||
                   n.StartsWith("City_WaterTank") || n.StartsWith("City_GateMetal") || n.StartsWith("City_PoleMetal") ||
                   n.StartsWith("City_SatelliteDish") || n.StartsWith("City_LampHead") || n.StartsWith("City_FacadeGrime") ||
                   n.StartsWith("City_Sign");
        }

        void Commit(MeshBatch building, Vector2 center)
        {
            var cell = CellAt(center);
            foreach (var kv in building.Entries)
            {
                if (kv.Value.Count == 0) continue;
                cell.Near[kv.Key].Append(kv.Value);
                if (!IsDetail(kv.Key)) cell.Far[kv.Key].Append(kv.Value);
            }
        }

        /// <summary>Clearance of a footprint: every sample must be off the sidewalk, inside the map and free.</summary>
        bool Fits(Vector2 c, Vector2 ax, float w, float d, float minS)
        {
            Vector2 ay = new Vector2(-ax.y, ax.x);
            for (float u = -w * 0.5f; u <= w * 0.5f + 0.01f; u += Mathf.Min(2f, w * 0.5f))
                for (float v = -d * 0.5f; v <= d * 0.5f + 0.01f; v += Mathf.Min(2f, d * 0.5f))
                {
                    var p = c + ax * u + ay * v;
                    if (!_area.Contains(p)) return false;
                    if (_surf.Sample(_surf.S, p) < minS) return false;
                    if (_surf.Sample(_surf.E, p) < 3f) return false;   // clear of flyovers and trenches
                    if (_occ.Get(p)) return false;
                }
            return true;
        }

        void MarkRect(Vector2 c, Vector2 ax, float w, float d) => _occ.FillRect(c, ax, w + 1f, d + 1f);

        // ------------------------------------------------------------------ keeping buildings off the carriageway
        // OSM outlines and lots often reach a few metres into the modelled road width; a building there stood
        // on a lane and stopped the car (58 boxes on lanes, 2026-11-24). R < 0 is the carriageway.

        const float RoadClear = 0.5f;

        bool RectClear(Vector2 c, Vector2 ax, float w, float d, float clear = RoadClear)
        {
            Vector2 ay = new Vector2(-ax.y, ax.x);
            int nu = Mathf.Max(1, Mathf.CeilToInt(w / 1.5f)), nv = Mathf.Max(1, Mathf.CeilToInt(d / 1.5f));
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                    if (_surf.Sample(_surf.R, c + ax * (w * (i / (float)nu - 0.5f)) + ay * (d * (j / (float)nv - 0.5f))) < clear) return false;
            return true;
        }

        /// <summary>
        /// Pulls each side of a rectangle (w along ax, d across) back off the carriageway. False when a road
        /// runs through it or a side would drop below minSize.
        /// </summary>
        bool PullOffRoad(ref Vector2 c, Vector2 ax, ref float w, ref float d, float minSize)
        {
            Vector2 ay = new Vector2(-ax.y, ax.x);
            for (int iter = 0; iter < 4; iter++)
            {
                bool moved = false;
                for (int s = 0; s < 4; s++)
                {
                    Vector2 n = s == 0 ? ax : s == 1 ? -ax : s == 2 ? ay : -ay, t = s < 2 ? ay : ax;
                    float half = s < 2 ? w * 0.5f : d * 0.5f, span = s < 2 ? d : w;
                    int k = Mathf.Max(1, Mathf.CeilToInt(span));
                    float worst = float.MaxValue;
                    for (int i = 0; i <= k; i++) worst = Mathf.Min(worst, _surf.Sample(_surf.R, c + n * half + t * (span * (i / (float)k - 0.5f))));
                    if (worst >= RoadClear) continue;
                    float cut = RoadClear - worst + 0.05f;
                    if (s < 2) w -= cut; else d -= cut;
                    c -= n * (cut * 0.5f);
                    moved = true;
                    if (w < minSize || d < minSize) return false;
                }
                if (!moved) return RectClear(c, ax, w, d, RoadClear - 0.1f);
            }
            return false;
        }

        /// <summary>
        /// An outline with points on the carriageway: edges resampled every 2 m and pushed off along the
        /// road distance gradient. Null when the result is not a simple polygon (a road through it).
        /// </summary>
        List<Vector2> PushOffRoad(List<Vector2> poly)
        {
            bool any = false;
            for (int i = 0; i < poly.Count && !any; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                int k = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude));
                for (int j = 0; j <= k && !any; j++) any = _surf.Sample(_surf.R, Vector2.Lerp(a, b, j / (float)k)) < RoadClear;
            }
            if (!any) return poly;
            var dense = new List<Vector2>();
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                int k = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / 2f));
                for (int j = 0; j < k; j++) dense.Add(Vector2.Lerp(a, b, j / (float)k));
            }
            for (int i = 0; i < dense.Count; i++)
                for (int it = 0; it < 3; it++)
                {
                    var p = dense[i];
                    float r = _surf.Sample(_surf.R, p);
                    if (r >= RoadClear) break;
                    var g = new Vector2(_surf.Sample(_surf.R, p + Vector2.right * 0.5f) - _surf.Sample(_surf.R, p - Vector2.right * 0.5f),
                                        _surf.Sample(_surf.R, p + Vector2.up * 0.5f) - _surf.Sample(_surf.R, p - Vector2.up * 0.5f));
                    if (g.sqrMagnitude < 1e-6f) break;
                    dense[i] = p + g.normalized * (RoadClear - r + 0.05f);
                }
            // Drop near-collinear and near-duplicate points so the roof triangulates cleanly
            var outp = new List<Vector2>();
            for (int i = 0; i < dense.Count; i++)
            {
                Vector2 prev = outp.Count > 0 ? outp[outp.Count - 1] : dense[dense.Count - 1], p = dense[i], next = dense[(i + 1) % dense.Count];
                if ((p - prev).sqrMagnitude < 0.25f) continue;
                Vector2 e0 = (p - prev).normalized, e1 = next - p;
                if (e1.sqrMagnitude > 1e-4f && Mathf.Abs(e0.x * e1.normalized.y - e0.y * e1.normalized.x) < 0.02f && Vector2.Dot(e0, e1) > 0f) continue;
                outp.Add(p);
            }
            if (outp.Count < 3 || Geometry.SignedArea(outp) < 0.5f * Geometry.SignedArea(poly) || SelfIntersects(outp)) return null;
            return outp;
        }

        static bool SelfIntersects(List<Vector2> p)
        {
            int n = p.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    Vector2 a = p[i], b = p[(i + 1) % n], c = p[j], d = p[(j + 1) % n];
                    float d1 = Cross(d - c, a - c), d2 = Cross(d - c, b - c), d3 = Cross(b - a, c - a), d4 = Cross(b - a, d - a);
                    if (((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0))) return true;
                }
            return false;
        }

        static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        // ------------------------------------------------------------------ land use (kept free of fill)

        void ReserveLanduse()
        {
            foreach (var w in _map.Ways)
            {
                string lu = w.Tag("landuse"), le = w.Tag("leisure"), am = w.Tag("amenity");
                bool reserve = am == "parking" || am == "school" || am == "hospital" || le == "pitch" || le == "park" || le == "garden" ||
                               le == "swimming_pool" || lu == "construction" || lu == "brownfield" || lu == "grass";
                if (!reserve || w.Tag("building") != null) continue;
                var poly = _map.Points(w);
                if (poly.Count >= 4) _occ.FillPolygon(poly);
            }
        }

        // ------------------------------------------------------------------ surveyed buildings

        void SurveyedBuildings()
        {
            foreach (var w in _map.Ways)
            {
                string b = w.Tag("building");
                if (b == null || b == "no" || b == "roof" || b == "construction") continue;
                var poly = _map.Points(w);
                if (poly.Count < 4) continue;
                if ((poly[0] - poly[poly.Count - 1]).sqrMagnitude < 0.01f) poly.RemoveAt(poly.Count - 1);
                if (Geometry.SignedArea(poly) < 0) poly.Reverse();
                float area = Geometry.SignedArea(poly);
                if (area < 12f) continue;
                var cen = Geometry.Centroid(poly);
                if (!_area.Contains(cen)) continue;
                int levels = ParseLevels(w);
                var nearCls = _surf.NearestClass(Geometry.Centroid(poly));
                if (levels <= 0) levels = DefaultLevels(b, nearCls);
                bool worship = w.Tag("amenity") == "place_of_worship" || b == "mosque";
                Geometry.MinAreaRect(poly, out var rc, out var rax, out float rw, out float rd);
                float fill = area / Mathf.Max(1f, rw * rd);
                // Rectangles step back off the carriageway; a road through one leaves only the outline path
                bool rect = fill > 0.8f;
                if (rect)
                {
                    float w0 = rw, d0 = rd;
                    rect = PullOffRoad(ref rc, rax, ref rw, ref rd, 6f);
                    if (rect && (rw < w0 - 0.01f || rd < d0 - 0.01f)) SurveyedTrimmed++;
                    if (!rect) Geometry.MinAreaRect(poly, out rc, out rax, out rw, out rd);
                }
                var batch = new MeshBatch();
                Material surveyedFar = null;
                bool outline = false, mosque = false;
                // Front of the building: the rectangle side nearest a road
                FrontFacing(rc, rax, rw, rd, out var fax, out float fw, out float fd, out float yaw);
                var near = _surf.NearestClass(rc);
                bool major = near >= RoadClass.Tertiary && _surf.Sample(_surf.R, rc) < fd * 0.5f + 10f;
                // The mosque kit's footprint: a 22 x 24 m hall, its minaret off the +X side (BuildingKit.Mosque)
                if (worship && rw > 14f && rd > 14f && rw < 45f && rd < 45f && RectClear(rc + fax * 2.5f, fax, 27f, 24f))
                {
                    BuildingKit.Mosque(batch, _m, new Vector3(rc.x, RoadSurface.KerbHeight, rc.y), yaw);
                    SurveyedKit++;
                    mosque = true;
                }
                else if (rect && fw >= 6f && fd >= 6f && fw <= 90f && fd <= 90f)
                {
                    if ((b == "house" || b == "villa" || b == "detached") && levels <= 2 && fw < 40f)
                        BuildingKit.Villa(batch, _m, new Vector3(rc.x, RoadSurface.KerbHeight, rc.y), yaw, fw, fd, _seed++);
                    else if (levels >= 7 && fw >= 16f && fd >= 14f && _rnd.NextDouble() < 0.6)
                    {
                        // Tall surveyed buildings: mostly modern glass towers (Tahlia's skyline)
                        ModernKit.GlassTower(batch, new Vector3(rc.x, RoadSurface.KerbHeight, rc.y), yaw, fw, fd, Mathf.Max(1, levels - 2), _seed++);
                        surveyedFar = ModernKit.Materials().Glass[_rnd.Next(4)];
                        Modern++;
                    }
                    else if (b != "house" && _rnd.NextDouble() < (major ? 0.5 : 0.3))
                    {
                        ModernKit.Apartment(batch, new Vector3(rc.x, RoadSurface.KerbHeight, rc.y), yaw, fw, fd, levels, major, _seed++);
                        surveyedFar = ModernKit.Materials().Cladding[_rnd.Next(3)];
                        Modern++;
                    }
                    else
                    {
                        var spec = ApartmentSpec(fw, fd, levels, major && b != "house");
                        BuildingKit.Apartment(batch, _m, new Vector3(rc.x, RoadSurface.KerbHeight, rc.y), yaw, spec);
                    }
                    SurveyedKit++;
                }
                else
                {
                    var off = PushOffRoad(poly);
                    if (off == null)
                    {
                        // A modelled road runs through the outline (OSM has the road and the building overlapping)
                        _occ.FillPolygon(poly);
                        SurveyedDropped++;
                        continue;
                    }
                    if (off != poly) SurveyedTrimmed++;
                    poly = off;
                    Extrude(batch, poly, levels, _seed++);
                    outline = true;
                }
                Commit(batch, rc);
                float h = levels * 3.3f + 4.5f;
                // An outline building's bounding rectangle can reach across the street (a 211 x 52 m box stood on
                // Tahlia Street at the spawn, 2026-11-24): its collision follows the outline instead.
                if (outline) Walls.Add((poly, h));
                else if (mosque)
                {
                    var n = new Vector2(-fax.y, fax.x);
                    Colliders.Add((new Vector3(rc.x, RoadSurface.KerbHeight + 5f, rc.y), new Vector3(22f, 10f, 24f), yaw));
                    var m = rc + fax * 14.2f + n * 9f;
                    Colliders.Add((new Vector3(m.x, RoadSurface.KerbHeight + 18f, m.y), new Vector3(3.6f, 36f, 3.6f), yaw));
                }
                else Colliders.Add((new Vector3(rc.x, RoadSurface.KerbHeight + h * 0.5f, rc.y), new Vector3(fw, h, fd), yaw));
                Boxes.Add((new Vector3(rc.x, RoadSurface.KerbHeight + h * 0.5f, rc.y), new Vector3(fw, h, fd), yaw, surveyedFar ?? _m.Plaster[(int)(w.Id % _m.Plaster.Length)]));
                _occ.FillPolygon(poly);
                MarkRect(rc, rax, rw, rd);
                Surveyed++;
            }
        }

        void FrontFacing(Vector2 c, Vector2 ax, float w, float d, out Vector2 frontAxis, out float width, out float depth, out float yaw)
        {
            Vector2 ay = new Vector2(-ax.y, ax.x);
            // Candidate fronts: +ay, -ay (width w along ax), +ax, -ax (width d along ay)
            Vector2[] normals = { ay, -ay, ax, -ax };
            float[] half = { d * 0.5f, d * 0.5f, w * 0.5f, w * 0.5f };
            int best = 0; float bestR = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                float r = _surf.Sample(_surf.R, c + normals[i] * (half[i] + 2f));
                if (r < bestR) { bestR = r; best = i; }
            }
            Vector2 n = normals[best];
            width = best < 2 ? w : d; depth = best < 2 ? d : w;
            frontAxis = new Vector2(n.y, -n.x);
            // Local +Z (the kit's front) must point along n: yaw measured from +Z (north) toward +X (east)
            yaw = Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg;
        }

        static int ParseLevels(OsmMap.Way w)
        {
            if (int.TryParse(w.Tag("building:levels"), out int l) && l > 0) return Mathf.Clamp(l, 1, 40);
            if (float.TryParse(w.Tag("height"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h) && h > 2f)
                return Mathf.Clamp(Mathf.RoundToInt((h - 4.5f) / 3.3f), 1, 40);
            return 0;
        }

        int DefaultLevels(string b, RoadClass near)
        {
            switch (b)
            {
                case "house": case "villa": case "detached": case "garage": case "shed": return 2;
                case "retail": case "commercial": case "supermarket": return 2 + _rnd.Next(3);
                case "hospital": case "hotel": case "office": return 6 + _rnd.Next(5);
                case "apartments": case "residential": return 4 + _rnd.Next(4);
                default: return near >= RoadClass.Secondary ? 4 + _rnd.Next(5) : 2 + _rnd.Next(3);
            }
        }

        BuildingKit.Spec ApartmentSpec(float w, float d, int floors, bool shops) => new BuildingKit.Spec
        {
            Width = w, Depth = d, Floors = Mathf.Max(1, floors), FloorHeight = 3.3f, GroundHeight = shops ? 4.8f : 3.8f,
            Shops = shops, PlasterIndex = _rnd.Next(8), Seed = _seed++, BalconyChance = 0.25f + (float)_rnd.NextDouble() * 0.4f,
            AcChance = 0.4f + (float)_rnd.NextDouble() * 0.4f, StoneGround = _rnd.NextDouble() < 0.5,
        };

        /// <summary>Irregular footprint: plaster walls with a window grid, a parapet and a flat roof.</summary>
        void Extrude(MeshBatch batch, List<Vector2> poly, int floors, int seed)
        {
            var rnd = new System.Random(seed);
            var plaster = _m.Plaster[rnd.Next(_m.Plaster.Length)];
            float y0 = RoadSurface.KerbHeight, ground = 4.2f, fh = 3.3f;
            float top = y0 + ground + floors * fh, parapet = top + 1.0f;
            var wall = batch[plaster];
            var glass = batch[_m.WindowGlass];
            var frame = batch[_m.FrameWhite];
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                Vector2 e = b - a; float len = e.magnitude;
                if (len < 0.2f) continue;
                Vector2 dir = e / len, n2 = new Vector2(dir.y, -dir.x);   // outward for a CCW polygon
                Vector3 n = new Vector3(n2.x, 0, n2.y);
                Vector3 A = new Vector3(a.x, y0, a.y), B = new Vector3(b.x, y0, b.y);
                int v0 = wall.Count;
                wall.AddVertex(A, n, new Vector2(0, y0) / 3f); wall.AddVertex(B, n, new Vector2(len, y0) / 3f);
                wall.AddVertex(new Vector3(b.x, parapet, b.y), n, new Vector2(len, parapet) / 3f); wall.AddVertex(new Vector3(a.x, parapet, a.y), n, new Vector2(0, parapet) / 3f);
                wall.T.AddRange(new[] { v0, v0 + 1, v0 + 2, v0, v0 + 2, v0 + 3 });
                // Parapet inner face
                int v1 = wall.Count;
                wall.AddVertex(new Vector3(a.x, top, a.y) - n * 0.25f, -n, Vector2.zero); wall.AddVertex(new Vector3(b.x, top, b.y) - n * 0.25f, -n, new Vector2(len / 3f, 0));
                wall.AddVertex(new Vector3(b.x, parapet, b.y) - n * 0.25f, -n, new Vector2(len / 3f, 0.33f)); wall.AddVertex(new Vector3(a.x, parapet, a.y) - n * 0.25f, -n, new Vector2(0, 0.33f));
                wall.T.AddRange(new[] { v1, v1 + 1, v1 + 2, v1, v1 + 2, v1 + 3 });
                // Windows: one per 3.4 m bay per floor
                int bays = Mathf.FloorToInt(len / 3.4f);
                if (bays < 1) continue;
                float bay = len / bays;
                for (int f = 0; f < floors; f++)
                {
                    float wy0 = y0 + ground + f * fh + 0.9f, wy1 = wy0 + 1.5f;
                    for (int k = 0; k < bays; k++)
                    {
                        float c = (k + 0.5f) * bay;
                        float hwid = Mathf.Min(0.8f, bay * 0.3f);
                        Vector2 p0 = a + dir * (c - hwid), p1 = a + dir * (c + hwid);
                        Vector3 o = n * 0.02f;
                        Vector3 g0 = new Vector3(p0.x, wy0, p0.y) + o, g1 = new Vector3(p1.x, wy0, p1.y) + o;
                        Vector3 g2 = new Vector3(p1.x, wy1, p1.y) + o, g3 = new Vector3(p0.x, wy1, p0.y) + o;
                        int gv = glass.Count;
                        glass.AddVertex(g0, n, new Vector2(0, 0)); glass.AddVertex(g1, n, new Vector2(1, 0)); glass.AddVertex(g2, n, new Vector2(1, 1)); glass.AddVertex(g3, n, new Vector2(0, 1));
                        glass.T.AddRange(new[] { gv, gv + 1, gv + 2, gv, gv + 2, gv + 3 });
                        // Frame: sill and lintel strips
                        Vector3 fo = n * 0.06f;
                        frame.Quad(g0 + fo - Vector3.up * 0.12f, g1 + fo - Vector3.up * 0.12f, g1 + fo, g0 + fo);
                        frame.Quad(g3 + fo, g2 + fo, g2 + fo + Vector3.up * 0.1f, g3 + fo + Vector3.up * 0.1f);
                    }
                }
            }
            // Roof
            var tris = Geometry.Triangulate(poly);
            var roof = batch[_m.Concrete];
            int r0 = roof.Count;
            foreach (var p in poly) roof.AddVertex(new Vector3(p.x, top, p.y), Vector3.up, p / 4f);
            foreach (var t in tris) roof.T.Add(r0 + t);
        }

        // ------------------------------------------------------------------ generated frontage

        void FrontageFill()
        {
            foreach (var road in _net.Roads)
            {
                if (road.Class == RoadClass.Service || road.Class == RoadClass.Link) continue;
                bool major = road.Class >= RoadClass.Tertiary;
                foreach (int side in new[] { 1, -1 })
                {
                    var edge = RoadNetwork.Offset(road.P, side * (road.HalfWidth + road.Sidewalk));
                    float len = RoadNetwork.Length(edge);
                    float s = 3f;
                    while (s < len - 6f)
                    {
                        Sample(edge, s, out var p, out var dir);
                        Vector2 outward = side > 0 ? new Vector2(dir.y, -dir.x) : new Vector2(-dir.y, dir.x);
                        bool villa = !major && _rnd.NextDouble() < 0.55;
                        float w = villa ? 20f + (float)_rnd.NextDouble() * 10f : major ? 16f + (float)_rnd.NextDouble() * 16f : 14f + (float)_rnd.NextDouble() * 8f;
                        float d = villa ? 22f + (float)_rnd.NextDouble() * 8f : major ? 16f + (float)_rnd.NextDouble() * 8f : 14f + (float)_rnd.NextDouble() * 6f;
                        float setback = villa ? 0.6f : major ? 1.2f : 1.5f;
                        bool placed = false;
                        for (int attempt = 0; attempt < 3 && !placed; attempt++)
                        {
                            var c = p + dir * (w * 0.5f) + outward * (setback + d * 0.5f);
                            if (Fits(c, dir, w, d, 0.4f))
                            {
                                float yaw = Mathf.Atan2(-outward.x, -outward.y) * Mathf.Rad2Deg;   // front (+Z) faces the road
                                var batch = new MeshBatch();
                                var c3 = new Vector3(c.x, RoadSurface.KerbHeight, c.y);
                                float h = Place(batch, c3, yaw, w, d, road.Class, villa, major || _rnd.NextDouble() < 0.2, out var farMat);
                                if (villa) GeneratedVillas++;
                                Commit(batch, c);
                                Colliders.Add((new Vector3(c.x, RoadSurface.KerbHeight + h * 0.5f, c.y), new Vector3(w, h, d), yaw));
                                AddBox(new Vector3(c.x, RoadSurface.KerbHeight, c.y), w, d, h, yaw, villa, farMat);
                                MarkRect(c, dir, w, d);
                                Generated++;
                                placed = true;
                                s += w + (villa ? 0.5f : 1f + (float)_rnd.NextDouble() * 3f);
                            }
                            else { w *= 0.75f; d *= 0.8f; if (w < 10f || d < 10f) break; }
                        }
                        if (!placed) s += 4f;
                    }
                }
            }
        }

        static void Sample(List<Vector2> pts, float s, out Vector2 p, out Vector2 dir)
        {
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float l = (pts[i] - pts[i - 1]).magnitude;
                if (acc + l >= s || i == pts.Count - 1)
                {
                    float t = l > 1e-4f ? Mathf.Clamp01((s - acc) / l) : 0f;
                    p = Vector2.Lerp(pts[i - 1], pts[i], t);
                    dir = l > 1e-4f ? (pts[i] - pts[i - 1]) / l : Vector2.right;
                    return;
                }
                acc += l;
            }
            p = pts[pts.Count - 1]; dir = Vector2.right;
        }

        /// <summary>
        /// One generated building in the style mix of its street: glass towers and modern apartments on the
        /// main roads, modern and traditional villas on residential streets. Returns the height.
        /// </summary>
        float Place(MeshBatch batch, Vector3 c3, float yaw, float w, float d, RoadClass cls, bool villa, bool shops, out Material farMat)
        {
            var mp = ModernKit.Materials();
            double r = _rnd.NextDouble();
            if (villa)
            {
                if (r < 0.5) { ModernKit.Villa(batch, c3, yaw, w, d, _seed++); farMat = mp.Cladding[0]; Modern++; }
                else { BuildingKit.Villa(batch, _m, c3, yaw, w, d, _seed++); farMat = null; }
                return 8f;
            }
            double tower = cls >= RoadClass.Primary ? 0.45 : cls >= RoadClass.Secondary ? 0.15 : 0.0;
            double modern = cls >= RoadClass.Primary ? 0.35 : cls >= RoadClass.Secondary ? 0.4 : cls >= RoadClass.Tertiary ? 0.35 : 0.3;
            if (r < tower && w >= 16f && d >= 14f)
            {
                int fl = cls >= RoadClass.Primary ? 8 + _rnd.Next(11) : 6 + _rnd.Next(6);
                float th = ModernKit.GlassTower(batch, c3, yaw, w, d, fl, _seed++);
                farMat = mp.Glass[_rnd.Next(mp.Glass.Length)]; Modern++;
                return th;
            }
            int floors = cls >= RoadClass.Primary ? 5 + _rnd.Next(5) : cls >= RoadClass.Secondary ? 4 + _rnd.Next(4) : cls >= RoadClass.Tertiary ? 3 + _rnd.Next(3) : 2 + _rnd.Next(3);
            if (r < tower + modern)
            {
                float ah = ModernKit.Apartment(batch, c3, yaw, w, d, floors, shops, _seed++);
                farMat = mp.Cladding[_rnd.Next(3)]; Modern++;
                return ah;
            }
            BuildingKit.Apartment(batch, _m, c3, yaw, ApartmentSpec(w, d, floors, shops));
            farMat = null;
            return (shops ? 4.8f : 3.8f) + 3.3f * floors;
        }

        void AddBox(Vector3 basePos, float w, float d, float h, float yaw, bool villa, Material mat = null)
        {
            var p = mat ?? _m.Plaster[_rnd.Next(_m.Plaster.Length)];
            if (villa)
            {
                Boxes.Add((basePos + Vector3.up * 1.45f, new Vector3(w, 2.9f, d), yaw, p));                      // compound wall
                Boxes.Add((basePos + Vector3.up * 3.6f, new Vector3(w * 0.62f, 7.2f, d * 0.55f), yaw, p));        // house
            }
            else Boxes.Add((basePos + Vector3.up * h * 0.5f, new Vector3(w, h, d), yaw, p));
        }

        // ------------------------------------------------------------------ block interiors

        void InteriorFill()
        {
            const float step = 26f;
            for (float x = _area.xMin + 15f; x < _area.xMax - 15f; x += step)
                for (float z = _area.yMin + 15f; z < _area.yMax - 15f; z += step)
                {
                    var c = new Vector2(x + (float)(_rnd.NextDouble() - 0.5) * 4f, z + (float)(_rnd.NextDouble() - 0.5) * 4f);
                    if (_surf.Sample(_surf.S, c) < 8f || _occ.Get(c)) continue;
                    // Orient to the nearest road (gradient of the road field)
                    float gx = _surf.Sample(_surf.R, c + Vector2.right) - _surf.Sample(_surf.R, c - Vector2.right);
                    float gz = _surf.Sample(_surf.R, c + Vector2.up) - _surf.Sample(_surf.R, c - Vector2.up);
                    var toRoad = -new Vector2(gx, gz).normalized;
                    if (toRoad.sqrMagnitude < 0.5f) toRoad = Vector2.up;
                    var ax = new Vector2(toRoad.y, -toRoad.x);
                    float w = 22f, d = 22f;
                    if (!Fits(c, ax, w, d, 0.4f)) { w = 16f; d = 16f; if (!Fits(c, ax, w, d, 0.4f)) continue; }
                    float yaw = Mathf.Atan2(toRoad.x, toRoad.y) * Mathf.Rad2Deg;
                    var batch = new MeshBatch();
                    var c3 = new Vector3(c.x, RoadSurface.KerbHeight, c.y);
                    bool iv = _rnd.NextDouble() < 0.7;
                    float bw = iv ? w : w - 4f, bd = iv ? d : d - 4f;
                    float ih = Place(batch, c3, yaw, bw, bd, RoadClass.Residential, iv, false, out var farMat);
                    if (iv) GeneratedVillas++;
                    Commit(batch, c);
                    Colliders.Add((new Vector3(c.x, RoadSurface.KerbHeight + ih * 0.5f, c.y), new Vector3(bw, ih, bd), yaw));
                    AddBox(c3, bw, bd, ih, yaw, iv, farMat);
                    MarkRect(c, ax, w, d);
                    Generated++;
                }
        }

        // ------------------------------------------------------------------ street lights and palms

        void StreetLightsAndPalms()
        {
            foreach (var road in _net.Roads)
            {
                if (road.Class < RoadClass.Tertiary) continue;
                bool lit = road.Way.Tag("lit") != "no";
                float spacing = road.Class >= RoadClass.Primary ? 38f : 45f;
                // Lights on the right-hand sidewalk (both sidewalks for two-way roads), 0.8 m from the kerb
                foreach (int side in road.Oneway ? new[] { 1 } : new[] { 1, -1 })
                {
                    if (!lit) break;
                    var line = RoadNetwork.Offset(road.P, side * (road.HalfWidth + 0.8f));
                    float len = RoadNetwork.Length(line);
                    for (float s = spacing * 0.5f; s < len; s += spacing)
                    {
                        Sample(line, s, out var p, out var dir);
                        if (!_area.Contains(p)) continue;
                        float r = _surf.Sample(_surf.R, p);
                        if (r < 0.4f || r > 1.4f) continue;   // in a junction or off the kerb line
                        if (_surf.Sample(_surf.E, p) < 1.5f) continue;   // flyover / trench: RoadStructures lights those
                        Vector2 toRoad = side > 0 ? new Vector2(-dir.y, dir.x) : new Vector2(dir.y, -dir.x);
                        float yaw = Mathf.Atan2(toRoad.x, toRoad.y) * Mathf.Rad2Deg;
                        var batch = new MeshBatch();
                        BuildingKit.StreetLight(batch, _m, new Vector3(p.x, RoadSurface.KerbHeight, p.y), false, yaw);
                        Commit(batch, p);
                        Colliders.Add((new Vector3(p.x, RoadSurface.KerbHeight + 2f, p.y), new Vector3(0.3f, 4f, 0.3f), 0f));
                        Lights++;
                    }
                }
                // Palms in the median of divided roads: points left of a one-way carriageway that sit
                // between two carriageways (raised, and another road within reach on the far side)
                if (road.Oneway && road.Class >= RoadClass.Tertiary)
                {
                    var line = RoadNetwork.Offset(road.P, -(road.HalfWidth + 1.6f));
                    float len = RoadNetwork.Length(line);
                    for (float s = 6f; s < len; s += 11f)
                    {
                        Sample(line, s, out var p, out var dir);
                        if (!_area.Contains(p)) continue;
                        if (_surf.Sample(_surf.R, p) < 1.2f) continue;
                        if (_surf.Sample(_surf.E, p) < 2f) continue;   // no palms in a flyover median or a trench
                        Vector2 left = new Vector2(-dir.y, dir.x);
                        bool median = false;
                        for (float k = 2f; k <= 14f; k += 1f) if (_surf.Sample(_surf.R, p + left * k) < 0f) { median = true; break; }
                        if (!median) continue;
                        Palms.Add(new Vector3(p.x, RoadSurface.KerbHeight, p.y));
                    }
                }
            }
        }

        // ------------------------------------------------------------------ occupancy grid

        sealed class Occupancy
        {
            readonly Rect _r; readonly float _c; readonly int _nx, _nz; readonly bool[] _g;
            public Occupancy(Rect r, float cell) { _r = r; _c = cell; _nx = Mathf.CeilToInt(r.width / cell); _nz = Mathf.CeilToInt(r.height / cell); _g = new bool[_nx * _nz]; }
            bool Idx(Vector2 p, out int k)
            {
                int i = Mathf.FloorToInt((p.x - _r.xMin) / _c), j = Mathf.FloorToInt((p.y - _r.yMin) / _c);
                k = j * _nx + i;
                return i >= 0 && j >= 0 && i < _nx && j < _nz;
            }
            public bool Get(Vector2 p) => !Idx(p, out int k) || _g[k];
            public int Nx => _nx; public int Nz => _nz; public float CellSize => _c;
            public bool GetCell(int i, int j) => i < 0 || j < 0 || i >= _nx || j >= _nz || _g[j * _nx + i];
            public void SetCell(int i, int j) { if (i >= 0 && j >= 0 && i < _nx && j < _nz) _g[j * _nx + i] = true; }
            public Vector2 CellCenter(int i, int j) => new Vector2(_r.xMin + (i + 0.5f) * _c, _r.yMin + (j + 0.5f) * _c);
            public Vector2Int CellOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt((p.x - _r.xMin) / _c), Mathf.FloorToInt((p.y - _r.yMin) / _c));
            public void Set(Vector2 p) { if (Idx(p, out int k)) _g[k] = true; }
            public void FillRect(Vector2 c, Vector2 ax, float w, float d)
            {
                Vector2 ay = new Vector2(-ax.y, ax.x);
                for (float u = -w * 0.5f; u <= w * 0.5f; u += _c * 0.5f)
                    for (float v = -d * 0.5f; v <= d * 0.5f; v += _c * 0.5f) Set(c + ax * u + ay * v);
            }
            public void FillPolygon(List<Vector2> poly)
            {
                var min = poly[0]; var max = poly[0];
                foreach (var p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                for (float x = min.x; x <= max.x; x += _c * 0.5f)
                    for (float z = min.y; z <= max.y; z += _c * 0.5f)
                        if (Geometry.Inside(poly, new Vector2(x, z))) Set(new Vector2(x, z));
            }
        }
    }

    /// <summary>2D polygon helpers: area, centroid, point-in-polygon, minimum-area rectangle, ear clipping.</summary>
    public static class Geometry
    {
        public static float SignedArea(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u.x * v.y - v.x * u.y; }
            return a * 0.5f;
        }

        public static Vector2 Centroid(List<Vector2> p)
        {
            Vector2 c = Vector2.zero;
            foreach (var q in p) c += q;
            return c / p.Count;
        }

        public static bool Inside(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>Minimum-area bounding rectangle: tries each edge direction of the polygon.</summary>
        public static void MinAreaRect(List<Vector2> poly, out Vector2 center, out Vector2 axis, out float width, out float depth)
        {
            float best = float.MaxValue; center = Vector2.zero; axis = Vector2.right; width = depth = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 e = poly[(i + 1) % poly.Count] - poly[i];
                if (e.sqrMagnitude < 1e-4f) continue;
                Vector2 ax = e.normalized, ay = new Vector2(-ax.y, ax.x);
                float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                foreach (var p in poly)
                {
                    float u = Vector2.Dot(p, ax), v = Vector2.Dot(p, ay);
                    u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u); v0 = Mathf.Min(v0, v); v1 = Mathf.Max(v1, v);
                }
                float area = (u1 - u0) * (v1 - v0);
                if (area < best)
                {
                    best = area; axis = ax; width = u1 - u0; depth = v1 - v0;
                    center = ax * ((u0 + u1) * 0.5f) + ay * ((v0 + v1) * 0.5f);
                }
            }
        }

        /// <summary>Ear clipping for a simple counter-clockwise polygon. Returns vertex indices.</summary>
        public static List<int> Triangulate(List<Vector2> poly)
        {
            var result = new List<int>();
            var idx = new List<int>();
            for (int i = 0; i < poly.Count; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                    Vector2 pa = poly[a], pb = poly[b], pc = poly[c];
                    float cross = (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x);
                    if (cross <= 1e-6f) continue;   // reflex
                    bool any = false;
                    foreach (int k in idx)
                    {
                        if (k == a || k == b || k == c) continue;
                        if (InTri(poly[k], pa, pb, pc)) { any = true; break; }
                    }
                    if (any) continue;
                    result.Add(a); result.Add(c); result.Add(b);   // clockwise seen from above = facing up in Unity
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[2]); result.Add(idx[1]); }
            return result;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
