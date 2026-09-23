#if PBHANDLES_VRCSDK_PRESENT
using System;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Draws a small "S" (Set Root to Self) button directly inside the Root Transform row of
    // VRC's own inspector for VRCPhysBoneCollider / VRCPhysBone / VRCContactSender /
    // VRCContactReceiver - the same row, not a row above it.
    //
    // There's no supported Unity API for "add a control to a field row inside someone else's
    // Editor". Traced via IL (see PR discussion / commit notes): both
    // EditorGUILayout.PropertyField and the public EditorGUI.PropertyField(Rect,...) are thin
    // forwarders - every property field in the entire Editor, custom-drawn or not, ultimately
    // funnels through the internal instance method
    //     UnityEditor.PropertyHandler.OnGUI(Rect position, SerializedProperty property,
    //                                        GUIContent label, bool includeChildren, Rect visibleArea)
    // That's the one real choke point, so that's what gets patched here - not anything
    // VRC-specific. Because PropertyHandler is internal, it can't be named with typeof/nameof
    // from this assembly; the MethodInfo is resolved by reflection at patch time instead, and
    // the patch is applied manually (Harmony.Patch) rather than via [HarmonyPatch], since that
    // attribute needs a compile-time Type.
    //
    // A Prefix shrinks the Rect it's about to draw into by the width of our button, and a
    // Postfix draws the button into the sliver of space that frees up, but only when the
    // property being drawn is literally named "rootTransform" on one of our four tracked
    // component types - every other PropertyHandler.OnGUI call in the whole editor (there are
    // thousands per frame, for every inspector field anywhere) bails out after one cheap
    // string compare.
    //
    // This is inherently more fragile than the CONTEXT/ menu item in RootSelfButtonEditors.cs
    // (which keeps working regardless): it depends on VRC's inspector continuing to draw Root
    // Transform as a plain SerializedProperty field, and on this exact internal method still
    // existing with this exact signature in whatever Unity version is running - the reflection
    // lookup is wrapped so a miss just logs a warning instead of breaking anything, but a
    // future Unity update can silently turn this back into "context menu only" until updated.
    [InitializeOnLoad]
    internal static class RootFieldOverlayBootstrap
    {
        static RootFieldOverlayBootstrap()
        {
            try
            {
                Type propertyHandlerType = typeof(Editor).Assembly.GetType("UnityEditor.PropertyHandler");
                MethodInfo original = propertyHandlerType?.GetMethod(
                    "OnGUI",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Rect), typeof(SerializedProperty), typeof(GUIContent), typeof(bool), typeof(Rect) },
                    null);

                if (original == null)
                {
                    Debug.LogWarning("PhysBone Handles: couldn't find PropertyHandler.OnGUI to patch " +
                        "for the inline \"Set Root to Self\" button (Unity version mismatch?). " +
                        "The Set Root to Self context menu item still works.");
                    return;
                }

                var harmony = new Harmony("com.opensource.physbonehandles.rootfieldoverlay");
                var prefix = new HarmonyMethod(typeof(RootFieldOverlayPatch).GetMethod(
                    nameof(RootFieldOverlayPatch.Prefix), BindingFlags.Static | BindingFlags.NonPublic));
                var postfix = new HarmonyMethod(typeof(RootFieldOverlayPatch).GetMethod(
                    nameof(RootFieldOverlayPatch.Postfix), BindingFlags.Static | BindingFlags.NonPublic));

                harmony.Patch(original, prefix: prefix, postfix: postfix);
            }
            catch (Exception e)
            {
                Debug.LogWarning("PhysBone Handles: failed to patch the inline \"Set Root to Self\" " +
                    $"button ({e.Message}). The Set Root to Self context menu item still works.");
            }
        }
    }

    internal static class RootFieldOverlayPatch
    {
        private const float ButtonWidth = 20f;

        private static bool IsTracked(SerializedProperty property)
        {
            if (property.name != "rootTransform") return false;
            UnityEngine.Object target = property.serializedObject.targetObject;
            return target is VRCPhysBoneCollider
                || target is VRCPhysBone
                || target is VRCContactSender
                || target is VRCContactReceiver;
        }

        internal static void Prefix(ref Rect position, SerializedProperty property)
        {
            if (!IsTracked(property)) return;
            position.width -= ButtonWidth + 2f;
        }

        internal static void Postfix(Rect position, SerializedProperty property)
        {
            if (!IsTracked(property)) return;

            Rect buttonRect = new Rect(position.xMax + 2f, position.y, ButtonWidth, position.height);
            if (GUI.Button(buttonRect, new GUIContent("S", "Set Root to this GameObject")))
            {
                UnityEngine.Object[] targets = property.serializedObject.targetObjects;
                Undo.RecordObjects(targets, "Set Root To Self");
                foreach (UnityEngine.Object t in targets)
                {
                    Transform self = ((Component)t).transform;
                    switch (t)
                    {
                        case VRCPhysBoneCollider c: c.rootTransform = self; break;
                        case VRCPhysBone pb: pb.rootTransform = self; break;
                        case VRCContactSender s: s.rootTransform = self; break;
                        case VRCContactReceiver r: r.rootTransform = self; break;
                    }
                    EditorUtility.SetDirty(t);
                }
            }
        }
    }
}
#endif
