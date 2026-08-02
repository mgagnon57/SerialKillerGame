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
