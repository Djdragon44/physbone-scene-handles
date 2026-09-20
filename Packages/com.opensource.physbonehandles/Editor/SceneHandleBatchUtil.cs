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
    }
}
#endif
