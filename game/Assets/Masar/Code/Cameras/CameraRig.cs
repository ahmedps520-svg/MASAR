using System;
using Masar.Core;
using Masar.Vehicles;
using UnityEngine;

namespace Masar.Cameras
{
    /// <summary>
    /// The six camera modes, driven from the vehicle's interpolated transform and telemetry.
    /// Runs in LateUpdate after the vehicle visuals.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        public VehicleController target;
        public CameraMode mode = CameraMode.Chase;
        public CameraModeSettings[] settings;

        /// <summary>Raised when the free camera starts or stops capturing movement input.</summary>
        public event Action<bool> FreeCameraChanged;
        public event Action<CameraMode> ModeChanged;

        public IDriverInputSource Input { get; set; }
        public Camera Cam { get; private set; }
        public CameraModeSettings Current => settings[(int)mode];

        private VehicleCameraAnchors _anchors;
        private Vector3 _pos;
        private Quaternion _rot = Quaternion.identity;
        private float _lookYaw, _lookPitch, _lastLookTime;
        private Vector3 _headOffset, _headVel;
        private Vector3 _cinePos;
        private bool _cineValid;
        private float _blend = 1f;
        private Vector3 _blendFromPos;
        private Quaternion _blendFromRot;
        private float _freeYaw, _freePitch;
        private float _fov;

        private static readonly CameraMode[] Cycle =
            { CameraMode.Chase, CameraMode.Cockpit, CameraMode.Hood, CameraMode.Passenger, CameraMode.Cinematic, CameraMode.Free };

        private void Awake()
        {
            Cam = GetComponent<Camera>();
            EnsureSettings();
            _pos = transform.position;
            _rot = transform.rotation;
            _fov = Cam.fieldOfView;
        }

        public void EnsureSettings()
        {
            int n = Enum.GetValues(typeof(CameraMode)).Length;
            if (settings == null || settings.Length != n)
            {
                var s = new CameraModeSettings[n];
                for (int i = 0; i < n; i++)
                    s[i] = settings != null && i < settings.Length && settings[i] != null ? settings[i] : CameraModeSettings.DefaultFor((CameraMode)i);
                settings = s;
            }
        }

        public void SetTarget(VehicleController v)
        {
            target = v;
            _anchors = v != null ? v.GetComponentInChildren<VehicleCameraAnchors>() : null;
            _cineValid = false;
        }

        public void SetMode(CameraMode m)
        {
            if (m == mode) return;
            bool wasFree = mode == CameraMode.Free;
            _blendFromPos = transform.position;
            _blendFromRot = transform.rotation;
            _blend = 0f;
            mode = m;
            _lookYaw = _lookPitch = 0f;
            _cineValid = false;
            if (m == CameraMode.Free)
            {
                var e = transform.rotation.eulerAngles;
                _freeYaw = e.y; _freePitch = e.x > 180f ? e.x - 360f : e.x;
                _pos = transform.position;
                _blend = 1f;
            }
            if (wasFree != (m == CameraMode.Free)) FreeCameraChanged?.Invoke(m == CameraMode.Free);
            ModeChanged?.Invoke(m);
        }

        public void NextMode(int dir = 1)
        {
            int idx = Array.IndexOf(Cycle, mode);
            idx = (idx + dir + Cycle.Length) % Cycle.Length;
            SetMode(Cycle[idx]);
        }

