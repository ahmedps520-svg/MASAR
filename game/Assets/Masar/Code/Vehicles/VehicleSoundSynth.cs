using UnityEngine;

namespace Masar.Vehicles
{
    /// <summary>
    /// Real-time synthesis of a car's sound from its simulation state, with no recorded samples.
    /// - Engine: a train of exhaust pulses at the firing frequency (rpm x cylinders / 120 for a
    ///   four-stroke), each pulse a short decaying burst whose strength follows the load, shaped
    ///   by three resonances (exhaust body, pipe, intake) whose balance brightens under load.
    ///   Small per-cylinder differences give the engine its uneven character.
    /// - Turbo: a whine that rises with boost, and a blow-off hiss when the throttle closes on boost.
    /// - Tyres: squeal (a narrow, wandering tone plus band noise) from how far the tyres slide.
    /// - Rolling noise and wind that grow with speed.
    /// - Interior: a muffled mix (low-pass) with less wind, for the cockpit cameras.
    /// Pure C#, called on the audio thread; the parameters are plain fields written by the main
    /// thread and smoothed here per sample, so torn reads only ever cause tiny glitches.
    /// </summary>
    public sealed class VehicleSoundSynth
    {
        // ---- inputs (main thread)
        public float Rpm = 800f;
        public float Load;            // 0..1, how hard the engine works
        public float Throttle;        // 0..1
        public int Cylinders = 4;
        public bool Turbo;
        public bool EngineRunning = true;
        public float SpeedMs;
        public float TyreSlip;        // 0..1 squeal amount
        public bool Interior;
        public float Volume = 0.8f;
        /// <summary>0 = deterministic pulses (tests). The default gives the small cylinder-to-cylinder variation.</summary>
        public float Roughness = 1f;

        readonly int _sr;
        float _rpm, _load, _speed, _slip, _interior, _boost, _blowOff, _vol;
        bool _thrWasHigh;
        double _firePhase;
        int _cyl;
        float _pulseEnv, _pulseAmp;
        Resonator _body, _pipe, _intake, _squealBand, _windBand;
        float _rollLp, _rollLp2, _muffle1, _muffle2, _dc, _dcOut;
        double _whinePhase, _squealPhase;
        float _squealWander;
        uint _rng = 22222u;
        readonly float[] _cylTrim = new float[16];

        public VehicleSoundSynth(int sampleRate)
        {
            _sr = Mathf.Max(8000, sampleRate);
            _rpm = Rpm;
            var r = new System.Random(5);
            for (int i = 0; i < _cylTrim.Length; i++) _cylTrim[i] = (float)(r.NextDouble() * 2.0 - 1.0);
            _body.Set(_sr, 140f, 1.1f);
            _pipe.Set(_sr, 420f, 2.2f);
            _intake.Set(_sr, 1500f, 2.5f);
            _squealBand.Set(_sr, 1050f, 6f);
            _windBand.Set(_sr, 900f, 0.7f);
        }

        float Noise() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / 8388608f - 1f; }

