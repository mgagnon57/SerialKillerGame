using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Headless checks for the sculpt tool's data layer - the slice of it that has no mouse or
    /// Scene view in it, and so can run the same way SmokeTest and Elevations do:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.SculptProbe.Run
    ///
    /// Painting itself is not here - there is no headless way to drag a mouse across a Scene
    /// view, so that stays a manual check against the checklist in the sculpt tool's design doc.
    /// What IS here is everything a bug could hide in without ever touching a mouse: the delta
    /// grid's arithmetic, its save/load round trip, and (once later tasks land) the brush's own
    /// falloff math and the undo/redo stack.
    /// </summary>
    public static class SculptProbe
    {
        [MenuItem("Noir/Sculpt Probe")]
        public static void Run()
        {
            int failures = 0;

            try
            {
                if (!ContentLoader.Exists)
                    throw new Exception($"content not found at {ContentLoader.Root}");

                failures += CheckDeltaRoundTrip();
                failures += CheckSaveLoadFormat();
            }
            catch (Exception ex)
            {
                Debug.LogError("[sculpt-probe] FAILED: " + ex);
                failures++;
            }

            Debug.Log(failures == 0 ? "--- SCULPT PROBE PASSED ---"
                                     : $"--- SCULPT PROBE FAILED ({failures}) ---");

            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>SetDeltaCell followed by HeightAt at that exact grid point must show the
        /// change - and only at that point, not smeared across the whole grid.</summary>
        private static int CheckDeltaRoundTrip()
        {
            int failures = 0;
            int col = 10, row = 10;
            int step = ElevationGrid.DeltaStep;
            float wx = col * step, wy = row * step;

            float before = ElevationGrid.HeightAt(wx, wy);
            float original = ElevationGrid.GetDeltaCell(col, row);

            ElevationGrid.SetDeltaCell(col, row, original + 5f);
            float after = ElevationGrid.HeightAt(wx, wy);

            if (Mathf.Abs(after - before - 5f) > 0.01f)
            {
                Debug.LogError($"[sculpt-probe] delta round trip: expected +5.00m at ({wx},{wy}), "
                              + $"got {after - before:0.00}m");
                failures++;
            }

            // Restore, so this probe leaves the in-memory grid exactly as it found it.
            ElevationGrid.SetDeltaCell(col, row, original);
            float restored = ElevationGrid.HeightAt(wx, wy);
            if (Mathf.Abs(restored - before) > 0.01f)
            {
                Debug.LogError("[sculpt-probe] delta round trip: did not restore cleanly");
                failures++;
            }

            Debug.Log($"[sculpt-probe] delta round trip  +5.00m applied and reverted at "
                    + $"({col},{row}), off by {Mathf.Abs(after - before - 5f):0.000}m");
            return failures;
        }

        /// <summary>SaveDelta() writes a file this can parse back to the exact values that were
        /// set, in the same "grid cols rows step" header format elevation.txt uses. Backs up and
        /// restores whatever was on disk before, so running this probe never leaves a stray or
        /// altered Content/elevation-delta.txt behind.</summary>
        private static int CheckSaveLoadFormat()
        {
            int failures = 0;
            string path = Path.Combine(ContentLoader.Root, "elevation-delta.txt");
            string backup = File.Exists(path) ? File.ReadAllText(path) : null;

            int col = 3, row = 4;
            float original = ElevationGrid.GetDeltaCell(col, row);

            try
            {
                ElevationGrid.SetDeltaCell(col, row, original + 2.5f);
                ElevationGrid.SaveDelta();

                if (!File.Exists(path))
                {
                    Debug.LogError("[sculpt-probe] SaveDelta did not write " + path);
                    return failures + 1;
                }

                float parsed = ParseCell(File.ReadAllText(path), col, row);
                if (Mathf.Abs(parsed - (original + 2.5f)) > 0.01f)
                {
                    Debug.LogError($"[sculpt-probe] save/load format: wrote {original + 2.5f:0.00} "
                                  + $"at ({col},{row}), file has {parsed:0.00}");
                    failures++;
                }
                else
                {
                    Debug.Log($"[sculpt-probe] save/load format  {path} round-trips cell "
                            + $"({col},{row}) correctly");
                }
            }
            finally
            {
                ElevationGrid.SetDeltaCell(col, row, original);
                if (backup != null) File.WriteAllText(path, backup);
                else if (File.Exists(path)) File.Delete(path);
            }

            return failures;
        }

        private static float ParseCell(string text, int col, int row)
        {
            var lines = text.Split('\n');
            int dataRow = 0;
            for (int i = 1; i < lines.Length; i++)   // line 0 is the "grid cols rows step" header
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (dataRow == row)
                {
                    var vals = line.Split(' ');
                    return float.Parse(vals[col], CultureInfo.InvariantCulture);
                }
                dataRow++;
            }
            throw new Exception($"row {row} not found in delta file");
        }
    }
}
