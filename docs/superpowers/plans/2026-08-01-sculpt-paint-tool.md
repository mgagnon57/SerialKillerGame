# Sculpt/Paint Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An Edit-Mode-only in-editor brush that nudges terrain height at specific spots, without ever touching the real USGS elevation data underneath.

**Architecture:** `ElevationGrid` gains a second, additive `float[,]` delta layer (own file, `Content/elevation-delta.txt`) that every `HeightAt` caller in the project picks up automatically. A new `SculptTerrainWindow` EditorWindow builds a throwaway ground-only preview via the existing `VillageMesh.BuildGround`, paints by hooking `SceneView.duringSceneGui`, and patches only the mesh vertices a stroke actually touches — never a full rebuild. The brush's falloff math (`SculptBrush`) and the undo/redo stack (`SculptHistory`) are both pure, Scene-view-free classes so they can be checked headlessly; painting itself cannot be scripted headlessly (no way to drag a mouse across a Scene view in batch mode) and stays a manual check.

**Tech Stack:** C#, Unity 6000.3.20f1 (6.3 LTS), UnityEditor `EditorWindow`/`SceneView`/`Handles` APIs. No new packages.

## Global Constraints

- **Edit Mode only.** No Play-mode integration, no `CityCollision` `MeshCollider` patching — both explicitly out of scope per `docs/superpowers/specs/2026-08-01-sculpt-paint-tool-design.md`.
- **The base elevation grid (`Content/elevation.txt`) is never written to.** Every mutation from this tool goes into the separate delta grid/file. Do not resample or touch `elevation.txt`.
- **Determinism.** No `Random`/`DateTime.Now` anywhere in this plan's code — matches the rest of the project (`Materials3D.Scatter`-style seeded generation), and specifically matters here because the offline snapshot renderer compares builds byte-for-byte.
- **`Assets/Noir/Editor/` compiles into `Noir.Editor` (`Noir.Editor.asmdef`), which already references `Noir.Unity` and the `Noir.Core.*` assemblies** — confirmed by reading the `.asmdef` files directly. Everything this plan adds to `Assets/Noir/Editor` can freely reference `Noir.Unity` types (`ElevationGrid`, `VillageMesh`, `Space3D`, `ContentLoader`, `VillageHost`, `PlaceKindTable`) with no assembly changes needed.
- **Compile-check headlessly before claiming any step done:**
  ```powershell
  & "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" `
    -batchmode -quit -nographics -projectPath C:\SerialKillerGame -logFile C:\SerialKillerGame\.unity-build.log
  ```
  Then check the log for `error CS`:
  ```powershell
  Select-String -Path C:\SerialKillerGame\.unity-build.log -Pattern "error CS"
  ```
  This fails if the Unity Editor is open on this project — close it first. `Unity.exe` forks a child and the launching process returns immediately, so wait on the process (`Wait-Process`/poll `tasklist`), never on the log file appearing.
- **No comments explaining WHAT code does — only WHY**, matching every existing file read for this plan (`ElevationGrid.cs`, `Countryside.cs`, `MeshChunks.cs`, `SmokeTest.cs`).
- **Any probe that writes into `Content/` must leave the working tree exactly as it found it** — back up and restore, matching the project's existing "headless runs must be silent/non-destructive" convention.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `Assets/Noir/Unity/ElevationGrid.cs` | Modify | Adds the additive delta grid: load/save, per-cell get/set, snapshot copy/restore. `HeightAt` sums base + delta. |
| `Assets/Noir/Unity/VillageMesh.cs` | Modify | `BuildGround` becomes a public entry point returning the `GameObject` it builds, so the sculpt window can call it directly instead of duplicating ~260 lines of tile/riser/skirt logic. |
| `Assets/Noir/Editor/SculptHistory.cs` | Create | Pure undo/redo stack of whole-delta-grid snapshots. No `UnityEditor`/Scene-view dependency — testable headlessly. |
| `Assets/Noir/Editor/SculptBrush.cs` | Create | Pure arithmetic: which delta cells a stroke at (x, y, radius) touches, and the smoothstep falloff weight each one gets. Testable headlessly. |
| `Assets/Noir/Editor/SculptTerrainWindow.cs` | Create | The `EditorWindow`: builds/tears down the ground-only preview, hooks `SceneView.duringSceneGui` for painting, patches touched chunk vertices, wires up `ElevationGrid` + `SculptHistory` + `SculptBrush`, GUI controls, save-on-close prompt. |
| `Assets/Noir/Editor/SculptProbe.cs` | Create, then modify twice | Headless verification, `Noir/Sculpt Probe` menu item, runnable via `-executeMethod Noir.Editor.SculptProbe.Run` — matches the existing `SmokeTest.cs`/`Elevations.cs` convention (no NUnit `EditMode` assembly exists in this project for `Noir.Unity`/`Noir.Editor`). Grows across Tasks 1, 3 and 4 as each pure piece lands. |

---

### Task 1: ElevationGrid delta layer

**Files:**
- Modify: `Assets/Noir/Unity/ElevationGrid.cs`
- Create: `Assets/Noir/Editor/SculptProbe.cs`

**Interfaces:**
- Produces: `ElevationGrid.DeltaCols`, `.DeltaRows`, `.DeltaStep` (`int`); `GetDeltaCell(int col, int row) : float`; `SetDeltaCell(int col, int row, float value) : void`; `CopyDelta() : float[,]`; `RestoreDelta(float[,] snapshot) : void`; `SaveDelta() : void`. `HeightAt(float, float)` keeps its existing signature but now includes the delta.
- Consumes: `ContentLoader.Root`, `ContentLoader.Read(string)` (both already exist, unchanged).

- [ ] **Step 1: Write the (uncompilable) probe**

Create `Assets/Noir/Editor/SculptProbe.cs`:

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
```

