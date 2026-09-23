#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Quick visual toggle for which VRCPhysBoneCollider(s) a selected VRCPhysBone actually
    // collides with, instead of dragging entries into the Colliders list one at a time.
    // Select a PhysBone, turn on "Edit Colliders" in the scene view, and every collider on the
    // same avatar shows up as a small sphere: green if it's already in this PhysBone's
    // Colliders list, red if it isn't. Click a sphere to toggle it.
    [InitializeOnLoad]
    internal static class PhysBoneColliderPicker
    {
        private const string PrefKey = "OpenSource.PhysBoneHandles.ColliderPickerActive";

        private static bool PickerActive
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        static PhysBoneColliderPicker()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static List<VRCPhysBone> GetSelectedPhysBones()
        {
            return Selection.gameObjects
                .Select(go => go.GetComponent<VRCPhysBone>())
                .Where(pb => pb != null)
                .ToList();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            List<VRCPhysBone> physBones = GetSelectedPhysBones();
            if (physBones.Count == 0)
                return;

            DrawTogglePanel(sceneView);
            if (!PickerActive)
                return;

            VRCPhysBone active = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<VRCPhysBone>()
                : null;
            if (active == null)
                active = physBones[0];

            List<VRCPhysBoneCollider> candidates = GetCandidateColliders(active.transform);
            if (candidates.Count == 0)
                return;

            foreach (VRCPhysBoneCollider candidate in candidates)
                DrawColliderButton(candidate, active, physBones);
        }

        // Scopes candidates to the same avatar as the PhysBone (via VRCAvatarDescriptor), so
        // picking doesn't get cluttered with unrelated colliders elsewhere in the scene. Falls
        // back to the topmost parent if there's no descriptor (e.g. a bare test rig).
        private static List<VRCPhysBoneCollider> GetCandidateColliders(Transform pbTransform)
        {
            VRCAvatarDescriptor descriptor = pbTransform.GetComponentInParent<VRCAvatarDescriptor>();
            GameObject scopeRoot;
            if (descriptor != null)
            {
                scopeRoot = descriptor.gameObject;
            }
            else
            {
                Transform t = pbTransform;
                while (t.parent != null)
                    t = t.parent;
                scopeRoot = t.gameObject;
            }

            return scopeRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true).ToList();
        }

        private static void DrawColliderButton(VRCPhysBoneCollider candidate, VRCPhysBone active, List<VRCPhysBone> allSelected)
        {
            Transform ct = candidate.transform;
            Transform colliderRoot = candidate.rootTransform != null ? candidate.rootTransform : ct;
            Vector3 worldPos = colliderRoot.TransformPoint(candidate.position);

            bool activeHasIt = active.colliders.Contains(candidate);
            Color color = activeHasIt ? new Color(0.35f, 1f, 0.35f) : new Color(1f, 0.35f, 0.35f);
            float size = HandleUtility.GetHandleSize(worldPos) * 0.12f;

            using (new Handles.DrawingScope(color))
            {
                if (Handles.Button(worldPos, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                {
                    bool newState = !activeHasIt;
                    bool alt = Event.current.alt;
                    List<VRCPhysBone> targets = alt ? new List<VRCPhysBone> { active } : allSelected;

                    Undo.RecordObjects(targets.Cast<Object>().ToArray(), "Toggle PhysBone Collider");
                    foreach (VRCPhysBone pb in targets)
                    {
                        bool has = pb.colliders.Contains(candidate);
                        if (newState && !has)
                            pb.colliders.Add(candidate);
                        else if (!newState && has)
                            pb.colliders.Remove(candidate);
                        EditorUtility.SetDirty(pb);
                    }
                }
                Handles.Label(worldPos + Vector3.up * size * 1.3f, ct.name, EditorStyles.whiteMiniLabel);
            }
        }

        private static void DrawTogglePanel(SceneView sceneView)
        {
            Handles.BeginGUI();
            const float w = 150f, h = 22f;
            float x = sceneView.position.width - w - 10f;
            float y = 30f;

            Color prevColor = GUI.backgroundColor;
            GUI.backgroundColor = PickerActive ? new Color(0.35f, 0.75f, 0.35f) : new Color(0.5f, 0.5f, 0.5f);
            string label = PickerActive ? "Edit Colliders: ON" : "Edit Colliders: OFF";
            if (GUI.Button(new Rect(x, y, w, h), new GUIContent(label,
                "Shows every collider on this avatar as a clickable sphere - green = in this " +
                "PhysBone's Colliders list, red = not. Click to toggle. Alt = only the active " +
                "object, not the whole selection.")))
            {
                PickerActive = !PickerActive;
            }
            GUI.backgroundColor = prevColor;

            Handles.EndGUI();
        }
    }
}
#endif
