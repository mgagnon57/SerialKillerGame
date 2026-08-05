using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.Contracts;
using Noir.Core.World;
using Terrain = Noir.Core.World.Terrain;

namespace Noir.Unity
{
    /// <summary>
    /// Builds the village as real geometry.
    ///
    /// Everything static is generated into meshes and SPLIT ON A 64-METRE GRID. The split is the
    /// whole design, and it is worth being clear about why, because the code it replaced was not
    /// wrong when it was written. One mesh for the entire floor made the village a handful of
    /// draw calls, which at 170x120 was exactly the right trade. What it cannot do is get out of
    /// the way: Unity culls a mesh by its bounding box, and the bounding box of the whole map
    /// contains the camera wherever the camera stands. Down one street the GPU was still being
    /// handed the whole village's floor, every wall in it and every roof on it. At town scale
    /// that is several times as much of nothing. See MeshChunks.
    ///
    /// Two techniques, chosen per job rather than uniformly:
    ///
    ///   GROUND, WALLS and ROOFS are generated meshes, a chunk at a time. Ten thousand tiles is
    ///   exactly what a mesh is for, and per-chunk submeshes mean the floor of the part of the
    ///   village you can see is a handful of draw calls.
    ///
    ///   PROPS are Unity's own primitives, but baked into the chunk meshes rather than left as
    ///   several hundred objects. Instancing already made them cheap to draw; what it could not
    ///   make cheap is being several hundred renderers to cull, sort and submit to four shadow
    ///   cascades apiece. The geometry is unchanged - the same cube, sphere and cylinder, at the
    ///   same places - so the picture is the same and the object count is not.
    ///
    ///   FURNITURE stays one primitive per piece, because each one carries its own colour in a
    ///   MaterialPropertyBlock and URP's Lit shader ignores vertex colour. Baking it would mean
    ///   a material per colour or a shader graph, and it is indoors, small, and already culled
    ///   one piece at a time.
    ///
    /// Interiors are left open on purpose - you are here to watch people, and a sealed box hides
    /// them.
    /// </summary>
    public static class VillageMesh
    {
        /// <summary>
        /// showDressing defaults true so nothing outside VillageHost/CityShot loses scenery it
        /// never asked to drop. The ground is unconditional either way - a plan still needs
        /// something under it to dim to near-black - but walls, furniture, props, roofs,
        /// frontage planting and the farmland scatter are all statements about how the town was
        /// BUILT, the same argument CityOutlines already makes for roads, and the plan draws
        /// none of them.
        /// </summary>
        public static GameObject Build(WorldModel world, Transform parent, bool showDressing = true)
        {
            var root = new GameObject("Village");
            root.transform.SetParent(parent, false);

            // Before any geometry: work out where the town's crossing is and how far its houses
            // reach, because the massing grammar for every dwelling is derived from that.
            HouseLayers.Install(world);

            BuildGround(world, root.transform);

            // The Massing layer decides whether this is BUILT, not just whether it is shown.
            // Walls, roofs, furniture, frontage and the countryside scatter are 45,510 props and
            // 9,172 country objects; building them to hide them was most of a second and a great
            // deal of memory for geometry nothing would draw.
            if (showDressing && Layers.IsOn(Layers.Kind.Massing))
            {
                // ONE ROOT OVER ALL OF IT, SO IT CAN BE SWITCHED OFF. Every one of these six used
                // to hang straight off `root`, which is the ground as well - so the generated
                // massing was drawn UNDER the bought prefabs with no way to remove it, and asking
                // the panel for "roads and lot lines only" still gave a town full of primitive
                // houses. The ground stays outside the switch: a plan needs a surface to dim.
                var dressing = new GameObject("Dressing");
                dressing.transform.SetParent(root.transform, false);

                BuildWalls(world, dressing.transform);
                BuildFurniture(world, dressing.transform);
                BuildProps(world, dressing.transform);
                RoofBuilder.Build(world, dressing.transform);
                Frontage.Build(world, dressing.transform);
                Countryside.Build(world, dressing.transform);

                Layers.Register(Layers.Kind.Massing, dressing);
            }

            // Reported AFTER building, not before. Textures load lazily when the first
            // material asks for them, so reporting first always printed "0 loaded" whether
            // they existed or not - a log line that lied about a system that was working.
            SurfaceTextures.ReportOnce();

            return root;
        }

        // ---------- props ----------