This will not compile yet — `ElevationGrid.DeltaStep`, `.GetDeltaCell`, `.SetDeltaCell` and `.SaveDelta` don't exist until Step 3.

- [ ] **Step 2: Confirm it fails (compile error)**

Run:
```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" `
  -batchmode -quit -nographics -projectPath C:\SerialKillerGame -logFile C:\SerialKillerGame\.unity-build.log
Select-String -Path C:\SerialKillerGame\.unity-build.log -Pattern "error CS"
```
Expected: matches for `error CS0117` (or similar) naming `DeltaStep`/`GetDeltaCell`/`SetDeltaCell`/`SaveDelta` as missing members of `ElevationGrid`.

- [ ] **Step 3: Implement the delta layer**

Replace the full contents of `Assets/Noir/Unity/ElevationGrid.cs`:

```csharp
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
    /// A SECOND grid, the delta, rides on top - same shape as the base, added in rather than
    /// replacing it. The base float[,] is never written to; every place a human hand needs to
    /// nudge the ground (the sculpt tool, and only the sculpt tool) writes the delta instead, so
    /// the real, measured USGS data underneath stays exactly what it was fetched as.
    /// </summary>
    public static class ElevationGrid
    {
        private static float[,] _grid;   // [row, col], row 0 = north - the real, measured data
        private static float[,] _delta;  // same shape - human-authored, additive, may be null
        private static int _cols, _rows, _step;
        private static float _baseline;  // raw elevation at the crossing - the new "zero"
        private static bool _loaded;

        /// <summary>Height in metres at a world (village-space) point, relative to the
        /// crossing. Zero if elevation.txt is missing - the flat map this project shipped with
        /// until now, rather than a crash.</summary>
        public static float HeightAt(float worldX, float worldY)
        {
            Load();
            if (_grid == null) return 0f;
            float raw = Sample(_grid, worldX, worldY) - _baseline;
            return _delta == null ? raw : raw + Sample(_delta, worldX, worldY);
        }

        public static float HeightAt(Vector2 world) => HeightAt(world.x, world.y);

        // ---------- sculpt tool surface ----------
        //
        // Everything below is read by SculptTerrainWindow and nothing else - runtime code has no
        // reason to touch a single cell or the save path, only the composed HeightAt above.

        public static int DeltaCols { get { Load(); return _cols; } }
        public static int DeltaRows { get { Load(); return _rows; } }
        public static int DeltaStep { get { Load(); return _step; } }

        public static float GetDeltaCell(int col, int row)
        {
            Load();
            if (_delta == null) return 0f;
            col = Mathf.Clamp(col, 0, _cols - 1);
            row = Mathf.Clamp(row, 0, _rows - 1);
            return _delta[row, col];
        }

        public static void SetDeltaCell(int col, int row, float value)
        {
            Load();
            if (_delta == null) return;
            if (col < 0 || col >= _cols || row < 0 || row >= _rows) return;
            _delta[row, col] = value;
        }

        /// <summary>A copy of the whole delta grid, for the sculpt window's undo stack to hold
        /// on to. A copy rather than the live array, so a snapshot from three strokes ago cannot
        /// be mutated by the fourth.</summary>
        public static float[,] CopyDelta()
        {
            Load();
            return _delta == null ? null : (float[,])_delta.Clone();
        }

        /// <summary>Replaces the whole delta grid - how the sculpt window pops a snapshot back
        /// in on undo. The snapshot must be the shape Load() produced; the window only ever
        /// hands back what CopyDelta gave it, so a mismatch means a caller outside the sculpt
        /// window and is ignored rather than trusted.</summary>
        public static void RestoreDelta(float[,] snapshot)
        {
            Load();
            if (_delta == null || snapshot == null) return;
            if (snapshot.GetLength(0) != _rows || snapshot.GetLength(1) != _cols) return;
            _delta = (float[,])snapshot.Clone();
        }

        /// <summary>Writes the delta grid to Content/elevation-delta.txt, in the same text
        /// format elevation.txt itself uses. Explicit, not automatic - the sculpt window calls
        /// this from its own Save button (and its close-with-unsaved-changes prompt).</summary>
        public static void SaveDelta()
        {
            Load();
            if (_delta == null) return;

            var sb = new StringBuilder();
            sb.Append("grid ").Append(_cols).Append(' ').Append(_rows).Append(' ').Append(_step).Append('\n');
            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _cols; col++)
                {
                    if (col > 0) sb.Append(' ');
                    sb.Append(_delta[row, col].ToString("0.###", CultureInfo.InvariantCulture));
                }
                sb.Append('\n');
            }

            File.WriteAllText(Path.Combine(ContentLoader.Root, "elevation-delta.txt"), sb.ToString());
        }

        private static float Sample(float[,] grid, float worldX, float worldY)
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

            _baseline = Sample(_grid, 750f, 1335f);
            LoadDelta();
        }

        /// <summary>Content/elevation-delta.txt, same format as the base grid. Missing file ->
        /// an all-zero delta the same shape as the base - flat until proven otherwise, the same
        /// fallback the base loader itself uses for a missing elevation.txt.</summary>
        private static void LoadDelta()
        {
            _delta = new float[_rows, _cols];

            string text;
            try { text = ContentLoader.Read("elevation-delta.txt"); }
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
                    bool ok = parts.Length == 4 && parts[0] == "grid"
                            && int.TryParse(parts[1], out int cols) && cols == _cols
                            && int.TryParse(parts[2], out int rows) && rows == _rows
                            && int.TryParse(parts[3], out int step) && step == _step;
                    if (!ok)
                    {
                        Debug.LogWarning("[elevation] elevation-delta.txt does not match the base "
                                        + $"grid's {_cols}x{_rows}@{_step}m shape - ignoring it "
                                        + "rather than trusting a stale or partial delta.");
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
                Debug.LogWarning($"[elevation] elevation-delta.txt expected {_rows} rows, read "
                                + $"{row} - ignoring it rather than trusting a partial one.");
                _delta = new float[_rows, _cols];
            }
        }
    }
}
```

Note the private `RawAt` method is renamed/generalized to `Sample(float[,] grid, ...)`, called for both `_grid` and `_delta`. `RawAt` was private and had exactly two call sites, both inside this file (confirmed by project-wide grep) — no other file references it.

- [ ] **Step 4: Confirm it compiles and the probe passes**

Run the same two commands as Step 2. Expected: no `error CS` matches, and:
```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" `
  -batchmode -quit -nographics -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SculptProbe.Run -logFile C:\SerialKillerGame\.unity-probe.log
