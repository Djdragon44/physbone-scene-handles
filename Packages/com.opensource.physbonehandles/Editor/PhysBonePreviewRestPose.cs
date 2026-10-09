#if PBHANDLES_VRCSDK_PRESENT
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenSource.PhysBoneHandles
{
    // The safety net under the whole edit-mode preview.
    //
    // The PhysBone solver writes to real scene Transforms. There is no sandbox: when it runs,
    // your avatar's bones genuinely move. So the preview is only honest if stopping it puts
    // every bone back exactly where it was, and leaves the scene no dirtier than it found it.
    // Get that wrong and the tool silently bakes a swung pose into someone's avatar - which is
    // worse than not having a preview at all.
    //
    // Two things are captured and restored:
    //
    //   * local position / rotation / scale of every Transform the solver could touch. Local,
    //     not world: restoring world values would fight with any parent that also moved, and
    //     local values are what Unity serializes anyway.
    //   * the scene's dirty flag. Moving a Transform marks the scene dirty, and that dirty mark
    //     is what makes Unity offer to save the swung pose. If the scene was clean when the
    //     preview started, it is made clean again on stop. If it was already dirty, it is left
    //     dirty - clearing someone's real unsaved edits would be the same class of mistake in
    //     the other direction.
    //
    // Deliberately not Undo: the solver writes transforms tens of times a second, and recording
    // that would bury the user's undo history under thousands of entries. A snapshot restore
    // leaves the undo stack untouched instead.
    internal sealed class PhysBonePreviewRestPose
    {
        private struct Entry
        {
            public Transform transform;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<Transform> _seen = new HashSet<Transform>();
        private readonly Dictionary<string, bool> _sceneWasDirty = new Dictionary<string, bool>();

        internal int Count => _entries.Count;

        // Snapshot `root` and everything under it. Called once per root before the first step.
        internal void Capture(Transform root)
        {
            if (root == null)
                return;

            RecordSceneDirtiness(root);

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !_seen.Add(t))
                    continue;

                _entries.Add(new Entry
                {
                    transform = t,
                    localPosition = t.localPosition,
                    localRotation = t.localRotation,
                    localScale = t.localScale,
                });
            }
        }

        // Put everything back. Safe to call more than once, and safe if objects were deleted
        // while the preview was running (a null transform is skipped, not an error).
        internal void Restore()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.transform == null)
                    continue;

                e.transform.localPosition = e.localPosition;
                e.transform.localRotation = e.localRotation;
                e.transform.localScale = e.localScale;
            }

            RestoreSceneDirtiness();
        }

        internal void Clear()
        {
            _entries.Clear();
            _seen.Clear();
            _sceneWasDirty.Clear();
        }

        private void RecordSceneDirtiness(Transform root)
        {
            UnityEngine.SceneManagement.Scene scene = root.gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
                return;
            if (!_sceneWasDirty.ContainsKey(scene.path))
                _sceneWasDirty[scene.path] = scene.isDirty;
        }

        private void RestoreSceneDirtiness()
        {
            foreach (KeyValuePair<string, bool> kv in _sceneWasDirty)
            {
                if (kv.Value)
                    continue; // Already dirty before we started - leave the user's edits alone.

                for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                {
                    UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetSceneAt(i);
                    if (scene.path == kv.Key && scene.isDirty)
                    {
                        ClearDirty(scene);
                        break;
                    }
                }
            }
        }

        // EditorSceneManager.ClearSceneDirtiness is internal (checked against 2022.3's
        // UnityEditor.CoreModule metadata), and there is no public equivalent - MarkSceneDirty
        // only goes the other way. Reflection is the only route.
        //
        // If it ever disappears the preview still restores every transform correctly; the only
        // consequence is a scene left marked dirty, so Unity offers to save a file whose
        // contents are already back to what they were. That is worth a warning, not a failure.
        private static MethodInfo _clearSceneDirtiness;
        private static bool _clearLookupDone;
        private static bool _warnedMissing;

        private static void ClearDirty(UnityEngine.SceneManagement.Scene scene)
        {
            if (!_clearLookupDone)
            {
                _clearLookupDone = true;
                _clearSceneDirtiness = typeof(EditorSceneManager).GetMethod(
                    "ClearSceneDirtiness",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new[] { typeof(UnityEngine.SceneManagement.Scene) },
                    null);
            }

            if (_clearSceneDirtiness == null)
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    Debug.LogWarning("[PhysBone Handles] Could not clear the scene's dirty flag after " +
                        "the preview. Your bones are back at their rest pose, but Unity may still offer " +
                        "to save the scene - saving it is harmless.");
                }
                return;
            }

            try
            {
                _clearSceneDirtiness.Invoke(null, new object[] { scene });
            }
            catch (Exception)
            {
                _clearSceneDirtiness = null;
            }
        }
    }
}
#endif
