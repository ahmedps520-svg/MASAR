using System.Collections.Generic;
using UnityEngine;

namespace Masar.World
{
    /// <summary>
    /// One 512 m block of cells. Beyond <see cref="CityLodManager.FarDistance"/> all its cells are
    /// switched off and one merged far mesh per material is drawn instead (few draw calls for the
    /// skyline). In the city the cells live in their own scene (<see cref="section"/>), loaded in the
    /// background as the camera comes near and unloaded when it is far again: the whole city's
    /// detail does not fit in memory (5.5 GB, 2026-11-23). The far mesh stays up until the cells are in.
    /// </summary>
    public sealed class BlockLod : MonoBehaviour
    {
        public GameObject cells, far;
        [Tooltip("Additive scene with this block's cells (streamed); empty when the cells are in this scene")]
        public string section;
        public Vector3 center;
        public float radius = 362f;
        internal int state = -1;
        internal float distance;
        internal bool loading, unloading;

        internal static readonly List<BlockLod> All = new List<BlockLod>();
        void OnEnable() { All.Add(this); state = -1; CityLodManager.Dirty = true; }
        void OnDisable() => All.Remove(this);

        internal bool Streamed => !string.IsNullOrEmpty(section);
        internal bool Loaded => cells != null;

        internal void Apply(Vector3 cam, float farDist)
        {
            Vector3 d = center - cam; d.y = 0f;
            distance = d.magnitude - radius;
            int s = distance < farDist && Loaded ? 0 : 1;
            if (s == state) return;
            state = s;
            if (cells != null) cells.SetActive(s == 0);
            if (far != null) far.SetActive(s == 1);
        }
    }
}
