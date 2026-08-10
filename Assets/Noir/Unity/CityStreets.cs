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
            //
            // 7.1m ACROSS IS 23 FEET, which is a residential street, not an alley. A platted
            // alley in a town like this is a sixteen-foot right of way with about ten feet of
            // gravel run down the middle of it. See Narrow() - the tile is squeezed laterally so
            // what is drawn is the track and not the whole easement, and RoadClass.Alley's
            // corridor came down to four metres to match.
            RoadClass.Alley    => Dirt + "Road_Dirt_B_Straight_10x10m.prefab",
            RoadClass.Track    => Dirt + "Road_Dirt_A_Straight_10x10m.prefab",

            _                  => Kit + "Road_Straight_10x10_City.prefab",
        };

        /// <summary>
        /// Scale a tile across so that what is DRAWN is exactly as wide as the corridor the
        /// simulation reasons about.
        ///
        /// THE CORRIDOR IS THE ONE SOURCE OF TRUTH AND THIS IS WHAT MAKES IT ONE. Until now the
        /// rendered width of a road was whatever its prefab happened to measure and the corridor
        /// was a separate number in Core, and the two were free to disagree - which they did, in
        /// both directions and silently:
        ///
        ///     Road_Straight_10x10_City      10.0m tile,  10m corridor    agreed by luck
        ///     Mainroad_Straight_30x30_City  30.0m tile,  30m corridor    agreed by luck
        ///     Road_Dirt_B (alley)            7.1m tile,   4m corridor    77% too wide
        ///     Road_Dirt_A (track)            7.1m tile,  10m corridor    29% too narrow
        ///
        /// The alley one is what made every block read as though it had a second street down the
        /// middle of it, and narrowing the CORRIDOR alone did nothing because the corridor was
        /// never the thing on screen. The track one is the same fault the other way up: cars on
        /// the outer lane of a farm track drive on grass, because the sim puts the lane at 5m out
        /// and the asphalt stops at 3.55m.
        ///
        /// So the ratio is measured off the prefab rather than guessed at, and a tile whose width
        /// already matches its corridor is left completely alone - the two city tiles scale by
        /// exactly 1.0 and nothing about the streets or Route 1 moves.
        ///
        /// ACROSS ONLY. The length is untouched, so the deliberate overlap between consecutive
        /// dirt tiles still hides the joins.
        ///
        /// If a stretched dirt tile ever reads badly, THE ANSWER IS TO CHANGE THE CORRIDOR, not to
        /// special-case the renderer again - that is how the two numbers drifted apart in the
        /// first place.
        /// </summary>
        // EDITOR ONLY, and it always was - it measures a prefab through AssetDatabase and
        // PrefabUtility, neither of which exists in a player. It sat outside every guard in this
        // file and compiled anyway, because `using UnityEditor;` at the top IS guarded, so in the
        // editor the names resolve and outside it they simply vanish. Both call sites (the two
        // SeatAt calls) are already inside the editor-only block below, so nothing outside the
        // editor ever wanted this. Found by the first player build this project has ever had.
#if UNITY_EDITOR
        private static float Narrow(RoadClass klass, string path)
        {
            if (!_tileWidth.TryGetValue(path, out float measured))
            {
                measured = 0f;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    var rends = probe.GetComponentsInChildren<Renderer>();
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds;
                        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                        measured = b.size.x;
                    }
                    Object.DestroyImmediate(probe);
                }
                _tileWidth[path] = measured;
            }

            if (measured < 0.01f) return 1f;
            float want = RoadClasses.CorridorWidth(klass);
            float scale = want / measured;
            return Mathf.Abs(scale - 1f) < 0.02f ? 1f : scale;      // already right: leave it be
        }
