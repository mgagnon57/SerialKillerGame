using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    public enum PropKind : byte
    {
        Tree = 0,
        Bush,
        Hedge,
        Fence,
        GatePost,
        Bench,
        Postbox,
        Lamppost,
        Headstone,
        WaterTrough,

        /// <summary>
        /// Bank growth: cattail, rush and the long wet grass that rings standing water. Appended
        /// rather than filed next to Bush because this enum is a byte and its VALUES are what a
        /// prop is stored as - inserting in the middle renumbers every kind after it.
        /// </summary>
        Reed,
    }

    /// <summary>
    /// Something standing in the world that is not a building: a tree, a length of hedge, a
    /// bench on the green.
    ///
    /// Generated as data in Core rather than scattered by the renderer, for two reasons. It
    /// stays deterministic, so the village does not rearrange its own trees between runs. And
    /// it stays available to the simulation - a hedge is a thing you cannot see through, which
    /// matters a great deal later and costs nothing to record now.
    /// </summary>
    public readonly struct Prop
    {
        public readonly PropKind Kind;
        public readonly Tile At;

        /// <summary>0..255. Drives size and rotation so no two trees are identical.</summary>
        public readonly byte Variant;

        public Prop(PropKind kind, Tile at, byte variant)
        {
            Kind = kind;
            At = at;
            Variant = variant;
        }

        /// <summary>Whether this blocks a line of sight through its tile.</summary>
        public bool BlocksSight
        {
            get
            {
                switch (Kind)
                {
                    case PropKind.Tree:
                    case PropKind.Hedge:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public override string ToString() => $"{Kind} at {At}";
    }

    /// <summary>
    /// Scatters props across the village.
    ///
    /// Rules rather than noise. Trees crowd the spinney and thin out across open grass; hedges
    /// run along the road where there is no building fronting it; benches sit on the green
    /// facing the cricket. Pure noise gives you a village with a tree in the middle of the
    /// road, which is exactly what generated content is not allowed to look like.
    /// </summary>
    public static class PropGenerator
    {
        /// <summary>
        /// How much ground one stream is responsible for.
        ///
        /// Thirty-two metres square: small enough that an edit is contained near where it was
        /// made, large enough that the per-chunk stream setup is nothing beside the thousand
        /// tiles it covers. The size is part of the seed — changing it moves every prop in the
        /// village, exactly as changing a stream name would.
        /// </summary>
        private const int ChunkSize = 32;

        public static List<Prop> Generate(WorldModel world, ulong seed) =>
            Generate(world.Grid, world.AllPlaces, seed);

        /// <summary>
        /// Scatter props over a finished grid.
        ///
        /// Takes the grid and the places rather than a WorldModel because it is called from
        /// inside WorldBuilder, which used to assemble a throwaway model purely to satisfy this
        /// signature and then assemble the real one again — twice the room index, twice the
        /// counter layout, for nothing.
        ///
        /// A stream per chunk rather than one threaded through a row-major walk. The walk made
        /// every prop in the village downstream of every prop before it, so narrowing one
        /// building by one tile moved a hundred trees on the other side of the map.
        /// </summary>
        public static List<Prop> Generate(TileGrid grid, IReadOnlyList<Place> places, ulong seed)
        {
            var props = new List<Prop>();

            int chunksAcross = (grid.Width + ChunkSize - 1) / ChunkSize;
            int chunksDown = (grid.Height + ChunkSize - 1) / ChunkSize;

            for (int cy = 0; cy < chunksDown; cy++)
            for (int cx = 0; cx < chunksAcross; cx++)
            {
                var rng = Xoshiro256ss.Substream(seed, "props:" + cx + ":" + cy);

                int x1 = System.Math.Min((cx + 1) * ChunkSize, grid.Width);
                int y1 = System.Math.Min((cy + 1) * ChunkSize, grid.Height);

                for (int y = cy * ChunkSize; y < y1; y++)
                for (int x = cx * ChunkSize; x < x1; x++)
                    ScatterOn(grid, x, y, rng, props);
            }

            AddPlaceProps(grid, places, props, seed);
            return props;
        }

        private static void ScatterOn(TileGrid grid, int x, int y, IRng rng, List<Prop> props)
        {
            var terrain = grid.TerrainAt(x, y);
            if (!grid.IsWalkable(x, y)) return;
            if (grid.PlaceAt(x, y).IsValid && terrain == Terrain.Floor) return;

            // THE BANK, BEFORE ANYTHING ELSE GETS THIS TILE.
            //
            // Rossville's water is a rasterisation: features.txt carries the real North Fork and
            // the real school ponds as surveyed polygons with curves, and relay-rossville.py lays
            // them into city.txt as axis-aligned one-metre rectangles. The mesher then closes each
            // shore with a 35 cm vertical riser. So every shoreline in the town is a low wall
            // following a staircase, and it is the most visible straight line left on the ground -
            // see BANK in docs/TERRAIN-FIXES.md.
            //
            // Growing the edge is the honest answer as well as the cheap one: a farm pond in
            // Vermilion County IS ringed with cattail, and a bare mown edge would be the wrong
            // picture even if the geometry were perfect. It also costs the tile grid nothing,
            // which matters - the grid is what BlocksSight and walkability read, and moving water
            // tiles to soften the line would put open water back under the path across the school
            // field. That was b9c1271's bug and it is not coming back for a prettier bank.
            //
            // Not every shore tile: at 1.0 this is a kerb of reeds, which is the same mistake as
            // the hedge along every Illinois front yard below. Two in three leaves gaps to see the
            // water through, which is what a real bank has.
            if (NextToWater(grid, x, y) && rng.Chance(0.66f))
            {
                props.Add(new Prop(PropKind.Reed, new Tile(x, y), Roll(rng)));
                return;
            }

            switch (terrain)
            {
                case Terrain.Wood:
                    // CREEK TIMBER, NOT A PLANTATION. This is eastern Illinois: the trees are a
                    // ribbon along the North Fork and the drainage ditches, and the rest of the
                    // county was drained for corn - which is what the tile works going from
                    // 2,000 to 8,000 tiles a day between 1898 and 1906 was FOR.
                    //
                    // At 0.34 this put 39,638 trees on 98,000 tiles of wood, a tree every two
                    // and a half square metres. A wood you can walk into is nearer one every
                    // twenty-five, and this is 1.9% of the map producing half of everything the
                    // renderer had to bake.
                    if (rng.Chance(0.045f)) props.Add(new Prop(PropKind.Tree, new Tile(x, y), Roll(rng)));
                    else if (rng.Chance(0.020f)) props.Add(new Prop(PropKind.Bush, new Tile(x, y), Roll(rng)));
                    break;

                case Terrain.Grass:
                    if (NextToRoad(grid, x, y))
                    {
                        // AN ILLINOIS FRONT YARD RUNS OPEN TO THE SIDEWALK. There is no hedge
                        // along it, and that absence is most of what a Midwestern residential
                        // street looks like: mown grass from the porch to the kerb, a street
                        // tree every few lots, and a clear view down the whole block.
                        //
                        // This planted a hedge on 42% of every grass tile touching a road -
                        // 17,849 of them, FORTY-FIVE PER CENT of every prop in the town - and it
                        // came from Ashcombe, where a clipped roadside hedge is exactly right.
                        // A hedge down both verges of every street is the single most English
                        // thing the map had left in it, and it hid the openness that is the
                        // whole character of the place.
                        //
                        // A shrub here and there survives, because somebody does plant one by a
                        // driveway - but at a fortieth of the old rate, so it reads as a choice
                        // somebody made rather than as a boundary somebody laid.
                        if (rng.Chance(0.010f)) props.Add(new Prop(PropKind.Bush, new Tile(x, y), Roll(rng)));
                    }
                    // Grass is a yard in town AND open pasture out of it, and it is 27.7% of the
                    // map, so a rate tuned for a village green plants a park across the county.
                    else if (rng.Chance(0.0015f))
                    {
                        props.Add(new Prop(PropKind.Tree, new Tile(x, y), Roll(rng)));
                    }
                    else if (rng.Chance(0.0015f))
                    {
                        props.Add(new Prop(PropKind.Bush, new Tile(x, y), Roll(rng)));
                    }
                    break;

                case Terrain.Field:
                    // A ROADSIDE IN VERMILION COUNTY IS A DITCH, NOT A HEDGE.
                    //
                    // This said "a roadside is hedged, not post-and-railed", which is true of
                    // England and is why the map had one. A county road here runs between a mown
                    // verge and an open drainage ditch, with the corn coming to within a few feet
                    // of it; what breaks the line is a volunteer tree or a stand of weeds, not a
                    // laid boundary. The hedge that WAS here is the osage orange the settlers
                    // planted as living fence, and by 1991 nearly all of it had gone to the
                    // machinery that made the fields bigger.
                    //
                    // The reason the old comment gave for a hedge - that a fence is one prop per
                    // tile while a hedge is drawn as a RUN, so hedging a whole county's verges is
                    // affordable and fencing them is not - still holds, and is exactly why the
                    // answer is to put NOTHING along most of it. What remains of the old fence
                    // rows is drawn by OnFieldEdge below, where it belongs.
                    if (NextToRoad(grid, x, y))
                    {
                        if (rng.Chance(0.012f)) props.Add(new Prop(PropKind.Bush, new Tile(x, y), Roll(rng)));
                        else if (rng.Chance(0.004f)) props.Add(new Prop(PropKind.Tree, new Tile(x, y), Roll(rng)));
                    }
                    else if (OnFieldEdge(grid, x, y))
                    {
                        // THE FENCE ROW IS WHERE THE TREES ARE. Nothing grows in the middle of a
                        // worked field - it gets ploughed - but the line between two fields is
                        // never ploughed, so volunteer trees come up in it and stay. A line of
                        // them along a field boundary is the shape of this landscape from any
                        // distance, and it is the only tree in open country that belongs.
                        if (rng.Chance(0.045f))
                            props.Add(new Prop(PropKind.Tree, new Tile(x, y), Roll(rng)));
                        else if (rng.Chance(0.55f))
                            props.Add(new Prop(PropKind.Fence, new Tile(x, y), Roll(rng)));
                    }
                    // and NOTHING scattered in the crop itself. 0.004 over 2.99 million field
                    // tiles was 11,864 trees standing in standing corn.
                    break;

                case Terrain.Churchyard:
                    if (rng.Chance(0.30f)) props.Add(new Prop(PropKind.Headstone, new Tile(x, y), Roll(rng)));
                    else if (rng.Chance(0.04f)) props.Add(new Prop(PropKind.Tree, new Tile(x, y), Roll(rng)));
                    break;
            }
        }

        /// <summary>Furniture for the outdoors: benches on the green, a postbox by the shop.
        /// Keyed on the place, so the bench outside the hall does not move when a cottage two
        /// streets away is widened.</summary>
        private static void AddPlaceProps(TileGrid grid, IReadOnlyList<Place> places,
                                          List<Prop> props, ulong seed)
        {
            for (int i = 0; i < places.Count; i++)
            {
                var place = places[i];
                var style = PlaceKindTable.Current.Row(place.Kind).Props;
                if (style == PlacePropStyle.None) continue;

                var rng = Xoshiro256ss.Substream(seed, "placeprops:" + place.KeySource);

                switch (style)
                {
                    case PlacePropStyle.Benches:
                    {
                        int benches = 2 + rng.NextInt(3);
                        for (int b = 0; b < benches; b++)
                        {
                            int x = place.Bounds.X + 1 + rng.NextInt(System.Math.Max(1, place.Bounds.W - 2));
                            int y = place.Bounds.Y + 1 + rng.NextInt(System.Math.Max(1, place.Bounds.H - 2));
                            if (grid.IsWalkable(x, y))
                                props.Add(new Prop(PropKind.Bench, new Tile(x, y), Roll(rng)));
                        }
                        break;
                    }

                    case PlacePropStyle.Postbox:
                        props.Add(new Prop(PropKind.Postbox, Beside(grid, place.Door), Roll(rng)));
                        break;

                    case PlacePropStyle.WaterTrough:
                        props.Add(new Prop(PropKind.WaterTrough, Beside(grid, place.Door), Roll(rng)));
                        break;
                }
            }
        }

        /// <summary>A walkable tile next to the given one, or the tile itself if hemmed in.</summary>
        private static Tile Beside(TileGrid grid, Tile t)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int x = t.X + dx, y = t.Y + dy;
                if (grid.IsWalkable(x, y) && grid.TerrainAt(x, y) == Terrain.Grass)
                    return new Tile(x, y);
            }
            return t;
        }

        /// <summary>
        /// Whether this tile is on the water's edge.
        ///
        /// Four-neighbour and not eight, deliberately. The water is a raster of axis-aligned
        /// rectangles, so a diagonal neighbour is a staircase CORNER - counting it plants a reed
        /// on the outside of every step and draws the staircase in cattail instead of hiding it.
        /// </summary>
        private static bool NextToWater(TileGrid grid, int x, int y)
        {
            return grid.TerrainAt(x - 1, y) == Terrain.Water
                || grid.TerrainAt(x + 1, y) == Terrain.Water
                || grid.TerrainAt(x, y - 1) == Terrain.Water
                || grid.TerrainAt(x, y + 1) == Terrain.Water;
        }

        private static bool NextToRoad(TileGrid grid, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if ((grid.FlagsAt(x + dx, y + dy) & TileFlags.Road) != 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether this field tile is on the boundary of the field - which is what post-and-rail
        /// is FOR: the line between one man's field and the next.
        ///
        /// WATER IS NOT A FIELD BOUNDARY, and saying so is the whole of this method's subtlety.
        /// Until the real North Fork and the school ponds were laid into the map there was no
        /// water out here at all, so "any neighbour that is not field" and "the next field starts
        /// here" were the same test. They stopped being the same the moment a river ran through
        /// the corn: every tile along both banks became an edge, and the first render showed each
        /// pond ringed by an unbroken post-and-rail fence following every wiggle of the shoreline
        /// - which is not what anybody fences, and read as a reservoir rather than a farm pond.
        /// A watercourse is where a field STOPS, not where it meets another one.
        /// </summary>
        private static bool OnFieldEdge(TileGrid grid, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var t = grid.TerrainAt(x + dx, y + dy);
                if (t != Terrain.Field && t != Terrain.Wall && t != Terrain.Water) return true;
            }
            return false;
        }

        private static byte Roll(IRng rng) => (byte)rng.NextInt(256);
    }
}
