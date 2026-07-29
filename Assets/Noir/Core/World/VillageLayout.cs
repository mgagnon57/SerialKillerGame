using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>Parsed, not-yet-rasterised description of the village. Pure data.</summary>
    public sealed class VillageLayout
    {
        public string Name = "Unnamed";
        public int Width = 64;
        public int Height = 64;

        public readonly List<TerrainPatch> Terrain = new List<TerrainPatch>();
        public readonly List<RoadRun> Roads = new List<RoadRun>();
        public readonly List<PlaceSpec> Places = new List<PlaceSpec>();
    }

    public sealed class TerrainPatch
    {
        public Terrain Kind;
        public TileRect Area;
    }

    public sealed class RoadRun
    {
        public Terrain Kind;          // Road or Path
        public int Width = 3;
        public readonly List<Tile> Points = new List<Tile>();
    }

    public sealed class PlaceSpec
    {
        public PlaceKind Kind;
        public string Name = "";
        public string Human = "";
        public TileRect Bounds;
        public Tile Door = Tile.None;
        public int JobSlots;

        /// <summary>
        /// What this place is called for the purpose of generating what is inside it. Empty
        /// means "use the name", which is what almost everything should do.
        ///
        /// It exists for the two cases the name cannot cover: two places that genuinely share a
        /// name, and renaming a building without wanting its interior rebuilt. Everything
        /// generated from a place hangs off this string, so changing it is the same act as
        /// demolishing the building and putting up a different one.
        /// </summary>
        public string Key = "";

        /// <summary>Separate homes inside this building. 1 = a house, 4 = a terrace, 10 = flats.</summary>
        public int Units = 1;
        public readonly List<OpenWindow> Hours = new List<OpenWindow>();

        /// <summary>
        /// Outdoor places are drawn as open ground, without walls or a door. Which are which is
        /// the `form` column in kinds.txt: this used to be a list of exceptions here and another
        /// one in the renderer, and they had drifted apart.
        /// </summary>
        public bool IsBuilding => PlaceKindTable.Current.Row(Kind).IsBuilding;
    }
}
