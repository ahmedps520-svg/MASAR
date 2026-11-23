using Masar.Vehicles;
using UnityEngine;

namespace Masar.Game.UI
{
    /// <summary>
    /// The 3D backdrop of the main menu: the selected car parked on asphalt under studio lights,
    /// with the camera slowly circling it. The car is a static display copy: its scripts and
    /// physics are switched off.
    /// </summary>
    public sealed class MenuShowroom : MonoBehaviour
    {
        public Camera cam;
        [Tooltip("Degrees per second of the camera orbit")] public float orbitSpeed = 5f;

        private GameObject _car;
        private Vector3 _focus = new Vector3(0f, 0.7f, 0f);
        private float _radius = 7f;
        private float _angle = 215f;

        /// <summary>Display name of the car on show, or null when the catalog has none.</summary>
        public string CarName { get; private set; }

        private void Start() => ShowSelectedCar();

        public void ShowSelectedCar()
        {
            if (_car != null) Destroy(_car);
            _car = null;
            CarName = null;
            var entry = CarCatalog.Instance != null ? CarCatalog.Instance.FindOrDefault(SettingsStore.Current.selectedCar) : null;
            var prefab = entry?.LoadPrefab();
            if (prefab == null) return;
            CarName = entry.displayName;

            _car = Instantiate(prefab, Vector3.zero, Quaternion.Euler(0f, 90f, 0f));
            _car.name = "ShowroomCar";
            foreach (var b in _car.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
            foreach (var rb in _car.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.detectCollisions = false; }
            foreach (var c in _car.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            foreach (var l in _car.GetComponentsInChildren<AudioListener>(true)) l.enabled = false;

            // Stand the car on the floor and frame it by its rendered size
            var rs = _car.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b0 = rs[0].bounds;
            foreach (var r in rs) b0.Encapsulate(r.bounds);
            _car.transform.position += Vector3.up * -b0.min.y;
            b0.center += Vector3.up * -b0.min.y;
            _focus = new Vector3(b0.center.x, b0.size.y * 0.42f, b0.center.z);
            _radius = Mathf.Max(b0.size.x, b0.size.z) * 2.1f + 3f;
        }

        private void LateUpdate()
        {
            if (cam == null) return;
            _angle += orbitSpeed * Time.unscaledDeltaTime;
            float a = _angle * Mathf.Deg2Rad;
            // A gentle rise and fall so the orbit doesn't feel mechanical
            float h = 1.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 0.11f);
            var pos = _focus + new Vector3(Mathf.Cos(a) * _radius, h, Mathf.Sin(a) * _radius);
            // Aim left of the car so it sits in the right half of the screen, clear of the menu
            var fwd = (_focus - pos).normalized;
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            var look = _focus - right * (_radius * 0.13f) + Vector3.up * 0.15f;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
        }
    }
}
