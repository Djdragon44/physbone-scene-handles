#if PBHANDLES_VRCSDK_PRESENT
using System;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

namespace OpenSource.PhysBoneHandles
{
    // Makes our replacement inspectors actually win against VRChat's.
    //
    // The bug this fixes: 0.6.0 shipped VRCPhysBoneInspector and VRCPhysBoneColliderInspector
    // with [CustomEditor(typeof(VRCPhysBone))] and assumed that was enough. It isn't. The SDK
    // ships its own VRCPhysBoneEditor / VRCPhysBoneColliderEditor, precompiled into
    // VRC.SDK3.Dynamics.PhysBone.Editor.dll, carrying the same attribute for the same type.
    // Unity allows exactly one editor per type, and when two claim it the winner is decided
    // inside UnityEditor.CustomEditorAttributes - which prefers the first match it finds and
    // additionally sorts Unity's own types ahead of others. VRChat's won every time, so our
    // inspectors were compiled, loaded, and never drawn: the Tools toggle appeared to do
    // nothing because both states produced the same (VRChat) inspector.
    //
    // There is no public API for "override another package's CustomEditor". The decision is
    // made in one place:
    //     internal static Type UnityEditor.CustomEditorAttributes
    //         .FindCustomEditorTypeByType(Type type, bool multiEdit)
    // (verified by reading the metadata of UnityEditor.CoreModule.dll for the Unity version in
    // use; there is a sibling FindCustomEditorType(object, bool) that funnels into the same
    // answer.) A Postfix on each one inspects the editor type Unity decided on, and if that is
    // one of VRChat's two PhysBone editors, substitutes ours.
    //
    // Matching is on the simple type name rather than a full namespace-qualified string: the
    // assembly is VRChat's and the namespace has moved between SDK versions, but
    // "VRCPhysBoneEditor" has not. The guard excludes our own assembly so the substitution
    // can't recurse.
    //
    // When the user turns the custom UI off, the postfix returns Unity's original answer
    // untouched - which is VRChat's own inspector, properly restored. That's strictly better
    // than 0.6.0's documented fallback to Unity's default field list, and the reason that note
    // is now gone from the README.
    //
    // If the internal method is ever renamed, the reflection lookup misses, a warning is
    // logged once, and the package degrades to VRChat's inspector plus all the Scene handles -
    // which is exactly 0.5.0's behaviour. Nothing breaks.
    [InitializeOnLoad]
    internal static class CustomEditorOverrideBootstrap
    {
        static CustomEditorOverrideBootstrap()
        {
            try
            {
                Type attrs = typeof(Editor).Assembly.GetType("UnityEditor.CustomEditorAttributes");
                if (attrs == null)
                {
                    Warn("couldn't find UnityEditor.CustomEditorAttributes");
                    return;
                }

                MethodInfo byType = attrs.GetMethod("FindCustomEditorTypeByType",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(Type), typeof(bool) }, null);
                MethodInfo byObject = attrs.GetMethod("FindCustomEditorType",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(UnityEngine.Object), typeof(bool) }, null);

                if (byType == null && byObject == null)
                {
                    Warn("couldn't find CustomEditorAttributes.FindCustomEditorType*");
                    return;
                }

                var harmony = new Harmony("com.opensource.physbonehandles.customeditoroverride");
                var postfix = new HarmonyMethod(typeof(CustomEditorOverridePatch).GetMethod(
                    nameof(CustomEditorOverridePatch.Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic));

                // Both entry points get the same postfix. Patching only one would leave the
                // other path returning VRChat's editor, which is how this would half-work.
                if (byType != null) harmony.Patch(byType, postfix: postfix);
                if (byObject != null) harmony.Patch(byObject, postfix: postfix);
            }
            catch (Exception e)
            {
                Warn(e.Message);
            }
        }

        private static void Warn(string detail)
        {
            Debug.LogWarning("PhysBone Handles: the custom PhysBone component UI is unavailable " +
                $"on this Unity version ({detail}). VRChat's own inspector is used instead; " +
                "all Scene-view handles still work.");
        }
    }

    internal static class CustomEditorOverridePatch
    {
        internal static void Postfix(ref Type __result)
        {
            if (!CustomInspectorSettings.Enabled) return;
            if (__result == null) return;
            // Never rewrite one of our own answers - that would recurse.
            if (__result.Assembly == typeof(CustomEditorOverridePatch).Assembly) return;

            switch (__result.Name)
            {
                case "VRCPhysBoneEditor":
                    __result = typeof(VRCPhysBoneInspector);
                    break;
                case "VRCPhysBoneColliderEditor":
                    __result = typeof(VRCPhysBoneColliderInspector);
                    break;
            }
        }
    }
}
#endif
