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
        }

        public void Place(Vector3 pos, Vector3 tan)
        {
            if (_rb == null) Awake();
            var rot = Quaternion.LookRotation(Flat(tan), Vector3.up);
            transform.SetPositionAndRotation(pos, rot);
            _rb.position = pos; _rb.rotation = rot;
            _lastTan = Flat(tan);
        }

        public void Pose(Vector3 pos, Vector3 tan, float speed, float dt, bool braking)
        {
            Vector3 t = Flat(tan);
            // Smooth the heading slightly so polyline corners don't snap the body
            Vector3 heading = Vector3.Slerp(transform.forward, t, Mathf.Clamp01(dt * 12f));
            _rb.MovePosition(pos);
            _rb.MoveRotation(Quaternion.LookRotation(heading, Vector3.up));

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
    }
}
