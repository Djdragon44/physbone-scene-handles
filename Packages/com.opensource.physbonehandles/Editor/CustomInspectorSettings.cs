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
    // Off restores VRChat's own inspector exactly. Our editors don't win by attribute alone -
    // the SDK ships its own [CustomEditor] for the same types and Unity picks one - so
    // CustomEditorOverridePatch substitutes ours at the point Unity decides. With this switch
    // off that patch stands down and Unity's original answer (VRChat's editor) is returned.
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

            // A repaint isn't enough. Which Editor class a component gets is decided once,
            // when the tracker builds its editor list, and CustomEditorOverridePatch only gets
            // asked at that moment - so repainting just redraws the editor instance Unity
            // already chose. The tracker has to rebuild for the switch to take effect without
            // reselecting the object.
            ActiveEditorTracker.sharedTracker.ForceRebuild();
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
