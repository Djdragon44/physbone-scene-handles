#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Draws the path each chain's tip has travelled over the last couple of seconds.
    //
    // A live bone is hard to read while it is moving: the eye can follow where the tip is but
    // not the shape of its path, and the shape is the useful part. A trail turns "that looks
    // about right" into something you can actually judge - a tight arc means the bone is
    // over-damped, a wide loop that keeps growing means Pull is too low, and a kink in the path
    // is where the chain hit a collider.
    //
    // Fixed-length ring buffer per component, sampled once per solver step. Old points fade out
    // rather than vanishing, so the direction of travel is readable from a still screenshot.
    internal static class PhysBonePreviewTrail
    {
        // Two seconds at 60 Hz. Long enough to show a full sway cycle, short enough that the
        // trail doesn't become a solid smear during a continuous turn.
        private const int Capacity = 120;

        private sealed class Trail
        {
            public readonly Vector3[] points = new Vector3[Capacity];
            public int count;
            public int head;
        }

        private static readonly Dictionary<VRCPhysBone, Trail> _trails = new Dictionary<VRCPhysBone, Trail>();

        internal static bool Enabled;

        internal static void Clear()
        {
            _trails.Clear();
        }

        // Called once per solver step, after the solve, so the sampled point is the solved one.
        internal static void Sample(IReadOnlyList<VRCPhysBone> bones)
        {
            if (!Enabled)
                return;

            for (int i = 0; i < bones.Count; i++)
            {
                VRCPhysBone pb = bones[i];
                if (pb == null)
                    continue;
                if (!PhysBonePreviewReadback.TryReadTip(pb, out Vector3 tip))
                    continue;

                if (!_trails.TryGetValue(pb, out Trail trail))
                {
                    trail = new Trail();
                    _trails[pb] = trail;
                }

                trail.points[trail.head] = tip;
                trail.head = (trail.head + 1) % Capacity;
                if (trail.count < Capacity)
                    trail.count++;
            }
        }

        internal static void Draw()
        {
            if (!Enabled)
                return;

            Color prev = Handles.color;

            foreach (KeyValuePair<VRCPhysBone, Trail> kv in _trails)
            {
                if (kv.Key == null)
                    continue;

                Trail trail = kv.Value;
                if (trail.count < 2)
                    continue;

                // Oldest first, so the alpha ramp runs from faint to solid along the direction
                // the tip actually moved.
                int start = trail.count == Capacity ? trail.head : 0;
                Vector3 previous = trail.points[start % Capacity];

                for (int step = 1; step < trail.count; step++)
                {
                    Vector3 point = trail.points[(start + step) % Capacity];
                    float age = step / (float)trail.count;
                    Handles.color = new Color(0.35f, 0.85f, 1f, 0.08f + age * 0.62f);
                    Handles.DrawLine(previous, point);
                    previous = point;
                }
            }

            Handles.color = prev;
        }
    }
}
#endif
