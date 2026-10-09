#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // The inspector half of the live preview: a start/stop button, the five animator parameters
    // read live off the solver, and a small 3D viewport of the chain.
    //
    // The parameter readout is the part that changes how people work. _Angle, _Stretch, _Squish,
    // _IsGrabbed and _IsPosed are what a PhysBone exposes to an avatar's animator, and they are
    // what gate everything built on top - a toggle that fires when hair is tugged, an expression
    // driven by stretch. The question "does this tug actually push _Stretch past 0.5" has no
    // answer outside VRChat itself, and guessing wrong means a feature that silently never
    // triggers. Here it is a number on screen that moves while the bone moves.
    //
    // The viewport is a plain Camera rendering to a RenderTexture, framed on the chain. It
    // exists because the Scene view is usually framed on the whole avatar when you are editing
    // one chain, and a close-up that follows the chain saves constant navigation. It is a
    // convenience, not a second simulation - it renders the same bones the Scene view does.
    internal static class PhysBonePreviewInspectorPanel
    {
        private const string PrefsViewport = "OpenSource.PhysBoneHandles.PreviewViewport";

        private static Camera _camera;
        private static RenderTexture _target;

        private static bool ViewportEnabled
        {
            get => EditorPrefs.GetBool(PrefsViewport, false);
            set => EditorPrefs.SetBool(PrefsViewport, value);
        }

        // Orbit state for the viewport, in degrees, plus a zoom multiplier on the auto-framed
        // distance. Static rather than per-editor so the angle survives reselecting the bone.
        private static Vector2 _orbit = new Vector2(25f, -15f);
        private static float _zoom = 1f;

        internal static void Draw(IReadOnlyList<VRCPhysBone> targets)
        {
            if (targets == null || targets.Count == 0)
                return;

            EditorGUILayout.Space(2f);

            bool running = PhysBonePreview.IsRunning;
            bool previewingThese = running && ContainsAny(targets);

            DrawControlRow(targets, running, previewingThese);

            if (previewingThese)
            {
                DrawParameterReadout(targets[0]);

                if (ViewportEnabled)
                    DrawViewport(targets[0]);
            }
        }

        private static bool ContainsAny(IReadOnlyList<VRCPhysBone> targets)
        {
            IReadOnlyList<VRCPhysBone> previewed = PhysBonePreview.PreviewedBones;
            for (int i = 0; i < targets.Count; i++)
            {
                for (int j = 0; j < previewed.Count; j++)
                {
                    if (previewed[j] == targets[i])
                        return true;
                }
            }
            return false;
        }

        private static void DrawControlRow(IReadOnlyList<VRCPhysBone> targets, bool running, bool previewingThese)
        {
            EditorGUILayout.BeginHorizontal();

            Color prev = GUI.backgroundColor;

            if (previewingThese)
            {
                GUI.backgroundColor = InspectorUI.OffColor;
                if (GUILayout.Button(new GUIContent("Stop Live Preview",
                        "Stop the simulation and restore the rest pose. Nothing on your avatar is changed.")))
                {
                    PhysBonePreview.Stop();
                }
            }
            else
            {
                GUI.backgroundColor = InspectorUI.OnColor;
                if (GUILayout.Button(new GUIContent("▶ Live Preview",
                        "Run VRChat's own PhysBone solver on this component in edit mode, so a change " +
                        "to Pull or Spring can be watched settling instead of guessed at. Stopping puts " +
                        "every bone back exactly where it was.")))
                {
                    PhysBonePreview.Start(targets);
                }
            }

            GUI.backgroundColor = ViewportEnabled ? InspectorUI.OnColor : InspectorUI.NeutralColor;
            if (GUILayout.Button(new GUIContent("3D", "Show a close-up viewport of this chain."),
                    GUILayout.Width(34f)))
            {
                ViewportEnabled = !ViewportEnabled;
                if (!ViewportEnabled)
                    ReleaseViewport();
            }

            GUI.backgroundColor = prev;
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawParameterReadout(VRCPhysBone pb)
        {
            PhysBonePreviewReadback.Params p = PhysBonePreviewReadback.Read(pb);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Animator Parameters", EditorStyles.miniBoldLabel);

            Bar("_Angle", p.angle, "What an avatar's _Angle parameter reads right now. 0 is the " +
                "bone at rest, 1 is fully bent against its limit.");
            Bar("_Stretch", p.stretch, "What _Stretch reads. Driven by pulling the chain past its " +
                "resting length - this is the one that needs a grab to move at all.");
            Bar("_Squish", p.squish, "What _Squish reads. Driven by pushing the chain shorter than " +
                "its resting length.");

            EditorGUILayout.BeginHorizontal();
            Flag("_IsGrabbed", p.isGrabbed);
            Flag("_IsPosed", p.isPosed);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            // The values change every solver step, so the inspector has to repaint with it or
            // the numbers would only update when the mouse moves over the window.
            if (PhysBonePreview.IsRunning && !PhysBonePreview.IsPaused)
                EditorWindow.focusedWindow?.Repaint();
        }

        private static void Bar(string label, float value, string tooltip)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect labelRect = new Rect(row.x, row.y, 62f, row.height);
            Rect barRect = new Rect(row.x + 66f, row.y + 2f, row.width - 66f, row.height - 4f);

            EditorGUI.LabelField(labelRect, new GUIContent(label, tooltip), EditorStyles.miniLabel);
            EditorGUI.ProgressBar(barRect, Mathf.Clamp01(value), value.ToString("0.000"));
        }

        private static void Flag(string label, bool on)
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = on ? InspectorUI.OnColor : InspectorUI.NeutralColor;
            GUILayout.Label($"{label}  {(on ? "true" : "false")}", EditorStyles.miniButton);
            GUI.backgroundColor = prev;
        }

        // --- 3D viewport ------------------------------------------------------------------

        private static void DrawViewport(VRCPhysBone pb)
        {
            if (!TryGetChainBounds(pb, out Bounds bounds))
            {
                InspectorUI.HelpRow("Nothing to show yet - this component has no bones under its root.");
                return;
            }

            Rect area = GUILayoutUtility.GetRect(10f, 10000f, 160f, 160f);
            HandleViewportInput(area);

            // Rebuilt whenever the inspector width changes. A RenderTexture cannot be resized in
            // place, and the inspector width changes whenever the window is dragged.
            int width = Mathf.Max((int)area.width, 32);
            int height = Mathf.Max((int)area.height, 32);
            EnsureTarget(width, height);
            EnsureCamera();

            if (_camera == null || _target == null)
                return;

            float radius = Mathf.Max(bounds.extents.magnitude, 0.05f);
            float distance = radius * 3.2f * _zoom;

            Quaternion rotation = Quaternion.Euler(_orbit.y, _orbit.x, 0f);
            _camera.transform.position = bounds.center + rotation * new Vector3(0f, 0f, -distance);
            _camera.transform.rotation = rotation;
            _camera.nearClipPlane = Mathf.Max(distance * 0.01f, 0.001f);
            _camera.farClipPlane = distance * 10f;
            _camera.targetTexture = _target;

            _camera.Render();

            GUI.DrawTexture(area, _target, ScaleMode.StretchToFill, false);

            GUI.Label(new Rect(area.x + 4f, area.yMax - 16f, area.width - 8f, 14f),
                "drag to orbit · scroll to zoom", EditorStyles.whiteMiniLabel);
        }

        private static void HandleViewportInput(Rect area)
        {
            Event e = Event.current;
            if (e == null || !area.Contains(e.mousePosition))
                return;

            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                _orbit.x += e.delta.x;
                _orbit.y = Mathf.Clamp(_orbit.y + e.delta.y, -88f, 88f);
                e.Use();
                GUI.changed = true;
            }
            else if (e.type == EventType.ScrollWheel)
            {
                _zoom = Mathf.Clamp(_zoom * (1f + e.delta.y * 0.05f), 0.25f, 6f);
                e.Use();
                GUI.changed = true;
            }
        }

        // World bounds of everything under the component's root, so the camera frames the chain
        // rather than the whole avatar.
        private static bool TryGetChainBounds(VRCPhysBone pb, out Bounds bounds)
        {
            bounds = default;
            Transform root = PhysBoneChainUtil.RootOf(pb);
            if (root == null)
                return false;

            bool any = false;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (!any)
                {
                    bounds = new Bounds(t.position, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(t.position);
                }
            }
            return any;
        }

        private static void EnsureTarget(int width, int height)
        {
            if (_target != null && _target.width == width && _target.height == height)
                return;

            if (_target != null)
            {
                _target.Release();
                Object.DestroyImmediate(_target);
            }

            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static void EnsureCamera()
        {
            if (_camera != null)
                return;

            GameObject host = new GameObject("PhysBone Preview Viewport Camera")
            {
                // Never saved, never shown in the hierarchy, and torn down with the viewport. A
                // stray camera committed into someone's scene would be a real nuisance.
                hideFlags = HideFlags.HideAndDontSave,
            };

            _camera = host.AddComponent<Camera>();
            _camera.enabled = false;              // Rendered explicitly, never by the game loop.
            _camera.cameraType = CameraType.Preview;
            _camera.fieldOfView = 40f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.17f, 0.18f, 0.20f);
            _camera.renderingPath = RenderingPath.Forward;
            _camera.useOcclusionCulling = false;
            _camera.scene = default;
        }

        private static void ReleaseViewport()
        {
            if (_camera != null)
            {
                Object.DestroyImmediate(_camera.gameObject);
                _camera = null;
            }
            if (_target != null)
            {
                _target.Release();
                Object.DestroyImmediate(_target);
                _target = null;
            }
        }

        [InitializeOnLoadMethod]
        private static void HookCleanup()
        {
            // The camera and render texture are unmanaged-ish resources on a hidden object; a
            // domain reload without this leaks one of each per reload.
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseViewport;
            EditorApplication.quitting += ReleaseViewport;
        }
    }
}
#endif
