using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>The rooms and doorways generated for one building.</summary>
    public sealed class Interior
    {
        public readonly List<(TileRect bounds, RoomKind kind)> Rooms = new List<(TileRect, RoomKind)>();
        public readonly List<Tile> Doors = new List<Tile>();
        public readonly List<(Tile a, Tile b)> Walls = new List<(Tile, Tile)>();

        /// <summary>
        /// What the owner called each room in <see cref="Rooms"/>, index for index. Only
        /// <see cref="WorldBuilder"/>'s authored branch fills this in ("" per adopted room
        /// with no name of its own); a generated interior leaves it empty, and a lookup past
        /// its end reads as "" - a generated room has none.
        /// </summary>
        public readonly List<string> Names = new List<string>();
    }

    /// <summary>
    /// Divides a building footprint into rooms.
    ///
    /// This used to BE the house generator. It is now where a building is asked what sort of
    /// building it is: the kind table names a grammar, <see cref="InteriorGrammars"/> finds it,
    /// and the grammar decides what rooms there are. The house rules moved intact into
    /// <see cref="DomesticBsp"/> and are still what a dwelling gets.
    ///
    /// The split exists because those rules were being applied to everything with a wall round
    /// it. They say a building has one room per eight square metres, that the biggest room is
    /// the front room and that the smallest is the bathroom - all true of houses, and together
    /// they turned the hospital into a 330 m2 kitchen with two bedrooms off it.
    ///
    /// Deterministic in (building, seed): the same cottage has the same layout forever, which
    /// matters because the player is meant to learn this village.
    /// </summary>
    public static class InteriorGenerator
    {
        /// <summary>Whether anything answers to this grammar name. The kind table checks its rows.</summary>
        public static bool IsKnown(string grammar) => InteriorGrammars.IsKnown(grammar);

        /// <summary>Every grammar name there is, for the error message when a row asks for one there isn't.</summary>
        public static string[] Names() => InteriorGrammars.Names();

        public static Interior Generate(TileRect exterior, Tile frontDoor, IRng rng) =>
            Generate(exterior, frontDoor, rng, null, null);

        /// <summary>
        /// The dispatch point.
        ///
        /// <paramref name="grammar"/> is how the building is arranged and <paramref name="programme"/>
        /// is what its rooms are for - a hospital and a school are the same corridor with
        /// different things off it, so the programme is simply the place kind's own name. A
        /// grammar this does not recognise falls back to the house rules, which is wrong for a
        /// hospital but is never wrong for a building.
        /// </summary>
        public static Interior Generate(TileRect exterior, Tile frontDoor, IRng rng,
                                        string grammar, string programme)
        {
            var chosen = InteriorGrammars.Find(grammar) ?? InteriorGrammars.Domestic;
            return chosen.Generate(exterior, frontDoor, programme, rng);
        }
    }
}