Select-String -Path C:\SerialKillerGame\.unity-probe.log -Pattern "sculpt-probe"
```
Expected: `--- SCULPT PROBE PASSED ---` and no `error`-tagged lines, and `Content/elevation-delta.txt` absent from `git status` afterward (the probe restores it).

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Unity/ElevationGrid.cs Assets/Noir/Editor/SculptProbe.cs
git commit -m "Add the additive delta layer to ElevationGrid, with a headless probe"
```

---

### Task 2: VillageMesh.BuildGround becomes a public entry point

**Files:**
- Modify: `Assets/Noir/Unity/VillageMesh.cs:391` (signature), `:650` (closing brace / return)

**Interfaces:**
- Produces: `VillageMesh.BuildGround(WorldModel world, Transform parent) : GameObject` (was `private static void`).
- Consumes: nothing new.

No new test file — this is a signature-only change to code every existing `SmokeTest.Run()` invocation already exercises (`VillageMesh.Build` calls `BuildGround` unconditionally), so the existing smoke test is the regression check.

- [ ] **Step 1: Widen the signature and return the built GameObject**

In `Assets/Noir/Unity/VillageMesh.cs`, change:
```csharp
        private static void BuildGround(WorldModel world, Transform parent)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
```
to:
```csharp
        /// <summary>Public so the sculpt tool can build just the ground - without walls, roofs,
        /// props or people - into its own throwaway preview scene, using exactly the geometry
        /// the real village ships.</summary>
        public static GameObject BuildGround(WorldModel world, Transform parent)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
```

And at the end of the method, change:
```csharp
            Debug.Log($"Ground mesh: {chunks.VertexCount + skirt.VertexCount:N0} vertices, "
                    + $"{risers:N0} risers, {tiles.Count} chunks + surround, "
                    + $"{chunks.DrawCalls + skirt.DrawCalls} draw calls.");
        }
```
to:
```csharp
            Debug.Log($"Ground mesh: {chunks.VertexCount + skirt.VertexCount:N0} vertices, "
                    + $"{risers:N0} risers, {tiles.Count} chunks + surround, "
                    + $"{chunks.DrawCalls + skirt.DrawCalls} draw calls.");
            return go;
        }
```

The one existing call site, `BuildGround(world, root.transform);` inside `Build()`, is a statement that already discards its return value — `void` becoming a `GameObject`-returning method with the call left as a bare statement compiles unchanged.

- [ ] **Step 2: Compile-check and run the smoke test**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" `
  -batchmode -quit -nographics -projectPath C:\SerialKillerGame -logFile C:\SerialKillerGame\.unity-build.log
