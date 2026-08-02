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

        [MenuItem("Noir/Sculpt Terrain")]
        public static void Open() => GetWindow<SculptTerrainWindow>("Sculpt Terrain");

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            _preview = new SculptPreview();
            _undo = new SculptUndoStack();
            _preview.Build();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;

            if (_dirty && EditorUtility.DisplayDialog("Sculpt Terrain",
                    "Save painted height changes to elevation-delta.txt before closing?",
                    "Save", "Discard"))
                ElevationGrid.SaveDelta();

            _preview?.Teardown();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Ground chunks", _preview?.Chunks.Count.ToString() ?? "0");
            _radius = EditorGUILayout.Slider("Radius (m)", _radius, 5f, 120f);
            _strength = EditorGUILayout.Slider("Strength (m/sample)", _strength, 0.02f, 2f);
            EditorGUILayout.HelpBox(
                "Drag with the left mouse button in the Scene view to raise. Hold Shift to lower.",
                MessageType.None);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_undo == null || !_undo.CanUndo))
                    if (GUILayout.Button("Undo")) OnUndoClicked();
                using (new EditorGUI.DisabledScope(_undo == null || !_undo.CanRedo))
                    if (GUILayout.Button("Redo")) OnRedoClicked();
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!_dirty))
                if (GUILayout.Button("Save"))
                {
                    ElevationGrid.SaveDelta();
                    _dirty = false;
                }

            if (GUILayout.Button("Rebuild Preview")) _preview.Build();

            EditorGUILayout.LabelField("Unsaved changes", _dirty ? "yes" : "no");
        }

        private void OnSceneGUI(SceneView view)
        {
            if (_preview?.Root == null) return;

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId);
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

            Handles.color = Color.yellow;
            Handles.DrawWireDisc(hit, Vector3.up, _radius);
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
