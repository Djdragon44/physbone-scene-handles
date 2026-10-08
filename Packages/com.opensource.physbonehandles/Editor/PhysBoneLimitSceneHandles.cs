#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Visualizes and edits a VRCPhysBone's *angle limits* in the scene view.
    //
    // Limits are the hardest part of a PhysBone to set up blind: "Max Angle 45" in the
    // inspector tells you nothing about where the bone can actually swing to, so the usual
    // workflow is type a number, enter play mode, shake the avatar, come back, type another
    // number. This draws the limit directly on the bone instead:
    //
    //   Angle limit : a cone from each bone, opening to maxAngleX degrees around its rest
    //                 direction - the bone can swing anywhere inside the cone and no further.
    //   Polar limit : an ellipse-ish cone using both maxAngleX (around Z) and maxAngleZ
    //                 (around X), for a joint that should swing freely one way and barely at
    //                 all the other (a knee, a jaw).
    //   Hinge       : a flat fan in the plane the bone is allowed to rotate in.
    //
    // A drag handle on the cone's rim edits maxAngleX, with the same Alt / Shift batch rules
    // as every other handle in this package.
    //
    // Reading the limit fields:
    //   These are read and written through SerializedObject by property name rather than as
    //   direct C# field access. It costs a little speed, but it means that if a future VRChat
    //   SDK renames or drops one of them this feature quietly switches itself off instead of
    //   breaking the whole package's compile - and every other feature here keeps working.
    //   Undo and prefab-override marking also come for free that way.
    [InitializeOnLoad]
    public static class PhysBoneLimitSceneHandles
    {
        private const string PrefKey = "OpenSource.PhysBoneHandles.Limits";

        internal static bool EditLimits
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        // Mirrors VRCPhysBoneBase.LimitType's ordering. Read as an int from the serialized
        // enum so this file doesn't need the enum type itself to compile.
        private enum LimitKind
        {
            None = 0,
            Angle = 1,
            Hinge = 2,
            Polar = 3,
        }

        static PhysBoneLimitSceneHandles()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!EditLimits)
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
                DrawLimits(pb, physBones, pb == active);
        }

        private static float ReadFloat(VRCPhysBone pb, string propertyName, float fallback)
        {
            SerializedProperty p = new SerializedObject(pb).FindProperty(propertyName);
            return p != null && p.propertyType == SerializedPropertyType.Float ? p.floatValue : fallback;
        }

        private static int ReadEnum(VRCPhysBone pb, string propertyName, int fallback)
        {
            SerializedProperty p = new SerializedObject(pb).FindProperty(propertyName);
            return p != null && p.propertyType == SerializedPropertyType.Enum ? p.enumValueIndex : fallback;
        }

        // The limit's orientation comes from the component's own `rotation` field (VRChat
        // documents it as pitch/yaw/roll about X/Y/Z), not from the bone transform - so a
        // rotated limit cone has to be drawn rotated too, or the drawing lies about where the
        // bone can actually swing.
        private static Quaternion LimitRotation(VRCPhysBone pb)
        {
            SerializedProperty p = new SerializedObject(pb).FindProperty("rotation");
            if (p == null || p.propertyType != SerializedPropertyType.Quaternion)
                return Quaternion.identity;
            Quaternion q = p.quaternionValue;
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return mag < 0.0001f ? Quaternion.identity : Quaternion.Normalize(q);
        }

        private static void WriteFloat(VRCPhysBone pb, string propertyName, float value, string undoLabel)
        {
            SerializedObject obj = new SerializedObject(pb);
            SerializedProperty p = obj.FindProperty(propertyName);
            if (p == null || p.propertyType != SerializedPropertyType.Float)
                return;
            Undo.RecordObject(pb, undoLabel);
            p.floatValue = value;
            obj.ApplyModifiedProperties();
        }

        private static void DrawLimits(VRCPhysBone pb, List<VRCPhysBone> allSelected, bool isActive)
        {
            LimitKind kind = (LimitKind)ReadEnum(pb, "limitType", 0);
            if (kind == LimitKind.None)
                return;

            float maxAngleX = ReadFloat(pb, "maxAngleX", 0f);
            float maxAngleZ = ReadFloat(pb, "maxAngleZ", 0f);
            if (maxAngleX <= 0f && maxAngleZ <= 0f)
                return;

            List<List<Transform>> chains = PhysBoneChainUtil.BuildChains(pb);
            Quaternion limitRot = LimitRotation(pb);
            Color coneColor = isActive
                ? new Color(0.3f, 0.7f, 1f, 0.9f)
                : new Color(0.3f, 0.7f, 1f, 0.4f);

            using (new Handles.DrawingScope(coneColor))
            {
                foreach (List<Transform> chain in chains)
                {
                    // The root bone itself isn't limited relative to anything (its parent is
                    // outside the chain), so limits are drawn from the second node onward.
                    for (int i = 1; i < chain.Count; i++)
                        DrawLimitAt(chain, i, kind, maxAngleX, maxAngleZ, limitRot);
                }
            }

            if (chains.Count > 0 && chains[0].Count > 1)
                DrawAngleHandle(pb, allSelected, chains[0], maxAngleX, limitRot);

        }

        // Draws the allowed swing region for the bone at `index`, centred on its rest
        // direction (the direction from its parent to it).
        private static void DrawLimitAt(List<Transform> chain, int index, LimitKind kind,
            float maxAngleX, float maxAngleZ, Quaternion limitRot)
        {
            Transform bone = chain[index];
            Transform parent = chain[index - 1];
            Vector3 origin = parent.position;
            Vector3 axis = bone.position - origin;
            float length = axis.magnitude;
            if (length < 1e-5f)
                return;
            axis /= length;

            // Keep the drawn cone a readable size: proportional to the bone, but never so big
            // on a long bone that it swamps the view.
            float drawLength = Mathf.Min(length, HandleUtility.GetHandleSize(origin) * 1.5f);

            // The limit frame: the bone's rest direction, reoriented by the component's own
            // `rotation`. Identity rotation leaves it centred on the rest direction, which is
            // the common case.
            Quaternion frame = parent.rotation * limitRot;

            switch (kind)
            {
                case LimitKind.Hinge:
                    // The hinge plane is the one `rotation` defines; its normal is the frame's
                    // X axis, and the bone sweeps a fan in the plane perpendicular to it.
                    DrawHingeFan(origin, axis, frame * Vector3.right, drawLength, maxAngleX);
                    break;
                case LimitKind.Polar:
                    // Max Pitch / Max Yaw - two different half-angles, so an elliptical cone.
                    DrawCone(origin, axis, frame, drawLength, maxAngleX, maxAngleZ);
                    break;
                default: // Angle - one Max Angle all the way round, so a circular cone.
                    DrawCone(origin, axis, frame, drawLength, maxAngleX, maxAngleX);
                    break;
            }
        }

        // An elliptical cone: half-angle `angleAroundZ` in one plane, `angleAroundX` in the
        // other. When the two are equal this is a plain circular cone (the Angle limit).
        private static void DrawCone(Vector3 origin, Vector3 axis, Quaternion frame,
            float length, float angleAroundZ, float angleAroundX)
        {
            const int segments = 32;

            // A stable pair of axes perpendicular to the bone direction, taken from the limit
            // frame so the ellipse's wide axis lines up with the pitch/yaw axes the runtime
            // actually uses.
            Vector3 right = frame * Vector3.right;
            if (Mathf.Abs(Vector3.Dot(right, axis)) > 0.99f)
                right = frame * Vector3.forward;
            right = Vector3.Normalize(right - axis * Vector3.Dot(right, axis));
            Vector3 forward = Vector3.Cross(axis, right);

            float tanX = Mathf.Tan(Mathf.Clamp(angleAroundZ, 0f, 89f) * Mathf.Deg2Rad);
            float tanZ = Mathf.Tan(Mathf.Clamp(angleAroundX, 0f, 89f) * Mathf.Deg2Rad);

            Vector3[] rim = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                Vector3 offset = right * (Mathf.Cos(a) * tanX) + forward * (Mathf.Sin(a) * tanZ);
                rim[i] = origin + (axis + offset).normalized * length;
            }

            Handles.DrawPolyLine(rim);

            // A few spokes back to the joint, so the shape reads as a cone and not a floating
            // ring. Four is enough to see the orientation without turning into a solid blob.
            for (int i = 0; i < segments; i += segments / 4)
                Handles.DrawLine(origin, rim[i]);

            // The rest direction itself, for reference.
            Handles.DrawLine(origin, origin + axis * length);
        }

        // A flat fan in the plane perpendicular to the hinge axis.
        private static void DrawHingeFan(Vector3 origin, Vector3 axis, Vector3 hingeAxis, float length, float maxAngle)
        {
            hingeAxis = Vector3.Normalize(hingeAxis - axis * Vector3.Dot(hingeAxis, axis));
            if (hingeAxis.sqrMagnitude < 1e-6f)
                return;

            float clamped = Mathf.Clamp(maxAngle, 0f, 180f);
            Handles.DrawWireArc(origin, hingeAxis, Quaternion.AngleAxis(-clamped, hingeAxis) * axis,
                clamped * 2f, length);
            Handles.DrawLine(origin, origin + Quaternion.AngleAxis(-clamped, hingeAxis) * axis * length);
            Handles.DrawLine(origin, origin + Quaternion.AngleAxis(clamped, hingeAxis) * axis * length);
            Handles.DrawLine(origin, origin + axis * length);
        }

        // One grab handle on the first chain's first limited joint, dragging the cone wider or
        // narrower. Reads out as a live degree label so you can still land on a round number.
        private static void DrawAngleHandle(VRCPhysBone pb, List<VRCPhysBone> allSelected,
            List<Transform> chain, float maxAngleX, Quaternion limitRot)
        {
            Transform bone = chain[1];
            Transform parent = chain[0];
            Vector3 origin = parent.position;
            Vector3 axis = bone.position - origin;
            float length = axis.magnitude;
            if (length < 1e-5f)
                return;
            axis /= length;

            float drawLength = Mathf.Min(length, HandleUtility.GetHandleSize(origin) * 1.5f);

            Quaternion frame = parent.rotation * limitRot;
            Vector3 right = frame * Vector3.right;
            if (Mathf.Abs(Vector3.Dot(right, axis)) > 0.99f)
                right = frame * Vector3.forward;
            right = Vector3.Normalize(right - axis * Vector3.Dot(right, axis));

            float clamped = Mathf.Clamp(maxAngleX, 0f, 180f);
            Vector3 rimDir = Quaternion.AngleAxis(clamped, Vector3.Cross(axis, right)) * axis;
            Vector3 handlePos = origin + rimDir * drawLength;
            float handleSize = HandleUtility.GetHandleSize(handlePos) * 0.07f;

            Handles.Label(handlePos, $"{clamped:0.#}°", EditorStyles.whiteMiniLabel);

            EditorGUI.BeginChangeCheck();
            // Dragging tangentially to the rim is what "open the cone wider" means, so the
            // slider axis is the tangent at the current rim point, not a world axis.
            Vector3 tangent = Vector3.Normalize(Vector3.Cross(Vector3.Cross(axis, right), rimDir));
            Vector3 newPos = Handles.Slider(handlePos, tangent, handleSize, Handles.DotHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 newDir = (newPos - origin).normalized;
                float newAngle = Vector3.Angle(axis, newDir);
                // Sign the angle so dragging past the rest direction reads as shrinking rather
                // than flipping to the other side of the cone.
                if (Vector3.Dot(newDir, right) < 0f && clamped < 90f)
                    newAngle = -newAngle;
                newAngle = Mathf.Clamp(newAngle, 0f, 180f);

                SceneHandleBatchUtil.ApplyScalarDelta(pb, allSelected,
                    p => ReadFloat(p, "maxAngleX", 0f),
                    (p, v) => WriteFloat(p, "maxAngleX", Mathf.Clamp(v, 0f, 180f), "Change PhysBone Angle Limit"),
                    newAngle, "Change PhysBone Angle Limit");
            }
        }
    }
}
#endif