Select-String -Path C:\SerialKillerGame\.unity-build.log -Pattern "error CS"
& "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" `
  -batchmode -quit -nographics -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SmokeTest.Run -logFile C:\SerialKillerGame\.unity-smoke.log
Select-String -Path C:\SerialKillerGame\.unity-smoke.log -Pattern "SMOKE TEST"
```
Expected: no `error CS`, and `--- SMOKE TEST PASSED ---`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Noir/Unity/VillageMesh.cs
git commit -m "Make VillageMesh.BuildGround a public entry point for the sculpt tool"
```

---

### Task 3: SculptHistory (undo/redo)

**Files:**
- Create: `Assets/Noir/Editor/SculptHistory.cs`
- Modify: `Assets/Noir/Editor/SculptProbe.cs`

**Interfaces:**
- Produces: `SculptHistory` (sealed class): `BeginStroke(float[,] before) : void`, `Undo(float[,] current) : float[,]`, `Redo(float[,] current) : float[,]`, `CanUndo`, `CanRedo` (`bool`), `Clear() : void`.
- Consumes: nothing (pure, no dependency on `ElevationGrid` or Unity Editor APIs).

- [ ] **Step 1: Write the failing probe check**

In `Assets/Noir/Editor/SculptProbe.cs`, add `using System.Collections.Generic;` to the top `using` block (needed by Task 4 too — harmless to add now), then add a call and a new method:

```csharp
                failures += CheckDeltaRoundTrip();
                failures += CheckSaveLoadFormat();
                failures += CheckSculptHistory();
```

```csharp
        /// <summary>Undo must return exactly the grid handed to BeginStroke; Redo must return
        /// exactly what Undo took away; starting a new stroke must throw away any redo the user
        /// never took.</summary>
        private static int CheckSculptHistory()
        {
            int failures = 0;
            var history = new SculptHistory();

            var a = new float[2, 2] { { 0f, 0f }, { 0f, 0f } };
            var b = new float[2, 2] { { 1f, 0f }, { 0f, 0f } };
            var c = new float[2, 2] { { 1f, 2f }, { 0f, 0f } };

            history.BeginStroke(a);
            history.BeginStroke(b);

            var undoneOnce = history.Undo(c);
            if (!GridEquals(undoneOnce, b))
            {
                Debug.LogError("[sculpt-probe] SculptHistory.Undo did not return the previous stroke");
                failures++;
            }

            var undoneTwice = history.Undo(undoneOnce);
            if (!GridEquals(undoneTwice, a))
            {
                Debug.LogError("[sculpt-probe] SculptHistory.Undo did not reach the original grid");
                failures++;
            }

            if (history.CanUndo)
            {
                Debug.LogError("[sculpt-probe] SculptHistory.CanUndo true with nothing left to undo");
                failures++;
            }

            var redoneOnce = history.Redo(undoneTwice);
            if (!GridEquals(redoneOnce, b))
            {
                Debug.LogError("[sculpt-probe] SculptHistory.Redo did not restore the undone stroke");
                failures++;
            }

            history.BeginStroke(redoneOnce);
            if (history.CanRedo)
            {
                Debug.LogError("[sculpt-probe] BeginStroke did not clear the redo stack");
                failures++;
            }

            Debug.Log("[sculpt-probe] sculpt history  undo/redo/new-stroke-clears-redo all correct");
            return failures;
        }

        private static bool GridEquals(float[,] x, float[,] y)
        {
            if (x.GetLength(0) != y.GetLength(0) || x.GetLength(1) != y.GetLength(1)) return false;
            for (int r = 0; r < x.GetLength(0); r++)
            for (int c = 0; c < x.GetLength(1); c++)
                if (x[r, c] != y[r, c]) return false;
            return true;
        }
```

This won't compile — `SculptHistory` doesn't exist yet.

- [ ] **Step 2: Confirm it fails (compile error)**

Same compile-check commands as Task 1 Step 2. Expected: `error CS0246` — "The type or namespace name 'SculptHistory' could not be found".

- [ ] **Step 3: Implement SculptHistory**

Create `Assets/Noir/Editor/SculptHistory.cs`:

```csharp
using System.Collections.Generic;

namespace Noir.Editor
{
    /// <summary>
    /// Undo/redo for the sculpt window, as a plain stack of whole-delta-grid snapshots.
    ///
    /// The delta grid is 71x81 floats - about 23KB - which is cheap enough to keep the FULL grid
    /// on every stroke rather than diff cell by cell. Deliberately not wired into Unity's own
    /// Undo system: that would mean wrapping the delta grid in a ScriptableObject purely to get
    /// Undo.RegisterCompleteObjectUndo to track it, and a private stack here is simpler, fully
    /// deterministic, and cannot collide with the user's own scene-edit undo history sharing the
    /// same Ctrl+Z.
    /// </summary>
    public sealed class SculptHistory
    {
        private readonly List<float[,]> _undo = new List<float[,]>();
        private readonly List<float[,]> _redo = new List<float[,]>();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Call before a stroke changes anything, with the grid as it stood going in.
        /// Starting a new stroke always clears redo - the branch a redo would have replayed no
        /// longer exists once the user has painted something new.</summary>
        public void BeginStroke(float[,] before)
        {
            _undo.Add((float[,])before.Clone());
            _redo.Clear();
        }

        /// <summary>Pops the most recent stroke and returns the grid to restore. current is
        /// pushed onto the redo stack so Redo can put it back.</summary>
        public float[,] Undo(float[,] current)
        {
            if (!CanUndo) return current;
            var previous = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add((float[,])current.Clone());
            return previous;
        }

