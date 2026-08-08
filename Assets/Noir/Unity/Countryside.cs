using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// The country the village stands in.
    ///
    /// The ground mesh already runs five hundred metres past the last tile, which stopped the
    /// village floating on a slab. But flat green to the horizon is its own kind of wrong: an
    /// English village is not surrounded by lawn, it is surrounded by fields, and what makes a
    /// field read as a field from the air is its boundary. Hedgerows do almost all the work
    /// here - an irregular patchwork of them turns that green into farmland in one move, with
    /// no new material and no new texture.
    ///
    /// Detail is spent where it can be seen. The first 120 metres get properly hedged fields
    /// with trees standing in the lines; beyond that it thins to copses and then to a wooded
    /// band that breaks the horizon, because at three hundred metres through fog a tree is a
    /// green mass and its trunk is nothing at all. Dressing the full five hundred metres at
    /// field detail would be several thousand objects nobody could ever resolve.
    ///
    /// NOTHING is placed on the map itself. Those tiles are authored and already dressed, so
    /// every position here is measured against the map rect before anything is built.
    ///
    /// Every choice comes from Materials3D.Scatter. The same land has to appear on every run:
    /// these frames are rendered offline and compared build against build, and countryside that
    /// reshuffled itself in between would make every one of those comparisons worthless.
    ///
    /// ALL OF IT IS COMBINED into chunk meshes, which is the one thing here that is about cost
    /// rather than about the picture. This is the largest single group of objects in the
    /// project - it was around seventeen hundred renderers, more than a third of everything -
    /// and it is also the group that gains least from being renderers at all: it is a ring of
    /// nearly identical trees a quarter of a kilometre away, half of it behind fog, none of it
    /// ever touched or clicked or lit individually. Combining costs the ability to cull one
    /// tree at a time, which out here is worth very little, and buys back an order of magnitude
    /// in objects. See the note on Chunk for why the grid out here is coarser than the
    /// village's.
    ///
    /// Combining did leave one bill behind it, though, and detail is now spent by DISTANCE as
    /// well as by band. Canopies are fewer than half the objects out here - 775 of 1,736 - and
    /// NINE TENTHS of the vertices, 399,000 of 440,000, because a sphere is 515 vertices and a
    /// hedge segment is 24. Left as renderers that hardly mattered; baked into a mesh a canopy
    /// costs its full 515 whether or not anybody could ever count them, and past the last hedge
    /// a whole tree is twenty pixels across. So beyond Massed - where the trees give up their
    /// trunks anyway - a canopy is built from the coarsest shape whose OUTLINE still holds, and
    /// that halves the countryside outright. See Canopy: the rule is arithmetic rather than
    /// taste, because the treeline is the thing that breaks the horizon and a canopy that reads
    /// as a polyhedron up there is worse than an expensive one.
    /// </summary>
    public static class Countryside
    {
        /// <summary>How far out the properly hedged fields reach, in metres.</summary>
        private const float Detail = 120f;

        /// <summary>
        /// How far out a tree is still worth building a trunk for, and where trees become bare
        /// canopy masses instead.
        ///
        /// The two ranges overlap by forty metres on purpose. A radius at which one technique
        /// stopped and the other started would draw itself as a ring on the ground, which is a
        /// worse artefact than either technique on its own.
        /// </summary>
        private const float Copsed = 190f;

        /// <summary>
        /// See <see cref="Copsed"/>. This is also the line the canopy detail drops at - a tree
        /// inside it keeps Unity's own sphere, vertex for vertex. See <see cref="Crown"/>.
        /// </summary>
        private const float Massed = 150f;

        /// <summary>How far out the woods reach. Past this it is open pasture and fog.</summary>
        private const float Horizon = 380f;

        /// <summary>
        /// Hedges are built in lengths of this. Long enough that a boundary is a few dozen
        /// objects rather than a few hundred, short enough that the top line still steps.
        /// </summary>
        private const float HedgeSegment = 7f;

        /// <summary>
        /// Keep-out margin around the map, in metres. Positions are already required to be
        /// outside the map rect; the margin makes sure the geometry hanging off them does not
        /// lean into the authored village either.
        /// </summary>
        private const float Clearance = 1.5f;

        /// <summary>
        /// Chunk edge out here, in metres - twice the village's.
        ///
        /// The village is chunked at 64 because that is roughly the length of a street and what
        /// you can see down one. Out here nothing is closer than the map edge and most of it is
        /// past the shadow distance and well into the fog, so finer culling buys almost nothing
        /// while costing a renderer per material per chunk on a ring that is over a kilometre
        /// round. Doubling the edge quarters the cells; the ones you can see are still discarded
        /// the moment you turn your back on them, which is all this needs to do.
        /// </summary>
        private const int Chunk = 128;

        /// <summary>
        /// How far past the map the chunk grid has to reach.
        ///
        /// Positions are bounded by Horizon, but the GEOMETRY hanging off them is not: a wood is
        /// a canopy up to twenty-one metres across, and a copse scatters its trees up to about
        /// thirteen metres from a cell centre that has itself been jittered. The grid is given
        /// room rather than sized to the exact worst case - being short only means a few objects
        /// are filed in the outermost chunk instead of just past it, which is a culling loss and
        /// never a hole, but it is not worth even that.
        /// </summary>
        private const int Reach = (int)Horizon + 40;

        /// <summary>
        /// Canopies built this run, and how many of them were built coarse. Tallied only so
        /// the log can say it: the number that matters when somebody next opens this file is
        /// what the distance rule is actually doing, and a vertex count on its own does not
        /// say. Reset per build, because in play mode this runs again.
        /// </summary>
        private static int _canopies, _coarse, _spared;

        public static void Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("Countryside");
            root.transform.SetParent(parent, false);

            _canopies = _coarse = _spared = 0;
            _handedOver.Clear();       // in play mode this runs again

            var chunks = new MeshChunks(Scenery.Count, Chunk,
                                        -Reach, -Reach, world.Width + Reach, world.Height + Reach);

            // A PLACE WITH NO FIELDS IN IT IS NOT IN FARMING COUNTRY.
            //
            // The field lattice - hedgerows, stone walls, the patchwork of boundaries - is the
            // single most village thing on screen, and it was being drawn to the horizon around
            // a city. Rather than add a switch to the map format, ask the map: Rossville is full
            // of `terrain field` and Rossville has none, so the content already says which this
            // is. Outskirt woodland stays either way, because a town has trees around it too.
            bool farmland = HasFarmland(world);

            int boundaries = farmland ? Fields(world, chunks) : 0;
            int copses = Copses(world, chunks);
            int woods = Woods(world, chunks);

            var renderers = chunks.Emit(root.transform, "Country", Scenery.Palette(),
                                        ShadowCastingMode.On, true);

            Debug.Log($"Countryside: {boundaries + copses + woods:N0} objects - "
                    + $"{boundaries:N0} field boundary, {copses:N0} copse and hedgerow tree, "
                    + $"{woods:N0} treeline - combined into {renderers.Count} chunk meshes, "
                    + $"{chunks.VertexCount:N0} vertices, {chunks.DrawCalls} draw calls. "
                    + $"{_coarse:N0} of {_canopies:N0} canopies built coarse, "
                    + $"{_spared:N0} vertices spared. "
                    + $"{_handedOver.Count:N0} near-band trees handed to CityGreenery.");
        }

        /// <summary>
        /// Does this place have farmland in it? Read off the map rather than declared, so a new
        /// map says what it is by what is in it and nobody has to remember a flag.
        /// </summary>
        private static bool HasFarmland(WorldModel world)
        {
            for (int y = 0; y < world.Height; y += 4)
            for (int x = 0; x < world.Width; x += 4)
                if (world.Grid.TerrainAt(x, y) == Noir.Core.World.Terrain.Field) return true;
            return false;
        }

        // ---------- fields ----------

        /// <summary>
        /// The field pattern: an irregular lattice, one boundary per edge of it.
        ///
        /// Regular spacing gives you allotments, not farmland. Three things break it up, and
        /// all three are visible from the air: the lines themselves are spaced unevenly, every
        /// junction wanders off where the spacing put it - so fields are quadrilaterals rather
        /// than rectangles - and roughly one boundary in five is simply missing, which is what
        /// gives a real parish its mix of small paddocks and long open fields instead of one
        /// field size repeated to the horizon.
        /// </summary>
        private static int Fields(WorldModel world, MeshChunks chunks)
        {
            var xs = Lattice(-Detail, world.Width + Detail, 11);
            var ys = Lattice(-Detail, world.Height + Detail, 17);

            int made = 0;

            for (int j = 0; j < ys.Count; j++)
            for (int i = 0; i < xs.Count; i++)
            {
                if (i + 1 < xs.Count && Chance(i, j, 211, 80))
                    made += Boundary(chunks, world,
                                     Node(xs, ys, i, j), Node(xs, ys, i + 1, j), i, j, 0);

                if (j + 1 < ys.Count && Chance(i, j, 223, 80))
                    made += Boundary(chunks, world,
                                     Node(xs, ys, i, j), Node(xs, ys, i, j + 1), i, j, 1);
            }

            return made;
        }

        /// <summary>
        /// The positions of one axis of lattice lines, at uneven spacings.
        ///
        /// The last span is absorbed into its neighbour rather than clamped, because a lattice
        /// that ends on a two-metre gap leaves a sliver of field along the whole outer rim that
        /// reads as a mistake rather than as a field.
        /// </summary>
        private static List<float> Lattice(float from, float to, int salt)
        {
            var lines = new List<float> { from };

            float at = from;
            for (int step = 0; ; step++)
            {
                at += 46f + Roll(step, salt, 401) * 34f;
                if (at > to - 24f) { lines.Add(to); break; }
                lines.Add(at);
            }

            return lines;
        }

        /// <summary>
        /// One junction of the lattice, displaced from where the spacing put it. The wander is
        /// kept well under the smallest spacing so two lines can never cross each other.
        /// </summary>
        private static Vector2 Node(List<float> xs, List<float> ys, int i, int j)
        {
            const float Wander = 9f;
            return new Vector2(xs[i] + (Roll(i, j, 307) - 0.5f) * 2f * Wander,
                               ys[j] + (Roll(i, j, 311) - 0.5f) * 2f * Wander);
        }

        /// <summary>
        /// One field boundary, walked from a to b in grid coordinates.
        ///
        /// Segments are clipped individually rather than the run being accepted or rejected as
        /// a whole, so a boundary that would cross the village simply stops at its edge and
        /// picks up again on the far side - which is what happens on the ground, the village
        /// being built over the fields that were there first.
        ///
        /// A boundary is a hedge or, occasionally, a drystone wall. Two kinds is enough for the
        /// patchwork to stop reading as one machine-made thing.
        /// </summary>
        private static int Boundary(MeshChunks chunks, WorldModel world,
                                    Vector2 a, Vector2 b, int ei, int ej, int axis)
        {
            float length = Vector2.Distance(a, b);
            if (length < 1f) return 0;

            var dir = (b - a) / length;
            var side = new Vector2(-dir.y, dir.x);
            var facing = Quaternion.LookRotation(new Vector3(dir.x, 0f, -dir.y), Vector3.up);

            bool drystone = Chance(ei, ej + axis * 977, 577, 15);
            int made = 0;

            int steps = Mathf.CeilToInt(length / HedgeSegment);
            for (int k = 0; k < steps; k++)
            {
                float t0 = k * HedgeSegment;
                float t1 = Mathf.Min(t0 + HedgeSegment, length);

                // A metre of lateral wander per segment. A boundary laid dead straight over
                // sixty metres is a fence panel; a hedge grew along a line somebody walked.
                var mid = a + dir * ((t0 + t1) * 0.5f)
                            + side * (Roll(ei * 31 + k, ej + axis, 569) - 0.5f);

                int hx = Mathf.RoundToInt(mid.x), hy = Mathf.RoundToInt(mid.y);
                if (OutsideBy(world, mid.x, mid.y) < Clearance) continue;

                // A gap, and a gateway in it. Every field has to be got into, and the pair of
                // posts is what says so.
                if (Chance(hx, hy, 509, 5))
                {
                    made += Gateway(chunks, world, a + dir * t0, a + dir * t1);
                    continue;
                }

                float run = t1 - t0 + 0.5f;   // overlap the neighbour so there is no daylight

                float ground = ElevationGrid.HeightAt(mid.x, mid.y);

                if (drystone)
                {
                    float tall = 1.05f + Roll(hx, hy, 587) * 0.25f;
                    made += Solid(chunks, PrimitiveType.Cube,
                                  new Vector3(mid.x, ground + tall * 0.5f, -mid.y),
                                  new Vector3(0.5f, tall, run), facing, Scenery.Stone);
                }
                else
                {
                    float tall = 1.35f + Roll(hx, hy, 521) * 0.85f;
                    float thick = 0.9f + Roll(hx, hy, 523) * 0.6f;
                    made += Solid(chunks, PrimitiveType.Cube,
                                  new Vector3(mid.x, ground + tall * 0.5f, -mid.y),
                                  new Vector3(thick, tall, run), facing, Scenery.Hedge);
                }

                // Standard trees left to grow out of the hedge line. This is the detail that
                // separates an English hedgerow from a garden hedge, and the line of them
                // across a field is most of what you actually see from a distance.
                if (Chance(hx, hy, 541, 9))
                    made += Tree(chunks, world, mid.x, mid.y, 3);
            }

            return made;
        }

        /// <summary>A pair of gateposts either side of a gap in a boundary.</summary>
        private static int Gateway(MeshChunks chunks, WorldModel world, Vector2 a, Vector2 b)
        {
            return Post(a) + Post(b);

            int Post(Vector2 at)
            {
                if (OutsideBy(world, at.x, at.y) < Clearance) return 0;
                return Solid(chunks, PrimitiveType.Cube,
                             new Vector3(at.x, ElevationGrid.HeightAt(at.x, at.y) + 0.65f, -at.y),
                             new Vector3(0.35f, 1.3f, 0.35f),
                             Quaternion.identity, Scenery.Stone);
            }
        }

        // ---------- trees ----------

        /// <summary>
        /// Copses and lone field trees, across the hedged land and the rougher ground past it.
        ///
        /// Scattered on a coarse cell grid with the cell centre displaced, rather than by
        /// walking every square metre: at this distance what matters is that the clumps are
        /// unevenly spaced, and a jittered cell gives that for a fraction of the tests.
        ///
        /// This reaches seventy metres further out than the fields do, which is what stops a
        /// bare green ring appearing between the last hedge and the first wood.
        /// </summary>
        private static int Copses(WorldModel world, MeshChunks chunks)
        {
            const float Cell = 22f;
            int cols = Mathf.CeilToInt((world.Width + Copsed * 2f) / Cell);
            int rows = Mathf.CeilToInt((world.Height + Copsed * 2f) / Cell);

            int made = 0;

            for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                float cx = -Copsed + (i + 0.5f) * Cell + (Roll(i, j, 811) - 0.5f) * Cell;
                float cy = -Copsed + (j + 0.5f) * Cell + (Roll(i, j, 821) - 0.5f) * Cell;

                // Trees get a wider berth than hedges do - a nine-metre canopy placed on the
                // boundary would hang over the village's own outermost gardens.
                if (OutsideBy(world, cx, cy) < 5f) continue;

                uint roll = Materials3D.Scatter(i, j, 809) % 100u;
                if (roll < 8) made += Copse(chunks, world, cx, cy, i, j);
                else if (roll < 12) made += Tree(chunks, world, cx, cy, 1);
            }

            return made;
        }

        /// <summary>
        /// A clump of trees in the corner of a field. Radius is square-rooted so the trees fill
        /// the clump evenly instead of crowding into a ring around its edge.
        /// </summary>
        private static int Copse(MeshChunks chunks, WorldModel world, float cx, float cy, int i, int j)
        {
            int trees = 4 + (int)(Materials3D.Scatter(i, j, 853) % 6u);
            float spread = 6f + Roll(i, j, 857) * 7f;

            int made = 0;
            for (int k = 0; k < trees; k++)
            {
                float angle = Roll(i * 31 + k, j, 859) * Mathf.PI * 2f;
                float radius = Mathf.Sqrt(Roll(i, j * 31 + k, 863)) * spread;

                float tx = cx + Mathf.Cos(angle) * radius;
                float ty = cy + Mathf.Sin(angle) * radius;
                if (OutsideBy(world, tx, ty) < 5f) continue;

                made += Tree(chunks, world, tx, ty, k);
            }
            return made;
        }

        /// <summary>
        /// One tree: a trunk and a canopy, the same two primitives the village uses, grown to
        /// field size. A hedgerow oak is half again the height of anything in a garden, and at
        /// this range that difference is the whole silhouette.
        /// </summary>
        /// <summary>
        /// Where a near-band tree WOULD have gone, for CityGreenery to plant a bought model on.
        ///
        /// Filled during Build and read straight afterwards. Safe because the order is fixed and
        /// one way: VillageMesh builds the countryside, and every caller runs CityGreenery after
        /// VillageMesh. Nothing here waits on anything.
        /// </summary>
        private static readonly List<Vector3> _handedOver = new List<Vector3>();

        public static IReadOnlyList<Vector3> NearTrees => _handedOver;

        private static int Tree(MeshChunks chunks, WorldModel world, float gx, float gy, int salt)
        {
            int hx = Mathf.RoundToInt(gx), hy = Mathf.RoundToInt(gy);
            float v = Roll(hx + salt, hy, 877);

            // CLOSE ENOUGH TO TELL THE DIFFERENCE? THEN IT IS NOT OURS.
            //
            // Everything inside the map is a bought model now, and a drawn tree beside a bought
            // one draws a line along the map edge - the same four green lobes repeating where a
            // moment ago there were oaks and birches. So the near band is handed over and only
            // the distance work is kept.
            //
            // The handover stops at Detail on purpose. Past it the canopy LOD has already taken
            // the trunks away and is building crowns from the coarsest shape whose outline still
            // holds; at that range a tree is a green mass through fog and a detailed model would
            // cost its full vertex count to render something nobody can resolve. The seam that
            // remains is between two kinds of blur, which is not a seam anybody sees.
            float outside = OutsideBy(world, gx, gy);
            float ground = ElevationGrid.HeightAt(gx, gy);
            if (outside <= Detail)
            {
                _handedOver.Add(new Vector3(gx, ground, -gy));
                return 0;
            }

            // Height and spread drawn separately, or the tallest tree in the county is always
            // also the widest one and a copse comes out as a neat little pyramid.
            float height = 6f + v * 5f;
            float canopy = 5f + Roll(hx + salt, hy, 883) * 4f;
            var at = new Vector3(gx, ground, -gy);

            // One of the four greens rather than the single Foliage colour. That palette exists
            // precisely because one green turns a copse into a solid mass with no separation
            // between trees - and it was being applied only to the forty trees inside the
            // village, while the four hundred out in the fields stayed uniform.
            int green = Scenery.CanopyFor(hx, hy, 887);

            // The trunk keeps its cylinder wherever it stands. It is 88 vertices against the
            // canopy's 515, so there is nothing in it worth the risk - and unlike the canopy a
            // trunk is a straight edge, where a facet count shows at once.
            return Solid(chunks, PrimitiveType.Cylinder,
                         at + Vector3.up * height * 0.35f,
                         new Vector3(0.34f, height * 0.35f, 0.34f),
                         Quaternion.identity, Scenery.Bark)
                 + Crown(chunks,
                           at + Vector3.up * height * 0.78f,
                           new Vector3(canopy, canopy * 0.82f, canopy),
                           OutsideBy(world, gx, gy), green);
        }

        /// <summary>
        /// The woods between the fields and the horizon, thickening with distance until they
        /// close into a treeline.
        ///
        /// Canopies only. Out here a trunk is well under a pixel and a third of it is behind
        /// fog, so paying two objects a tree would double the count of the largest group in
        /// this file to render something nobody can see. What the far distance needs is a
        /// broken green mass along the skyline instead of a ruler-straight join between ground
        /// and sky, and a mass is what a canopy already is.
        /// </summary>
        private static int Woods(WorldModel world, MeshChunks chunks)
        {
            const float Cell = 24f;
            int cols = Mathf.CeilToInt((world.Width + Horizon * 2f) / Cell);
            int rows = Mathf.CeilToInt((world.Height + Horizon * 2f) / Cell);

            int made = 0;

            for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                float cx = -Horizon + (i + 0.5f) * Cell + (Roll(i, j, 907) - 0.5f) * Cell * 0.9f;
                float cy = -Horizon + (j + 0.5f) * Cell + (Roll(i, j, 911) - 0.5f) * Cell * 0.9f;

                float far = OutsideBy(world, cx, cy);
                if (far <= Massed || far > Horizon) continue;   // nearer than this, trees have trunks

                float t = Mathf.InverseLerp(Massed, Horizon, far);
                if (!Chance(i, j, 919, Mathf.RoundToInt(Mathf.Lerp(14f, 72f, t)))) continue;

                float spread = Mathf.Lerp(9f, 21f, t) * (0.75f + Roll(i, j, 929) * 0.5f);
                float tall = spread * 0.72f;

                // Sunk a little into the ground rather than resting on it, so there is never a
                // strip of daylight under the treeline when the camera drops to eye level.
                made += Crown(chunks,
                                new Vector3(cx, ElevationGrid.HeightAt(cx, cy) + tall * 0.42f, -cy),
                                new Vector3(spread, tall, spread),
                                far, Scenery.CanopyFor(i, j, 937));
            }

            return made;
        }

        // ---------- plumbing ----------

        /// <summary>
        /// How far outside the map a point in grid coordinates lies, in metres. Zero means it
        /// is standing on the authored village, which is the one place nothing may go.
        /// </summary>
        private static float OutsideBy(WorldModel world, float gx, float gy)
        {
            float dx = Mathf.Max(Mathf.Max(-gx, gx - world.Width), 0f);
            float dy = Mathf.Max(Mathf.Max(-gy, gy - world.Height), 0f);
            return Mathf.Max(dx, dy);
        }

        /// <summary>A stable 0..1 from a position. See Materials3D.Scatter.</summary>
        private static float Roll(int x, int y, int salt) =>
            Materials3D.Scatter(x, y, salt) % 1024u / 1024f;

        private static bool Chance(int x, int y, int salt, int percent) =>
            Materials3D.Scatter(x, y, salt) % 100u < (uint)percent;

        /// <summary>
        /// One primitive, baked into the chunk mesh covering where it stands. Returns 1, so
        /// callers can total the object count as they build.
        ///
        /// Filed by its own position rather than by the cell that generated it, because the
        /// jitter that stops the countryside looking like a grid is worth up to half a cell and
        /// would otherwise put a tree in a chunk it is not standing in.
        ///
        /// Nothing out here was ever walked on or clicked - none of it had a collider - so the
        /// only thing lost by combining is its own name in the hierarchy.
        /// </summary>
        private static int Solid(MeshChunks chunks, PrimitiveType type,
                                 Vector3 position, Vector3 scale, Quaternion rotation, int submesh) =>
            Solid(chunks, Primitives.Of(type), position, scale, rotation, submesh);

        /// <summary>As above, for geometry that brings its own shape rather than naming one of
        /// Unity's. See <see cref="Crown"/>.</summary>
        private static int Solid(MeshChunks chunks, Primitives.Shape shape,
                                 Vector3 position, Vector3 scale, Quaternion rotation, int submesh)
        {
            chunks.At(position.x, -position.z)
                  .Add(shape, submesh, position, rotation, scale);
            return 1;
        }

        /// <summary>
        /// One canopy, built at whatever density its distance from the village deserves.
        ///
        /// The distance passed in is how far outside the map the tree stands, which is the
        /// closest a camera can get to it - the orbit camera's target is clamped to the map, so
        /// at street level the camera is standing on the village, and in overview it is further
        /// out but looking back at it. Canopy turns that into a shape; everything inside Massed
        /// gets Unity's own sphere and is untouched.
        ///
        /// Still never rotated, exactly as before. Every coarse canopy therefore carries its
        /// facets in the same world orientation, which sounds like it should show as a repeat
        /// along the treeline and does not: the vertex normals are the sphere's, so the SHADING
        /// is smooth whatever the facet count, and all that is left to line up is an outline
        /// wobble of under one percent of the radius. A per-tree rotation would cost a
        /// quaternion each to hide something already below a pixel.
        /// </summary>
        private static int Crown(MeshChunks chunks, Vector3 position, Vector3 scale,
                                 float distance, int submesh)
        {
            // Nothing inside Massed is touched, and the floor is deliberate rather than a
            // consequence of the arithmetic - the rule on its own would start trimming at about
            // forty metres out and still be within its own budget. Massed is where this file
            // already stops calling these things trees and starts calling them masses, and the
            // promise worth keeping is the simpler one: anything you can walk up to is exactly
            // the shape it always was. It costs about 22,000 vertices, which is a fifth of a
            // megabyte, and buys a wide shot that MUST come back byte-identical.
            var shape = distance < Massed
                      ? Primitives.Of(PrimitiveType.Sphere)
                      : Canopy.Of(scale.x, distance);
            int full = Primitives.Of(PrimitiveType.Sphere).Verts.Length;

            _canopies++;
            if (shape.Verts.Length < full) { _coarse++; _spared += full - shape.Verts.Length; }

            return Solid(chunks, shape, position, scale, Quaternion.identity, submesh);
        }
    }
}
