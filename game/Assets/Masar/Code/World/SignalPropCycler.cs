using Masar.World.Traffic;
using UnityEngine;

namespace Masar.World
{
    /// <summary>
    /// Lamps of a traffic-signal head. In the test block it cycles on its own timer (visual only).
    /// In generated city maps (<see cref="useClock"/>) it shows the junction's <see cref="SignalClock"/>
    /// state for its signal group, the same state the AI traffic obeys.
    /// </summary>
    public sealed class SignalPropCycler : MonoBehaviour
    {
        public Renderer red, amber, green;
        public float greenTime = 14f, amberTime = 3f, redTime = 17f;
        public float phaseOffset;
        [Header("Generated maps: driven by the junction's signal clock")]
        public bool useClock;
        public int group;
        [ColorUsage(false, true)] public Color redOn = new Color(6f, 0.25f, 0.15f);
        [ColorUsage(false, true)] public Color amberOn = new Color(6f, 3.2f, 0.2f);
        [ColorUsage(false, true)] public Color greenOn = new Color(0.2f, 6f, 2.5f);

        private MaterialPropertyBlock _mpb;
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private int _state = -1;

        private void Awake() => _mpb = new MaterialPropertyBlock();

        private void Update()
        {
            int s;
            if (useClock)
            {
                var st = SignalClock.Get(greenTime, phaseOffset, group, Time.time);
                s = st == SignalClock.State.Green ? 0 : st == SignalClock.State.Amber ? 1 : 2;
            }
            else
            {
                float cycle = greenTime + amberTime + redTime;
                float t = Mathf.Repeat(Time.time + phaseOffset, cycle);
                s = t < greenTime ? 0 : t < greenTime + amberTime ? 1 : 2;
            }
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