        public float[,] Redo(float[,] current)
        {
            if (!CanRedo) return current;
            var next = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add((float[,])current.Clone());
            return next;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
```

- [ ] **Step 4: Confirm it compiles and the probe passes**

Same commands as Task 1 Step 4. Expected: no `error CS`, log contains `sculpt history  undo/redo/new-stroke-clears-redo all correct` and `--- SCULPT PROBE PASSED ---`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Editor/SculptHistory.cs Assets/Noir/Editor/SculptProbe.cs
git commit -m "Add SculptHistory, a pure undo/redo stack of delta-grid snapshots"
```

---

### Task 4: SculptBrush (falloff math)

**Files:**
- Create: `Assets/Noir/Editor/SculptBrush.cs`
- Modify: `Assets/Noir/Editor/SculptProbe.cs`

**Interfaces:**
- Produces: `SculptBrush.FalloffWeight(float distance, float radius) : float`; `SculptBrush.CellsInBrush(float centreX, float centreY, float radius, int step, int cols, int rows) : IEnumerable<(int col, int row, float weight)>`.
- Consumes: nothing.

- [ ] **Step 1: Write the failing probe check**

In `Assets/Noir/Editor/SculptProbe.cs`, add a call:

```csharp
                failures += CheckSculptHistory();
                failures += CheckBrushFalloff();
```

and a new method:

```csharp
        /// <summary>Falloff is 1.0 dead centre, 0.0 at the brush edge, and strictly between the
        /// two halfway out. CellsInBrush must find the cell at its own centre and must not reach
        /// a cell nowhere near it - the two ends of "the brush touches the right cells".</summary>
        private static int CheckBrushFalloff()
        {
            int failures = 0;

            float centreWeight = SculptBrush.FalloffWeight(0f, 10f);
            if (Mathf.Abs(centreWeight - 1f) > 0.001f)
            {
                Debug.LogError($"[sculpt-probe] brush falloff: centre weight {centreWeight:0.000}, "
                              + "expected 1.0");
                failures++;
            }

            float edgeWeight = SculptBrush.FalloffWeight(10f, 10f);
            if (Mathf.Abs(edgeWeight) > 0.001f)
            {
                Debug.LogError($"[sculpt-probe] brush falloff: edge weight {edgeWeight:0.000}, "
                              + "expected 0.0");
                failures++;
            }

            float midWeight = SculptBrush.FalloffWeight(5f, 10f);
            if (midWeight <= edgeWeight || midWeight >= centreWeight)
            {
                Debug.LogError($"[sculpt-probe] brush falloff: mid weight {midWeight:0.000} not "
                              + $"between edge {edgeWeight:0.000} and centre {centreWeight:0.000}");
                failures++;
            }

            // A cell dead centre of a 20m brush at (300,300) with a 30m step must appear -
            // 300/30 = 10 exactly. A cell in the far corner of a 71x81 grid must not.
            var cells = new List<(int col, int row, float weight)>(
                SculptBrush.CellsInBrush(300f, 300f, 20f, 30, 71, 81));

            bool centreCellFound = false;
            foreach (var cell in cells)
                if (cell.col == 10 && cell.row == 10) centreCellFound = true;
            if (!centreCellFound)
            {
                Debug.LogError("[sculpt-probe] brush falloff: CellsInBrush missed the cell at "
                              + "its own centre");
                failures++;
            }

            foreach (var cell in cells)
            {
                if (cell.col != 70 && cell.row != 80) continue;
                Debug.LogError($"[sculpt-probe] brush falloff: CellsInBrush included "
                              + $"({cell.col},{cell.row}), nowhere near the brush centre");
                failures++;
                break;
            }

            Debug.Log($"[sculpt-probe] brush falloff  centre 1.0, edge 0.0, {cells.Count} cells "
                    + "for a 20m brush");
            return failures;
        }
```

This won't compile — `SculptBrush` doesn't exist yet.

- [ ] **Step 2: Confirm it fails (compile error)**

Same compile-check commands as Task 1 Step 2. Expected: `error CS0246` — "The type or namespace name 'SculptBrush' could not be found".

- [ ] **Step 3: Implement SculptBrush**

Create `Assets/Noir/Editor/SculptBrush.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// The pure arithmetic of one brush stroke: which delta cells a stroke touches, and how much
    /// of the stroke's strength each one gets.
    ///
    /// Kept apart from SculptTerrainWindow so it can be exercised without a Scene view - the
    /// window's own job is reading the mouse and writing pixels, and none of that is where a
    /// falloff bug would hide.
    /// </summary>
    public static class SculptBrush
    {
        /// <summary>0 at the brush edge, 1 at its centre, smoothstepped rather than linear so a
        /// wide brush fades out instead of ending in a visible ring.</summary>
        public static float FalloffWeight(float distance, float radius)
        {
            if (radius <= 0f) return distance <= 0f ? 1f : 0f;
            float t = Mathf.Clamp01(1f - distance / radius);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Every delta cell within radius of (centreX, centreY), and the falloff weight
        /// each one gets. Cell (col, row) sits at world (col * step, row * step) - the same
        /// lattice ElevationGrid samples bilinearly - so walking one cell past the radius on
        /// every side (rather than exactly to it) is what keeps a wide brush from leaving a hard
        /// step where a sample point just inside the radius would otherwise blend against a cell
        /// just outside it that was never touched.</summary>
        public static IEnumerable<(int col, int row, float weight)> CellsInBrush(
            float centreX, float centreY, float radius, int step, int cols, int rows)
        {
            int margin = 1;
            int c0 = Mathf.Max(0, Mathf.FloorToInt((centreX - radius) / step) - margin);
            int c1 = Mathf.Min(cols - 1, Mathf.CeilToInt((centreX + radius) / step) + margin);
            int r0 = Mathf.Max(0, Mathf.FloorToInt((centreY - radius) / step) - margin);
            int r1 = Mathf.Min(rows - 1, Mathf.CeilToInt((centreY + radius) / step) + margin);

            for (int row = r0; row <= r1; row++)
            for (int col = c0; col <= c1; col++)
            {
                float dx = col * step - centreX;
                float dy = row * step - centreY;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float weight = FalloffWeight(distance, radius);
                if (weight > 0f) yield return (col, row, weight);
            }
        }
    }
}
```

- [ ] **Step 4: Confirm it compiles and the probe passes**

Same commands as Task 1 Step 4. Expected: no `error CS`, log contains `brush falloff  centre 1.0, edge 0.0` and `--- SCULPT PROBE PASSED ---`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Editor/SculptBrush.cs Assets/Noir/Editor/SculptProbe.cs
git commit -m "Add SculptBrush, the pure falloff/cell-selection math for one stroke"
```

---

### Task 5: SculptTerrainWindow

**Files:**
- Create: `Assets/Noir/Editor/SculptTerrainWindow.cs`

**Interfaces:**
- Consumes: `ElevationGrid.{DeltaCols, DeltaRows, DeltaStep, GetDeltaCell, SetDeltaCell, CopyDelta, RestoreDelta, SaveDelta, HeightAt}` (Task 1); `VillageMesh.BuildGround(WorldModel, Transform) : GameObject` (Task 2); `SculptHistory` (Task 3); `SculptBrush.{FalloffWeight, CellsInBrush}` (Task 4); `Space3D.GroundHit(Ray, out Vector3) : bool`, `Space3D.FromWorld(Vector3) : Vec2`; `ContentLoader.{Exists, Root, Read}`; `PlaceKindTable.{IsInstalled, Install, Parse}`; `VillageParser.Parse`; `WorldBuilder.Build`; `VillageHost.{MapFile, Seed}`.
- Produces: the `Noir/Sculpt Terrain` menu item. Nothing else in the project depends on this file.

This is the one piece of the design that cannot be verified headlessly — there is no way to script a mouse drag across a Scene view in `-batchmode`. Painting is checked by hand against the manual checklist in Step 4. Every other file in this plan exists specifically to keep as much of the risk as possible OUT of this unverifiable path.

- [ ] **Step 1: Implement the window**

Create `Assets/Noir/Editor/SculptTerrainWindow.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Nudges the real ground at specific spots without ever touching the measured data
    /// underneath. Edit Mode only, the same way Unity's own Terrain tool works - see
    /// docs/superpowers/specs/2026-08-01-sculpt-paint-tool-design.md for why.
    ///
    /// Opens its own throwaway ground-only preview (no city, no people, no traffic) built by the
    /// same VillageMesh.BuildGround the real village uses, and patches only the mesh vertices a
    /// stroke actually touches - see SculptBrush for which those are and ElevationGrid for where
    /// the height itself now lives.
    /// </summary>
    public sealed class SculptTerrainWindow : EditorWindow
    {
        private const int ChunkSize = 64;   // MeshChunks.Size - the ground's own chunk grid

        private WorldModel _world;
        private GameObject _previewRoot;
        private GameObject _groundRoot;
        private readonly Dictionary<(int col, int row), MeshFilter> _chunks =
            new Dictionary<(int col, int row), MeshFilter>();

        private readonly SculptHistory _history = new SculptHistory();
        private float[,] _strokeStart;   // delta grid at the moment the mouse went down, or null

        private float _radius = 12f;
        private float _strength = 0.15f;
        private bool _painting;

        [MenuItem("Noir/Sculpt Terrain")]
        public static void Open() => GetWindow<SculptTerrainWindow>("Sculpt Terrain");

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            RebuildPreview();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            TeardownPreview();
        }

        /// <summary>Unity calls this when the window closes with hasUnsavedChanges true and the
        /// user picks "Save" on the native prompt - the same mechanism Shader Graph and Timeline
        /// use, which is what makes this "matching Unity's own unsaved-scene prompt" rather than
        /// a bespoke dialog.</summary>
        public override void SaveChanges()
        {
            ElevationGrid.SaveDelta();
            hasUnsavedChanges = false;
            base.SaveChanges();
        }

        // ---------- preview lifecycle ----------

        private void RebuildPreview()
        {
            TeardownPreview();

            if (!ContentLoader.Exists)
            {
                Debug.LogError($"[sculpt] content not found at {ContentLoader.Root}");
                return;
            }

            if (!PlaceKindTable.IsInstalled)
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));

