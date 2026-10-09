#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEngine;

namespace OpenSource.PhysBoneHandles
{
    // Moves the avatar while the preview runs, because a PhysBone at rest tells you almost
    // nothing. Pull and Spring only show their character when the root is in motion, and the
    // failure everyone actually cares about - hair clipping through a shoulder on a turn - only
    // happens when the avatar turns.
    //
    // Each motion drives the avatar root's local position/rotation directly. It is the same
    // transform the rest-pose snapshot covers, so stopping the preview undoes all of this.
    //
    // All offsets are applied relative to the pose captured when the motion was armed, not
    // accumulated frame to frame. Accumulating would drift: a sine wave built from per-frame
    // deltas slowly walks away from its start, and the bone would be reacting to a slow
    // translation that the user never asked for on top of the sway.
    internal static class PhysBoneTestMotion
    {
        internal enum Kind
        {
            None,
            Sway,       // steady side-to-side rotation - the everyday case
            Gust,       // one sharp impulse then stillness, for reading settle time
            Circle,     // continuous yaw, the turn that makes hair clip a shoulder
            Drop,       // vertical fall and stop, for reading stretch and bounce
            Walk,       // forward translation with a small bob
        }

        private struct Target
        {
            public Transform transform;
            public Vector3 basePosition;
            public Quaternion baseRotation;
        }

        private static readonly List<Target> _targets = new List<Target>();

        internal static Kind Current = Kind.None;

        // Amplitude multiplier. 1 is a readable default for a human-scale avatar; the slider
        // exists because a 0.2 m prop and a 2 m avatar need very different magnitudes.
        internal static float Strength = 1f;

        // Cycles per second for the periodic motions.
        internal static float Speed = 0.5f;

        private static float _time;
        private static bool _armed;

        internal static void Arm(IEnumerable<Transform> avatarRoots)
        {
            _targets.Clear();
            foreach (Transform t in avatarRoots)
            {
                if (t == null) continue;
                _targets.Add(new Target
                {
                    transform = t,
                    basePosition = t.localPosition,
                    baseRotation = t.localRotation,
                });
            }
            _time = 0f;
            _armed = _targets.Count > 0;
        }

        internal static void Disarm()
        {
            RestoreBase();
            _targets.Clear();
            _armed = false;
            _time = 0f;
        }

        internal static void Reset()
        {
            _time = 0f;
            RestoreBase();
        }

        private static void RestoreBase()
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                Target t = _targets[i];
                if (t.transform == null) continue;
                t.transform.localPosition = t.basePosition;
                t.transform.localRotation = t.baseRotation;
            }
        }

        // Called once per solver step, before the solve, so the bones react to the new root
        // position in the same step rather than one behind it.
        internal static void Apply(float deltaTime)
        {
            if (!_armed || Current == Kind.None)
                return;

            _time += deltaTime;

            for (int i = 0; i < _targets.Count; i++)
            {
                Target t = _targets[i];
                if (t.transform == null) continue;

                Vector3 posOffset = Vector3.zero;
                Quaternion rotOffset = Quaternion.identity;
                Evaluate(_time, out posOffset, out rotOffset);

                t.transform.localPosition = t.basePosition + posOffset;
                t.transform.localRotation = t.baseRotation * rotOffset;
            }
        }

        private static void Evaluate(float time, out Vector3 posOffset, out Quaternion rotOffset)
        {
            posOffset = Vector3.zero;
            rotOffset = Quaternion.identity;

            float phase = time * Speed * Mathf.PI * 2f;

            switch (Current)
            {
                case Kind.Sway:
                    // Rotation, not translation: a real avatar swaying is pivoting at the hips,
                    // and the bone's immobile setting responds differently to the two.
                    rotOffset = Quaternion.Euler(0f, 0f, Mathf.Sin(phase) * 20f * Strength);
                    break;

                case Kind.Gust:
                {
                    // A single decaying impulse rather than a repeating one. Settle time is the
                    // thing being read, and that needs a stretch of stillness after the kick.
                    float period = 3f;
                    float t = Mathf.Repeat(time, period);
                    float impulse = t < 0.15f ? Mathf.Sin(t / 0.15f * Mathf.PI) : 0f;
                    posOffset = new Vector3(impulse * 0.35f * Strength, 0f, 0f);
                    rotOffset = Quaternion.Euler(0f, 0f, impulse * 25f * Strength);
                    break;
                }

                case Kind.Circle:
                    rotOffset = Quaternion.Euler(0f, time * Speed * 360f, 0f);
                    break;

                case Kind.Drop:
                {
                    // Fall, land, hold. The hold is deliberate - the bounce after the landing
                    // is what tells you whether maxStretch is set sanely.
                    float period = 2f;
                    float t = Mathf.Repeat(time, period);
                    float height = t < 0.5f
                        ? Mathf.Lerp(0.5f, 0f, t / 0.5f)
                        : 0f;
                    posOffset = new Vector3(0f, height * Strength, 0f);
                    break;
                }

                case Kind.Walk:
                {
                    // Forward travel plus the vertical bob of a stride. Two cycles of bob per
                    // stride, which is what a walk actually does (one per footfall).
                    posOffset = new Vector3(
                        0f,
                        Mathf.Abs(Mathf.Sin(phase)) * 0.04f * Strength,
                        Mathf.Sin(phase * 0.5f) * 1.5f * Strength);
                    break;
                }
            }
        }

        internal static string Describe(Kind kind)
        {
            switch (kind)
            {
                case Kind.None: return "Still";
                case Kind.Sway: return "Sway";
                case Kind.Gust: return "Gust";
                case Kind.Circle: return "Turn";
                case Kind.Drop: return "Drop";
                case Kind.Walk: return "Walk";
                default: return kind.ToString();
            }
        }

        internal static string Tooltip(Kind kind)
        {
            switch (kind)
            {
                case Kind.None: return "No avatar motion. The bones only react to what you change.";
                case Kind.Sway: return "Steady side-to-side lean. The everyday case for checking Pull and Spring.";
                case Kind.Gust: return "One sharp push, then stillness - read how long the bone takes to settle.";
                case Kind.Circle: return "Continuous turn. This is the motion that makes hair clip into a shoulder.";
                case Kind.Drop: return "Fall and land. Shows stretch and the bounce after the landing.";
                case Kind.Walk: return "Walk forward with a stride bob.";
                default: return string.Empty;
            }
        }
    }
}
#endif