        /// <summary>Fills an interleaved buffer (adds nothing to what is there: it overwrites).</summary>
        public void Render(float[] data, int channels)
        {
            int frames = data.Length / Mathf.Max(1, channels);
            float k = 1f - Mathf.Exp(-1f / (0.03f * _sr));        // ~30 ms parameter smoothing
            float kSlow = 1f - Mathf.Exp(-1f / (0.6f * _sr));     // turbo spool
            float targetRpm = EngineRunning ? Mathf.Max(0f, Rpm) : 0f;
            float targetLoad = Mathf.Clamp01(Load);
            int cyl = Mathf.Clamp(Cylinders, 1, 16);
            float targetBoost = Turbo ? Mathf.Clamp01(Throttle * Mathf.InverseLerp(1800f, 3800f, Rpm)) : 0f;
            bool thrHigh = Throttle > 0.5f;
            if (_thrWasHigh && Throttle < 0.15f && _boost > 0.35f) _blowOff = Mathf.Max(_blowOff, _boost);
            _thrWasHigh = thrHigh || (_thrWasHigh && Throttle >= 0.15f);
            float muffleK = 1f - Mathf.Exp(-2f * Mathf.PI * 1100f / _sr);
            float rollK = 1f - Mathf.Exp(-2f * Mathf.PI * 220f / _sr);

            for (int f = 0; f < frames; f++)
            {
                _rpm += (targetRpm - _rpm) * k;
                _load += (targetLoad - _load) * k;
                _speed += (SpeedMs - _speed) * k;
                _slip += (Mathf.Clamp01(TyreSlip) - _slip) * k;
                _interior += ((Interior ? 1f : 0f) - _interior) * k;
                _vol += (Mathf.Clamp01(Volume) - _vol) * k;
                _boost += (targetBoost - _boost) * kSlow;

                // ---- engine: exhaust pulses at the firing frequency
                float fire = _rpm / 60f * cyl * 0.5f;
                float engine = 0f;
                if (_rpm > 60f)
                {
                    _firePhase += fire / _sr;
                    if (_firePhase >= 1.0)
                    {
                        _firePhase -= 1.0;
                        _cyl = (_cyl + 1) % cyl;
                        float uneven = 1f + Roughness * (0.10f * _cylTrim[_cyl] + 0.06f * Noise());
                        _pulseAmp = (0.30f + 0.70f * _load) * uneven;
                        _pulseEnv = 1f;
                    }
                    // Each pulse decays within about a third of the firing period (never longer than 4 ms)
                    float decay = Mathf.Exp(-1f / (Mathf.Min(0.004f, 0.33f / Mathf.Max(fire, 1f)) * _sr));
                    float excite = _pulseAmp * _pulseEnv * (0.85f + 0.15f * Roughness * Noise());
                    _pulseEnv *= decay;
                    // Louder with more cylinders firing per second is real, but keep the level in range
                    float norm = 1f / Mathf.Sqrt(Mathf.Max(1f, fire / 60f));
                    float b = _body.Process(excite);
                    float p = _pipe.Process(excite);
                    float i = _intake.Process(excite * (0.3f + 0.7f * _load));
                    engine = (1.0f * b + (0.55f + 0.35f * _load) * p + (0.12f + 0.4f * _load) * i + 0.25f * excite) * norm * 1.6f;
                }

                // ---- turbo whine and blow-off
                float turbo = 0f;
                if (Turbo)
                {
                    _whinePhase += (1800f + 6500f * _boost) / _sr;
                    if (_whinePhase > 1.0) _whinePhase -= 1.0;
                    turbo = Mathf.Sin((float)(_whinePhase * 2.0 * Mathf.PI)) * 0.035f * _boost * _boost;
                    if (_blowOff > 0.01f)
                    {
                        turbo += Noise() * 0.10f * _blowOff;
                        _blowOff *= 1f - 4f / _sr;
                    }
                }

                // ---- tyres
                float squeal = 0f;
                if (_slip > 0.002f)
                {
                    _squealWander += (Noise() * 0.5f - _squealWander) * (40f / _sr);
                    _squealPhase += (980f + 160f * _squealWander + 60f * Mathf.Sin(_slip * 7f)) / _sr;
                    if (_squealPhase > 1.0) _squealPhase -= 1.0;
                    float tone = Mathf.Sin((float)(_squealPhase * 2.0 * Mathf.PI));
                    squeal = (0.6f * tone + 0.8f * _squealBand.Process(Noise())) * 0.22f * _slip;
                }
                float sp = Mathf.Clamp01(_speed / 33f);
                _rollLp += (Noise() - _rollLp) * rollK;
                _rollLp2 += (_rollLp - _rollLp2) * rollK;
                float roll = _rollLp2 * 1.4f * sp;
                float windAmt = sp * sp * (1f - 0.65f * _interior);
                float wind = _windBand.Process(Noise()) * 0.10f * windAmt;

                // ---- mix, interior muffling, DC block, soft clip
                float ext = engine * 0.55f + turbo + squeal + roll + wind;
                _muffle1 += (ext - _muffle1) * muffleK;
                _muffle2 += (_muffle1 - _muffle2) * muffleK;
                float mix = Mathf.Lerp(ext, _muffle2 * 1.35f, _interior);
                float hp = mix - _dc + 0.995f * _dcOut; _dc = mix; _dcOut = hp;
                float o = SoftClip(hp * 1.2f) * _vol;
                for (int c = 0; c < channels; c++) data[f * channels + c] = o;
            }
        }

        static float SoftClip(float x) => x / (1f + Mathf.Abs(x));

        /// <summary>Constant-peak-gain band-pass biquad (RBJ cookbook).</summary>
        struct Resonator
        {
            float b0, b2, a1, a2, x1, x2, y1, y2;

            public void Set(int sr, float freq, float q)
            {
                float w0 = 2f * Mathf.PI * freq / sr;
                float alpha = Mathf.Sin(w0) / (2f * q);
                float a0 = 1f + alpha;
                b0 = alpha / a0; b2 = -alpha / a0;
                a1 = -2f * Mathf.Cos(w0) / a0; a2 = (1f - alpha) / a0;
                x1 = x2 = y1 = y2 = 0f;
            }

            public float Process(float x)
            {
                float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }
    }
}