            var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
            _world = WorldBuilder.Build(layout, VillageHost.Seed);

            _previewRoot = new GameObject("SculptPreview") { hideFlags = HideFlags.DontSave };
            _groundRoot = VillageMesh.BuildGround(_world, _previewRoot.transform);

            _chunks.Clear();
            foreach (var filter in _groundRoot.GetComponentsInChildren<MeshFilter>())
            {
                var parts = filter.gameObject.name.Split(' ');
                if (parts.Length != 2 || parts[0] != "Ground") continue;   // skips "Surround 0,0"
                var coords = parts[1].Split(',');
                if (coords.Length != 2) continue;
                if (!int.TryParse(coords[0], out int col) || !int.TryParse(coords[1], out int row))
                    continue;
                _chunks[(col, row)] = filter;
            }

            Debug.Log($"[sculpt] preview ready: {_chunks.Count} ground chunks.");
        }

        private void TeardownPreview()
        {
            if (_previewRoot != null) DestroyImmediate(_previewRoot);
            _previewRoot = null;
            _groundRoot = null;
            _chunks.Clear();
        }

        // ---------- GUI ----------

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Paint in the Scene view with the left mouse button. Shift lowers instead of "
              + "raising.", MessageType.Info);

            _radius = EditorGUILayout.Slider("Radius (m)", _radius, 1f, 60f);
            _strength = EditorGUILayout.Slider("Strength (m/pass)", _strength, 0.02f, 1f);

            using (new EditorGUI.DisabledScope(!_history.CanUndo))
                if (GUILayout.Button("Undo")) ApplyHistory(_history.Undo);

            using (new EditorGUI.DisabledScope(!_history.CanRedo))
                if (GUILayout.Button("Redo")) ApplyHistory(_history.Redo);

            using (new EditorGUI.DisabledScope(!hasUnsavedChanges))
                if (GUILayout.Button("Save")) SaveChanges();

            if (GUILayout.Button("Rebuild Preview (pick up map edits)")) RebuildPreview();

            EditorGUILayout.LabelField("Unsaved changes:", hasUnsavedChanges ? "yes" : "no");
        }

        /// <summary>Undo/redo goes through a full RebuildPreview rather than patching vertices
        /// incrementally. A stroke's own vertex patch can lean on "only the delta changed, so the
        /// difference in HeightAt IS the difference to apply" (see Paint below) - but a popped
        /// snapshot can touch cells anywhere on the map, not just under a brush, and a full
        /// rebuild is the same code path the real village already trusts rather than a second,
        /// narrower one written just for this.</summary>
        private void ApplyHistory(Func<float[,], float[,]> pop)
        {
            var current = ElevationGrid.CopyDelta();
            if (current == null) return;
            var restored = pop(current);
            ElevationGrid.RestoreDelta(restored);
            RebuildPreview();
            hasUnsavedChanges = _history.CanUndo || hasUnsavedChanges;
            Repaint();
        }

        // ---------- painting ----------

        private void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            if (e == null || _groundRoot == null) return;

            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Space3D.GroundHit(ray, out var hit)) return;

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                _painting = true;
                _strokeStart = ElevationGrid.CopyDelta();
                Paint(hit, e.shift);
                e.Use();
            }
            else if (_painting && e.type == EventType.MouseDrag && e.button == 0 && !e.alt)
            {
                Paint(hit, e.shift);
                e.Use();
            }
            else if (_painting && e.type == EventType.MouseUp && e.button == 0)
            {
                _painting = false;
                if (_strokeStart != null) _history.BeginStroke(_strokeStart);
                _strokeStart = null;
                hasUnsavedChanges = true;
                e.Use();
            }

            Handles.color = new Color(1f, 0.85f, 0.2f, 0.6f);
            Handles.DrawWireDisc(hit, Vector3.up, _radius);
            view.Repaint();
        }

        /// <summary>One paint event: mutate the delta cells under the brush, then nudge every
        /// touched vertex by exactly how much HeightAt changed at that vertex's own (x, y).
        /// Diffing HeightAt before/after - rather than writing a computed height straight onto
        /// the vertex - is what keeps this correct regardless of whatever offset is already
        /// baked into that vertex's Y (water sits lower than grass, a road is worn below its
        /// verge): the offset never changes, so it cancels out of the difference.</summary>
        private void Paint(Vector3 hit, bool lowering)
        {
            var centre = Space3D.FromWorld(hit);
            float signedStrength = (lowering ? -1f : 1f) * _strength;

            var touched = TouchedVertices(centre, _radius);
            var before = new float[touched.Count];
            for (int i = 0; i < touched.Count; i++)
                before[i] = ElevationGrid.HeightAt(touched[i].wx, touched[i].wy);

            foreach (var (col, row, weight) in SculptBrush.CellsInBrush(
                         centre.X, centre.Y, _radius, ElevationGrid.DeltaStep,
                         ElevationGrid.DeltaCols, ElevationGrid.DeltaRows))
            {
                float value = ElevationGrid.GetDeltaCell(col, row) + signedStrength * weight;
                ElevationGrid.SetDeltaCell(col, row, value);
            }

            var byFilter = new Dictionary<MeshFilter, Vector3[]>();
            for (int i = 0; i < touched.Count; i++)
            {
                var (filter, index, wx, wy) = touched[i];
                if (!byFilter.TryGetValue(filter, out var verts))
                    byFilter[filter] = verts = filter.sharedMesh.vertices;

                float after = ElevationGrid.HeightAt(wx, wy);
                var v = verts[index];
                v.y += after - before[i];
                verts[index] = v;
            }

            foreach (var kv in byFilter)
            {
                kv.Key.sharedMesh.vertices = kv.Value;
                kv.Key.sharedMesh.RecalculateNormals();
                kv.Key.sharedMesh.RecalculateBounds();
            }
        }

        /// <summary>Every vertex, across every ground chunk the brush's bounding box overlaps,
        /// that falls inside the brush radius - as (filter, vertex index, world x, world y). The
        /// chunk grid here is MeshChunks.Size (64m), a different lattice from the delta grid's
        /// own 30m step - a brush can span several chunks and still land inside one delta cell,
        /// or the other way round.</summary>
        private List<(MeshFilter filter, int index, float wx, float wy)> TouchedVertices(
            Vec2 centre, float radius)
        {
            var found = new List<(MeshFilter, int, float, float)>();

            int c0 = Mathf.FloorToInt((centre.X - radius) / ChunkSize);
            int c1 = Mathf.FloorToInt((centre.X + radius) / ChunkSize);
            int r0 = Mathf.FloorToInt((centre.Y - radius) / ChunkSize);
            int r1 = Mathf.FloorToInt((centre.Y + radius) / ChunkSize);

            for (int row = r0; row <= r1; row++)
            for (int col = c0; col <= c1; col++)
            {
                if (!_chunks.TryGetValue((col, row), out var filter)) continue;
                var verts = filter.sharedMesh.vertices;
                for (int i = 0; i < verts.Length; i++)
                {
                    float wx = verts[i].x, wy = -verts[i].z;
                    float dx = wx - centre.X, dy = wy - centre.Y;
                    if (dx * dx + dy * dy <= radius * radius)
                        found.Add((filter, i, wx, wy));
                }
            }

            return found;
        }
    }
}
```

- [ ] **Step 2: Compile-check**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" `
  -batchmode -quit -nographics -projectPath C:\SerialKillerGame -logFile C:\SerialKillerGame\.unity-build.log
