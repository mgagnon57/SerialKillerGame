using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.World;
using Terrain = Noir.Core.World.Terrain;

namespace Noir.Unity
{
    /// <summary>
    /// The CSX Woodland Subdivision, at grade, on the ground.
    ///
    /// WHAT WAS WRONG. Content/features.txt carries the actual surveyed right-of-way, and getting
    /// that alignment right is what found a quarter of the town's houses on the wrong side of the
    /// real track. CityOutlines draws it correctly - as a painted ribbon with tie ticks, in the
    /// SURVEY PLAN, which VillageHost only builds when ShowBuildings is false. Turn the buildings
    /// on for actual gameplay, as the project is plainly heading towards, and the one feature a
    /// Rossville person would recognise on sight simply was not there. Nothing painted a rail bed
    /// anywhere the dressed game could show it.
    ///
    /// NOT CityRail. That is the ELEVATED urban "El" - a viaduct, a station and a two-car train -
    /// gated on a `place railway` that city.txt deliberately does not have. This is a single-track
    /// branch line through a farm town, which is a different object entirely.
    ///
    /// GENERATED, NOT BOUGHT. The pack's rail kit (`Modular Parts/Rails`) is a ground-level TRAM
    /// kit, six pieces, city-styled - wrong era and wrong country, the same objection that keeps
    /// the brownstones off the residential streets. Ballast, ties and rail are simple enough to
    /// generate and then they follow the real curve exactly instead of being chopped into 1/3/5/10m
    /// modules that cannot.
    ///
    /// IT READS THE SAME CURVE THE PLAN DOES, through MapFeatures - same parse, same Catmull-Rom.
    /// Two readers of the alignment would eventually disagree, and the railroad is the last thing
    /// in this town that should be in two places at once.
    /// </summary>
    public static class CityRailBed
    {
        /// <summary>Standard gauge, rail head to rail head. Not a number to adjust to taste.</summary>
        private const float Gauge = 1.435f;

        private const float BallastTop = 4.4f;     // width of the level crown
        private const float BallastFoot = 6.2f;    // width where the shoulders meet the ground
        private const float BallastHeight = 0.34f;

        private const float TieLength = 2.6f;
        private const float TieWidth = 0.26f;      // along the track
        private const float TieHeight = 0.18f;
        private const float TiePitch = 0.65f;

        private const float RailWidth = 0.08f;
        private const float RailHeight = 0.16f;

        /// <summary>How often the curve is sampled. The ballast and rails are drawn between
        /// consecutive samples, so this is also how closely the bed follows the real ground -
        /// two metres against an elevation grid that resolves at thirty is comfortably finer
        /// than the data it is following.</summary>
        private const float Step = 2f;

        private const int Ballast = 0, Timber = 1, Steel = 2;

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var go = new GameObject("CityRailBed");
            go.transform.SetParent(parent, false);

            var palette = new[] { Materials3D.Ballast, Materials3D.Sleeper, Materials3D.RailSteel };

            // THE LINE DOES NOT STOP AT THE MAP EDGE AND SHOULD NOT. Chicago Street runs the full
            // height of the map because Hoopeston is six miles up it; the CSX line is the same
            // kind of thing and features.txt carries it out past both corners - to y=2674 on a
            // 2400 map. Declaring the chunk grid over the map alone made MeshChunks clamp the
            // overhang into the outermost chunks, which still draws but files two kilometres of
            // track under a chunk it is not in, and that chunk can then never be culled. The grid
            // is declared over the geometry that actually exists instead.
            int minX = 0, minY = 0, maxX = world.Width, maxY = world.Height;
            foreach (var feature in MapFeatures.All())
            {
                if (feature.Kind != "rail") continue;
                foreach (var p in feature.Points)
                {
                    minX = Mathf.Min(minX, Mathf.FloorToInt(p.x - BallastFoot));
                    minY = Mathf.Min(minY, Mathf.FloorToInt(p.y - BallastFoot));
                    maxX = Mathf.Max(maxX, Mathf.CeilToInt(p.x + BallastFoot));
                    maxY = Mathf.Max(maxY, Mathf.CeilToInt(p.y + BallastFoot));
                }
            }

