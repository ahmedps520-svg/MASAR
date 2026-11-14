using UnityEngine;

namespace Masar.World
{
    /// <summary>
    /// VISUAL PROP ONLY: cycles the lamps of a traffic-signal head on a timer.
    /// This is not traffic control. No vehicle reads it and no rule is enforced.
    /// Real signal controllers come in Month 3/4 with the road network.
    /// </summary>
    public sealed class SignalPropCycler : MonoBehaviour
    {
        public Renderer red, amber, green;
        public float greenTime = 14f, amberTime = 3f, redTime = 17f;
        public float phaseOffset;
        [ColorUsage(false, true)] public Color redOn = new Color(6f, 0.25f, 0.15f);
        [ColorUsage(false, true)] public Color amberOn = new Color(6f, 3.2f, 0.2f);
        [ColorUsage(false, true)] public Color greenOn = new Color(0.2f, 6f, 2.5f);

        private MaterialPropertyBlock _mpb;
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private int _state = -1;

        private void Awake() => _mpb = new MaterialPropertyBlock();

        private void Update()
        {
            float cycle = greenTime + amberTime + redTime;
            float t = Mathf.Repeat(Time.time + phaseOffset, cycle);
            int s = t < greenTime ? 0 : t < greenTime + amberTime ? 1 : 2;
            if (s == _state) return;
            _state = s;
            Set(green, s == 0 ? greenOn : Color.black);
            Set(amber, s == 1 ? amberOn : Color.black);
            Set(red, s == 2 ? redOn : Color.black);
        }

        private void Set(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(Emission, c);
            r.SetPropertyBlock(_mpb);
        }
    }
}
