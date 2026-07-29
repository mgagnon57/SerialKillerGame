using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// The bespoke bits: a tower, a spire, a bell-cote, a hoist.
    ///
    /// All of it is boxes and pyramids, emitted into the same chunked mesh as the roofs, so it
    /// inherits the existing chunking and frustum culling and adds no renderers of its own.
    ///
    /// THESE ARE EXTERIOR ONLY. The church tower does not claim interior floor space - the
    /// interior grammar has already laid rooms across the whole footprint, and a tower that
    /// carved a hole in the nave would mean rebuilding interiors from the outside in. Recorded
    /// in the spec as a known simplification rather than discovered later by somebody walking
    /// into a tower that is not there.
    /// </summary>
    public static class MassingExtras
    {
        /// <summary>Where a tower ended up, so the spire can sit on it.</summary>
        public readonly struct TowerTop
        {
            public readonly Vector3 Centre;
            public readonly float Side;
            public TowerTop(Vector3 centre, float side) { Centre = centre; Side = side; }
        }

        /// <summary>
        /// A square tower at the end of the building FURTHEST FROM THE FRONT DOOR.
        ///
        /// That rule is the whole difference between a church and a shed with a lump on it. A
        /// tower in the middle of the roof reads as a mistake; a tower at the far end reads as a
        /// west tower with the nave running away from it, which is the shape of every parish
        /// church in England.
        /// </summary>
        public static TowerTop Tower(Place place, Massing m, MeshChunk into, int submesh)
        {
            var b = place.Bounds;
            float side = Mathf.Min(b.W, b.H) * 0.55f;
            float height = m.Eaves + m.Pitch + 4.0f;

            // The four edge midpoints, in grid space.
            var candidates = new[]
            {
                new Vector2(b.X + b.W * 0.5f, b.Y + 0.5f),              // north edge
                new Vector2(b.X + b.W * 0.5f, b.Y + b.H - 0.5f),        // south edge
                new Vector2(b.X + 0.5f, b.Y + b.H * 0.5f),              // west edge
                new Vector2(b.X + b.W - 0.5f, b.Y + b.H * 0.5f),        // east edge
            };

            // No door recorded is not a failure - open ground has none and some buildings are
            // entered from a yard. Fall back to the north edge, deterministically.
            var door = place.Door.IsValid
                ? new Vector2(place.Door.X + 0.5f, place.Door.Y + 0.5f)
                : candidates[1];

            int best = 0;
            float bestDistance = -1f;
            for (int i = 0; i < candidates.Length; i++)
            {
                float distance = (candidates[i] - door).sqrMagnitude;
                if (distance > bestDistance) { bestDistance = distance; best = i; }
            }

            // Pulled in by half its own side so the tower sits ON the building rather than
            // straddling its wall.
            var at = candidates[best];
            at.x = Mathf.Clamp(at.x, b.X + side * 0.5f, b.X + b.W - side * 0.5f);
            at.y = Mathf.Clamp(at.y, b.Y + side * 0.5f, b.Y + b.H - side * 0.5f);

            var centre = new Vector3(at.x, height * 0.5f, -at.y);
            Box(into, centre, new Vector3(side, height, side), submesh);

            return new TowerTop(new Vector3(at.x, height, -at.y), side);
        }

        /// <summary>The spire on top of the tower. Steep, because a shallow one reads as a hat.</summary>
        public static void Spire(TowerTop tower, MeshChunk into, int submesh) =>
            Pyramid(into, tower.Centre, tower.Side, tower.Side * 2.2f, submesh);

        /// <summary>
        /// A bell over the gable. Small - the point is the outline against the sky, and anything
        /// bigger stops being a village school and starts being a chapel.
        /// </summary>
        public static void BellCote(Place place, Massing m, MeshChunk into, int submesh)
        {
            var b = place.Bounds;
            bool alongX = m.RidgeAcross ? b.H > b.W : b.W >= b.H;
            float ridgeY = m.Eaves + m.Pitch;

            // A third of the way along from the end furthest from the door.
            var door = place.Door.IsValid ? new Vector2(place.Door.X, place.Door.Y)
                                          : new Vector2(b.X, b.Y);

            float x, y;
            if (alongX)
            {
                bool doorWest = door.x < b.X + b.W * 0.5f;
                x = doorWest ? b.X + b.W * 0.75f : b.X + b.W * 0.25f;
                y = b.Y + b.H * 0.5f;
            }
            else
            {
                bool doorNorth = door.y < b.Y + b.H * 0.5f;
                x = b.X + b.W * 0.5f;
                y = doorNorth ? b.Y + b.H * 0.75f : b.Y + b.H * 0.25f;
            }

            Box(into, new Vector3(x, ridgeY + 0.7f, -y), new Vector3(0.9f, 1.4f, 0.5f), submesh);
        }

        /// <summary>
        /// The hoist housing over a mill's loading door - the projecting box near the ridge that
        /// the sack hoist runs out of. On a real mill it is the one feature you can name from a
        /// field away.
        /// </summary>
        public static void Lucam(Place place, Massing m, MeshChunk into, int submesh)
        {
            var b = place.Bounds;
            bool alongX = m.RidgeAcross ? b.H > b.W : b.W >= b.H;

            float x = b.X + b.W * 0.5f;
            float y = b.Y + b.H * 0.5f;
            float top = m.Eaves + m.Pitch * 0.5f;

            // Projecting from the long wall, which is the one a cart can pull up to.
            if (alongX) y = b.Y + 0.4f;
            else        x = b.X + 0.4f;

            var size = alongX ? new Vector3(2.0f, 1.6f, 1.4f) : new Vector3(1.4f, 1.6f, 2.0f);
            Box(into, new Vector3(x, top, -y), size, submesh);
        }

        // ---- primitives ----

        /// <summary>A box, centred on <paramref name="centre"/>. Six quads, no shared vertices.</summary>
        private static void Box(MeshChunk into, Vector3 centre, Vector3 size, int submesh)
        {
            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;

            var ppp = centre + new Vector3(+hx, +hy, +hz);
            var ppn = centre + new Vector3(+hx, +hy, -hz);
            var pnp = centre + new Vector3(+hx, -hy, +hz);
            var pnn = centre + new Vector3(+hx, -hy, -hz);
            var npp = centre + new Vector3(-hx, +hy, +hz);
            var npn = centre + new Vector3(-hx, +hy, -hz);
            var nnp = centre + new Vector3(-hx, -hy, +hz);
            var nnn = centre + new Vector3(-hx, -hy, -hz);

            Quad(into, submesh, npp, ppp, pnp, nnp);   // +z
            Quad(into, submesh, ppn, npn, nnn, pnn);   // -z
            Quad(into, submesh, ppp, ppn, pnn, pnp);   // +x
            Quad(into, submesh, npn, npp, nnp, nnn);   // -x
            Quad(into, submesh, npn, ppn, ppp, npp);   // +y
            Quad(into, submesh, nnp, pnp, pnn, nnn);   // -y
        }

        /// <summary>A four-sided pyramid standing on <paramref name="baseCentre"/>.</summary>
        private static void Pyramid(MeshChunk into, Vector3 baseCentre, float width, float height,
                                    int submesh)
        {
            float h = width * 0.5f;

            var a = baseCentre + new Vector3(-h, 0f, +h);
            var b = baseCentre + new Vector3(+h, 0f, +h);
            var c = baseCentre + new Vector3(+h, 0f, -h);
            var d = baseCentre + new Vector3(-h, 0f, -h);
            var apex = baseCentre + new Vector3(0f, height, 0f);

            Tri(into, submesh, a, b, apex);
            Tri(into, submesh, b, c, apex);
            Tri(into, submesh, c, d, apex);
            Tri(into, submesh, d, a, apex);
        }

        /// <summary>
        /// Own vertices per face, matching how the walls are built: shared corners would force
        /// one UV wrapping across two perpendicular faces and crease the texture at every edge.
        /// </summary>
        private static void Quad(MeshChunk into, int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var verts = into.Verts;
            var uvs = into.Uvs;
            var tris = into.Tris[submesh];

            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);

            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(0f, 0f));

            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        private static void Tri(MeshChunk into, int submesh, Vector3 a, Vector3 b, Vector3 c)
        {
            var verts = into.Verts;
            var uvs = into.Uvs;
            var tris = into.Tris[submesh];

            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(0.5f, 1f));

            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }
    }
}