            var chunks = new MeshChunks(palette.Length, MeshChunks.Size, minX, minY, maxX, maxY);

            int lines = 0, ties = 0, crossings = 0;
            float metres = 0f;

            foreach (var feature in MapFeatures.All())
            {
                if (feature.Kind != "rail") continue;
                var pts = MapFeatures.Smoothed(feature.Points);

                // Resample the smoothed curve at a fixed pitch, so a long straight and a tight
                // bend are built to the same resolution and the tie spacing stays even through
                // both. Walking the raw vertices instead would put four ties in a curve and one
                // every eighty metres on the tangent.
                var path = Resample(pts, Step);
                if (path.Count < 2) continue;

                lines++;
                float sinceTie = 0f;

                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector2 a = path[i], b = path[i + 1];
                    Vector2 along = b - a;
                    float len = along.magnitude;
                    if (len < 0.001f) continue;
                    metres += len;

                    along /= len;
                    // Map space is x east, y south; the plan's own right-hand side is (-dy, dx),
                    // the same derivation LaneGraph uses for which side of a road to drive on.
                    var side = new Vector2(-along.y, along.x);

                    // A LEVEL CROSSING IS WHERE THE ROAD ALREADY IS. Rather than reading the four
                    // `crossing` entries and matching them to positions - which would be a second
                    // opinion about where a road is - the bed simply asks the map what it is
                    // standing on. Ballast and ties stop; the rails carry on flush, which is what
                    // a grade crossing looks like from a car. The four surveyed OSM crossings are
                    // exactly the places this fires, because they are where the real roads are.
                    bool onRoad = IsRoad(world, a) || IsRoad(world, b);
                    if (onRoad) crossings++;

                    var into = chunks.At(a.x, a.y);
                    float crown = onRoad ? 0.04f : BallastHeight;

                    if (!onRoad)
                    {
                        // Crown, then a shoulder down each side. What makes it read as a raised
                        // bed rather than a grey stripe painted on the field is that the
                        // shoulders catch the light at a different angle from the top.
                        //
                        // EVERY CROSS-SECTION RUNS LOW OFFSET TO HIGH, and that is not style. The
                        // face's normal works out as (0, offsetDelta, heightDelta) for a walk
                        // a->b, so an offset delta that runs the other way faces the quad at the
                        // ground and URP culls it - correct geometry, correct material, correct
                        // normals, invisible. The first cut of this had all three strips wound
                        // that way AND passed the same offset twice for the crown, which made it
                        // a zero-area quad Quad() drops on the floor. The whole bed rendered as
                        // two thin dark lines and read as a scratch on the field.
                        Strip(into, Ballast, a, b, side, -BallastTop * 0.5f, crown,
                              BallastTop * 0.5f, crown);
                        Strip(into, Ballast, a, b, side, BallastTop * 0.5f, crown,
                              BallastFoot * 0.5f, 0f);
                        Strip(into, Ballast, a, b, side, -BallastFoot * 0.5f, 0f,
                              -BallastTop * 0.5f, crown);
                    }

                    // Ties, laid at a fixed pitch along the walk rather than one per segment, so
                    // the spacing is a property of the track and not of how the curve was sampled.
                    if (!onRoad)
                    {
                        for (float t = sinceTie; t < len; t += TiePitch)
                        {
                            var at = a + along * t;
                            Tie(into, at, along, side, crown);
                            ties++;
                        }
                        sinceTie = (sinceTie - len) % TiePitch;
                        if (sinceTie < 0f) sinceTie += TiePitch;
                    }
                    else sinceTie = 0f;

                    float railBase = crown + (onRoad ? 0f : TieHeight);
                    Rail(into, a, b, side, +Gauge * 0.5f, railBase);
                    Rail(into, a, b, side, -Gauge * 0.5f, railBase);
                }
            }

