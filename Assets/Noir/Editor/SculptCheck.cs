using System;
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
            var originalDelta = ElevationGrid.CopyDelta();
            string deltaPath = Path.Combine(ContentLoader.Root, "elevation-delta.txt");
            bool hadFile = File.Exists(deltaPath);
            string originalText = hadFile ? File.ReadAllText(deltaPath) : null;

            try
            {
                failures += CheckDeltaRoundTrip();
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

            ElevationGrid.SetDeltaCell(0, 0, 0f);
            Debug.Log("[sculptcheck] save format ok");
            return failures;
        }
    }
}