#endif

        private static readonly Dictionary<string, float> _tileWidth = new Dictionary<string, float>();

        private static string Crosswalk(RoadClass klass) => klass switch
        {
            RoadClass.Freeway  => Kit + "Freeway_Crosswalk_30x30_City.prefab",
            RoadClass.Mainroad => Kit + "Mainroad_Crosswalk_City.prefab",
            RoadClass.Alley    => Straight(klass),      // nor across an alley mouth
            RoadClass.Track    => Straight(klass),      // nobody paints a zebra on a farm track

            // AND NOT ON A RESIDENTIAL STREET EITHER, which leaves this arm with nothing but
            // Street in it. `Road_Crosswalk_10x10_City` carries its zebra, its stop line and its
            // edge line on the M_Universal_A submesh - and Verge(), eleven lines after the tile is
            // seated, repaints exactly that submesh to plain asphalt on every Street tile. So the
            // crossing was laid and then blanked, every time, at every side-street junction in
            // town. Two decisions disagreeing, and the tile choice was the wrong one: Verge is
            // stating the RULING - Rossville's side streets are unmarked, the 2007 photographs
            // show paint only on the through route - and this line had never heard it.
            //
            // Route 1, Attica and every Mainroad or Freeway crossing are untouched: both Verge
            // call sites are behind a Street guard.
            _                  => Straight(klass),
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
        /// The piece for a junction with TWO arms at a right angle: a street that turns a corner.
        ///
        /// Rossville has these because the county records a street as chained segments and the
        /// chain gets a new name where it bends - Maple becomes Park, 3550north becomes Attica.
        /// Before JUNC-2 no junction was recorded at all where two same-axis roads met, so this
        /// case could not arise and there was nothing to lay. Now there is, and laying a
        /// four-way crossing at a corner paints two arms into whatever is behind the bend.
        ///
        /// THE PIECE WAS ALREADY IN THE KIT. It was never opened because nothing ever asked for
        /// a corner.
        /// </summary>
        /// <summary>
        /// The class of the widest road at this junction running along the given axis - what the
        /// tee piece has to fit, since the through road is most of the tile. Falls back to the
        /// widest road of any axis where nothing matches.
        /// </summary>
        private static RoadClass ClassAlong(Junction j, bool northSouth, RoadClass fallback)
        {
            var best = fallback;
            float widest = -1f;

            foreach (var arm in j.Arms)
            {
                if (arm.Road == null) continue;

                // Asked of the TANGENT, not of the road's IsNorthSouth: a road is on the axis it
                // is pointing along here, not the one its two end points imply over its whole run.
                bool armIsNorthSouth = Mathf.Abs(arm.Tangent.Y) > Mathf.Abs(arm.Tangent.X);
                if (armIsNorthSouth != northSouth) continue;
                if (arm.Road.HalfWidth > widest) { widest = arm.Road.HalfWidth; best = arm.Road.Class; }
            }

            return best;
        }

        private static string Turn(RoadClass widest, float reach)
        {
            if (reach < 8f) return Kit + "Road_Turn_10x10_City.prefab";
            if (widest == RoadClass.Freeway) return Kit + "Freeway_Turn_30x30_City.prefab";
            return Kit + "Mainroad_Turn_30x30_City.prefab";
        }

        /// <summary>
        /// The piece for a junction with ONE arm: a road that stops here and goes no further.
        ///
        /// A dead end is a junction in the model - `Touches` records a road ending on another -
        /// and one of the two may have nothing beyond it. Ten of these on Content/roads.txt are
        /// alleys stopping at a lot line.
        /// </summary>
        private static string End(RoadClass widest, float reach)
        {
            if (reach < 8f) return Kit + "Road_End_10x10_City.prefab";
            return Kit + "Mainroad_End_30x30_City.prefab";
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

            // AS DRAWN, NOT AS SHIPPED - `ROAD-FIXES` CONS-4, and it was a whole road class wide.
            //
            // `Seat` narrows a tile whose width does not match its corridor (see `Narrow`), and
            // the two dirt tiles are exactly that case: the alley tile is 7.1 m across and its
            // corridor is 4 m. Measuring the PREFAB and not applying the same scale reported
            // `[streets] Alley: 7.1m of asphalt in a 4m corridor` - which is not merely wrong, it
            // is impossible, and the pavement figure beside it came out at MINUS 1.6 m each side.
            //
            // Everything that stands beside or drives on a street derives from this number:
            // `LaneOffset` put alley traffic 1.8 m off the centre line of a 2 m half-width lane,
            // and `CityUnderTest`'s "is this vehicle on the road" check passed every point inside
            // an alley UNCONDITIONALLY, because the asphalt it was told about was wider than the
            // corridor containing it. That exemption is gone with this line.
            float drawn = Narrow(klass, Straight(klass));
            if (drawn != 1f) half *= drawn;

            Debug.Log($"[streets] {klass}: {half * 2f:0.0}m of asphalt in a {corridor}m corridor "
                    + $"({corridor / 2f - half:0.0}m of pavement each side), "
                    + $"{LanesEachWay(klass)} lane(s) each way"
                    + (drawn != 1f ? $" - tile narrowed x{drawn:0.00} to fit." : "."));
            return _asphalt[klass] = half;
#else
            // MEASURED, NOT GUESSED - and the guess was badly wrong.
            //
            // Outside the editor there is no AssetDatabase to measure the tile with, so this
            // returned `corridor / 2 - 2`, described in the editor branch above as "a fallback, if
            // the mesh cannot be read". In a player it is not a fallback, it is ALWAYS the answer,
            // and it does not agree with the measurement:
            //
            //     class      corridor   guessed half   MEASURED half
            //     Street         10 m        3.0 m         3.0 m     ok
            //     Alley           4 m        0.0 m         2.0 m     zero asphalt
            //     Mainroad       30 m       13.0 m         6.0 m     more than double
            //
            // THE ALLEY FIGURE IS 2.0 AND NOT 3.55 - see CONS-4 above. 3.55 is half the alley
            // tile's own width; `Seat` narrows that tile by 4/7.1 to fit its corridor, so 2.0 is
            // what a player actually draws. The old number here made a player and the editor
            // disagree about a whole road class.
            //
            // Zero is the bad one: LaneOffset multiplies by it, so every lane on an alley collapses
            // onto the centre line and VergeOffset puts the pavement in the middle of the road.
            // Nobody had ever seen it because nobody had ever built a player.
            //
            // These are the numbers the editor measures off the real tiles, logged by the branch
            // above as "[streets] Street: 6.0m of asphalt in a 10m corridor". They are constants
            // here rather than a formula because they are measurements of specific art; if the
            // road kit changes, run in the editor, read the [streets] lines, and update these.
            switch (klass)
            {
                case RoadClass.Street: return 3.0f;
                case RoadClass.Alley:  return 2.0f;
                default:
                    // Not measured on this map - Rossville has no freeway, main road or track in
                    // Content/roads.txt, so no [streets] line has ever been logged for them. The
                    // old formula stands, and says so, rather than inventing a number that looks
                    // authoritative.
                    Debug.LogWarning($"[streets] no measured asphalt width for {klass} in a player "
                                   + "build - falling back to corridor/2-2, which is a guess. "
                                   + "Run in the editor, read the [streets] line, and put the "
                                   + "measurement in CityStreets.Asphalt.");
                    return RoadClasses.CorridorWidth(klass) / 2f - 2f;
            }
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

        public static GameObject Build(WorldModel world, Transform parent) =>
            Build(world, parent, out _);

        /// <summary>
        /// Streets and alleys, built into TWO roots so they can be switched independently.
        ///
        /// The alley root is a SIBLING of the street root, not a child of it: a child would be
        /// switched off with its parent, and the whole point is that the alleys can be taken away
        /// while the streets stay. Requested while the alleys were known to be wrong and the
        /// streets were being judged - "make them a layer to add as they are still all fucked".
        ///
        /// Junctions stay with the streets. An alley-only junction is a few tiles and splitting
        /// them would mean reading the arms of every junction to decide.
        /// </summary>
        public static GameObject Build(WorldModel world, Transform parent, out GameObject alleys)
        {
            var root = new GameObject("CityStreets");
            root.transform.SetParent(parent, false);

            alleys = new GameObject("CityAlleys");
            alleys.transform.SetParent(parent, false);

            // Clear of Rossville's ground, which is still drawn underneath the whole city at y=0.
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
            alleys.transform.localPosition = root.transform.localPosition;   // same lift, or they sink

#if UNITY_EDITOR
            int tiles = 0, dressing = 0;

            // 1. The junctions first, so the straights know which ground is already spoken for.
            //
            // AND WHICH GROUND IS NOT. The carriageway walk below skips any tile within reach of a
            // junction, on the understanding that a junction tile has already covered it. Where
            // this loop lays nothing - a straight-through node, or one with no arms at all - that
            // understanding is false and the road would have a hole in it. So the ones that got
            // no tile are collected and the walk is told to lay straight across them.
            var untiled = new HashSet<int>();

            int tees = 0, corners = 0, deadEnds = 0, straightThrough = 0;
            for (int ji = 0; ji < world.Roads.Junctions.Count; ji++)
            {
                var j = world.Roads.Junctions[ji];
                float reach = j.Reach;
                bool inTown = CitySignals.InTheTown(world, j);

                // WHICH ARMS ACTUALLY EXIST, ASKED OF THE ARMS.
                //
                // This read `j.NorthSouth.From < j.Y - reach` and three more like it: a road's
                // declared extent on ITS OWN axis, compared against the junction's coordinate on
                // the OTHER one. That only means anything while the NorthSouth slot holds a
                // north-south road, and since JUNC-2 it does not - seven junctions on
                // Content/roads.txt are two SAME-AXIS roads meeting, and every one of them has an
                // east-west road in that slot. It could not see past the pair either, so at the
                // eighteen merged nodes the third and fourth roads contributed no arms at all.
                //
                // An arm leaves backwards along its own path when there is road behind the stop
                // line, and forwards when there is road beyond it. The tangent says which compass
                // direction that is, and village y runs SOUTH - so north is the smaller y, which
                // is the same convention the four lines above were using.
                bool north = false, south = false, east = false, west = false;

                foreach (var arm in j.Arms)
                {
                    var road = arm.Road;
                    if (road?.Path == null) continue;

                    if (arm.S > reach) Leaves(-arm.Tangent.X, -arm.Tangent.Y);
                    if (arm.S < road.Path.Length - reach) Leaves(arm.Tangent.X, arm.Tangent.Y);
                }

                void Leaves(float dx, float dy)
                {
                    if (Mathf.Abs(dx) >= Mathf.Abs(dy)) { if (dx >= 0f) east = true; else west = true; }
                    else { if (dy >= 0f) south = true; else north = true; }
                }

                int arms = (north ? 1 : 0) + (south ? 1 : 0) + (west ? 1 : 0) + (east ? 1 : 0);

                // The widest road at the node, which is what the piece has to fit. Not the wider
                // of the pair: a merged node may have four roads and the widest may be neither.
                var widest = RoadClass.Track;
                float widestHalf = -1f;
                bool anyFreeway = false;
                foreach (var arm in j.Arms)
                {
                    if (arm.Road == null) continue;
                    if (arm.Road.Class == RoadClass.Freeway) anyFreeway = true;
                    if (arm.Road.HalfWidth > widestHalf) { widestHalf = arm.Road.HalfWidth; widest = arm.Road.Class; }
                }

                string piece;
                float yaw = 0f;

                if (arms == 2 && ((north && south) || (east && west)))
                {
                    // STRAIGHT THROUGH IS NOT A JUNCTION TO LOOK AT. Two arms facing opposite
                    // ways is one continuous piece of road - either a crossing whose other road
                    // stops short of it, or, and this is the case JUNC-2 created, two roads of
                    // the same axis meeting head-on where the county's chain changed its name.
                    // Maple becomes Park; there is nothing there but tarmac. Lay no tile, and put
                    // this node on the untiled list so the carriageway walk paves straight over it
                    // instead of leaving the hole it would otherwise leave.
                    straightThrough++;
                    untiled.Add(ji);
                    continue;
                }

                if (arms == 4)
                {
                    piece = Cross(anyFreeway ? RoadClass.Freeway : RoadClass.Mainroad, reach, inTown);
                }
                else if (arms == 3)
                {
                    // The through road is the one with both of its arms; the stem is the odd one.
                    bool throughIsNorthSouth = north && south;
                    piece = Tee(ClassAlong(j, throughIsNorthSouth, widest), reach, inTown);

                    // The tile is drawn with its through road along x and its stem toward +z,
                    // which is village NORTH because village y runs into Unity -z. Yaw turns +z
                    // toward +x, so the yaw simply IS the compass direction of the stem.
                    yaw = throughIsNorthSouth
                        ? (west ? 270f : 90f)
                        : (north ? 0f : 180f);
                    tees++;
                }
                else if (arms == 2)
                {
                    // A CORNER. The kit's turn piece joins its +z arm to its +x arm at yaw 0 -
                    // north and east in village terms - so the yaw is how far round from there
                    // this corner's own pair sits. VERIFY THIS AGAINST A RENDER before trusting
                    // it: the prefab's built-in orientation is not written down anywhere and
                    // guessing it wrong turns every corner in the town inside out.
                    piece = Turn(widest, reach);
                    yaw = north && east ? 0f
                        : east && south ? 90f
                        : south && west ? 180f
                        : 270f;
                    corners++;
                }
                else if (arms == 1)
                {
                    // A DEAD END. Same warning as the corner: the end piece's own facing is read
                    // off a render, not assumed. Taken here as the road arriving from +z at yaw 0.
                    piece = End(widest, reach);
                    yaw = north ? 0f : east ? 90f : south ? 180f : 270f;
                    deadEnds++;
                }
                else
                {
                    // No arms at all. A junction where both roads stop exactly at it and neither
                    // continues - which should be impossible, since something has to have brought
                    // the crossing into being. Say so rather than laying tarmac over it.
                    Debug.LogWarning($"[streets] the junction at {j.X},{j.Y} has no arms at all "
                                   + $"({j.Arms?.Length ?? 0} roads meet there). Nothing laid.");
                    untiled.Add(ji);
                    continue;
                }

                // WHERE THE UNUSUAL ONES ARE, so they can be photographed rather than counted.
                // The turn and end pieces each have a built-in orientation that is not written
                // down anywhere in the pack, and a corner laid at the wrong yaw is invisible in
                // any total - it is only ever wrong to look at.
                if (arms <= 2)
                    Debug.Log($"[streets] {(arms == 1 ? "dead end" : "corner")} at {j.X:0},{j.Y:0} "
                            + $"yaw {yaw:0} - arms"
                            + (north ? " N" : "") + (south ? " S" : "")
                            + (east ? " E" : "") + (west ? " W" : "")
                            + $", {string.Join(" x ", System.Array.ConvertAll(j.Arms, a => a.Road?.Name ?? "?"))}");

                if (Seat(root.transform, piece,
                         j.X - reach, j.Y - reach, reach * 2f, reach * 2f, yaw) != null) tiles++;
            }

            // 2. The carriageways, each into the root that owns its class.
            foreach (var line in world.Roads.Lines)
                tiles += Lay(line.Class == RoadClass.Alley ? alleys.transform : root.transform,
                             line, world, untiled);

            // 3. Everything that is not a carriageway, sampled off the terrain grid as before:
            //    pavement where a street has an edge, parks, and the backs of blocks.
            dressing += Dress(root.transform, world);

            // BROKEN OUT BY SHAPE, because "111 junctions" hid the fact that seven of them were
            // being laid as crossroads with two arms painted into open ground.
            Debug.Log($"[streets] {tiles} road tiles on {world.Roads.Lines.Count} roads and "
                    + $"{world.Roads.Junctions.Count} junctions "
                    + $"({tees} three-way, {corners} corners, {deadEnds} dead ends, "
                    + $"{straightThrough} straight through and laid as plain road), "
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
        private static int Lay(Transform parent, RoadLine line, WorldModel world,
                               HashSet<int> untiled)
        {
            if (!line.IsStraight) return LayCurved(parent, line, world, untiled);

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
                // A JUNCTION THAT WAS NOT TILED HAS NOT SPOKEN FOR ITS GROUND. Skipping every
                // tile near every junction assumed one had been laid there; at a straight-through
                // node none is, and the road would simply stop for the width of the crossing.
                bool inJunction = false;
                for (int ji = 0; ji < world.Roads.Junctions.Count; ji++)
                {
                    if (untiled.Contains(ji)) continue;
                    var j = world.Roads.Junctions[ji];
                    if (Mathf.Abs(cx - j.X) < j.Reach && Mathf.Abs(cy - j.Y) < j.Reach)
                    { inJunction = true; break; }
                }
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
                var t = Seat(parent, piece, cx - half, cy - half, module, module, yaw,
                             Narrow(line.Class, piece));
                if (t != null) { if (line.Class == RoadClass.Street) Verge(t); laid++; }
            }
            return laid;
        }

        /// <summary>
        /// A road that bends, tiled along its own centre line.
        ///
        /// WHY IT IS A SEPARATE WALK. The straight version steps a village coordinate and lays a
        /// tile square to an axis, which is meaningless once the road turns: there is no single
        /// yaw for the whole run and no axis to step along. This walks ARC LENGTH instead, asks
        /// the path where it is and which way it points at each step, and seats one tile there
        /// facing that way.
        ///
        /// THE PREFABS ARE STRAIGHT AND STAY STRAIGHT. There is no curved asphalt in the kit, so
        /// a curve is a chain of straight tiles each turned a little further - which is how a
        /// real road is actually built, and at Route 1's rate of bend, about a fifth of a degree
        /// per thirty-metre tile, the wedge gaps on the outside of the curve are millimetres.
        /// A sharper road would show them, and that is a real limit of laying a straight prefab
        /// along a curve rather than a defect to chase.
        /// </summary>
        private static int LayCurved(Transform parent, RoadLine line, WorldModel world,
                                     HashSet<int> untiled)
        {
            int module = RoadClasses.CorridorWidth(line.Class);
            float half = module / 2f;
            var path = line.Path;
            if (path == null || path.Length < module) return 0;

            int laid = 0;
            for (float s = 0f; s + module <= path.Length + 0.01f; s += module)
            {
                // The middle of this tile, not its leading edge, so the tile is centred on the
                // stretch of road it is covering.
                float mid = s + half;
                var at = path.PointAt(mid);
                var tangent = path.TangentAt(mid);

                // Village (x,y) is x east, y south; Unity yaw is clockwise from +Z and +Z is
                // north. So a map tangent (tx,ty) points along the world direction (tx,0,-ty),
                // which is the same derivation GroundShot uses to aim a camera down the rail.
                float yaw = Mathf.Atan2(tangent.X, -tangent.Y) * Mathf.Rad2Deg;

                // See Lay(): an untiled junction has not covered its own ground.
                bool inJunction = false;
                for (int ji = 0; ji < world.Roads.Junctions.Count; ji++)
                {
                    if (untiled.Contains(ji)) continue;
                    var j = world.Roads.Junctions[ji];
                    if (Mathf.Abs(at.X - j.X) < j.Reach && Mathf.Abs(at.Y - j.Y) < j.Reach)
                    { inJunction = true; break; }
                }
                if (inJunction) continue;

                // Near a crossing, and in the town: the crosswalk tile, which carries the zebra
                // and the stop line. Measured as real distance to the junction rather than along
                // one axis, because on a bend neither axis means anything.
                bool atCrossing = false;
                foreach (var j in world.Roads.Junctions)
                {
                    // ANY ARM, not the pair. `NorthSouth` and `EastWest` report the first arm of
                    // each axis, so at the eighteen merged nodes on Content/roads.txt the third
                    // and fourth roads were never recognised as being at their own junction and
                    // got no stop line and no zebra approaching it.
                    bool mine = false;
                    foreach (var arm in j.Arms)
                        if (ReferenceEquals(arm.Road, line)) { mine = true; break; }
                    if (!mine) continue;

                    float dx = at.X - j.X, dy = at.Y - j.Y;
                    if (dx * dx + dy * dy >= module * 1.5f * (module * 1.5f)) continue;
                    if (!CitySignals.InTheTown(world, j)) continue;

                    atCrossing = true;
                    break;
                }

                string piece = atCrossing ? Crosswalk(line.Class) : Straight(line.Class);
                var t = SeatAt(parent, piece, at.X, at.Y, yaw, Narrow(line.Class, piece));
                if (t != null) { if (line.Class == RoadClass.Street) Verge(t); laid++; }
            }

            Debug.Log($"[streets] '{line.Name}' bends: {laid} tiles laid along "
                    + $"{path.Length:0}m of centre line.");
            return laid;
        }

        /// <summary>
        /// Seat a tile CENTRED on a point, at any yaw.
        ///
        /// Seat() below covers an axis-aligned patch, which cannot express "centred here, turned
        /// twenty degrees". Same measured-bounds correction and the same ground sample - the
        /// tiles pivot at a corner rather than the middle, so the drift has to be measured after
        /// the rotation is applied, not before.
        /// </summary>

        /// <summary>
        /// A RESIDENTIAL STREET HAS NO KERB. Turn its concrete edging into grass verge.
        ///
        /// The pack ships one 10 m road tile and it is a CITY street: asphalt, paint, and a
        /// concrete sidewalk strip down both sides - the same three materials the 30 m main road
        /// uses. Laid on Rossville that gave every side street in a town of twelve hundred the
        /// kerbs and walks of a downtown block, which is the mismatch the owner spotted: "Route 1
        /// and Attica are good size roads while each side street is not".
        ///
        /// The pack has no unkerbed 10 m tile, so the geometry stays and the SURFACE changes. The
        /// sidewalk strip becomes M_Ground_Grass and reads as the mown verge it should be. Chicago
        /// and Attica keep their concrete, which is right - the 2007 photographs of the crossing
        /// show exactly that, kerbs and set-back walks on the through route and nothing on the
        /// residential streets running off it.
        ///
        /// BY MATERIAL NAME, NOT SUBMESH INDEX. The two prefabs order their submeshes
        /// differently - sidewalk is index 2 on the 10 m tile and index 0 on the 30 m one - so
        /// anything keyed on position would silently repaint the carriageway on one of them.
        /// </summary>
        private static void Verge(GameObject tile)
        {
            if (tile == null) return;
            if (_verge == null) _verge = VergeGrass();
            if (_plainTop == null)
                _plainTop = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/polyperfect/Poly Universal Pack/Materials/City/M_Asphalt_A_City.mat");
            if (_verge == null) return;

            foreach (var r in tile.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                bool hit = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;

                    // The concrete edging becomes mown verge.
                    //
                    // THE EXACT PREFIX, NOT THE FAMILY. These were `M_Sidewalk` and `M_Universal`,
                    // which also match M_Universal_B..F and, worse, M_Universal_Glass and
                    // M_Universal_Glass_Night. Only M_Universal_A is on the road tiles today, so
                    // it has never fired wrongly - but SunRig finds the pack's lamp lens by the
                    // substring "Glass" and CityChunker refuses to bake anything whose material
                    // name contains "Night", so the day a street tile carries a lit element this
                    // repaint would silently kill it. Measured against SM_Road_Straight_10x10_City
                    // and SM_Road_Crosswalk_10x10_City; StackProbe already uses the tight form.
                    if (mats[i].name.StartsWith("M_Sidewalk_A", System.StringComparison.Ordinal))
                    { mats[i] = _verge; hit = true; }

                    // AND THE PAINT GOES. The kit's 10 m tile carries a white edge line on its
                    // M_Universal submesh, which is a city street's marking. Rossville's side
                    // streets are unmarked asphalt - the 2007 photographs show paint only on the
                    // through route, and in 1940 most of these were not even hard-surfaced.
                    // Repainted as asphalt rather than deleted, so the mesh stays whole.
                    else if (_plainTop != null &&
                             mats[i].name.StartsWith("M_Universal_A", System.StringComparison.Ordinal))
                    { mats[i] = _plainTop; hit = true; }
                }
                if (hit) r.sharedMaterials = mats;
            }
        }

        /// <summary>Measured off SM_Road_Straight_10x10_City and its crosswalk sibling: both
        /// UV their sidewalk and universal submeshes at exactly one third of a UV per world
        /// metre. (The 0.0843 in the same file is UVMap_Lightmap, which is a different question.)
        /// </summary>
        private const float TileUvPerMetre = 0.3333f;

        /// <summary>
        /// A COPY OF THE PACK'S GRASS, NEVER THE PACK'S OWN ASSET, and at a tiling derived from
        /// the mesh rather than inherited from a terrain.
        ///
        /// `M_Ground_Grass` is the pack's TERRAIN material: `_BaseMap` scale 0.2, sized for a
        /// ground plane whose UVs run in metres. Assigned straight onto a road tile UV'd at 1/3
        /// per metre, the grass repeated every FIFTEEN metres where the concrete it replaced
        /// repeated every three - and every four on the lawn it runs into, which ForTerrain binds
        /// at SurfaceTextures.TilingMetres. A verge five times coarser than the garden behind it
        /// is the "weird" a wide shot shows and no test can.
        ///
        /// 0.75 puts the repeat at 4.00 m exactly, so the verge and the lawn are the same grass at
        /// the same size - and they really are the same grass: M_Ground_Grass's _BaseMap resolves
        /// to Nature/Grass_A_Alb.png, which is the sheet ForTerrain already binds. Only the tiling
        /// ever separated them. The normal map is scaled with it, because the pack ships that
        /// material with _BumpMap at 1 against a _BaseMap at 0.2 - its own grain five times finer
        /// than its own colour.
        ///
        /// A COPY because `Assets/polyperfect` is gitignored: an edit to the .mat would exist on
        /// this machine and on no other. The name must not contain "Night" or "Emission", or
        /// CityChunker refuses to bake the tile.
        /// </summary>
        private static Material VergeGrass()
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/polyperfect/Poly Universal Pack/Materials/Nature/M_Ground_Grass.mat");
            if (src == null) return null;

            float s = 1f / (TileUvPerMetre * SurfaceTextures.TilingMetres);   // 0.75
            var m = new Material(src) { name = "M_Verge_Grass" };
            m.enableInstancing = true;
            // All four names, because the pack ships this material with the HDRP/Lit and URP/Lit
            // property sets both filled in and which one the shader reads is not this file's
            // business. HasProperty makes the ones it does not have free.
            foreach (var prop in new[] { "_BaseMap", "_MainTex", "_BumpMap", "_NormalMap" })
                if (m.HasProperty(prop)) m.SetTextureScale(prop, new Vector2(s, s));
            return m;
        }

        private static Material _verge, _plainTop;

        private static GameObject SeatAt(Transform parent, string path, float mx, float my, float yaw,
                                         float lateral = 1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[streets] missing " + path); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // Across only, and BEFORE the bounds are measured - the seat below works off the
            // measured footprint, so squeezing afterwards would leave the tile off its centre line.
            if (lateral != 1f) go.transform.localScale = new Vector3(lateral, 1f, 1f);

            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return go;

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            float groundY = parent.position.y + ElevationGrid.HeightAt(mx, my);
            var want = new Vector3(mx, 0f, -my);
            var drift = b.center - go.transform.position;
            go.transform.position = new Vector3(want.x - drift.x, groundY, want.z - drift.z);
            return go;
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
                                       float x, float y, float w, float h, float yaw,
                                       float lateral = 1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[streets] missing " + path); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // SIZE BEFORE MEASURING. w and h say where the tile GOES, never how big it is - this
            // method has only ever positioned. That is why narrowing the alley corridor changed
            // nothing on screen: every straight road comes through here, and a 7.1m dirt tile
            // centred on a 4m patch is still 7.1m of dirt. See Narrow().
            if (lateral != 1f) go.transform.localScale = new Vector3(lateral, 1f, 1f);

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
            // are five bins, four bollards and two phone boothes in here and a hand-written list
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
                // and hung street lamps, a phone booth and a litter bin round a working farm.
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
        /// phone booth and planter.
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

                // GUARDED LIKE `lamps` AND `trees` ABOVE, which it was not. The line below takes
                // a modulo by kerbside.Count, and Count is zero whenever the prop catalogue came
                // back empty - a checkout where the Poly pack has not finished importing, or a
                // fresh clone that does not have it at all. Catalogue returns nothing, line 463
                // strips every empty role, kerbside ends up empty rather than merely short, and
                // the divide throws the moment a street is dressed. It cannot fire in a project
                // that HAS the pack, which is why it sat here: the two lists either side of it
                // were guarded and this one was not, and no local run could tell the difference.
                if (kerbside.Count == 0) continue;

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

                // Nature ships the whole world in one folder. A palm or a cactus on Rossville
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
                // city flagstones and then have street lamps, hydrants and a phone booth hung
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
