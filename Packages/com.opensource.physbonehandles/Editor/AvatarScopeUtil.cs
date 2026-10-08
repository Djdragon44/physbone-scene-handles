#if PBHANDLES_VRCSDK_PRESENT
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace OpenSource.PhysBoneHandles
{
    // "Which avatar does this bone belong to?" - used anywhere a tool needs to search the
    // whole avatar but must not reach into a different avatar sitting in the same scene that
    // happens to use the same bone names (very common: two copies of the same base model).
    internal static class AvatarScopeUtil
    {
        // The nearest VRCAvatarDescriptor above `t`, or the topmost parent if there isn't one
        // (a bare test rig with no descriptor yet).
        internal static Transform ScopeRootOf(Transform t)
        {
            VRCAvatarDescriptor descriptor = t.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor != null)
                return descriptor.transform;

            Transform cur = t;
            while (cur.parent != null)
                cur = cur.parent;
            return cur;
        }
    }
}
#endif
