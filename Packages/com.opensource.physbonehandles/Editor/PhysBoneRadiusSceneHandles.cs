#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Per-bone-node grab handles along a VRCPhysBone chain that shape its `radiusCurve` -
    // the per-position multiplier on the base `radius` field. Lets you e.g. grab the middle
    // of a tail and pull it outward to make it bulge, or pull the tip in to taper it, without
    // hand-editing curve keys in the inspector.
    //
    // The base `radius` field itself is left to the normal inspector - these handles only
    // ever affect the curve (a multiplier), so dragging always changes *shape*, never the
    // component's overall thickness.
    //
    // Shares the "Radius" toggle and the Alt ("only this chain")/Shift ("equalize this point
    // across the whole selection") batch behavior with PhysBoneColliderSceneHandles.
    [InitializeOnLoad]
    public static class PhysBoneRadiusSceneHandles
    {
        private const int MaxChainDepth = 64;
        private const float KeyTimeMergeThreshold = 0.02f;

        static PhysBoneRadiusSceneHandles()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!PhysBoneColliderSceneHandles.EditRadius)
                return;

            List<VRCPhysBone> physBones = Selection.gameObjects
                .Select(go => go.GetComponent<VRCPhysBone>())
                .Where(pb => pb != null)
                .ToList();
            if (physBones.Count == 0)
                return;

            VRCPhysBone active = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<VRCPhysBone>()
                : null;

            foreach (VRCPhysBone pb in physBones)
                DrawChain(pb, physBones, pb == active);
        }

        private static Transform RootOf(VRCPhysBone pb) => pb.rootTransform != null ? pb.rootTransform : pb.transform;

        private static float EvaluateCurveOrDefault(AnimationCurve curve, float t)
            => curve != null && curve.length > 0 ? curve.Evaluate(t) : 1f;

        private static void DrawChain(VRCPhysBone pb, List<VRCPhysBone> allSelected, bool isActive)
        {
            Transform root = RootOf(pb);
            float scale = root.lossyScale.x;
            List<Transform> chain = BuildChain(pb, root);

            Color wireColor = isActive ? new Color(1f, 0.65f, 0.2f) : new Color(1f, 0.65f, 0.2f, 0.5f);
            using (new Handles.DrawingScope(wireColor))
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    float t = chain.Count <= 1 ? 0f : (float)i / (chain.Count - 1);
                    float worldRadius = pb.radius * EvaluateCurveOrDefault(pb.radiusCurve, t) * scale;
                    if (worldRadius <= 0f)
                        continue;
                    Vector3 pos = chain[i].position;
                    Handles.DrawWireDisc(pos, Vector3.up, worldRadius);
                    Handles.DrawWireDisc(pos, Vector3.right, worldRadius);
                    Handles.DrawWireDisc(pos, Vector3.forward, worldRadius);
                }
            }

            if (chain.Count < 2)
                return; // nothing to taper along

            for (int i = 0; i < chain.Count; i++)
                DrawNodeHandle(pb, allSelected, chain, i, root, scale);
        }

        private static void DrawNodeHandle(VRCPhysBone pb, List<VRCPhysBone> allSelected, List<Transform> chain, int i, Transform root, float scale)
        {
            float t = (float)i / (chain.Count - 1);
            Vector3 nodePos = chain[i].position;

            Vector3 tangent = i < chain.Count - 1
                ? chain[i + 1].position - nodePos
                : nodePos - chain[i - 1].position;
            tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector3.forward;
            Vector3 outward = Vector3.Cross(tangent, Vector3.up);
            if (outward.sqrMagnitude < 1e-6f)
                outward = Vector3.Cross(tangent, Vector3.right);
            outward.Normalize();

            float curWorldRadius = pb.radius * EvaluateCurveOrDefault(pb.radiusCurve, t) * scale;
            float handleSize = HandleUtility.GetHandleSize(nodePos) * 0.08f;
            Vector3 handlePos = nodePos + outward * Mathf.Max(curWorldRadius, handleSize * 0.5f);

            EditorGUI.BeginChangeCheck();
            Vector3 newHandlePos = Handles.Slider(handlePos, outward, handleSize, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float newWorldRadius = Mathf.Max(0f, Vector3.Dot(newHandlePos - nodePos, outward));
                float newLocalRadius = newWorldRadius / Mathf.Max(scale, 0.0001f);
                float baseRadius = Mathf.Max(pb.radius, 0.0001f);
                float newCurveMul = newLocalRadius / baseRadius;

                SceneHandleBatchUtil.ApplyScalarDelta(pb, allSelected,
                    p => EvaluateCurveOrDefault(p.radiusCurve, t),
                    (p, v) => SetCurveValueAtTime(GetOrCreateCurve(p), t, Mathf.Max(0f, v)),
                    newCurveMul, "Change PhysBone Radius Curve");
            }
        }

        private static AnimationCurve GetOrCreateCurve(VRCPhysBone pb)
        {
            if (pb.radiusCurve == null)
                pb.radiusCurve = new AnimationCurve();
            return pb.radiusCurve;
        }

        // Adds or moves a key at normalized time `t` to `value`. If the curve was empty,
        // seeds flat (0,1)/(1,1) boundary keys first so only the dragged point's neighborhood
        // changes shape instead of the whole chain jumping to one value.
        private static void SetCurveValueAtTime(AnimationCurve curve, float t, float value)
        {
            if (curve.length == 0)
            {
                curve.AddKey(new Keyframe(0f, 1f));
                curve.AddKey(new Keyframe(1f, 1f));
            }

            int existingIndex = -1;
            for (int i = 0; i < curve.length; i++)
            {
                if (Mathf.Abs(curve[i].time - t) < KeyTimeMergeThreshold)
                {
                    existingIndex = i;
                    break;
                }
            }

            int index = existingIndex >= 0
                ? curve.MoveKey(existingIndex, new Keyframe(curve[existingIndex].time, value))
                : curve.AddKey(new Keyframe(t, value));

            if (index >= 0)
                curve.SmoothTangents(index, 0f);
        }

        // Walks the actual bone chain the way VRCPhysBone will: single-child transforms only,
        // stopping at leaves or branches, respecting ignoreTransforms.
        private static List<Transform> BuildChain(VRCPhysBone pb, Transform root)
        {
            List<Transform> chain = new List<Transform> { root };
            Transform current = root;
            for (int depth = 0; depth < MaxChainDepth; depth++)
            {
                List<Transform> children = new List<Transform>();
                for (int i = 0; i < current.childCount; i++)
                {
                    Transform child = current.GetChild(i);
                    if (pb.ignoreTransforms != null && pb.ignoreTransforms.Contains(child))
                        continue;
                    children.Add(child);
                }

                if (children.Count != 1)
                    break;

                current = children[0];
                chain.Add(current);
            }
            return chain;
        }
    }
}
#endif