        /// <summary>
        /// Trees, hedges, fences and benches. Trees are a trunk and a canopy; everything else
        /// is one box. Crude, and it does the job: what makes open ground stop looking like a
        /// spreadsheet is vertical clutter breaking the sightline, not detailed models.
        ///
        /// All of it is combined into the chunk meshes. Each piece keeps the primitive it had
        /// and the material it had - the colour of a canopy is still a property of the tile the
        /// tree stands on, so the same tree is the same green it always was - and what it loses
        /// is only its own GameObject, its own name in the hierarchy, and the ability to be
        /// culled on its own. That last one is the trade: a chunk of woodland is now all or
        /// nothing at 64 metres' granularity, bought for several hundred fewer renderers to walk
        /// every frame and, more to the point, several hundred fewer to submit per shadow
        /// cascade.
        /// </summary>
        private static void BuildProps(WorldModel world, Transform parent)
        {
            if (world.PropCount == 0) return;

            var root = new GameObject("Props");
            root.transform.SetParent(parent, false);

            // The same 64 m as the ground, and the spinney is the one place that argues with
            // it: the wood at 0,74 is 22x40 metres of trees, and it falls almost entirely into
            // one chunk - 1,126 of the village's 1,994 prop primitives, 392,000 vertices in a
            // single renderer, where every other chunk in the project is under 46,000. So the
            // wood is all or nothing: stand at its edge and you draw the far side of it too.
            // Left at 64 deliberately, because that is 588,000 triangles submitted rather than
            // 1,126 draws avoided and the trade is still worth it - but if it ever does bite,
            // the lever is this one number and nothing else.
            var chunks = new MeshChunks(Scenery.Count, MeshChunks.Size,
                                        0, 0, world.Width, world.Height);

            BuildHedges(world, chunks);

            foreach (var prop in world.AllProps)
            {
                if (prop.Kind == PropKind.Hedge) continue;   // handled as runs, above

                // A BOUGHT MODEL STANDS HERE INSTEAD. The tree below - a cylinder and three
                // spheres - was the right answer while there was nothing else, and the pack has
                // 216 trees and 33 bushes that were never on screen outside the street verges.
                // Single answer, consumed here, exactly as CityBuildings.Handles is: drawing one
                // as well as placing the other is how every apartment came to have a grey box
                // inside it.
                if (CityGreenery.Handles(prop.Kind)) continue;

                float v = prop.Variant / 255f;
                var at = Space3D.ToWorld(prop.At);

                // Nudge off the tile centre so rows of props do not read as a grid.
                float jx = ((prop.Variant * 37) % 100 / 100f - 0.5f) * 0.5f;
                float jz = ((prop.Variant * 61) % 100 / 100f - 0.5f) * 0.5f;
                at += new Vector3(jx, 0f, jz);

                // Filed by the TILE rather than by the nudged position, so which chunk a prop
                // belongs to is decided by the world and not by a quarter-metre of jitter.
                var into = chunks.At(prop.At.X, prop.At.Y);

                switch (prop.Kind)
                {
                    case PropKind.Tree:
                    {
                        float height = 4.0f + v * 3.5f;
                        float canopy = 1.8f + v * 1.4f;

                        Box(into, PrimitiveType.Cylinder,
                            at + Vector3.up * height * 0.35f,
                            new Vector3(0.28f, height * 0.35f, 0.28f), Scenery.Bark);

                        // Three overlapping lobes rather than one ball. A single sphere is
                        // unmistakably a lollipop from any angle, and a wood full of them has
                        // no depth - the lobes give a broken outline, which is the only thing
                        // the eye needs at this range to read a canopy.
                        int green = Scenery.CanopyFor(prop.At.X, prop.At.Y, 17);

                        for (int lobe = 0; lobe < 3; lobe++)
                        {
                            uint s = Materials3D.Scatter(prop.At.X, prop.At.Y, 31 + lobe * 7);
                            float scale = 0.62f + s % 40 / 100f;
                            var offset = new Vector3(
                                ((s >> 8) % 100 / 100f - 0.5f) * canopy * 0.8f,
                                height * (0.70f + lobe * 0.09f),
                                ((s >> 16) % 100 / 100f - 0.5f) * canopy * 0.8f);

                            Box(into, PrimitiveType.Sphere, at + offset,
                                new Vector3(canopy * scale, canopy * scale * 0.85f, canopy * scale),
                                green);
                        }
                        break;
                    }

                    case PropKind.Bush:
                        Box(into, PrimitiveType.Sphere, at + Vector3.up * 0.45f,
                            new Vector3(1.0f + v * 0.5f, 0.8f, 1.0f + v * 0.5f),
                            Scenery.CanopyFor(prop.At.X, prop.At.Y, 23));
                        break;

                    case PropKind.Fence:
                    {
                        // Palings and a rail, not a solid panel. At 1.1 m of unbroken board
                        // these walled the allotments in and, being nearly black, read from
                        // above as a row of headstones.
                        float tall = 0.82f + v * 0.14f;
                        Box(into, PrimitiveType.Cube, at + Vector3.up * tall * 0.5f,
                            new Vector3(0.92f, tall, 0.06f), Scenery.Timber);
                        Box(into, PrimitiveType.Cube, at + Vector3.up * tall * 0.72f,
                            new Vector3(1.0f, 0.09f, 0.10f), Scenery.Timber);
                        break;
                    }

                    case PropKind.Bench:
                        Box(into, PrimitiveType.Cube, at + Vector3.up * 0.42f,
                            new Vector3(1.6f, 0.09f, 0.5f), Scenery.Timber);
                        Box(into, PrimitiveType.Cube, at + Vector3.up * 0.66f,
                            new Vector3(1.6f, 0.30f, 0.08f), Scenery.Timber);
                        break;

                    case PropKind.Postbox:
                        Box(into, PrimitiveType.Cylinder, at + Vector3.up * 0.7f,
                            new Vector3(0.55f, 0.7f, 0.55f), Scenery.Postbox);
                        break;

                    case PropKind.Headstone:
                        Box(into, PrimitiveType.Cube, at + Vector3.up * 0.35f,
                            new Vector3(0.55f, 0.7f, 0.16f), Scenery.Stone);
                        break;

                    case PropKind.WaterTrough:
                        Box(into, PrimitiveType.Cube, at + Vector3.up * 0.3f,
                            new Vector3(2.0f, 0.6f, 0.8f), Scenery.Stone);
                        break;

                    default:
                        Box(into, PrimitiveType.Cube, at + Vector3.up * 0.5f,
                            new Vector3(0.8f, 1.0f, 0.8f), Scenery.Stone);
                        break;
                }
            }

            var renderers = chunks.Emit(root.transform, "Props", Scenery.Palette(),
                                        ShadowCastingMode.On, true);

            Debug.Log($"Props: {world.PropCount} pieces of scenery, {chunks.VertexCount:N0} "
                    + $"vertices in {renderers.Count} chunk meshes, {chunks.DrawCalls} draw calls.");
        }

        /// <summary>
        /// Throw away a component. Object.Destroy is a no-op outside play mode - it logs
        /// "Destroy may not be called from edit mode" and leaves the object alone - so the
        /// snapshot renderer, which builds the village without ever pressing Play, produced
        /// several hundred error lines and kept every collider it asked to be rid of.
        /// </summary>
        private static void Discard(Object doomed)
        {
            if (doomed == null) return;
            if (Application.isPlaying) Object.Destroy(doomed);
            else Object.DestroyImmediate(doomed);
        }

        /// <summary>
        /// Hedges, merged into runs.
        ///
        /// Placed one per tile they came out as a row of separate boxes with daylight between
        /// them - which is a line of shrubs in pots, not a boundary. A hedge is a continuous
        /// thing, and the moment it is continuous it starts doing its actual job in the
        /// picture: telling you where one person's garden stops and the next begins.
        ///
        /// Height still varies along the run, because a hedge clipped to a perfect line reads
        /// as a wall painted green.
        /// </summary>
        private static void BuildHedges(WorldModel world, MeshChunks chunks)
        {
            var tiles = new HashSet<long>();
            foreach (var prop in world.AllProps)
                if (prop.Kind == PropKind.Hedge)
                    tiles.Add(Key(prop.At.X, prop.At.Y));

            if (tiles.Count == 0) return;

            var done = new HashSet<long>();
            int runs = 0;

            // Runs first, and only from an end of one. Starting anywhere would let a tile in
            // the MIDDLE of a hedge be claimed as a lone shrub before the run reached it,
            // punching a hole the run could then never fill.
            foreach (var prop in world.AllProps)
            {
                if (prop.Kind != PropKind.Hedge) continue;
                int x = prop.At.X, y = prop.At.Y;
                if (done.Contains(Key(x, y))) continue;

                int dx = 0, dy = 0;
                if (tiles.Contains(Key(x + 1, y)) && !tiles.Contains(Key(x - 1, y))) dx = 1;
                else if (tiles.Contains(Key(x, y + 1)) && !tiles.Contains(Key(x, y - 1))) dy = 1;
                else continue;   // middle of a run, or alone - both wait for the second pass

                int length = 0;
                while (tiles.Contains(Key(x + dx * length, y + dy * length))
                       && !done.Contains(Key(x + dx * length, y + dy * length)))
                {
                    done.Add(Key(x + dx * length, y + dy * length));
                    length++;
                }

                Run(x, y, dx, dy, length);
                runs++;
            }

            // Whatever is left is a hedge one tile long, or a stub the first pass could not
            // reach from an end.
            foreach (var prop in world.AllProps)
            {
                if (prop.Kind != PropKind.Hedge) continue;
                if (!done.Add(Key(prop.At.X, prop.At.Y))) continue;
                Run(prop.At.X, prop.At.Y, 1, 0, 1);
                runs++;
            }

            Debug.Log($"Hedges: {tiles.Count} tiles in {runs} runs.");

            // Segmented every two metres so the top line is not dead flat over twelve. Each
            // segment is filed by its OWN tile rather than the run's first one, so a hedge that
            // crosses a chunk boundary is split there - which costs nothing, the segments being
            // separate boxes already and the material carrying no texture to break continuity.
            void Run(int x, int y, int dx, int dy, int length)
            {
                for (int i = 0; i < length; i += 2)
                {
                    int span = Mathf.Min(2, length - i);
                    int sx = x + dx * i, sy = y + dy * i;
                    float tall = 0.82f + Materials3D.Scatter(sx, sy, 61) % 30 / 100f;

                    float cx = x + dx * (i + span * 0.5f) + (dx == 0 ? 0.5f : 0f);
                    float cy = y + dy * (i + span * 0.5f) + (dy == 0 ? 0.5f : 0f);

                    Box(chunks.At(sx, sy), PrimitiveType.Cube,
                        new Vector3(cx, tall * 0.5f, -cy),
                        new Vector3(dx != 0 ? span : 0.8f, tall, dy != 0 ? span : 0.8f),
                        Scenery.Hedge);
                }
            }
        }

        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;

