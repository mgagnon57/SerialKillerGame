using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// Tree canopies built at the density the distance deserves.
    ///
    /// A canopy in this project is one of Unity's built-in spheres: 515 vertices and 768
    /// triangles, which is the right shape for a tree you can stand under and an absurd one
    /// for a green mass a quarter of a mile away behind fog. The countryside was spending
    /// 399,000 of its 440,000 vertices on canopy spheres, and better than two thirds of them
    /// are out past the last hedge where a whole tree is twenty pixels across.
    ///
    /// What may NOT change is the OUTLINE. The treeline is the one thing standing between the
    /// village and a ruler-straight join of ground and sky, and a canopy that reads as a
    /// polyhedron on the skyline is worse than an expensive one. So the level is not chosen
    /// by eye: it is the cheapest shape whose outline is within half a pixel of a true circle
    /// from the closest place a camera can legally stand, and everything nearer than that
    /// keeps Unity's own sphere untouched.
    ///
    /// GEODESIC rather than a coarser UV sphere. A UV sphere spends its vertices where they
    /// do least good - crowded into the two poles, thin around the equator where the outline
    /// actually is - so for the same worst-case outline it costs roughly three times as many.
    /// It also needs a seam of duplicated vertices to carry a texture across, and a canopy
    /// carries no texture at all (see Materials3D.Canopies), so there is nothing to pay that
    /// for.
    ///
    /// The level is a pure function of where the canopy stands and how big it is, and both of
    /// those are already properties of the position - see Materials3D.Scatter. It is settled
    /// once, when the chunk mesh is built, and never looked at again. A level that read the
    /// camera would make the geometry a function of the frame, and the twelve offline
    /// snapshots this project compares byte for byte would stop being worth comparing.
    /// </summary>
    public static class Canopy
    {
        /// <summary>
        /// How far the outline may sit from a true circle, as an angle at the eye.
        ///
        /// The snapshots are 900 pixels over a 45 degree field, so one pixel is a twentieth of
        /// a degree and this is half of one. Kept as an ANGLE rather than as pixels because
        /// the game window is not 1600x900 and a number in pixels would quietly come to mean
        /// something else the moment somebody resized it.
        /// </summary>
        private static readonly float Budget = 0.025f * Mathf.Deg2Rad;

        /// <summary>
        /// The ladder of geodesic frequencies, coarsest first. Frequency n cuts every edge of
        /// an icosahedron into n, giving 10n^2 + 2 vertices and 20n^2 triangles: 42, 92, 162,
        /// 252. Nothing finer is offered - by then a geodesic is within sight of the built-in
        /// sphere's own vertex count, and a canopy that needs that many is one you are close
        /// enough to walk into, where the honest answer is the sphere it always was.
        ///
        /// Ashcombe's countryside uses only 92 and 162 today - the ends of the ladder are there
        /// because this is a rule and not a two-value table, and the map is about to become a
        /// town three times the size, at which point both ends will start being asked for.
        /// </summary>
        private const int Coarsest = 2, Finest = 5;

        private static readonly Dictionary<int, Primitives.Shape> _shapes =
            new Dictionary<int, Primitives.Shape>();

        /// <summary>Worst outline error of each frequency, as a fraction of the radius, after
        /// the centring in <see cref="Geodesic"/>. Measured off the shape, never written down.</summary>
        private static readonly Dictionary<int, float> _errors = new Dictionary<int, float>();

        /// <summary>
        /// The shape to build a canopy of this diameter from, standing this far from the
        /// nearest place a camera can be.
        ///
        /// The distance passed in by Countryside is how far outside the map the canopy stands,
        /// which is the closest a camera can get to it: the orbit camera's target is clamped
        /// to the map (OrbitCamera.HandleWalk - "walking off the edge into fog is not
        /// atmosphere, it is a bug"), so at street level the camera is standing on the map. In
        /// overview it swings further out than that, but it is always looking back AT the map,
        /// so the country on its own side of the village is behind it.
        /// </summary>
        public static Primitives.Shape Of(float diameter, float distance)
        {
            for (int frequency = Coarsest; frequency <= Finest; frequency++)
            {
                var shape = Geodesic(frequency);
                // Multiplied out rather than divided, so a canopy standing ON the map edge
                // asks for an impossible outline and falls through to the built-in sphere
                // instead of dividing by zero.
                if (_errors[frequency] * diameter * 0.5f <= Budget * distance) return shape;
            }
            return Primitives.Of(PrimitiveType.Sphere);
        }

        /// <summary>
        /// A geodesic sphere of the given frequency, a unit across like Unity's own, so a
        /// caller's scale still means diameter.
        ///
        /// Two things here are worth more than they look.
        ///
        /// The vertices are PUSHED OUT past the sphere by half their own worst error. A hull
        /// of points sitting exactly on a sphere is inscribed in it, so every canopy comes out
        /// systematically SMALLER than the one it replaces - at the frequencies used here by up
        /// to about one and a half percent, which on a single tree is nothing and across the
        /// whole width of a treeline is the horizon band quietly thinning. Pushed out by half
        /// the error the outline is sometimes a hair wide and sometimes a hair narrow and on
        /// average exactly right, and the worst case is halved into the bargain.
        ///
        /// The WINDING is derived from the geometry rather than written down. Unity takes a
        /// triangle's facing from Cross(v1-v0, v2-v0) and culls the back, and for a convex
        /// solid built around the origin the way a face faces is simply the way its own
        /// centroid points - so the test is exact and there is no table of twenty triples to
        /// get subtly wrong. Ground quads in this project were once all wound face-down: lit
        /// correctly, shadowed correctly, and invisible, which read as "the textures are too
        /// dark" for a fortnight.
        /// </summary>
        private static Primitives.Shape Geodesic(int frequency)
        {
            Primitives.Shape existing;
            if (_shapes.TryGetValue(frequency, out existing)) return existing;

            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var corners = new[]
            {
                new Vector3(-1f, t, 0f), new Vector3(1f, t, 0f),
                new Vector3(-1f, -t, 0f), new Vector3(1f, -t, 0f),
                new Vector3(0f, -1f, t), new Vector3(0f, 1f, t),
                new Vector3(0f, -1f, -t), new Vector3(0f, 1f, -t),
                new Vector3(t, 0f, -1f), new Vector3(t, 0f, 1f),
                new Vector3(-t, 0f, -1f), new Vector3(-t, 0f, 1f),
            };
            for (int i = 0; i < corners.Length; i++) corners[i] = corners[i].normalized;

            var faces = new[]
            {
                new[] { 0, 11, 5 },  new[] { 0, 5, 1 },   new[] { 0, 1, 7 },
                new[] { 0, 7, 10 },  new[] { 0, 10, 11 }, new[] { 1, 5, 9 },
                new[] { 5, 11, 4 },  new[] { 11, 10, 2 }, new[] { 10, 7, 6 },
                new[] { 7, 1, 8 },   new[] { 3, 9, 4 },   new[] { 3, 4, 2 },
                new[] { 3, 2, 6 },   new[] { 3, 6, 8 },   new[] { 3, 8, 9 },
                new[] { 4, 9, 5 },   new[] { 2, 4, 11 },  new[] { 6, 2, 10 },
                new[] { 8, 6, 7 },   new[] { 9, 8, 1 },
            };

            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Points shared between two faces are welded by an EXACT key rather than by
            // comparing positions: a point of the subdivision is a whole-number blend of the
            // icosahedron's own corners, so the blend itself names it, and the two faces
            // meeting along an edge name the points on that edge identically. Welding on
            // floating-point positions instead would leave the odd seam vertex doubled
            // whenever two routes to the same point disagreed in the last bit.
            var shared = new Dictionary<long, int>();

            foreach (var face in faces)
            {
                // Row i is i steps from corner face[0]; within it, j steps from face[1]
                // towards face[2]. So the three integer weights always sum to the frequency.
                var grid = new int[frequency + 1][];
                for (int i = 0; i <= frequency; i++)
                {
                    grid[i] = new int[i + 1];
                    for (int j = 0; j <= i; j++)
                        grid[i][j] = Weld(shared, verts, corners, face, frequency - i, i - j, j);
                }

                for (int i = 0; i < frequency; i++)
                for (int j = 0; j <= i; j++)
                {
                    Triangle(tris, verts, grid[i][j], grid[i + 1][j], grid[i + 1][j + 1]);
                    if (j < i) Triangle(tris, verts, grid[i][j], grid[i + 1][j + 1], grid[i][j + 1]);
                }
            }

            // The worst the outline can be, taken off the faces. For a hull of points on a
            // sphere the outline seen from any direction has radius max(dot(vertex, sideways)),
            // so the worst case over EVERY camera is one minus the cosine of the largest gap
            // between the vertices - and the widest gap in a triangle is at its circumcentre,
            // which for triangles this near equilateral is a whisker from the centroid.
            float error = 0f;
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                var middle = (verts[tris[i]] + verts[tris[i + 1]] + verts[tris[i + 2]]).normalized;
                for (int k = 0; k < 3; k++)
                    error = Mathf.Max(error, 1f - Vector3.Dot(middle, verts[tris[i + k]]));
            }

            var shape = new Primitives.Shape
            {
                Verts = new Vector3[verts.Count],
                Normals = new Vector3[verts.Count],
                // No UVs. The four canopy materials carry no texture, so there is nothing for
                // them to address, and a spherical mapping would need a seam of duplicated
                // vertices which is most of what this class exists to avoid. If a canopy ever
                // DOES get a texture, this is where it will come out as one flat texel.
                Uvs = new Vector2[verts.Count],
                Tris = tris.ToArray(),
            };

            float radius = 1f / (2f - error);   // half a unit, pushed out to centre the error
            for (int i = 0; i < verts.Count; i++)
            {
                shape.Normals[i] = verts[i];
                shape.Verts[i] = verts[i] * radius;
            }

            _errors[frequency] = error * 0.5f;
            return _shapes[frequency] = shape;
        }

        /// <summary>
        /// One point of the subdivision, made once and then shared. The key is the blend that
        /// names it, sorted so that a point on an edge is named the same by both faces.
        /// </summary>
        private static int Weld(Dictionary<long, int> shared, List<Vector3> verts,
                                Vector3[] corners, int[] face, int wa, int wb, int wc)
        {
            // Only the corners this point actually leans on, in ascending order. A point along
            // an edge leans on that edge's two corners and on nothing else, which is precisely
            // why the two faces meeting there name it identically. Carrying the third corner
            // with a weight of zero would name it once per face and every seam would come out
            // doubled - which still draws correctly and quietly costs back much of what this
            // class is for.
            int count = 0;
            var used = new int[3];
            var weight = new int[3];
            if (wa > 0) { used[count] = face[0]; weight[count] = wa; count++; }
            if (wb > 0) { used[count] = face[1]; weight[count] = wb; count++; }
            if (wc > 0) { used[count] = face[2]; weight[count] = wc; count++; }
            for (int a = 0; a < count; a++)
            for (int b = a + 1; b < count; b++)
                if (used[b] < used[a])
                {
                    int c = used[a]; used[a] = used[b]; used[b] = c;
                    int w = weight[a]; weight[a] = weight[b]; weight[b] = w;
                }

            // Every packed pair is at least one, so a two-corner key can never collide with a
            // three-corner one.
            long key = 0L;
            for (int a = 0; a < count; a++) key = key * 4096L + used[a] * 64L + weight[a];

            int at;
            if (shared.TryGetValue(key, out at)) return at;

            var point = corners[face[0]] * wa + corners[face[1]] * wb + corners[face[2]] * wc;
            verts.Add(point.normalized);
            return shared[key] = verts.Count - 1;
        }

        /// <summary>One triangle, wound so that Unity's Cross(v1-v0, v2-v0) points outward.</summary>
        private static void Triangle(List<int> tris, List<Vector3> verts, int i0, int i1, int i2)
        {
            var a = verts[i0];
            var facing = Vector3.Cross(verts[i1] - a, verts[i2] - a);
            if (Vector3.Dot(facing, a + verts[i1] + verts[i2]) < 0f)
            {
                int swap = i1; i1 = i2; i2 = swap;
            }
            tris.Add(i0); tris.Add(i1); tris.Add(i2);
        }
    }
}
