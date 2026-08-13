using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// A corridor, with rooms off it. Hospitals, schools, offices, the surgery.
    ///
    /// The rule this encodes: in an institution the CIRCULATION is the building, and every
    /// room is a thing that hangs off it. That is the opposite of a house, where circulation is
    /// what is left over once the rooms have been placed - and it is why a house generator can
    /// never produce a hospital however many rooms you give it. A ward off a corridor is what
    /// makes a hospital read as a hospital from above with the roof lifted; six rooms in a ring
    /// is a very large bungalow.
    ///
    /// Consequences, all of which are the same rule stated again:
    ///
    ///   * the spine runs the whole length of the longer axis, wall to wall, and is the only
    ///     room that touches every other one;
    ///   * you never pass through one ward to reach another;
    ///   * rooms come in a band down each side, and a strip too thin to be a room is given back
    ///     to the corridor rather than left as a block of wall nobody can enter;
    ///   * the room by the front door is the one the public is meant to arrive in - reception,
    ///     the waiting room, the school office - and never a ward.
    ///
    /// The programme decides what the rooms are. Shape alone does not distinguish a hospital
    /// from a school, and it should not: they are the same building with different signage
    /// until you look at what is in the rooms.
    /// </summary>
    public sealed class SpineCorridor : IInteriorGrammar
    {
        /// <summary>Below this a band is not worth putting rooms in, so the corridor gets it.</summary>
        private const int MinBand = 4;

        public string Name => "spine";

        public Interior Generate(TileRect exterior, Tile frontDoor, string programme, IRng rng)
        {
            var brief = Brief.For(programme);
            var result = new Interior();

            var interior = new TileRect(exterior.X + 1, exterior.Y + 1, exterior.W - 2, exterior.H - 2);
            if (interior.W < InteriorGeometry.MinSide || interior.H < InteriorGeometry.MinSide)
            {
                if (interior.W > 0 && interior.H > 0) result.Rooms.Add((interior, brief.Main));
                return result;
            }

            var door = frontDoor.IsValid ? frontDoor : interior.Centre;

            // The spine takes the long way through the building. Anything else wastes the length
            // it has and leaves rooms wider than they are deep.
            bool horizontal = interior.W > interior.H || (interior.W == interior.H && rng.Chance(0.5f));

            int a0 = horizontal ? interior.X : interior.Y;
            int aLen = horizontal ? interior.W : interior.H;
            int c0 = horizontal ? interior.Y : interior.X;
            int cLen = horizontal ? interior.H : interior.W;

            int doorAlong = horizontal ? door.X : door.Y;
            int doorCross = horizontal ? door.Y : door.X;
            bool onEndWall = doorAlong == a0 - 1 || doorAlong == a0 + aLen;

            // Two metres of corridor wherever it can be afforded - one is a passage you turn
            // sideways in, which no institution has. Rooms on both sides cost eight of those
            // metres before the corridor gets any, so a narrow building spends them on rooms
            // and puts the corridor down one side.
            int width = cLen >= 12 ? 2 : 1;
            bool twoSided = cLen >= width + 2 + 2 * MinBand;
            if (!twoSided) width = cLen >= 8 ? 2 : 1;

            int spine = Spine(c0, cLen, width, twoSided, doorCross, onEndWall, rng);

            // A door in the end wall that lines up with the corridor's own wall would be
            // tunnelled the whole length of the building by WorldBuilder looking for floor.
            // One tile sideways and it opens into the corridor instead.
            if (onEndWall)
            {
                if (doorCross == spine - 1 && spine > c0) spine--;
                else if (doorCross == spine + width && spine + width < c0 + cLen) spine++;
            }

            int lowDepth = spine - 1 - c0;
            int highFrom = spine + width + 1;
            int highDepth = c0 + cLen - highFrom;
            bool lowBand = lowDepth >= InteriorGeometry.MinSide;
            bool highBand = highDepth >= InteriorGeometry.MinSide;

            if (!lowBand && !highBand)
            {
                // Nothing left over to put a room in, so there is nothing for a corridor to
                // serve. Whatever this building is, at this size it is one room of it.
                result.Rooms.Add((interior, brief.Main));
                return result;
            }

            int from = lowBand ? spine : c0;
            int to = highBand ? spine + width - 1 : c0 + cLen - 1;
            var corridor = horizontal
                ? new TileRect(a0, from, aLen, to - from + 1)
                : new TileRect(from, a0, to - from + 1, aLen);

            // Only a door in a long wall can collide with a dividing wall between two rooms.
            int guardAlong = onEndWall ? int.MinValue : doorAlong;

            var rooms = new List<TileRect>();
            if (lowBand) AddBand(rooms, c0, lowDepth, horizontal, a0, aLen, brief.MainSpan, guardAlong, rng);
            if (highBand) AddBand(rooms, highFrom, highDepth, horizontal, a0, aLen, brief.MainSpan, guardAlong, rng);

            var kinds = Assign(rooms, brief, door);

            result.Rooms.Add((corridor, RoomKind.Corridor));
            for (int i = 0; i < rooms.Count; i++) result.Rooms.Add((rooms[i], kinds[i]));

            InteriorGeometry.Connect(result, 0, rng);
            return result;
        }

        /// <summary>Where the corridor sits across the building.</summary>
        private static int Spine(int c0, int cLen, int width, bool twoSided, int doorCross,
                                 bool onEndWall, IRng rng)
        {
            if (!twoSided)
            {
                // One band only, so the corridor hugs a wall - and it hugs the wall the front
                // door is in, so you walk straight into it rather than through a classroom.
                bool low = doorCross == c0 - 1 || (doorCross != c0 + cLen && rng.Chance(0.5f));
                return low ? c0 : c0 + cLen - width;
            }

            int bands = cLen - width - 2;

            // A door in the end wall means the corridor is straight ahead as you come in.
            if (onEndWall)
                return InteriorGeometry.Clamp(doorCross - (width - 1) / 2,
                                              c0 + MinBand + 1, c0 + bands - MinBand + 1);

            int near = MinBand + (bands - 2 * MinBand > 0 ? rng.NextInt(bands - 2 * MinBand + 1) : 0);
            near = (near + bands / 2) / 2;      // bias to the middle, as the house splitter does
            return c0 + InteriorGeometry.Clamp(near, MinBand, bands - MinBand) + 1;
        }

        /// <summary>One side of the corridor, cut into rooms.</summary>
        private static void AddBand(List<TileRect> rooms, int from, int depth, bool horizontal,
                                    int a0, int aLen, int span, int doorAlong, IRng rng)
        {
            // A corridor with one room off it is a hallway. If two will fit, two there are.
            int parts = InteriorGeometry.Parts(aLen, span);
            if (parts < 2 && aLen >= 2 * InteriorGeometry.MinSide + 1) parts = 2;

            var starts = new List<int>();
            var sizes = new List<int>();
            InteriorGeometry.Slice(a0, aLen, parts, rng, starts, sizes);
            if (doorAlong != int.MinValue) InteriorGeometry.ClearDoorLine(starts, sizes, doorAlong);

            for (int i = 0; i < starts.Count; i++)
                rooms.Add(horizontal
                    ? new TileRect(starts[i], from, sizes[i], depth)
                    : new TileRect(from, starts[i], depth, sizes[i]));
        }

        /// <summary>
        /// What each room off the corridor is for.
        ///
        /// Same shape of argument as the house rules, and for the same reason. A hospital
        /// without a linen store is still a hospital; a hospital that is all linen stores is
        /// not. So the support rooms are taken from the SMALLEST left over, they are never
        /// allowed to eat the last two rooms, and everything big that remains is what the
        /// building exists for.
        /// </summary>
        private static RoomKind[] Assign(List<TileRect> rooms, Brief brief, Tile door)
        {
            var kinds = new RoomKind[rooms.Count];
            var taken = new bool[rooms.Count];
            var order = InteriorGeometry.ByAreaDescending(rooms);
            int free = rooms.Count;

            // A two-room school is two classrooms, and the head teacher's office is her desk in
            // the corner of one of them. A two-room surgery still has a waiting room, because
            // nobody is examined in front of the next patient - so how many rooms it takes
            // before the public gets one to arrive in is a fact about the programme.
            if (rooms.Count >= brief.EntranceNeeds)
            {
                int entrance = InteriorGeometry.NearestTo(rooms, door);
                kinds[entrance] = brief.Entrance;
                taken[entrance] = true;
                free--;
            }

            for (int s = 0; s < brief.Support.Length && free > 2; s++)
            {
                for (int i = order.Length - 1; i >= 0; i--)
                {
                    if (taken[order[i]]) continue;
                    kinds[order[i]] = brief.Support[s];
                    taken[order[i]] = true;
                    free--;
                    break;
                }
            }

            // Two thirds of what is left, and the biggest two thirds. The rest are the rooms
            // that go with the main ones: consulting rooms beside the wards, an office beside
            // the classrooms.
            int main = free - free / 3;
            for (int i = 0; i < order.Length; i++)
            {
                if (taken[order[i]]) continue;
                kinds[order[i]] = main > 0 ? brief.Main : brief.Secondary;
                taken[order[i]] = true;
                main--;
            }
            return kinds;
        }

        /// <summary>The schedule of accommodation: what hangs off the corridor, and how wide.</summary>
        private readonly struct Brief
        {
            public readonly RoomKind Entrance;
            public readonly RoomKind Main;
            public readonly RoomKind Secondary;

            /// <summary>Taken from the smallest rooms first, in the order the building needs them.</summary>
            public readonly RoomKind[] Support;

            /// <summary>How wide a main room wants to be along the corridor. A classroom is not a ward.</summary>
            public readonly int MainSpan;

            /// <summary>How many rooms there must be before one is given over to arriving in.</summary>
            public readonly int EntranceNeeds;

            private Brief(RoomKind entrance, RoomKind main, RoomKind secondary, RoomKind[] support,
                          int span, int entranceNeeds)
            {
                Entrance = entrance; Main = main; Secondary = secondary; Support = support;
                MainSpan = span; EntranceNeeds = entranceNeeds;
            }

            public static Brief For(string programme)
            {
                switch (programme == null ? null : programme.ToLowerInvariant())
                {
                    case "hospital":
                    case "infirmary":
                        return new Brief(RoomKind.Waiting, RoomKind.Ward, RoomKind.Consulting,
                            new[] { RoomKind.Washroom, RoomKind.StockRoom, RoomKind.Office }, 6, 3);

                    case "school":
                        return new Brief(RoomKind.Foyer, RoomKind.Classroom, RoomKind.StaffRoom,
                            new[] { RoomKind.Washroom, RoomKind.StockRoom, RoomKind.Office }, 8, 4);

                    case "clinic":
                    case "dentist":
                        return new Brief(RoomKind.Waiting, RoomKind.Consulting, RoomKind.Office,
                            new[] { RoomKind.Washroom, RoomKind.StockRoom }, 5, 2);

                    // Offices, the council, the police station: rooms with a desk in them, off a
                    // corridor, which is the least a spine building can be.
                    default:
                        return new Brief(RoomKind.Foyer, RoomKind.Office, RoomKind.StaffRoom,
                            new[] { RoomKind.Washroom, RoomKind.StockRoom }, 4, 3);
                }
            }
        }
    }
}
