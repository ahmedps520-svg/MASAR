using System;
using UnityEngine;

namespace Masar.Input
{
    /// <summary>Per-device-kind tuning, saved with the player's settings.</summary>
    [Serializable]
    public class InputCalibration
    {
        [Header("Steering")]
        [Range(0f, 0.3f)] public float steerDeadzone = 0.02f;
        [Tooltip("1 = linear. >1 makes the center less sensitive.")]
        [Range(0.5f, 3f)] public float steerExponent = 1.0f;
        [Range(0.2f, 2f)] public float steerSensitivity = 1.0f;
        public bool invertSteer;

        [Header("Wheel only")]
        [Tooltip("Physical wheel rotation lock-to-lock in degrees (the G923 is 900).")]
        public float wheelRotationRange = 900f;
        [Tooltip("Rotation range used by the game (degrees). 0 = match the vehicle's steering lock.")]
        public float wheelSoftLockRange;

        [Header("Pedals")]
        [Range(0f, 0.3f)] public float pedalDeadzone = 0.03f;
        public bool invertThrottle;
        public bool invertBrake;
        public bool invertClutch;
        [Tooltip("Raw axis value at pedal released / fully pressed (set by the calibration screen).")]
        public float throttleRawMin = -1f, throttleRawMax = 1f;
        public float brakeRawMin = -1f, brakeRawMax = 1f;
        public float clutchRawMin = -1f, clutchRawMax = 1f;

        [Header("Digital steering (keyboard)")]
        [Tooltip("Steering rate toward the target at low speed (units/s).")]
        public float digitalSteerRate = 2.2f;
        [Tooltip("Return-to-center rate (units/s).")]
        public float digitalReturnRate = 3.5f;

        public float ShapeSteer(float raw)
        {
            float s = invertSteer ? -raw : raw;
            float a = Mathf.Abs(s);
            if (a < steerDeadzone) return 0f;
            a = (a - steerDeadzone) / (1f - steerDeadzone);
            a = Mathf.Pow(Mathf.Clamp01(a), steerExponent) * steerSensitivity;
            return Mathf.Clamp(Mathf.Sign(s) * a, -1f, 1f);
        }

        public float ShapePedal(float raw, float rawMin, float rawMax, bool invert)
        {
            float v = Mathf.InverseLerp(rawMin, rawMax, raw);
            if (invert) v = 1f - v;
            if (v < pedalDeadzone) return 0f;
            return Mathf.Clamp01((v - pedalDeadzone) / (1f - pedalDeadzone));
        }
    }
}
