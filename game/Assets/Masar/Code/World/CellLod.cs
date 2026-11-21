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

    /// <summary>
    /// One 512 m block of cells. Beyond <see cref="CityLodManager.FarDistance"/> all its cells are
    /// switched off and one merged far mesh per material is drawn instead (few draw calls for the
    /// skyline).
    /// </summary>
    public sealed class BlockLod : MonoBehaviour
    {
        public GameObject cells, far;
        public Vector3 center;
        public float radius = 362f;
        internal int state = -1;

        internal static readonly List<BlockLod> All = new List<BlockLod>();
        void OnEnable() { All.Add(this); state = -1; CityLodManager.Dirty = true; }
        void OnDisable() => All.Remove(this);

        internal void Apply(Vector3 cam, float farDist)
        {
            Vector3 d = center - cam; d.y = 0f;
            int s = d.magnitude - radius < farDist ? 0 : 1;
            if (s == state) return;
            state = s;
            if (cells != null) cells.SetActive(s == 0);
            if (far != null) far.SetActive(s == 1);
        }
    }

    /// <summary>Checks the camera distance of every block and cell five times a second.</summary>
    public sealed class CityLodManager : MonoBehaviour
    {
        public const float NearDistance = 170f, FarDistance = 420f;
        internal static bool Dirty;
        float _next;

        void Update()
        {
            if (!Dirty && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.2f;
            Dirty = false;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 c = cam.transform.position;
            float s = CellLod.DistanceScale;
            for (int i = 0; i < BlockLod.All.Count; i++) BlockLod.All[i].Apply(c, FarDistance * s);
            for (int i = 0; i < CellLod.All.Count; i++) CellLod.All[i].Apply(c, NearDistance * s);
        }
    }
}
