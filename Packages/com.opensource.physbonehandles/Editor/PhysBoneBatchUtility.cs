#if PBHANDLES_VRCSDK_PRESENT
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Menu commands for adding PhysBone / PhysBoneCollider components to every selected
    // GameObject at once, so a whole row of finger bones (etc.) can be set up in one action
    // instead of one-at-a-time through the inspector's Add Component button.
    public static class PhysBoneBatchUtility
    {
        [MenuItem("GameObject/PhysBone Handles/Add VRC Phys Bone Collider to Selection", false, 10)]
        private static void AddCollidersToSelection()
        {
            Undo.SetCurrentGroupName("Add VRC Phys Bone Colliders");
            int group = Undo.GetCurrentGroup();

            foreach (GameObject go in Selection.gameObjects)
            {
                if (go.GetComponent<VRCPhysBoneCollider>() != null)
                    continue;
                Undo.AddComponent<VRCPhysBoneCollider>(go);
            }

            Undo.CollapseUndoOperations(group);
        }

        [MenuItem("GameObject/PhysBone Handles/Add VRC Phys Bone Collider to Selection", true)]
        private static bool ValidateAddCollidersToSelection() => Selection.gameObjects.Length > 0;

        [MenuItem("GameObject/PhysBone Handles/Add VRC Phys Bone to Selection", false, 11)]
        private static void AddPhysBonesToSelection()
        {
            Undo.SetCurrentGroupName("Add VRC Phys Bones");
            int group = Undo.GetCurrentGroup();

            foreach (GameObject go in Selection.gameObjects)
            {
                if (go.GetComponent<VRCPhysBone>() != null)
                    continue;
                Undo.AddComponent<VRCPhysBone>(go);
            }

            Undo.CollapseUndoOperations(group);
        }

        [MenuItem("GameObject/PhysBone Handles/Add VRC Phys Bone to Selection", true)]
        private static bool ValidateAddPhysBonesToSelection() => Selection.gameObjects.Length > 0;

        // Copies every field from the active object's VRCPhysBoneCollider onto every other
        // selected object's VRCPhysBoneCollider, except Root/Position/Rotation (which are
        // usually per-bone). Useful after using the scene handles to dial in one collider's
        // feel and wanting the rest of the set to match.
        [MenuItem("GameObject/PhysBone Handles/Copy Collider Settings (Active -> Selection)", false, 12)]
        private static void CopyColliderSettings()
        {
            VRCPhysBoneCollider source = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<VRCPhysBoneCollider>()
                : null;
            if (source == null)
            {
                Debug.LogWarning("PhysBone Handles: active object has no VRCPhysBoneCollider to copy from.");
                return;
            }

            Undo.SetCurrentGroupName("Copy PhysBoneCollider Settings");
            int group = Undo.GetCurrentGroup();

            foreach (GameObject go in Selection.gameObjects)
            {
                VRCPhysBoneCollider target = go.GetComponent<VRCPhysBoneCollider>();
                if (target == null || target == source)
                    continue;

                Undo.RecordObject(target, "Copy PhysBoneCollider Settings");
                target.shapeType = source.shapeType;
                target.radius = source.radius;
                target.height = source.height;
                target.insideBounds = source.insideBounds;
                target.bonesAsSpheres = source.bonesAsSpheres;
                EditorUtility.SetDirty(target);
            }

            Undo.CollapseUndoOperations(group);
        }

        [MenuItem("GameObject/PhysBone Handles/Copy Collider Settings (Active -> Selection)", true)]
        private static bool ValidateCopyColliderSettings() => Selection.gameObjects.Length > 1
            && Selection.activeGameObject != null
            && Selection.activeGameObject.GetComponent<VRCPhysBoneCollider>() != null;
    }
}
#endif