        private void LateUpdate()
        {
            if (Input != null)
            {
                var a = Input.Actions;
                if (a.NextCamera) NextMode(1);
                if (a.PreviousCamera) NextMode(-1);
            }
            if (target == null) return;
            if (_anchors == null) _anchors = target.GetComponentInChildren<VehicleCameraAnchors>();

            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            var s = Current;
            Vector3 pos; Quaternion rot; float near = 0.25f;

            switch (mode)
            {
                case CameraMode.Cockpit: Interior(s, _anchors ? _anchors.driverEye : null, new Vector3(-0.37f, 1.18f, -0.15f), dt, out pos, out rot); near = 0.03f; break;
                case CameraMode.Passenger: Interior(s, _anchors ? _anchors.passengerEye : null, new Vector3(0.37f, 1.18f, -0.15f), dt, out pos, out rot); near = 0.03f; break;
                case CameraMode.Hood: Hood(s, dt, out pos, out rot); near = 0.05f; break;
                case CameraMode.Cinematic: Cinematic(s, dt, out pos, out rot); break;
                case CameraMode.Free: Free(s, dt, out pos, out rot); near = 0.1f; break;
                default: Chase(s, dt, out pos, out rot); break;
            }

            // Mode-switch blend
            if (_blend < 1f)
            {
                _blend = Mathf.Min(1f, _blend + dt / 0.35f);
                float k = Mathf.SmoothStep(0f, 1f, _blend);
                pos = Vector3.Lerp(_blendFromPos, pos, k);
                rot = Quaternion.Slerp(_blendFromRot, rot, k);
            }

            // Shake (speed-scaled noise), applied after smoothing so it stays crisp
            if (s.shake > 0f && mode != CameraMode.Free)
            {
                float speed = target.Telemetry.SpeedMs;
                float amp = s.shake * Mathf.Clamp01(speed / 35f) * 0.0045f;
                float tt = Time.time * 23f;
                pos += rot * new Vector3((Mathf.PerlinNoise(tt, 0.3f) - 0.5f) * amp, (Mathf.PerlinNoise(0.7f, tt) - 0.5f) * amp, 0f);
            }

            transform.SetPositionAndRotation(pos, rot);
            _fov = Mathf.Lerp(_fov, mode == CameraMode.Cinematic ? _cineFov : s.fieldOfView, 1f - Mathf.Exp(-8f * dt));
            Cam.fieldOfView = _fov;
            Cam.nearClipPlane = near;
        }

        // ------------------------------------------------------------------ modes

        private float SmoothK(float smoothing, float dt) => 1f - Mathf.Exp(-Mathf.Lerp(40f, 2.5f, smoothing) * dt);

        private void Chase(CameraModeSettings s, float dt, out Vector3 pos, out Quaternion rot)
        {
            var t = target.transform;
            Vector3 look = _anchors && _anchors.lookTarget ? _anchors.lookTarget.position : t.position + Vector3.up * 1.0f;
            Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-3f) fwd = Vector3.forward;

            // Optional orbit from look input
            ApplyLook(s, dt, autoCenter: true);
            Quaternion orbit = Quaternion.AngleAxis(_lookYaw, Vector3.up);
            Vector3 desired = t.position + Vector3.up * s.height - (orbit * fwd) * s.followDistance + t.rotation * s.offset;

            // Keep the camera out of walls: cast from the look target toward the desired position
            Vector3 dir = desired - look;
            float dist = dir.magnitude;
            if (dist > 0.01f && Physics.SphereCast(look, 0.25f, dir / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.attachedRigidbody != target.Body)
                desired = look + dir / dist * Mathf.Max(0.8f, hit.distance - 0.1f);

            _pos = Vector3.Lerp(_pos, desired, SmoothK(s.smoothing, dt));
            pos = _pos;
            rot = Quaternion.LookRotation(look - pos + Vector3.up * 0.0f, Vector3.up) * Quaternion.Euler(s.pitch * 0.25f, 0f, 0f);
            _rot = rot;
        }

        private void Interior(CameraModeSettings s, Transform eye, Vector3 fallbackLocal, float dt, out Vector3 pos, out Quaternion rot)
        {
            var t = target.transform;
            // Head motion from G-forces: lean against acceleration, spring-damped
            Vector3 g = target.Telemetry.LocalAccelerationG;
            Vector3 headTarget = new Vector3(-g.x * 0.035f, -Mathf.Abs(g.y - 0f) * 0.0f, -g.z * 0.03f) * s.headMotion;
            headTarget = Vector3.ClampMagnitude(headTarget, 0.06f);
            _headOffset = Vector3.SmoothDamp(_headOffset, headTarget, ref _headVel, 0.12f, Mathf.Infinity, dt);

            ApplyLook(s, dt, autoCenter: true);
            Vector3 local = (eye ? t.InverseTransformPoint(eye.position) : fallbackLocal) + s.offset + _headOffset;
            pos = t.TransformPoint(local);
            Quaternion baseRot = eye ? eye.rotation : t.rotation;
            rot = baseRot * Quaternion.Euler(s.pitch + _lookPitch, _lookYaw, 0f);
        }

