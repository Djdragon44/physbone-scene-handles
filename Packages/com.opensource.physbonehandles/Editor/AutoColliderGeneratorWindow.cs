#if PBHANDLES_VRCSDK_PRESENT
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Auto-fits a VRCPhysBoneCollider to each of an avatar's core Humanoid bones (torso + limbs)
    // by looking at which mesh vertices are actually skin-weighted to that bone and fitting a
    // capsule (sphere for the head) to their extents. Uses each bone's bind pose, so it works
    // regardless of the avatar's current pose in the scene.
    //
    // This only knows about Unity's Humanoid rig mapping - it can't find custom jiggle bones
    // (breasts, etc.) that aren't part of that mapping. Use the "Add VRC Phys Bone Collider to
    // Selection" menu command for those instead, then size them with the scene handles.
    public class AutoColliderGeneratorWindow : EditorWindow
    {
        private class BoneResult
        {
            public UnityEngine.HumanBodyBones humanBone;
            public Transform transform;
            public string label;
            public bool isCapsule;
            public int vertexCount;
            public bool hadExistingCollider;
            public bool included;

            // Raw (unmargined) fit, in the bone's local space / units.
            public Vector3 positionLocal;
            public Quaternion rotationLocal;
            public float rawRadiusLocal;
            public float rawHeightLocal;
        }

        private class MeshEntry
        {
            public SkinnedMeshRenderer renderer;
            public bool included;
            public int vertexCount;
        }

        private GameObject avatarRoot;
        private float radiusMargin = 1.15f;
        private float fitPercentile = 0.95f;
        private bool skipExisting = true;
        private Vector2 scroll;
        private Vector2 meshScroll;
        private List<BoneResult> results = new List<BoneResult>();
        private List<MeshEntry> meshEntries = new List<MeshEntry>();
        private string statusMessage = "";

        [MenuItem("Tools/PhysBone Scene Handles/Auto Collider Generator")]
        private static void Open()
        {
            GetWindow<AutoColliderGeneratorWindow>("Auto Collider Generator");
        }

        private void OnEnable()
        {
            if (avatarRoot == null && Selection.activeGameObject != null)
            {
                Animator a = Selection.activeGameObject.GetComponentInParent<Animator>();
                if (a != null && a.isHuman)
                    avatarRoot = a.gameObject;
            }
            if (avatarRoot != null)
                RefreshMeshList();
            SceneView.duringSceneGui += OnPreviewSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnPreviewSceneGUI;
        }

        // Draws exactly what Apply would create - same included/skip-existing filtering, same
        // radiusMargin - without touching any actual components. A distinct color (cyan) from
        // the real collider handles' green, so a preview can't be mistaken for an applied one.
        private void OnPreviewSceneGUI(SceneView sceneView)
        {
            if (results.Count == 0)
                return;

            using (new Handles.DrawingScope(new Color(0.3f, 0.85f, 1f, 0.9f)))
            {
                foreach (BoneResult r in results)
                {
                    if (!r.included || r.transform == null)
                        continue;
                    if (skipExisting && r.hadExistingCollider)
                        continue;

                    Transform t = r.transform;
                    float scale = t.lossyScale.x;
                    Vector3 worldPos = t.TransformPoint(r.positionLocal);
                    Quaternion worldRot = t.rotation * r.rotationLocal;
                    float radius = r.rawRadiusLocal * radiusMargin * scale;

                    if (r.isCapsule)
                        SceneHandleBatchUtil.DrawCapsuleWire(worldPos, worldRot, radius, r.rawHeightLocal * scale);
                    else
                        SceneHandleBatchUtil.DrawSphereWire(worldPos, worldRot, radius);
                }
            }
        }

        // Populates the mesh checklist from every SkinnedMeshRenderer under the avatar, with a
        // best-effort default selection: anything named "body" (there's almost always exactly
        // one), or - if nothing matches that - just the single highest-vertex-count mesh, since
        // clothing/hair/accessories are usually lower-poly than the body itself. Either way,
        // it's just a starting point - the user picks the final set of meshes to fit against.
        private void RefreshMeshList()
        {
            meshEntries.Clear();
            if (avatarRoot == null)
                return;

            SkinnedMeshRenderer[] renderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bool anyNamedBody = false;
            foreach (SkinnedMeshRenderer r in renderers)
            {
                if (r.sharedMesh == null)
                    continue;
                bool looksLikeBody = r.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0;
                anyNamedBody |= looksLikeBody;
                meshEntries.Add(new MeshEntry
                {
                    renderer = r,
                    included = looksLikeBody,
                    vertexCount = r.sharedMesh.vertexCount,
                });
            }

            if (!anyNamedBody && meshEntries.Count > 0)
            {
                MeshEntry biggest = meshEntries[0];
                foreach (MeshEntry m in meshEntries)
                    if (m.vertexCount > biggest.vertexCount)
                        biggest = m;
                biggest.included = true;
            }

            meshEntries.Sort((a, b) => b.vertexCount.CompareTo(a.vertexCount));
        }

        private void DrawMeshList()
        {
            EditorGUILayout.LabelField(new GUIContent("Meshes to fit against",
                "Only checked meshes contribute vertices to the fit. Uncheck clothing/hair/accessories " +
                "so they don't inflate colliders past the actual body - e.g. a flared skirt weighted to " +
                "Hips will otherwise balloon the Hips collider."), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(40)))
                    foreach (MeshEntry m in meshEntries) m.included = true;
                if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(40)))
                    foreach (MeshEntry m in meshEntries) m.included = false;
            }

            meshScroll = EditorGUILayout.BeginScrollView(meshScroll, GUILayout.Height(Mathf.Min(140, meshEntries.Count * 20 + 4)));
            foreach (MeshEntry m in meshEntries)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    m.included = EditorGUILayout.Toggle(m.included, GUILayout.Width(20));
                    GUILayout.Label(m.renderer != null ? m.renderer.name : "<missing>", GUILayout.ExpandWidth(true));
                    GUILayout.Label(m.vertexCount + " verts", GUILayout.Width(70));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Fits a VRCPhysBoneCollider to each core Humanoid bone (Hips/Spine/Chest/arms/legs/etc.) " +
                "using the extents of whichever mesh vertices are actually skin-weighted to that bone. " +
                "Doesn't know about custom bones (breasts, etc.) outside Unity's Humanoid mapping - add " +
                "those separately with the batch-add menu command.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            avatarRoot = (GameObject)EditorGUILayout.ObjectField("Avatar Root", avatarRoot, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                results.Clear();
                RefreshMeshList();
            }

            EditorGUI.BeginChangeCheck();
            radiusMargin = EditorGUILayout.Slider(new GUIContent("Radius Margin", "Multiplier applied to every computed radius, so colliders sit slightly outside the skin instead of exactly on it. Updates the scene preview live."), radiusMargin, 1f, 1.5f);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            EditorGUI.BeginChangeCheck();
            fitPercentile = EditorGUILayout.Slider(new GUIContent("Fit Tightness", "Controls RADIUS only (capsule/sphere length always spans the full joint-to-joint distance regardless of this). How much of each bone's perpendicular vertex spread to cover, before Radius Margin is applied. Lower = tighter/smaller radius that ignores more of the wide/outlier parts (e.g. a flared hip); higher = covers more of the full width."), fitPercentile, 0.5f, 0.99f);
            if (EditorGUI.EndChangeCheck() && results.Count > 0)
                Scan();

            EditorGUI.BeginChangeCheck();
            skipExisting = EditorGUILayout.ToggleLeft(new GUIContent("Skip bones that already have a Collider", "Leaves any bone you've already hand-tuned alone."), skipExisting);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            if (avatarRoot != null)
            {
                EditorGUI.BeginChangeCheck();
                DrawMeshList();
                if (EditorGUI.EndChangeCheck() && results.Count > 0)
                    Scan();
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(avatarRoot == null || meshEntries.TrueForAll(m => !m.included)))
            {
                if (GUILayout.Button("Scan", GUILayout.Height(28)))
                    Scan();
            }

            if (!string.IsNullOrEmpty(statusMessage))
                EditorGUILayout.HelpBox(statusMessage, MessageType.Warning);

            if (results.Count > 0)
            {
                EditorGUILayout.Space();
                DrawResultsHeader();
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
                foreach (BoneResult r in results)
                    DrawResultRow(r);
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();
                if (GUILayout.Button("Apply", GUILayout.Height(32)))
                    Apply();
            }
        }

        private void DrawResultsHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("", GUILayout.Width(20));
            GUILayout.Label("Bone", GUILayout.Width(110));
            GUILayout.Label("Shape", GUILayout.Width(60));
            GUILayout.Label("Verts", GUILayout.Width(50));
            GUILayout.Label("Radius", GUILayout.Width(70));
            GUILayout.Label("Height", GUILayout.Width(70));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawResultRow(BoneResult r)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            r.included = EditorGUILayout.Toggle(r.included, GUILayout.Width(20));
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
            string label = r.hadExistingCollider ? r.label + " *" : r.label;
            GUILayout.Label(new GUIContent(label, r.hadExistingCollider ? "Already has a VRCPhysBoneCollider" : ""), GUILayout.Width(110));
            GUILayout.Label(r.isCapsule ? "Capsule" : "Sphere", GUILayout.Width(60));
            GUILayout.Label(r.vertexCount.ToString(), GUILayout.Width(50));
            GUILayout.Label((r.rawRadiusLocal * radiusMargin).ToString("F4"), GUILayout.Width(70));
            GUILayout.Label(r.isCapsule ? r.rawHeightLocal.ToString("F4") : "-", GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();
        }

        // --- Scanning ------------------------------------------------------

        private struct BoneSpec
        {
            public UnityEngine.HumanBodyBones bone;
            public UnityEngine.HumanBodyBones? childRef;
            public string label;

            public BoneSpec(UnityEngine.HumanBodyBones bone, UnityEngine.HumanBodyBones? childRef, string label)
            {
                this.bone = bone;
                this.childRef = childRef;
                this.label = label;
            }
        }

        private void Scan()
        {
            results.Clear();
            statusMessage = "";

            Animator animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                statusMessage = "Avatar Root needs an Animator with a configured Humanoid Avatar.";
                return;
            }

            List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
            foreach (MeshEntry m in meshEntries)
                if (m.included && m.renderer != null)
                    renderers.Add(m.renderer);
            if (renderers.Count == 0)
            {
                statusMessage = "No meshes checked in the list above - check at least the body mesh.";
                return;
            }

            bool hasUpperChest = animator.GetBoneTransform(UnityEngine.HumanBodyBones.UpperChest) != null;

            List<BoneSpec> specs = new List<BoneSpec>
            {
                new BoneSpec(UnityEngine.HumanBodyBones.Hips, UnityEngine.HumanBodyBones.Spine, "Hips"),
                new BoneSpec(UnityEngine.HumanBodyBones.Spine, UnityEngine.HumanBodyBones.Chest, "Spine"),
                new BoneSpec(UnityEngine.HumanBodyBones.Chest, hasUpperChest ? UnityEngine.HumanBodyBones.UpperChest : UnityEngine.HumanBodyBones.Neck, "Chest"),
            };
            if (hasUpperChest)
                specs.Add(new BoneSpec(UnityEngine.HumanBodyBones.UpperChest, UnityEngine.HumanBodyBones.Neck, "UpperChest"));
            specs.Add(new BoneSpec(UnityEngine.HumanBodyBones.Neck, UnityEngine.HumanBodyBones.Head, "Neck"));
            specs.Add(new BoneSpec(UnityEngine.HumanBodyBones.Head, null, "Head"));

            AddSidedSpecs(specs, UnityEngine.HumanBodyBones.LeftShoulder, UnityEngine.HumanBodyBones.LeftUpperArm, "L Shoulder");
            AddSidedSpecs(specs, UnityEngine.HumanBodyBones.LeftUpperArm, UnityEngine.HumanBodyBones.LeftLowerArm, "L Upper Arm");
            AddSidedSpecs(specs, UnityEngine.HumanBodyBones.LeftLowerArm, UnityEngine.HumanBodyBones.LeftHand, "L Lower Arm");
            AddSidedSpecs(specs, UnityEngine.HumanBodyBones.LeftUpperLeg, UnityEngine.HumanBodyBones.LeftLowerLeg, "L Upper Leg");
            AddSidedSpecs(specs, UnityEngine.HumanBodyBones.LeftLowerLeg, UnityEngine.HumanBodyBones.LeftFoot, "L Lower Leg");

            AddSidedSpecs(specs, UnityEngine.HumanBodyBones.RightShoulder, UnityEngine.HumanBodyBones.RightUpperArm, "R Shoulder");
            AddSidedSpecs(specs, UnityEngine.HumanBodyBones.RightUpperArm, UnityEngine.HumanBodyBones.RightLowerArm, "R Upper Arm");
            AddSidedSpecs(specs, UnityEngine.HumanBodyBones.RightLowerArm, UnityEngine.HumanBodyBones.RightHand, "R Lower Arm");
            AddSidedSpecs(specs, UnityEngine.HumanBodyBones.RightUpperLeg, UnityEngine.HumanBodyBones.RightLowerLeg, "R Upper Leg");
            AddSidedSpecs(specs, UnityEngine.HumanBodyBones.RightLowerLeg, UnityEngine.HumanBodyBones.RightFoot, "R Lower Leg");

            foreach (BoneSpec spec in specs)
            {
                Transform bone = animator.GetBoneTransform(spec.bone);
                if (bone == null)
                    continue;
                Transform childRef = spec.childRef.HasValue ? animator.GetBoneTransform(spec.childRef.Value) : null;

                BoneResult result = ComputeBoneCollider(spec.bone, bone, childRef, renderers, spec.label, fitPercentile);
                if (result != null)
                {
                    result.hadExistingCollider = bone.GetComponent<VRCPhysBoneCollider>() != null;
                    result.included = !(skipExisting && result.hadExistingCollider);
                    results.Add(result);
                }
            }

            if (results.Count == 0)
                statusMessage = "Scanned, but couldn't fit any colliders - no skin-weighted vertices found for the target bones (unusual rig?).";

            SceneView.RepaintAll();
        }

        // note: these are separate calls (not a "Left"/"Right" swap helper) so the enum values
        // stay compiler-checked instead of relying on Enum.Parse(string) at runtime.
        private static void AddSidedSpecs(List<BoneSpec> specs, UnityEngine.HumanBodyBones bone, UnityEngine.HumanBodyBones childRef, string label)
            => specs.Add(new BoneSpec(bone, childRef, label));

        private static void GetDominant(BoneWeight bw, out int index, out float weight)
        {
            index = bw.boneIndex0;
            weight = bw.weight0;
            if (bw.weight1 > weight) { index = bw.boneIndex1; weight = bw.weight1; }
            if (bw.weight2 > weight) { index = bw.boneIndex2; weight = bw.weight2; }
            if (bw.weight3 > weight) { index = bw.boneIndex3; weight = bw.weight3; }
        }

        private const float DominantWeightThreshold = 0.6f;

        private static BoneResult ComputeBoneCollider(UnityEngine.HumanBodyBones humanBone, Transform bone, Transform childRef,
            List<SkinnedMeshRenderer> renderers, string label, float fitPercentile)
        {
            List<Vector3> worldPoints = new List<Vector3>();

            foreach (SkinnedMeshRenderer r in renderers)
            {
                Mesh mesh = r.sharedMesh;
                if (mesh == null)
                    continue;
                BoneWeight[] weights = mesh.boneWeights;
                Transform[] bones = r.bones;
                Matrix4x4[] bindposes = mesh.bindposes;
                if (weights == null || weights.Length == 0 || bones == null || bindposes == null)
                    continue;

                HashSet<int> matchIndices = new HashSet<int>();
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] == bone)
                        matchIndices.Add(i);
                if (matchIndices.Count == 0)
                    continue;

                Vector3[] verts = mesh.vertices;
                int n = Mathf.Min(verts.Length, weights.Length);
                for (int vi = 0; vi < n; vi++)
                {
                    GetDominant(weights[vi], out int dominantIndex, out float dominantWeight);
                    if (dominantWeight < DominantWeightThreshold)
                        continue;
                    if (!matchIndices.Contains(dominantIndex))
                        continue;
                    if (dominantIndex < 0 || dominantIndex >= bindposes.Length)
                        continue;

                    // Bind pose maps mesh-local -> that bone's rest-pose local space; from there
                    // the bone's CURRENT transform places it correctly regardless of scene pose.
                    Vector3 boneSpacePos = bindposes[dominantIndex].MultiplyPoint3x4(verts[vi]);
                    worldPoints.Add(bone.TransformPoint(boneSpacePos));
                }
            }

            if (worldPoints.Count < 4)
                return null;

            Vector3 bonePos = bone.position;
            float scale = Mathf.Max(bone.lossyScale.x, 0.0001f);

            if (childRef == null)
            {
                Vector3 centroid = Vector3.zero;
                foreach (Vector3 p in worldPoints)
                    centroid += p;
                centroid /= worldPoints.Count;

                List<float> dists = new List<float>(worldPoints.Count);
                foreach (Vector3 p in worldPoints)
                    dists.Add(Vector3.Distance(p, centroid));
                dists.Sort();
                float radius = Percentile(dists, fitPercentile);

                return new BoneResult
                {
                    humanBone = humanBone,
                    transform = bone,
                    label = label,
                    isCapsule = false,
                    vertexCount = worldPoints.Count,
                    positionLocal = bone.InverseTransformPoint(centroid),
                    rotationLocal = Quaternion.identity,
                    rawRadiusLocal = radius / scale,
                    rawHeightLocal = 0f,
                };
            }
            else
            {
                Vector3 toChild = childRef.position - bonePos;
                float boneToChildDist = toChild.magnitude;
                Vector3 axis = boneToChildDist > 1e-6f ? toChild / boneToChildDist : bone.up;

                // Perpendicular (radius) is still vertex-based: it varies with actual body width
                // and benefits from outlier rejection. Length deliberately ISN'T - it comes
                // straight from the skeleton's own bone-to-child distance below, not from vertex
                // weighting, because rigs with "twist" bones (common on anime/VRoid-style rigs,
                // for shoulder/elbow correction) split dominant weight ownership of the middle of
                // a limb away from the main Humanoid bone, which would otherwise make the fitted
                // length come out far shorter than the limb actually is.
                Vector3 perpSum = Vector3.zero;
                List<Vector3> perpValues = new List<Vector3>(worldPoints.Count);
                foreach (Vector3 p in worldPoints)
                {
                    Vector3 rel = p - bonePos;
                    Vector3 perp = rel - axis * Vector3.Dot(rel, axis);
                    perpValues.Add(perp);
                    perpSum += perp;
                }
                Vector3 perpCentroid = perpSum / worldPoints.Count;

                // Measure radius from the RECENTERED axis, not the bone's raw origin - otherwise
                // an off-center pivot inflates the radius by roughly however far off-center it
                // is. A percentile (instead of the true max) so a handful of stray mis-weighted
                // vertices can't balloon the fit on their own.
                List<float> recenteredPerpDist = new List<float>(perpValues.Count);
                foreach (Vector3 perp in perpValues)
                    recenteredPerpDist.Add((perp - perpCentroid).magnitude);
                recenteredPerpDist.Sort();

                float radius = Percentile(recenteredPerpDist, fitPercentile);

                Vector3 worldCenter = bonePos + axis * (boneToChildDist * 0.5f) + perpCentroid;

                Vector3 axisLocal = bone.InverseTransformDirection(axis).normalized;
                Quaternion rotLocal = Quaternion.FromToRotation(Vector3.up, axisLocal);

                float lengthWorld = boneToChildDist;

                return new BoneResult
                {
                    humanBone = humanBone,
                    transform = bone,
                    label = label,
                    isCapsule = true,
                    vertexCount = worldPoints.Count,
                    positionLocal = bone.InverseTransformPoint(worldCenter),
                    rotationLocal = rotLocal,
                    rawRadiusLocal = radius / scale,
                    rawHeightLocal = lengthWorld / scale,
                };
            }
        }

        // Linear-interpolated percentile of an already-sorted list (0 = min, 1 = max).
        private static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 0)
                return 0f;
            if (sorted.Count == 1)
                return sorted[0];
            float pos = Mathf.Clamp01(p) * (sorted.Count - 1);
            int lo = Mathf.FloorToInt(pos);
            int hi = Mathf.CeilToInt(pos);
            if (lo == hi)
                return sorted[lo];
            float frac = pos - lo;
            return Mathf.Lerp(sorted[lo], sorted[hi], frac);
        }

        // --- Applying --------------------------------------------------------

        private void Apply()
        {
            Undo.SetCurrentGroupName("Auto-Generate PhysBone Colliders");
            int group = Undo.GetCurrentGroup();
            int count = 0;

            foreach (BoneResult r in results)
            {
                if (!r.included)
                    continue;
                if (skipExisting && r.hadExistingCollider)
                    continue;

                VRCPhysBoneCollider collider = r.transform.GetComponent<VRCPhysBoneCollider>();
                if (collider == null)
                    collider = Undo.AddComponent<VRCPhysBoneCollider>(r.transform.gameObject);
                else
                    Undo.RecordObject(collider, "Auto-Generate PhysBone Colliders");

                collider.rootTransform = null;
                collider.shapeType = r.isCapsule ? VRCPhysBoneColliderBase.ShapeType.Capsule : VRCPhysBoneColliderBase.ShapeType.Sphere;
                collider.radius = r.rawRadiusLocal * radiusMargin;
                collider.height = r.rawHeightLocal;
                collider.position = r.positionLocal;
                collider.rotation = r.rotationLocal;
                EditorUtility.SetDirty(collider);
                count++;
            }

            Undo.CollapseUndoOperations(group);
            statusMessage = $"Applied {count} collider(s).";
            Debug.Log($"PhysBone Handles: auto-generated/updated {count} collider(s) on {(avatarRoot != null ? avatarRoot.name : "avatar")}.");
            Scan();
        }
    }
}
#endif
