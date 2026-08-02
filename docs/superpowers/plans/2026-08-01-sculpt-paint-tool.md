# Sculpt/Paint Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An Edit-Mode brush that raises/lowers the visible ground in the Scene view, backed by an
additive delta layer on `ElevationGrid` that never touches the real USGS base data.

**Architecture:** `ElevationGrid` grows a second 71×81 grid (`_delta`), added into `HeightAt`
after the base sample. A new `Noir.Editor` toolset — `SculptPreview` (ground-only Edit-Mode
scene), `SculptBrush` (pure cell + vertex math), `SculptUndoStack` (whole-grid snapshot undo) and
`SculptTerrainWindow` (the `EditorWindow` that wires mouse painting to the other three) — never
rebuilds the world on a stroke; it patches only the `Ground {col},{row}` chunk meshes the brush
touches.

**Tech Stack:** Unity 6000-series editor scripting (`UnityEditor.EditorWindow`,
`SceneView.duringSceneGui`), C#, the project's existing plain-text `Content/` convention.

## Global Constraints

- **Edit Mode only.** No Play-mode painting, no live `MeshCollider` patching — confirmed design
  choice, see `docs/superpowers/specs/2026-08-01-sculpt-paint-tool-design.md`.
- **Base data is immutable.** `elevation.txt` is never written to, resampled, or re-rotated.
- **Delta grid resolution matches the base grid** — 71×81 at the same 30m step. No independent
  finer grid.
- Any new code that crosses the `Noir.Unity` / `Noir.Editor` assembly boundary must be `public`
  — `Assets/Noir/Editor` compiles into its own `Noir.Editor.asmdef` (referencing `Noir.Unity`),
  so `internal` is invisible across it exactly as it would be under the folder-name convention.
- Follow the project's existing doc-comment voice: explain *why*, not *what*, especially for any
  non-obvious constraint (see any file under `Assets/Noir/Unity` or `Assets/Noir/Editor` for the
  house style).
- Headless verification commands follow the project's existing convention (see
  `Assets/Noir/Editor/SmokeTest.cs`, `PlayCheck.cs`):
  `Unity.exe -batchmode -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.<Class>.Run -logFile <log> -quit`
- Per project memory: headless runs must stay silent (no audio) and windows this plan opens for
  manual verification must not be launched maximized — restore/size explicitly. This plan's only
  manual step (Task 5) opens a small utility `EditorWindow`, not a game window, so this mostly
  doesn't apply, but don't maximize the Unity Editor window itself if you resize it.

---

## Task 1: `ElevationGrid` delta layer

**Files:**
- Modify: `Assets/Noir/Unity/ElevationGrid.cs`
- Create: `Assets/Noir/Editor/SculptCheck.cs`

**Interfaces:**
- Produces (used by every later task):
  - `ElevationGrid.HeightAt(float worldX, float worldY)` — unchanged signature, now returns base
    + delta.
  - `ElevationGrid.DeltaCols`, `DeltaRows`, `DeltaStep` : `int` (editor-only, `#if UNITY_EDITOR`)
  - `ElevationGrid.GetDeltaCell(int col, int row)` : `float`
  - `ElevationGrid.SetDeltaCell(int col, int row, float value)` : `void`
  - `ElevationGrid.SnapshotDelta()` : `float[,]` (a copy, safe to mutate)
  - `ElevationGrid.RestoreDelta(float[,] snapshot)` : `void`
  - `ElevationGrid.SaveDelta()` : `void` — writes `Content/elevation-delta.txt`

