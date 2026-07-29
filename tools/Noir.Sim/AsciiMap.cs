using System;
using System.Text;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// Renders the world as text. This is the whole reason the village can be built and iterated
    /// on before Unity exists — I can author a layout, look at it, and fix it in one loop.
    /// </summary>
    public static class AsciiMap
    {
        public static char Glyph(Terrain t)
        {
            switch (t)
            {
                case Terrain.Grass: return '.';
                case Terrain.Field: return ',';
                case Terrain.Wood: return 't';
                case Terrain.Water: return '~';
                case Terrain.Road: return '=';
                case Terrain.Path: return '-';
                case Terrain.Wall: return '#';
                case Terrain.Floor: return ' ';
                case Terrain.Churchyard: return '+';
                default: return '?';
            }
        }

        public static string Render(WorldModel world, int scale = 1)
        {
            var grid = world.Grid;
            var sb = new StringBuilder();

            // column ruler
            sb.Append("    ");
            for (int x = 0; x < grid.Width; x += scale)
                sb.Append(x % 10 == 0 ? (char)('0' + (x / 10) % 10) : ' ');
            sb.AppendLine();

            for (int y = 0; y < grid.Height; y += scale)
            {
                sb.Append(y.ToString().PadLeft(3)).Append(' ');
                for (int x = 0; x < grid.Width; x += scale)
                    sb.Append(Glyph(DominantTerrain(grid, x, y, scale)));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>When downsampling, prefer the most "structural" terrain in the block so
        /// walls and water survive rather than being averaged away into grass.</summary>
        private static Terrain DominantTerrain(TileGrid grid, int x0, int y0, int scale)
        {
            if (scale <= 1) return grid.TerrainAt(x0, y0);

            Terrain best = Terrain.Grass;
            int bestRank = -1;
            for (int y = y0; y < y0 + scale && y < grid.Height; y++)
            for (int x = x0; x < x0 + scale && x < grid.Width; x++)
            {
                var t = grid.TerrainAt(x, y);
                int rank = Rank(t);
                if (rank > bestRank) { bestRank = rank; best = t; }
            }
            return best;
        }

        private static int Rank(Terrain t)
        {
            switch (t)
            {
                case Terrain.Wall: return 9;
                case Terrain.Water: return 8;
                case Terrain.Road: return 7;
                case Terrain.Path: return 6;
                case Terrain.Floor: return 5;
                case Terrain.Churchyard: return 4;
                case Terrain.Wood: return 3;
                case Terrain.Field: return 2;
                default: return 1;
            }
        }

        public static string Legend() =>
            "  . grass    , field    t wood     ~ water\n" +
            "  = road     - path     # wall     ' ' indoors    + churchyard";

        /// <summary>A labelled index of every place, for cross-checking the map.</summary>
        public static string PlaceTable(WorldModel world)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{"kind",-12} {"name",-22} {"bounds",-14} {"door",-10} jobs  hours");
            sb.AppendLine(new string('-', 92));
            foreach (var p in world.AllPlaces)
            {
                string hours = p.AlwaysOpen ? "—" : string.Join(" ", p.Hours);
                sb.AppendLine($"{p.Kind,-12} {Truncate(p.Name, 22),-22} {p.Bounds,-14} {p.Door,-10} {p.JobSlots,4}  {hours}");
            }
            return sb.ToString();
        }

        private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }
}
