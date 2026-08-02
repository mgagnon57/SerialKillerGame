using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Headless checks for the sculpt tool's non-visual core - the delta layer, the brush's cell
    /// and vertex math, the undo stack, and the preview scene. Nothing here drives a mouse; that
    /// part can only be judged by hand in the Scene view (see SculptTerrainWindow's own doc
    /// comment). Everything else is plain arithmetic and belongs in a probe.
    ///
    /// Every check restores whatever it touched - the in-memory delta grid via
    /// ElevationGrid.RestoreDelta, and Content/elevation-delta.txt via a byte-for-byte rewrite -
    /// so running this leaves the checked-in content and the running editor session exactly as
    /// it found them.
    ///
    /// Run headlessly:
    ///   Unity.exe -batchmode -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SculptCheck.Run -logFile <log> -quit
    /// </summary>
    public static class SculptCheck
    {
        [MenuItem("Noir/Sculpt Check")]
        public static void Run()
        {
            int failures = 0;
            var originalDelta = ElevationGrid.SnapshotDelta();
            string deltaPath = Path.Combine(ContentLoader.Root, "elevation-delta.txt");
            bool hadFile = File.Exists(deltaPath);
            string originalText = hadFile ? File.ReadAllText(deltaPath) : null;

            try
            {
                failures += CheckDeltaRoundTrip();
                failures += CheckUndoStack();
                failures += CheckBrushPaint();
                failures += CheckPreviewBuild();
                failures += CheckSaveFormat(deltaPath);
            }
            catch (Exception ex)
            {
                Debug.LogError("[sculptcheck] FAILED: " + ex);
                failures++;
            }
            finally
            {
                ElevationGrid.RestoreDelta(originalDelta);
                if (hadFile) File.WriteAllText(deltaPath, originalText);
                else if (File.Exists(deltaPath)) File.Delete(deltaPath);
            }

            Debug.Log(failures == 0 ? "--- SCULPT CHECK PASSED ---" : $"--- SCULPT CHECK FAILED ({failures}) ---");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        private static int CheckDeltaRoundTrip()
        {
            int failures = 0;
            int col = Mathf.Min(3, ElevationGrid.DeltaCols - 1);
            int row = Mathf.Min(3, ElevationGrid.DeltaRows - 1);
            float step = ElevationGrid.DeltaStep;

            float baseline = ElevationGrid.HeightAt(col * step, row * step);
            ElevationGrid.SetDeltaCell(col, row, 5f);
            float raised = ElevationGrid.HeightAt(col * step, row * step);

            if (Mathf.Abs(raised - baseline - 5f) > 0.001f)
            {
                Debug.LogError($"[sculptcheck] delta cell: expected +5m, got {raised - baseline:0.###}m");
                failures++;
            }

            ElevationGrid.SetDeltaCell(col, row, 0f);
            float restored = ElevationGrid.HeightAt(col * step, row * step);
            if (Mathf.Abs(restored - baseline) > 0.001f)
            {
                Debug.LogError($"[sculptcheck] delta cell: did not return to baseline, off by {restored - baseline:0.###}m");
                failures++;
            }

            Debug.Log("[sculptcheck] delta round trip ok");
            return failures;
        }

        private static int CheckSaveFormat(string deltaPath)
        {
            int failures = 0;
            ElevationGrid.SetDeltaCell(0, 0, 12.5f);
            ElevationGrid.SaveDelta();

            if (!File.Exists(deltaPath))
            {
                Debug.LogError("[sculptcheck] save: elevation-delta.txt was not written");
                return failures + 1;
            }

            string[] lines = File.ReadAllText(deltaPath).Split('\n');
            string header = lines[0].Trim();
            string expectedHeader = $"grid {ElevationGrid.DeltaCols} {ElevationGrid.DeltaRows} {ElevationGrid.DeltaStep}";
            if (header != expectedHeader)
            {
                Debug.LogError($"[sculptcheck] save: header '{header}' != expected '{expectedHeader}'");
                failures++;
            }

            string[] firstRow = lines[1].Trim().Split(' ');
            if (!float.TryParse(firstRow[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float written)
                || Mathf.Abs(written - 12.5f) > 0.001f)
            {
                Debug.LogError($"[sculptcheck] save: expected cell (0,0) = 12.5, file has '{firstRow[0]}'");
                failures++;
            }

            // The checks above only prove SaveDelta's TEXT is well-formed - not that LoadDelta
            // can parse it back. ReloadFromDisk forces a fresh read of both elevation.txt and
            // elevation-delta.txt (the one-shot _loaded latch would otherwise make every later
            // HeightAt call in this session a no-op read of already-parsed memory), so comparing
            // HeightAt before and after actually exercises the full save/reload round trip that
            // "painted deltas persist on save/reload" depends on.
            float expectedHeight = ElevationGrid.HeightAt(0f, 0f);
            ElevationGrid.ReloadFromDisk();
            float reloadedHeight = ElevationGrid.HeightAt(0f, 0f);
            if (Mathf.Abs(reloadedHeight - expectedHeight) > 0.001f)
            {
                Debug.LogError($"[sculptcheck] save: after ReloadFromDisk, HeightAt(0,0) was "
                    + $"{reloadedHeight:0.###}, expected {expectedHeight:0.###} - the painted "
                    + "delta did not round trip through disk");
                failures++;
            }
            else
            {
                Debug.Log("[sculptcheck] save/reload round trip ok");
            }

            ElevationGrid.SetDeltaCell(0, 0, 0f);
            Debug.Log("[sculptcheck] save format ok");
            return failures;
        }

        private static int CheckUndoStack()
        {
            int failures = 0;
            var stack = new SculptUndoStack();
            var a = new float[,] { { 1f, 2f }, { 3f, 4f } };
            var b = new float[,] { { 9f, 9f }, { 9f, 9f } };

            if (stack.CanUndo || stack.CanRedo)
            {
                Debug.LogError("[sculptcheck] undo: fresh stack should have nothing to undo or redo");
                failures++;
            }

            stack.RecordBeforeStroke(a);
            if (!stack.CanUndo)
            {
                Debug.LogError("[sculptcheck] undo: CanUndo false after a stroke was recorded");
                failures++;
            }

            var restored = stack.Undo(b);
            if (restored != a)
            {
                Debug.LogError("[sculptcheck] undo: did not return the pre-stroke grid");
                failures++;
            }
            if (!stack.CanRedo)
            {
                Debug.LogError("[sculptcheck] undo: CanRedo false immediately after an undo");
                failures++;
            }

            var redone = stack.Redo(a);
            if (redone != b)
            {
                Debug.LogError("[sculptcheck] undo: redo did not return the grid undo replaced");
                failures++;
            }

            stack.RecordBeforeStroke(a);
            if (stack.CanRedo)
            {
                Debug.LogError("[sculptcheck] undo: a new stroke should clear the redo stack");
                failures++;
            }

            Debug.Log("[sculptcheck] undo stack ok");
            return failures;
        }

        private static int CheckBrushPaint()
        {
            int failures = 0;
            int col = 1, row = 1;
            float step = ElevationGrid.DeltaStep;
            float wx = col * step, wy = row * step;
            float radius = 10f;

            // A second point, diagonally offset from the brush centre, sitting in the grid cell
            // diagonally adjacent to (col, row) and close to that cell's far corner. It only
            // picks up a small bilinear sliver of the painted node's delta, but it must pick up
            // SOME - this is the exact case the DeltaStep*sqrt(2) Reach() margin exists for
            // (see SculptBrush.Reach's doc comment), caught in Task 3's review. The plain 1x
            // margin the old (buggy) code used would have excluded this point entirely.
            float diagOffset = step * 29f / 30f;
            float diagWx = wx + diagOffset, diagWy = wy + diagOffset;
            float diagDist = Mathf.Sqrt(diagOffset * diagOffset + diagOffset * diagOffset);
            float oldBuggyMargin = radius + step;
            float correctMargin = radius + step * 1.41421356f;
            if (!(diagDist > oldBuggyMargin && diagDist <= correctMargin))
            {
                Debug.LogError($"[sculptcheck] brush: diagonal test point at {diagDist:0.###}m "
                    + $"is not between the old buggy margin {oldBuggyMargin:0.###}m and the "
                    + $"correct one {correctMargin:0.###}m - fix the test geometry, it no "
                    + "longer proves what it claims to");
                failures++;
            }

            GameObject go = null;
            try
            {
                float before = ElevationGrid.HeightAt(wx, wy);
                float diagBefore = ElevationGrid.HeightAt(diagWx, diagWy);

                var mesh = new Mesh();
                mesh.SetVertices(new[]
                {
                    new Vector3(wx, before, -wy),
                    new Vector3(diagWx, diagBefore, -diagWy),
                });
                mesh.SetIndices(new[] { 0, 0, 0 }, MeshTopology.Triangles, 0);

                go = new GameObject("SculptCheckQuad");
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var chunks = new Dictionary<(int, int), MeshFilter> { [(0, 0)] = mf };
                var overlapping = SculptBrush.OverlappingChunks(wx, wy, radius, chunks);
                SculptBrush.Apply(wx, wy, radius, 2f, invert: false, overlapping);

                float afterCell = ElevationGrid.GetDeltaCell(col, row);
                if (Mathf.Abs(afterCell - 2f) > 0.001f)
                {
                    Debug.LogError($"[sculptcheck] brush: expected cell delta 2m at brush centre, got {afterCell:0.###}m");
                    failures++;
                }

                float afterVertexY = mf.sharedMesh.vertices[0].y;
                if (Mathf.Abs(afterVertexY - before - 2f) > 0.001f)
                {
                    Debug.LogError($"[sculptcheck] brush: expected vertex to rise 2m, rose {afterVertexY - before:0.###}m");
                    failures++;
                }

                float diagAfterY = mf.sharedMesh.vertices[1].y;
                float diagChange = diagAfterY - diagBefore;
                if (diagChange <= 0.0005f)
                {
                    Debug.LogError($"[sculptcheck] brush: diagonal-margin vertex at "
                        + $"{diagDist:0.###}m from brush centre did not rise (rose "
                        + $"{diagChange:0.######}m) - the sqrt(2) Reach() margin regressed");
                    failures++;
                }
                else
                {
                    Debug.Log($"[sculptcheck] brush diagonal-margin vertex rose {diagChange:0.######}m ok");
                }

                ElevationGrid.SetDeltaCell(col, row, 0f);
                Debug.Log("[sculptcheck] brush paint ok");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            return failures;
        }

        private static int CheckPreviewBuild()
        {
            int failures = 0;
            var preview = new SculptPreview();

            try
            {
                preview.Build();

                if (preview.Root == null)
                {
                    Debug.LogError("[sculptcheck] preview: Root is null after Build");
                    failures++;
                }
                if (preview.World == null)
                {
                    Debug.LogError("[sculptcheck] preview: World is null after Build");
                    failures++;
                }
                if (preview.Chunks.Count == 0)
                {
                    Debug.LogError("[sculptcheck] preview: no ground chunks were cached");
                    failures++;
                }

                Debug.Log($"[sculptcheck] preview build ok ({preview.Chunks.Count} chunks)");
            }
            finally
            {
                preview.Teardown();
            }

            if (preview.Root != null)
            {
                Debug.LogError("[sculptcheck] preview: Root survived Teardown");
                failures++;
            }

            return failures;
        }
    }
}
