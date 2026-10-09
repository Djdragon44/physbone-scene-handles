#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // A throwaway collider you can shove through a chain while it simulates.
    //
    // The use case is specific and common: you want to know whether a chain behaves sanely when
    // something pushes into it - a hand, a prop, a wall - without committing a VRCPhysBoneCollider
    // to the avatar, wiring it into the component's collider list, and then remembering to delete
    // it. This one exists only while the preview runs, is never saved, and is registered directly
    // with the solver rather than added to anyone's list.
    //
    // It is a real VRCPhysBoneCollider on a hidden GameObject, so what it does to the chain is
    // exactly what a permanent collider would do. The only difference is that it is thrown away.
    internal static class PhysBonePreviewTestCollider
    {
        private static GameObject _host;
        private static VRCPhysBoneCollider _collider;

        internal static bool Enabled => _collider != null;

        internal static float Radius = 0.08f;

        // Create the collider and register it with the running solver. Positioned in front of
        // the scene camera so it starts somewhere visible rather than at world zero, which on a
        // typical avatar is inside the feet.
        internal static void Spawn(PhysBoneManager manager, IReadOnlyList<VRCPhysBone> bones)
        {
            if (manager == null || _collider != null)
                return;

            _host = new GameObject("PhysBone Preview Test Collider")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            Vector3 at = Vector3.zero;
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null && view.camera != null)
                at = view.camera.transform.position + view.camera.transform.forward * 1.5f;
            else if (bones.Count > 0 && bones[0] != null)
                at = bones[0].transform.position;

            _host.transform.position = at;

            _collider = _host.AddComponent<VRCPhysBoneCollider>();
            _collider.shapeType = VRCPhysBoneColliderBase.ShapeType.Sphere;
            _collider.radius = Radius;
            _collider.rootTransform = _host.transform;

            _collider.InitShape();
            manager.AddCollider(_collider);

            // The solver only tests a chain against colliders the component lists, so the
            // component lists have to include this one. Done on the live component rather than
            // through SerializedObject so it is never serialized, and undone on despawn.
            for (int i = 0; i < bones.Count; i++)
            {
                VRCPhysBone pb = bones[i];
                if (pb == null) continue;
                if (pb.colliders == null)
                    pb.colliders = new List<VRCPhysBoneColliderBase>();
                pb.colliders.Add(_collider);
                pb.configHasUpdated = true;
            }
        }

        internal static void Despawn(PhysBoneManager manager, IReadOnlyList<VRCPhysBone> bones)
        {
            if (_collider == null)
                return;

            // Pull it back out of every component's list before destroying it, or the lists are
            // left holding a destroyed reference - which would show up as an empty slot in the
            // user's inspector and get saved with the scene.
            for (int i = 0; i < bones.Count; i++)
            {
                VRCPhysBone pb = bones[i];
                if (pb == null || pb.colliders == null) continue;
                pb.colliders.Remove(_collider);
                pb.configHasUpdated = true;
            }

            if (manager != null)
                manager.RemoveCollider(_collider);

            _collider = null;

            if (_host != null)
            {
                Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        // A move handle, so it can be dragged through the chain.
        internal static void DrawHandle()
        {
            if (_host == null)
                return;

            Color prev = Handles.color;
            Handles.color = new Color(0.4f, 0.9f, 1f, 0.8f);
            SceneHandleBatchUtil.DrawSphereWire(_host.transform.position, Quaternion.identity, Radius);
            Handles.color = prev;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(_host.transform.position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
                _host.transform.position = moved;

            if (_collider != null && !Mathf.Approximately(_collider.radius, Radius))
            {
                _collider.radius = Radius;
                _collider.InitShape();
            }
        }
    }
}
#endif
