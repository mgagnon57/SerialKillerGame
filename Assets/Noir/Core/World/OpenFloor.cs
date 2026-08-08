using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// One room the public is in, and everything else behind it. Shops, pubs, the cinema, the
    /// barber, the garage.
    ///
    /// The rule this encodes: a place of trade is ONE space, and the line between the public
    /// and the staff is the only wall in it that matters. Subdivide a shop and you get a house
    /// with a till in it. The stock room is not a room the shop has, it is the shop's back;
    /// which is why it is a strip across the whole width rather than a room among rooms, and
    /// why it is always on the far side from where you came in.
    ///
    /// The cinema turns that round: its service strip is at the door end, because the foyer is
    /// the thing between the street and the auditorium. Same grammar, same strip, other end -
    /// and the auditorium then runs to the far wall, which is where the screen has to be.
    ///
    /// The public room keeps at least four metres of depth. Below that it is a corridor with a
    /// counter in it, and the strip is simply not built.
    /// </summary>
    public sealed class OpenFloor : IInteriorGrammar
    {
        /// <summary>Shallower than this and it is not somewhere you could stand and be served.</summary>
        private const int MinPublicDepth = 4;

        public string Name => "open";

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

            // Depth runs away from the street, so the strip lies across the frontage.
            bool deepInY = door.X != exterior.X && door.X != exterior.Right;

            int frontage = deepInY ? interior.W : interior.H;
            int depth = deepInY ? interior.H : interior.W;
            bool doorAtLow = (deepInY ? door.Y : door.X) < (deepInY ? interior.Centre.Y : interior.Centre.X);

            int strip = brief.ServiceDepth;
            if (strip > depth / 3) strip = depth / 3;
            if (brief.Service.Length == 0 || strip < InteriorGeometry.MinSide
                || depth - strip - 1 < MinPublicDepth)
            {
                // No room for a back: one room, and it is the whole business.
                result.Rooms.Add((interior, brief.Main));
                return result;
            }

            // The strip sits at the door end for a foyer and at the far end for a stock room.
            bool stripAtLow = brief.ServiceAtDoor == doorAtLow;
            int stripFrom = stripAtLow ? 0 : depth - strip;
            int publicFrom = stripAtLow ? strip + 1 : 0;
            int publicDepth = depth - strip - 1;

            var floor = Band(interior, deepInY, publicFrom, publicDepth);

            int parts = InteriorGeometry.Parts(frontage, brief.ServiceSpan);
            if (parts > brief.Service.Length) parts = brief.Service.Length;

            var starts = new List<int>();
            var sizes = new List<int>();
            int a0 = deepInY ? interior.X : interior.Y;
            InteriorGeometry.Slice(a0, frontage, parts, rng, starts, sizes);

            var rooms = new List<TileRect>();
            for (int i = 0; i < starts.Count; i++)
                rooms.Add(deepInY
                    ? new TileRect(starts[i], interior.Y + stripFrom, sizes[i], strip)
                    : new TileRect(interior.X + stripFrom, starts[i], strip, sizes[i]));

            result.Rooms.Add((floor, brief.Main));
            foreach (var (bounds, kind) in AssignStrip(rooms, brief, door))
                result.Rooms.Add((bounds, kind));

            InteriorGeometry.Connect(result, 0, rng);
            return result;
        }

        private static TileRect Band(TileRect interior, bool deepInY, int from, int size) =>
            deepInY
                ? new TileRect(interior.X, interior.Y + from, interior.W, size)
                : new TileRect(interior.X + from, interior.Y, size, interior.H);

        /// <summary>
        /// What the back of the house is, in the order the trade cannot do without it: the
        /// cellar before the gents, the stock room before the office. The biggest gets the
        /// first thing on the list - except that when the strip is the way in, the room the
        /// door actually opens into has to be the foyer whatever size it is.
        /// </summary>
        private static List<(TileRect, RoomKind)> AssignStrip(List<TileRect> rooms, Brief brief, Tile door)
        {
            var output = new List<(TileRect, RoomKind)>(rooms.Count);
            var taken = new bool[rooms.Count];
            var kinds = new RoomKind[rooms.Count];
            int next = 0;

            if (brief.ServiceAtDoor)
            {
                int at = InteriorGeometry.NearestTo(rooms, door);
                kinds[at] = brief.Service[next++];
                taken[at] = true;
            }

            foreach (int i in InteriorGeometry.ByAreaDescending(rooms))
            {
                if (taken[i]) continue;
                kinds[i] = brief.Service[next < brief.Service.Length ? next++ : brief.Service.Length - 1];
                taken[i] = true;
            }

            for (int i = 0; i < rooms.Count; i++) output.Add((rooms[i], kinds[i]));
            return output;
        }

        /// <summary>The trade, and what it keeps out of sight.</summary>
        private readonly struct Brief
        {
            public readonly RoomKind Main;

            /// <summary>The strip behind (or in front of) the public room, in order of priority.</summary>
            public readonly RoomKind[] Service;

            /// <summary>True when the strip is between the street and the public room: a foyer.</summary>
            public readonly bool ServiceAtDoor;

            public readonly int ServiceDepth;
            public readonly int ServiceSpan;

            private Brief(RoomKind main, RoomKind[] service, bool atDoor, int depth, int span)
            {
                Main = main; Service = service; ServiceAtDoor = atDoor;
                ServiceDepth = depth; ServiceSpan = span;
            }

            public static Brief For(string programme)
            {
                switch (programme == null ? null : programme.ToLowerInvariant())
                {
                    case "shop":
                    case "grocer":
                    case "butcher":
                    case "baker":
                        return new Brief(RoomKind.ShopFloor,
                            new[] { RoomKind.StockRoom, RoomKind.Office }, false, 3, 4);

                    case "post":
                    case "postoffice":
                    case "bank":
                        return new Brief(RoomKind.ShopFloor,
                            new[] { RoomKind.Office, RoomKind.StockRoom }, false, 3, 4);

                    // The cellar first. A village pub can manage without a kitchen and cannot
                    // manage without somewhere to keep the beer.
                    case "tavern":
                    case "inn":
                        return new Brief(RoomKind.Bar,
                            new[] { RoomKind.StockRoom, RoomKind.Washroom, RoomKind.Kitchen }, false, 3, 4);

                    case "cafe":
                    case "tearoom":
                        return new Brief(RoomKind.Bar,
                            new[] { RoomKind.Kitchen, RoomKind.Washroom }, false, 3, 4);

                    case "cinema":
                    case "theatre":
                        return new Brief(RoomKind.Auditorium,
                            new[] { RoomKind.Foyer, RoomKind.Washroom, RoomKind.Office }, true, 3, 6);

                    case "barber":
                    case "hairdresser":
                        return new Brief(RoomKind.Salon,
                            new[] { RoomKind.StockRoom }, false, 2, 4);

                    case "garage":
                    case "workshop":
                    case "mill":
                        return new Brief(RoomKind.Workshop,
                            new[] { RoomKind.Office, RoomKind.StockRoom }, false, 4, 5);

                    default:
                        return new Brief(RoomKind.ShopFloor,
                            new[] { RoomKind.StockRoom, RoomKind.Office }, false, 3, 4);
                }
            }
        }
    }
}