Select-String -Path C:\SerialKillerGame\.unity-build.log -Pattern "error CS"
```
Expected: no matches.

- [ ] **Step 3: Re-run the full probe and smoke test (regression)**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" `
  -batchmode -quit -nographics -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SculptProbe.Run -logFile C:\SerialKillerGame\.unity-probe.log
Select-String -Path C:\SerialKillerGame\.unity-probe.log -Pattern "sculpt-probe"
& "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" `
  -batchmode -quit -nographics -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SmokeTest.Run -logFile C:\SerialKillerGame\.unity-smoke.log
Select-String -Path C:\SerialKillerGame\.unity-smoke.log -Pattern "SMOKE TEST"
```
Expected: `--- SCULPT PROBE PASSED ---` and `--- SMOKE TEST PASSED ---`.

- [ ] **Step 4: Manual verification (tell the user to do this — it cannot be scripted)**

Ask the user to open the project, then:
1. `Noir > Sculpt Terrain` from the menu bar. A window opens; the Scene view shows bare ground (no walls, buildings, or props) with no errors in the Console.
2. Left-click-drag on the ground in the Scene view. The ground rises smoothly under the cursor with no frame drops; a yellow wire circle tracks the brush.
3. Hold Shift and drag on a raised spot. It lowers back down.
4. Click **Undo**. The last stroke reverts; the ground returns to its previous shape.
5. Click **Redo**. The stroke reapplies.
6. Click **Save**. `Content/elevation-delta.txt` appears (check via `git status` — untracked, new file).
7. Close the window with an un-saved stroke still pending (paint something, don't click Save, then close the window with the X). Unity's native "Save changes" prompt should appear, offering Save/Discard/Cancel.
8. Re-open `Noir > Sculpt Terrain`. The previously saved stroke is visible in the rebuilt preview (proves the round trip through the file, not just in-memory state).

If `Content/elevation-delta.txt` was created only for this check and should not be committed yet, delete it and confirm `git status` is clean again.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Editor/SculptTerrainWindow.cs
git commit -m "Add SculptTerrainWindow: Edit-Mode brush painting on the delta layer"
```

---

## Self-Review Notes

- **Spec coverage:** all five sign-off gates from the parent spec are covered — responsiveness (Task 5's per-stroke vertex patch, never a full rebuild), persistence (Task 1's save/load, checked in Task 1's probe and Task 5's manual Step 4.6–4.8), undo/redo correctness with the base grid untouched (Task 3's probe + Task 5's `ApplyHistory`), integration with `ElevationGrid` (Task 1, consumed everywhere via the unchanged `HeightAt` signature), and no crashes at boundaries/rapid undo (`GetDeltaCell`/`SetDeltaCell` clamp/no-op rather than throw; `SculptHistory.Undo`/`Redo` no-op rather than throw when the stack is empty).
- **Out of scope, confirmed absent from every task:** Play-mode sculpting, `CityCollision` patching, texturing, and any change to `elevation.txt`'s own sampling.
- **Placeholder scan:** no TODOs; every step has complete code, not a description of code.
- **Type consistency:** `SculptHistory.Undo/Redo` return `float[,]` in both Task 3's implementation and Task 5's `ApplyHistory` call; `SculptBrush.CellsInBrush` returns `IEnumerable<(int col, int row, float weight)>` in both Task 4's implementation and Task 5's `foreach` destructuring; `ElevationGrid.DeltaCols/DeltaRows/DeltaStep` are `int` everywhere they're read.
