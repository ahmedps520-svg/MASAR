using System.Collections.Generic;
using UnityEngine;

namespace Masar.Vehicles
{
    /// <summary>
    /// Plays the player's car through <see cref="VehicleSoundSynth"/>: reads the simulation each
    /// frame (rpm, load, tyre slip, speed) and renders the sound on the audio thread. A silent
    /// looping clip keeps the AudioSource playing so the filter callback always runs.
    /// Optional recording (first channel) for QA: the smoke test saves it as a WAV.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class VehicleAudio : MonoBehaviour
    {
        /// <summary>Cockpit cameras: muffled mix.</summary>
        public bool Interior;
        public float Volume = 0.8f;

        VehicleController _car;
        VehicleSoundSynth _synth;
        AudioSource _src;
        float _peakTorque = 1f;

        public bool Record;
        readonly List<float> _recorded = new List<float>();
        readonly object _lock = new object();
        public int SampleRate { get; private set; }

        public static VehicleAudio Ensure(VehicleController car)
        {
            var a = car.GetComponent<VehicleAudio>();
            return a != null ? a : car.gameObject.AddComponent<VehicleAudio>();
        }

        void Awake()
        {
            _car = GetComponent<VehicleController>();
            SampleRate = AudioSettings.outputSampleRate;
            _synth = new VehicleSoundSynth(SampleRate);
            var def = _car != null ? _car.Definition : null;
            if (def != null)
            {
                _synth.Cylinders = def.engine.cylinders;
                _synth.Turbo = def.engine.turbo;
                foreach (var t in def.engine.torqueCurveNm) _peakTorque = Mathf.Max(_peakTorque, t);
            }
            _src = GetComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.loop = true;
            _src.spatialBlend = 0f;
            _src.dopplerLevel = 0f;
            _src.priority = 0;
            _src.clip = AudioClip.Create("MASAR silence", 1024, 1, SampleRate, false);
            _src.Play();
        }

        void Update()
        {
            if (_car == null || _car.Simulation == null) return;
            var sim = _car.Simulation;
            var tel = _car.Telemetry;
            var dt = sim.Drivetrain;
            float torqueShare = Mathf.Clamp01(dt.EngineTorque / _peakTorque);
            _synth.Rpm = tel.EngineRpm;
            _synth.EngineRunning = tel.EngineRunning;
            _synth.Throttle = tel.Throttle;
            _synth.Load = Mathf.Clamp01(0.65f * tel.Throttle + 0.35f * torqueShare);
            _synth.SpeedMs = tel.SpeedMs;
            float slide = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = sim.Wheels[i];
                if (!w.Grounded) continue;
                float combined = Mathf.Sqrt(w.SlipRatio * w.SlipRatio + w.TanSlipAngle * w.TanSlipAngle);
                slide = Mathf.Max(slide, Mathf.InverseLerp(0.12f, 0.35f, combined));   // from about the grip peak of the tyre model
            }
            _synth.TyreSlip = slide * Mathf.InverseLerp(1.5f, 5f, tel.SpeedMs);
            _synth.Interior = Interior;
            _synth.Volume = Volume;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_synth == null) return;
            _synth.Render(data, channels);
            if (Record)
                lock (_lock)
                    for (int i = 0; i < data.Length; i += channels)
                        if (_recorded.Count < SampleRate * 120) _recorded.Add(data[i]);
        }

        /// <summary>Writes what was recorded as 16-bit mono PCM WAV. Returns the sample count.</summary>
        public int SaveRecording(string path)
        {
            float[] samples;
            lock (_lock) { samples = _recorded.ToArray(); _recorded.Clear(); }
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                int bytes = samples.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (var s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
            }
            return samples.Length;
        }
    }
}