- [ ] **Step 1: Replace the contents of `Assets/Noir/Unity/ElevationGrid.cs`**

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// How high the real ground is, everywhere - the one function every other height in the
    /// project should be built on top of rather than assuming a flat zero.
    ///
    /// Content/elevation.txt is USGS NED, real metres above sea level, sampled on a regular grid
    /// over the whole map. Height here is reported RELATIVE TO THE CROSSING (Chicago St x Attica
    /// St, 750,1335) rather than absolute ASL, so "ground level" keeps meaning what every other
    /// system already assumes it means at the town centre, and simply stops being a lie
    /// everywhere else. Bilinear-sampled between grid points rather than snapped to the nearest
    /// one, so two buildings six metres apart do not stand on a visible step.
    ///
    /// A SECOND grid, the same shape as the first, layers hand-painted correction on top -
    /// Content/elevation-delta.txt, written only by the sculpt tool
    /// (Assets/Noir/Editor/SculptTerrainWindow.cs). It exists so a specific spot can be fixed
    /// without resampling or overwriting the real measured data underneath it: HeightAt adds the
    /// two together, but only the delta grid is ever written by anything in this project.
    /// </summary>
    public static class ElevationGrid
    {
        private static float[,] _grid;   // [row, col], row 0 = north
        private static float[,] _delta;  // same shape as _grid; all zero until something paints it
        private static int _cols, _rows, _step;
        private static float _baseline;  // raw elevation at the crossing - the new "zero"
        private static bool _loaded;

        private const string DeltaFileName = "elevation-delta.txt";

        /// <summary>Height in metres at a world (village-space) point, relative to the
        /// crossing. Zero if elevation.txt is missing - the flat map this project shipped with
        /// until now, rather than a crash.</summary>
        public static float HeightAt(float worldX, float worldY)
        {
            Load();
            if (_grid == null) return 0f;
            return RawAt(worldX, worldY) - _baseline + DeltaAt(worldX, worldY);
        }

        public static float HeightAt(Vector2 world) => HeightAt(world.x, world.y);

        private static float RawAt(float worldX, float worldY) => Bilinear(_grid, worldX, worldY);

        private static float DeltaAt(float worldX, float worldY) => Bilinear(_delta, worldX, worldY);

        private static float Bilinear(float[,] grid, float worldX, float worldY)
        {
            float gx = worldX / _step;
            float gy = worldY / _step;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, _cols - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(gy), 0, _rows - 1);
            int x1 = Mathf.Min(x0 + 1, _cols - 1);
            int y1 = Mathf.Min(y0 + 1, _rows - 1);

            float tx = Mathf.Clamp01(gx - x0);
            float ty = Mathf.Clamp01(gy - y0);

            float h00 = grid[y0, x0], h10 = grid[y0, x1];
            float h01 = grid[y1, x0], h11 = grid[y1, x1];
            float h0 = Mathf.Lerp(h00, h10, tx);
            float h1 = Mathf.Lerp(h01, h11, tx);
            return Mathf.Lerp(h0, h1, ty);
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            string text;
            try { text = ContentLoader.Read("elevation.txt"); }
            catch { return; }

            int row = 0;
            bool haveHeader = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (!haveHeader)
                {
                    var parts = line.Split(' ');
                    if (parts.Length != 4 || parts[0] != "grid") return;
                    _cols = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    _rows = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    _step = int.Parse(parts[3], CultureInfo.InvariantCulture);
                    _grid = new float[_rows, _cols];
                    haveHeader = true;
                    continue;
                }

                var vals = line.Split(' ');
                for (int col = 0; col < _cols && col < vals.Length; col++)
                    _grid[row, col] = float.Parse(vals[col], CultureInfo.InvariantCulture);
                row++;
            }

            if (row != _rows)
            {
                Debug.LogWarning($"[elevation] expected {_rows} rows, read {row} - ignoring the "
                                + "grid rather than trusting a partial one.");
                _grid = null;
                return;
            }

            _baseline = RawAt(750f, 1335f);
            LoadDelta();
        }

        /// <summary>
        /// The painted correction layer. Same shape as the base grid by construction - it is
        /// always sized _rows x _cols, whatever elevation-delta.txt says, so a shape mismatch is
        /// a warning and a flat layer rather than an array that stops matching HeightAt's own
        /// indexing.
        /// </summary>
        private static void LoadDelta()
        {
            _delta = new float[_rows, _cols];

            string text;
            try { text = ContentLoader.Read(DeltaFileName); }
            catch { return; }   // no delta file yet - flat correction, same fallback as the base grid

            int row = 0;
            bool haveHeader = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (!haveHeader)
                {
                    var parts = line.Split(' ');
                    if (parts.Length != 4 || parts[0] != "grid"
                        || int.Parse(parts[1], CultureInfo.InvariantCulture) != _cols
                        || int.Parse(parts[2], CultureInfo.InvariantCulture) != _rows
                        || int.Parse(parts[3], CultureInfo.InvariantCulture) != _step)
                    {
                        Debug.LogWarning($"[elevation] {DeltaFileName}'s grid shape does not "
                                        + "match elevation.txt - ignoring the delta file.");
                        return;
                    }
                    haveHeader = true;
                    continue;
                }

                var vals = line.Split(' ');
                for (int col = 0; col < _cols && col < vals.Length; col++)
                    _delta[row, col] = float.Parse(vals[col], CultureInfo.InvariantCulture);
                row++;
            }

            if (row != _rows)
            {
                Debug.LogWarning($"[elevation] {DeltaFileName}: expected {_rows} rows, read "
                                + $"{row} - ignoring the delta file.");
                _delta = new float[_rows, _cols];
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only surface for the sculpt tool (Assets/Noir/Editor/SculptTerrainWindow.cs and
        /// its helpers). Nothing at runtime calls any of this - the game only ever reads through
        /// HeightAt.
        /// </summary>

        public static int DeltaCols { get { Load(); return _cols; } }
        public static int DeltaRows { get { Load(); return _rows; } }
        public static int DeltaStep { get { Load(); return _step; } }

        public static float GetDeltaCell(int col, int row)
        {
            Load();
            return _delta[row, col];
        }

        public static void SetDeltaCell(int col, int row, float value)
        {
            Load();
            _delta[row, col] = value;
        }

        /// <summary>A copy of every delta cell - what the sculpt window's undo stack snapshots,
        /// and what SaveDelta and RestoreDelta both work from.</summary>
        public static float[,] SnapshotDelta()
        {
            Load();
            var copy = new float[_rows, _cols];
            Array.Copy(_delta, copy, _delta.Length);
            return copy;
        }

        /// <summary>The undo stack's only write path back into the live grid.</summary>
        public static void RestoreDelta(float[,] snapshot)
        {
            Load();
            Array.Copy(snapshot, _delta, _delta.Length);
        }

        public static void SaveDelta()
        {
            Load();
            var text = new StringBuilder();
            text.Append("grid ").Append(_cols).Append(' ').Append(_rows).Append(' ')
                .Append(_step).Append('\n');

            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _cols; col++)
                {
                    if (col > 0) text.Append(' ');
                    text.Append(_delta[row, col].ToString("0.###", CultureInfo.InvariantCulture));
                }
                text.Append('\n');
            }

            File.WriteAllText(Path.Combine(ContentLoader.Root, DeltaFileName), text.ToString());
        }
