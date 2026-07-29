using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// The geometry every grammar shares: cutting a run of tiles into rooms, and finding a
    /// doorway in the wall between two of them.
    ///
    /// Nothing here knows what a room is for. The rules about that live in the grammars, which
    /// is the whole point of having more than one of them.
    /// </summary>
    internal static class InteriorGeometry
    {
        /// <summary>A room is never one tile wide. A corridor may be; a corridor is not a room.</summary>
        public const int MinSide = 2;

        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        public static int Area(TileRect r) => r.W * r.H;

        /// <summary>
        /// How many rooms to cut a run of <paramref name="length"/> tiles into if each wants to
        /// be about <paramref name="preferred"/> tiles across. Each room after the first costs
        /// its own width plus the wall in front of it.
        ///
        /// Rounded to the nearest, not floored. Floored, sixteen metres of school at eight
        /// metres a classroom came to one classroom.
        /// </summary>
        public static int Parts(int length, int preferred)
        {
            if (preferred < MinSide) preferred = MinSide;
            int each = preferred + 1;
            int parts = (2 * (length + 1) + each) / (2 * each);
            if (parts < 1) parts = 1;
            while (parts > 1 && (length - (parts - 1)) / parts < MinSide) parts--;
            return parts;
        }

        /// <summary>
        /// Cut a run into rooms separated by one-tile walls, writing (start, size) pairs.
        ///
        /// The tiles that will not divide evenly are handed out from a random starting room
        /// rather than always to the first, so that the two sides of a corridor in the same
        /// building do not come out as mirror images of each other.
        /// </summary>
        public static void Slice(int start, int length, int parts, IRng rng,
                                 List<int> starts, List<int> sizes)
        {
            if (parts < 1) parts = 1;

            int usable = length - (parts - 1);
            int size = usable / parts;
            int extra = usable - size * parts;
            int from = extra > 0 ? rng.NextInt(parts) : 0;

            int at = start;
            for (int i = 0; i < parts; i++)
            {
                int width = size + (((i - from + parts) % parts) < extra ? 1 : 0);
                starts.Add(at);
                sizes.Add(width);
                at += width + 1;
            }
        }

        /// <summary>
        /// Shift a dividing wall off the line the front door stands on.
        ///
        /// Without this the door can open onto a wall, and WorldBuilder then tunnels inward
        /// along it looking for floor - leaving a one-tile slot between two rooms as the way in.
        /// A house gets away with that. A hospital entrance does not.
        /// </summary>
        public static void ClearDoorLine(List<int> starts, List<int> sizes, int doorAt)
        {
            for (int i = 0; i + 1 < starts.Count; i++)
            {
                if (starts[i] + sizes[i] != doorAt) continue;

                if (sizes[i + 1] > MinSide)
                {
                    sizes[i]++;
                    starts[i + 1]++;
                    sizes[i + 1]--;
                }
                else if (sizes[i] > MinSide)
                {
                    sizes[i]--;
                    starts[i + 1]--;
                    sizes[i + 1]++;
                }
                return;
            }
        }

        /// <summary>Largest first, ties broken by position so the order never depends on the sort.</summary>
        public static int[] ByAreaDescending(List<TileRect> rooms)
        {
            var order = new int[rooms.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;

            for (int i = 1; i < order.Length; i++)
            {
                int key = order[i];
                int j = i - 1;
                while (j >= 0 && Area(rooms[order[j]]) < Area(rooms[key])) { order[j + 1] = order[j]; j--; }
                order[j + 1] = key;
            }
            return order;
        }

        public static int NearestTo(List<TileRect> rooms, Tile point)
        {
            int best = 0, bestDist = int.MaxValue;
            for (int i = 0; i < rooms.Count; i++)
            {
                int d = Tile.DistanceSquared(rooms[i].Centre, point);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Find a doorway in the wall between two rooms. The wall is the one-tile gap the
        /// subdivision left, so we look for rooms separated by exactly one tile and pick a
        /// spot along the shared span.
        /// </summary>
        public static bool TryDoorBetween(TileRect a, TileRect b, IRng rng, out Tile door)
        {
            door = Tile.None;

            // Vertical wall: b is to the right of a, or vice versa.
            if (a.Right + 2 == b.X || b.Right + 2 == a.X)
            {
                int wallX = a.Right + 2 == b.X ? a.Right + 1 : b.Right + 1;
                int lo = a.Y > b.Y ? a.Y : b.Y;
                int hi = a.Bottom < b.Bottom ? a.Bottom : b.Bottom;
                if (hi < lo) return false;
                door = new Tile(wallX, lo + rng.NextInt(hi - lo + 1));
                return true;
            }

            // Horizontal wall.
            if (a.Bottom + 2 == b.Y || b.Bottom + 2 == a.Y)
            {
                int wallY = a.Bottom + 2 == b.Y ? a.Bottom + 1 : b.Bottom + 1;
                int lo = a.X > b.X ? a.X : b.X;
                int hi = a.Right < b.Right ? a.Right : b.Right;
                if (hi < lo) return false;
                door = new Tile(lo + rng.NextInt(hi - lo + 1), wallY);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Door every room onto one hub - the corridor, the shop floor, the nave.
        ///
        /// The hub is tried first for every room, every time, and only what cannot reach it
        /// takes a door off something else. That ordering is the rule: a store off a kitchen
        /// is a building, a ward you reach by walking through another ward is not.
        /// </summary>
        public static void Connect(Interior interior, int hub, IRng rng)
        {
            int n = interior.Rooms.Count;
            if (n <= 1) return;

            var connected = new bool[n];
            connected[hub] = true;
            int remaining = n - 1;

            for (int b = 0; b < n && remaining > 0; b++)
            {
                if (connected[b]) continue;
                if (!TryDoorBetween(interior.Rooms[hub].bounds, interior.Rooms[b].bounds, rng, out var door))
                    continue;

                interior.Doors.Add(door);
                connected[b] = true;
                remaining--;
            }

            int guard = 0;
            while (remaining > 0 && guard++ < n + 2)
            {
                bool progressed = false;

                for (int a = 0; a < n && remaining > 0; a++)
                {
                    if (!connected[a]) continue;
                    for (int b = 0; b < n; b++)
                    {
                        if (connected[b] || a == b) continue;
                        if (!TryDoorBetween(interior.Rooms[a].bounds, interior.Rooms[b].bounds, rng, out var door))
                            continue;

                        interior.Doors.Add(door);
                        connected[b] = true;
                        remaining--;
                        progressed = true;
                        break;
                    }
                }

                if (!progressed) break;   // genuinely unreachable; the validator will say so
            }
        }
    }
}
