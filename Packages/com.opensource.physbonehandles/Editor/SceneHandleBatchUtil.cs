#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenSource.PhysBoneHandles
{
    // Shared "drag one handle, apply the change across a whole selection" logic, used by both
    // the VRCPhysBoneCollider handles and the VRCPhysBone radius handle so the Alt/Shift
    // batch-editing behavior is identical and defined in exactly one place.
    internal static class SceneHandleBatchUtil
    {
        // (none)  - delta applied to every object in allSelected, additively.
        // Alt     - only `dragged` is changed.
        // Shift   - every object in allSelected is set to newValue directly (equalize).
        internal static void ApplyScalarDelta<T>(T dragged, List<T> allSelected,
            System.Func<T, float> get, System.Action<T, float> set,
            float newValue, string undoLabel) where T : Object
        {
            float delta = newValue - get(dragged);
            bool alt = Event.current.alt;
            bool shift = Event.current.shift;

            Undo.RecordObjects(allSelected.Cast<Object>().ToArray(), undoLabel);
            if (alt)
            {
                set(dragged, newValue);
            }
            else if (shift)
            {
                foreach (T c in allSelected)
                    set(c, newValue);
            }
            else
            {
                foreach (T c in allSelected)
                    set(c, get(c) + delta);
            }
            MarkDirty(allSelected);
        }

        internal static void MarkDirty<T>(List<T> objects) where T : Object
        {
            foreach (T o in objects)
                EditorUtility.SetDirty(o);
        }

        // Shared wireframe drawing, used both by the live collider handles and by the Auto
        // Collider Generator's "what would Apply actually create" scene preview.
        internal static void DrawSphereWire(Vector3 center, Quaternion rot, float radius)
        {
            Handles.DrawWireDisc(center, rot * Vector3.up, radius);
            Handles.DrawWireDisc(center, rot * Vector3.right, radius);
            Handles.DrawWireDisc(center, rot * Vector3.forward, radius);
        }

        internal static void DrawCapsuleWire(Vector3 center, Quaternion rot, float radius, float height)
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

        // Used by the VRCContactSender/VRCContactReceiver Box shape - the only shape of
        // theirs that isn't a sphere or capsule.
        internal static void DrawBoxWire(Vector3 center, Quaternion rot, Vector3 size)
        {
            Matrix4x4 prevMatrix = Handles.matrix;
            Handles.matrix = Matrix4x4.TRS(center, rot, Vector3.one);
            Handles.DrawWireCube(Vector3.zero, size);
            Handles.matrix = prevMatrix;
        }
    }
}
#endif
