using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
// CLAUDE.md names this exact collision: Terrain is one of the twelve names UnityEngine also uses.
using Terrain = Noir.Core.World.Terrain;

namespace Noir.Unity
{
    /// <summary>
    /// The tarmac where roads meet, GENERATED rather than tiled — and it is the same argument
    /// `CityRailBed` makes about the pack's tram kit, for the same reason.
    ///
    /// WHAT WAS WRONG. Every junction in Rossville was paved with `Road_Turn_10x10_City`: a
    /// downtown block corner carrying a ten-metre slab of sidewalk paving on the inside of the
    /// bend. Measured on the live town, 2026-08-10: **all 120 junctions have a reach under 8 m,
    /// so all 120 got that one piece.** 105 of them are two-arm and 15 are tees; not one is a
    /// four-way crossroads. So the town had a hundred and twenty city-block corners with plazas
    /// on them, in a farm town where those corners are grass and gravel.
    ///
    /// The pack has nothing else at that size — only 20x20 and 30x30 city pieces, and two farm
    /// dirt turns more than twice the width of the junctions they would sit in. Owner's ruling
    /// 2026-08-10, taken on those numbers: generate them.
    ///
    /// IT FOLLOWS THE SURVEY, WHICH A TILE CANNOT. A module has to be seated at a yaw, and
    /// `CityStreets` carried a written warning that the turn piece's built-in orientation "is not
    /// written down anywhere in the pack, and a corner laid at the wrong yaw is invisible in any
    /// total — it is only ever wrong to look at." This reads the junction's own arm TANGENTS, so
    /// there is no yaw to get wrong and no compass involved: a road leaving at 22 degrees gets
    /// tarmac at 22 degrees.
    ///
    /// THE CORNERS COME OUT ROUNDED, AND THAT IS THE STANDING RULING. The apron is the convex
    /// hull of the arm mouths, so where two roads meet at an angle the tarmac cuts the corner
    /// rather than reaching into it — which is what a turning radius is. `CLAUDE.md`: *"a real
    /// street corner has a turning radius and a car cannot pivot on a point."*
    /// </summary>
    public static class CityJunction
    {
        /// <summary>
        /// Just above the road tiles' own asphalt plane.
        ///
        /// A pack road tile is two levels: pavement at local 0 and asphalt at local -0.1. The
        /// apron has to land ON the carriageway, not on the kerb, so it sits at the asphalt's
        /// height plus a centimetre — enough that the two never z-fight where a tile overlaps a
        /// junction, little enough that nobody can see a step.
        /// </summary>
        private const float Lift = -0.09f;

        /// <summary>How many metres of asphalt one repeat of the texture covers. The pack's road
        /// tiles are 10 m square, so this keeps the grain the same size across the join.</summary>
        private const float TextureMetres = 10f;

        /// <summary>
        /// One mesh for every junction in the town. Combined rather than one object each, because
        /// a hundred and twenty separate twenty-triangle meshes is a hundred and twenty draw calls
        /// for less geometry than one house — and `CityChunker` would only have to merge them
        /// again afterwards.
        /// </summary>
        /// <param name="skip">Junction indices `CityStreets` has decided to pave straight over:
        /// the straight-through nodes, and any node with no arms at all. Passing them here rather
        /// than re-deriving the rule keeps one answer to "is this a junction to look at".</param>
        public static GameObject Build(WorldModel world, Transform parent, HashSet<int> skip)
        {
            if (world?.Roads == null) return null;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int made = 0, degenerate = 0;

            for (int ji = 0; ji < world.Roads.Junctions.Count; ji++)
            {
                if (skip != null && skip.Contains(ji)) continue;

                var j = world.Roads.Junctions[ji];
                if (j.Arms == null || j.Arms.Length == 0) continue;

                var mouths = Mouths(j);
                if (mouths.Count < 3) { degenerate++; continue; }

                var hull = Hull(mouths);
                if (hull.Count < 3) { degenerate++; continue; }

                Fan(hull, parent, verts, uvs, tris);
                made++;
            }

            if (made == 0) return null;

            var mesh = new Mesh { name = "JunctionAprons" };
            mesh.indexFormat = verts.Count > 60000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            // The pack albedos are nearly flat and all the detail is in the normal map, which URP
            // builds from a tangent stream. Without this the asphalt reads as a colour rather than
            // as a surface - the same line MeshChunks.Emit needs for the same reason.
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var go = new GameObject("CityJunctions");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Asphalt();

            Debug.Log($"[junctions] {made} aprons generated from their own arm tangents, "
                    + $"{verts.Count} vertices, {tris.Count / 3} triangles"
                    + (degenerate > 0 ? $"; {degenerate} node(s) had too few mouths to enclose "
                                      + "anything and were left to the carriageway walk" : "")
                    + $". Material '{mr.sharedMaterial?.name ?? "none"}'.");
            return go;
        }

