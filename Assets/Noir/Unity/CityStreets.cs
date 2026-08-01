using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Lays the street itself out of bought models: carriageway, junctions, crossings, kerbs,
    /// pavement, lamps, signs, bins and street trees.
    ///
    /// THE ROADS COME FROM THE ROAD NETWORK, NOT FROM THE TERRAIN GRID. A rasterised road is a
    /// field of identical Terrain.Road tiles: it has forgotten which corridor it belonged to, how
    /// wide that corridor was and what class it is, so every pass that wanted those had to infer
    /// them by sampling neighbours. That inference is how a lane came to be measured off the
    /// centre of a TILE - which is mostly pavement - rather than the centre of a ROAD.
    /// WorldModel.Roads keeps the corridors, and this reads them.
    ///
    /// THE KIT IS Prefabs/City/Roads City, not Modular Parts/Roads. The city was built for
    /// months out of the generic ten-metre kit, whose "main road" is six metres of unpainted
    /// asphalt. The City kit is drawn at thirty metres with painted lanes, zebra crossings, stop
    /// lines, lay-bys and matching junction pieces, and it had never been opened.
    ///
    ///     freeway    30m corridor, 24m asphalt, two lanes each way, solid orange centre
    ///     mainroad   30m corridor, 12m asphalt, one lane each way
    ///     street     10m corridor,  6m asphalt, unmarked
    ///
    /// Every one of those asphalt widths is MEASURED off the tile at load, never typed in here.
    ///
    /// TILES ARE SEATED BY THEIR BOUNDS, NOT THEIR PIVOTS. These prefabs pivot at the corner
    /// opposite the origin, so a tile turned ninety degrees lands a full tile away from where its
    /// pivot says it will. Placing by pivot laid every east-west road one cell north of its own
    /// corridor - which put the traffic, correctly following the map, on the pavement.
    ///
    /// Furniture is picked by scanning the pack's own folders rather than naming prefabs here.
    /// There are 232 cars and 181 props in it; a hand-written list would have used six.
    ///
    /// Editor-only in practice - the pieces load through AssetDatabase.
    /// </summary>
    public static class CityStreets
    {
        private const string Kit = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Roads City/";
        private const string CityProps = "Assets/polyperfect/Poly Universal Pack/Prefabs/City";

        /// <summary>
        /// The smallest module in the kit, and the grid this samples terrain on for everything
        /// that is not a carriageway - pavement, parks, back alleys.
        /// </summary>
        public const int Cell = 10;

        /// <summary>The module a through-road is drawn at. A corridor is three cells.</summary>
        public const int Block = 30;

        // ---- measured geometry --------------------------------------------------------

        private static readonly Dictionary<RoadClass, float> _asphalt =
            new Dictionary<RoadClass, float>();

        private const string Dirt = "Assets/polyperfect/Poly Universal Pack/Prefabs/Farm/Roads Farm/";

        /// <summary>The straight tile for a class, which is also what its width is measured off.</summary>
        private static string Straight(RoadClass klass) => klass switch
        {
            RoadClass.Freeway  => Kit + "Freeway_Straight_30x30_City.prefab",
            RoadClass.Mainroad => Kit + "Mainroad_Straight_30x30_City.prefab",

            // A dirt tile is built quite unlike the city ones: its pivot is CENTRED rather than
            // at a corner, and though it is called 10x10m it measures 7.1m across and 13.7m
            // long. Laid every ten metres it therefore overlaps its neighbour by nearly four,
            // which on a rutted track is not a fault - it is what stops the joins showing.
            // Seating by measured bounds is what makes that harmless.
            RoadClass.Track    => Dirt + "Road_Dirt_A_Straight_10x10m.prefab",

            _                  => Kit + "Road_Straight_10x10_City.prefab",
        };

        private static string Crosswalk(RoadClass klass) => klass switch
        {
            RoadClass.Freeway  => Kit + "Freeway_Crosswalk_30x30_City.prefab",
            RoadClass.Mainroad => Kit + "Mainroad_Crosswalk_City.prefab",
            RoadClass.Track    => Straight(klass),      // nobody paints a zebra on a farm track
            _                  => Kit + "Road_Crosswalk_10x10_City.prefab",
        };

        /// <summary>
        /// The crossing piece for a junction, sized to the road that needs the most room.
        ///
        /// Chosen by REACH rather than by class, because the piece has to fit the junction
        /// tile: dropping a thirty-metre crossing on the ten-metre square where two tracks meet
        /// would overhang the fields by ten metres on every side.
        ///
        /// ZEBRAS ONLY WHERE THERE ARE PAVEMENTS. The main-road crossing has a crossing painted
        /// on all four arms, which is what a signalised town junction looks like and is what
        /// every junction on the map used to get - so there were pedestrian crossings, with stop
        /// lines, at crossroads in the middle of a wheatfield with no pavement within four
        /// hundred metres. Out of town the plain crossing is the honest piece.
        /// </summary>
        private static string Cross(RoadClass widest, float reach, bool inTown)
        {
            if (reach < 8f) return Kit + "Road_Cross_10x10_City.prefab";

            if (widest == RoadClass.Freeway) return Kit + "Freeway_Cross_30x30_City.prefab";

            return inTown
                ? Kit + "Mainroad_Cross_Crosswalk_30x30_City.prefab"
                : Kit + "Mainroad_Cross_30x30_City.prefab";
        }

        /// <summary>
        /// The piece for a junction with only THREE arms.
        ///
        /// Every junction on the map used to be laid with a four-way crossing whether or not
        /// there were four roads meeting at it. Both places where a dirt track dead-ends into a
        /// through road got a full crossroads, so each had a fourth arm painted on it - kerbs,
        /// stop line and all - running straight into a paddock.
        ///
        /// The through road decides the piece, because the through road is most of the tile.
        /// </summary>
        private static string Tee(RoadClass through, float reach, bool inTown)
        {
            if (reach < 8f) return Kit + "Road_T_10x10_City.prefab";
            if (through == RoadClass.Freeway) return Kit + "Freeway_T_30x30_City.prefab";

            return inTown
                ? Kit + "Mainroad_T_Crosswalk_30x30_City.prefab"
                : Kit + "Mainroad_T_30x30_City.prefab";
        }

        /// <summary>
        /// HALF the width of the actual asphalt for this class, measured off its own tile.
        ///
        /// A tile is carriageway AND pavement - a thirty-metre main-road tile is twelve metres of
        /// asphalt and nine metres of pavement each side - so anything positioned "off the middle
        /// of the road" by eye is positioned off the middle of a strip that is mostly not road.
        /// Everything that drives on or stands beside a street derives from this one number.
        /// </summary>
        public static float Asphalt(RoadClass klass)
        {
#if UNITY_EDITOR
            if (_asphalt.TryGetValue(klass, out float known)) return known;

            int corridor = RoadClasses.CorridorWidth(klass);
            float half = corridor / 2f - 2f;              // a fallback, if the mesh cannot be read

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Straight(klass));
            if (prefab != null)
            {
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = mf.sharedMesh;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mesh == null || mr == null) continue;

                    // The city tiles carry the carriageway on its own submesh, so the surface is
                    // the one painted with asphalt. A DIRT TILE HAS ONLY ONE SUBMESH - the whole
                    // tile is the track - so on those the single submesh IS the surface. Without
                    // this the search for "Asphalt" finds nothing on a track and the width falls
                    // through to a guess, silently, which is how the last three width bugs
                    // happened.
                    bool single = mesh.subMeshCount == 1;

                    for (int s = 0; s < mesh.subMeshCount && s < mr.sharedMaterials.Length; s++)
                    {
                        var m = mr.sharedMaterials[s];
                        if (!single &&
                            (m == null || m.name.IndexOf("Asphalt", System.StringComparison.Ordinal) < 0))
                            continue;

                        // SubMeshDescriptor.bounds is importer metadata, so this works whether or
                        // not Read/Write is enabled on the model.
                        var b = mesh.GetSubMesh(s).bounds;

                        // HALF THE SURFACE'S OWN WIDTH, and nothing about where the pivot is.
                        //
                        // The city tiles fill x[-corridor,0] about a corner pivot and the dirt
                        // tiles straddle a centred one, and an earlier version of this tried to
                        // work out which - and got it backwards, reporting a thirty-metre main
                        // road as carrying forty-two metres of asphalt and minus six metres of
                        // pavement. The pivot never mattered: the carriageway is symmetrical
                        // about its own centre line by construction, and Seat() puts that centre
                        // on the corridor's centre, so its half-width is simply half its extent.
                        half = (b.max.x - b.min.x) * 0.5f;
                        break;
                    }
                }
            }

            Debug.Log($"[streets] {klass}: {half * 2f:0.0}m of asphalt in a {corridor}m corridor "
                    + $"({corridor / 2f - half:0.0}m of pavement each side), "
                    + $"{LanesEachWay(klass)} lane(s) each way.");
            return _asphalt[klass] = half;