            var renderers = chunks.Emit(go.transform, "RailBed", palette,
                                        ShadowCastingMode.On, true);

            Debug.Log($"[railbed] {lines} rail lines, {metres:N0}m of track, {ties:N0} ties, "
                    + $"{crossings} sampled segments at grade crossings, "
                    + $"{chunks.VertexCount:N0} vertices in {renderers.Count} chunks, "
                    + $"{chunks.DrawCalls} draw calls.");
            return go;
        }

        /// <summary>Half-width of the cleared right of way, in metres. Wider than the ballast
        /// foot: a railroad keeps the trees back off its own drainage, and a canopy overhanging
        /// the running line is the thing a section gang goes out to cut.</summary>
        private const float Clearance = 6.5f;

        private static bool[] _rightOfWay;
        private static int _rowWidth, _rowHeight;

        /// <summary>
        /// Whether a tile is inside the railroad's cleared right of way.
        ///
        /// It is derived from the SAME resampled polyline the bed is built from, not from a
        /// second copy of the alignment - which is the whole reason it lives here rather than in
        /// CityGreenery. The first render of the bed had trees standing between the rails,
        /// because scatter is decided in Core from terrain alone and the corridor is not terrain.
        /// </summary>
        public static bool OnRightOfWay(WorldModel world, int gx, int gy)
        {
            if (_rightOfWay == null || _rowWidth != world.Width || _rowHeight != world.Height)
                BuildRightOfWay(world);

            if (gx < 0 || gy < 0 || gx >= _rowWidth || gy >= _rowHeight) return false;
            return _rightOfWay[gy * _rowWidth + gx];
        }

        private static void BuildRightOfWay(WorldModel world)
        {
            _rowWidth = world.Width;
            _rowHeight = world.Height;
            _rightOfWay = new bool[_rowWidth * _rowHeight];

            int reach = Mathf.CeilToInt(Clearance);
            foreach (var feature in MapFeatures.All())
            {
                if (feature.Kind != "rail") continue;
                foreach (var p in Resample(MapFeatures.Smoothed(feature.Points), 1f))
                {
                    int cx = Mathf.FloorToInt(p.x), cy = Mathf.FloorToInt(p.y);
                    for (int dy = -reach; dy <= reach; dy++)
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= _rowWidth || y >= _rowHeight) continue;
                        float ox = x + 0.5f - p.x, oy = y + 0.5f - p.y;
                        if (ox * ox + oy * oy <= Clearance * Clearance)
                            _rightOfWay[y * _rowWidth + x] = true;
                    }
                }
            }
        }

        /// <summary>Points along the polyline at a fixed spacing, keeping the first and last.</summary>
        private static List<Vector2> Resample(List<Vector2> pts, float step)
        {
            var outv = new List<Vector2>();
            if (pts.Count == 0) return outv;

            outv.Add(pts[0]);
            float carry = 0f;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = Vector2.Distance(a, b);
                if (len < 0.0001f) continue;

                float t = step - carry;
                while (t < len)
                {
                    outv.Add(Vector2.Lerp(a, b, t / len));
                    t += step;
                }
                carry = (carry + len) % step;
            }

            if (outv[outv.Count - 1] != pts[pts.Count - 1]) outv.Add(pts[pts.Count - 1]);
            return outv;
        }

        private static bool IsRoad(WorldModel world, Vector2 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            if (x < 0 || y < 0 || x >= world.Width || y >= world.Height) return false;
            var t = world.Grid.TerrainAt(x, y);
            return t == Terrain.Road || t == Terrain.Path;
        }

        /// <summary>
        /// One quad running from a to b, between two offsets across the track. Both ends of the
        /// cross-section carry their own height, which is what lets the same call draw a level
        /// crown and a sloping shoulder.
        /// </summary>
        private static void Strip(MeshChunk into, int submesh, Vector2 a, Vector2 b, Vector2 side,
                                  float off0, float h0, float off1, float h1)
        {
            var p0 = Point(a, side, off0, h0);
            var p1 = Point(b, side, off0, h0);
            var p2 = Point(b, side, off1, h1);
            var p3 = Point(a, side, off1, h1);
            Quad(into, submesh, p0, p1, p2, p3);
        }

        /// <summary>One rail: a running top face and the two faces down its sides, which is
        /// enough for something 8cm wide seen from a metre and a half up.</summary>
        private static void Rail(MeshChunk into, Vector2 a, Vector2 b, Vector2 side,
                                 float centre, float baseY)
        {
            float o0 = centre - RailWidth * 0.5f, o1 = centre + RailWidth * 0.5f;
            float top = baseY + RailHeight;

            // Running face, then the two flanks. A vertical face wound base->top along the walk
            // faces -side; wound top->base it faces +side. So the outer flank (the larger
            // offset) takes the second form and the inner flank the first, or each one is
            // culled from precisely the side you look at it from.
            Quad(into, Steel, Point(a, side, o0, top), Point(b, side, o0, top),
                              Point(b, side, o1, top), Point(a, side, o1, top));
            Quad(into, Steel, Point(a, side, o1, top), Point(b, side, o1, top),
                              Point(b, side, o1, baseY), Point(a, side, o1, baseY));
            Quad(into, Steel, Point(a, side, o0, baseY), Point(b, side, o0, baseY),
                              Point(b, side, o0, top), Point(a, side, o0, top));
        }

        /// <summary>One sleeper, across the bed: the top, and the two faces you see looking along
        /// the track. Its ends are left off - they are 26cm of end grain buried in ballast.</summary>
        private static void Tie(MeshChunk into, Vector2 at, Vector2 along, Vector2 side, float crown)
        {
            Vector2 a = at - along * (TieWidth * 0.5f);
            Vector2 b = at + along * (TieWidth * 0.5f);
            float top = crown + TieHeight;
            float half = TieLength * 0.5f;

            Quad(into, Timber, Point(a, side, -half, top), Point(b, side, -half, top),
                               Point(b, side, half, top), Point(a, side, half, top));

            // The far face looks along the track, the near face back down it. Wound the other
            // way round they are culled from the one direction anybody sees a sleeper's face.
            Quad(into, Timber, Point(b, side, -half, crown), Point(b, side, half, crown),
                               Point(b, side, half, top), Point(b, side, -half, top));
            Quad(into, Timber, Point(a, side, half, crown), Point(a, side, -half, crown),
                               Point(a, side, -half, top), Point(a, side, half, top));
        }

        /// <summary>
        /// A cross-section point in world space. The height goes through Space3D, so the whole
        /// bed rides the real ground - the alternative, one height for the line, would have the
        /// track hanging in the air at one end of a two-kilometre run and buried at the other.
        /// </summary>
        private static Vector3 Point(Vector2 at, Vector2 side, float across, float up)
        {
            var p = at + side * across;
            return Space3D.ToWorld(new Core.Contracts.Vec2(p.x, p.y), up);
        }

        /// <summary>
        /// Wound so the face looks the way its own geometry says it should, then given a normal
        /// derived from that same winding rather than an assumed "up" - a ballast shoulder and
        /// the inner face of a rail are not up, and lighting them as though they were is what
        /// makes generated geometry read as flat cardboard.
        /// </summary>
        private static void Quad(MeshChunk into, int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();

            int v = into.Verts.Count;
            into.Verts.Add(a); into.Verts.Add(b); into.Verts.Add(c); into.Verts.Add(d);
            for (int i = 0; i < 4; i++) into.Normals.Add(n);

            // World-metre UVs, as the ground uses, so the ballast texture runs along the track
            // instead of restarting at every two-metre sample.
            into.Uvs.Add(new Vector2(a.x, -a.z));
            into.Uvs.Add(new Vector2(b.x, -b.z));
            into.Uvs.Add(new Vector2(c.x, -c.z));
            into.Uvs.Add(new Vector2(d.x, -d.z));

            var tris = into.Tris[submesh];
            tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
            tris.Add(v); tris.Add(v + 2); tris.Add(v + 3);
        }
    }
}
