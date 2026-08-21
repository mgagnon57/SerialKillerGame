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

    /// <summary>One piece of furniture the owner placed, already in tile space.</summary>
    public readonly struct AuthoredFurniture
    {
        public readonly FurnitureKind Kind;
        public readonly TileRect Footprint;

        /// <summary>A model name for the Unity side to bind. "" resolves by kind.</summary>
        public readonly string Model;

        public AuthoredFurniture(FurnitureKind kind, TileRect footprint, string model = "")
        {
            Kind = kind;
            Footprint = footprint;
            Model = model ?? "";
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
        /// Furniture the owner placed by hand. Empty by default. Any piece here replaces the
        /// generated furnishing of this building wholesale - see <see cref="Furnish"/> for the
        /// other half of that rule.
        /// </summary>
        public readonly List<AuthoredFurniture> Furniture = new List<AuthoredFurniture>();

        /// <summary>
        /// False when a hand-made model owns the visible inside (a Content/models.txt
        /// building): generated furniture would double up inside his mesh. Authored
        /// furniture is stamped regardless — the plan is his hand either way.
        /// </summary>
        public bool Furnish = true;
    }
}