#else
            return RoadClasses.CorridorWidth(klass) / 2f - 2f;
#endif
        }

        /// <summary>
        /// How many running lanes there are in each direction.
        ///
        /// Read off the paint, which is the only place it is written down: the freeway tile has
        /// a dashed line either side of a solid orange centre, and the main-road tile has one
        /// dashed centre line and nothing else.
        /// </summary>
        public static int LanesEachWay(RoadClass klass) => klass == RoadClass.Freeway ? 2 : 1;

        /// <summary>
        /// How far off the centre line lane <paramref name="index"/> runs, counting outward from
        /// the middle. Derived from the measured asphalt so a lane is always inside it.
        /// </summary>
        public static float LaneOffset(RoadClass klass, int index)
        {
            int lanes = LanesEachWay(klass);
            return Asphalt(klass) * (2 * index + 1) / (2f * lanes);
        }

        /// <summary>The middle of the pavement, for anything that stands rather than drives.</summary>
        public static float VergeOffset(RoadClass klass) =>
            (RoadClasses.CorridorWidth(klass) / 2f + Asphalt(klass)) * 0.5f;

        // ---- building ------------------------------------------------------------------

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityStreets");
            root.transform.SetParent(parent, false);

            // Clear of Ashcombe's ground, which is still drawn underneath the whole city at y=0.
            //
            // A road tile is TWO levels: its pavement is a plane at y=0 and its asphalt is a
            // plane at y=-0.1, ten centimetres lower, which is the kerb. The old lift of 0.04
            // was chosen to stop the pavement z-fighting with the ground and nobody checked the
            // other plane - so the asphalt sat at -0.06, UNDER the ground, and was never once
            // drawn. Every road in every screenshot so far has been the village renderer's own
            // road colour showing through the hole where the real carriageway was hidden, which
            // is why the streets looked like paving and the lane markings never appeared.
            //
            // 0.12 puts the asphalt at 0.02 and the kerb at 0.12, both above the ground plane,
            // and leaves the kerb reading as a kerb.
            root.transform.localPosition = new Vector3(0f, 0.12f, 0f);

