#if PBHANDLES_VRCSDK_PRESENT
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // Replacement inspector for VRCPhysBoneCollider.
    //
    // A collider is a short component - shape, root, radius, height, position, rotation - and
    // every one of those except Root has a draggable handle in this package. So the win here
    // isn't grouping (there's little to group), it's that each row becomes the switch for its
    // own handle: click the pencil next to Radius and the radius handle appears on this
    // collider in the Scene view. That removes the trip to the corner toggle panel.
    //
    // Shape and Height interact: Height and Rotation do nothing on a Sphere, so they're hidden
    // for one, the same way the handles themselves skip them. Inside/Outside Bounds rides on
    // the Shape row as a pill, since it reads as a property of the shape.
    //
    // Field names are taken from VRChat's serialized output, and every row goes through
    // InspectorUI.Optional, so an SDK rename costs that one row and nothing else.
    [CustomEditor(typeof(VRCPhysBoneCollider))]
    [CanEditMultipleObjects]
    public class VRCPhysBoneColliderInspector : Editor
    {
        private SerializedProperty P(string name) => serializedObject.FindProperty(name);

        public override void OnInspectorGUI()
        {
            if (!CustomInspectorSettings.Enabled)
            {
                DrawDefaultInspector();
                return;
            }

            serializedObject.Update();

            SerializedProperty shapeType = P("shapeType");
            InspectorUI.PropertyRowWithPill(shapeType, new GUIContent("Shape"),
                P("insideBounds"),
                inside => inside ? "Inside Bounds" : "Outside Bounds",
                "Outside Bounds pushes bones out of the shape (the usual case). Inside Bounds " +
                "keeps them within it.");

            // The "S" (set Root to self) button comes from RootFieldOverlayPatch, which hooks
            // the method PropertyField itself goes through - so drawing our own here would
            // paint a second button on top of the patch's.
            InspectorUI.Optional(P("rootTransform"), "Root");

            // enumValueIndex is -1 on a mixed selection; treat that as "show everything"
            // rather than hiding a field the user might need to set.
            int shape = shapeType != null ? shapeType.enumValueIndex : -1;
            bool hasHeight = shape != 0; // 0 == Sphere in VRCPhysBoneColliderBase.ShapeType

            InspectorUI.PropertyRowWithHandleToggle(P("radius"), new GUIContent("Radius"),
                () => PhysBoneColliderSceneHandles.EditRadius,
                v => PhysBoneColliderSceneHandles.EditRadius = v,
                "Radius");

            if (hasHeight)
                InspectorUI.PropertyRowWithHandleToggle(P("height"), new GUIContent("Height"),
                    () => PhysBoneColliderSceneHandles.EditHeight,
                    v => PhysBoneColliderSceneHandles.EditHeight = v,
                    "Height");

            InspectorUI.PropertyRowWithHandleToggle(P("position"), new GUIContent("Position"),
                () => PhysBoneColliderSceneHandles.EditPosition,
                v => PhysBoneColliderSceneHandles.EditPosition = v,
                "Position");

            if (hasHeight)
                InspectorUI.PropertyRowWithHandleToggle(P("rotation"), new GUIContent("Rotation"),
                    () => PhysBoneColliderSceneHandles.EditRotation,
                    v => PhysBoneColliderSceneHandles.EditRotation = v,
                    "Rotation");

            // Newer SDKs added these; absent on older ones, which Optional handles.
            InspectorUI.Optional(P("bonesAsSpheres"), "Bones As Spheres");
            InspectorUI.Optional(P("globalCollision"), "Global Collision");

            serializedObject.ApplyModifiedProperties();
        }

    }
}
#endif
