using System.Collections.Generic;
using UnityEngine;

namespace Masar.World.Traffic
{
    /// <summary>
    /// Pedestrians on the sidewalk walkways of a <see cref="CityGraph"/> (Month 2 v1). They walk on the
    /// right of the walkway, turn back at its ends, stop for the player's car when it comes at them,
    /// and spawn out of view around the player. They do not cross roads yet.
    /// The bodies are placeholders (<see cref="Pedestrian"/>) until real characters are imported.
    /// </summary>
    public sealed class PedestrianSystem : MonoBehaviour
    {
        public CityGraph graph;
        public Pedestrian[] prefabs;
        public static int Density = 2;
        static readonly int[] Targets = { 0, 18, 40, 70 };
        public float spawnMin = 30f, spawnMax = 150f, despawnDistance = 190f;

        sealed class Ped { public Pedestrian view; public int prefab, walk, dir; public float s, speed, pause; }

        readonly List<Ped> _peds = new List<Ped>();
        Stack<Pedestrian>[] _pool;
        System.Random _rnd = new System.Random(11);
        float _next;
        bool _initial = true;

        public int Count => _peds.Count;

        void Awake()
        {
            if (graph == null || graph.walks.Length == 0 || prefabs == null || prefabs.Length == 0) { enabled = false; return; }
            _pool = new Stack<Pedestrian>[prefabs.Length];
            for (int i = 0; i < _pool.Length; i++) _pool[i] = new Stack<Pedestrian>();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var player = TrafficSystem.Player;
            Vector3 pp = player != null ? player.position : Vector3.zero;
            Vector3 pv = player != null ? player.linearVelocity : Vector3.zero;
            for (int i = _peds.Count - 1; i >= 0; i--)
            {
                var p = _peds[i];
                var w = graph.walks[p.walk];
                float speed = p.speed;
                if (p.pause > 0f) { p.pause -= dt; speed = 0f; }
                // Stop when the player's car is coming at them
                if (player != null)
                {
                    Vector3 d = p.view.transform.position - pp;
                    if (d.sqrMagnitude < 36f && Vector3.Dot(pv, d) > 0.5f) speed = 0f;
                }
                p.s += p.dir * speed * dt;
                if (p.s <= 0f || p.s >= w.length)
                {
                    p.s = Mathf.Clamp(p.s, 0f, w.length);
                    p.dir = -p.dir;
                    p.pause = 0.6f + (float)_rnd.NextDouble() * 1.5f;
                }
                graph.SampleWalk(p.walk, p.s, out var pos, out var tan);
                Vector3 fwd = tan * p.dir;
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                p.view.Pose(pos + right * 0.55f, fwd, speed, dt);
            }
            if (Time.time >= _next)
            {
                _next = Time.time + 0.3f;
                Manage(pp, player != null);
            }
        }

        void Manage(Vector3 pp, bool havePlayer)
        {
            int target = havePlayer ? Targets[Mathf.Clamp(Density, 0, 3)] : 0;
            var cam = Camera.main;
            for (int i = _peds.Count - 1; i >= 0; i--)
            {
                var p = _peds[i];
                float d = (p.view.transform.position - pp).magnitude;
                bool vis = cam != null && Visible(cam, p.view.transform.position);
                if (d > despawnDistance || (_peds.Count > target && !vis)) Remove(i);
            }
            int budget = _initial ? target : 2;
            for (int tries = 0; tries < budget * 8 && budget > 0 && _peds.Count < target; tries++)
            {
                int wk = _rnd.Next(graph.walks.Length);
                var w = graph.walks[wk];
                float s = (float)_rnd.NextDouble() * w.length;
                graph.SampleWalk(wk, s, out var pos, out _);
                float d = (pos - pp).magnitude;
                if (d < (_initial ? 12f : spawnMin) || d > spawnMax) continue;
                if (!_initial && cam != null && Visible(cam, pos) && d < 110f) continue;
                int pf = _rnd.Next(prefabs.Length);
                var view = _pool[pf].Count > 0 ? _pool[pf].Pop() : Instantiate(prefabs[pf], transform);
                view.gameObject.SetActive(true);
                _peds.Add(new Ped { view = view, prefab = pf, walk = wk, s = s, dir = _rnd.Next(2) == 0 ? 1 : -1, speed = 1.05f + (float)_rnd.NextDouble() * 0.45f });
                budget--;
            }
            _initial = false;
        }

        static bool Visible(Camera cam, Vector3 p)
        {
            var v = cam.WorldToViewportPoint(p + Vector3.up);
            return v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        }

        void Remove(int i)
        {
            var p = _peds[i];
            p.view.gameObject.SetActive(false);
            _pool[p.prefab].Push(p.view);
            _peds.RemoveAt(i);
        }
    }
}
