using System.Globalization;
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
    /// </summary>
    public static class ElevationGrid
    {
        private static float[,] _grid;   // [row, col], row 0 = north
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
            return RawAt(worldX, worldY) - _baseline;
        }

        public static float HeightAt(Vector2 world) => HeightAt(world.x, world.y);

        private static float RawAt(float worldX, float worldY)
        {
            float gx = worldX / _step;
            float gy = worldY / _step;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, _cols - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(gy), 0, _rows - 1);
            int x1 = Mathf.Min(x0 + 1, _cols - 1);
            int y1 = Mathf.Min(y0 + 1, _rows - 1);

            float tx = Mathf.Clamp01(gx - x0);
            float ty = Mathf.Clamp01(gy - y0);

            float h00 = _grid[y0, x0], h10 = _grid[y0, x1];
            float h01 = _grid[y1, x0], h11 = _grid[y1, x1];
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
        }
    }
}
