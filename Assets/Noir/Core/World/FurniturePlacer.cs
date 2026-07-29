using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// Puts the furniture where the room says it goes.
    ///
    /// Real rooms are furnished around their edges, with the middle left clear to walk through.
    /// Doing the same thing here gets two results for free: rooms read correctly from above,
    /// and the floor stays walkable so pathfinding never has to care that furniture exists.
    ///
    /// That is true of every room in a house and false of half the rooms in a town. A cinema,
    /// a classroom and a church are rooms where the MIDDLE is the room and the walls are what
    /// is left over, so those get ranks facing a focus instead - see <see cref="Placement"/>.
    /// The floor stays walkable either way, on the same terms as a bed does.
    ///
    /// Nothing is placed next to a doorway. A wardrobe blocking the only door is the single
    /// most obvious way for a generated interior to look generated.
    /// </summary>
    public static class FurniturePlacer
    {
        public static void Place(Room room, HashSet<int> doorTiles, int gridWidth,
                                 List<Furniture> output)
        {
            var plan = FurniturePlans.For(room.Kind);
            if (plan.Count == 0) return;

            var b = room.Bounds;
            if (b.W < 2 || b.H < 2) return;   // too small to furnish without blocking it

            var perimeter = Perimeter(b);
            var used = new HashSet<int>();
            int focal = -1;                   // worked out once, and only if something needs it

            foreach (var item in plan)
            {
                switch (item.Where)
                {
                    case Placement.Along:
                    {
                        int limit = item.Count > 0 ? item.Count : perimeter.Count;
                        for (int n = 0; n < limit; n++)
                            if (!PlaceAgainstWall(item, room, perimeter, used, doorTiles,
                                                  gridWidth, item.Gap, output)) break;
                        break;
                    }

                    case Placement.Focal:
                        if (focal < 0) focal = FocalWall(b, doorTiles, gridWidth);
                        PlaceFocal(item, focal, room, used, doorTiles, gridWidth, output);
                        break;

                    case Placement.Rows:
                        if (focal < 0) focal = FocalWall(b, doorTiles, gridWidth);
                        PlaceRows(item, focal, room, used, doorTiles, gridWidth, output);
                        break;

                    default:
                        PlaceAgainstWall(item, room, perimeter, used, doorTiles, gridWidth, 0, output);
                        break;
                }
            }
        }

        /// <summary>One item against the first stretch of wall long enough to take it.</summary>
        private static bool PlaceAgainstWall(FurniturePlans.Item item, Room room, List<Tile> perimeter,
                                             HashSet<int> used, HashSet<int> doorTiles, int gridWidth,
                                             int gap, List<Furniture> output)
        {
            int length = item.W > item.H ? item.W : item.H;
            if (!TryFindRun(perimeter, length, used, doorTiles, gridWidth, out var run, out int at))
                return false;

            var first = run[0];
            var last = run[run.Count - 1];

            var footprint = new TileRect(
                first.X < last.X ? first.X : last.X,
                first.Y < last.Y ? first.Y : last.Y,
                first.Y == last.Y ? run.Count : 1,
                first.Y == last.Y ? 1 : run.Count);

            output.Add(new Furniture(item.Kind, footprint, room.Id));
            foreach (var t in run) used.Add(t.Y * gridWidth + t.X);

            // Hold the next one off this one's shoulder, so a ward reads as a row of beds
            // rather than as one long slab down the wall.
            for (int k = 1; k <= gap; k++)
            {
                int before = at - k;
                int after = at + run.Count - 1 + k;
                if (before >= 0) used.Add(perimeter[before].Y * gridWidth + perimeter[before].X);
                if (after < perimeter.Count) used.Add(perimeter[after].Y * gridWidth + perimeter[after].X);
            }
            return true;
        }

        /// <summary>
        /// The wall a room is arranged to face.
        ///
        /// It has to be worked out from the room's own geometry, because a room does not know
        /// which of its doors is the one the public comes in by. The longest wall with no
        /// doorway in it turns out to be the right answer every time: a cinema screen goes
        /// across the wide end, a village hall stage goes at the narrow one, and in both cases
        /// that is exactly the wall nobody has cut a door through.
        /// </summary>
        private static int FocalWall(TileRect b, HashSet<int> doorTiles, int gridWidth)
        {
            var doors = new List<Tile>();
            for (int x = b.X; x <= b.Right; x++)
            {
                if (doorTiles.Contains((b.Y - 1) * gridWidth + x)) doors.Add(new Tile(x, b.Y - 1));
                if (doorTiles.Contains((b.Bottom + 1) * gridWidth + x)) doors.Add(new Tile(x, b.Bottom + 1));
            }
            for (int y = b.Y; y <= b.Bottom; y++)
            {
                if (doorTiles.Contains(y * gridWidth + b.X - 1)) doors.Add(new Tile(b.X - 1, y));
                if (doorTiles.Contains(y * gridWidth + b.Right + 1)) doors.Add(new Tile(b.Right + 1, y));
            }

            int best = 0;
            int bestScore = int.MinValue;

            for (int side = 0; side < 4; side++)
            {
                bool acrossX = side == 0 || side == 2;
                int length = acrossX ? b.W : b.H;
                int line = side == 0 ? b.Y - 1 : side == 1 ? b.Right + 1 : side == 2 ? b.Bottom + 1 : b.X - 1;

                bool clear = true;
                int away = 999;
                foreach (var d in doors)
                {
                    int at = acrossX ? d.Y : d.X;
                    int distance = at > line ? at - line : line - at;
                    if (distance == 0) clear = false;
                    if (distance < away) away = distance;
                }

                int score = (clear ? 1000000 : 0) + length * 1000 + away;
                if (score > bestScore) { bestScore = score; best = side; }
            }
            return best;
        }

        /// <summary>The screen, the altar, the blackboard: centred on the wall, and only one of them.</summary>
        private static void PlaceFocal(FurniturePlans.Item item, int side, Room room, HashSet<int> used,
                                       HashSet<int> doorTiles, int gridWidth, List<Furniture> output)
        {
            var b = room.Bounds;
            bool acrossX = side == 0 || side == 2;
            int wall = acrossX ? b.W : b.H;
            int inward = acrossX ? b.H : b.W;

            // A tile clear at each end, so it reads as furniture rather than as a wall.
            int length = item.W < wall - 2 ? item.W : wall - 2;
            if (length < 1) length = item.W < wall ? item.W : wall;
            int deep = item.H < inward - 1 ? item.H : inward - 1;
            if (length < 1 || deep < 1) return;

            int lo = acrossX ? b.X : b.Y;
            int centre = lo + (wall - length) / 2;

            for (int shift = 0; shift <= wall; shift++)
            for (int dir = 0; dir < 2; dir++)
            {
                int at = centre + (dir == 0 ? shift : -shift);
                if (at < lo || at + length > lo + wall) continue;

                var rect = Slab(side, b, at, length, deep);
                if (!IsClear(rect, used, doorTiles, gridWidth)) continue;

                output.Add(new Furniture(item.Kind, rect, room.Id));
                Occupy(rect, used, gridWidth);
                return;
            }
        }

        /// <summary>
        /// Ranks facing the focal wall, stepping back from it with a gangway between each.
        /// A central aisle appears once the room is wide enough that people would want one.
        /// </summary>
        private static void PlaceRows(FurniturePlans.Item item, int side, Room room, HashSet<int> used,
                                      HashSet<int> doorTiles, int gridWidth, List<Furniture> output)
        {
            var b = room.Bounds;
            bool acrossX = side == 0 || side == 2;

            int runLo = (acrossX ? b.X : b.Y) + 1;
            int runHi = (acrossX ? b.Right : b.Bottom) - 1;
            if (runHi < runLo) return;

            int aisle = runHi - runLo + 1 >= 9 ? (runLo + runHi) / 2 : int.MinValue;

            int length = item.W > item.H ? item.W : item.H;
            int limit = item.Count > 0 ? item.Count : int.MaxValue;
            int placed = 0;

            // Two tiles off the focal wall - one for whatever stands against it, one to stand
            // in front of that - and stopping a tile short of the back, to get in and out.
            bool forward = side == 0 || side == 3;
            int from = side == 0 ? b.Y + 2 : side == 2 ? b.Bottom - 2 : side == 3 ? b.X + 2 : b.Right - 2;
            int to = side == 0 ? b.Bottom - 1 : side == 2 ? b.Y + 1 : side == 3 ? b.Right - 1 : b.X + 1;

            for (int rank = from; forward ? rank <= to : rank >= to; rank += forward ? 2 : -2)
            {
                if (aisle == int.MinValue)
                {
                    Rank(item, acrossX, rank, runLo, runHi, length, room, used, doorTiles,
                         gridWidth, limit, ref placed, output);
                }
                else
                {
                    Rank(item, acrossX, rank, runLo, aisle - 1, length, room, used, doorTiles,
                         gridWidth, limit, ref placed, output);
                    Rank(item, acrossX, rank, aisle + 1, runHi, length, room, used, doorTiles,
                         gridWidth, limit, ref placed, output);
                }
                if (placed >= limit) return;
            }
        }

        /// <summary>
        /// One straight stretch of one rank. Whatever fits is centred in the stretch: a rank
        /// packed from the left leaves an offcut at the right-hand end and reads as a mistake.
        /// </summary>
        private static void Rank(FurniturePlans.Item item, bool acrossX, int rank, int lo, int hi,
                                 int length, Room room, HashSet<int> used, HashSet<int> doorTiles,
                                 int gridWidth, int limit, ref int placed, List<Furniture> output)
        {
            int span = hi - lo + 1;
            if (span < length) return;

            int fit = (span + item.Gap) / (length + item.Gap);
            if (fit > limit - placed) fit = limit - placed;
            if (fit < 1) return;

            int at = lo + (span - (fit * length + (fit - 1) * item.Gap)) / 2;
            for (int k = 0; k < fit; k++, at += length + item.Gap)
            {
                var rect = acrossX
                    ? new TileRect(at, rank, length, 1)
                    : new TileRect(rank, at, 1, length);

                if (!IsClear(rect, used, doorTiles, gridWidth)) continue;

                output.Add(new Furniture(item.Kind, rect, room.Id));
                Occupy(rect, used, gridWidth);
                placed++;
            }
        }

        private static TileRect Slab(int side, TileRect b, int at, int length, int deep)
        {
            switch (side)
            {
                case 0: return new TileRect(at, b.Y, length, deep);
                case 1: return new TileRect(b.Right - deep + 1, at, deep, length);
                case 2: return new TileRect(at, b.Bottom - deep + 1, length, deep);
                default: return new TileRect(b.X, at, deep, length);
            }
        }

        private static bool IsClear(TileRect r, HashSet<int> used, HashSet<int> doorTiles, int gridWidth)
        {
            for (int y = r.Y; y <= r.Bottom; y++)
            for (int x = r.X; x <= r.Right; x++)
            {
                if (used.Contains(y * gridWidth + x)) return false;
                if (NearDoor(new Tile(x, y), doorTiles, gridWidth)) return false;
            }
            return true;
        }

        private static void Occupy(TileRect r, HashSet<int> used, int gridWidth)
        {
            for (int y = r.Y; y <= r.Bottom; y++)
            for (int x = r.X; x <= r.Right; x++)
                used.Add(y * gridWidth + x);
        }

        /// <summary>Tiles around the inside edge of a room, in walk order.</summary>
        private static List<Tile> Perimeter(TileRect b)
        {
            var tiles = new List<Tile>();

            for (int x = b.X; x <= b.Right; x++) tiles.Add(new Tile(x, b.Y));
            for (int y = b.Y + 1; y <= b.Bottom; y++) tiles.Add(new Tile(b.Right, y));
            if (b.H > 1) for (int x = b.Right - 1; x >= b.X; x--) tiles.Add(new Tile(x, b.Bottom));
            if (b.W > 1) for (int y = b.Bottom - 1; y > b.Y; y--) tiles.Add(new Tile(b.X, y));

            return tiles;
        }

        /// <summary>
        /// A run of free tiles along one straight stretch of wall. Runs may not turn a corner
        /// and may not touch a doorway.
        /// </summary>
        private static bool TryFindRun(List<Tile> perimeter, int length, HashSet<int> used,
                                       HashSet<int> doorTiles, int gridWidth,
                                       out List<Tile> run, out int at)
        {
            run = new List<Tile>(length);
            at = -1;

            for (int start = 0; start < perimeter.Count; start++)
            {
                run.Clear();
                bool ok = true;

                for (int k = 0; k < length; k++)
                {
                    int index = start + k;
                    if (index >= perimeter.Count) { ok = false; break; }

                    var tile = perimeter[index];
                    int key = tile.Y * gridWidth + tile.X;

                    if (used.Contains(key)) { ok = false; break; }
                    if (NearDoor(tile, doorTiles, gridWidth)) { ok = false; break; }

                    // Runs must stay on one wall: every tile shares a row or a column with the first.
                    if (k > 0 && run[0].X != tile.X && run[0].Y != tile.Y) { ok = false; break; }

                    run.Add(tile);
                }

                if (ok && run.Count == length) { at = start; return true; }
            }

            run = null;
            return false;
        }

        /// <summary>A tile is off-limits if it or any orthogonal neighbour is a doorway.</summary>
        private static bool NearDoor(Tile tile, HashSet<int> doorTiles, int gridWidth)
        {
            if (doorTiles.Contains(tile.Y * gridWidth + tile.X)) return true;
            if (doorTiles.Contains(tile.Y * gridWidth + tile.X - 1)) return true;
            if (doorTiles.Contains(tile.Y * gridWidth + tile.X + 1)) return true;
            if (doorTiles.Contains((tile.Y - 1) * gridWidth + tile.X)) return true;
            if (doorTiles.Contains((tile.Y + 1) * gridWidth + tile.X)) return true;
            return false;
        }
    }
}
