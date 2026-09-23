#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace OpenSource.PhysBoneHandles
{
    // Draws draggable scene-view handles (size / position / rotation) on top of every
    // selected VRCContactSender/VRCContactReceiver at once - the same batch-editing idea as
    // PhysBoneColliderSceneHandles, extended to the Contact system's three shapes (Sphere,
    // Capsule, Box) instead of PhysBoneCollider's two (Sphere, Capsule).
    //
    // VRCContactSender and VRCContactReceiver share every field this needs (rootTransform,
    // shapeType, radius, height, size, position, rotation) via their common ContactBase base
    // class, so unlike the PhysBoneCollider handles this works generically across both
    // concrete component types in one pass - selecting a Sender and a Receiver together
    // batches them exactly like selecting two Senders would.
    //
    // Modifier keys while dragging a handle (same as PhysBoneColliderSceneHandles):
    //   (none)  - the dragged delta is applied to every selected contact at once, additively.
    //   Alt     - only the one you're actually dragging is changed.
    //   Shift   - every selected one is set to the exact same value as the one you dragged.
    [InitializeOnLoad]
    public static class ContactSceneHandles
    {
        private const string PrefPrefix = "OpenSource.PhysBoneHandles.Contact.";

        private static bool EditSize
        {
            get => EditorPrefs.GetBool(PrefPrefix + "Size", true);
            set => EditorPrefs.SetBool(PrefPrefix + "Size", value);
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

        static ContactSceneHandles()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static List<ContactBase> GetSelectedContacts()
        {
            return Selection.gameObjects
                .Select(go => go.GetComponent<ContactBase>())
                .Where(c => c != null)
                .ToList();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            List<ContactBase> contacts = GetSelectedContacts();

            // Same reasoning as PhysBoneColliderSceneHandles: hide Unity's own Move/Rotate
            // gizmo while ours would sit on top of it, on the same GameObject.
            bool suppressBuiltinGizmo = contacts.Count > 0 && (EditPosition || EditRotation);
            if (Tools.hidden != suppressBuiltinGizmo)
                Tools.hidden = suppressBuiltinGizmo;

            if (contacts.Count == 0)
                return;

            DrawTogglePanel(sceneView);

            ContactBase active = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<ContactBase>()
                : null;

            foreach (ContactBase c in contacts)
                DrawContactHandles(c, contacts, c == active);
        }

        private static Transform RootOf(ContactBase c) => c.rootTransform != null ? c.rootTransform : c.transform;

        private static bool IsValid(Quaternion q)
        {
            return !float.IsNaN(q.x) && !float.IsNaN(q.y) && !float.IsNaN(q.z) && !float.IsNaN(q.w)
                && (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w) > 0.0001f;
        }

        private static void DrawContactHandles(ContactBase c, List<ContactBase> allSelected, bool isActive)
        {
            // Self-heal: same NaN-rotation guard as PhysBoneColliderSceneHandles - a bad drag
            // should never turn into a console spam loop on every subsequent repaint.
            if (!IsValid(c.rotation))
            {
                Debug.LogWarning($"PhysBone Handles: {c.name}'s Contact rotation was invalid (NaN) - resetting it to identity.", c);
                Undo.RecordObject(c, "Fix Invalid Contact Rotation");
                c.rotation = Quaternion.identity;
                EditorUtility.SetDirty(c);
            }

            Transform root = RootOf(c);
            float scale = root.lossyScale.x;
            Vector3 worldPos = root.TransformPoint(c.position);
            Quaternion worldRot = root.rotation * c.rotation;
            ContactBase.ShapeType shape = c.shapeType;

            Color wireColor = isActive ? new Color(0.3f, 0.7f, 1f) : new Color(0.3f, 0.7f, 1f, 0.5f);
            using (new Handles.DrawingScope(wireColor))
            {
                switch (shape)
                {
                    case ContactBase.ShapeType.Capsule:
                        SceneHandleBatchUtil.DrawCapsuleWire(worldPos, worldRot, c.radius * scale, c.height * scale);
                        break;
                    case ContactBase.ShapeType.Box:
                        SceneHandleBatchUtil.DrawBoxWire(worldPos, worldRot, c.size * scale);
                        break;
                    default:
                        SceneHandleBatchUtil.DrawSphereWire(worldPos, worldRot, c.radius * scale);
                        break;
                }
                Handles.SphereHandleCap(0, worldPos, worldRot, HandleUtility.GetHandleSize(worldPos) * 0.05f, EventType.Repaint);
            }

            if (EditSize)
            {
                switch (shape)
                {
                    case ContactBase.ShapeType.Sphere:
                        HandleRadius(c, allSelected, worldPos, worldRot, scale);
                        break;
                    case ContactBase.ShapeType.Capsule:
                        HandleRadius(c, allSelected, worldPos, worldRot, scale);
                        HandleHeight(c, allSelected, worldPos, worldRot, scale);
                        break;
                    case ContactBase.ShapeType.Box:
                        HandleBoxSize(c, allSelected, worldPos, worldRot, scale);
                        break;
                }
            }

            if (EditPosition)
                HandlePosition(c, allSelected, root, worldPos, scale);

            // Rotation is meaningless on a bare sphere, same reasoning PhysBoneColliderSceneHandles
            // uses to only show it for capsules.
            if (EditRotation && shape != ContactBase.ShapeType.Sphere)
                HandleRotation(c, allSelected, root, worldPos, worldRot);
        }

        // --- Radius (Sphere / Capsule) ------------------------------------------

        private static void HandleRadius(ContactBase c, List<ContactBase> allSelected, Vector3 worldPos, Quaternion worldRot, float scale)
        {
            EditorGUI.BeginChangeCheck();
            float worldRadius = c.radius * scale;
            float newWorldRadius = Handles.RadiusHandle(worldRot, worldPos, worldRadius, false);
            if (EditorGUI.EndChangeCheck())
            {
                float newLocalRadius = Mathf.Max(0f, newWorldRadius / Mathf.Max(scale, 0.0001f));
                SceneHandleBatchUtil.ApplyScalarDelta(c, allSelected,
                    x => x.radius, (x, v) => x.radius = Mathf.Max(0f, v), newLocalRadius, "Change Contact Radius");
            }
        }

        // --- Height (Capsule only) -----------------------------------------------

        private static void HandleHeight(ContactBase c, List<ContactBase> allSelected, Vector3 worldPos, Quaternion worldRot, float scale)
        {
            Vector3 up = worldRot * Vector3.up;
            float halfHeight = c.height * scale * 0.5f;

            EditorGUI.BeginChangeCheck();
            Vector3 topHandlePos = Handles.Slider(worldPos + up * halfHeight, up, HandleUtility.GetHandleSize(worldPos) * 0.15f, Handles.ConeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float newHalf = Mathf.Max(0f, Vector3.Dot(topHandlePos - worldPos, up));
                float newLocalHeight = (newHalf * 2f) / Mathf.Max(scale, 0.0001f);
                SceneHandleBatchUtil.ApplyScalarDelta(c, allSelected,
                    x => x.height, (x, v) => x.height = Mathf.Max(0f, v), newLocalHeight, "Change Contact Height");
            }

            EditorGUI.BeginChangeCheck();
            Vector3 bottomHandlePos = Handles.Slider(worldPos - up * halfHeight, -up, HandleUtility.GetHandleSize(worldPos) * 0.15f, Handles.ConeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float newHalf = Mathf.Max(0f, Vector3.Dot(worldPos - bottomHandlePos, up));
                float newLocalHeight = (newHalf * 2f) / Mathf.Max(scale, 0.0001f);
                SceneHandleBatchUtil.ApplyScalarDelta(c, allSelected,
                    x => x.height, (x, v) => x.height = Mathf.Max(0f, v), newLocalHeight, "Change Contact Height");
            }
        }

        // --- Size (Box only) - one opposing slider pair per axis, same symmetric
        // grow-from-center behavior as the capsule's height handles --------------

        private static void HandleBoxSize(ContactBase c, List<ContactBase> allSelected, Vector3 worldPos, Quaternion worldRot, float scale)
        {
            HandleBoxAxis(c, allSelected, worldPos, worldRot * Vector3.right, scale,
                x => x.size.x, (x, v) => x.size.x = Mathf.Max(0f, v), "Change Contact Size X");
            HandleBoxAxis(c, allSelected, worldPos, worldRot * Vector3.up, scale,
                x => x.size.y, (x, v) => x.size.y = Mathf.Max(0f, v), "Change Contact Size Y");
            HandleBoxAxis(c, allSelected, worldPos, worldRot * Vector3.forward, scale,
                x => x.size.z, (x, v) => x.size.z = Mathf.Max(0f, v), "Change Contact Size Z");
        }

        private static void HandleBoxAxis(ContactBase c, List<ContactBase> allSelected, Vector3 worldPos, Vector3 worldAxis, float scale,
            System.Func<ContactBase, float> getAxisSize, System.Action<ContactBase, float> setAxisSize, string undoLabel)
        {
            float halfExtent = getAxisSize(c) * scale * 0.5f;
            float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.15f;

            EditorGUI.BeginChangeCheck();
            Vector3 posHandlePos = Handles.Slider(worldPos + worldAxis * halfExtent, worldAxis, handleSize, Handles.CubeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float newHalf = Mathf.Max(0f, Vector3.Dot(posHandlePos - worldPos, worldAxis));
                float newLocal = (newHalf * 2f) / Mathf.Max(scale, 0.0001f);
                SceneHandleBatchUtil.ApplyScalarDelta(c, allSelected, getAxisSize, setAxisSize, newLocal, undoLabel);
            }

            EditorGUI.BeginChangeCheck();
            Vector3 negHandlePos = Handles.Slider(worldPos - worldAxis * halfExtent, -worldAxis, handleSize, Handles.CubeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float newHalf = Mathf.Max(0f, Vector3.Dot(worldPos - negHandlePos, worldAxis));
                float newLocal = (newHalf * 2f) / Mathf.Max(scale, 0.0001f);
                SceneHandleBatchUtil.ApplyScalarDelta(c, allSelected, getAxisSize, setAxisSize, newLocal, undoLabel);
            }
        }

        // --- Position ------------------------------------------------------------

        private static void HandlePosition(ContactBase c, List<ContactBase> allSelected, Transform root, Vector3 worldPos, float scale)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 newWorldPos = Handles.PositionHandle(worldPos, Tools.pivotRotation == PivotRotation.Local ? root.rotation : Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 newLocalPos = root.InverseTransformPoint(newWorldPos);
                Vector3 delta = newLocalPos - c.position;
                bool alt = Event.current.alt;
                bool shift = Event.current.shift;

                Undo.RecordObjects(allSelected.Cast<Object>().ToArray(), "Move Contact");
                if (alt)
                {
                    c.position = newLocalPos;
                }
                else if (shift)
                {
                    foreach (ContactBase x in allSelected)
                        x.position = newLocalPos;
                }
                else
                {
                    foreach (ContactBase x in allSelected)
                        x.position += delta;
                }
                SceneHandleBatchUtil.MarkDirty(allSelected);
            }
        }

        // --- Rotation (Capsule / Box) ---------------------------------------------

        private static void HandleRotation(ContactBase c, List<ContactBase> allSelected, Transform root, Vector3 worldPos, Quaternion worldRot)
        {
            EditorGUI.BeginChangeCheck();
            Quaternion newWorldRot = Handles.RotationHandle(worldRot, worldPos);
            // Handles.RotationHandle can momentarily return a degenerate (NaN) quaternion
            // while dragging the free-rotate ring at certain angles - never write that out.
            if (EditorGUI.EndChangeCheck() && IsValid(newWorldRot))
            {
                newWorldRot = Quaternion.Normalize(newWorldRot);
                Quaternion newLocalRot = Quaternion.Normalize(Quaternion.Inverse(root.rotation) * newWorldRot);
                Quaternion delta = Quaternion.Normalize(newLocalRot * Quaternion.Inverse(c.rotation));
                bool alt = Event.current.alt;
                bool shift = Event.current.shift;

                Undo.RecordObjects(allSelected.Cast<Object>().ToArray(), "Rotate Contact");
                if (alt)
                {
                    c.rotation = newLocalRot;
                }
                else if (shift)
                {
                    foreach (ContactBase x in allSelected)
                        x.rotation = newLocalRot;
                }
                else
                {
                    foreach (ContactBase x in allSelected)
                        x.rotation = Quaternion.Normalize(delta * x.rotation);
                }
                SceneHandleBatchUtil.MarkDirty(allSelected);
            }
        }

        // --- On-screen "Editing" toggle panel -------------------------------------
        // Bottom-left, deliberately not the same corner as PhysBoneColliderSceneHandles's
        // panel (bottom-right) so the two never overlap if a PhysBoneCollider and a Contact
        // end up selected together.

        private static void DrawTogglePanel(SceneView sceneView)
        {
            Handles.BeginGUI();
            const float w = 120f, h = 22f, pad = 4f;
            const float x = 10f;
            float y = sceneView.position.height - (h + pad) * 3f - 30f;

            DrawToggleButton(new Rect(x, y, w, h), "Size", EditSize, v => EditSize = v);
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