#if UNITY_EDITOR
            int tiles = 0, dressing = 0;

            // 1. The junctions first, so the straights know which ground is already spoken for.
            int tees = 0;
            foreach (var j in world.Roads.Junctions)
            {
                float reach = j.Reach;
                bool inTown = CitySignals.InTheTown(world, j);

                // WHICH ARMS ACTUALLY EXIST. A road only reaches out of the junction on a side
                // where its own declared run continues past it - so a track that dead-ends here
                // contributes one arm, not two, and this is a three-way junction.
                bool north = j.NorthSouth.From < j.Y - reach;
                bool south = j.NorthSouth.To   > j.Y + reach;
                bool west  = j.EastWest.From   < j.X - reach;
                bool east  = j.EastWest.To     > j.X + reach;

                int arms = (north ? 1 : 0) + (south ? 1 : 0) + (west ? 1 : 0) + (east ? 1 : 0);

                string piece;
                float yaw = 0f;

                if (arms == 3)
                {
                    // The through road is the one with both of its arms; the stem is the odd one.
                    bool throughIsNorthSouth = north && south;
                    var through = throughIsNorthSouth ? j.NorthSouth : j.EastWest;
                    piece = Tee(through.Class, reach, inTown);

                    // The tile is drawn with its through road along x and its stem toward +z,
                    // which is village NORTH because village y runs into Unity -z. Yaw turns +z
                    // toward +x, so the yaw simply IS the compass direction of the stem.
                    yaw = throughIsNorthSouth
                        ? (west ? 270f : 90f)
                        : (north ? 0f : 180f);
                    tees++;
                }
                else
                {
                    var klass = j.NorthSouth.Class == RoadClass.Freeway
                             || j.EastWest.Class == RoadClass.Freeway
                        ? RoadClass.Freeway : RoadClass.Mainroad;
                    piece = Cross(klass, reach, inTown);

                    if (arms < 3)
                        Debug.LogWarning($"[streets] the junction at {j.X},{j.Y} has {arms} arms "
                                       + "and is being laid as a crossing anyway.");
                }

                if (Seat(root.transform, piece,
                         j.X - reach, j.Y - reach, reach * 2f, reach * 2f, yaw) != null) tiles++;
            }

            // 2. The carriageways.
            foreach (var line in world.Roads.Lines)
                tiles += Lay(root.transform, line, world);

            // 3. Everything that is not a carriageway, sampled off the terrain grid as before:
            //    pavement where a street has an edge, parks, and the backs of blocks.
            dressing += Dress(root.transform, world);

            Debug.Log($"[streets] {tiles} road tiles on {world.Roads.Lines.Count} roads and "
                    + $"{world.Roads.Junctions.Count} junctions ({tees} of them three-way), "
                    + $"{dressing} pieces of furniture, "
                    + $"{root.GetComponentsInChildren<Renderer>().Length} renderers.");
