using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// An OpenStreetMap extract (Overpass JSON, see tools/osm/fetch-osm.ps1) projected into local meters:
    /// +X east, +Z north, origin at the center of the download box. Jeddah is flat enough that the
    /// map is built on a flat plane (OSM has no elevation).
    /// Data (c) OpenStreetMap contributors, ODbL 1.0.
    /// </summary>
    public sealed class OsmMap
    {
        public sealed class Way
        {
            public long Id;
            public List<long> Nodes = new List<long>();
            public Dictionary<string, string> Tags = new Dictionary<string, string>();
            public string Tag(string k) => Tags.TryGetValue(k, out var v) ? v : null;
        }

        public sealed class Node
        {
            public long Id;
            public Vector2 P;   // local meters (x east, y north)
            public Dictionary<string, string> Tags;
            public string Tag(string k) => Tags != null && Tags.TryGetValue(k, out var v) ? v : null;
        }

        public readonly Dictionary<long, Node> Nodes = new Dictionary<long, Node>();
        public readonly List<Way> Ways = new List<Way>();
        public double Lat0, Lon0;
        public Rect Bounds;   // the download box in local meters

        const double MetersPerDegLat = 110574.0, MetersPerDegLonEquator = 111320.0;

        public Vector2 Project(double lat, double lon) =>
            new Vector2((float)((lon - Lon0) * MetersPerDegLonEquator * Math.Cos(Lat0 * Math.PI / 180.0)), (float)((lat - Lat0) * MetersPerDegLat));

        public static OsmMap Load(string jsonPath, double south, double west, double north, double east)
        {
            var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(jsonPath));
            var map = new OsmMap { Lat0 = (south + north) * 0.5, Lon0 = (west + east) * 0.5 };
            var a = map.Project(south, west);
            var b = map.Project(north, east);
            map.Bounds = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            foreach (var o in (List<object>)root["elements"])
            {
                var e = (Dictionary<string, object>)o;
                string type = (string)e["type"];
                long id = Convert.ToInt64(e["id"], CultureInfo.InvariantCulture);
                Dictionary<string, string> tags = null;
                if (e.TryGetValue("tags", out var t))
                {
                    tags = new Dictionary<string, string>();
                    foreach (var kv in (Dictionary<string, object>)t) tags[kv.Key] = kv.Value?.ToString();
                }
                if (type == "node")
                {
                    if (!e.ContainsKey("lat")) continue;
                    double lat = Convert.ToDouble(e["lat"], CultureInfo.InvariantCulture), lon = Convert.ToDouble(e["lon"], CultureInfo.InvariantCulture);
                    if (map.Nodes.TryGetValue(id, out var existing)) { if (tags != null) existing.Tags = tags; continue; }
                    map.Nodes[id] = new Node { Id = id, P = map.Project(lat, lon), Tags = tags };
                }
                else if (type == "way")
                {
                    var w = new Way { Id = id, Tags = tags ?? new Dictionary<string, string>() };
                    if (e.TryGetValue("nodes", out var ns)) foreach (var n in (List<object>)ns) w.Nodes.Add(Convert.ToInt64(n, CultureInfo.InvariantCulture));
                    if (w.Nodes.Count >= 2) map.Ways.Add(w);
                }
            }
            // Overpass returns the same way twice when it matches several clauses: keep one, merging tags
            var seen = new Dictionary<long, Way>();
            var unique = new List<Way>();
            foreach (var w in map.Ways)
            {
                if (seen.TryGetValue(w.Id, out var first)) { foreach (var kv in w.Tags) first.Tags[kv.Key] = kv.Value; continue; }
                seen[w.Id] = w; unique.Add(w);
            }
            map.Ways.Clear(); map.Ways.AddRange(unique);
            return map;
        }

        public List<Vector2> Points(Way w)
        {
            var list = new List<Vector2>(w.Nodes.Count);
            foreach (var id in w.Nodes) if (Nodes.TryGetValue(id, out var n)) list.Add(n.P);
            return list;
        }
    }

    /// <summary>A small JSON reader (objects, arrays, strings, numbers, booleans, null) for editor tools.</summary>
    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i); i++; // ':'
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return d;   // '}'
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return l;   // ']'
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    char e = s[i];
                    if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == 'b' ? '\b' : e == 'f' ? '\f' : e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }
    }
}
