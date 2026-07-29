using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// One volume that IS the building, with the small stuff pushed to the far end. The church,
    /// the village hall.
    ///
    /// The rule this encodes: some buildings are a single room, and everything else in them is
    /// an apology for interrupting it. A nave is not the largest room in a church, it is the
    /// church; the vestry is a cupboard that got ideas. So the volume is guaranteed two thirds
    /// of the depth and at least five metres of it, and if the ancillary strip cannot be had
    /// without breaking that, it is not built and the building is one room. That is the
    /// difference from <see cref="OpenFloor"/>, which will give a shop up rather than go
    /// without somewhere to keep the stock.
    ///
    /// The strip goes at the FAR end, past the focus - the vestry behind the altar, the kitchen
    /// behind the stage - so that the length of the hall is between the door and the thing
    /// everybody is looking at.
    /// </summary>
    public sealed class HallGrammar : IInteriorGrammar
    {
        private const int MinHallDepth = 5;

        public string Name => "hall";

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

            bool deepInY = door.X != exterior.X && door.X != exterior.Right;
            int frontage = deepInY ? interior.W : interior.H;
            int depth = deepInY ? interior.H : interior.W;
            bool doorAtLow = (deepInY ? door.Y : door.X) < (deepInY ? interior.Centre.Y : interior.Centre.X);

            int strip = depth >= 14 ? 3 : 2;
            int hallDepth = depth - strip - 1;

            // Two thirds of the depth, or the strip does not happen and the building is one room.
            if (brief.Ancillary.Length == 0 || hallDepth < MinHallDepth || hallDepth * 3 < depth * 2)
            {
                result.Rooms.Add((interior, brief.Main));
                return result;
            }

            int stripFrom = doorAtLow ? depth - strip : 0;
            int hallFrom = doorAtLow ? 0 : strip + 1;

            var hall = deepInY
                ? new TileRect(interior.X, interior.Y + hallFrom, interior.W, hallDepth)
                : new TileRect(interior.X + hallFrom, interior.Y, hallDepth, interior.H);

            int parts = InteriorGeometry.Parts(frontage, 5);
            if (parts > brief.Ancillary.Length) parts = brief.Ancillary.Length;

            var starts = new List<int>();
            var sizes = new List<int>();
            InteriorGeometry.Slice(deepInY ? interior.X : interior.Y, frontage, parts, rng, starts, sizes);

            var rooms = new List<TileRect>();
            for (int i = 0; i < starts.Count; i++)
                rooms.Add(deepInY
                    ? new TileRect(starts[i], interior.Y + stripFrom, sizes[i], strip)
                    : new TileRect(interior.X + stripFrom, starts[i], strip, sizes[i]));

            result.Rooms.Add((hall, brief.Main));

            int next = 0;
            var kinds = new RoomKind[rooms.Count];
            foreach (int i in InteriorGeometry.ByAreaDescending(rooms))
                kinds[i] = brief.Ancillary[next < brief.Ancillary.Length ? next++ : brief.Ancillary.Length - 1];
            for (int i = 0; i < rooms.Count; i++) result.Rooms.Add((rooms[i], kinds[i]));

            InteriorGeometry.Connect(result, 0, rng);
            return result;
        }

        /// <summary>The volume, and the rooms that are only there to serve it.</summary>
        private readonly struct Brief
        {
            public readonly RoomKind Main;
            public readonly RoomKind[] Ancillary;

            private Brief(RoomKind main, RoomKind[] ancillary) { Main = main; Ancillary = ancillary; }

            public static Brief For(string programme)
            {
                switch (programme == null ? null : programme.ToLowerInvariant())
                {
                    case "church":
                    case "chapel":
                        return new Brief(RoomKind.Nave, new[] { RoomKind.Office, RoomKind.StockRoom });

                    // The Women's Institute needs a kitchen more than it needs anything else,
                    // and the jumble has to be kept somewhere between springs.
                    default:
                        return new Brief(RoomKind.Assembly,
                            new[] { RoomKind.Kitchen, RoomKind.StockRoom, RoomKind.Washroom });
                }
            }
        }
    }
}