        /// <summary>The corners of every road mouth around a junction, in village coordinates.</summary>
        private static List<Vector2> Mouths(Junction j)
        {
            var points = new List<Vector2>();
            var centre = new Vector2(j.X, j.Y);
            float reach = j.Reach;

            foreach (var arm in j.Arms)
            {
                var road = arm.Road;
                if (road?.Path == null) continue;

                // HALF THE ASPHALT, NOT HALF THE CORRIDOR. A corridor is the right of way and
                // includes the verge; the apron has to meet the carriageway the tiles actually
                // draw, which is what CityStreets.Asphalt measures off the tile.
                float w = CityStreets.Asphalt(road.Class);
                var t = new Vector2(arm.Tangent.X, arm.Tangent.Y);
                if (t.sqrMagnitude < 1e-6f) continue;
                t.Normalize();

                // A road that continues on both sides of the node contributes TWO mouths, which
                // is why this asks the arc length rather than counting arms: `Junction.Arms` has
                // one entry per ROAD, and a road passing straight through leaves in two
                // directions. Exactly the test CityStreets uses to count its N/S/E/W departures.
                if (arm.S > reach) Mouth(points, centre, -t, w, reach);
                if (arm.S < road.Path.Length - reach) Mouth(points, centre, t, w, reach);
            }
            return points;
        }

        private static void Mouth(List<Vector2> into, Vector2 centre, Vector2 dir, float w, float reach)
        {
            var out_ = centre + dir * reach;
            var side = new Vector2(-dir.y, dir.x) * w;
            into.Add(out_ + side);
            into.Add(out_ - side);
        }

        /// <summary>
        /// Convex hull, monotone chain. No trigonometry and no sorting by angle — this file is in
        /// the Unity layer rather than Core so it is not bound by Core's ban on transcendentals,
        /// but a cross product is the honest way to ask which side of a line a point is on and
        /// costs nothing.
        /// </summary>
        private static List<Vector2> Hull(List<Vector2> pts)
        {
            pts.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    int k = pass == 0 ? i : pts.Count - 1 - i;
                    while (hull.Count >= start + 2
                           && Cross(hull[hull.Count - 2], hull[hull.Count - 1], pts[k]) <= 0f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(pts[k]);
                }
                hull.RemoveAt(hull.Count - 1);      // the last point starts the other pass
            }
            return hull;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
            (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>
        /// Triangulate a convex ring as a fan, in Unity space, following the ground.
        ///
        /// WOUND TO FACE UPWARDS, and this is not a detail anybody may skip: `CityCollision`'s
        /// ground mesh was once wound to face DOWNWARDS, and the player fell through the back of
        /// the floor "intermittently" for weeks, because PhysX stops a CharacterController on a
        /// backface only sometimes. Village y runs into Unity -z, which reverses handedness, so a
        /// ring that is counter-clockwise on the map is clockwise here.
        /// </summary>
        private static void Fan(List<Vector2> ring, Transform parent,
                                List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            int first = verts.Count;
            foreach (var p in ring)
            {
                verts.Add(new Vector3(p.x, Lift + ElevationGrid.HeightAt(p.x, p.y), -p.y));
                uvs.Add(new Vector2(p.x / TextureMetres, p.y / TextureMetres));
            }

            // WHICH WAY ROUND IS MEASURED, NOT REASONED. The first version of this fan reasoned
            // it out - hull is counter-clockwise, village y maps to Unity -z, a reflection
            // reverses orientation, therefore emit the fan backwards - and got it wrong: every
            // apron in the town came out facing DOWNWARDS and read as a black glob in the middle
            // of the intersection. Which is the failure this file's own docstring warns about,
            // two paragraphs up, citing CityCollision's ground doing exactly the same thing.
            //
            // So ask the geometry instead. Twice the signed area of the ring in the XZ plane is
            // positive for one winding and negative for the other; the sign that yields an
            // upward normal is fixed by Unity's left-handed frame and is verifiable here rather
            // than in a render twelve minutes away. No reflection to keep track of, and it stays
            // right if anybody ever changes the hull's direction.
            float twiceArea = 0f;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = verts[first + i];
                var b = verts[first + (i + 1) % ring.Count];
                twiceArea += a.x * b.z - b.x * a.z;
            }
            bool facesUp = twiceArea < 0f;

            for (int i = 1; i + 1 < ring.Count; i++)
            {
                tris.Add(first);
                tris.Add(first + (facesUp ? i : i + 1));
                tris.Add(first + (facesUp ? i + 1 : i));
            }
        }

        private static Material _asphalt;

        /// <summary>
        /// The road tiles' own asphalt, so the apron and the carriageway are the same surface.
        ///
        /// Falls back to `Materials3D.ForTerrain(Terrain.Road)` — which is the same sheet at the
        /// same tiling — rather than returning null or a magenta default, and SAYS WHICH IT USED
        /// in the `[junctions]` line. `Assets/polyperfect` is gitignored, so a fresh clone has no
        /// pack and every one of these lookups comes back empty.
        /// </summary>
        private static Material Asphalt()
        {
            if (_asphalt != null) return _asphalt;
#if UNITY_EDITOR
            _asphalt = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/polyperfect/Poly Universal Pack/Materials/City/M_Asphalt_A_City.mat");
#endif
            return _asphalt != null ? _asphalt : (_asphalt = Materials3D.ForTerrain(Terrain.Road));
        }
    }
}
