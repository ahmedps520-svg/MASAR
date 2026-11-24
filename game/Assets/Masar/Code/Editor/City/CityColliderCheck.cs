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

            // Every lane point (road lanes, not junction connectors), at car height
            var hit = new HashSet<Collider>();
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
                        if (buf[k].transform.parent != null && buf[k].transform.parent.name == "BuildingColliders") { hit.Add(buf[k]); any = true; }
                    if (any) blocked++;
                }
            }
            var sizes = new StringBuilder();
            int shown = 0;
            foreach (var c in hit)
            {
                if (shown++ >= 25) break;
                var b = (BoxCollider)c;
                sizes.Append($"\n  centre {c.transform.position} size {b.size} yaw {c.transform.eulerAngles.y:F0}");
            }
            Debug.Log($"[MASAR check] lane samples {samples}, inside a building box {blocked}; boxes on lanes {hit.Count}{sizes}");
        }

        static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
    }
}
