#if PBHANDLES_VRCSDK_PRESENT
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Grab and pose testing: drag a bone in the Scene view while the preview runs, exactly as a
    // player's hand would, and optionally let go leaving it posed.
    //
    // Why this matters more than it sounds: allowGrabbing / allowPosing / maxStretch / stiffness
    // only reveal their real behaviour under a grab, and the two failure modes people actually
    // ship - a chain that stretches to absurd length when tugged, and a pose that snaps back
    // instantly or never returns - are invisible until something pulls on the bone. Before this,
    // the only way to see it was to upload and test in VRChat.
    //
    // The grab is the SDK's own, not an imitation:
    //
    //   * PhysBoneManager.AttemptGrab(grabberId, chainId, bone) creates a real Grab and runs it
    //     through the same IsGrabAllowed / grabFilter checks a player's hand would hit. If the
    //     component has grabbing switched off, the grab is refused here too - which is the
    //     correct answer, and worth surfacing rather than hiding.
    //   * Grab.GlobalPosition is where the "hand" is. UpdateGrabs() copies it into the chain at
    //     the start of every solve, so moving it between steps is all a drag needs to be.
    //   * ReleaseGrab(grab, createPose: true) is what the game does when you let go while posing
    //     is enabled. Passing false is a plain release.
    //
    // A grabber id of -1 is used throughout: that is "no player", which is what the SDK itself
    // uses for non-networked interaction, and it keeps InteractAllowed from consulting player
    // filters that have no meaning in the editor.
    internal static class PhysBonePreviewGrab
    {
        private const int GrabberId = -1;

        private sealed class ActiveGrab
        {
            public VRCPhysBone bone;
            public PhysBoneManager.Grab grab;
            public int boneIndex;
            public Vector3 worldPosition;
        }

        private static readonly List<ActiveGrab> _grabs = new List<ActiveGrab>();

        // Set by the overlay. When on, clicking a bone in the Scene view grabs it instead of
        // selecting the object underneath.
        internal static bool Enabled;

        // Whether releasing leaves the chain posed. Matches the in-game behaviour of letting go
        // of a bone on an avatar whose posing is enabled.
        internal static bool PoseOnRelease;

        internal static int ActiveCount => _grabs.Count;

        internal static bool IsGrabbing(VRCPhysBone pb)
        {
            for (int i = 0; i < _grabs.Count; i++)
            {
                if (_grabs[i].bone == pb)
                    return true;
            }
            return false;
        }

        // Try to grab the bone at `boneIndex` of `pb`. Returns false when the SDK refuses -
        // almost always because allowGrabbing is off, which is reported rather than swallowed so
        // the user doesn't think the tool is broken when it is reporting the truth.
        internal static bool TryGrab(PhysBoneManager manager, VRCPhysBone pb, int boneIndex, Vector3 worldPosition)
        {
            if (manager == null || pb == null)
                return false;
            if (IsGrabbing(pb))
                return true;

            // Checked up front so the refusal can be explained. AttemptGrab would also refuse
            // (via IsGrabAllowed) but returns a bare null, which looks like a broken tool rather
            // than the component's own setting doing exactly what it says.
            if (pb.allowGrabbing == VRCPhysBoneBase.AdvancedBool.False)
            {
                Debug.LogWarning($"[PhysBone Handles] '{pb.name}' has Allow Grabbing off, so it cannot be " +
                    "grabbed - here or in VRChat. Set Allow Grabbing to True to test a tug.");
                return false;
            }

            PhysBoneManager.Grab grab;
            try
            {
                grab = manager.AttemptGrab(GrabberId, pb.chainId, boneIndex);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PhysBone Handles] Grab failed on '{pb.name}'. {e.GetType().Name}: {e.Message}");
                return false;
            }

            if (grab == null)
                return false;

            grab.GlobalPosition = worldPosition;

            _grabs.Add(new ActiveGrab
            {
                bone = pb,
                grab = grab,
                boneIndex = boneIndex,
                worldPosition = worldPosition,
            });
            return true;
        }

        // Move the hand. Called while the user drags; the next solve picks it up through
        // UpdateGrabs().
        internal static void MoveGrab(VRCPhysBone pb, Vector3 worldPosition)
        {
            for (int i = 0; i < _grabs.Count; i++)
            {
                if (_grabs[i].bone != pb)
                    continue;
                _grabs[i].worldPosition = worldPosition;
                _grabs[i].grab.GlobalPosition = worldPosition;
                return;
            }
        }

        internal static void Release(PhysBoneManager manager, VRCPhysBone pb)
        {
            for (int i = _grabs.Count - 1; i >= 0; i--)
            {
                if (_grabs[i].bone != pb)
                    continue;
                ReleaseAt(manager, i);
                return;
            }
        }

        // Drop everything. Called on preview stop, and on any teardown path, because a Grab left
        // registered against a chain the manager no longer has would keep the chain flagged as
        // grabbed for as long as the manager lives.
        internal static void ReleaseAll(PhysBoneManager manager)
        {
            for (int i = _grabs.Count - 1; i >= 0; i--)
                ReleaseAt(manager, i);
            _grabs.Clear();
        }

        private static void ReleaseAt(PhysBoneManager manager, int index)
        {
            ActiveGrab g = _grabs[index];
            _grabs.RemoveAt(index);

            if (manager == null || g.grab == null)
                return;

            try
            {
                // createPose only has an effect when the component allows posing; the SDK checks
                // IsPoseAllowed itself, so passing true on a no-posing component is a plain
                // release rather than an error.
                manager.ReleaseGrab(g.grab, PoseOnRelease, GrabberId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PhysBone Handles] Releasing the grab threw. {e.GetType().Name}: {e.Message}");
            }
        }

        // Clear any pose left behind by a posed release, putting the chain back under physics.
        internal static void ClearPoses(PhysBoneManager manager, IEnumerable<VRCPhysBone> bones)
        {
            if (manager == null)
                return;

            foreach (VRCPhysBone pb in bones)
            {
                if (pb == null || pb.chainId == ChainId.Null)
                    continue;
                try
                {
                    manager.RemovePoseForChain(pb.chainId, false);
                }
                catch (Exception)
                {
                    // A chain with no pose is the common case and not an error.
                }
            }
        }

        // --- scene interaction ------------------------------------------------------------

        // The bone closest to `ray`, across every previewed component. Returns false when
        // nothing is near enough to be a deliberate click.
        internal static bool TryPickBone(Ray ray, IReadOnlyList<VRCPhysBone> bones,
            out VRCPhysBone pickedBone, out int pickedIndex, out Vector3 pickedPosition)
        {
            pickedBone = null;
            pickedIndex = -1;
            pickedPosition = default;

            float bestSqr = float.MaxValue;

            for (int b = 0; b < bones.Count; b++)
            {
                VRCPhysBone pb = bones[b];
                if (pb == null || pb.bones == null)
                    continue;

                for (int i = 0; i < pb.bones.Count; i++)
                {
                    Transform t = pb.bones[i].transform;
                    if (t == null)
                        continue;

                    Vector3 p = t.position;

                    // Distance from the click ray, scaled by how far the bone is from the
                    // camera. Without the scale, a near bone and a far bone that look equally
                    // close to the cursor would not compete fairly.
                    Vector3 toPoint = p - ray.origin;
                    float along = Vector3.Dot(toPoint, ray.direction);
                    if (along <= 0f)
                        continue;

                    float perpSqr = (toPoint - ray.direction * along).sqrMagnitude;
                    float tolerance = HandleUtility.GetHandleSize(p) * 0.18f;
                    if (perpSqr > tolerance * tolerance)
                        continue;

                    // Among candidates within tolerance, nearest to the camera wins - that is
                    // the one the user can see and therefore meant to click.
                    float score = along;
                    if (score < bestSqr)
                    {
                        bestSqr = score;
                        pickedBone = pb;
                        pickedIndex = i;
                        pickedPosition = p;
                    }
                }
            }

            return pickedBone != null;
        }

        // Draw the hand position for every live grab, and the line back to the bone it holds.
        internal static void DrawGrabHandles()
        {
            for (int i = 0; i < _grabs.Count; i++)
            {
                ActiveGrab g = _grabs[i];
                if (g.bone == null)
                    continue;

                Color prev = Handles.color;
                Handles.color = new Color(1f, 0.78f, 0.25f);

                float size = HandleUtility.GetHandleSize(g.worldPosition) * 0.1f;
                Handles.SphereHandleCap(0, g.worldPosition, Quaternion.identity, size, EventType.Repaint);

                if (g.boneIndex >= 0 && g.bone.bones != null && g.boneIndex < g.bone.bones.Count)
                {
                    Transform t = g.bone.bones[g.boneIndex].transform;
                    if (t != null)
                        Handles.DrawDottedLine(g.worldPosition, t.position, 3f);
                }

                Handles.color = prev;
            }
        }

        // Position handle for the currently held bone, so the grab can be dragged in 3D rather
        // than only along the screen plane.
        internal static void DrawDragHandles()
        {
            for (int i = 0; i < _grabs.Count; i++)
            {
                ActiveGrab g = _grabs[i];
                if (g.bone == null)
                    continue;

                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(g.worldPosition, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                    MoveGrab(g.bone, moved);
            }
        }
    }
}
#endif