#endif
            return root;
        }

#if UNITY_EDITOR
        /// <summary>
        /// One road, tiled end to end at its own module, skipping the cells the junctions took.
        ///
        /// The tile immediately either side of a junction is the CROSSWALK variant, because that
        /// is where a pedestrian crossing goes and because it puts a painted stop line exactly
        /// where the traffic has to stop.
        /// </summary>
        private static int Lay(Transform parent, RoadLine line, WorldModel world)
        {
            if (!line.IsStraight)
            {
                Debug.LogWarning($"[streets] '{line.Name}' bends, and only straight roads are "
                               + "tiled. It will have terrain but no surface.");
                return 0;
            }

            int module = RoadClasses.CorridorWidth(line.Class);
            float half = module / 2f;
            float yaw = line.IsNorthSouth ? 0f : 90f;

            // BETWEEN THE POINTS IT WAS DECLARED BETWEEN. The city's arterials are declared edge
            // to edge, so for them this is the whole map; a farm track is not, and running it
            // the full width would lay a road across fields nobody put one in.
            float from = line.From, to = line.To;

            int laid = 0;
            for (float along = from; along + module <= to + 0.01f; along += module)
            {
                float cx = line.IsNorthSouth ? line.Centre : along + half;
                float cy = line.IsNorthSouth ? along + half : line.Centre;

                // Inside a crossing? Tested by REACH rather than by an exact cell, because a
                // thirty-metre junction swallows three ten-metre tiles of the track that meets
                // it, and only the middle one shares its centre.
                bool inJunction = false;
                foreach (var j in world.Roads.Junctions)
                    if (Mathf.Abs(cx - j.X) < j.Reach && Mathf.Abs(cy - j.Y) < j.Reach)
                    { inJunction = true; break; }
                if (inJunction) continue;

                // Next to a crossing? Then this is where the zebra and the stop line go - but
                // ONLY IN THE TOWN. The crosswalk tile paints a pedestrian crossing and a stop
                // line, and laying it at every junction put both at crossroads in open fields,
                // where there is no pavement for a pedestrian to cross from and nothing is
                // required to stop. See CitySignals.InTheTown.
                bool atCrossing = false;
                foreach (var j in world.Roads.Junctions)
                {
                    if (line.IsNorthSouth && Mathf.Abs(j.X - line.Centre) > 0.5f) continue;
                    if (!line.IsNorthSouth && Mathf.Abs(j.Y - line.Centre) > 0.5f) continue;

                    float gap = line.IsNorthSouth ? Mathf.Abs(cy - j.Y) : Mathf.Abs(cx - j.X);
                    if (gap >= module * 1.5f) continue;
                    if (!CitySignals.InTheTown(world, j)) continue;

                    atCrossing = true;
                    break;
                }

                string piece = atCrossing ? Crosswalk(line.Class) : Straight(line.Class);
                if (Seat(parent, piece, cx - half, cy - half, module, module, yaw) != null) laid++;
            }
            return laid;
        }

        /// <summary>
        /// Put a tile down so that its MEASURED FOOTPRINT covers the given patch of village.
        ///
        /// Not by pivot. These tiles pivot at the corner opposite the origin and fill the square
        /// behind it, so a tile rotated ninety degrees about that pivot ends up a whole tile away
        /// from where the arithmetic says. Every east-west road in the city was laid one cell
        /// north of its own corridor for exactly that reason, and the traffic - which was
        /// correctly following the map - drove down the pavement beside it.
        ///
        /// Village y runs into Unity -z, as everywhere else.
        /// </summary>
        private static GameObject Seat(Transform parent, string path,
                                       float x, float y, float w, float h, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[streets] missing " + path); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return go;

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            // Where the middle of this tile is meant to end up. Only x and z are corrected: the
            // height is the ROOT's kerb lift PLUS the real terrain under this tile - the root
            // alone used to be enough back when the whole map was one flat plane, but a road
            // spanning real elevation needs its own ground sample under every tile, not one
            // shared number for the entire network. Writing a world y of zero here quietly
            // cancelled the lift entirely and put the carriageway back underneath the village.
            float groundY = parent.position.y + ElevationGrid.HeightAt(x + w / 2f, y + h / 2f);
            var want = new Vector3(x + w / 2f, 0f, -(y + h / 2f));
            var drift = b.center - go.transform.position;
            go.transform.position =
                new Vector3(want.x - drift.x, groundY, want.z - drift.z);
            return go;
        }

        /// <summary>
        /// Everything that is not carriageway: pavement at the edge of a street, the parks, and
        /// what piles up behind a building. Walks the terrain grid, because those ARE properties
        /// of the ground rather than of a road.
        /// </summary>
        private static int Dress(Transform parent, WorldModel world)
        {
            int cols = world.Width / Cell, rows = world.Height / Cell;

            bool Is(int cx, int cy, Noir.Core.World.Terrain want) =>
                cx >= 0 && cy >= 0 && cx < cols && cy < rows &&
                world.Grid.TerrainAt(cx * Cell + Cell / 2, cy * Cell + Cell / 2) == want;

            var lamps = Catalogue(CityProps + "/Lamps City", "Lamp_Sidewalk");
            var play = Catalogue(CityProps + "/Playground City", null);
            var skate = Catalogue(CityProps + "/SkatePark City", "Skate_");

            // ROAD SIGNS ARE NOT KERB DRESSING and are no longer scattered from here. See
            // CitySigns: this used to catalogue everything beginning with "Sign_", which is two
            // different kits - the mounted sign AND the bare plate meant to go on a post - and
            // drop one every fourth cell at a fixed offset facing a fixed direction. Half of
            // them were discs sunk to their equator in the pavement, and the half that stood up
            // said "deer crossing" outside the bank.

            // What actually stands on a pavement, as against one bin every seventh cell. Each of
            // these is a role rather than a prefab, so the pack's own variants get used: there
            // are five bins, four bollards and two phone boxes in here and a hand-written list
            // would have picked one of each.
            var kerbside = new List<List<string>>
            {
                Catalogue(CityProps + "/Props City", "Hydrant"),
                Catalogue(CityProps + "/Props City", "Bollard_"),
                Catalogue(CityProps + "/Props City", "Bin_"),
                Catalogue(CityProps + "/Props City", "Parking_Machine"),
                Catalogue(CityProps + "/Props City", "Telephone_Booth"),
                Catalogue(CityProps + "/Props City", "Newspaper_Stand"),
                Catalogue(CityProps + "/Props City", "Clock_"),
                Catalogue(CityProps + "/Props City", "Stand_"),
                Catalogue(CityProps + "/Park City", "City_Planter"),
                Catalogue(CityProps + "/Park City", "Tree_Pot_City"),
                Catalogue(CityProps + "/Props City", "Barrier_"),
                Catalogue(CityProps + "/Props City", "Cone"),
            };
            kerbside.RemoveAll(c => c.Count == 0);

            // Nature has 760 prefabs and the city was using none of them. Trees City is the nine
            // authored for a pavement; the rest is what a park is made of.
            const string Nat = "Assets/polyperfect/Poly Universal Pack/Prefabs/Nature";
            var streetTrees = Catalogue(Nat + "/Trees City", null);
            var parkKit = new List<List<string>>
            {
                Catalogue(Nat + "/Trees", null),
                Catalogue(Nat + "/Bushes", null),
                Catalogue(Nat + "/Flowers", null),
                Catalogue(Nat + "/Rocks", null),
                Catalogue(Nat + "/Grass", null),
            };
            parkKit.RemoveAll(c => c.Count == 0);

            // OPEN COUNTRY IS NOT A PARK. Grass beyond the town gets tufts, flowers and the odd
            // bush and NOTHING TALL: a park's kit sown across two hundred cells of farmland
            // turned every field into woodland with boulders in it, which is the opposite of
            // what a field is - you cannot see across it, and being able to see across it is
            // the whole reason to have one.
            var countryKit = new List<List<string>>
            {
                Catalogue(Nat + "/Grass", null),
                Catalogue(Nat + "/Flowers", null),
                Catalogue(Nat + "/Bushes", null),
            };
            countryKit.RemoveAll(c => c.Count == 0);

            // What piles up behind a building rather than in front of it.
            var alley = new List<List<string>>
            {
                Catalogue(CityProps + "/Props City/Garbage Props", null),
                Catalogue(CityProps + "/Props City/Pipes Props", null),
            };
            alley.RemoveAll(c => c.Count == 0);

            int n = 0;

            for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                if (Is(cx, cy, Noir.Core.World.Terrain.Road)) continue;   // laid from the network

                // THE COUNTRY BELONGS TO CityFarm. A farmyard is authored with path for its
                // ground because it is bare and hard-standing, and this used to read that as
                // "pavement" - so it laid city flagstones over the yard, then walked its kerb
                // and hung street lamps, a phone box and a litter bin round a working farm.
                // Whoever owns a place dresses it; this one does not own these.
                if (Owned(world, cx, cy)) continue;

                float vx = cx * Cell, vy = cy * Cell;

                if (Is(cx, cy, Noir.Core.World.Terrain.Path))
                {
                    // PAVE THE WHOLE BLOCK, not just the strip beside the road.
                    //
                    // Paving only the kerb-adjacent ring left the middle of every block showing
                    // the village renderer's own path colour, which is a warm tan meant for a
                    // 1979 English lane and sits next to the pack's grey stone like a different
                    // game. A city block's ground between its buildings IS paved; the reason
                    // this was once a ring is that the map used to be mostly empty, and it is
                    // not any more. Building interiors are stamped as floor rather than path, so
                    // they are already excluded.
                    if (Seat(parent, Kit + "Sidewalk_10x10_City.prefab", vx, vy, Cell, Cell, 0f) != null)
                        n++;

                    bool besideAStreet = Is(cx - 1, cy, Noir.Core.World.Terrain.Road)
                                      || Is(cx + 1, cy, Noir.Core.World.Terrain.Road)
                                      || Is(cx, cy - 1, Noir.Core.World.Terrain.Road)
                                      || Is(cx, cy + 1, Noir.Core.World.Terrain.Road);

                    if (besideAStreet)
                        n += Kerb(parent, cx, cy, vx, vy, lamps, kerbside, streetTrees);
                    // Behind the buildings. Bins, crates, pallets, tyres, and the pipework that
                    // runs up an alley wall.
                    else if (alley.Count > 0 && Materials3D.Scatter(cx, cy, 653) % 3 == 0)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            var role = alley[(int)(Materials3D.Scatter(cx + k, cy, 659) % (uint)alley.Count)];
                            float ox = 2f + Materials3D.Scatter(cx * 5 + k, cy, 661) % 7;
                            float oz = 2f + Materials3D.Scatter(cx, cy * 5 + k, 673) % 7;
                            if (Put(parent, Pick(role, cx + k, cy, 677), vx + ox, vy + oz,
                                    Materials3D.Scatter(cx + k, cy, 683) % 4 * 90f) != null) n++;
                        }
                    }
                }
                // The parks: the only unpaved ground, and where the playground and the skatepark
                // live. Both were bought and neither had ever been on screen.
                else if (Is(cx, cy, Noir.Core.World.Terrain.Grass))
                {
                    // A PARK IS A PLACE, NOT A COLOUR. Swings and a skate ramp go where somebody
                    // authored a green; they must not appear in a paddock just because a paddock
                    // is also grass.
                    bool park = IsPark(world, cx, cy);
                    var kit = park ? ((cx + cy) % 2 == 0 ? play : skate) : Empty;

                    for (int k = 0; k < 3 && kit.Count > 0; k++)
                    {
                        float ox = 2f + Materials3D.Scatter(cx * 7 + k, cy, 131) % 6;
                        float oz = 2f + Materials3D.Scatter(cx, cy * 7 + k, 137) % 6;
                        if (Put(parent, Pick(kit, cx + k, cy, 149 + k), vx + ox, vy + oz,
                                Materials3D.Scatter(cx, cy + k, 151) % 4 * 90f) != null) n++;
                    }

                    // Five pieces of planting to a park cell and one to a field: a park is
                    // gardened and a field is not, and at five the country came out as forest.
                    var green = park ? parkKit : countryKit;
                    int how = park ? 5 : 1;

                    for (int k = 0; k < how && green.Count > 0; k++)
                    {
                        var role = green[(int)(Materials3D.Scatter(cx + k, cy, 691) % (uint)green.Count)];
                        float ox = 1f + Materials3D.Scatter(cx * 11 + k, cy, 701) % 8;
                        float oz = 1f + Materials3D.Scatter(cx, cy * 11 + k, 709) % 8;
                        if (Put(parent, Pick(role, cx + k, cy + k, 719), vx + ox, vy + oz,
                                Materials3D.Scatter(cx + k, cy, 727) % 4 * 90f) != null) n++;
                    }
                }
            }

            return n;
        }

        /// <summary>
        /// Furnish ten metres of pavement.
        ///
        /// Walks ALONG it rather than dropping one prop per cell: a street is furnished at
        /// intervals of a few metres, and at ten it reads as a road with an ornament on it. Lamps
        /// keep a fixed rhythm because street lighting is laid out by a highways department;
        /// everything else is rolled, so no two blocks carry the same run of hydrant, meter,
        /// phone box and planter.
        /// </summary>
        private static int Kerb(Transform parent, int cx, int cy, float vx, float vy,
                                List<string> lamps, List<List<string>> kerbside,
                                List<string> trees)
        {
            int n = 0;

            for (int step = 0; step < 3; step++)
            {
                float along = 1.8f + step * 3.2f;
                float across = 1.2f + Materials3D.Scatter(cx, cy * 7 + step, 227) % 6;

                uint roll = Materials3D.Scatter(cx * 31 + step, cy * 17, 211);

                // Lighting on its own rhythm - twice a block, same place every block.
                if (step == 1 && lamps.Count > 0 && ((cx + cy) & 1) == 0)
                {
                    if (Put(parent, Pick(lamps, cx, cy, 11), vx + along, vy + across, 0f) != null) n++;
                    continue;
                }

                if (roll % 5 >= 3) continue;      // not every metre of kerb has something on it

                var role = kerbside[(int)(roll / 5 % (uint)kerbside.Count)];
                if (Put(parent, Pick(role, cx + step, cy, 223), vx + along, vy + across,
                        roll % 4 * 90f) != null) n++;
            }

            // Street trees. Nature/Trees City is the nine authored to stand in a pavement rather
            // than in a wood, and they do more for a street than any other single prop - they
            // break the roofline and cast something on it.
            if (trees.Count > 0 && Materials3D.Scatter(cx, cy, 641) % 2 == 0)
                if (Put(parent, Pick(trees, cx, cy, 643), vx + 5f, vy + 5f,
                        Materials3D.Scatter(cx, cy, 647) % 4 * 90f) != null) n++;

            return n;
        }

        /// <summary>
        /// Every prefab in a folder, so the city draws on the whole library rather than on the
        /// half-dozen names somebody could be bothered to type.
        /// </summary>
        private static List<string> Catalogue(string folder, string startsWith)
        {
            var found = new List<string>();
            folder = System.IO.Path.GetFullPath(folder).Replace('\\', '/');
            int at = folder.IndexOf("Assets/", System.StringComparison.Ordinal);
            if (at >= 0) folder = folder.Substring(at);

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                // Nature ships the whole world in one folder. A palm or a cactus on Northgate
                // Avenue is not a bug in the pack, it is a bug in asking the pack for "a tree"
                // and taking whatever comes back.
                if (path.IndexOf("Palm", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (path.IndexOf("Cactus", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (path.IndexOf("Dead", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                // AND IT SHIPS ALL FOUR SEASONS. FindAssets searches a folder recursively, so
                // asking Nature/Rocks for a rock also hands back Nature/Rocks Winter - which is
                // how a summer afternoon in the fields came to be strewn with snow-capped
                // boulders. Two hundred prefabs across Nature are seasonal.
                if (path.IndexOf("Winter", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (path.IndexOf("Snow", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (path.IndexOf("Ice", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (path.IndexOf("Frozen", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                // A menhir is a four-metre standing stone. Atmospheric once; not eighty times
                // in a field somebody is trying to grow wheat in.
                if (path.IndexOf("Menhir", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (startsWith != null &&
                    System.IO.Path.GetFileName(path).StartsWith(startsWith, System.StringComparison.Ordinal) == false)
                    continue;
                found.Add(path);
            }
            found.Sort(System.StringComparer.Ordinal);   // stable, so the city looks the same twice
            return found;
        }

        private static readonly List<string> Empty = new List<string>();

        /// <summary>What kind of place a cell belongs to, or "" for open ground.</summary>
        private static string KindAt(WorldModel world, int cx, int cy)
        {
            var id = world.Grid.PlaceAt(cx * Cell + Cell / 2, cy * Cell + Cell / 2);
            if (!id.IsValid) return "";
            var place = world.GetPlace(id);
            return place == null ? "" : PlaceKindTable.Current.Row(place.Kind).Name;
        }

        /// <summary>Is this cell inside somewhere authored as a green, rather than just grass?</summary>
        private static bool IsPark(WorldModel world, int cx, int cy) =>
            KindAt(world, cx, cy) == "green";

        /// <summary>Does another renderer own the ground here? See CityFarm.</summary>
        private static bool Owned(WorldModel world, int cx, int cy)
        {
            switch (KindAt(world, cx, cy))
            {
                case "farmyard": case "cornfield": case "paddock": case "orchard":
                    return true;

                // A car park is hard-standing, and its ground is authored as path for exactly
                // the same reason the farmyard's is. Without this the lots would be paved with
                // city flagstones and then have street lamps, hydrants and a phone box hung
                // round their kerbs. See CityParking.
                case "carpark":
                    return true;
                default:
                    return false;
            }
        }

        private static string Pick(List<string> from, int x, int y, int salt) =>
            from[(int)(Materials3D.Scatter(x, y, salt) % (uint)from.Count)];

        /// <summary>A prop, by its pivot, at a point in village coordinates.</summary>
        private static GameObject Put(Transform parent, string path, float vx, float vy, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);

            // On the pavement, which is the root's kerb lift over the REAL ground here - not at
            // world zero, which is the ground the pavement is laid on top of, and not the root's
            // lift alone, which by itself only holds for a flat map.
            go.transform.position =
                new Vector3(vx, parent.position.y + ElevationGrid.HeightAt(vx, vy), -vy);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }
#endif
    }
}