        private static void Box(MeshChunk into, PrimitiveType type,
                                Vector3 position, Vector3 scale, int submesh) =>
            into.Add(Primitives.Of(type), submesh, position, Quaternion.identity, scale);

        // ---------- furniture ----------

        /// <summary>
        /// Every piece of furniture as a box of the right size, in the right place, at the
        /// right height.
        ///
        /// This is the cheapest possible model and it does the whole job: from above, a small
        /// room with two white rectangles is unmistakably a bathroom, and a pale slab against a
        /// wall is a bed. No texture, no mesh, no purchase. And when real models do arrive they
        /// drop onto exactly these footprints - the box IS the specification.
        ///
        /// The one group NOT baked into the chunk meshes, and the reason is the colour: each
        /// piece carries its own in a MaterialPropertyBlock, which is what lets several hundred
        /// of them share one material. Combined they would need a material per colour, since
        /// URP's Lit shader ignores vertex colour. It is indoors and small, so it culls per
        /// piece anyway and the roofs are usually over it.
        /// </summary>
        private static void BuildFurniture(WorldModel world, Transform parent)
        {
            if (world.FurnitureCount == 0) return;

            var root = new GameObject("Furniture");
            root.transform.SetParent(parent, false);

            var block = new MaterialPropertyBlock();
            int baseColour = Shader.PropertyToID("_BaseColor");
            int colour = Shader.PropertyToID("_Color");

            // THE FLOOR A PIECE OF FURNITURE STANDS ON is its building's, not the terrain's.
            // Everything else about a building is seated on the ground under the middle of its
            // footprint so the building stands level; a chair sampling the contour under its own
            // four legs would sit at a slightly different height from the floor it is on, and on
            // a slope would sink through it. Looked up per item rather than cached: 800 pieces
            // against 500 places is a few hundred thousand rectangle tests, once, at build time.
            float FloorUnder(TileRect fp)
            {
                int cx = fp.X + fp.W / 2, cy = fp.Y + fp.H / 2;
                foreach (var place in world.AllPlaces)
                    if (place.Bounds.Contains(cx, cy)) return Space3D.GroundUnder(place.Bounds);
                return ElevationGrid.HeightAt(cx + 0.5f, cy + 0.5f);
            }

            foreach (var f in world.AllFurniture)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = f.Kind.ToString();
                box.transform.SetParent(root.transform, false);

                var fp = f.Footprint;
                float height = f.Height;

                // Slightly inset so adjacent pieces read as separate objects rather than one
                // continuous slab running round the room.
                box.transform.localScale = new Vector3(fp.W - 0.12f, height, fp.H - 0.12f);
                box.transform.position = new Vector3(
                    fp.X + fp.W * 0.5f,
                    FloorUnder(fp) + height * 0.5f + 0.02f,   // on the raised interior floor
                    -(fp.Y + fp.H * 0.5f));

                Discard(box.GetComponent<BoxCollider>());

                var mr = box.GetComponent<MeshRenderer>();
                mr.sharedMaterial = Materials3D.Furniture;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;

                var c = Materials3D.ColourOf(f.Kind);
                mr.GetPropertyBlock(block);
                block.SetColor(baseColour, c);
                block.SetColor(colour, c);
                mr.SetPropertyBlock(block);
            }

            Debug.Log($"Furniture: {world.FurnitureCount} pieces across {world.RoomCount} rooms.");
        }

        // ---------- ground ----------

        /// <param name="chunkSize">Chunk edge in metres, for GroundChunkProbe to sweep. Left at
        /// zero - which is every caller in the game - it is MeshChunks.GroundSize. It is a
        /// parameter rather than a settable static precisely because the snapshots are compared
        /// byte for byte: a global somebody forgot to put back would change the mesh for every
        /// later build in the same editor session, and would show up as a snapshot diff a long
        /// way from whatever moved it.</param>
        public static void BuildGround(WorldModel world, Transform parent, int chunkSize = 0)
        {
            int size = chunkSize > 0 ? chunkSize : MeshChunks.GroundSize;

            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);

            // One submesh per terrain, THEN one per zoned look a Grass or Field tile can turn
            // into (see GroundZoning), THEN a last one for the open country beyond the map. The
            // zoned kinds are appended after GroundOrder rather than folded into it, because they
            // are not a placement anybody made on the map - Grass and Field stay exactly the
            // submeshes they always were for every tile zoning does not overrule.
            int baseKinds = Materials3D.GroundOrder.Length;
            int zonedKinds = System.Enum.GetValues(typeof(Materials3D.ZonedGround)).Length;
            int pasture = baseKinds + zonedKinds;

            // One more after the pasture: the darker green a dwelling's footprint is painted in
            // when the buildings themselves are switched off. Appended rather than folded into
            // GroundOrder for the same reason the zoned kinds are - it is not a terrain anybody
            // placed on the map, it is a way of drawing one.
            int dwelling = pasture + 1;
            int submeshes = dwelling + 1;

            var materials = new Material[submeshes];
            for (int i = 0; i < submeshes; i++)
                materials[i] = i == dwelling
                    ? Materials3D.Dwelling
                    : i == pasture
                        ? Materials3D.Pasture
                        : i < baseKinds
                            ? Materials3D.ForTerrain(Materials3D.GroundOrder[i])
                            : Materials3D.ForZoned((Materials3D.ZonedGround)(i - baseKinds));

            // WHICH LOTS THE COUNTY CALLS RESIDENTIAL, rasterised once into a mask.
            //
            // The lot, not the house on it. A dwelling's own footprint is a few tiles and reads
            // as nothing from a survey view; the LOT is what the eye is looking for when the
            // question is "which parts of town are people living in".
            //
            // Zoning is CountyRecord's reading of the assessor's class codes - 0011 Homesite,
            // 0040 Improved Residential Lot, 0050 six-units-and-over - not anything invented
            // here. Rasterised per parcel over its own bounding box rather than asked per tile,
            // because 5,040,000 tiles against 794 polygons is a hundred million point-in-polygon
            // tests and this is a few hundred thousand.
            var residential = VillageHost.FlatGroundColour
                ? ResidentialMask(world.Width, world.Height) : null;

            // The far rim is inclusive because the riser pass below walks one PAST the last
            // tile on both axes: a riser at x == world.Width is the cut edge of the last column
            // and has to have a chunk to go in.
            var chunks = new MeshChunks(submeshes, size, 0, 0, world.Width, world.Height);

            // The land beyond the map is deliberately NOT chunked. It is a dozen quads two
            // kilometres across, and dropped into the chunk grid it would drag one chunk's
            // bounding box out to the horizon - after which that chunk could never be culled
            // from anywhere, which is the exact fault this file was changed to fix. It stays one
            // small mesh that is always drawn, which is what a horizon is for.
            var skirt = MeshChunks.Single(submeshes);
            var beyond = skirt.At(0, 0);

            // ---- corner heights, cached once per GRID POINT rather than resampled per tile ----
            //
            // Every corner here used to be an ElevationGrid.HeightAt call made by whichever tile
            // reached it first - up to four times over, since an interior corner is shared by
            // four tiles. (width+1) x (height+1) points sampled once apiece is exactly the same
            // information for a quarter of the calls, and it is what the merge below needs
            // anyway: a run's own outer corners are these same cached points, just read further
            // apart.
            var corner = new float[world.Height + 1, world.Width + 1];
            for (int gy = 0; gy <= world.Height; gy++)
            for (int gx = 0; gx <= world.Width; gx++)
                corner[gy, gx] = ElevationGrid.HeightAt(gx, gy);

