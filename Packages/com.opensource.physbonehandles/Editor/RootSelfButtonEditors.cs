#if PBHANDLES_VRCSDK_PRESENT
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Adds "Set Root to Self" to the component context menu (the ⋮ next to the component
    // name, or right-click on its header) for the four VRC components that have a Root
    // Transform field, so you don't need to drag the object's own Transform into its own
    // Root slot by hand.
    //
    // This is deliberately NOT a custom Editor / OnInspectorGUI override: VRC's SDK already
    // ships its own CustomEditor for all four component types, and Unity has no supported
    // way to "chain" two [CustomEditor] classes targeting the same component - whichever one
    // Unity's type cache resolves last silently wins, and there's no guarantee which that
    // is (in practice VRC's own editor won, so a subclassing attempt here was dead code).
    // CONTEXT/ menu items are the sanctioned way to add an action to a component's inspector
    // without touching its Editor, and they work no matter who owns the CustomEditor.
    internal static class RootSelfContextMenu
    {
        [MenuItem("CONTEXT/VRCPhysBoneCollider/Set Root to Self")]
        private static void SetColliderRootToSelf(MenuCommand command)
        {
            var c = (VRCPhysBoneCollider)command.context;
            Undo.RecordObject(c, "Set Root To Self");
            c.rootTransform = c.transform;
            EditorUtility.SetDirty(c);
        }

        [MenuItem("CONTEXT/VRCPhysBone/Set Root to Self")]
        private static void SetPhysBoneRootToSelf(MenuCommand command)
        {
            var pb = (VRCPhysBone)command.context;
            Undo.RecordObject(pb, "Set Root To Self");
            pb.rootTransform = pb.transform;
            EditorUtility.SetDirty(pb);
        }

        [MenuItem("CONTEXT/VRCContactSender/Set Root to Self")]
        private static void SetContactSenderRootToSelf(MenuCommand command)
        {
            var s = (VRCContactSender)command.context;
            Undo.RecordObject(s, "Set Root To Self");
            s.rootTransform = s.transform;
            EditorUtility.SetDirty(s);
        }

        [MenuItem("CONTEXT/VRCContactReceiver/Set Root to Self")]
        private static void SetContactReceiverRootToSelf(MenuCommand command)
        {
            var r = (VRCContactReceiver)command.context;
            Undo.RecordObject(r, "Set Root To Self");
            r.rootTransform = r.transform;
            EditorUtility.SetDirty(r);
        }
    }
}
#endif
