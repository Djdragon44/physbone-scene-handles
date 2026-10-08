#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Scene-view handles for VRCPhysBone itself (as opposed to its colliders):
    //
    //  * Radius  - one grab handle per bone node, shaping `radiusCurve` (the per-position
    //              multiplier on the base `radius`). Grab the middle of a tail and pull it
    //              out to bulge it, or pull the tip in to taper it, without hand-editing
    //              curve keys. The base `radius` field is left to the inspector, so dragging
    //              always changes *shape*, never overall thickness.
    //  * Position - moves the chain's virtual endpoint (`endpointPosition`), the extra
    //              simulated segment past the final real bone. There's no way to see where
    //              that lands from the inspector's three raw numbers, so it gets a handle.
    //
    // Chains are walked as every root-to-leaf path (see PhysBoneChainUtil), so a PhysBone on
    // a Hand bone draws all five fingers, not just whichever one happens to be child 0.
    //
    // Shares the Radius/Position toggles and the Alt ("only this chain") / Shift ("equalize
    // across the whole selection") batch behavior with PhysBoneColliderSceneHandles.
    [InitializeOnLoad]
    public static class PhysBoneRadiusSceneHandles
    {
        private const float KeyTimeMergeThreshold = 0.02f;

        static PhysBoneRadiusSceneHandles()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            List<VRCPhysBone> physBones = Selection.gameObjects
                .Select(go => go.GetComponent<VRCPhysBone>())
                .Where(pb => pb != null)
                .ToList();
            if (physBones.Count == 0)
                return;

            // The toggle panel lives in PhysBoneColliderSceneHandles, which only draws it when
            // a collider is selected. Draw it here for a PhysBone-only selection, so the
            // Radius/Position/Limits toggles are reachable either way - but don't draw it twice
            // if the selection happens to contain both.
            bool anyCollider = Selection.gameObjects.Any(go => go.GetComponent<VRCPhysBoneCollider>() != null);
            if (!anyCollider)
                PhysBoneColliderSceneHandles.DrawTogglePanel(sceneView);

            if (!PhysBoneColliderSceneHandles.EditRadius && !PhysBoneColliderSceneHandles.EditPosition)
                return;

            VRCPhysBone active = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<VRCPhysBone>()
                : null;

            foreach (VRCPhysBone pb in physBones)
                DrawPhysBone(pb, physBones, pb == active);
        }

        private static void DrawPhysBone(VRCPhysBone pb, List<VRCPhysBone> allSelected, bool isActive)
        {
            Transform root = PhysBoneChainUtil.RootOf(pb);
            float scale = PhysBoneChainUtil.UniformScale(root);
            List<List<Transform>> chains = PhysBoneChainUtil.BuildChains(pb);

            Color wireColor = isActive ? new Color(1f, 0.65f, 0.2f) : new Color(1f, 0.65f, 0.2f, 0.5f);
            using (new Handles.DrawingScope(wireColor))
            {
                foreach (List<Transform> chain in chains)
                    DrawChainWire(pb, chain, scale);
            }

            if (PhysBoneColliderSceneHandles.EditRadius)
            {
                // A bone shared by several paths (the hand itself, on a five-finger PhysBone)
                // would otherwise get one handle per path stacked in the same spot, and each
                // would fight the others for the same curve key. Draw each distinct node once,
                // at the deepest normalized time any path gives it.
                Dictionary<Transform, NodeInfo> nodes = new Dictionary<Transform, NodeInfo>();
                foreach (List<Transform> chain in chains)
                {
                    for (int i = 0; i < chain.Count; i++)
                    {
                        float t = PhysBoneChainUtil.NormalizedTime(i, chain.Count);
                        Transform node = chain[i];
                        Vector3 tangent = ChainTangent(chain, i);
                        if (!nodes.TryGetValue(node, out NodeInfo existing) || t > existing.Time)
                            nodes[node] = new NodeInfo { Time = t, Tangent = tangent };
                    }
                }

                foreach (KeyValuePair<Transform, NodeInfo> entry in nodes)
                    DrawNodeHandle(pb, allSelected, entry.Key, entry.Value, scale);
            }

            if (PhysBoneColliderSceneHandles.EditPosition && chains.Count > 0)
                HandleEndpoint(pb, allSelected, chains);
        }

        private struct NodeInfo
        {
            public float Time;
            public Vector3 Tangent;
        }

        private static Vector3 ChainTangent(List<Transform> chain, int i)
        {
            Vector3 tangent = i < chain.Count - 1
                ? chain[i + 1].position - chain[i].position
                : (i > 0 ? chain[i].position - chain[i - 1].position : Vector3.forward);
            return tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector3.forward;
        }

        private static void DrawChainWire(VRCPhysBone pb, List<Transform> chain, float scale)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                float t = PhysBoneChainUtil.NormalizedTime(i, chain.Count);
                float worldRadius = pb.radius * PhysBoneChainUtil.EvaluateCurveOrDefault(pb.radiusCurve, t) * scale;
                Vector3 pos = chain[i].position;

                if (worldRadius > 0f)
                {
                    Handles.DrawWireDisc(pos, Vector3.up, worldRadius);
                    Handles.DrawWireDisc(pos, Vector3.right, worldRadius);
                    Handles.DrawWireDisc(pos, Vector3.forward, worldRadius);
                }

                // Spine line, so a branching chain reads as connected strands rather than a
                // scatter of loose discs.
                if (i > 0)
                    Handles.DrawLine(chain[i - 1].position, pos);
            }

            if (PhysBoneChainUtil.HasEndpoint(pb) && chain.Count > 0)
            {
                Transform tip = chain[chain.Count - 1];
                Vector3 endpoint = PhysBoneChainUtil.EndpointWorldPosition(pb, tip);
                Handles.DrawDottedLine(tip.position, endpoint, 3f);
                float endRadius = pb.radius * PhysBoneChainUtil.EvaluateCurveOrDefault(pb.radiusCurve, 1f) * scale;
                if (endRadius > 0f)
                    SceneHandleBatchUtil.DrawSphereWire(endpoint, Quaternion.identity, endRadius);
            }
        }

        private static void DrawNodeHandle(VRCPhysBone pb, List<VRCPhysBone> allSelected,
            Transform node, NodeInfo info, float scale)
        {
            float t = info.Time;
            Vector3 nodePos = node.position;

            Vector3 outward = Vector3.Cross(info.Tangent, Vector3.up);
            if (outward.sqrMagnitude < 1e-6f)
                outward = Vector3.Cross(info.Tangent, Vector3.right);
            outward.Normalize();

            float curWorldRadius = pb.radius * PhysBoneChainUtil.EvaluateCurveOrDefault(pb.radiusCurve, t) * scale;
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
                    p => PhysBoneChainUtil.EvaluateCurveOrDefault(p.radiusCurve, t),
                    (p, v) => SetCurveValueAtTime(GetOrCreateCurve(p), t, Mathf.Max(0f, v)),
                    newCurveMul, "Change PhysBone Radius Curve");
            }
        }

        // `endpointPosition` is one local-space offset shared by every strand of the chain, so
        // the handle is drawn on the first chain's tip and the drag is expressed in that tip's
        // local space before being written back.
        private static void HandleEndpoint(VRCPhysBone pb, List<VRCPhysBone> allSelected, List<List<Transform>> chains)
        {
            List<Transform> chain = chains[0];
            Transform tip = chain[chain.Count - 1];
            Vector3 current = PhysBoneChainUtil.EndpointWorldPosition(pb, tip);
            float size = HandleUtility.GetHandleSize(current) * 0.1f;

            EditorGUI.BeginChangeCheck();
            Vector3 newWorld = Handles.FreeMoveHandle(current, size, Vector3.zero, Handles.RectangleHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 newLocal = tip.InverseTransformPoint(newWorld);
                Vector3 delta = newLocal - pb.endpointPosition;
                bool alt = Event.current.alt;
                bool shift = Event.current.shift;

                Undo.RecordObjects(allSelected.Cast<Object>().ToArray(), "Move PhysBone Endpoint");
                if (alt)
                {
                    pb.endpointPosition = newLocal;
                }
                else if (shift)
                {
                    foreach (VRCPhysBone p in allSelected)
                        p.endpointPosition = newLocal;
                }
                else
                {
                    foreach (VRCPhysBone p in allSelected)
                        p.endpointPosition += delta;
                }
                SceneHandleBatchUtil.MarkDirty(allSelected);
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
    }
}
#endif
