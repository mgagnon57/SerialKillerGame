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
        public string Name = "";
        public int Width = 3;
        public readonly List<Tile> Points = new List<Tile>();

        /// <summary>
        /// How wide the PUBLIC LAND is here, in metres - the right of way, which is the gap the
        /// lots leave rather than the part that gets paved. 0 means not measured.
        ///
        /// The corridor is pinned to the class and is what the road kit's tiles draw; this is
        /// the easement it sits inside. A Rossville street paves a 10 m corridor down the middle
        /// of a 20 m easement, so about 5 m each side is public ground that is NOT road - verge,
        /// utilities, and a sidewalk where there is one. Anything laying a walk needs this
        /// number, because measuring outward from the asphalt and guessing puts it in a hedge.
        /// </summary>
        public float Easement;

        /// <summary>
        /// What is painted on it, or null to take the obvious answer from the width.
        ///
        /// Optional because every map written before roads had classes leaves it unsaid, and
        /// those maps should keep meaning what they meant. See <see cref="EffectiveClass"/>.
        /// </summary>
        public RoadClass? Class;

        /// <summary>
        /// The class this road actually has: what was declared, or the widest class that fits
        /// inside the declared width.
        ///
        /// A corridor is only wide enough for a main road at thirty metres, so anything narrower
        /// is a street whatever it is called. A thirty-metre road that does not say otherwise is
        /// a main road rather than an arterial, because two lanes each way is the thing you
        /// should have to ask for.
        /// </summary>
        public RoadClass EffectiveClass =>
            Class ?? (Width >= RoadClasses.CorridorWidth(RoadClass.Mainroad)
                          ? RoadClass.Mainroad
                          : RoadClass.Street);
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