        private void Hood(CameraModeSettings s, float dt, out Vector3 pos, out Quaternion rot)
        {
            var t = target.transform;
            Vector3 local = _anchors && _anchors.hood ? t.InverseTransformPoint(_anchors.hood.position) : new Vector3(0f, 1.25f, 0.9f);
            pos = t.TransformPoint(local + s.offset);
            // Slight smoothing of roll and pitch only, so the horizon doesn't jitter
            Quaternion targetRot = t.rotation * Quaternion.Euler(s.pitch, 0f, 0f);
            _rot = Quaternion.Slerp(_rot, targetRot, SmoothK(s.smoothing, dt));
            rot = _rot;
        }

        private float _cineFov = 35f;

        private void Cinematic(CameraModeSettings s, float dt, out Vector3 pos, out Quaternion rot)
        {
            var t = target.transform;
            Vector3 carPos = t.position + Vector3.up * 0.9f;
            Vector3 vel = target.Body ? target.Body.linearVelocity : Vector3.zero;
            if (!_cineValid || (carPos - _cinePos).sqrMagnitude > 50f * 50f)
            {
                // Place a new "trackside" camera ahead of the car, off to one side
                Vector3 fwd = vel.sqrMagnitude > 4f ? vel.normalized : t.forward;
                Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized * (UnityEngine.Random.value > 0.5f ? 1f : -1f);
                Vector3 p = carPos + fwd * Mathf.Clamp(vel.magnitude * 2.2f, 8f, 40f) + side * UnityEngine.Random.Range(6f, 11f);
                p.y = carPos.y + UnityEngine.Random.Range(0.4f, 3.0f);
                if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore))
                    p.y = Mathf.Max(p.y, hit.point.y + 0.6f);
                _cinePos = p;
                _cineValid = true;
                _rot = Quaternion.LookRotation(carPos - p, Vector3.up);
            }
            pos = _cinePos;
            Quaternion want = Quaternion.LookRotation(carPos - pos, Vector3.up);
            _rot = Quaternion.Slerp(_rot, want, SmoothK(s.smoothing, dt));
            rot = _rot;
            float d = Vector3.Distance(pos, carPos);
            _cineFov = Mathf.Clamp(2f * Mathf.Atan(4.5f / Mathf.Max(1f, d)) * Mathf.Rad2Deg, 12f, s.fieldOfView);
        }

        private void Free(CameraModeSettings s, float dt, out Vector3 pos, out Quaternion rot)
        {
            Vector2 look = Input != null ? Input.Look : Vector2.zero;
            Vector3 move = Input != null ? Input.Move : Vector3.zero;
            _freeYaw += look.x * s.lookSensitivity;
            _freePitch = Mathf.Clamp(_freePitch - look.y * s.lookSensitivity, -85f, 85f);
            rot = Quaternion.Euler(_freePitch, _freeYaw, 0f);
            float speed = 9f;
            _pos += rot * new Vector3(move.x, 0f, move.z) * speed * dt + Vector3.up * move.y * speed * dt;
            pos = _pos;
        }

        private void ApplyLook(CameraModeSettings s, float dt, bool autoCenter)
        {
            Vector2 look = Input != null ? Input.Look : Vector2.zero;
            if (look.sqrMagnitude > 1e-6f)
            {
                _lookYaw = Mathf.Clamp(_lookYaw + look.x * s.lookSensitivity, -150f, 150f);
                _lookPitch = Mathf.Clamp(_lookPitch - look.y * s.lookSensitivity, -40f, 40f);
                _lastLookTime = Time.unscaledTime;
            }
            else if (autoCenter && Time.unscaledTime - _lastLookTime > 1.2f)
            {
                float k = 1f - Mathf.Exp(-4f * dt);
                _lookYaw = Mathf.Lerp(_lookYaw, 0f, k);
                _lookPitch = Mathf.Lerp(_lookPitch, 0f, k);
            }
        }
    }
}