#endif
    }
}
```

- [ ] **Step 2: Create `Assets/Noir/Editor/SculptCheck.cs`**

```csharp
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
            var originalDelta = ElevationGrid.SnapshotDelta();
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
```

- [ ] **Step 3: Run the check and verify it passes**

Run: `Unity.exe -batchmode -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SculptCheck.Run -logFile -`
Expected: log ends with `--- SCULPT CHECK PASSED ---`, exit code 0. `Content/elevation-delta.txt`
must not exist afterward (this task's check creates and then deletes it, since it didn't exist
before).

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Unity/ElevationGrid.cs Assets/Noir/Editor/SculptCheck.cs
git commit -m "Add an additive delta layer to ElevationGrid for the sculpt tool"
```

---

## Task 2: `SculptUndoStack`

**Files:**
- Create: `Assets/Noir/Editor/SculptUndoStack.cs`
- Modify: `Assets/Noir/Editor/SculptCheck.cs`

**Interfaces:**
- Consumes: nothing (pure `float[,]` snapshots supplied by the caller)
- Produces: `SculptUndoStack` with `CanUndo`, `CanRedo` : `bool`,
  `RecordBeforeStroke(float[,] snapshotBeforeStroke)` : `void`,
  `Undo(float[,] currentGrid)` : `float[,]`, `Redo(float[,] currentGrid)` : `float[,]`,
  `Clear()` : `void`. Consumed by `SculptTerrainWindow` in Task 5.