            // ---- what every tile looks like, decided once, same as it always was ----
            //
            // This is the ORIGINAL per-tile loop, unchanged in what it decides - same terrain
            // lookup, same GroundZoning call with the same four corners, same small flat offset
            // for water/road/path/floor. What changed is that the decision is now stored instead
            // of drawn immediately, so identical neighbours can be found and merged before any
            // geometry is emitted. A tile's classification here is byte-for-byte what it would
            // have gotten drawn as one quad at a time - the merge below can only ever join tiles
            // that already agree, never blur ones that do not.
            var submeshGrid = new int[world.Height, world.Width];
            var flatGrid = new float[world.Height, world.Width];

            for (int gy = 0; gy < world.Height; gy++)
            for (int gx = 0; gx < world.Width; gx++)
            {
                var terrain = world.Grid.TerrainAt(gx, gy);

                // A building's slab and walls are TERRAIN, not objects, so no layer switch can
                // take them away - see VillageHost.ShowBuildingFootprints. Painted as the land
                // they stand on instead, which also drops the 2 cm step up into a floor, so the
                // ground reads as one surface and a road on it can actually be judged.
                if (!VillageHost.ShowBuildingFootprints
                    && (terrain == Terrain.Wall || terrain == Terrain.Floor))
                    terrain = Terrain.Grass;

                bool isResidential = residential != null && residential[gy, gx];

                float h00 = corner[gy, gx];
                float h10 = corner[gy, gx + 1];
                float h11 = corner[gy + 1, gx + 1];
                float h01 = corner[gy + 1, gx];

                if (VillageHost.FlatGroundColour)
                {
                    // ONE COLOUR, NOT ONE HEIGHT. h00..h11 above come from `corner`, which is the
                    // USGS elevation, and nothing here touches it - the land keeps every foot of
                    // its relief. What goes away is the PAINT: field against town grass, the
                    // GroundZoning scatter that reads as bushes, wooded ground, paved yards, the
                    // churchyard, and the road terrain. All of it one green, so a centreline can
                    // be judged against a lot line with nothing else competing.
                    //
                    // Water is the one exception, kept blue on the owner's call: the North Fork
                    // is a landmark you navigate the map by.
                    // A residentially zoned LOT is the third exception, after water: the same
                    // green a quarter darker, so where the town actually lives reads at a
                    // glance. Its HEIGHT is still grass - hiding the footprints was about losing
                    // the 2 cm step into a floor, and a patch that read darker but stood proud
                    // would put that straight back.
                    bool wet = terrain == Terrain.Water;
                    submeshGrid[gy, gx] = wet ? SubmeshFor(Terrain.Water)
                                        : isResidential ? dwelling
                                        : SubmeshFor(Terrain.Grass);
                    flatGrid[gy, gx] = HeightOf(wet ? Terrain.Water : Terrain.Grass);
                }
                else
                {
                    submeshGrid[gy, gx] = (terrain == Terrain.Grass || terrain == Terrain.Field)
                        ? SubmeshForLook(GroundZoning.LookAt(world, gx, gy, terrain, h00, h10, h01, h11))
                        : SubmeshFor(terrain);
                    flatGrid[gy, gx] = HeightOf(terrain);
                }
            }

            // ---- merge into runs, greedily, bounded two ways ----
            //
            // A run may grow in either direction only while every tile in it agrees on BOTH of
            // the following, and stops the moment either one would be broken:
            //
            //  - it may not cross a CHUNK edge, for the same reason a riser may not (see
            //    AssertFootprint below): the chunk a run is filed under is decided by chunks.At
            //    on its OWN first tile, and a run that then reached past that chunk's boundary
            //    would be correctly wound, correctly coloured geometry filed under a chunk it
            //    only partly belongs to - and it would vanish from the angles that chunk is
            //    culled from, which is exactly the fault AssertFootprint exists to catch.
            //
            //  - it may not cross an ELEVATION GRID line. Content/elevation.txt resolves the
            //    real ground at 30 m (ElevationGrid.Step) and HeightAt bilinearly interpolates
            //    WITHIN each 30 m cell - so a quad whose four corners sit on one cell's own four
            //    sample points reproduces that cell's real surface exactly, the same as the old
            //    one-quad-per-tile mesh always did at 1 m, because both are just the two
            //    triangles a bilinear cell has always been drawn as. A quad spanning MORE than
            //    one cell would not: the true surface bends differently in the next cell over,
            //    and one flat quad across the seam would silently average the two rather than
            //    following either. Capping runs at the data's own resolution is not a loss of
            //    detail the data had - a run that stays inside one cell was already flat or a
            //    single tilted plane as far as the USGS grid actually measured it.
            //
            // BANK never merges at all, whatever these two bounds would otherwise allow, and is
            // the one exception carved out ahead of them. It is the one look that exists BECAUSE
            // a tile's own slope cleared BankGrade (see GroundZoning) - which is exactly where a
            // stretch of real ground is most likely to still be curving inside a single 30 m
            // cell, the one case the cell bound is least likely to be generous enough for. Banks
            // are a small fraction of the map by construction, so keeping every one of them at
            // 1 m costs almost nothing.
            int elevStep = ElevationGrid.Step;
            if (elevStep <= 0)
                elevStep = Mathf.Max(world.Width, world.Height);   // no elevation.txt loaded -
                                                                    // flat everywhere, so nothing
                                                                    // real to preserve by capping
            int bankSubmesh = Materials3D.GroundOrder.Length + (int)Materials3D.ZonedGround.Bank;

            var claimed = new bool[world.Height, world.Width];
            int quads = 0;

            for (int gy = 0; gy < world.Height; gy++)
            for (int gx = 0; gx < world.Width; gx++)
            {
                if (claimed[gy, gx]) continue;
                int sm = submeshGrid[gy, gx];

                int w = 1, h = 1;
                if (sm != bankSubmesh)
                {
                    int chunkX1 = (gx / size + 1) * size;
                    int cellX1 = (gx / elevStep + 1) * elevStep;
                    int maxX = Mathf.Min(Mathf.Min(chunkX1, cellX1), world.Width);
                    while (gx + w < maxX && !claimed[gy, gx + w] && submeshGrid[gy, gx + w] == sm)
                        w++;

                    int chunkY1 = (gy / size + 1) * size;
                    int cellY1 = (gy / elevStep + 1) * elevStep;
                    int maxY = Mathf.Min(Mathf.Min(chunkY1, cellY1), world.Height);
                    while (gy + h < maxY)
                    {
                        bool rowMatches = true;
                        for (int dx = 0; dx < w; dx++)
                            if (claimed[gy + h, gx + dx] || submeshGrid[gy + h, gx + dx] != sm)
                            { rowMatches = false; break; }
                        if (!rowMatches) break;
                        h++;
                    }
                }

                for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    claimed[gy + dy, gx + dx] = true;

                // Small height differences keep surfaces from z-fighting and read as real:
                // water sits in its channel, roads are worn slightly below the verge. The real
                // ground's own elevation is ADDED to that per corner rather than once for the
                // whole quad - two neighbouring RUNS of the same terrain share a world x,y at
                // their common edge, so they sample the identical height there and the surface
                // stays seamless. Only a terrain-TYPE boundary (below) still needs a riser; a
                // smooth real slope needs none, because there is no gap for one to close.
                float flat = flatGrid[gy, gx];
                float x0 = gx, x1 = gx + w;
                float z0 = -gy, z1 = -(gy + h);

                var into = chunks.At(gx, gy);
                int v0 = into.Verts.Count;

                into.Verts.Add(new Vector3(x0, flat + corner[gy, gx], z0));
                into.Verts.Add(new Vector3(x1, flat + corner[gy, gx + w], z0));
                into.Verts.Add(new Vector3(x1, flat + corner[gy + h, gx + w], z1));
                into.Verts.Add(new Vector3(x0, flat + corner[gy + h, gx], z1));

                for (int i = 0; i < 4; i++) into.Normals.Add(Vector3.up);

                // UVs in world units so a tiling texture runs continuously across the village
                // instead of restarting at every tile - and, now, so it runs continuously
                // across a chunk boundary, and across a run boundary, too. This is why the UVs
                // are in absolute metres and not relative to anything: split the mesh, or merge
                // several tiles into one quad, and nothing about the texture moves.
                into.Uvs.Add(new Vector2(x0, -z0));
                into.Uvs.Add(new Vector2(x1, -z0));
                into.Uvs.Add(new Vector2(x1, -z1));
                into.Uvs.Add(new Vector2(x0, -z1));

                // Winding matters more than the normals do. Unity takes a triangle's facing
                // from Cross(v1-v0, v2-v0), and culls the back. Wound the other way round,
                // these quads face straight down: lit correctly, shaded correctly, and
                // invisible from every camera above the ground - which is all of them.
                var tris = into.Tris[sm];
                tris.Add(v0); tris.Add(v0 + 1); tris.Add(v0 + 2);
                tris.Add(v0); tris.Add(v0 + 2); tris.Add(v0 + 3);
                quads++;
            }

