using UnityEngine;

namespace Masar.World.Traffic
{
    /// <summary>
    /// Visual body of one AI traffic car: a kinematic rigidbody moved along its lane by
    /// <see cref="TrafficSystem"/>, spinning wheels, front wheels steered from the path curvature,
    /// and brake lights.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TrafficCar : MonoBehaviour
    {
        public Transform[] wheels = new Transform[4];   // FL, FR, RL, RR
        public float wheelRadius = 0.33f, wheelbase = 2.7f;
        public Renderer[] brakeLights;
        [ColorUsage(false, true)] public Color brakeOn = new Color(4f, 0.08f, 0.05f);
        [ColorUsage(false, true)] public Color tailOn = new Color(0.9f, 0.02f, 0.01f);

        /// <summary>Headlights on for every traffic car (sunset and night).</summary>
        public static bool LightsOn;
        [ColorUsage(false, true)] public Color headOn = new Color(6f, 6f, 5.5f);
        [ColorUsage(false, true)] public Color indicatorOn = new Color(5f, 1.9f, 0.05f);

        Renderer[] _heads;
        Renderer[] _indicators;   // left front, left rear, right front, right rear
        int _indicator, _shownIndicator = 99;
        bool _shownLights;
        float _blink;

        Rigidbody _rb;
        Quaternion[] _rest;
        float _spin, _steer;
        Vector3 _lastTan;
        bool _brake;
        MaterialPropertyBlock _mpb;
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rest = new Quaternion[wheels.Length];
            for (int i = 0; i < wheels.Length; i++) if (wheels[i] != null) _rest[i] = wheels[i].localRotation;
            _mpb = new MaterialPropertyBlock();
            SetBrake(false, true);
            BuildLamps();
        }

        /// <summary>
        /// Headlight renderers come from the car body; the indicators are small lamps added here,
        /// just outboard of the head- and taillights, using the headlight's own (emissive) material.
        /// </summary>
        void BuildLamps()
        {
            var heads = new System.Collections.Generic.List<Renderer>();
            Transform hl = null, hr = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Headlight_L") hl = t;
                if (t.name == "Headlight_R") hr = t;
            }
            foreach (var t in new[] { hl, hr }) if (t != null && t.GetComponent<Renderer>() != null) heads.Add(t.GetComponent<Renderer>());
            _heads = heads.ToArray();
            if (_heads.Length == 0 || brakeLights == null || brakeLights.Length < 2) { _indicators = new Renderer[0]; return; }
            var mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var mat = _heads[0].sharedMaterial;
            _indicators = new Renderer[4];
            var sources = new[] { hl, brakeLights[0].transform, hr, brakeLights[1].transform };
            for (int i = 0; i < 4; i++)
            {
                var src = sources[i];
                if (src == null) continue;
                var b = src.GetComponent<Renderer>().bounds;
                Vector3 c = transform.InverseTransformPoint(b.center);
                float side = Mathf.Sign(c.x == 0f ? (i < 2 ? -1f : 1f) : c.x);
                Vector3 ext = transform.InverseTransformVector(b.extents);
                var go = new GameObject(i < 2 ? "Indicator_L" : "Indicator_R");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(c.x + side * (Mathf.Abs(ext.x) + 0.07f), c.y, c.z);
                go.transform.localScale = new Vector3(0.10f, 0.07f, 0.12f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _indicators[i] = r;
            }
        }

        /// <summary>-1 left, 0 off, +1 right.</summary>
        public void SetIndicator(int dir) { _indicator = dir; if (dir != 0) _blink = 0f; }

        void UpdateLamps(float dt)
        {
            if (LightsOn != _shownLights)
            {
                _shownLights = LightsOn;
                foreach (var r in _heads) Emit(r, LightsOn ? headOn : Color.black);
            }
            _blink += dt;
            bool phase = Mathf.Repeat(_blink * 1.5f, 1f) < 0.5f;   // 90 flashes a minute
            int show = _indicator != 0 && phase ? _indicator : 0;
            if (show == _shownIndicator || _indicators == null) return;
            _shownIndicator = show;
            for (int i = 0; i < _indicators.Length; i++)
            {
                if (_indicators[i] == null) continue;
                bool on = (show < 0 && i < 2) || (show > 0 && i >= 2);
                Emit(_indicators[i], on ? indicatorOn : Color.black);
            }
        }

        void Emit(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(Emission, c);
            r.SetPropertyBlock(_mpb);
        }

        public void Place(Vector3 pos, Vector3 tan)
        {
            if (_rb == null) Awake();
            var rot = Quaternion.LookRotation(Pitched(Flat(tan), tan), Vector3.up);
            transform.SetPositionAndRotation(pos, rot);
            _rb.position = pos; _rb.rotation = rot;
            _lastTan = Flat(tan);
        }

        public void Pose(Vector3 pos, Vector3 tan, float speed, float dt, bool braking)
        {
            Vector3 t = Flat(tan);
            // Smooth the heading slightly so polyline corners don't snap the body
            Vector3 heading = Vector3.Slerp(Flat(transform.forward), t, Mathf.Clamp01(dt * 12f));
            _rb.MovePosition(pos);
            // Pitch with the road on ramps and flyovers
            _rb.MoveRotation(Quaternion.LookRotation(Pitched(heading, tan), Vector3.up));

            float yawRate = Vector3.SignedAngle(_lastTan, t, Vector3.up) * Mathf.Deg2Rad / Mathf.Max(1e-4f, dt);
            _lastTan = t;
            float steer = speed > 0.5f ? Mathf.Atan(wheelbase * yawRate / speed) * Mathf.Rad2Deg : _steer;
            _steer = Mathf.Lerp(_steer, Mathf.Clamp(steer, -35f, 35f), Mathf.Clamp01(dt * 6f));
            _spin = Mathf.Repeat(_spin + speed * dt / wheelRadius * Mathf.Rad2Deg, 360f);
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                var steerQ = i < 2 ? Quaternion.Euler(0f, _steer, 0f) : Quaternion.identity;
                wheels[i].localRotation = steerQ * _rest[i] * Quaternion.Euler(_spin, 0f, 0f);
            }
            if (braking != _brake) SetBrake(braking, false);
            UpdateLamps(dt);
        }

        void SetBrake(bool on, bool force)
        {
            if (!force && on == _brake) return;
            _brake = on;
            if (brakeLights == null) return;
            foreach (var r in brakeLights)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(Emission, on ? brakeOn : tailOn);
                r.SetPropertyBlock(_mpb);
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        static Vector3 Pitched(Vector3 flatHeading, Vector3 tan)
        {
            float horiz = new Vector2(tan.x, tan.z).magnitude;
            float slope = horiz > 1e-4f ? Mathf.Clamp(tan.y / horiz, -0.2f, 0.2f) : 0f;
            return (flatHeading + Vector3.up * slope).normalized;
        }
    }
}