- [ ] **Step 1: Create `Assets/Noir/Editor/SculptUndoStack.cs`**

```csharp
using System.Collections.Generic;

namespace Noir.Editor
{
    /// <summary>
    /// Undo/redo for the sculpt tool, as whole-grid snapshots rather than per-cell diffs.
    ///
    /// The grid this undoes is 71x81 floats - about 23KB - so snapshotting all of it on every
    /// stroke costs nothing worth optimising away, and it means undo can never drift from what a
    /// stroke actually did: there is no incremental state to get out of sync.
    ///
    /// Deliberately not Unity's own Undo system, which only tracks UnityEngine.Objects - wiring
    /// this into it would mean wrapping the delta grid in a ScriptableObject purely to get
    /// Undo.RegisterCompleteObjectUndo to see it, for a stack that is simpler and easier to test
    /// on its own, and that cannot collide with the user's own scene-edit undo history sharing
    /// the same Ctrl+Z.
    /// </summary>
    public sealed class SculptUndoStack
    {
        private readonly List<float[,]> _undo = new List<float[,]>();
        private readonly List<float[,]> _redo = new List<float[,]>();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Call BEFORE a stroke changes the grid, with the grid as it stood going in.
        /// Starting a new stroke clears redo - the same rule any undo history follows once you
        /// branch off from the point you rewound to.</summary>
        public void RecordBeforeStroke(float[,] snapshotBeforeStroke)
        {
            _undo.Add(snapshotBeforeStroke);
            _redo.Clear();
        }

        /// <summary>Pops the most recent stroke and returns the grid to restore. Call only when
        /// CanUndo is true.</summary>
        public float[,] Undo(float[,] currentGrid)
        {
            float[,] restore = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(currentGrid);
            return restore;
        }

        /// <summary>Reapplies the most recently undone stroke. Call only when CanRedo is true.</summary>
        public float[,] Redo(float[,] currentGrid)
        {
            float[,] restore = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(currentGrid);
            return restore;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
```

- [ ] **Step 2: Add `CheckUndoStack` to `Assets/Noir/Editor/SculptCheck.cs`**

Add this method to the `SculptCheck` class:

```csharp
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
```

And call it from `Run()`, alongside the existing checks:

```csharp
                failures += CheckDeltaRoundTrip();
                failures += CheckUndoStack();
                failures += CheckSaveFormat(deltaPath);
```

- [ ] **Step 3: Run the check and verify it passes**

