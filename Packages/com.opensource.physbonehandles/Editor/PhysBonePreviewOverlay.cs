#if PBHANDLES_VRCSDK_PRESENT
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace OpenSource.PhysBoneHandles
{
    // The Scene-view control panel for the live preview: start/stop, pause and frame-step, the
    // refresh rate, the test motion, grab mode and trails.
    //
    // It lives in the Scene view rather than its own window on purpose. Everything it controls
    // is judged by looking at the avatar, so putting the controls anywhere else would mean
    // looking away from the thing being tuned.
    //
    // The panel only appears when a PhysBone is selected or a preview is already running. An
    // always-on panel in the corner of everyone's Scene view would be an imposition on people
    // who installed this package for the handles alone.
    [InitializeOnLoad]
    internal static class PhysBonePreviewOverlay
    {
        private const string PrefsEnabled = "OpenSource.PhysBoneHandles.PreviewOverlay";

        private static readonly int[] RefreshRates = { 72, 90, 120, 144 };
        private static readonly string[] RefreshLabels = { "72", "90", "120", "144" };

        private static readonly List<VRCPhysBone> _selected = new List<VRCPhysBone>();

        // Whether the panel is shown at all. Off hides the panel but does not stop a running
        // preview - stopping is always explicit, so nothing can leave bones displaced silently.
        internal static bool PanelVisible
        {
            get => EditorPrefs.GetBool(PrefsEnabled, true);
            set => EditorPrefs.SetBool(PrefsEnabled, value);
        }

        static PhysBonePreviewOverlay()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        [MenuItem("Tools/PhysBone Handles/Show Live Preview Panel", false, 101)]
        private static void TogglePanel()
        {
            PanelVisible = !PanelVisible;
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/PhysBone Handles/Show Live Preview Panel", true)]
        private static bool TogglePanelValidate()
        {
            Menu.SetChecked("Tools/PhysBone Handles/Show Live Preview Panel", PanelVisible);
            return true;
        }

        private static void CollectSelection()
        {
            _selected.Clear();
            foreach (GameObject go in Selection.gameObjects)
            {
                if (go == null) continue;
                foreach (VRCPhysBone pb in go.GetComponentsInChildren<VRCPhysBone>(true))
                {
                    if (pb != null && !_selected.Contains(pb))
                        _selected.Add(pb);
                }
            }
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!PanelVisible)
                return;

            CollectSelection();

            bool running = PhysBonePreview.IsRunning;
            if (!running && _selected.Count == 0)
                return;

            // Curve markers are worth having with the preview stopped too - the resolved values
            // are a property of the settings, not of the simulation.
            PhysBoneCurveMarkers.Draw(running ? PhysBonePreview.PreviewedBones : _selected);

            if (running)
            {
                PhysBonePreview.DrawBaseline();
                PhysBonePreviewTrail.Draw();
                PhysBonePreviewTestCollider.DrawHandle();
                PhysBonePreviewGrab.DrawGrabHandles();
                if (PhysBonePreviewGrab.Enabled)
                    PhysBonePreviewGrab.DrawDragHandles();

                HandleGrabInput(sceneView);
            }

            DrawPanel(sceneView, running);
        }

        private static void DrawPanel(SceneView sceneView, bool running)
        {
            Handles.BeginGUI();

            const float w = 232f;
            // Grown by the rows that only appear once a motion is picked, so the panel is not
            // padded with empty space in the common case.
            float h = 70f;

            // The curve row is drawn whether or not a preview is running, so its height is
            // added before the running-only rows rather than inside them.
            h += 22f;
            if (PhysBoneCurveMarkers.Current != PhysBoneCurveMarkers.Curve.None)
                h += 28f;

            if (running)
            {
                h += 252f;
                if (PhysBoneTestMotion.Current != PhysBoneTestMotion.Kind.None)
                    h += 40f;
                // The clip picker is always one row; the scrub readout only exists once a clip
                // is actually assigned.
                h += 22f;
                if (PhysBoneClipPlayback.HasClip)
                    h += 22f;
            }
            Rect panel = new Rect(10f, sceneView.position.height - h - 30f, w, h);

            GUILayout.BeginArea(panel, GUIContent.none, EditorStyles.helpBox);

            GUILayout.Label(running ? "Live Preview — running" : "Live Preview", EditorStyles.boldLabel);

            DrawStartStopRow(running);
            DrawCurveMarkerRow();

            if (running)
            {
                DrawTransportRow();
                DrawRateRow();
                DrawMotionRow();
                DrawClipRow();
                DrawToggleRow();
                DrawStatsRow();
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static void DrawStartStopRow(bool running)
        {
            Color prev = GUI.backgroundColor;

            if (running)
            {
                GUI.backgroundColor = InspectorUI.OffColor;
                if (GUILayout.Button(new GUIContent("Stop & Restore",
                        "Stop the simulation and put every bone back exactly where it was.")))
                {
                    PhysBonePreview.Stop();
                }
            }
            else
            {
                GUI.backgroundColor = InspectorUI.OnColor;
                string label = _selected.Count == 1
                    ? "Start Preview (1 bone)"
                    : $"Start Preview ({_selected.Count} bones)";
                if (GUILayout.Button(new GUIContent(label,
                        "Run VRChat's own PhysBone solver on the selected components in edit mode. " +
                        "Stopping restores the rest pose - nothing on your avatar is changed.")))
                {
                    PhysBonePreview.Start(_selected);
                }
            }

            GUI.backgroundColor = prev;
        }

        // Curve sampling markers. Outside the `running` block in DrawPanel on purpose: what a
        // distribution curve resolves to on each bone is a property of the settings, so it is
        // just as useful before you start simulating.
        private static void DrawCurveMarkerRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Curve",
                "Show what the chosen force's distribution curve resolves to on every bone. " +
                "The curve is sampled by depth along each strand, which is the part the " +
                "inspector's little curve thumbnail cannot tell you."),
                GUILayout.Width(46f));

            PhysBoneCurveMarkers.Curve before = PhysBoneCurveMarkers.Current;
            PhysBoneCurveMarkers.Curve after = (PhysBoneCurveMarkers.Curve)EditorGUILayout.Popup(
                (int)before, PhysBoneCurveMarkers.Labels);
            if (after != before)
            {
                PhysBoneCurveMarkers.Current = after;
                SceneView.RepaintAll();
            }

            if (after != PhysBoneCurveMarkers.Curve.None)
            {
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = PhysBoneCurveMarkers.ShowValues
                    ? InspectorUI.OnColor
                    : InspectorUI.NeutralColor;
                if (GUILayout.Button(new GUIContent("#",
                        "Print the resolved number next to each marker."), GUILayout.Width(24f)))
                {
                    PhysBoneCurveMarkers.ShowValues = !PhysBoneCurveMarkers.ShowValues;
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = prev;
            }

            GUILayout.EndHorizontal();

            if (after != PhysBoneCurveMarkers.Curve.None)
            {
                GUILayout.Label("    " + PhysBoneCurveMarkers.Tooltip(after), EditorStyles.miniLabel);
            }
        }

        private static void DrawTransportRow()
        {
            GUILayout.BeginHorizontal();

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = PhysBonePreview.IsPaused ? InspectorUI.OnColor : InspectorUI.NeutralColor;
            if (GUILayout.Button(new GUIContent(PhysBonePreview.IsPaused ? "Resume" : "Pause",
                    "Freeze the simulation where it is, so a single moment can be inspected.")))
            {
                PhysBonePreview.TogglePause();
            }
            GUI.backgroundColor = prev;

            using (new EditorGUI.DisabledScope(!PhysBonePreview.IsPaused))
            {
                if (GUILayout.Button(new GUIContent("Step ▸",
                        "Advance one solver iteration (1/60 s). Frame-stepping a settling bone is " +
                        "how you see exactly where it overshoots.")))
                {
                    PhysBonePreview.StepOnce();
                }
            }

            if (GUILayout.Button(new GUIContent("Reset",
                    "Snap back to the rest pose and keep simulating from there.")))
            {
                PhysBonePreview.ResetToRest();
            }

            GUI.backgroundColor = PhysBonePreview.HasBaseline ? InspectorUI.OnColor : InspectorUI.NeutralColor;
            if (GUILayout.Button(new GUIContent("❄",
                    "Freeze the current pose as a ghost outline, so a settings change can be " +
                    "compared against where the chain used to sit. Click again to clear it."),
                    GUILayout.Width(26f)))
            {
                if (PhysBonePreview.HasBaseline)
                    PhysBonePreview.ClearBaseline();
                else
                    PhysBonePreview.CaptureBaseline();
            }
            GUI.backgroundColor = prev;

            GUILayout.EndHorizontal();
        }

        private static void DrawRateRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Hz",
                "Headset refresh rate to simulate at. VRChat steps PhysBones on a fixed internal " +
                "cadence, so the same bone genuinely behaves differently at 72 and 144."),
                GUILayout.Width(22f));

            int current = 0;
            for (int i = 0; i < RefreshRates.Length; i++)
            {
                if (RefreshRates[i] == PhysBonePreview.RefreshRate)
                    current = i;
            }

            int picked = GUILayout.Toolbar(current, RefreshLabels, EditorStyles.miniButton);
            if (picked != current)
                PhysBonePreview.RefreshRate = RefreshRates[picked];

            GUILayout.EndHorizontal();
        }

        private static void DrawMotionRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Motion",
                "Move the avatar while it simulates. A bone at rest tells you almost nothing - " +
                "Pull and Spring only show their character under movement."),
                GUILayout.Width(46f));

            PhysBoneTestMotion.Kind before = PhysBoneTestMotion.Current;
            PhysBoneTestMotion.Kind after = (PhysBoneTestMotion.Kind)EditorGUILayout.EnumPopup(before);
            if (after != before)
            {
                PhysBoneTestMotion.Current = after;
                PhysBonePreview.RefreshTestMotion();
            }
            GUILayout.EndHorizontal();

            if (PhysBoneTestMotion.Current != PhysBoneTestMotion.Kind.None)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Amount",
                    PhysBoneTestMotion.Tooltip(PhysBoneTestMotion.Current)), GUILayout.Width(46f));
                PhysBoneTestMotion.Strength = GUILayout.HorizontalSlider(
                    PhysBoneTestMotion.Strength, 0.1f, 3f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Speed",
                    "Cycles per second for the repeating motions."), GUILayout.Width(46f));
                PhysBoneTestMotion.Speed = GUILayout.HorizontalSlider(
                    PhysBoneTestMotion.Speed, 0.1f, 2f);
                GUILayout.EndHorizontal();
            }
        }

        // Pick one of the avatar's own AnimationClips to run the chain against. The built-in
        // motions tune forces in the abstract; a real emote is what tells you whether the hair
        // clears the shoulder in the dance you are shipping.
        private static void DrawClipRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Clip",
                "Play one of the avatar's AnimationClips while it simulates. Needs an Animator " +
                "on the avatar root, since a clip's curves are addressed relative to it."),
                GUILayout.Width(46f));

            AnimationClip before = PhysBoneClipPlayback.Clip;
            AnimationClip after = (AnimationClip)EditorGUILayout.ObjectField(
                before, typeof(AnimationClip), false);
            GUILayout.EndHorizontal();

            if (after != before)
            {
                PhysBoneClipPlayback.Clip = after;
                // Re-enter sampling with the new clip, since the preview is already running and
                // Start() is the only other place that would have done it.
                PhysBonePreview.RefreshClipPlayback();
            }

            if (PhysBoneClipPlayback.HasClip)
            {
                GUILayout.Label(
                    $"    {PhysBoneClipPlayback.Time:0.00}s / {PhysBoneClipPlayback.Length:0.00}s" +
                    (PhysBoneClipPlayback.IsActive ? string.Empty : "  (no Animator)"),
                    EditorStyles.miniLabel);
            }
        }

        private static void DrawToggleRow()
        {
            GUILayout.BeginHorizontal();

            Color prev = GUI.backgroundColor;

            GUI.backgroundColor = PhysBonePreviewGrab.Enabled ? InspectorUI.OnColor : InspectorUI.NeutralColor;
            if (GUILayout.Button(new GUIContent("Grab",
                    "Click a bone to grab it, drag the handle to pull, right-click to let go. " +
                    "Uses VRChat's real grab path, so Allow Grabbing and the grab filters apply.")))
            {
                PhysBonePreviewGrab.Enabled = !PhysBonePreviewGrab.Enabled;
            }

            GUI.backgroundColor = PhysBonePreviewGrab.PoseOnRelease ? InspectorUI.OnColor : InspectorUI.NeutralColor;
            if (GUILayout.Button(new GUIContent("Pose",
                    "Letting go leaves the chain posed, the way it does in game when Allow Posing " +
                    "is on. Reset clears it.")))
            {
                PhysBonePreviewGrab.PoseOnRelease = !PhysBonePreviewGrab.PoseOnRelease;
            }

            GUI.backgroundColor = PhysBonePreviewTrail.Enabled ? InspectorUI.OnColor : InspectorUI.NeutralColor;
            if (GUILayout.Button(new GUIContent("Trail",
                    "Draw the path each chain's tip has travelled. The shape of the path is what " +
                    "tells you whether the damping is right.")))
            {
                PhysBonePreviewTrail.Enabled = !PhysBonePreviewTrail.Enabled;
                if (!PhysBonePreviewTrail.Enabled)
                    PhysBonePreviewTrail.Clear();
            }

            GUI.backgroundColor = prev;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = PhysBonePreviewTestCollider.Enabled
                ? InspectorUI.OnColor
                : InspectorUI.NeutralColor;
            if (GUILayout.Button(new GUIContent("Test Collider",
                    "Spawn a throwaway sphere collider you can drag through the chain. It is never " +
                    "saved and is removed when the preview stops, so nothing is added to your avatar.")))
            {
                if (PhysBonePreviewTestCollider.Enabled)
                    PhysBonePreviewTestCollider.Despawn(PhysBonePreview.Manager, PhysBonePreview.PreviewedBones);
                else
                    PhysBonePreviewTestCollider.Spawn(PhysBonePreview.Manager, PhysBonePreview.PreviewedBones);
            }
            GUI.backgroundColor = prev;

            if (PhysBonePreviewTestCollider.Enabled)
            {
                PhysBonePreviewTestCollider.Radius = GUILayout.HorizontalSlider(
                    PhysBonePreviewTestCollider.Radius, 0.01f, 0.5f, GUILayout.Width(70f));
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawStatsRow()
        {
            string grabs = PhysBonePreviewGrab.ActiveCount > 0
                ? $"  ·  {PhysBonePreviewGrab.ActiveCount} held"
                : string.Empty;

            GUILayout.Label(
                $"{PhysBonePreview.SimulatedSeconds:0.0}s  ·  {PhysBonePreview.StepCount} steps{grabs}",
                EditorStyles.miniLabel);
        }

        // Click-to-grab. Only intercepts the click when grab mode is on and the click actually
        // landed on a bone - otherwise the event is left alone so normal selection and camera
        // navigation keep working, which matters because this runs on every Scene view.
        private static void HandleGrabInput(SceneView sceneView)
        {
            if (!PhysBonePreviewGrab.Enabled)
                return;

            Event e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (PhysBonePreviewGrab.TryPickBone(ray, PhysBonePreview.PreviewedBones,
                        out VRCPhysBone pb, out int index, out Vector3 position))
                {
                    if (PhysBonePreviewGrab.TryGrab(PhysBonePreview.Manager, pb, index, position))
                        e.Use();
                }
            }
            else if (e.type == EventType.MouseUp && e.button == 1)
            {
                // Right-click releases whatever is held. Left-click-release can't be used: the
                // position handle needs the left button for dragging.
                foreach (VRCPhysBone pb in PhysBonePreview.PreviewedBones)
                {
                    if (PhysBonePreviewGrab.IsGrabbing(pb))
                        PhysBonePreviewGrab.Release(PhysBonePreview.Manager, pb);
                }
                e.Use();
            }
        }
    }
}
#endif
