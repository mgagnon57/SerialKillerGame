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

        /// <summary>
        /// What the road CARRIES, when that is not what it is paved to. See RoadLine.Carries for
        /// why the two are allowed to disagree - a county highway on a village street's ground.
        /// </summary>
        public RoadClass? Carries;

        /// <summary>The function this road actually has: what was declared, or its own class.</summary>
        public RoadClass EffectiveCarries => Carries ?? EffectiveClass;

        /// <summary>
        /// Annual Average Daily Traffic — how many vehicles a day the county actually counted on
        /// this road, both directions. 0 means NOT COUNTED, which is not the same as none.
        ///
        /// IDOT counts village streets by name, not just state routes, and it counts eleven of
        /// Rossville's. Route 1 is 5,200 a day, Attica 1,100, Church Street 200. Everything that
        /// wants to know how busy a road is asked its CLASS before this existed, and a class is a
        /// four-step ladder against a measured spread of twenty-one to one.
        /// See docs/research/TRAFFIC-COUNTS.md.
        /// </summary>
        public int Aadt;
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
        /// The building's real outline, in tiles, or null for a plain rectangle.
        ///
        /// A ring, not closed - the last point joins the first. It must lie within
        /// <see cref="Bounds"/>, which stays the rectangle everything else measures the place by:
        /// this changes the SHAPE the building is stamped in, not the extent it occupies.
        ///
        /// Nothing in the map file writes it and nothing in Core computes it. It exists so the
        /// Unity side can hand over a measured footprint - see SeatOnSurvey, which reads the
        /// traced outlines in Content/parcel-buildings.txt - without Core having to know that
        /// such a thing exists. A town with no survey behind it leaves this null everywhere and
        /// is stamped exactly as it was before this field.
        /// </summary>
        public Tile[] Outline;

        /// <summary>
        /// The interior the owner drew for this building, in tile space, or null for the
        /// generated one. Filled by the Unity survey side from Content/floorplans/; nothing
        /// in the map file writes it and nothing in Core computes it — the Outline pattern.
        /// </summary>
        public AuthoredInterior AuthoredInterior;

        /// <summary>
        /// The same ring as <see cref="Outline"/>, in continuous metres rather than tiles - or
        /// null to say nothing more precise than the tile-rounded ring was ever computed, which is
        /// what every caller except <c>DowntownFromSanborn</c> does today. See
        /// <see cref="Place.OutlinePrecise"/> for why this exists and where it actually changes
        /// what gets drawn.
        /// </summary>
        public Vec2[] OutlinePrecise;

        /// <summary>
        /// The wall's base height in metres, shared with this place's row neighbours - or null to
        /// say "measure it yourself", which is what every caller except <c>DowntownFromSanborn</c>
        /// does today. See <see cref="Place.GroundHeight"/> for why this exists.
        /// </summary>
        public float? GroundHeight;

        /// <summary>
        /// Outdoor places are drawn as open ground, without walls or a door. Which are which is
        /// the `form` column in kinds.txt: this used to be a list of exceptions here and another
        /// one in the renderer, and they had drifted apart.
        /// </summary>
        public bool IsBuilding => PlaceKindTable.Current.Row(Kind).IsBuilding;
    }
}
