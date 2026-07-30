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
        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("Village");
            root.transform.SetParent(parent, false);

            BuildGround(world, root.transform);
            BuildWalls(world, root.transform);
            BuildFurniture(world, root.transform);
            BuildProps(world, root.transform);
            RoofBuilder.Build(world, root.transform);
            Frontage.Build(world, root.transform);
            Countryside.Build(world, root.transform);

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
                    height * 0.5f + 0.02f,          // sits on the raised interior floor
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

        private static void BuildGround(WorldModel world, Transform parent)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);

            // One submesh per terrain, plus a last one for the open country beyond the map.
            int pasture = Materials3D.GroundOrder.Length;
            int submeshes = pasture + 1;

            var materials = new Material[submeshes];
            for (int i = 0; i < submeshes; i++)
                materials[i] = i == pasture
                    ? Materials3D.Pasture
                    : Materials3D.ForTerrain(Materials3D.GroundOrder[i]);

            // The far rim is inclusive because the riser pass below walks one PAST the last
            // tile on both axes: a riser at x == world.Width is the cut edge of the last column
            // and has to have a chunk to go in.
            var chunks = new MeshChunks(submeshes, MeshChunks.Size, 0, 0, world.Width, world.Height);

            // The land beyond the map is deliberately NOT chunked. It is a dozen quads two
            // kilometres across, and dropped into the chunk grid it would drag one chunk's
            // bounding box out to the horizon - after which that chunk could never be culled
            // from anywhere, which is the exact fault this file was changed to fix. It stays one
            // small mesh that is always drawn, which is what a horizon is for.
            var skirt = MeshChunks.Single(submeshes);
            var beyond = skirt.At(0, 0);

            for (int gy = 0; gy < world.Height; gy++)
            for (int gx = 0; gx < world.Width; gx++)
            {
                var terrain = world.Grid.TerrainAt(gx, gy);
                int submesh = SubmeshFor(terrain);

                // Small height differences keep surfaces from z-fighting and read as real:
                // water sits in its channel, roads are worn slightly below the verge.
                float y = HeightOf(terrain);

                var into = chunks.At(gx, gy);
                int v0 = into.Verts.Count;
                float x0 = gx, x1 = gx + 1f;
                float z0 = -gy, z1 = -(gy + 1f);

                into.Verts.Add(new Vector3(x0, y, z0));
                into.Verts.Add(new Vector3(x1, y, z0));
                into.Verts.Add(new Vector3(x1, y, z1));
                into.Verts.Add(new Vector3(x0, y, z1));

                for (int i = 0; i < 4; i++) into.Normals.Add(Vector3.up);

                // UVs in world units so a tiling texture runs continuously across the village
                // instead of restarting at every tile - and, now, so it runs continuously
                // across a chunk boundary too. This is why the UVs are in absolute metres and
                // not relative to anything: split the mesh and nothing about the texture moves.
                into.Uvs.Add(new Vector2(x0, -z0));
                into.Uvs.Add(new Vector2(x1, -z0));
                into.Uvs.Add(new Vector2(x1, -z1));
                into.Uvs.Add(new Vector2(x0, -z1));

                // Winding matters more than the normals do. Unity takes a triangle's facing
                // from Cross(v1-v0, v2-v0), and culls the back. Wound the other way round,
                // these quads face straight down: lit correctly, shaded correctly, and
                // invisible from every camera above the ground - which is all of them.
                var tris = into.Tris[submesh];
                tris.Add(v0); tris.Add(v0 + 1); tris.Add(v0 + 2);
                tris.Add(v0); tris.Add(v0 + 2); tris.Add(v0 + 3);
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
                int v = into.Verts.Count;
                into.Verts.Add(new Vector3(ax, y, az));
                into.Verts.Add(new Vector3(bx, y, az));
                into.Verts.Add(new Vector3(bx, y, bz));
                into.Verts.Add(new Vector3(ax, y, bz));

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

                int v = into.Verts.Count;
                into.Verts.Add(new Vector3(ax, low, az));
                into.Verts.Add(new Vector3(bx, low, bz));
                into.Verts.Add(new Vector3(bx, high, bz));
                into.Verts.Add(new Vector3(ax, high, az));

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
                AssertFootprint(chunk);
            }
            AssertWinding(beyond, "Ground surround");

            // A flat floor casts nothing useful, and never did.
            var tiles = chunks.Emit(go.transform, "Ground", materials, ShadowCastingMode.Off, true);
            skirt.Emit(go.transform, "Surround", materials, ShadowCastingMode.Off, true);

            Debug.Log($"Ground mesh: {chunks.VertexCount + skirt.VertexCount:N0} vertices, "
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
        private static void AssertFootprint(MeshChunk chunk)
        {
            float x0 = (float)chunk.Col * MeshChunks.Size, x1 = x0 + MeshChunks.Size;
            float y0 = (float)chunk.Row * MeshChunks.Size, y1 = y0 + MeshChunks.Size;

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

        private static int SubmeshFor(Terrain t)
        {
            for (int i = 0; i < Materials3D.GroundOrder.Length; i++)
                if (Materials3D.GroundOrder[i] == t) return i;
            return 0;   // walls sit on grass; their footprint is covered by the wall cube anyway
        }

        /// <summary>The submesh a tile draws in; the pasture skirt beyond the last tile.</summary>
        private static int SubmeshAt(WorldModel world, int gx, int gy, int pasture) =>
            world.Grid.InBounds(gx, gy) ? SubmeshFor(world.Grid.TerrainAt(gx, gy)) : pasture;

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

            // A garden wall or a churchyard wall belongs to no building and keeps the old height.
            float HeightAt(int gx, int gy)
            {
                int id = owner[gy * world.Width + gx];
                if (id < 0) return Space3D.WallHeight;
                var p = world.GetPlace(new PlaceId(id));
                return p == null ? Space3D.WallHeight : MassingGrammars.Of(p).Eaves;
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
                        AddWall(chunks.At(start, gy), start, gy, length, 1, HeightAt(start, gy));
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
                    AddWall(chunks.At(gx, start), gx, start, 1, gy - start, HeightAt(gx, start));
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
        private static void AddWall(MeshChunk into, int gx, int gy, int w, int h, float top)
        {
            var verts = into.Verts;
            var uvs = into.Uvs;
            var tris = into.Tris[0];

            float x0 = gx, x1 = gx + w;
            float z0 = -gy, z1 = -(gy + h);

            // (corner a, corner b) walked so that a->b->up is wound outward.
            Face(new Vector3(x1, 0f, z0), new Vector3(x0, 0f, z0));   // north
            Face(new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1));   // south
            Face(new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z0));   // east
            Face(new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1));   // west

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

                verts.Add(a);
                verts.Add(b);
                verts.Add(b + Vector3.up * top);
                verts.Add(a + Vector3.up * top);

                // Metres across, metres up. The texture is the same size on a cottage as on
                // the mill, which is the whole point of doing this by hand.
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(run, 0f));
                uvs.Add(new Vector2(run, top));
                uvs.Add(new Vector2(0f, top));

                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }
        }
    }
}
