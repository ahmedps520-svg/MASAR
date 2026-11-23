using System.Collections.Generic;
using Masar.World;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// 3D road structures for the parts of the network that leave the ground (<see cref="RoadGrades"/>):
    /// - Flyover decks: asphalt deck, concrete fascia and soffit, New Jersey barriers, wall piers with
    ///   pier caps every 30 m (moved off the roads below), and lights on the barriers of main roads.
    /// - Ramps and embankments: the deck sits on solid retaining walls down to the ground.
    /// - Trenches and tunnels: the road at its depth between retaining walls, barriers along the top,
    ///   a roof slab where a ground road crosses over (or along a tagged tunnel) with portal faces and
    ///   ceiling light strips.
    /// Barriers and walls are left out where another deck joins at the same height (merges, splits)
    /// and where a ground road meets the ramp foot.
    /// </summary>
    public sealed class RoadStructures
    {
        public const float Lift = 0.02f;        // decks sit just above the ground asphalt where they overlap
        const float Fascia = 1.5f;              // deck structure depth
        const float BarrierH = 0.85f, BarrierW = 0.45f, Edge = 0.55f;
        const float LidDepth = 1.2f;

        readonly RoadNetwork _net; readonly RoadSurface _surf; readonly CityMaterials _m;
        readonly Material _light;
        readonly System.Func<Vector2, MeshBatch> _chunk;
        readonly Dictionary<Vector2Int, List<(RoadNetwork.Road r, int s)>> _hash = new Dictionary<Vector2Int, List<(RoadNetwork.Road, int)>>();
        const float HashCell = 24f;

        public int Segments, Piers, Lids, Lights;

        public RoadStructures(RoadNetwork net, RoadSurface surf, CityMaterials m, Material light, System.Func<Vector2, MeshBatch> chunk)
        {
            _net = net; _surf = surf; _m = m; _light = light; _chunk = chunk;
            foreach (var r in net.Roads)
            {
                if (!r.Graded) continue;
                for (int s = 1; s < r.P.Count; s++)
                {
                    if (!RoadSurface.SegGraded(r, s)) continue;
                    var c = Cell((r.P[s - 1] + r.P[s]) * 0.5f);
                    if (!_hash.TryGetValue(c, out var l)) _hash[c] = l = new List<(RoadNetwork.Road, int)>();
                    l.Add((r, s));
                }
            }
        }

        static Vector2Int Cell(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / HashCell), Mathf.FloorToInt(p.y / HashCell));

        /// <summary>Is q on another road's deck at about height h (a merge, split or crossing at the same level)?</summary>
        bool OtherDeck(Vector2 q, RoadNetwork.Road self, float h)
        {
            var c = Cell(q);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_hash.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var l)) continue;
                    foreach (var (r, s) in l)
                    {
                        if (r == self) continue;
                        float d = RoadNetwork.SegDist(q, r.P[s - 1], r.P[s], out float t);
                        if (d > r.HalfWidth + Edge + 0.3f) continue;
                        if (Mathf.Abs(Mathf.Lerp(r.H[s - 1], r.H[s], t) - h) < 1.5f) return true;
                    }
                }
            return false;
        }

        public void Build()
        {
            foreach (var r in _net.Roads)
            {
                if (!r.Graded) continue;
                float w = r.HalfWidth + Edge;
                var L = RoadNetwork.Offset(r.P, -w);
                var Rt = RoadNetwork.Offset(r.P, w);
                var Lo = RoadNetwork.Offset(r.P, -(w + 0.3f));
                var Ro = RoadNetwork.Offset(r.P, w + 0.3f);
                bool lit = r.Class >= RoadClass.Secondary;
                bool lidBefore = false;
                float arc = 0f, nextPier = 15f, nextLight = 20f;
                for (int s = 1; s < r.P.Count; s++)
                {
                    float segLen = (r.P[s] - r.P[s - 1]).magnitude;
                    if (!RoadSurface.SegGraded(r, s)) { arc += segLen; lidBefore = false; continue; }
                    Segments++;
                    int a = s - 1, b = s;
                    float ha = r.H[a], hb = r.H[b], hm = (ha + hb) * 0.5f;
                    Vector2 mid = (r.P[a] + r.P[b]) * 0.5f;
                    var mb = _chunk(mid);
                    bool up = hm >= 0f;
                    float ya = up ? ha + Lift : ha, yb = up ? hb + Lift : hb;
                    // Deck top (asphalt, world UVs like the ground roads)
                    Face(mb[_m.Asphalt], V(L[a], ya), V(Rt[a], ya), V(Rt[b], yb), V(L[b], yb), Vector3.up, 7f);

                    foreach (int side in new[] { -1, 1 })
                    {
                        var E = side < 0 ? L : Rt;          // deck edge
                        var O = side < 0 ? Lo : Ro;         // just outside
                        Vector2 dir = (r.P[b] - r.P[a]).normalized;
                        Vector2 n2 = new Vector2(dir.y, -dir.x) * side;   // outward
                        Vector3 nOut = new Vector3(n2.x, 0f, n2.y);
                        Vector2 q = (O[a] + O[b]) * 0.5f;
                        bool joined = OtherDeck(q, r, hm);
                        int k = _surf.Index(q);
                        if (up)
                        {
                            bool atFoot = hm < 0.6f && _surf.Rg[k] < 0.3f;
                            bool open = r.Bridge && Mathf.Min(ha, hb) > 2.5f;
                            if (!joined)
                            {
                                // Outer face: barrier + fascia (open deck) or retaining wall down to the ground
                                float ba = open ? ya - Fascia : -0.3f, bb = open ? yb - Fascia : -0.3f;
                                float ta = atFoot ? ya : ya + BarrierH, tb = atFoot ? yb : yb + BarrierH;
                                Face(mb[_m.Concrete], V(E[a], ba), V(E[b], bb), V(E[b], tb), V(E[a], ta), nOut, 2f);
                                if (!atFoot) Barrier(mb[_m.Concrete], E[a], E[b], ya, yb, -nOut);
                            }
                            // Soffit under an open deck (once per segment)
                            if (open && side < 0) Face(mb[_m.Concrete], V(L[a], ya - Fascia), V(L[b], yb - Fascia), V(Rt[b], yb - Fascia), V(Rt[a], ya - Fascia), Vector3.down, 3f);
                            // Lights on the barrier of main roads, alternating sides
                            if (lit && !joined && !atFoot && hm > 3f && side == (Lights % 2 == 0 ? 1 : -1) && arc + segLen > nextLight)
                            {
                                Vector2 lp = E[a] + (E[b] - E[a]) * 0.5f - n2 * (BarrierW * 0.5f);
                                BuildingKit.StreetLight(mb, _m, new Vector3(lp.x, hm + Lift + BarrierH, lp.y), false, Mathf.Atan2(-n2.x, -n2.y) * Mathf.Rad2Deg);
                                Lights++; nextLight += 40f;
                            }
                        }
                        else
                        {
                            // Trench / tunnel: retaining wall facing the road, from the road up to the ground
                            bool crossing = _surf.Crossing(k);
                            if (!joined)
                            {
                                float top = crossing || r.Tunnel ? -LidDepth : RoadSurface.KerbHeight;
                                if (top > Mathf.Min(ya, yb))
                                    Face(mb[_m.Concrete], V(E[a], ya), V(E[b], yb), V(E[b], Mathf.Max(top, yb)), V(E[a], Mathf.Max(top, ya)), -nOut, 2f);
                                if (!crossing && !r.Tunnel && Mathf.Min(ha, hb) < -0.5f)
                                {
                                    // Barrier on the rim, facing out to the street and in to the trench
                                    float y0 = RoadSurface.KerbHeight;
                                    Vector2 ia = E[a], ib = E[b], oa = E[a] + n2 * 0.3f, ob = E[b] + n2 * 0.3f;
                                    Face(mb[_m.Concrete], V(ia, y0), V(ib, y0), V(ib, y0 + 0.9f), V(ia, y0 + 0.9f), -nOut, 2f);
                                    Face(mb[_m.Concrete], V(oa, y0), V(ob, y0), V(ob, y0 + 0.9f), V(oa, y0 + 0.9f), nOut, 2f);
                                    Face(mb[_m.Concrete], V(ia, y0 + 0.9f), V(ib, y0 + 0.9f), V(ob, y0 + 0.9f), V(oa, y0 + 0.9f), Vector3.up, 2f);
                                }
                            }
                        }
                    }

                    // Roof slab over the trench: tagged tunnels, and where a ground road crosses
                    if (!up)
                    {
                        bool lid = r.Tunnel || _surf.Crossing(_surf.Index(mid)) || _surf.Crossing(_surf.Index((L[a] + L[b]) * 0.5f)) || _surf.Crossing(_surf.Index((Rt[a] + Rt[b]) * 0.5f));
                        lid &= Mathf.Min(ha, hb) < -(LidDepth + 2.5f);
                        if (lid)
                        {
                            Lids++;
                            var c = mb[_m.Concrete];
                            Face(c, V(L[a], -LidDepth), V(Rt[a], -LidDepth), V(Rt[b], -LidDepth), V(L[b], -LidDepth), Vector3.down, 3f);
                            // Ceiling light strip
                            Vector2 cl = (L[a] + Rt[a]) * 0.5f, cr = (L[b] + Rt[b]) * 0.5f;
                            Vector2 dn = (Rt[a] - L[a]).normalized * 0.2f;
                            Face(mb[_light], V(cl - dn, -LidDepth + 0.01f), V(cl + dn, -LidDepth + 0.01f), V(cr + dn, -LidDepth + 0.01f), V(cr - dn, -LidDepth + 0.01f), Vector3.down, 1f);
                            if (!lidBefore) Portal(c, L[a], Rt[a], r.P[a] - r.P[b]);
                        }
                        else if (lidBefore) Portal(mb[_m.Concrete], L[a], Rt[a], r.P[b] - r.P[a]);
                        lidBefore = lid;
                    }
                    else lidBefore = false;

                    // Piers under open decks, off the roads below
                    if (r.Bridge && hm > 3f && arc + segLen > nextPier)
                    {
                        Vector2 dir = (r.P[b] - r.P[a]).normalized;
                        Vector2 at = Vector2.Lerp(r.P[a], r.P[b], Mathf.Clamp01((nextPier - arc) / Mathf.Max(0.01f, segLen)));
                        float[] tries = { 0f, 3f, -3f, 6f, -6f };
                        foreach (float off in tries)
                        {
                            var p = at + dir * off;
                            if (_surf.Sample(_surf.Rg, p) < 1.0f || _surf.VoidField(_surf.Index(p)) > 0f) continue;
                            Pier(mb[_m.Concrete], p, dir, r.HalfWidth, Mathf.Lerp(ha, hb, 0.5f) + Lift - Fascia);
                            Piers++;
                            break;
                        }
                        nextPier += 30f;
                    }
                    arc += segLen;
                }
            }
            Debug.Log($"[MASAR] Structures: {Segments} deck segments, {Piers} piers, {Lids} roof slabs, {Lights} deck lights");
        }

        static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        /// <summary>A quad facing n; UVs from world x/z (top) or along/up (walls).</summary>
        static void Face(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, float uvScale)
        {
            Vector2 Uv(Vector3 p) => Mathf.Abs(n.y) > 0.7f ? new Vector2(p.x, p.z) / uvScale : new Vector2(p.x + p.z, p.y) / uvScale;
            int i = mb.Count;
            mb.AddVertex(a, n, Uv(a)); mb.AddVertex(b, n, Uv(b)); mb.AddVertex(c, n, Uv(c)); mb.AddVertex(d, n, Uv(d));
            Vector3 fn = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(fn, n) >= 0f) { mb.T.Add(i); mb.T.Add(i + 1); mb.T.Add(i + 2); mb.T.Add(i); mb.T.Add(i + 2); mb.T.Add(i + 3); }
            else { mb.T.Add(i); mb.T.Add(i + 2); mb.T.Add(i + 1); mb.T.Add(i); mb.T.Add(i + 3); mb.T.Add(i + 2); }
        }

        /// <summary>New Jersey barrier on a deck edge (inward = toward the road): sloped lower face, upright upper face, flat top.</summary>
        static void Barrier(MeshBuilder mb, Vector2 ea, Vector2 eb, float ya, float yb, Vector3 inward)
        {
            Vector2 n = new Vector2(inward.x, inward.z);
            // Profile from the outer face: top width 0.2, toe at BarrierW
            Vector2 ta = ea + n * 0.2f, tb = eb + n * 0.2f;           // top inner edge
            Vector2 ka = ea + n * 0.28f, kb = eb + n * 0.28f;         // knee
            Vector2 fa = ea + n * BarrierW, fb = eb + n * BarrierW;   // toe
            float kneeH = 0.33f;
            Face(mb, V(fa, ya), V(fb, yb), V(kb, yb + kneeH), V(ka, ya + kneeH), (inward * 0.4f + Vector3.up).normalized, 2f);
            Face(mb, V(ka, ya + kneeH), V(kb, yb + kneeH), V(tb, yb + BarrierH), V(ta, ya + BarrierH), (inward + Vector3.up * 0.1f).normalized, 2f);
            Face(mb, V(ta, ya + BarrierH), V(tb, yb + BarrierH), V(eb, yb + BarrierH), V(ea, ya + BarrierH), Vector3.up, 2f);
        }

        static void Pier(MeshBuilder mb, Vector2 p, Vector2 dir, float halfWidth, float top)
        {
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            float across = Mathf.Clamp(halfWidth * 0.9f, 2.5f, 7f);
            mb.Transform = Matrix4x4.TRS(new Vector3(p.x, 0f, p.y), Quaternion.Euler(0f, yaw, 0f), Vector3.one);
            // Wall pier (across the deck, thin along it) and a wider cap under the deck
            mb.Box(new Vector3(-across, -0.2f, -0.7f), new Vector3(across, top - 1.0f, 0.7f), 2f, bottom: false, top: false);
            mb.Box(new Vector3(-halfWidth * 0.95f, top - 1.0f, -0.9f), new Vector3(halfWidth * 0.95f, top, 0.9f), 2f, bottom: true, top: false);
            mb.Transform = Matrix4x4.identity;
        }

        /// <summary>Vertical face closing the gap between the roof slab and the ground at a tunnel mouth (facing out along `outward`).</summary>
        static void Portal(MeshBuilder mb, Vector2 l, Vector2 r, Vector2 outward)
        {
            var n = new Vector3(outward.x, 0f, outward.y).normalized;
            Face(mb, V(l, -LidDepth), V(r, -LidDepth), V(r, RoadSurface.KerbHeight), V(l, RoadSurface.KerbHeight), n, 2f);
        }
    }
}
