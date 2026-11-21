using System;
using UnityEngine;

namespace Masar.World.Traffic
{
    /// <summary>
    /// The drivable lane graph and the pedestrian walkways of a generated city map, baked in the
    /// editor from OSM (OsmCityBuilder) and read by <see cref="TrafficSystem"/> and
    /// <see cref="PedestrianSystem"/>. Lanes are polylines in world meters. Junction connectors are
    /// lanes too (junction &gt;= 0), with the connectors they cross listed as conflicts.
    /// </summary>
    public sealed class CityGraph : ScriptableObject
    {
        [Serializable]
        public struct Lane
        {
            public int pointStart, pointCount;
            public float length;
            public float speed;          // m/s (speed limit, or the comfortable curve speed on connectors)
            public int junction;         // -1 = road lane, else index into junctions
            public int signalGroup;      // connectors at signalized junctions: 0 / 1
            public int nextStart, nextCount;
            public int conflictStart, conflictCount;
            public byte roadClass;       // priority at unsignalized junctions
            public byte turn;            // 0 straight, 1 right, 2 left (connectors)
        }

        [Serializable]
        public struct Junction
        {
            public Vector3 position;
            public bool signal;
            public float cycleOffset;    // seconds, staggers neighbouring signals
            public float greenTime;
        }

        [Serializable]
        public struct Walk
        {
            public int pointStart, pointCount;
            public float length;
        }

        public Vector3[] points = new Vector3[0];
        public Lane[] lanes = new Lane[0];
        public int[] next = new int[0];
        public int[] conflicts = new int[0];
        public Junction[] junctions = new Junction[0];
        public Vector3[] walkPoints = new Vector3[0];
        public Walk[] walks = new Walk[0];
        public Vector3 spawnPosition;
        public float spawnYaw;
        public string attribution = "Map data (c) OpenStreetMap contributors, ODbL";

        /// <summary>Position and unit tangent at arc length s along lane i.</summary>
        public void Sample(int lane, float s, out Vector3 pos, out Vector3 tangent)
        {
            var l = lanes[lane];
            s = Mathf.Clamp(s, 0f, l.length);
            float acc = 0f;
            int end = l.pointStart + l.pointCount - 1;
            for (int k = l.pointStart; k < end; k++)
            {
                Vector3 a = points[k], b = points[k + 1];
                float seg = Vector3.Distance(a, b);
                if (acc + seg >= s || k == end - 1)
                {
                    float t = seg > 1e-4f ? Mathf.Clamp01((s - acc) / seg) : 0f;
                    pos = Vector3.LerpUnclamped(a, b, t);
                    tangent = seg > 1e-4f ? (b - a) / seg : Vector3.forward;
                    return;
                }
                acc += seg;
            }
            pos = points[l.pointStart];
            tangent = Vector3.forward;
        }

        public void SampleWalk(int walk, float s, out Vector3 pos, out Vector3 tangent)
        {
            var w = walks[walk];
            s = Mathf.Clamp(s, 0f, w.length);
            float acc = 0f;
            int end = w.pointStart + w.pointCount - 1;
            for (int k = w.pointStart; k < end; k++)
            {
                Vector3 a = walkPoints[k], b = walkPoints[k + 1];
                float seg = Vector3.Distance(a, b);
                if (acc + seg >= s || k == end - 1)
                {
                    float t = seg > 1e-4f ? Mathf.Clamp01((s - acc) / seg) : 0f;
                    pos = Vector3.LerpUnclamped(a, b, t);
                    tangent = seg > 1e-4f ? (b - a) / seg : Vector3.forward;
                    return;
                }
                acc += seg;
            }
            pos = walkPoints[w.pointStart];
            tangent = Vector3.forward;
        }
    }

    /// <summary>
    /// Deterministic two-phase signal timing shared by the signal heads (props) and the traffic, so
    /// cars obey exactly the lights the player sees. Cycle: group 0 green, amber 3 s, all-red 2 s,
    /// group 1 green, amber 3 s, all-red 2 s.
    /// </summary>
    public static class SignalClock
    {
        public const float Amber = 3f, AllRed = 2f;
        public enum State { Green, Amber, Red }

        public static State Get(float greenTime, float offset, int group, float time)
        {
            float half = greenTime + Amber + AllRed;
            float t = Mathf.Repeat(time + offset, half * 2f);
            if (group == 1) t = Mathf.Repeat(t - half, half * 2f);
            if (t < greenTime) return State.Green;
            if (t < greenTime + Amber) return State.Amber;
            return State.Red;
        }
    }
}