            // Land beyond the last tile.
            //
            // Without this the village stands on a floating slab with grey nothing past the
            // edge, and in any wide shot that edge is the first thing the eye finds. None of
            // it is simulated - nobody walks on it, nothing is placed on it, no tile exists
            // there. It is just the countryside the village happens to be standing in, and it
            // costs a dozen quads.
            //
            // Two kilometres rather than a few hundred metres, because a flat plane's edge
            // does not go away with distance - it just becomes a straight line further off,
            // and you have swapped one visible edge for another. The number that matters is
            // the fog: at this range the far edge is about 98% fogged out, so the land ends
            // in haze rather than ending in a line. It is still only a dozen quads.
            //
            // The west and east sides are built AROUND the lane rather than over it. Ashcombe
            // Street reaches both edges of the map, and a main road that simply stopped in the
            // middle of a hedged field would be the one thing out here that reads as an error
            // rather than as distance - so the carriageway carries on at its own level until
            // the fog takes it. Which rows it runs in is read off the edge column, so the lane
            // follows village.txt instead of the road being written down twice.
            const float Skirt = 2000f;
            float ex = world.Width, ez = -world.Height;
            int risers = 0;

            Flank(-Skirt, 0f, LaneAt(world, 0));                     // west
            Flank(ex, ex + Skirt, LaneAt(world, world.Width - 1));   // east
            Surround(beyond, 0f, ex, Skirt, 0f, 0f, pasture);        // north
            Surround(beyond, 0f, ex, ez, ez - Skirt, 0f, pasture);   // south

            // Risers.
            //
            // Every surface above is a single-sided quad lying flat, so where two neighbours
            // sit at different heights the step between them is simply not there: from the low
            // side, at any shallow angle, the view goes through the gap and straight out of the
            // bottom of the map. On the Ash - eight rows of water 35cm down, running the full
            // width of the village - that showed as a band of sky lying along the far bank.
            //
            // A riser goes in the submesh of the HIGHER surface, because the face is the cut
            // edge of the higher ground rather than of the lower: a river bank is bank, not
            // water, and a 35cm wall of glossy blue down the length of the Ash would be a worse
            // artefact than the hole it filled.
            //
            // One pass over every edge on the map - for each tile, the edge on its west side
            // and the edge on its north side - with both loops running one past the last tile
            // so the rim where the map meets the pasture skirt is included.
            //
            // A riser is filed in the chunk of the tile it is the west or north edge OF, which
            // is what keeps it inside that chunk's footprint: the face lies exactly on the
            // chunk's own boundary plane when the tile is the first in its column or row. It is
            // worth being sure of, because a riser filed one chunk out is a strip of river bank
            // that goes missing from precisely the angles the riser exists to cover - see
            // AssertFootprint.
            for (int gy = 0; gy <= world.Height; gy++)
            for (int gx = 0; gx <= world.Width; gx++)
            {
                var into = chunks.At(gx, gy);

                if (gy < world.Height)
                {
                    float west = HeightAt(world, gx - 1, gy), east = HeightAt(world, gx, gy);
                    if (west != east)
                        Riser(into, gx, -gy, gx, -(gy + 1f),
                              Mathf.Min(west, east), Mathf.Max(west, east),
                              east > west ? Vector3.left : Vector3.right,
                              SubmeshAt(world, east > west ? gx : gx - 1, gy, pasture));
                }

                if (gx < world.Width)
                {
                    float north = HeightAt(world, gx, gy - 1), south = HeightAt(world, gx, gy);
                    if (north != south)
                        Riser(into, gx, -gy, gx + 1f, -gy,
                              Mathf.Min(north, south), Mathf.Max(north, south),
                              south > north ? Vector3.forward : Vector3.back,
                              SubmeshAt(world, gx, south > north ? gy : gy - 1, pasture));
                }
            }

            // One side of the skirt, split around the corridor the lane leaves the map through.
            void Flank(float ax, float bx, (int first, int last) lane)
            {
                if (lane.first < 0)
                {
                    Surround(beyond, ax, bx, Skirt, ez - Skirt, 0f, pasture);
                    return;
                }

                float road = HeightOf(Terrain.Road);
                float z0 = -lane.first, z1 = -(lane.last + 1f);

                Surround(beyond, ax, bx, Skirt, z0, 0f, pasture);
                Surround(beyond, ax, bx, z1, ez - Skirt, 0f, pasture);
                Surround(beyond, ax, bx, z0, z1, road, SubmeshFor(Terrain.Road));

                // Worn below its verges like the rest of the road, so it needs the same pair of
                // risers the kerbs inside the map get - each one looking at the carriageway.
                Riser(beyond, ax, z0, bx, z0, road, 0f, Vector3.back, pasture);
                Riser(beyond, ax, z1, bx, z1, road, 0f, Vector3.forward, pasture);
            }

            void Surround(MeshChunk into, float ax, float bx, float az, float bz,
                          float y, int submesh)
            {
                // Sampled once at the quad's own centre rather than per corner: this is the flat
                // exterior skirt, and ElevationGrid clamps anything past the real data to its
                // nearest edge column anyway, so a quad standing half in and half out of real
                // coverage would get an oddly averaged tilt for no benefit. One sample keeps it
                // flat and flush with the real ground at the boundary, which is all this owes -
                // nobody stands on the pasture skirt to notice it stopped being real terrain.
                float yy = y + ElevationGrid.HeightAt((ax + bx) * 0.5f, -(az + bz) * 0.5f);

                int v = into.Verts.Count;
                into.Verts.Add(new Vector3(ax, yy, az));
                into.Verts.Add(new Vector3(bx, yy, az));
                into.Verts.Add(new Vector3(bx, yy, bz));
                into.Verts.Add(new Vector3(ax, yy, bz));

                for (int i = 0; i < 4; i++) into.Normals.Add(Vector3.up);
                for (int i = v; i < into.Verts.Count; i++)
                    into.Uvs.Add(new Vector2(into.Verts[i].x, -into.Verts[i].z));

                var tris = into.Tris[submesh];
                tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
                tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
            }

