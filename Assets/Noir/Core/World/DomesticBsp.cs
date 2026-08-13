using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// Houses. Subdivide the footprint, then argue about what the rectangles are for.
    ///
    /// Subdivision alone does not produce a house - it produces plausible rectangles in an
    /// implausible arrangement. What makes a generated layout read as architecture is the rules
    /// applied afterwards:
    ///
    ///   * the front door opens into a hall or the front room, never a bedroom or bathroom;
    ///   * the bathroom is the smallest room and hangs off circulation, never a through-room;
    ///   * you never walk through one bedroom to reach another;
    ///   * the kitchen wants an outside wall, for a window and a back door;
    ///   * nothing is 1 tile wide - a corridor is not a room.
    ///
    /// Project Zomboid gets its realism by having humans draw every building. We cannot afford
    /// that, so the rules are doing the work instead. What IS taken from PZ is the important
    /// structural idea: a room carries a TYPE, and that type is simulation data rather than
    /// decoration.
    ///
    /// This grammar ignores the programme. A house is a house.
    /// </summary>
    public sealed class DomesticBsp : IInteriorGrammar
    {
        private const int MinSide = 2;   // a room is never 1 tile wide

        public string Name => "bsp";

        public Interior Generate(TileRect exterior, Tile frontDoor, string programme, IRng rng)
        {
            var result = new Interior();

            // The habitable area is inside the exterior walls.
            var interior = new TileRect(exterior.X + 1, exterior.Y + 1, exterior.W - 2, exterior.H - 2);
            if (interior.W < MinSide || interior.H < MinSide)
            {
                // A shed or a phone booth: one room, no internal structure.
                if (interior.W > 0 && interior.H > 0)
                    result.Rooms.Add((interior, RoomKind.Living));
                return result;
            }

            // Rooms scale with floor area, at roughly 8 m2 per room once walls are accounted
            // for. That lands a small cottage on three or four rooms and a farmhouse on six -
            // and it is the number that decides whether a house can have a bathroom at all.
            //
            // It is also the number that made this generator unusable for anything else: the
            // same arithmetic gives a hospital six rooms, one of which is a kitchen.
            int target = Clamp(interior.W * interior.H / 8, 1, 6);

            var cells = new List<TileRect>();
            Subdivide(interior, target, rng, cells);

            AssignKinds(cells, interior, frontDoor, exterior, result);
            CarveDoors(result, interior, frontDoor, rng);

            return result;
        }

        // ---------- subdivision ----------

        private static void Subdivide(TileRect area, int target, IRng rng, List<TileRect> output)
        {
            if (target <= 1 || (area.W < MinSide * 2 + 1 && area.H < MinSide * 2 + 1))
            {
                output.Add(area);
                return;
            }

            // Split the longer axis, so rooms stay squarish rather than degenerating into
            // corridors. Ties broken randomly to avoid every house looking the same.
            bool vertical = area.W > area.H || (area.W == area.H && rng.Chance(0.5f));

            int extent = vertical ? area.W : area.H;
            if (extent < MinSide * 2 + 1)
            {
                vertical = !vertical;
                extent = vertical ? area.W : area.H;
                if (extent < MinSide * 2 + 1) { output.Add(area); return; }
            }

            // The wall consumes one tile, so both sides need MinSide clear.
            int lowest = MinSide;
            int highest = extent - MinSide - 1;
            if (highest < lowest) { output.Add(area); return; }

            // Bias toward the middle: a house divided 1:4 looks like a mistake.
            int at = lowest + rng.NextInt(highest - lowest + 1);
            int middle = extent / 2;
            at = (at + middle) / 2;
            at = Clamp(at, lowest, highest);

            TileRect first, second;
            if (vertical)
            {
                first = new TileRect(area.X, area.Y, at, area.H);
                second = new TileRect(area.X + at + 1, area.Y, area.W - at - 1, area.H);
            }
            else
            {
                first = new TileRect(area.X, area.Y, area.W, at);
                second = new TileRect(area.X, area.Y + at + 1, area.W, area.H - at - 1);
            }

            // Split the bigger half more, so a large room does not sit beside four small ones.
            int firstShare = Math.Max(1, target * Area(first) / Math.Max(1, Area(first) + Area(second)));
            Subdivide(first, firstShare, rng, output);
            Subdivide(second, target - firstShare, rng, output);
        }

        private static int Area(TileRect r) => r.W * r.H;

        // ---------- what each room is for ----------

        private static void AssignKinds(List<TileRect> cells, TileRect interior, Tile frontDoor,
                                        TileRect exterior, Interior result)
        {
            if (cells.Count == 0) return;

            // Order by area so the rules below can talk about "the smallest" and "the largest".
            cells.Sort((a, b) => Area(b).CompareTo(Area(a)));

            int entrance = NearestTo(cells, frontDoor);
            var kinds = new RoomKind[cells.Count];
            var taken = new bool[cells.Count];

            // ---- priority order, and it is the whole trick ----
            //
            // Rooms are assigned in order of what a house cannot do without, not in order of
            // size. An earlier version handed out hall, kitchen, bathroom and front room first
            // and ran out of space before any bedroom - producing a four-room house, occupied
            // by a family of three, with nowhere to sleep.
            //
            // A small cottage with a kitchen and one bedroom and no front room is a real
            // house. One with a front room and no bed is not.
            //
            //   hall -> kitchen -> bedroom -> bathroom -> front room -> more bedrooms
            //
            // A three-room house therefore ends up hall/kitchen/bedroom, and it has a bathroom -
            // see the threshold below, and the reason it is three rather than four.

            kinds[entrance] = cells.Count <= 2 ? RoomKind.Living : RoomKind.Hall;
            taken[entrance] = true;

            // Bathroom first, and specifically the SMALLEST room. Claiming it early is the
            // point: assigned later it ends up with whatever is left over, which produced
            // houses with a twelve-square-metre bathroom and a nine-metre bedroom.
            // A BATHROOM AT THREE ROOMS, NOT FOUR, AND THE BAR MOVED BECAUSE THE TOWN DID.
            //
            // This said: "a house only gets one at all if there are four rooms to go round - a
            // three-room cottage in 1979 has the privy outside, which is a fact rather than an
            // oversight." It is a fact about rural England in 1979. Rossville has run its own
            // water and sewer continuously since 1898 - Content/kinds.txt says so, citing
            // docs/research/ROSSVILLE-HISTORY.md - so an occupied dwelling here in 1991 with no
            // bathroom in it does not exist.
            //
            // MEASURED BEFORE CHANGING IT, because rooms are what the whole simulation walks
            // around in and a room count is not a thing to guess at. SmokeTest, 2026-08-09:
            //
            //   608 dwellings, 601 with a bathroom (98%)
            //   rooms per house   1:1   2:1   3:5   4:24   5:124   6:453
            //
            // So this moves FIVE houses, to 606 of 608. The two that still have none have one
            // room and two rooms, and a one-room dwelling with a bathroom carved out of it would
            // be a bathroom and nothing else. That is the right place for the bar to sit.
            if (cells.Count >= 3)
            {
                int bathroom = SmallestFree(cells, taken);
                if (bathroom >= 0) { kinds[bathroom] = RoomKind.Bathroom; taken[bathroom] = true; }
            }

            // The kitchen wants an outside wall, for a window and a back door. Cells are
            // area-ordered, so the first match is the largest one that qualifies.
            //
            // AND IT YIELDS TO THE BED WHEN THERE IS ONLY ONE ROOM LEFT. Dropping the bathroom
            // bar to three took the third room of a three-room house, and hall + kitchen +
            // bathroom is a house with NOWHERE TO SLEEP - which the fixture suite said out loud
            // on the next run, correctly, and which is the reason this ordering is worth writing
            // down rather than leaving to the sequence of statements.
            //
            // A three-room house in Rossville is a bedroom, a bathroom and one room you both
            // cook and sit in. That is what a small frame house on a fifty-foot lot is, and it is
            // what the entrance cell becomes below when nothing else claims it.
            bool keepOneForABed = Free(taken) <= 1;
            if (!keepOneForABed)
            {
                int kitchen = FirstFree(cells, taken, interior, requireEdge: true);
                if (kitchen < 0) kitchen = FirstFree(cells, taken, interior, requireEdge: false);
                if (kitchen >= 0) { kinds[kitchen] = RoomKind.Kitchen; taken[kitchen] = true; }
            }

            // At least one bedroom, always. Take a middling room rather than the largest -
            // the biggest room in a house is the one you sit in, not the one you sleep in.
            int firstBedroom = MiddlingFree(cells, taken);
            if (firstBedroom >= 0) { kinds[firstBedroom] = RoomKind.Bedroom; taken[firstBedroom] = true; }

            // Front room: the largest thing still going spare.
            int living = FirstFree(cells, taken, interior, requireEdge: false);
            if (living >= 0) { kinds[living] = RoomKind.Living; taken[living] = true; }

            // Everything after that is another bedroom.
            for (int i = 0; i < cells.Count; i++)
                if (!taken[i]) { kinds[i] = RoomKind.Bedroom; taken[i] = true; }

            // A room too small to hold a bed is a scullery, not a bedroom.
            for (int i = 0; i < cells.Count; i++)
                if (kinds[i] == RoomKind.Bedroom && Area(cells[i]) < 6)
                    kinds[i] = RoomKind.Scullery;

            for (int i = 0; i < cells.Count; i++)
                result.Rooms.Add((cells[i], kinds[i]));
        }

        /// <summary>How many cells nothing has claimed yet.</summary>
        private static int Free(bool[] taken)
        {
            int n = 0;
            for (int i = 0; i < taken.Length; i++) if (!taken[i]) n++;
            return n;
        }

        /// <summary>Largest unassigned room, optionally requiring an exterior wall.</summary>
        private static int FirstFree(List<TileRect> cells, bool[] taken, TileRect interior, bool requireEdge)
        {
            for (int i = 0; i < cells.Count; i++)          // cells are sorted largest first
            {
                if (taken[i]) continue;
                if (requireEdge && !TouchesEdge(cells[i], interior)) continue;
                return i;
            }
            return -1;
        }

        private static int SmallestFree(List<TileRect> cells, bool[] taken)
        {
            for (int i = cells.Count - 1; i >= 0; i--)
                if (!taken[i]) return i;
            return -1;
        }

        /// <summary>
        /// A room from the middle of the size range. Bedrooms should not be the grandest room
        /// in the house nor the broom cupboard.
        /// </summary>
        private static int MiddlingFree(List<TileRect> cells, bool[] taken)
        {
            var free = new List<int>();
            for (int i = 0; i < cells.Count; i++) if (!taken[i]) free.Add(i);
            if (free.Count == 0) return -1;
            return free[free.Count / 2];
        }

        private static bool TouchesEdge(TileRect room, TileRect interior) =>
            room.X == interior.X || room.Y == interior.Y ||
            room.Right == interior.Right || room.Bottom == interior.Bottom;

        private static int NearestTo(List<TileRect> cells, Tile point)
        {
            int best = 0, bestDist = int.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i].Centre;
                int d = Tile.DistanceSquared(c, point);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // ---------- doors ----------

        /// <summary>
        /// Connect every room to the entrance with exactly one door each, growing outward from
        /// the front door. A spanning tree rather than a door between every adjacent pair: a
        /// house where every room opens into every other is a maze, not a home.
        /// </summary>
        private static void CarveDoors(Interior interior, TileRect area, Tile frontDoor, IRng rng)
        {
            int n = interior.Rooms.Count;
            if (n <= 1) return;

            int start = 0;
            int bestDist = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int d = Tile.DistanceSquared(interior.Rooms[i].bounds.Centre, frontDoor);
                if (d < bestDist) { bestDist = d; start = i; }
            }

            var connected = new bool[n];
            connected[start] = true;
            int remaining = n - 1;

            int guard = 0;
            while (remaining > 0 && guard++ < n * n + 8)
            {
                bool progressed = false;

                for (int a = 0; a < n && remaining > 0; a++)
                {
                    if (!connected[a]) continue;

                    for (int b = 0; b < n; b++)
                    {
                        if (connected[b] || a == b) continue;

                        // Bedrooms and bathrooms are dead ends: reach them from circulation,
                        // never through each other.
                        var kindA = interior.Rooms[a].kind;
                        if (kindA == RoomKind.Bedroom || kindA == RoomKind.Bathroom) continue;

                        if (!InteriorGeometry.TryDoorBetween(interior.Rooms[a].bounds, interior.Rooms[b].bounds,
                                                             rng, out var door)) continue;

                        interior.Doors.Add(door);
                        connected[b] = true;
                        remaining--;
                        progressed = true;
                        break;
                    }
                }

                // If the private-room rule has stranded something, relax it rather than leave
                // a room nobody can enter.
                if (!progressed)
                {
                    for (int a = 0; a < n && remaining > 0; a++)
                    {
                        if (!connected[a]) continue;
                        for (int b = 0; b < n; b++)
                        {
                            if (connected[b] || a == b) continue;
                            if (!InteriorGeometry.TryDoorBetween(interior.Rooms[a].bounds, interior.Rooms[b].bounds,
                                                                 rng, out var door)) continue;
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

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
