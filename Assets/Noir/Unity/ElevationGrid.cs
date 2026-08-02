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
