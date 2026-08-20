using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>One room of an authored floor plan, already in tile space.</summary>
    public readonly struct AuthoredRoom
    {
        public readonly TileRect Bounds;
        public readonly RoomKind Kind;
        public readonly string Name;

        public AuthoredRoom(TileRect bounds, RoomKind kind, string name)
        {
            Bounds = bounds;
            Kind = kind;
            Name = name ?? "";
        }
    }

    /// <summary>
    /// A floor plan the owner drew, overriding the generated interior of one building.
    ///
    /// Core never reads Content/floorplans/ and never learns what a parcel is: the Unity
    /// survey side converts feet to oriented tiles and hands the result over on
    /// PlaceSpec.AuthoredInterior — the same hand-over Outline uses for the measured
    /// footprint. A fixture town never sets it and is stamped exactly as before.
    ///
    /// Spec: docs/superpowers/specs/2026-08-19-authored-interiors-design.md.
    /// </summary>
    public sealed class AuthoredInterior
    {
        public readonly List<AuthoredRoom> Rooms = new List<AuthoredRoom>();

        /// <summary>Interior doorway tiles — each should sit in a wall between two rooms.</summary>
        public readonly List<Tile> Doors = new List<Tile>();

        /// <summary>
        /// False when a hand-made model owns the visible inside (a Content/models.txt
        /// building): generated furniture would double up inside his mesh. Authored
        /// furniture is stamped regardless — the plan is his hand either way.
        /// </summary>
        public bool Furnish = true;
    }
}
