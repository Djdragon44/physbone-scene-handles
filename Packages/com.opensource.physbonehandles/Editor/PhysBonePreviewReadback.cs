#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEngine;

using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Reads the solver's own per-bone state back out while the preview runs, for the things
    // that can't be worked out from the transform hierarchy alone.
    //
    // Two kinds of state, and only one of them needs the solver's buffers:
    //
    //   * Angle / Stretch / Squish / IsGrabbed / IsPosed. These are the five values a PhysBone
    //     exposes to an avatar's animator, and the reason they matter here is that they drive
    //     animations - "does this hair tug actually push _Stretch past 0.5" is otherwise an
    //     unanswerable question outside of VRChat itself. The SDK already surfaces them as
    //     public properties on the component (Angle, Stretch, Squish, IsGrabbed, IsPosed),
    //     updated every solve, so no buffer walking is needed.
    //
    //   * Per-bone solved endpoints, for tip trails. These do need the chain buffers, reached
    //     through the public GetChains/GetBone/GetTransformData readback API.
    internal static class PhysBonePreviewReadback
    {
        internal struct Params
        {
            public float angle;
            public float stretch;
            public float squish;
            public bool isGrabbed;
            public bool isPosed;
        }

        internal static Params Read(VRCPhysBone pb)
        {
            Params p = default;
            if (pb == null)
                return p;

            // Public properties on VRCPhysBoneBase, written by the solver each step via
            // SetAngle/SetStretch/SetSquish. Reading the component rather than the buffers
            // keeps this working even if the readback API changes shape.
            p.angle = pb.Angle;
            p.stretch = pb.Stretch;
            p.squish = pb.Squish;
            p.isGrabbed = pb.IsGrabbed;
            p.isPosed = pb.IsPosed;
            return p;
        }

        // World-space solved tip of a component's longest strand, for the trail.
        //
        // Taken from the Transforms rather than the solver's own Bone.endPoint buffer. The
        // solver writes its results back onto the real Transforms every step - that is why the
        // handles and the skinned mesh follow the simulation for free - so the Transform is the
        // same number, one indirection later.
        //
        // The buffer route would also work, but Bone.endPoint is a Unity.Mathematics float3,
        // and reading it would make this package depend on com.unity.mathematics for one field.
        // The SDK pulls that package in itself, but our assembly does not reference it, and
        // adding an assembly reference for a trail is a poor trade.
        internal static bool TryReadTip(VRCPhysBone pb, out Vector3 tip)
        {
            tip = default;
            if (pb == null)
                return false;

            List<List<Transform>> chains = PhysBoneChainUtil.BuildChains(pb);
            List<Transform> longest = null;
            for (int i = 0; i < chains.Count; i++)
            {
                if (longest == null || chains[i].Count > longest.Count)
                    longest = chains[i];
            }
            if (longest == null || longest.Count == 0)
                return false;

            Transform last = longest[longest.Count - 1];
            if (last == null)
                return false;

            // Prefer the virtual endpoint when there is one: it is the furthest simulated point
            // and the one whose arc actually shows the bone's behaviour.
            tip = PhysBoneChainUtil.HasEndpoint(pb)
                ? PhysBoneChainUtil.EndpointWorldPosition(pb, last)
                : last.position;
            return true;
        }
    }
}
#endif
