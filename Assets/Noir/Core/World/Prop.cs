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

            switch (terrain)
            {
                case Terrain.Wood:
                    // Dense but not solid - a wood you can walk into.
                    if (rng.Chance(0.34f)) props.Add(new Prop(PropKind.Tree, new Tile(x, y), Roll(rng)));
                    else if (rng.Chance(0.10f)) props.Add(new Prop(PropKind.Bush, new Tile(x, y), Roll(rng)));
                    break;

                case Terrain.Grass:
                    if (NextToRoad(grid, x, y))
                    {
                        // A hedge along the verge, broken often enough to look grown
                        // rather than drawn.
                        if (rng.Chance(0.42f)) props.Add(new Prop(PropKind.Hedge, new Tile(x, y), Roll(rng)));
                    }
                    else if (rng.Chance(0.012f))
                    {
                        props.Add(new Prop(PropKind.Tree, new Tile(x, y), Roll(rng)));
                    }
                    else if (rng.Chance(0.010f))
                    {
                        props.Add(new Prop(PropKind.Bush, new Tile(x, y), Roll(rng)));
                    }
                    break;

                case Terrain.Field:
                    // A ROADSIDE IS HEDGED, NOT POST-AND-RAILED. Post-and-rail is for the
                    // boundary between one field and the next; what a field shows a road is a
                    // hedge, the same as grass does.
                    //
                    // It is also the difference between a map that builds and one that does not.
                    // A fence is one prop per tile and a hedge is drawn as a RUN, and a road
                    // through farmland puts an edge down both of its verges for its whole
                    // length: on a 960m map that is some thirteen kilometres of verge, which at
                    // 55% is fourteen thousand individual fence posts and pales.
                    if (NextToRoad(grid, x, y))
                    {
                        if (rng.Chance(0.42f)) props.Add(new Prop(PropKind.Hedge, new Tile(x, y), Roll(rng)));
                    }
                    else if (OnFieldEdge(grid, x, y) && rng.Chance(0.55f))
                        props.Add(new Prop(PropKind.Fence, new Tile(x, y), Roll(rng)));
                    else if (rng.Chance(0.004f))
                        props.Add(new Prop(PropKind.Tree, new Tile(x, y), Roll(rng)));
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

        private static bool OnFieldEdge(TileGrid grid, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var t = grid.TerrainAt(x + dx, y + dy);
                if (t != Terrain.Field && t != Terrain.Wall) return true;
            }
            return false;
        }

        private static byte Roll(IRng rng) => (byte)rng.NextInt(256);
    }
}