            // One vertical face closing a step, from the edge it stands on and the way it has
            // to look - which is always at the LOWER of the two surfaces, that being the side
            // you can otherwise see under.
            //
            // The winding is DERIVED from that facing rather than written out per call site.
            // Unity takes a triangle's facing from Cross(v1 - v0, v2 - v0), which for the vertex
            // order below comes to (-dz, 0, dx) for a walk d from a to b; so if walking a->b
            // would look the wrong way, the ends are swapped. There is one formula to get right
            // instead of one orientation per caller, and AssertWinding then checks that formula
            // against what Unity itself computes.
            void Riser(MeshChunk into, float ax, float az, float bx, float bz,
                       float low, float high, Vector3 facing, int submesh)
            {
                if (-(bz - az) * facing.x + (bx - ax) * facing.z < 0f)
                {
                    float sx = ax, sz = az;
                    ax = bx; az = bz;
                    bx = sx; bz = sz;
                }

                // Sampled per end rather than once, so the riser stays flush with the ground
                // tiles either side of it - they sample the same two world points at their own
                // shared corners - while the LOCAL step it exists to close (low to high) stays
                // exactly what it always was, wherever on the real slope it happens to stand.
                float elevA = ElevationGrid.HeightAt(ax, -az);
                float elevB = ElevationGrid.HeightAt(bx, -bz);

                int v = into.Verts.Count;
                into.Verts.Add(new Vector3(ax, low + elevA, az));
                into.Verts.Add(new Vector3(bx, low + elevB, bz));
                into.Verts.Add(new Vector3(bx, high + elevB, bz));
                into.Verts.Add(new Vector3(ax, high + elevA, az));

                for (int i = 0; i < 4; i++) into.Normals.Add(facing);

                // Metres along the face and metres up it, in the same world units the flat
                // ground uses, so a kerb carries the road's texture rather than restarting it.
                for (int i = v; i < into.Verts.Count; i++)
                    into.Uvs.Add(new Vector2(into.Verts[i].x - into.Verts[i].z, into.Verts[i].y));

                var tris = into.Tris[submesh];
                tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
                tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
                risers++;
            }

            foreach (var chunk in chunks.All)
            {
                AssertWinding(chunk, $"Ground chunk {chunk.Col},{chunk.Row}");
                AssertFootprint(chunk, size);
            }
            AssertWinding(beyond, "Ground surround");

            // A flat floor casts nothing useful, and never did.
            var tiles = chunks.Emit(go.transform, "Ground", materials, ShadowCastingMode.Off, true);
            skirt.Emit(go.transform, "Surround", materials, ShadowCastingMode.Off, true);

