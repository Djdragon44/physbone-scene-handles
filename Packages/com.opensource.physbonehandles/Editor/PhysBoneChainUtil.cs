#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Walks the bone tree under a VRCPhysBone's root the way the runtime does, and flattens it
    // into a list of root-to-leaf paths ("chains").
    //
    // Why paths and not one list: a VRCPhysBone on a Hand bone covers all five fingers at once,
    // and hair PhysBones routinely have several strands off one root. The earlier single-chain
    // walk here stopped dead at the first branch, so on a hand it only ever drew one finger.
    // The runtime instead treats every root-to-leaf path as its own strand, and evaluates
    // curves (radiusCurve, and the rest) against normalized depth *along that strand* - so the
    // tip of a short finger and the tip of a long one both sit at t = 1.
    internal static class PhysBoneChainUtil
    {
        private const int MaxChainDepth = 64;
        private const int MaxPaths = 64;

        internal static Transform RootOf(VRCPhysBone pb)
            => pb.rootTransform != null ? pb.rootTransform : pb.transform;

        // A uniform scale factor for converting the component's local-space radius/height
        // numbers into world space. lossyScale can be non-uniform, in which case no single
        // number is right; the runtime effectively treats these as uniform too, so take the
        // largest axis (over-estimating a collider is the safer error - the wireframe then
        // bounds what the runtime actually uses rather than sitting inside it).
        internal static float UniformScale(Transform t)
        {
            Vector3 s = t.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        internal static bool HasNonUniformScale(Transform t)
        {
            Vector3 s = t.lossyScale;
            float max = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
            float min = Mathf.Min(Mathf.Abs(s.x), Mathf.Min(Mathf.Abs(s.y), Mathf.Abs(s.z)));
            return max > 0.0001f && (max - min) / max > 0.01f;
        }

        private static bool IsIgnored(VRCPhysBone pb, Transform t)
        {
            if (pb.ignoreTransforms == null)
                return false;
            for (int i = 0; i < pb.ignoreTransforms.Count; i++)
            {
                if (pb.ignoreTransforms[i] == t)
                    return true;
            }
            return false;
        }

        private static List<Transform> AffectedChildren(VRCPhysBone pb, Transform t)
        {
            List<Transform> children = new List<Transform>();
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (!IsIgnored(pb, child))
                    children.Add(child);
            }
            return children;
        }

        // Every root-to-leaf path through the affected bone tree, each starting at the root.
        // A bare root with no children still yields one single-element path, so callers can
        // treat the result as "always at least one chain" without a special case.
        internal static List<List<Transform>> BuildChains(VRCPhysBone pb)
        {
            Transform root = RootOf(pb);
            List<List<Transform>> paths = new List<List<Transform>>();
            Walk(pb, root, new List<Transform> { root }, paths);
            if (paths.Count == 0)
                paths.Add(new List<Transform> { root });
            return paths;
        }

        private static void Walk(VRCPhysBone pb, Transform current, List<Transform> sofar, List<List<Transform>> paths)
        {
            if (paths.Count >= MaxPaths)
                return;

            List<Transform> children = sofar.Count >= MaxChainDepth
                ? new List<Transform>()
                : AffectedChildren(pb, current);

            if (children.Count == 0)
            {
                paths.Add(new List<Transform>(sofar));
                return;
            }

            foreach (Transform child in children)
            {
                sofar.Add(child);
                Walk(pb, child, sofar, paths);
                sofar.RemoveAt(sofar.Count - 1);
            }
        }

        // Normalized position of index `i` along a chain, matching how the runtime samples
        // curves: 0 at the root, 1 at the tip.
        internal static float NormalizedTime(int i, int chainCount)
            => chainCount <= 1 ? 0f : (float)i / (chainCount - 1);

        internal static float EvaluateCurveOrDefault(AnimationCurve curve, float t)
            => curve != null && curve.length > 0 ? curve.Evaluate(t) : 1f;

        // World-space position of the virtual endpoint hanging off a chain's last bone.
        // `endpointPosition` is a local-space offset applied in the tip bone's own space, and
        // it's what gives a chain one extra simulated segment past the final real bone.
        internal static Vector3 EndpointWorldPosition(VRCPhysBone pb, Transform tip)
            => tip.TransformPoint(pb.endpointPosition);

        internal static bool HasEndpoint(VRCPhysBone pb)
            => pb.endpointPosition.sqrMagnitude > 1e-10f;
    }
}
#endif
