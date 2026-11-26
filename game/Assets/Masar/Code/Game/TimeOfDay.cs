using System.Collections.Generic;
using Masar.Vehicles;
using Masar.World.Traffic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Masar.Game
{
    /// <summary>
    /// Day, sunset and night for the drive scenes (M3-3), applied at runtime over the scene's
    /// baked day lighting:
    /// - the sun moves (sunset: low in the west-south-west; night: a moon), with its colour and strength;
    /// - sunset and night use the procedural <c>Masar/Sky</c> shader drawn from the same direction,
    ///   because the day HDRI has its sun baked in;
    /// - the baked ambient light, reflection probes and fog are tinted and dimmed to match;
    /// - street-lamp heads glow, and a pool of real spot lights follows the camera to the nearest
    ///   street lamps (city map); the player's headlights come on.
    /// Day restores exactly what the scene was built with.
    /// </summary>
    public sealed class TimeOfDay : MonoBehaviour
    {
        public enum Mode { Day = 0, Sunset = 1, Night = 2 }

        public Mode Current { get; private set; } = Mode.Day;
        public int LampCount => _lamps.Count;
        public int LitLamps { get; private set; }

        struct Look
        {
            public float sunAzimuth, sunElevation, sunIntensity;
            public Color sunColor;
            public Color zenith, horizon, ground, glow;
            public float disc, discSize, glowPower, glowIntensity, stars;
            public Color ambient;      // per-channel scale of the baked day ambient
            public Color fog;
            public float reflections;  // scale of probe and sky reflections
            public Color lampEmission;
            public float lampLight;    // spot light intensity, 0 = lamps off
        }

        static readonly Look SunsetLook = new Look
        {
            sunAzimuth = 248f, sunElevation = 6f, sunIntensity = 1.45f, sunColor = new Color(1f, 0.56f, 0.30f),
            zenith = new Color(0.10f, 0.17f, 0.36f), horizon = new Color(1.00f, 0.52f, 0.26f), ground = new Color(0.22f, 0.15f, 0.10f),
            glow = new Color(1f, 0.48f, 0.18f), disc = 22f, discSize = 0.9996f, glowPower = 6f, glowIntensity = 1.2f, stars = 0f,
            ambient = new Color(0.62f, 0.46f, 0.40f), fog = new Color(0.82f, 0.52f, 0.34f), reflections = 0.55f,
            lampEmission = new Color(2.2f, 1.9f, 1.5f), lampLight = 0f,
        };

        static readonly Look NightLook = new Look
        {
            sunAzimuth = 125f, sunElevation = 48f, sunIntensity = 0.07f, sunColor = new Color(0.55f, 0.66f, 1f),
            zenith = new Color(0.004f, 0.006f, 0.014f), horizon = new Color(0.045f, 0.038f, 0.040f), ground = new Color(0.01f, 0.01f, 0.01f),
            glow = new Color(0.75f, 0.80f, 0.95f), disc = 4f, discSize = 0.99988f, glowPower = 60f, glowIntensity = 0.04f, stars = 0.5f,
            ambient = new Color(0.08f, 0.085f, 0.11f), fog = new Color(0.035f, 0.032f, 0.034f), reflections = 0.08f,
            lampEmission = new Color(7.5f, 6.4f, 5.0f), lampLight = 110f,
        };

        // ---- the scene as built (day)
        bool _captured;
        Light _sun;
        Quaternion _sunRot;
        Color _sunColor;
        float _sunIntensity, _reflectionIntensity;
        Material _daySky;
        SphericalHarmonicsL2 _ambient;
        Color _ambSky, _ambEquator, _ambGround;
        Color _fogColor;
        readonly List<(ReflectionProbe probe, float intensity)> _probes = new List<(ReflectionProbe, float)>();
        Material _sky;

        // ---- street lamps
        readonly List<Vector3> _lamps = new List<Vector3>();       // lamp head positions
        readonly List<Light> _pool = new List<Light>();
        readonly Dictionary<Material, Material> _lampMats = new Dictionary<Material, Material>();
        Color _lampEmission = Color.black;
        float _lampLight, _nextPool;
        VehicleController _car;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public static int PoolSizeForQuality()
        {
            string q = QualitySettings.names[QualitySettings.GetQualityLevel()];
            switch (q) { case "Low": return 4; case "Medium": return 8; case "High": return 16; case "Ultra": return 24; default: return 28; }
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            // City blocks stream in: give their lamp heads the current glow
            if (m == LoadSceneMode.Additive) SwapLampMaterials(s);
        }

        void Capture()
        {
            if (_captured) return;
            _captured = true;
            _sun = RenderSettings.sun;
            if (_sun == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { _sun = l; break; }
            if (_sun != null) { _sunRot = _sun.transform.rotation; _sunColor = _sun.color; _sunIntensity = _sun.intensity; }
            _daySky = RenderSettings.skybox;
            _ambient = RenderSettings.ambientProbe;
            _ambSky = RenderSettings.ambientSkyColor; _ambEquator = RenderSettings.ambientEquatorColor; _ambGround = RenderSettings.ambientGroundColor;
            _fogColor = RenderSettings.fogColor;
            _reflectionIntensity = RenderSettings.reflectionIntensity;
            foreach (var p in FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)) _probes.Add((p, p.intensity));
            FindLamps();
            for (int i = 0; i < SceneManager.sceneCount; i++) SwapLampMaterials(SceneManager.GetSceneAt(i));
        }

        /// <summary>
        /// Street-lamp positions on the city map, from the lamp poles' colliders (the builder gives
        /// each pole a 0.3 x 4 x 0.3 m box, a size nothing else uses). The head hangs 2.75 m out
        /// towards the road and 9.9 m up, so the nearest lane decides which way.
        /// </summary>
        void FindLamps()
        {
            _lamps.Clear();
            var root = GameObject.Find("BuildingColliders");
            if (root == null) return;
            var graph = LaneAutopilot.FindGraph();
            var grid = new Dictionary<Vector2Int, List<Vector3>>();
            if (graph != null)
                foreach (var p in graph.points)
                {
                    var k = new Vector2Int(Mathf.FloorToInt(p.x / 10f), Mathf.FloorToInt(p.z / 10f));
                    if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<Vector3>(8);
                    list.Add(p);
                }
            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
            {
                var sz = box.size;
                if (Mathf.Abs(sz.x - 0.3f) > 0.01f || Mathf.Abs(sz.y - 4f) > 0.01f || Mathf.Abs(sz.z - 0.3f) > 0.01f) continue;
                Vector3 b = box.transform.position - Vector3.up * 2f;   // pole base
                Vector3 toRoad = Vector3.zero;
                float best = 15f * 15f;
                var c = new Vector2Int(Mathf.FloorToInt(b.x / 10f), Mathf.FloorToInt(b.z / 10f));
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (grid.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var list))
                            foreach (var p in list)
                            {
                                Vector3 d = p - b; d.y = 0f;
                                if (d.sqrMagnitude < best) { best = d.sqrMagnitude; toRoad = d.normalized; }
                            }
                _lamps.Add(b + Vector3.up * 9.9f + toRoad * 2.75f);
            }
        }

        void SwapLampMaterials(Scene s)
        {
            if (!s.isLoaded) return;
            foreach (var root in s.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null) continue;
                        if (!_lampMats.TryGetValue(m, out var clone))
                        {
                            if (!m.name.StartsWith("City_LampHead")) continue;
                            // A runtime copy, so the project's material asset is never changed (editor play mode)
                            clone = new Material(m) { name = m.name + " (time of day)" };
                            _lampMats[m] = clone;
                            _lampMats[clone] = clone;
                            SetGlow(clone);
                        }
                        if (clone != m) { mats[i] = clone; changed = true; }
                    }
                    if (changed) r.sharedMaterials = mats;
                }
        }

        void SetGlow(Material m)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionId, _lampEmission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        /// <summary>Applies a time of day. The car gets its headlights on at sunset and night.</summary>
        public void Apply(Mode mode, VehicleController car)
        {
            Capture();
            Current = mode;
            _car = car;
            if (mode == Mode.Day) Restore();
            else Set(mode == Mode.Sunset ? SunsetLook : NightLook);
            TrafficCar.LightsOn = mode != Mode.Day;
            var vis = car != null ? car.GetComponent<VehicleVisuals>() : null;
            if (vis != null) vis.SetHeadlights(mode != Mode.Day);
            Debug.Log($"[MASAR] Time of day: {mode} ({_lamps.Count} street lamps, {_lampMats.Count / 2} lamp materials)");
        }

        void Restore()
        {
            if (_sun != null) { _sun.transform.rotation = _sunRot; _sun.color = _sunColor; _sun.intensity = _sunIntensity; }
            RenderSettings.skybox = _daySky;
            RenderSettings.ambientSkyColor = _ambSky; RenderSettings.ambientEquatorColor = _ambEquator; RenderSettings.ambientGroundColor = _ambGround;
            RenderSettings.ambientProbe = _ambient;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.reflectionIntensity = _reflectionIntensity;
            foreach (var (p, i) in _probes) if (p != null) p.intensity = i;
            _lampEmission = Color.black;
            _lampLight = 0f;
            foreach (var m in _lampMats.Values) SetGlow(m);
            UpdatePool(true);
        }

        void Set(Look k)
        {
            float az = k.sunAzimuth * Mathf.Deg2Rad, el = k.sunElevation * Mathf.Deg2Rad;
            // Map x = east, z = north (OSM projection); azimuth clockwise from north
            Vector3 toSun = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
            if (_sun != null)
            {
                _sun.transform.rotation = Quaternion.LookRotation(-toSun, Vector3.up);
                _sun.color = k.sunColor;
                _sun.intensity = k.sunIntensity;
            }
            if (_sky == null)
            {
                var sh = Resources.Load<Shader>("Shaders/MasarSky");
                if (sh != null) _sky = new Material(sh) { name = "MASAR sky (time of day)" };
                else Debug.LogWarning("[MASAR] Masar/Sky shader missing: keeping the day sky");
            }
            if (_sky != null)
            {
                _sky.SetColor("_Zenith", k.zenith);
                _sky.SetColor("_Horizon", k.horizon);
                _sky.SetColor("_Ground", k.ground);
                _sky.SetColor("_SunColor", k.glow);
                _sky.SetVector("_SunDir", toSun);
                _sky.SetFloat("_SunSize", k.discSize);
                _sky.SetFloat("_DiscIntensity", k.disc);
                _sky.SetFloat("_GlowPower", k.glowPower);
                _sky.SetFloat("_GlowIntensity", k.glowIntensity);
                _sky.SetFloat("_Stars", k.stars);
                RenderSettings.skybox = _sky;
            }
            var amb = _ambient;
            for (int c = 0; c < 9; c++) { amb[0, c] *= k.ambient.r; amb[1, c] *= k.ambient.g; amb[2, c] *= k.ambient.b; }
            // The scenes use trilight ambient, which Unity rebuilds from these colours: scale them too
            RenderSettings.ambientSkyColor = Mul(_ambSky, k.ambient);
            RenderSettings.ambientEquatorColor = Mul(_ambEquator, k.ambient);
            RenderSettings.ambientGroundColor = Mul(_ambGround, k.ambient);
            RenderSettings.ambientProbe = amb;
            RenderSettings.fogColor = k.fog;
            RenderSettings.reflectionIntensity = _reflectionIntensity * k.reflections;
            foreach (var (p, i) in _probes) if (p != null) p.intensity = i * k.reflections;
            _lampEmission = k.lampEmission;
            _lampLight = k.lampLight;
            foreach (var m in _lampMats.Values) SetGlow(m);
            UpdatePool(true);
        }

        static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a);

        void Update()
        {
            if (_lampLight <= 0f || _lamps.Count == 0) return;
            if (Time.unscaledTime < _nextPool) return;
            UpdatePool(false);
        }

        /// <summary>Moves the pool of real lights to the street lamps nearest the camera.</summary>
        void UpdatePool(bool force)
        {
            _nextPool = Time.unscaledTime + 0.25f;
            int want = _lampLight > 0f ? Mathf.Min(PoolSizeForQuality(), _lamps.Count) : 0;
            while (_pool.Count < want)
            {
                var go = new GameObject("StreetLampLight");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 130f;
                l.innerSpotAngle = 60f;
                l.range = 26f;
                l.color = new Color(1f, 0.88f, 0.72f);
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                go.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                _pool.Add(l);
            }
            for (int i = 0; i < _pool.Count; i++) _pool[i].enabled = i < want;
            LitLamps = want;
            if (want == 0) return;
            var cam = Camera.main;
            Vector3 c = cam != null ? cam.transform.position : (_car != null ? _car.transform.position : Vector3.zero);
            // nearest lamps (selection by partial sort; about 1,000 lamps)
            _order.Clear();
            for (int i = 0; i < _lamps.Count; i++) _order.Add(((_lamps[i] - c).sqrMagnitude, i));
            _order.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            for (int i = 0; i < want; i++)
            {
                var l = _pool[i];
                l.transform.position = _lamps[_order[i].Item2];
                // fade the farthest lights so swapping lamps is not visible as a pop
                float d = Mathf.Sqrt(_order[i].Item1);
                float far = Mathf.Sqrt(_order[want - 1].Item1);
                l.intensity = _lampLight * Mathf.Clamp01((far - d) / 25f + 0.15f);
            }
        }

        readonly List<(float, int)> _order = new List<(float, int)>(1024);
    }
}
