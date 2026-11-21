using UnityEngine;

namespace Masar.World.Traffic
{
    /// <summary>
    /// One pedestrian body. PLACEHOLDER: a simple generated figure (thobe, abaya or casual clothes)
    /// with a procedural walk cycle: legs and arms swing about their joints with the walking speed.
    /// It is replaced by imported characters (Mixamo) once the owner provides them.
    /// </summary>
    public sealed class Pedestrian : MonoBehaviour
    {
        public Transform legL, legR, armL, armR, body;
        /// <summary>Imported (Mixamo) characters: the walk clip plays at a rate matching the walking speed.</summary>
        public Animator animator;
        public float clipSpeed = 1.35f;   // m/s the walk clip was authored at
        public float strideLength = 1.3f;
        public float legSwing = 28f, armSwing = 22f;
        float _phase, _bob;
        Vector3 _bodyRest;
        bool _init;

        public void Pose(Vector3 pos, Vector3 forward, float speed, float dt)
        {
            if (!_init) { _init = true; if (body != null) _bodyRest = body.localPosition; }
            forward.y = 0f;
            if (forward.sqrMagnitude > 1e-6f)
            {
                var target = Quaternion.LookRotation(forward.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, Mathf.Clamp01(dt * 8f));
            }
            transform.position = pos;
            if (animator != null) { animator.speed = speed / clipSpeed; return; }
            _phase = Mathf.Repeat(_phase + speed / strideLength * dt * Mathf.PI * 2f, Mathf.PI * 2f);
            float amount = Mathf.Clamp01(speed / 1.2f);
            float sw = Mathf.Sin(_phase) * amount;
            if (legL != null) legL.localRotation = Quaternion.Euler(sw * legSwing, 0f, 0f);
            if (legR != null) legR.localRotation = Quaternion.Euler(-sw * legSwing, 0f, 0f);
            if (armL != null) armL.localRotation = Quaternion.Euler(-sw * armSwing, 0f, 3f);
            if (armR != null) armR.localRotation = Quaternion.Euler(sw * armSwing, 0f, -3f);
            _bob = Mathf.Abs(Mathf.Cos(_phase)) * 0.035f * amount;
            if (body != null) body.localPosition = _bodyRest + Vector3.up * _bob;
        }
    }
}
