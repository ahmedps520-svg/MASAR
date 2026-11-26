using System.Collections.Generic;
using Masar.Core;
using Masar.Vehicles;
using Masar.World.Traffic;
using UnityEngine;

namespace Masar.Game
{
    /// <summary>
    /// Drives the player's car along the city lane graph, for the smoke test and the benchmark
    /// drive. It produces ordinary driver inputs (steer, throttle, brake), so the car is driven
    /// through the same vehicle model as a person would drive it.
    /// - Steering: pure pursuit on a point ahead on the lane path (look-ahead grows with speed).
    /// - Speed: the lane's limit (capped at <see cref="MaxSpeed"/>), slowing early for connectors,
    ///   stopping at red and amber lights, and for anything with a rigidbody ahead (AI cars).
    /// - Route: straight on where possible, never into a lane that dead-ends within 500 m.
    /// </summary>
    [DefaultExecutionOrder(-60)]   // before VehicleController reads the controls
    public sealed class LaneAutopilot : MonoBehaviour, IDriverInputSource
    {
        public float MaxSpeed = 80f / 3.6f;

        public DriverControls Controls { get; private set; }
        public DriverActions Actions { get; private set; }
        public Vector2 Look => Vector2.zero;
        public Vector3 Move => Vector3.zero;

        /// <summary>Distance from the car to its lane path (m).</summary>
        public float LateralError { get; private set; }
        public string State { get; private set; } = "starting";
        public bool HasRoute => _route.Count > 0;

        VehicleController _car;
        CityGraph _g;
        readonly List<int> _route = new List<int>();
        float _s, _steer;
        readonly Dictionary<int, bool> _deadEnd = new Dictionary<int, bool>();
        System.Random _rnd = new System.Random(11);
        readonly RaycastHit[] _hits = new RaycastHit[16];

        const float Decel = 2.6f;

        public static CityGraph FindGraph()
        {
            var ts = TrafficSystem.Instance != null ? TrafficSystem.Instance : FindFirstObjectByType<TrafficSystem>(FindObjectsInactive.Include);
            return ts != null && ts.graph != null && ts.graph.lanes.Length > 0 ? ts.graph : null;
        }

        public bool Begin(VehicleController car, CityGraph graph)
        {
            _car = car; _g = graph;
            _route.Clear();
            if (_car != null && _car.Simulation != null)
            {
                // Drive in D with the automatic, whatever the player had selected (restored by the benchmark)
                var dt = _car.Simulation.Drivetrain;
                dt.Mode = TransmissionKind.Automatic;
                if (!dt.EngineRunning) dt.StartEngine();
                dt.SetSelector(GearSelector.Drive, 0f);
            }
            if (_g == null || _car == null) { State = "no lane graph"; return false; }
            if (!Acquire()) { State = "no lane near the car"; return false; }
            State = "driving";
            return true;
        }

        // ------------------------------------------------------------------ route

        bool Acquire()
        {
            Vector3 p = _car.transform.position, f = Flat(_car.transform.forward);
            float best = 12f; int bestLane = -1; float bestS = 0f;
            for (int i = 0; i < _g.lanes.Length; i++)
            {
                if (_g.lanes[i].junction >= 0) continue;
                float s = Closest(i, p, out float d, out Vector3 tan);
                if (Vector3.Dot(Flat(tan), f) < 0.6f) continue;
                if (d < best) { best = d; bestLane = i; bestS = s; }
            }
            if (bestLane < 0) return false;
            _route.Add(bestLane);
            _s = bestS;
            Extend();
            return true;
        }

        void Extend()
        {
            float ahead = _g.lanes[_route[0]].length - _s;
            for (int k = 1; k < _route.Count; k++) ahead += _g.lanes[_route[k]].length;
            while (ahead < 160f)
            {
                int last = _route[_route.Count - 1];
                int n = ChooseNext(last);
                if (n < 0) break;
                _route.Add(n);
                ahead += _g.lanes[n].length;
            }
        }

