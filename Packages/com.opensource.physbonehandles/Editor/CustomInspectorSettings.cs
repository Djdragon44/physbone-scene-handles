#if PBHANDLES_VRCSDK_PRESENT
using UnityEditor;

namespace OpenSource.PhysBoneHandles
{
    // One switch for the replacement component inspectors.
    //
    // Replacing another package's inspector is the most intrusive thing this package does, so
    // it has to be possible to turn off without uninstalling: a [CustomEditor] wins over
    // VRChat's for the same type, and if ours ever misbehaves on a future SDK the user needs a
    // way out that isn't deleting files.
    //
    // Off doesn't restore VRChat's editor (two [CustomEditor]s for one type can't hand off at
    // runtime) - it falls back to Unity's default field list, which shows every serialized
    // field and stays fully editable.
    internal static class CustomInspectorSettings
    {
        private const string Key = "OpenSource.PhysBoneHandles.CustomInspectorUI";
        private const string MenuPath = "Tools/PhysBone Handles/Use Custom Component UI";

        internal static bool Enabled
        {
            get => EditorPrefs.GetBool(Key, true);
            set => EditorPrefs.SetBool(Key, value);
        }

        [MenuItem(MenuPath, false, 100)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            // Force open inspectors to redraw with the other layout immediately.
            foreach (Editor editor in ActiveEditorTracker.sharedTracker.activeEditors)
                editor.Repaint();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}
#endif