Run: `Unity.exe -batchmode -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SculptCheck.Run -logFile -`
Expected: `--- SCULPT CHECK PASSED ---`, exit code 0.

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Editor/SculptUndoStack.cs Assets/Noir/Editor/SculptCheck.cs
git commit -m "Add the sculpt tool's undo/redo stack"
```

---

## Task 3: `SculptBrush`

**Files:**
- Create: `Assets/Noir/Editor/SculptBrush.cs`
- Modify: `Assets/Noir/Editor/SculptCheck.cs`

**Interfaces:**
- Consumes: `ElevationGrid.{DeltaCols,DeltaRows,DeltaStep,GetDeltaCell,SetDeltaCell,HeightAt}`
  (Task 1); `Noir.Unity.MeshChunks.Size : int` (existing, `Assets/Noir/Unity/MeshChunks.cs`)
- Produces:
  - `SculptBrush.OverlappingChunks(float cx, float cy, float radius, IReadOnlyDictionary<(int col, int row), MeshFilter> chunkCache)` : `IEnumerable<MeshFilter>`
  - `SculptBrush.Apply(float cx, float cy, float radius, float strength, bool invert, IEnumerable<MeshFilter> overlappingChunks)` : `void`
  - Both consumed by `SculptTerrainWindow` in Task 5.

- [ ] **Step 1: Create `Assets/Noir/Editor/SculptBrush.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// The brush's own math: which delta cells a stroke touches, and which mesh vertices have to
    /// move because of it. No SceneView, no EditorWindow, no mouse - SculptTerrainWindow is the
    /// only caller, and everything here works from plain world coordinates so it can be checked
    /// headlessly against a hand-built mesh (see SculptCheck.CheckBrushPaint).
    /// </summary>
    public static class SculptBrush
    {
        /// <summary>Which cached Ground chunks a brush centred at (cx, cy) could possibly touch -
        /// including the one-cell margin DeltaStep beyond the brush radius, since a cell's
        /// bilinear neighbours can move a vertex that stands outside the radius itself.</summary>
        public static IEnumerable<MeshFilter> OverlappingChunks(float cx, float cy, float radius,
            IReadOnlyDictionary<(int col, int row), MeshFilter> chunkCache)
        {
            float reach = radius + ElevationGrid.DeltaStep;
            int colFrom = Mathf.FloorToInt((cx - reach) / MeshChunks.Size);
            int colTo = Mathf.FloorToInt((cx + reach) / MeshChunks.Size);
            int rowFrom = Mathf.FloorToInt((cy - reach) / MeshChunks.Size);
            int rowTo = Mathf.FloorToInt((cy + reach) / MeshChunks.Size);

            for (int row = rowFrom; row <= rowTo; row++)
            for (int col = colFrom; col <= colTo; col++)
                if (chunkCache.TryGetValue((col, row), out var mf) && mf != null)
                    yield return mf;
        }

        /// <summary>
        /// One brush application: nudges every delta cell within `radius` of (cx, cy) by
        /// `strength`, weighted by a smooth radial falloff, then patches every vertex of every
        /// given chunk whose (x, y) could have moved - live, without rebuilding anything.
        ///
        /// Costed in vertex fetches, not vertex count: Mesh.vertices copies the whole array on
        /// every call, so each chunk is read once (for the "before" heights) and written once
        /// (after mutating the grid), never per vertex.
        /// </summary>
        public static void Apply(float cx, float cy, float radius, float strength, bool invert,
            IEnumerable<MeshFilter> overlappingChunks)
        {
            float signedStrength = strength * (invert ? -1f : 1f);
            float reach = radius + ElevationGrid.DeltaStep;

            var verts = new Dictionary<MeshFilter, Vector3[]>();
            var before = new Dictionary<MeshFilter, float[]>();

            foreach (var mf in overlappingChunks)
            {
                var v = mf.sharedMesh.vertices;
                var b = new float[v.Length];
                for (int i = 0; i < v.Length; i++)
                    if (InReach(v[i], cx, cy, reach)) b[i] = ElevationGrid.HeightAt(v[i].x, -v[i].z);
                verts[mf] = v;
                before[mf] = b;
            }

            PaintCells(cx, cy, radius, signedStrength);

            foreach (var entry in verts)
            {
                var mf = entry.Key;
                var v = entry.Value;
                var b = before[mf];
                bool touched = false;

                for (int i = 0; i < v.Length; i++)
                {
                    if (!InReach(v[i], cx, cy, reach)) continue;
                    float after = ElevationGrid.HeightAt(v[i].x, -v[i].z);
                    v[i].y += after - b[i];
                    touched = true;
                }

                if (!touched) continue;
                mf.sharedMesh.vertices = v;
                mf.sharedMesh.RecalculateNormals();
                mf.sharedMesh.RecalculateBounds();
            }
        }

        private static bool InReach(Vector3 vertex, float cx, float cy, float reach)
        {
            float dx = vertex.x - cx, dy = -vertex.z - cy;
            return dx * dx + dy * dy <= reach * reach;
        }

        /// <summary>Nudges every delta cell within `radius` of (cx, cy). A smooth falloff -
        /// full strength at the centre, none at the rim - so a wide brush blends into its
        /// neighbours instead of stepping at the edge.</summary>
        private static void PaintCells(float cx, float cy, float radius, float signedStrength)
        {
            int step = ElevationGrid.DeltaStep;
            int colFrom = Mathf.Max(0, Mathf.FloorToInt((cx - radius) / step));
            int colTo = Mathf.Min(ElevationGrid.DeltaCols - 1, Mathf.CeilToInt((cx + radius) / step));
            int rowFrom = Mathf.Max(0, Mathf.FloorToInt((cy - radius) / step));
            int rowTo = Mathf.Min(ElevationGrid.DeltaRows - 1, Mathf.CeilToInt((cy + radius) / step));

            for (int row = rowFrom; row <= rowTo; row++)
            for (int col = colFrom; col <= colTo; col++)
            {
                float dx = col * step - cx, dy = row * step - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > radius) continue;

                float t = 1f - Mathf.SmoothStep(0f, 1f, dist / radius);
                ElevationGrid.SetDeltaCell(col, row, ElevationGrid.GetDeltaCell(col, row) + signedStrength * t);
            }
        }
    }
}
```

- [ ] **Step 2: Add `CheckBrushPaint` to `Assets/Noir/Editor/SculptCheck.cs`**

This builds one throwaway quad by hand — no `SculptPreview` dependency, so this task is testable
on its own. Add:

```csharp
        private static int CheckBrushPaint()
        {
            int failures = 0;
            int col = 1, row = 1;
            float step = ElevationGrid.DeltaStep;
            float wx = col * step, wy = row * step;

            GameObject go = null;
            try
            {
                float before = ElevationGrid.HeightAt(wx, wy);

                var mesh = new Mesh();
                mesh.SetVertices(new[] { new Vector3(wx, before, -wy) });
                mesh.SetIndices(new[] { 0, 0, 0 }, MeshTopology.Triangles, 0);

                go = new GameObject("SculptCheckQuad");
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var chunks = new Dictionary<(int, int), MeshFilter> { [(0, 0)] = mf };
                var overlapping = SculptBrush.OverlappingChunks(wx, wy, 10f, chunks);
                SculptBrush.Apply(wx, wy, 10f, 2f, invert: false, overlapping);

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

                ElevationGrid.SetDeltaCell(col, row, 0f);
                Debug.Log("[sculptcheck] brush paint ok");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            return failures;
        }
```

Add `using System.Collections.Generic;` and `using UnityEngine;` (`UnityEngine` is already
present) to the top of the file, and call the new check from `Run()`:

```csharp
                failures += CheckDeltaRoundTrip();
                failures += CheckUndoStack();
                failures += CheckBrushPaint();
                failures += CheckSaveFormat(deltaPath);
```

- [ ] **Step 3: Run the check and verify it passes**

Run: `Unity.exe -batchmode -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SculptCheck.Run -logFile -`
Expected: `--- SCULPT CHECK PASSED ---`, exit code 0.

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Editor/SculptBrush.cs Assets/Noir/Editor/SculptCheck.cs
git commit -m "Add the sculpt brush's cell and vertex patch math"
```

---

## Task 4: `SculptPreview` (ground-only Edit-Mode scene)

**Files:**
- Modify: `Assets/Noir/Unity/VillageMesh.cs:391` (widen `BuildGround` to `public`)
- Create: `Assets/Noir/Editor/SculptPreview.cs`
- Modify: `Assets/Noir/Editor/SculptCheck.cs`

**Interfaces:**
- Consumes: `VillageMesh.BuildGround(WorldModel world, Transform parent)` (now public);
  `VillageHost.MapFile : string` (existing, public); `ContentLoader`, `VillageParser`,
  `WorldBuilder`, `PlaceKindTable` (existing, `Noir.Core.World` / `Noir.Unity`)
- Produces: `SculptPreview` with `Root : GameObject`, `World : WorldModel`,
  `Chunks : IReadOnlyDictionary<(int col, int row), MeshFilter>`, `Build() : void`,
  `Teardown() : void`. Consumed by `SculptTerrainWindow` in Task 5.

- [ ] **Step 1: Widen `VillageMesh.BuildGround` to `public`**

In `Assets/Noir/Unity/VillageMesh.cs`, change:

```csharp
        private static void BuildGround(WorldModel world, Transform parent)
```

to:

```csharp
        public static void BuildGround(WorldModel world, Transform parent)
```

No other change in that file — every existing caller of `BuildGround` still works unchanged.

- [ ] **Step 2: Create `Assets/Noir/Editor/SculptPreview.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// The ground-only Edit-Mode scene the sculpt window paints on: no city, no buildings, no
    /// traffic, no people - none of that changes shape under a brush, and building it anyway
    /// would be exactly the "regenerate everything on every stroke" cost this tool exists to
    /// avoid. Built from the same VillageMesh.BuildGround every full village build already runs,
    /// so there is exactly one ground mesh generator in the project rather than a second one that
    /// could drift from it.
    /// </summary>
    public sealed class SculptPreview
    {
        public GameObject Root { get; private set; }
        public WorldModel World { get; private set; }

        private readonly Dictionary<(int col, int row), MeshFilter> _chunks =
            new Dictionary<(int col, int row), MeshFilter>();

        public IReadOnlyDictionary<(int col, int row), MeshFilter> Chunks => _chunks;

        /// <summary>Tears down any previous preview and builds a fresh one from the map file on
        /// disk. Also how Undo/Redo apply a restored delta grid - simpler and safer than trying
        /// to re-derive exactly which chunks a multi-stroke undo touched.</summary>
        public void Build()
        {
            Teardown();

            PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
            var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
            World = WorldBuilder.Build(layout);

            Root = new GameObject("SculptPreview");
            VillageMesh.BuildGround(World, Root.transform);

            var ground = Root.transform.Find("Ground");
            foreach (Transform child in ground)
            {
                var mf = child.GetComponent<MeshFilter>();
                if (mf == null) continue;
                if (!TryParseChunkName(child.name, out int col, out int row)) continue;
                _chunks[(col, row)] = mf;
            }

            Debug.Log($"[sculpt] preview built: {World.Width}x{World.Height}, {_chunks.Count} ground chunks.");
        }

        public void Teardown()
        {
            if (Root != null) Object.DestroyImmediate(Root);
            Root = null;
            World = null;
            _chunks.Clear();
        }

        /// <summary>Chunk mesh names are "Ground {col},{row}" (see MeshChunks.Emit) - "Ground"
        /// specifically, not "Surround", which is the same container's unchunked map-edge skirt
        /// and would otherwise collide with a real chunk at (0,0).</summary>
        private static bool TryParseChunkName(string name, out int col, out int row)
        {
            col = row = 0;
            var parts = name.Split(' ');
            if (parts.Length != 2 || parts[0] != "Ground") return false;
            var coords = parts[1].Split(',');
            if (coords.Length != 2) return false;
            return int.TryParse(coords[0], out col) && int.TryParse(coords[1], out row);
        }
    }
}
```

- [ ] **Step 3: Add `CheckPreviewBuild` to `Assets/Noir/Editor/SculptCheck.cs`**

```csharp
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
```

Call it from `Run()`:

```csharp
                failures += CheckDeltaRoundTrip();
                failures += CheckUndoStack();
                failures += CheckBrushPaint();
                failures += CheckPreviewBuild();
                failures += CheckSaveFormat(deltaPath);
```

- [ ] **Step 4: Run the check and verify it passes**

Run: `Unity.exe -batchmode -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SculptCheck.Run -logFile -`
Expected: `--- SCULPT CHECK PASSED ---`, exit code 0. Note this check builds and tears down a real
ground mesh for whatever `VillageHost.MapFile` currently points at (`city.txt`) — expect the log
to also show the `[sculpt] preview built: ...` line with a non-zero chunk count.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Unity/VillageMesh.cs Assets/Noir/Editor/SculptPreview.cs Assets/Noir/Editor/SculptCheck.cs
git commit -m "Add the sculpt tool's ground-only Edit-Mode preview scene"
```

---

## Task 5: `SculptTerrainWindow`

**Files:**
- Create: `Assets/Noir/Editor/SculptTerrainWindow.cs`

**Interfaces:**
- Consumes: `SculptPreview` (Task 4), `SculptBrush` (Task 3), `SculptUndoStack` (Task 2),
  `ElevationGrid.{SnapshotDelta,RestoreDelta,SaveDelta}` (Task 1), `Space3D.GroundHit`
  (existing, `Assets/Noir/Unity/Space3D.cs`)
- Produces: the `Noir/Sculpt Terrain` menu item. Nothing else depends on this task — it is the
  top of the stack.

- [ ] **Step 1: Create `Assets/Noir/Editor/SculptTerrainWindow.cs`**

```csharp
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
```

- [ ] **Step 2: Manual verification (no automated test — this is the interactive layer)**

This cannot be checked headlessly; open the editor and drive it by hand, per project convention
(see the "Look at it before saying it works" project memory — render/observe before claiming a
visual or interactive feature works):

1. In the Unity Editor, `Noir > Sculpt Terrain`. A window opens and the Scene view shows the
   ground for `city.txt` (no buildings, no traffic).
2. Left-drag on the ground in the Scene view. Verify: a yellow wire circle follows the cursor,
   the ground rises smoothly under the brush in real time, and there are no visible frame drops.
3. Hold Shift and drag over the same spot. Verify the ground lowers back down.
4. Click **Undo**. Verify the last stroke reverts and the preview rebuilds. Click **Redo**;
   verify it reapplies.
5. Click **Save**. Verify `Content/elevation-delta.txt` now exists and its header reads
   `grid 71 81 30`.
6. Close the window, reopen it (`Noir > Sculpt Terrain`). Verify the terrain you painted is still
   there — the saved delta reloaded correctly.
7. Run `Noir > Play Check` (`Assets/Noir/Editor/PlayCheck.cs`, existing) and confirm it still
   reports `VERDICT: Play should work.` — the delta file must not break the normal Play path.
8. **Clean up:** if step 5 wrote a real `elevation-delta.txt` you don't want to keep for this
   verification pass, `git checkout -- Content/elevation-delta.txt` (if it already existed) or
   delete it (if it didn't) before committing, so this task's commit doesn't carry throwaway
   test terrain.

- [ ] **Step 3: Commit**

```bash
git add Assets/Noir/Editor/SculptTerrainWindow.cs
git commit -m "Add the Sculpt Terrain editor window: brush painting, undo/redo, save"
```

---

## Self-review notes

- **Spec coverage:** every Stream 2 gate in `docs/superpowers/specs/2026-08-01-sculpt-paint-tool-design.md`
  maps to a task — responsiveness and boundary safety (Task 3's chunk-local patching + clamped
  cell ranges), persistence (Task 1's save/load), undo/redo with an untouched base grid (Tasks 1
  + 2), `ElevationGrid` integration (Task 1's `HeightAt`).
- **Type consistency checked:** `(int col, int row)` tuple keys are used identically in
  `SculptPreview.Chunks`, `SculptBrush.OverlappingChunks`, and `SculptTerrainWindow`; `float[,]`
  is the snapshot type everywhere it crosses a boundary (`ElevationGrid.SnapshotDelta/RestoreDelta`,
  `SculptUndoStack`).
- **No placeholders:** every step above is either runnable code or an explicit, concrete manual
  verification checklist (Task 5, Step 2) — there is no "add tests for the above" left unresolved.
- **Scope:** this plan is Stream 2 only. Texturing (Stream 3) and performance work (Stream 4) are
  explicitly out of scope per the design doc and untouched here.
