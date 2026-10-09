#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Draws, on each bone, the value a chosen distribution curve actually resolves to there.
    //
    // This is the one part of PhysBone tuning that is genuinely invisible. Pull, Spring and the
    // rest each have a paired AnimationCurve that distributes the value along the chain, and the
    // curve is sampled at the bone's *normalized depth along its own strand* - 0 at the root, 1
    // at the tip. The inspector shows you a 70-pixel curve thumbnail and leaves you to imagine
    // how it maps onto 14 hair bones. A curve with a dip in the middle is indistinguishable from
    // a flat one until something behaves oddly three bones down.
    //
    // So: a marker per bone, sized by the resolved value, plus the number itself. On a branching
    // chain (a hand, multi-strand hair) each strand is sampled separately, which is also what
    // makes the branching behaviour legible - the same bone depth on a short strand and a long
    // one resolve to different values, and here you can see that.
    internal static class PhysBoneCurveMarkers
    {
        // Which curve to visualise. The underlying fields are value + "<name>Curve" pairs, so
        // one enum covers both halves of each.
        internal enum Curve
        {
            None,
            Pull,
            Spring,
            Stiffness,
            Gravity,
            GravityFalloff,
            Immobile,
            Radius,
            MaxAngleX,
            MaxStretch,
        }

        internal static Curve Current = Curve.None;

        // Whether to print the resolved number next to each marker. Useful while tuning, noise
        // once you know the shape, so it is separate from the markers themselves.
        internal static bool ShowValues = true;

        internal static readonly string[] Labels =
        {
            "None", "Pull", "Spring", "Stiffness", "Gravity", "Grav Falloff",
            "Immobile", "Radius", "Max Angle X", "Max Stretch",
        };

        private static GUIStyle _valueStyle;

        internal static string Tooltip(Curve curve)
        {
            switch (curve)
            {
                case Curve.Pull: return "How strongly each bone is pulled back towards its rest pose.";
                case Curve.Spring: return "How much each bone overshoots and oscillates on the way back.";
                case Curve.Stiffness: return "Resistance to being bent away from the rest pose.";
                case Curve.Gravity: return "Downward force per bone, in units of the bone's own length.";
                case Curve.GravityFalloff: return "Blends gravity from world-down towards the rest pose direction.";
                case Curve.Immobile: return "How much each bone ignores the avatar's own movement.";
                case Curve.Radius: return "Collision thickness per bone.";
                case Curve.MaxAngleX: return "Allowed swing angle per bone, in degrees.";
                case Curve.MaxStretch: return "How far each bone may stretch past its rest length.";
                default: return "Pick a force to see the value it resolves to on every bone.";
            }
        }

        // The base value and its distribution curve, read off the live component.
        private static bool TryResolve(VRCPhysBone pb, Curve curve, out float baseValue,
            out AnimationCurve distribution, out string suffix)
        {
            suffix = string.Empty;
            switch (curve)
            {
                case Curve.Pull:
                    baseValue = pb.pull; distribution = pb.pullCurve; return true;
                case Curve.Spring:
                    baseValue = pb.spring; distribution = pb.springCurve; return true;
                case Curve.Stiffness:
                    baseValue = pb.stiffness; distribution = pb.stiffnessCurve; return true;
                case Curve.Gravity:
                    baseValue = pb.gravity; distribution = pb.gravityCurve; return true;
                case Curve.GravityFalloff:
                    baseValue = pb.gravityFalloff; distribution = pb.gravityFalloffCurve; return true;
                case Curve.Immobile:
                    baseValue = pb.immobile; distribution = pb.immobileCurve; return true;
                case Curve.Radius:
                    baseValue = pb.radius; distribution = pb.radiusCurve; suffix = "m"; return true;
                case Curve.MaxAngleX:
                    baseValue = pb.maxAngleX; distribution = pb.maxAngleXCurve; suffix = "°"; return true;
                case Curve.MaxStretch:
                    baseValue = pb.maxStretch; distribution = pb.maxStretchCurve; return true;
                default:
                    baseValue = 0f; distribution = null; return false;
            }
        }

        internal static void Draw(IReadOnlyList<VRCPhysBone> bones)
        {
            if (Current == Curve.None || bones == null)
                return;

            if (_valueStyle == null)
            {
                _valueStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(1f, 0.95f, 0.6f) },
                };
            }

            Color prev = Handles.color;

            for (int i = 0; i < bones.Count; i++)
            {
                VRCPhysBone pb = bones[i];
                if (pb == null) continue;
                if (!TryResolve(pb, Current, out float baseValue, out AnimationCurve dist, out string suffix))
                    continue;

                List<List<Transform>> chains = PhysBoneChainUtil.BuildChains(pb);
                for (int c = 0; c < chains.Count; c++)
                    DrawStrand(pb, chains[c], baseValue, dist, suffix);
            }

            Handles.color = prev;
        }

        private static void DrawStrand(VRCPhysBone pb, List<Transform> strand, float baseValue,
            AnimationCurve dist, string suffix)
        {
            // The endpoint counts as one more sampled position when it exists: the runtime
            // simulates it as a segment, so leaving it out would understate the chain.
            bool hasEndpoint = PhysBoneChainUtil.HasEndpoint(pb);
            int count = strand.Count + (hasEndpoint ? 1 : 0);

            for (int b = 0; b < count; b++)
            {
                bool isEndpoint = hasEndpoint && b == count - 1;
                Transform t = strand[isEndpoint ? strand.Count - 1 : b];
                if (t == null) continue;

                Vector3 at = isEndpoint
                    ? PhysBoneChainUtil.EndpointWorldPosition(pb, t)
                    : t.position;

                float time = PhysBoneChainUtil.NormalizedTime(b, count);
                float resolved = baseValue * PhysBoneChainUtil.EvaluateCurveOrDefault(dist, time);

                // Size carries the shape, so a dip in the middle of a curve reads at a glance
                // even with the numbers turned off. Scaled against the handle size so it stays
                // the same on-screen size as you zoom.
                float unit = HandleUtility.GetHandleSize(at);
                float magnitude = Mathf.Abs(baseValue) > 1e-5f
                    ? Mathf.Clamp01(Mathf.Abs(resolved) / Mathf.Max(Mathf.Abs(baseValue), 1e-5f))
                    : 0f;

                Handles.color = Color.Lerp(
                    new Color(0.25f, 0.4f, 0.55f, 0.55f),
                    new Color(1f, 0.85f, 0.35f, 0.95f),
                    magnitude);
                Handles.SphereHandleCap(0, at, Quaternion.identity,
                    unit * (0.035f + magnitude * 0.075f), EventType.Repaint);

                if (ShowValues)
                {
                    Handles.Label(at + Vector3.up * unit * 0.1f,
                        resolved.ToString("0.###") + suffix, _valueStyle);
                }
            }
        }
    }
}
#endif
