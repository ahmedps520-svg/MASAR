using System.Collections.Generic;
using System.Text;
using Masar.Game;
using Masar.World.Traffic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Masar.EditorTools.City
{
    /// <summary>
    /// Finds building collision boxes that stand on driving lanes (they stop the car dead).
    /// -executeMethod Masar.EditorTools.City.CityColliderCheck.Run
    /// </summary>
    public static class CityColliderCheck
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(OsmCityBuilder.TahliaScene, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var graph = AssetDatabase.LoadAssetAtPath<CityGraph>("Assets/Masar/Data/Cities/TahliaGraph.asset");
            var session = Object.FindFirstObjectByType<DriveSession>();
            var spawn = session != null && session.spawnPoint != null ? session.spawnPoint.position : graph.spawnPosition;
            Debug.Log($"[MASAR check] spawn {spawn}, graph spawn {graph.spawnPosition}, car {(session ? session.vehicle.transform.position : Vector3.zero)}");
            foreach (var c in Physics.OverlapSphere(spawn + Vector3.up * 0.6f, 2.5f))
                Debug.Log($"[MASAR check] at spawn: {Path(c.transform)} {c.GetType().Name} centre {c.bounds.center} size {(c is BoxCollider b ? b.size : c.bounds.size)} yaw {c.transform.eulerAngles.y:F0}");

            foreach (var arg in System.Environment.GetCommandLineArgs())
                if (arg.StartsWith("at="))
                {
                    var xyz = arg.Substring(3).Split(',');
                    var q = new Vector3(float.Parse(xyz[0], System.Globalization.CultureInfo.InvariantCulture), 0.8f, float.Parse(xyz[1], System.Globalization.CultureInfo.InvariantCulture));
                    float best = float.MaxValue; int bl = -1;
                    for (int l = 0; l < graph.lanes.Length; l++)
                        for (float s = 0f; s < graph.lanes[l].length; s += 1f)
                        {
                            graph.Sample(l, s, out var p, out _);
                            float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z));
                            if (d < best) { best = d; bl = l; }
                        }
                    Debug.Log($"[MASAR check] at {q}: nearest lane {bl} ({(bl >= 0 && graph.lanes[bl].junction >= 0 ? "junction connector" : "road")}) {best:F1} m away");
                    foreach (var c in Physics.OverlapSphere(q, 4f))
                        Debug.Log($"[MASAR check] near {q}: {Path(c.transform)} {c.GetType().Name} centre {c.transform.position} size {(c is BoxCollider bb ? bb.size : c.bounds.size)} yaw {c.transform.eulerAngles.y:F0}");
                }

            // Every lane point (road lanes, not junction connectors), at car height
            var hit = new Dictionary<Collider, int>();
            int samples = 0, blocked = 0;
            var buf = new Collider[16];
            for (int l = 0; l < graph.lanes.Length; l++)
            {
                var lane = graph.lanes[l];
                if (lane.junction >= 0) continue;
                for (float s = 0f; s < lane.length; s += 4f)
                {
                    graph.Sample(l, s, out var p, out _);
                    samples++;
                    int n = Physics.OverlapSphereNonAlloc(p + Vector3.up * 0.8f, 0.5f, buf);
                    bool any = false;
                    for (int k = 0; k < n; k++)
                        if (buf[k].transform.parent != null && buf[k].transform.parent.name == "BuildingColliders") { hit.TryGetValue(buf[k], out int c0); hit[buf[k]] = c0 + 1; any = true; }
                    if (any) blocked++;
                }
            }
            var sizes = new StringBuilder();
            int shown = 0;
            var order = new List<KeyValuePair<Collider, int>>(hit);
            order.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in order)
            {
                if (shown++ >= 120) break;
                var c = kv.Key;
                var size = c is BoxCollider bx ? bx.size : c.bounds.size;
                sizes.Append($"\n  {c.name} lane samples {kv.Value} centre {c.transform.position} size {size} yaw {c.transform.eulerAngles.y:F0}");
            }
            Debug.Log($"[MASAR check] lane samples {samples}, inside a building box {blocked}; boxes on lanes {hit.Count}{sizes}");
        }

        static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
    }
}
