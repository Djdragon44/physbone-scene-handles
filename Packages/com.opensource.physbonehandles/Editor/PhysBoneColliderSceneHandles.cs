#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Draws draggable scene-view handles (radius / height / position / rotation) on top of
    // every selected VRCPhysBoneCollider at once, so you can batch-tune colliders without
    // hand-typing numbers into the inspector for each one individually.
    //
    // Modifier keys while dragging a handle:
    //   (none)  - the dragged delta is applied to every selected collider additively.
    //   Alt     - only the collider you're actually dragging is changed.
    //   Shift   - every selected collider's value is set equal to the one you're dragging.
    [InitializeOnLoad]
    public static class PhysBoneColliderSceneHandles
    {
        private const string PrefPrefix = "OpenSource.PhysBoneHandles.";

        // internal (not private): shared with PhysBoneRadiusSceneHandles so one "Radius" toggle
        // governs both VRCPhysBoneCollider and VRCPhysBone radius handles.
        internal static bool EditRadius
        {
            get => EditorPrefs.GetBool(PrefPrefix + "Radius", true);
            set => EditorPrefs.SetBool(PrefPrefix + "Radius", value);
        }

        private static bool EditHeight
        {
            get => EditorPrefs.GetBool(PrefPrefix + "Height", true);
            set => EditorPrefs.SetBool(PrefPrefix + "Height", value);
        }

        private static bool EditPosition
        {
            get => EditorPrefs.GetBool(PrefPrefix + "Position", true);
            set => EditorPrefs.SetBool(PrefPrefix + "Position", value);
        }

        private static bool EditRotation
        {
            get => EditorPrefs.GetBool(PrefPrefix + "Rotation", false);
            set => EditorPrefs.SetBool(PrefPrefix + "Rotation", value);
        }

        static PhysBoneColliderSceneHandles()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static List<VRCPhysBoneCollider> GetSelectedColliders()
        {
            return Selection.gameObjects
                .Select(go => go.GetComponent<VRCPhysBoneCollider>())
                .Where(c => c != null)
                .ToList();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            List<VRCPhysBoneCollider> colliders = GetSelectedColliders();

            // Unity's own Move/Rotate gizmo for the active tool sits on the same GameObject
            // and, when the collider's local offset is small, right on top of our own handles -
            // making it easy to grab the wrong one and drag the actual bone. Hide the built-in
            // gizmo while ours would conflict with it, and give it back otherwise.
            bool suppressBuiltinGizmo = colliders.Count > 0 && (EditPosition || EditRotation);
            if (Tools.hidden != suppressBuiltinGizmo)
                Tools.hidden = suppressBuiltinGizmo;

            if (colliders.Count == 0)
                return;

            DrawTogglePanel(sceneView);

            // The "active" collider is whichever one Unity considers the primary selection;
            // its handles are drawn slightly brighter and is what modifier-key batching is relative to.
            VRCPhysBoneCollider active = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<VRCPhysBoneCollider>()
                : null;

            foreach (VRCPhysBoneCollider collider in colliders)
                DrawColliderHandles(collider, colliders, collider == active);
        }

        private static Transform RootOf(VRCPhysBoneCollider c) => c.rootTransform != null ? c.rootTransform : c.transform;

        private static void DrawColliderHandles(VRCPhysBoneCollider collider, List<VRCPhysBoneCollider> allSelected, bool isActive)
        {
            Transform root = RootOf(collider);
            float scale = root.lossyScale.x;
            Vector3 worldPos = root.TransformPoint(collider.position);
            Quaternion worldRot = root.rotation * collider.rotation;
            bool isCapsule = collider.shapeType == VRCPhysBoneColliderBase.ShapeType.Capsule;

            Color wireColor = isActive ? new Color(0.4f, 1f, 0.4f) : new Color(0.4f, 1f, 0.4f, 0.5f);
            using (new Handles.DrawingScope(wireColor))
            {
                if (isCapsule)
                    DrawCapsuleWire(worldPos, worldRot, collider.radius * scale, collider.height * scale);
                else
                    DrawSphereWire(worldPos, worldRot, collider.radius * scale);
                Handles.SphereHandleCap(0, worldPos, worldRot, HandleUtility.GetHandleSize(worldPos) * 0.05f, EventType.Repaint);
            }

            if (EditRadius)
                HandleRadius(collider, allSelected, root, worldPos, worldRot, scale, isCapsule);

            if (EditHeight && isCapsule)
                HandleHeight(collider, allSelected, root, worldPos, worldRot, scale);

            if (EditPosition)
                HandlePosition(collider, allSelected, root, worldPos, scale);

            if (EditRotation && isCapsule)
                HandleRotation(collider, allSelected, root, worldPos, worldRot);
        }

        private static void DrawSphereWire(Vector3 center, Quaternion rot, float radius)
        {
            Handles.DrawWireDisc(center, rot * Vector3.up, radius);
            Handles.DrawWireDisc(center, rot * Vector3.right, radius);
            Handles.DrawWireDisc(center, rot * Vector3.forward, radius);
        }

        private static void DrawCapsuleWire(Vector3 center, Quaternion rot, float radius, float height)
        {
            float half = Mathf.Max(height * 0.5f - radius, 0f);
            Vector3 up = rot * Vector3.up;
            Vector3 top = center + up * half;
            Vector3 bottom = center - up * half;
            Handles.DrawWireDisc(top, up, radius);
            Handles.DrawWireDisc(bottom, up, radius);
            Handles.DrawLine(top + rot * Vector3.right * radius, bottom + rot * Vector3.right * radius);
            Handles.DrawLine(top - rot * Vector3.right * radius, bottom - rot * Vector3.right * radius);
            Handles.DrawLine(top + rot * Vector3.forward * radius, bottom + rot * Vector3.forward * radius);
            Handles.DrawLine(top - rot * Vector3.forward * radius, bottom - rot * Vector3.forward * radius);
        }

        // --- Radius ---------------------------------------------------------

        private static void HandleRadius(VRCPhysBoneCollider collider, List<VRCPhysBoneCollider> allSelected,
            Transform root, Vector3 worldPos, Quaternion worldRot, float scale, bool isCapsule)
        {
            EditorGUI.BeginChangeCheck();
            float worldRadius = collider.radius * scale;
            float newWorldRadius = Handles.RadiusHandle(worldRot, worldPos, worldRadius, false);
            if (EditorGUI.EndChangeCheck())
            {
                float newLocalRadius = Mathf.Max(0f, newWorldRadius / Mathf.Max(scale, 0.0001f));
                ApplyScalarDelta(collider, allSelected, c => c.radius, (c, v) => c.radius = Mathf.Max(0f, v), newLocalRadius, "Change PhysBoneCollider Radius");
            }
        }

        // --- Height (capsule only) ------------------------------------------

        private static void HandleHeight(VRCPhysBoneCollider collider, List<VRCPhysBoneCollider> allSelected,
            Transform root, Vector3 worldPos, Quaternion worldRot, float scale)
        {
            Vector3 up = worldRot * Vector3.up;
            float halfHeight = collider.height * scale * 0.5f;

            EditorGUI.BeginChangeCheck();
            Vector3 topHandlePos = Handles.Slider(worldPos + up * halfHeight, up, HandleUtility.GetHandleSize(worldPos) * 0.15f, Handles.ConeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float newHalf = Mathf.Max(0f, Vector3.Dot(topHandlePos - worldPos, up));
                float newLocalHeight = (newHalf * 2f) / Mathf.Max(scale, 0.0001f);
                ApplyScalarDelta(collider, allSelected, c => c.height, (c, v) => c.height = Mathf.Max(0f, v), newLocalHeight, "Change PhysBoneCollider Height");
            }

            EditorGUI.BeginChangeCheck();
            Vector3 bottomHandlePos = Handles.Slider(worldPos - up * halfHeight, -up, HandleUtility.GetHandleSize(worldPos) * 0.15f, Handles.ConeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float newHalf = Mathf.Max(0f, Vector3.Dot(worldPos - bottomHandlePos, up));
                float newLocalHeight = (newHalf * 2f) / Mathf.Max(scale, 0.0001f);
                ApplyScalarDelta(collider, allSelected, c => c.height, (c, v) => c.height = Mathf.Max(0f, v), newLocalHeight, "Change PhysBoneCollider Height");
            }
        }

        // --- Position ---------------------------------------------------------

        private static void HandlePosition(VRCPhysBoneCollider collider, List<VRCPhysBoneCollider> allSelected,
            Transform root, Vector3 worldPos, float scale)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 newWorldPos = Handles.PositionHandle(worldPos, Tools.pivotRotation == PivotRotation.Local ? root.rotation : Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 newLocalPos = root.InverseTransformPoint(newWorldPos);
                Vector3 delta = newLocalPos - collider.position;
                bool alt = Event.current.alt;
                bool shift = Event.current.shift;

                Undo.RecordObjects(allSelected.Cast<Object>().ToArray(), "Move PhysBoneCollider");
                if (alt)
                {
                    collider.position = newLocalPos;
                }
                else if (shift)
                {
                    foreach (VRCPhysBoneCollider c in allSelected)
                        c.position = newLocalPos;
                }
                else
                {
                    foreach (VRCPhysBoneCollider c in allSelected)
                        c.position += delta;
                }
                MarkDirty(allSelected);
            }
        }

        // --- Rotation (capsule only) -------------------------------------------

        private static void HandleRotation(VRCPhysBoneCollider collider, List<VRCPhysBoneCollider> allSelected,
            Transform root, Vector3 worldPos, Quaternion worldRot)
        {
            EditorGUI.BeginChangeCheck();
            Quaternion newWorldRot = Handles.RotationHandle(worldRot, worldPos);
            if (EditorGUI.EndChangeCheck())
            {
                Quaternion newLocalRot = Quaternion.Inverse(root.rotation) * newWorldRot;
                Quaternion delta = newLocalRot * Quaternion.Inverse(collider.rotation);
                bool alt = Event.current.alt;
                bool shift = Event.current.shift;

                Undo.RecordObjects(allSelected.Cast<Object>().ToArray(), "Rotate PhysBoneCollider");
                if (alt)
                {
                    collider.rotation = newLocalRot;
                }
                else if (shift)
                {
                    foreach (VRCPhysBoneCollider c in allSelected)
                        c.rotation = newLocalRot;
                }
                else
                {
                    foreach (VRCPhysBoneCollider c in allSelected)
                        c.rotation = delta * c.rotation;
                }
                MarkDirty(allSelected);
            }
        }

        // --- Shared scalar-field apply helper (radius / height) ----------------

        private static void ApplyScalarDelta(VRCPhysBoneCollider dragged, List<VRCPhysBoneCollider> allSelected,
            System.Func<VRCPhysBoneCollider, float> get, System.Action<VRCPhysBoneCollider, float> set,
            float newValue, string undoLabel)
            => SceneHandleBatchUtil.ApplyScalarDelta(dragged, allSelected, get, set, newValue, undoLabel);

        private static void MarkDirty(List<VRCPhysBoneCollider> colliders) => SceneHandleBatchUtil.MarkDirty(colliders);

        // --- On-screen "Editing" toggle panel ----------------------------------

        private static void DrawTogglePanel(SceneView sceneView)
        {
            Handles.BeginGUI();
            const float w = 120f, h = 22f, pad = 4f;
            float x = sceneView.position.width - w - 10f;
            float y = sceneView.position.height - (h + pad) * 4f - 30f;

            DrawToggleButton(new Rect(x, y, w, h), "Radius", EditRadius, v => EditRadius = v);
            y += h + pad;
            DrawToggleButton(new Rect(x, y, w, h), "Height", EditHeight, v => EditHeight = v);
            y += h + pad;
            DrawToggleButton(new Rect(x, y, w, h), "Position", EditPosition, v => EditPosition = v);
            y += h + pad;
            DrawToggleButton(new Rect(x, y, w, h), "Rotation", EditRotation, v => EditRotation = v);

            Handles.EndGUI();
        }

        private static void DrawToggleButton(Rect rect, string label, bool value, System.Action<bool> setValue)
        {
            Color prevColor = GUI.backgroundColor;
            GUI.backgroundColor = value ? new Color(0.35f, 0.75f, 0.35f) : new Color(0.75f, 0.35f, 0.35f);
            if (GUI.Button(rect, label))
                setValue(!value);
            GUI.backgroundColor = prevColor;
        }
    }
}
#endif
