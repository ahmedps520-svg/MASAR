using System.Collections.Generic;
using UnityEngine;

namespace Masar.World
{
    /// <summary>
    /// One 128 m city cell: merged full-detail geometry near the camera, merged walls/glass/roofs
    /// beyond <see cref="CityLodManager.NearDistance"/>.
    /// </summary>
    public sealed class CellLod : MonoBehaviour
    {
        public GameObject near, mid;
        public Vector3 center;
        public float radius = 90f;
        internal int state = -1;

        /// <summary>Set by the graphics tier (Low 0.6 ... Max 1.6).</summary>
        public static float DistanceScale = 1f;

        internal static readonly List<CellLod> All = new List<CellLod>();
        void OnEnable() { All.Add(this); state = -1; CityLodManager.Dirty = true; }
        void OnDisable() => All.Remove(this);

        internal void Apply(Vector3 cam, float nearDist)
        {
            Vector3 d = center - cam; d.y = 0f;
            int s = d.magnitude - radius < nearDist ? 0 : 1;
            if (s == state) return;
            state = s;
            if (near != null) near.SetActive(s == 0);
            if (mid != null) mid.SetActive(s == 1);
        }
    }
}
