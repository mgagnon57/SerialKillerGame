using UnityEditor;
using UnityEngine;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// The sculpt brush itself: an Edit-Mode window that paints height onto the ground preview
    /// it builds when opened. Left-drag in the Scene view raises; hold Shift to lower.
    ///
    /// EDIT MODE ONLY, on purpose - see docs/superpowers/specs/2026-08-01-sculpt-paint-tool-design.md.
    /// It never touches Play mode's MeshCollider (Assets/Noir/Unity/CityCollision.cs); that picks
    /// up whatever gets saved here the next time the world is built.
    ///
    /// Undo/Redo are the window's own buttons, not Unity's global Ctrl+Z/Cmd+Z - see
    /// SculptUndoStack's doc comment for why. This has no automated test: driving a mouse through
    /// SceneView is not something a headless probe can do, so verifying this file means actually
    /// opening it and painting - see Step 2 below.
    /// </summary>
    public sealed class SculptTerrainWindow : EditorWindow
    {
        private SculptPreview _preview;
        private SculptUndoStack _undo;
        private float[,] _strokeBefore;
        private bool _dragging;
        private bool _dirty;
        private float _radius = 20f;
        private float _strength = 0.2f;
        private Vector3 _lastHit;
        private bool _hasLastHit;

        [MenuItem("Noir/Sculpt Terrain")]
        public static void Open() => GetWindow<SculptTerrainWindow>("Sculpt Terrain");

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            _preview = new SculptPreview();
            _undo = new SculptUndoStack();

            // Entering/exiting Play mode triggers a domain reload, which fires OnEnable on this
            // window again. Building here in that case would stack a second full ground mesh
            // (plus PlaceKindTable.Install) on top of the real running village - see the class
            // doc comment. Only build when we're genuinely opening in Edit mode.
            if (!EditorApplication.isPlayingOrWillChangePlaymode) _preview.Build();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;

            // A Play-mode transition also tears this window down via a domain reload; that is
            // not the user closing the window, so don't block the transition on a save dialog.
            // The preview GameObject still has to go, though - otherwise it rides along into
            // the running game exactly like the OnEnable double-build this guards against.
            bool userClosedWindow = !EditorApplication.isPlayingOrWillChangePlaymode;

            if (userClosedWindow && _dirty && EditorUtility.DisplayDialog("Sculpt Terrain",
                    "Save painted height changes to elevation-delta.txt before closing?",
                    "Save", "Discard"))
                ElevationGrid.SaveDelta();

            _preview?.Teardown();
        }

        // ---- readability ----
        //
        // This window is drawn with the EDITOR skin, which is fixed at about 11px and takes no
        // notice of VillageUI.Scale - so it stayed unreadable while the in-game panel grew. The
        // styles below are rebuilt whenever the scale changes and every control is given an
        // explicit height, because a bigger font inside a default-height button is a clipped
        // font. Adjust with the same Ctrl+= / Ctrl+- as the game, or the slider at the foot.
        private GUIStyle _bigLabel, _bigButton;
        private float _styledAt = -1f;

        private void BuildStyles()
        {
            if (_bigLabel != null && Mathf.Approximately(_styledAt, Noir.Unity.VillageUI.Scale)) return;
            _styledAt = Noir.Unity.VillageUI.Scale;

            int size = Mathf.Max(9, Mathf.RoundToInt(12f * _styledAt));
            _bigLabel = new GUIStyle(EditorStyles.label) { fontSize = size, wordWrap = true };
            _bigButton = new GUIStyle(GUI.skin.button) { fontSize = size };

            // EditorGUIUtility drives the LABEL half of every slider and field row. Without
            // these two the numbers grow and their captions do not.
            EditorGUIUtility.labelWidth = 160f * _styledAt;
        }

        private float Row => 20f * Mathf.Max(1f, _styledAt);

        private void OnGUI()
        {
            BuildStyles();

            EditorGUIUtility.labelWidth = 160f * _styledAt;
            var h = GUILayout.Height(Row);

            EditorGUILayout.LabelField("Ground chunks", _preview?.Chunks.Count.ToString() ?? "0",
                                       _bigLabel, h);
            _radius = EditorGUILayout.Slider("Radius (m)", _radius, 5f, 120f, h);
            _strength = EditorGUILayout.Slider("Strength (m/sample)", _strength, 0.02f, 2f, h);

            EditorGUILayout.LabelField(
                "Drag with the left mouse button in the Scene view to raise. Hold Shift to lower.",
                _bigLabel, GUILayout.Height(Row * 2f));

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_undo == null || !_undo.CanUndo))
                    if (GUILayout.Button("Undo", _bigButton, h)) OnUndoClicked();
                using (new EditorGUI.DisabledScope(_undo == null || !_undo.CanRedo))
                    if (GUILayout.Button("Redo", _bigButton, h)) OnRedoClicked();
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!_dirty))
                if (GUILayout.Button("Save", _bigButton, h))
                {
                    ElevationGrid.SaveDelta();
                    _dirty = false;
                }

            if (GUILayout.Button("Rebuild Preview", _bigButton, h)) _preview.Build();

            EditorGUILayout.LabelField("Unsaved changes", _dirty ? "yes" : "no", _bigLabel, h);

            EditorGUILayout.Space();
            float was = Noir.Unity.VillageUI.Scale;
            float now = EditorGUILayout.Slider("Text size", was, 0.8f, 3.0f, h);
            if (!Mathf.Approximately(was, now))
            {
                Noir.Unity.VillageUI.Scale = now;
                UnityEngine.PlayerPrefs.SetFloat("noir.ui.scale", now);
                UnityEngine.PlayerPrefs.Save();
                _bigLabel = null;         // force a rebuild at the new size
                Repaint();
            }
        }

        private void OnSceneGUI(SceneView view)
        {
            if (_preview?.Root == null) return;

            // A Play-mode transition can land an OnSceneGUI call mid-domain-reload; don't
            // raycast or mutate anything against a preview scene that's about to be torn down
            // (or a running game that's about to replace it).
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId);

            // Handles only actually draws on Repaint events - MouseDown/Drag/Up just schedule
            // one via SceneView.RepaintAll() below. Doing the raycast-and-draw here, on every
            // Repaint (which the Scene view sends continuously as the mouse moves, even with no
            // button held), is what makes the cursor disc follow the mouse on hover rather than
            // only while dragging.
            if (e.type == EventType.Repaint)
            {
                Ray hoverRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (Space3D.GroundHit(hoverRay, out Vector3 hoverHit))
                {
                    _lastHit = hoverHit;
                    _hasLastHit = true;
                }

                if (_hasLastHit)
                {
                    Handles.color = Color.yellow;
                    Handles.DrawWireDisc(_lastHit, Vector3.up, _radius);
                }
            }

            if (e.button != 0) return;

            bool isDown = e.type == EventType.MouseDown;
            bool isDrag = e.type == EventType.MouseDrag;
            bool isUp = e.type == EventType.MouseUp;
            if (!isDown && !isDrag && !isUp) return;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Space3D.GroundHit(ray, out Vector3 hit))
            {
                if (isUp) EndStroke();
                return;
            }

            _lastHit = hit;
            _hasLastHit = true;
            SceneView.RepaintAll();

            if (isDown)
            {
                _strokeBefore = ElevationGrid.SnapshotDelta();
                _dragging = true;
                e.Use();
            }
            else if (isDrag && _dragging)
            {
                Paint(hit.x, -hit.z, e.shift);
                e.Use();
            }
            else if (isUp)
            {
                EndStroke();
                e.Use();
            }
        }

        private void Paint(float worldX, float worldY, bool invert)
        {
            var chunks = SculptBrush.OverlappingChunks(worldX, worldY, _radius, _preview.Chunks);
            SculptBrush.Apply(worldX, worldY, _radius, _strength, invert, chunks);
            _dirty = true;
            Repaint();
        }

        private void EndStroke()
        {
            if (!_dragging) return;
            _dragging = false;
            _undo.RecordBeforeStroke(_strokeBefore);
            Repaint();
        }

        private void OnUndoClicked()
        {
            if (!_undo.CanUndo) return;
            var current = ElevationGrid.SnapshotDelta();
            var restore = _undo.Undo(current);
            ElevationGrid.RestoreDelta(restore);
            _preview.Build();
            _dirty = true;
        }

        private void OnRedoClicked()
        {
            if (!_undo.CanRedo) return;
            var current = ElevationGrid.SnapshotDelta();
            var restore = _undo.Redo(current);
            ElevationGrid.RestoreDelta(restore);
            _preview.Build();
            _dirty = true;
        }
    }
}
