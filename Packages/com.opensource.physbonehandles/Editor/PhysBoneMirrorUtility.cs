#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace OpenSource.PhysBoneHandles
{
    // "I've dialled in the left arm's colliders, now make the right arm match."
    //
    // Humanoid rigs are near-mirror-symmetric, and bone names carry the side in them
    // (Left/Right, _L/_R, .L/.R). So for each selected component this finds the opposite-side
    // bone by name, copies the settings across, and negates the one axis that has to flip.
    //
    // Mirroring happens in the component's *local* (root-relative) space, not world space.
    // That's the useful behaviour: a mirrored rig's left and right bones already have mirrored
    // world orientations, so copying the local offset straight across and flipping its X puts
    // the collider in the visually mirrored spot on the other limb. Rotation flips the same way
    // (negate the Y and Z terms of the quaternion, the standard reflection across the YZ plane).
    public static class PhysBoneMirrorUtility
    {
        // Name fragment pairs, longest first so "Left"/"Right" is tried before the bare "L"/"R"
        // suffix forms and we don't mangle a bone literally called "LeftLeg" into "RightLeg"
        // twice over.
        private static readonly (string a, string b)[] SidePairs =
        {
            ("Left", "Right"),
            ("left", "right"),
            ("LEFT", "RIGHT"),
        };

        // Suffix/affix forms, matched only as a whole word-ish token so "Leg" doesn't count as
        // containing an "L" side marker.
        private static readonly (string a, string b)[] SideAffixes =
        {
            ("_L", "_R"),
            (".L", ".R"),
            ("-L", "-R"),
            ("_l", "_r"),
            (".l", ".r"),
        };

        [MenuItem("GameObject/PhysBone Handles/Mirror Settings to Other Side", false, 13)]
        private static void MirrorToOtherSide()
        {
            Undo.SetCurrentGroupName("Mirror PhysBone Settings");
            int group = Undo.GetCurrentGroup();

            int mirrored = 0;
            List<string> unmatched = new List<string>();

            foreach (GameObject go in Selection.gameObjects)
            {
                string opposite = OppositeName(go.name);
                if (opposite == null)
                {
                    unmatched.Add($"{go.name} (no Left/Right or _L/_R in the name)");
                    continue;
                }

                GameObject target = FindOppositeObject(go, opposite);
                if (target == null)
                {
                    unmatched.Add($"{go.name} (no \"{opposite}\" found on the same avatar)");
                    continue;
                }

                if (MirrorComponents(go, target))
                    mirrored++;
                else
                    unmatched.Add($"{go.name} (nothing mirrorable on it)");
            }

            Undo.CollapseUndoOperations(group);

            if (unmatched.Count > 0)
            {
                Debug.LogWarning($"PhysBone Handles: mirrored {mirrored} object(s). Skipped:\n  "
                    + string.Join("\n  ", unmatched));
            }
            else
            {
                Debug.Log($"PhysBone Handles: mirrored settings for {mirrored} object(s).");
            }
        }

        [MenuItem("GameObject/PhysBone Handles/Mirror Settings to Other Side", true)]
        private static bool ValidateMirror() => Selection.gameObjects.Length > 0;

        // Swaps the side marker in a bone name, or returns null if there isn't one.
        internal static string OppositeName(string name)
        {
            foreach ((string a, string b) in SidePairs)
            {
                if (name.Contains(a) && !name.Contains(b))
                    return ReplaceFirst(name, a, b);
                if (name.Contains(b) && !name.Contains(a))
                    return ReplaceFirst(name, b, a);
            }

            foreach ((string a, string b) in SideAffixes)
            {
                if (name.EndsWith(a))
                    return name.Substring(0, name.Length - a.Length) + b;
                if (name.EndsWith(b))
                    return name.Substring(0, name.Length - b.Length) + a;
                // Blender-style "Hand_L_001" - marker in the middle, still unambiguous.
                if (name.Contains(a + "_") && !name.Contains(b + "_"))
                    return ReplaceFirst(name, a + "_", b + "_");
                if (name.Contains(b + "_") && !name.Contains(a + "_"))
                    return ReplaceFirst(name, b + "_", a + "_");
            }

            return null;
        }

        private static string ReplaceFirst(string haystack, string find, string replace)
        {
            int i = haystack.IndexOf(find, System.StringComparison.Ordinal);
            return i < 0 ? haystack : haystack.Substring(0, i) + replace + haystack.Substring(i + find.Length);
        }

        // Looks for the opposite-side object within the same avatar, so mirroring doesn't
        // accidentally reach into a different avatar in the same scene that happens to use the
        // same bone names.
        private static GameObject FindOppositeObject(GameObject source, string oppositeName)
        {
            Transform scope = AvatarScopeUtil.ScopeRootOf(source.transform);
            foreach (Transform t in scope.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == oppositeName)
                    return t.gameObject;
            }
            return null;
        }

        private static bool MirrorComponents(GameObject source, GameObject target)
        {
            bool did = false;
            did |= MirrorOne<VRCPhysBoneCollider>(source, target);
            did |= MirrorOne<VRCContactSender>(source, target);
            did |= MirrorOne<VRCContactReceiver>(source, target);
            return did;
        }

        // The copy is done through SerializedObject rather than field-by-field, so every
        // serialized setting comes across - including any the SDK adds in a future version -
        // and then the three that can't be copied verbatim get fixed up:
        //   position / rotation : mirrored across the local YZ plane.
        //   rootTransform       : if it pointed at the source's own transform (the common
        //                         case), it should point at the *target's* own transform, not
        //                         back at the source's bone.
        private static bool MirrorOne<T>(GameObject source, GameObject target) where T : Component
        {
            T src = source.GetComponent<T>();
            if (src == null)
                return false;

            T dst = target.GetComponent<T>();
            if (dst == null)
                dst = Undo.AddComponent<T>(target);

            Undo.RecordObject(dst, "Mirror " + typeof(T).Name);

            SerializedObject srcObj = new SerializedObject(src);
            SerializedObject dstObj = new SerializedObject(dst);

            SerializedProperty it = srcObj.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.propertyPath == "m_Script")
                    continue;

                SerializedProperty dstProp = dstObj.FindProperty(it.propertyPath);
                if (dstProp != null)
                    dstObj.CopyFromSerializedProperty(it);
            }

            SerializedProperty pos = dstObj.FindProperty("position");
            if (pos != null && pos.propertyType == SerializedPropertyType.Vector3)
                pos.vector3Value = MirrorPosition(pos.vector3Value);

            SerializedProperty rot = dstObj.FindProperty("rotation");
            if (rot != null && rot.propertyType == SerializedPropertyType.Quaternion)
                rot.quaternionValue = MirrorRotation(rot.quaternionValue);

            SerializedProperty root = dstObj.FindProperty("rootTransform");
            if (root != null && root.propertyType == SerializedPropertyType.ObjectReference
                && (Object)root.objectReferenceValue == source.transform)
            {
                root.objectReferenceValue = target.transform;
            }

            dstObj.ApplyModifiedProperties();
            EditorUtility.SetDirty(dst);
            return true;
        }

        // Reflection across the local YZ plane.
        private static Vector3 MirrorPosition(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        // The quaternion form of the same reflection: a rotation R becomes M*R*M for the
        // mirror M, which for the X axis reduces to negating the y and z components.
        private static Quaternion MirrorRotation(Quaternion q)
        {
            Quaternion m = new Quaternion(q.x, -q.y, -q.z, q.w);
            float mag = Mathf.Sqrt(m.x * m.x + m.y * m.y + m.z * m.z + m.w * m.w);
            return mag < 0.0001f ? Quaternion.identity : new Quaternion(m.x / mag, m.y / mag, m.z / mag, m.w / mag);
        }
    }
}
#endif