        int ChooseNext(int lane)
        {
            var l = _g.lanes[lane];
            if (l.nextCount == 0) return -1;
            float total = 0f;
            var w = new float[l.nextCount];
            for (int k = 0; k < l.nextCount; k++)
            {
                int c = _g.next[l.nextStart + k];
                int t = _g.lanes[c].turn;
                float wt = t == 0 ? 6f : t == 1 ? 1.5f : t == 2 ? 1f : 0.05f;
                if (DeadEnd(c, 500f, 0)) wt *= 0.001f;
                w[k] = wt; total += wt;
            }
            float r = (float)_rnd.NextDouble() * total;
            for (int k = 0; k < l.nextCount; k++) { r -= w[k]; if (r <= 0f) return _g.next[l.nextStart + k]; }
            return _g.next[l.nextStart + l.nextCount - 1];
        }

        /// <summary>True if every path from this lane ends within the given distance.</summary>
        bool DeadEnd(int lane, float within, int depth)
        {
            if (depth == 0 && _deadEnd.TryGetValue(lane, out bool cached)) return cached;
            var l = _g.lanes[lane];
            bool dead;
            if (l.length >= within) dead = false;
            else if (l.nextCount == 0) dead = true;
            else if (depth > 12) dead = false;
            else
            {
                dead = true;
                for (int k = 0; k < l.nextCount && dead; k++)
                    if (!DeadEnd(_g.next[l.nextStart + k], within - l.length, depth + 1)) dead = false;
            }
            if (depth == 0) _deadEnd[lane] = dead;
            return dead;
        }

        float Closest(int lane, Vector3 p, out float dist, out Vector3 tangent)
        {
            var l = _g.lanes[lane];
            float acc = 0f, bestS = 0f, best = float.MaxValue;
            tangent = Vector3.forward;
            int end = l.pointStart + l.pointCount - 1;
            for (int k = l.pointStart; k < end; k++)
            {
                Vector3 a = _g.points[k], b = _g.points[k + 1];
                Vector3 ab = b - a;
                float len = ab.magnitude;
                if (len < 1e-4f) continue;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / (len * len));
                Vector3 q = a + ab * t;
                float d = Flat2(p - q);
                if (d < best) { best = d; bestS = acc + t * len; tangent = ab / len; }
                acc += len;
            }
            dist = best;
            return bestS;
        }

        void Track()
        {
            Vector3 p = _car.transform.position;
            // Move onto the next lane once the car is past the end of the current one
            for (int guard = 0; guard < 4 && _route.Count > 1; guard++)
            {
                float sCur = Closest(_route[0], p, out float dCur, out _);
                float sNext = Closest(_route[1], p, out float dNext, out _);
                bool pastEnd = sCur >= _g.lanes[_route[0]].length - 0.05f;
                if ((pastEnd && dNext < dCur + 1f) || (sNext > 0.5f && dNext + 0.3f < dCur)) _route.RemoveAt(0);
                else break;
            }
            _s = Closest(_route[0], p, out float d, out _);
            LateralError = d;
            Extend();
        }

        void PointAhead(float distance, out Vector3 pos, out Vector3 tan)
        {
            float s = _s + distance;
            for (int k = 0; k < _route.Count; k++)
            {
                float len = _g.lanes[_route[k]].length;
                if (s <= len || k == _route.Count - 1) { _g.Sample(_route[k], Mathf.Min(s, len), out pos, out tan); return; }
                s -= len;
            }
            _g.Sample(_route[0], _s, out pos, out tan);
        }

        // ------------------------------------------------------------------ control

