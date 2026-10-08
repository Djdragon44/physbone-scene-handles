#if PBHANDLES_VRCSDK_PRESENT
using System;
using UnityEditor;
using UnityEngine;

namespace OpenSource.PhysBoneHandles
{
    // Shared drawing primitives for our replacement PhysBone / PhysBoneCollider inspectors.
    //
    // Three things the stock SDK inspector doesn't give you, and which the whole point of the
    // custom UI is to add:
    //
    //   * Section headers that are real foldouts persisted per-component, so a PhysBone with
    //     eight sections isn't a wall of forty fields.
    //   * "Pill" buttons that sit on the right-hand end of a header or field row - a labelled
    //     bool drawn as a coloured button (green = on) instead of a checkbox somewhere else
    //     in the component.
    //   * A compact scene-handle toggle at the end of a field row, so the row that shows you
    //     Radius is also the switch for the Radius handle in the Scene view. That's the piece
    //     that ties this package's handles to the inspector.
    //
    // Everything here is pure IMGUI against SerializedProperty, with no dependency on SDK
    // types, so a future SDK change can't break this file.
    internal static class InspectorUI
    {
        internal static readonly Color OnColor = new Color(0.30f, 0.70f, 0.35f);
        internal static readonly Color OffColor = new Color(0.62f, 0.22f, 0.22f);
        internal static readonly Color NeutralColor = new Color(0.42f, 0.42f, 0.42f);

        private static GUIStyle _sectionHeader;
        private static GUIStyle _pill;

        private static GUIStyle SectionHeader => _sectionHeader ?? (_sectionHeader = new GUIStyle(EditorStyles.foldout)
        {
            fontStyle = FontStyle.Bold,
            fontSize = EditorStyles.boldLabel.fontSize + 1,
        });

        private static GUIStyle Pill => _pill ?? (_pill = new GUIStyle(EditorStyles.miniButton)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = 17f,
        });

        // A bold foldout header with an optional pill button parked on its right-hand edge.
        // Returns the (new) expanded state. `foldoutProperty` is the SDK's own serialized
        // foldout bool where one exists (foldout_transforms, foldout_limits, ...) so expanding
        // a section here and in the stock inspector mean the same thing; pass null to fall
        // back to an EditorPrefs key.
        internal static bool SectionHeaderRow(string title, SerializedProperty foldoutProperty,
            string prefsKey, Action<Rect> drawRightSide = null)
        {
            Rect row = EditorGUILayout.GetControlRect(false, 20f);
            Rect labelRect = row;
            labelRect.height = EditorGUIUtility.singleLineHeight;

            bool expanded = foldoutProperty != null
                ? foldoutProperty.boolValue
                : EditorPrefs.GetBool(prefsKey, true);

            if (drawRightSide != null)
            {
                // Reserve the right third of the row for the caller's control and clip the
                // foldout's own hit area to the left of it, so clicking the pill doesn't also
                // collapse the section underneath it.
                const float rightWidth = 120f;
                Rect rightRect = new Rect(row.xMax - rightWidth, row.y, rightWidth, labelRect.height);
                labelRect.width = Mathf.Max(labelRect.width - rightWidth - 4f, 40f);

                bool newExpanded = EditorGUI.Foldout(labelRect, expanded, title, true, SectionHeader);
                drawRightSide(rightRect);
                expanded = newExpanded;
            }
            else
            {
                expanded = EditorGUI.Foldout(labelRect, expanded, title, true, SectionHeader);
            }

            if (foldoutProperty != null)
                foldoutProperty.boolValue = expanded;
            else
                EditorPrefs.SetBool(prefsKey, expanded);

            return expanded;
        }

        // Draws `property` (a bool) as a labelled coloured button inside `rect`. Used for the
        // header-level switches: Allow Collision, Allow Grabbing, Show Gizmos.
        internal static void PillToggle(Rect rect, SerializedProperty property, string label, string tooltip)
        {
            if (property == null) return;
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = property.boolValue ? OnColor : OffColor;
            if (GUI.Button(rect, new GUIContent(label, tooltip), Pill))
                property.boolValue = !property.boolValue;
            GUI.backgroundColor = prev;
        }

        // Same, but for a plain bool that isn't a SerializedProperty (an EditorPrefs-backed
        // tool setting such as "Advanced" or a scene-handle toggle).
        internal static bool PillToggle(Rect rect, bool value, string label, string tooltip)
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = value ? OnColor : OffColor;
            bool clicked = GUI.Button(rect, new GUIContent(label, tooltip), Pill);
            GUI.backgroundColor = prev;
            return clicked ? !value : value;
        }

        // A property row with a scene-handle toggle button on its right end.
        //
        // The button is the package's own feature, not an SDK field: it switches the matching
        // Scene-view handle on and off, so the inspector row for Radius is also where you turn
        // the Radius handle on. Green = that handle is currently drawn.
        internal static void PropertyRowWithHandleToggle(SerializedProperty property, GUIContent label,
            Func<bool> getHandle, Action<bool> setHandle, string handleName)
        {
            if (property == null) return;

            const float toggleWidth = 22f;
            Rect row = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            Rect fieldRect = new Rect(row.x, row.y, row.width - toggleWidth - 2f, row.height);
            Rect toggleRect = new Rect(row.xMax - toggleWidth, row.y, toggleWidth, row.height);

            EditorGUI.PropertyField(fieldRect, property, label);

            bool on = getHandle();
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = on ? OnColor : OffColor;
            string tip = on
                ? $"The {handleName} scene handle is on. Click to turn it off."
                : $"The {handleName} scene handle is off. Click to draw it in the Scene view.";
            if (GUI.Button(toggleRect, new GUIContent("✐", tip), Pill))
                setHandle(!on);
            GUI.backgroundColor = prev;
        }

        // An enum/dropdown row with a pill on the right, for "Shape [Capsule] (Outside Bounds)".
        internal static void PropertyRowWithPill(SerializedProperty property, GUIContent label,
            SerializedProperty pillProperty, Func<bool, string> pillLabel, string pillTooltip)
        {
            if (property == null) return;
            if (pillProperty == null)
            {
                EditorGUILayout.PropertyField(property, label);
                return;
            }

            const float pillWidth = 110f;
            Rect row = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            Rect fieldRect = new Rect(row.x, row.y, row.width - pillWidth - 4f, row.height);
            Rect pillRect = new Rect(row.xMax - pillWidth, row.y, pillWidth, row.height);

            EditorGUI.PropertyField(fieldRect, property, label);
            PillToggle(pillRect, pillProperty, pillLabel(pillProperty.boolValue), pillTooltip);
        }

        // Draws a property if it exists; silently skips it if a future SDK dropped or renamed
        // the field. Every field in the custom inspectors goes through this, which is what
        // keeps an SDK change from blanking the whole component.
        internal static void Optional(SerializedProperty property, string label = null)
        {
            if (property == null) return;
            if (label == null)
                EditorGUILayout.PropertyField(property, true);
            else
                EditorGUILayout.PropertyField(property, new GUIContent(label), true);
        }

        internal static void HelpRow(string message)
        {
            EditorGUILayout.HelpBox(message, MessageType.None);
        }
    }
}
#endif