            Debug.Log($"Ground mesh ({size}m chunks): "
                    + $"{chunks.VertexCount + skirt.VertexCount:N0} vertices, "
                    + $"{chunks.TriangleCount + skirt.TriangleCount:N0} triangles, "
                    + $"{quads:N0} merged ground quads over {world.Width * world.Height:N0} tiles, "
                    + $"{risers:N0} risers, {tiles.Count} chunks + surround, "
                    + $"{chunks.DrawCalls + skirt.DrawCalls} draw calls.");
        }

        /// <summary>
        /// Check every triangle is wound to face the way its own normal says it does - the
        /// floor at the sky, each riser at the low ground whose step it closes.
        ///
        /// Worth the lines because the failure is invisible in every way except the finished
        /// picture: wrong-wound ground still has correct normals, correct bounds, correct
        /// materials and a correct vertex count, and it lights and shadows itself perfectly
        /// well - facing down, under the map, culled. It read as "the ground textures are too
        /// dark" for a while, which sent the fix to entirely the wrong place.
        ///
        /// Checking against the normal rather than against "up" is what lets the risers in: it
        /// puts Unity's own Cross up against the direction the geometry was built to face, so
        /// a sign slipped in the winding formula fails here instead of in a screenshot. Every
        /// triangle, not a sample - it is one cross product each, and the ground is a hundred
        /// thousand of them, which is nothing set against being wrong for a fortnight.
        ///
        /// Run per chunk, and the chunk is named in the error, because "triangle 4102" means
        /// nothing once there are twenty meshes it could be in.
        /// </summary>
        private static void AssertWinding(MeshChunk chunk, string what)
        {
            for (int s = 0; s < chunk.Tris.Length; s++)
            {
                var tris = chunk.Tris[s];
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    var a = chunk.Verts[tris[i]];
                    var facing = Vector3.Cross(chunk.Verts[tris[i + 1]] - a,
                                               chunk.Verts[tris[i + 2]] - a);
                    if (Vector3.Dot(facing, chunk.Normals[tris[i]]) > 0f) continue;

                    Debug.LogError($"{what}, submesh {s}, triangle {i / 3} is wound backwards: "
                                 + $"{facing} against a normal of {chunk.Normals[tris[i]]}. It "
                                 + "will be backface-culled from the one direction it exists to "
                                 + "be seen from, silently.");
                    return;
                }
            }
        }

        /// <summary>
        /// Check a ground chunk's geometry lies inside the chunk it was filed under.
        ///
        /// This is the hole that chunking opens in AssertWinding, and it wants closing for the
        /// same reason that one does. Winding still catches a face built backwards; what it
        /// cannot see is a face built the RIGHT way round and filed in the WRONG chunk. That
        /// fails just as quietly - the mesh is complete, the materials are right, the bounds do
        /// contain the geometry so it draws perfectly well - and then a strip of ground is culled
        /// along with a chunk it does not belong to and goes missing from the angles where that
        /// chunk happens to be off screen. In the wide shot, where everything is on screen, it
        /// does not show at all, which is exactly the sort of fault that gets found late.
        ///
        /// Ground is the one thing that can be checked this tightly: tiles and risers are cut on
        /// chunk boundaries and never cross them, so the bound is exact and inclusive. A wall
        /// run, a roof's eaves and a tree's canopy all overhang the chunk they were filed under
        /// on purpose, and are not checked.
        /// </summary>
        private static void AssertFootprint(MeshChunk chunk, int size)
        {
            float x0 = (float)chunk.Col * size, x1 = x0 + size;
            float y0 = (float)chunk.Row * size, y1 = y0 + size;

            for (int i = 0; i < chunk.Verts.Count; i++)
            {
                var v = chunk.Verts[i];
                if (v.x >= x0 && v.x <= x1 && -v.z >= y0 && -v.z <= y1) continue;

                Debug.LogError($"Ground chunk {chunk.Col},{chunk.Row} holds a vertex at "
                             + $"{v.x},{-v.z}, outside its own {x0}..{x1} by {y0}..{y1} "
                             + "footprint. It will still draw, but it is culled with the wrong "
                             + "chunk and will vanish from angles it should be visible at.");
                return;
            }
        }

        /// <summary>
        /// Every tile inside a lot the county's class code calls residential.
        ///
        /// Scanline point-in-polygon, per parcel, over that parcel's own bounding box. A lot is
        /// tens of metres across and there are 794 of them, so this touches a few hundred
        /// thousand tiles once - against the hundred million tests that asking every tile which
        /// parcel it is in would cost.
        ///
        /// Returns null rather than an empty mask when there are no parcels or no county
        /// records, so the caller draws plain grass and nothing pretends to know a zoning it
        /// does not have.
        /// </summary>
        private static bool[,] ResidentialMask(int width, int height)
        {
            var parcels = ParcelIndex.All;
            if (parcels.Count == 0) return null;

            var mask = new bool[height, width];
            int lots = 0, tiles = 0;

            int unrecorded = 0;
            foreach (var parcel in parcels)
            {
                // EIGHTEEN OF THE 794 HAVE NO COUNTY RECORD. parcel-county.txt says so on its
                // second line - 776 matched by nearest centroid - and For() returns null for the
                // rest. Unguarded that is a NullReferenceException inside BuildGround, which
                // aborts VillageHost.Awake half built and shows as a black screen.
                var record = CountyRecord.For(parcel.Id);
                if (record == null) { unrecorded++; continue; }
                if (record.Zoning != ParcelNotes.Zoning.Residential) continue;
                lots++;

                var p = parcel.Points;
                var b = parcel.Bounds;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(b.xMin));
                int x1 = Mathf.Min(width - 1, Mathf.CeilToInt(b.xMax));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(b.yMin));
                int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(b.yMax));

                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    // Tile centres, so a lot line running along a tile edge lands on one side of
                    // it rather than on both - neighbouring lots would otherwise each claim the
                    // shared boundary and the seam would read a tile wide.
                    if (mask[y, x] || !Inside(p, x + 0.5f, y + 0.5f)) continue;
                    mask[y, x] = true;
                    tiles++;
                }
            }

            Debug.Log($"[zoning] {lots} of {parcels.Count} lots are residential in the county's "
                    + $"class codes - {tiles:N0} tiles shaded"
                    + (unrecorded > 0 ? $"; {unrecorded} lots the county has no record for." : "."));
            return lots == 0 ? null : mask;
        }

        /// <summary>Crossing number, the standard one. Points are a closed ring.</summary>
        private static bool Inside(Vector2[] poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > y) == (poly[j].y > y)) continue;
                float t = (poly[j].y - poly[i].y);
                if (Mathf.Abs(t) < 1e-9f) continue;
                if (x < (poly[j].x - poly[i].x) * (y - poly[i].y) / t + poly[i].x) inside = !inside;
            }
            return inside;
        }

        private static int SubmeshFor(Terrain t)
        {
            for (int i = 0; i < Materials3D.GroundOrder.Length; i++)
                if (Materials3D.GroundOrder[i] == t) return i;
            return 0;   // walls sit on grass; their footprint is covered by the wall cube anyway
        }

        /// <summary>The submesh a GroundZoning verdict draws in - Grass and Field fall back to
        /// their ordinary GroundOrder submesh, unchanged for every tile zoning does not
        /// overrule; Hard, Rough and Bank are the three appended after GroundOrder.</summary>
        private static int SubmeshForLook(GroundLook look)
        {
            switch (look)
            {
                case GroundLook.Field: return SubmeshFor(Terrain.Field);
                case GroundLook.Hard: return Materials3D.GroundOrder.Length + (int)Materials3D.ZonedGround.Hard;
                case GroundLook.Rough: return Materials3D.GroundOrder.Length + (int)Materials3D.ZonedGround.Rough;
                case GroundLook.Bank: return Materials3D.GroundOrder.Length + (int)Materials3D.ZonedGround.Bank;
                default: return SubmeshFor(Terrain.Grass);
            }
        }

        /// <summary>
        /// The submesh a tile draws in, for a riser's higher side; the pasture skirt beyond the
        /// last tile. Zoning-aware for the same reason the flat ground is - a kerb between a
        /// road and a commercial yard should carry the yard's own material, not plain grass -
        /// but not slope-aware: a riser's higher side is a different tile from the one whose
        /// corners are in hand here, and re-sampling ElevationGrid just to catch the rare case
        /// of a riser standing next to a steep bank was not worth a second elevation lookup for
        /// every riser on the map.
        /// </summary>
        private static int SubmeshAt(WorldModel world, int gx, int gy, int pasture)
        {
            if (!world.Grid.InBounds(gx, gy)) return pasture;

            var terrain = world.Grid.TerrainAt(gx, gy);
            return (terrain == Terrain.Grass || terrain == Terrain.Field)
                ? SubmeshForLook(GroundZoning.ZoningLookAt(world, gx, gy, terrain))
                : SubmeshFor(terrain);
        }

        /// <summary>
        /// The height of the ground at a tile, and of the land past the edge of the map.
        ///
        /// Outside is the pasture skirt, flat at zero - except in the corridor the lane runs
        /// out through, which carries the carriageway on at its own level. Without that
        /// exception the riser pass lays a four-centimetre kerb straight across the road at
        /// the map boundary, which is exactly the thing extending the road was meant to stop.
        /// </summary>
        private static float HeightAt(WorldModel world, int gx, int gy)
        {
            if (world.Grid.InBounds(gx, gy)) return HeightOf(world.Grid.TerrainAt(gx, gy));
            if (gy < 0 || gy >= world.Height) return 0f;

            int edge = gx < 0 ? 0 : world.Width - 1;
            return world.Grid.TerrainAt(edge, gy) == Terrain.Road ? HeightOf(Terrain.Road) : 0f;
        }

        /// <summary>
        /// The rows of an edge column that are carriageway - the corridor the road leaves the
        /// map through, and so where the lane outside has to pick it up. Read off the grid so
        /// the lane follows village.txt rather than the road being written down twice. Returns
        /// (-1, -1) where no road reaches that edge, and the skirt is left whole.
        ///
        /// The first run only. A second road out of the same side would want its own band.
        /// </summary>
        private static (int first, int last) LaneAt(WorldModel world, int gx)
        {
            int first = -1, last = -1;
            for (int gy = 0; gy < world.Height; gy++)
            {
                if (world.Grid.TerrainAt(gx, gy) != Terrain.Road)
                {
                    if (first >= 0) break;
                    continue;
                }
                if (first < 0) first = gy;
                last = gy;
            }
            return (first, last);
        }

        private static float HeightOf(Terrain t)
        {
            switch (t)
            {
                case Terrain.Water: return -0.35f;
                case Terrain.Road: return -0.04f;
                case Terrain.Path: return -0.02f;
                case Terrain.Floor: return 0.02f;    // a step up into a building
                default: return 0f;
            }
        }

        // ---------- walls ----------

        /// <summary>
        /// Every wall in the village, built from contiguous runs and split across the chunk grid.
        ///
        /// Runs first: a cottage is four long boxes rather than twenty-four tile-sized ones.
        /// Then generated geometry rather than one object each, which matters for a reason
        /// beyond the draw call - a primitive cube has UVs that run 0..1 over each face however
        /// big the cube is, so a twelve-metre terrace stretched a single texture along its whole
        /// length and every wall in the village read as a vertical smear. Generating the
        /// geometry means the UVs can be in metres, and stone is the size of stone.
        ///
        /// A run goes WHOLE into the chunk its first tile is in, however far past the boundary
        /// it then reaches. Cutting it at the boundary was the obvious thing and is wrong twice
        /// over: the faces are mapped in metres FROM THE START OF THE RUN, so a cut restarts the
        /// stonework mid-wall and every terrace long enough to cross a chunk grows a seam down
        /// the middle of it - and the cut would put two end faces back to back in the same plane
        /// to z-fight. The chunk's bounding box simply grows to hold the overhang, which is what
        /// a bounding box is for and costs only a little culling accuracy at the edges.
        /// </summary>
        private static void BuildWalls(WorldModel world, Transform parent)
        {
            var walls = new GameObject("Walls");
            walls.transform.SetParent(parent, false);

            var chunks = new MeshChunks(1, MeshChunks.Size, 0, 0, world.Width, world.Height);
            var used = new bool[world.Width * world.Height];
            int count = 0;

            // ---- which building owns each wall tile ----
            //
            // Runs used to be merged PURELY GEOMETRICALLY, which was correct while every building
            // in the village was three metres tall and is wrong the moment they are not: a run
            // that spans two buildings would have to pick one of their heights.
            //
            // Where two buildings share a boundary tile the LOWEST PLACE ID WINS. Any rule would
            // do; what matters is that it is FIXED. Letting iteration order decide a wall's
            // height would make the mesh differ run to run, and the twelve snapshots are asserted
            // byte-identical across two separate Unity processes - so this would not fail as a
            // wrong-looking wall, it would fail as a regression check that has quietly stopped
            // meaning anything.
            var owner = new int[world.Width * world.Height];
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;

            // Tiles belonging to a building that arrives as a bought model. Their walls are in
            // the model, so the run-walker below has to leave them alone entirely.
            var bought = new bool[world.Width * world.Height];

            foreach (var place in world.AllPlaces)
            {
                if (!PlaceKindTable.Current.Row(place.Kind).IsBuilding) continue;

                // A bought model brings its own walls. Marked rather than skipped, because the
                // wall run below is walked off the TERRAIN GRID and not off the place list -
                // leaving these unowned would not omit them, it would just draw them at the
                // default height with nobody responsible for them.
                if (CityBuildings.Handles(place))
                {
                    var m = place.Bounds;
                    for (int y = m.Y; y < m.Y + m.H; y++)
                    for (int x = m.X; x < m.X + m.W; x++)
                        if (x >= 0 && y >= 0 && x < world.Width && y < world.Height)
                            bought[y * world.Width + x] = true;
                    continue;
                }

                var b = place.Bounds;
                for (int y = b.Y; y < b.Y + b.H; y++)
                for (int x = b.X; x < b.X + b.W; x++)
                {
                    if (x < 0 || y < 0 || x >= world.Width || y >= world.Height) continue;
                    int at = y * world.Width + x;
                    if (owner[at] < 0 || place.Id.Value < owner[at]) owner[at] = place.Id.Value;
                }
            }

            // WHERE THE FOOT OF THIS WALL SITS.
            //
            // A building's walls take ONE height for the whole footprint - the ground under the
            // middle of it - so the building stands level and the ground is cut and filled around
            // it. A garden or churchyard wall belongs to no building and follows the contour
            // tile by tile, because a garden wall really does run up a slope.
            float BaseAt(int gx, int gy)
            {
                int id = owner[gy * world.Width + gx];
                if (id >= 0)
                {
                    var p = world.GetPlace(new PlaceId(id));
                    if (p != null) return Space3D.GroundUnder(p.Bounds);
                }
                return ElevationGrid.HeightAt(gx + 0.5f, gy + 0.5f);
            }

            // The ABSOLUTE height of the wall top, ground included, so it meets the roof - which
            // is lifted onto the same ground in RoofBuilder. Returning the eaves alone built
            // every wall from y=0 up, which on Rossville's contour left the roof floating three
            // metres clear of the walls holding it up.
            float HeightAt(int gx, int gy)
            {
                int id = owner[gy * world.Width + gx];
                float ground = BaseAt(gx, gy);
                if (id < 0) return ground + Space3D.WallHeight;
                var p = world.GetPlace(new PlaceId(id));
                return ground + (p == null ? Space3D.WallHeight : MassingGrammars.Of(p).Eaves);
            }

            int OwnerAt(int gx, int gy) => owner[gy * world.Width + gx];

            // Every test below goes through this rather than IsWall, so a bought building's
            // perimeter is invisible to the run-walker and no run can start on or cross it.
            bool Walled(int gx, int gy) =>
                IsWall(world, gx, gy) && !bought[gy * world.Width + gx];

            // Horizontal runs first, then whatever vertical runs remain.
            for (int gy = 0; gy < world.Height; gy++)
            {
                int gx = 0;
                while (gx < world.Width)
                {
                    if (!Walled(gx, gy) || used[gy * world.Width + gx]) { gx++; continue; }

                    int start = gx;
                    int mine = OwnerAt(gx, gy);
                    while (gx < world.Width && Walled(gx, gy) && !used[gy * world.Width + gx]
                           && OwnerAt(gx, gy) == mine)
                    {
                        used[gy * world.Width + gx] = true;
                        gx++;
                    }
                    int length = gx - start;
                    if (length >= 2)
                    {
                        AddWall(chunks.At(start, gy), start, gy, length, 1, BaseAt(start, gy), HeightAt(start, gy));
                        count++;
                    }
                    else { used[gy * world.Width + start] = false; }   // leave singles for the vertical pass
                }
            }

            for (int gx = 0; gx < world.Width; gx++)
            {
                int gy = 0;
                while (gy < world.Height)
                {
                    if (!Walled(gx, gy) || used[gy * world.Width + gx]) { gy++; continue; }

                    int start = gy;
                    int mine = OwnerAt(gx, gy);
                    while (gy < world.Height && Walled(gx, gy) && !used[gy * world.Width + gx]
                           && OwnerAt(gx, gy) == mine)
                    {
                        used[gy * world.Width + gx] = true;
                        gy++;
                    }
                    AddWall(chunks.At(gx, start), gx, start, 1, gy - start, BaseAt(gx, start), HeightAt(gx, start));
                    count++;
                }
            }

            var renderers = chunks.Emit(walls.transform, "Walls", new[] { Materials3D.Wall },
                                        ShadowCastingMode.On, true);

            Debug.Log($"Walls: {count} runs, {chunks.VertexCount:N0} vertices, "
                    + $"{renderers.Count} chunk meshes.");
        }

        private static bool IsWall(WorldModel world, int gx, int gy) =>
            world.Grid.TerrainAt(gx, gy) == Terrain.Wall;

        /// <summary>
        /// One run of wall, appended to its chunk. No bottom face - you never see the
        /// underside - but a top one, because the roofs lift off when you come in close and
        /// without a cap every wall in the village grows a metre-wide hole along its top edge
        /// looking three storeys down at the ground. That is the exact view the cutaway exists
        /// to give you, so "the roof covers it" was true only when nobody was looking.
        ///
        /// Each face gets its own four vertices so the UVs can be per-face - shared corners
        /// would force one wrapping across two perpendicular walls and crease the texture at
        /// every corner in the village. It is also what makes the chunk split free: no vertex
        /// is shared between faces, so RecalculateNormals gives each face its own flat normal
        /// whichever mesh it ends up in, and splitting the runs across meshes cannot change a
        /// single shaded pixel.
        /// </summary>
        private static void AddWall(MeshChunk into, int gx, int gy, int w, int h,
                                    float bottom, float top)
        {
            var verts = into.Verts;
            var uvs = into.Uvs;
            var tris = into.Tris[0];

            float x0 = gx, x1 = gx + w;
            float z0 = -gy, z1 = -(gy + h);

            // (corner a, corner b) walked so that a->b->up is wound outward.
            // Sunk half a metre below the ground it stands on, so a wall meets a contour that
            // dips slightly across the footprint without daylight under its foot.
            float y0 = bottom - 0.5f;

            Face(new Vector3(x1, y0, z0), new Vector3(x0, y0, z0));   // north
            Face(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1));   // south
            Face(new Vector3(x1, y0, z1), new Vector3(x1, y0, z0));   // east
            Face(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1));   // west

            Cap();

            void Cap()
            {
                int i = verts.Count;

                verts.Add(new Vector3(x0, top, z0));
                verts.Add(new Vector3(x1, top, z0));
                verts.Add(new Vector3(x1, top, z1));
                verts.Add(new Vector3(x0, top, z1));

                for (int v = i; v < verts.Count; v++)
                    uvs.Add(new Vector2(verts[v].x, -verts[v].z));

                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            void Face(Vector3 a, Vector3 b)
            {
                int i = verts.Count;
                float run = Vector3.Distance(a, b);

                // `top` IS AN ABSOLUTE HEIGHT, not a rise. It always was for Cap(), which places
                // its four corners at exactly `top`; this used to extrude `Vector3.up * top` from
                // the foot instead, and the two agreed only because the foot was hard-coded to
                // y=0. The moment walls were seated on real ground the sides shot to base+top -
                // 10.80 m on a house whose eaves are at 7.75 - while the cap stayed put, so every
                // building grew a parapet exactly as tall as the ground it stood on.
                float rise = top - a.y;

                verts.Add(a);
                verts.Add(b);
                verts.Add(new Vector3(b.x, top, b.z));
                verts.Add(new Vector3(a.x, top, a.z));

                // Metres across, metres up. The texture is the same size on a cottage as on
                // the mill, which is the whole point of doing this by hand.
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(run, 0f));
                uvs.Add(new Vector2(run, rise));
                uvs.Add(new Vector2(0f, rise));

                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }
        }
    }
}