        void FixedUpdate()
        {
            if (_car == null || _g == null || _route.Count == 0) { Controls = new DriverControls { Brake = 1f }; return; }
            Actions = default;

            Track();
            var tr = _car.transform;
            float v = Vector3.Dot(_car.Body.linearVelocity, tr.forward);
            var def = _car.Definition;

            // --- steering: pure pursuit
            float look = Mathf.Clamp(4f + 0.9f * Mathf.Abs(v), 6f, 28f);
            PointAhead(look, out Vector3 target, out _);
            Vector3 local = tr.InverseTransformPoint(target);
            float alpha = Mathf.Atan2(local.x, Mathf.Max(0.1f, local.z));
            float L = Mathf.Max(2f, new Vector2(local.x, local.z).magnitude);
            float delta = Mathf.Atan(2f * def.body.wheelbase * Mathf.Sin(alpha) / L);
            float want = Mathf.Clamp(delta / (def.steering.maxSteerAngle * Mathf.Deg2Rad), -1f, 1f);
            _steer = Mathf.MoveTowards(_steer, want, Time.fixedDeltaTime * 2.5f);

            // --- speed
            float vt = Mathf.Min(MaxSpeed, _g.lanes[_route[0]].speed);
            string why = "cruise";
            float dist = _g.lanes[_route[0]].length - _s;
            for (int k = 1; k < _route.Count && dist < 160f; k++)
            {
                var l = _g.lanes[_route[k]];
                float allowed = Mathf.Sqrt(l.speed * l.speed + 2f * Decel * Mathf.Max(0f, dist - 3f));
                if (allowed < vt) { vt = allowed; why = "curve ahead"; }
                if (l.junction >= 0 && _g.junctions[l.junction].signal)
                {
                    var j = _g.junctions[l.junction];
                    var st = SignalClock.Get(j.greenTime, j.cycleOffset, l.signalGroup, Time.time);
                    bool canStop = dist > v * v / (2f * 4f) + 1f;
                    if (st == SignalClock.State.Red || (st == SignalClock.State.Amber && canStop))
                    {
                        float stopAt = Mathf.Max(0f, dist - 2f);
                        float vs = Mathf.Sqrt(2f * Decel * stopAt);
                        if (vs < vt) { vt = stopAt < 0.5f ? 0f : vs; why = "red light"; }
                    }
                }
                dist += l.length;
            }
            // The route ends (edge of the map): stop before the end
            int lastLane = _route[_route.Count - 1];
            if (_g.lanes[lastLane].nextCount == 0 && dist < 160f)
            {
                float vs = Mathf.Sqrt(2f * Decel * Mathf.Max(0f, dist - 6f));
                if (vs < vt) { vt = dist < 7f ? 0f : vs; why = "end of the map"; }
            }
            // Anything moving ahead (AI cars are kinematic rigidbodies)
            float gap = ObstacleGap(tr, Mathf.Clamp(10f + v * 2.5f, 12f, 70f));
            if (gap < float.MaxValue)
            {
                float vs = Mathf.Sqrt(2f * Decel * Mathf.Max(0f, gap - 5f));
                if (vs < vt) { vt = gap < 6f ? 0f : vs; why = "car ahead"; }
            }
            // Slow down while far off the path (recovering)
            if (LateralError > 2.5f) { vt = Mathf.Min(vt, 6f); why = "off the lane"; }

            float e = vt - v;
            float throttle = 0f, brake = 0f;
            if (vt < 0.2f) brake = v > 0.5f ? Mathf.Clamp01(0.35f + v * 0.08f) : 0.6f;
            else if (e > 0f) throttle = Mathf.Clamp01(0.12f + e * 0.22f);
            else if (e < -0.6f) brake = Mathf.Clamp01(-e * 0.18f);
            State = why;
            Controls = new DriverControls { Steer = _steer, SteerIsAbsolute = true, Throttle = throttle, Brake = brake, Source = InputDeviceKind.None };
        }

        float ObstacleGap(Transform tr, float range)
        {
            Vector3 origin = tr.position + Vector3.up * 0.9f;
            int n = Physics.SphereCastNonAlloc(origin, 0.95f, Flat(tr.forward), _hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                var rb = h.collider.attachedRigidbody;
                if (rb == null || rb == _car.Body) continue;
                if (h.distance > 0f && h.distance < best) best = h.distance;
            }
            return best;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
        static float Flat2(Vector3 v) => v.magnitude;
    }
}
