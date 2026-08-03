using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Pitched roofs, and the cutaway that hides them.
    ///
    /// A village of flat-topped boxes reads as a diagram; a village of pitched roofs reads as a
    /// village. It is the single biggest difference between "some geometry" and "a place",
    /// and it costs one mesh per chunk.
    ///
    /// THE CUTAWAY: roofs are on when the camera is up high and off when you come down close.
    /// Far away you want to see a village; up close you want to see the people in it. Tying it
    /// to camera distance means you never have to think about a toggle - it does the obvious
    /// thing as you move. The camera owns the threshold, in OrbitCamera.WantsRoofs, because it
    /// also owns the other half of the rule: street mode forces roofs on whatever the distance.
    ///
    /// Roofs are combined per chunk rather than into one mesh for the whole map, so the ones
    /// behind you can be culled - see MeshChunks. A whole roof and its chimneys go into the
    /// chunk of the building's north-west corner, eaves and all: splitting a hip roof at a
    /// chunk boundary would cut it across the slope, and the two halves would need their own
    /// ridge vertices and their own recalculated normals, which is a visible crease down the
    /// middle of somebody's house in exchange for nothing. The overhang past the boundary is
    /// 35 centimetres of eaves and a chimney stack, and it only widens the chunk's bounding box
    /// by that much.
    ///
    /// The whole set still switches together, which is predictable and cheap; per-building
    /// fading is a refinement for later.
    /// </summary>
    public sealed class RoofBuilder : MonoBehaviour
    {
        private MeshRenderer[] _renderers;
        private OrbitCamera _camera;
        private bool _shown = true;

        private const float Pitch = 2.2f;      // ridge height above the eaves, in metres
        private const float Overhang = 0.35f;  // eaves project past the walls

        public static RoofBuilder Build(WorldModel world, Transform parent)
        {
            var go = new GameObject("Roofs");
            go.transform.SetParent(parent, false);

            var coverings = Materials3D.Roofs;
            var chunks = new MeshChunks(coverings.Length, MeshChunks.Size,
                                        0, 0, world.Width, world.Height);
            int count = 0;

            foreach (var place in world.AllPlaces)
            {
                if (!IsRoofed(place.Kind)) continue;

                // A bought model brings its own roof. Without this every apartment and every
                // landmark had a generated one hanging inside it.
                if (CityBuildings.Handles(place)) continue;

                var into = chunks.At(place.Bounds.X, place.Bounds.Y);

                // The covering is still chosen from the building's own corner, so which roof a
                // house has is a property of where it stands and not of how the map happens to
                // be divided up. Chunking must not be visible in the picture at all.
                // ON THE GROUND IT STANDS ON. The walls already follow the terrain (VillageMesh
                // samples ElevationGrid for every wall segment) but the roof did not, so on a map
                // with real elevation the two came apart. Rossville's dwellings sit on ground
                // running from -3 m to +9 m, which buried the highest houses to above the ridge.
                // It never showed before because the only town with contour is Rossville and its
                // houses were bought models, which are seated through Space3D.ToWorld.
                var massing = MassingGrammars.Of(place).Lifted(Space3D.GroundUnder(place.Bounds));
                AddRoof(place.Bounds, massing, into,
                        Materials3D.RoofingFor(place.Bounds.X, place.Bounds.Y));

                // A chimney per home. On a terrace that means one per unit, which is exactly
                // what tells you from the street how many families live in the building -
                // and it is the detail that makes a roofline read as inhabited rather than
                // as a shape.
                if (massing.Chimneys)
                {
                    int stacks = place.Kind == PlaceKind.Dwelling ? place.Units : 1;
                    AddChimneys(place.Bounds, stacks, massing, into, Materials3D.ChimneyIndex);
                }

                // Whatever this kind and no other gets: a tower, a bell-cote, a hoist.
                MassingGrammars.For(place).Extras(place, into);

                count++;
            }

            if (count == 0) return null;

            Unweld(chunks);

            var renderers = chunks.Emit(go.transform, "Roofs", coverings,
                                        ShadowCastingMode.On, true);

            var builder = go.AddComponent<RoofBuilder>();
            builder._renderers = renderers.ToArray();

            Debug.Log($"Roofs: {count} buildings, {chunks.VertexCount:N0} vertices, "
                    + $"{renderers.Count} chunk meshes, {chunks.DrawCalls} draw calls.");
            return builder;
        }

        /// <summary>
        /// While X-ray is on, the camera does not get a vote on the roofs.
        ///
        /// Without this the two disagree and the cutaway wins, because it reasserts itself every
        /// time the camera crosses its threshold: you turn the buildings off, orbit two degrees,
        /// and the roofs come back on their own.
        /// </summary>
        public void Suppress(bool suppressed)
        {
            _suppressed = suppressed;

            // Force the next LateUpdate to rewrite rather than early-out on an unchanged value.
            // On release the camera may well want the opposite of whatever X-ray left behind.
            if (!suppressed) _shown = !_shown;
        }

        private bool _suppressed;

        private void LateUpdate()
        {
            if (_suppressed) return;
            if (_renderers == null) return;
            if (_camera == null) _camera = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            if (_camera == null) return;

            // The camera decides: roofs on from a distance and at street level, off when you
            // drop in close from above to watch what people are doing indoors.
            //
            // Only on a CHANGE. It was one renderer and one assignment a frame; a town is
            // dozens of chunks, and writing renderer.enabled dirties Unity's culling state
            // whether or not the value differs.
            bool wanted = _camera.WantsRoofs;
            if (wanted == _shown) return;

            _shown = wanted;
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = wanted;
        }

        /// <summary>
        /// Whether this kind has a roof at all - the same question as which have walls, so SunRig
        /// asks this rather than keeping its own list of the buildings that deserve windows. Two
        /// hand-written lists is one too many, which is the argument that ends in the table.
        ///
        /// This WAS a switch listing the five or six open-ground kinds by hand, with a
        /// `default: return true` underneath. Two things were wrong with that. It had already
        /// drifted out of step with VillageLayout.IsBuilding once, and left a 1.7 m pyramid with
        /// a chimney on it hanging three metres above bare grass beside Ashcombe Street. And the
        /// `default` meant any kind authored purely in content got a roof whether or not its row
        /// said `roof no` - so the open-kind property Stage 4 bought was quietly broken for
        /// anything that was not a building.
        ///
        /// The column has existed and been authored since Stage 4. It was simply never read.
        /// </summary>
        public static bool IsRoofed(PlaceKind kind) =>
            PlaceKindTable.Current.Row(kind).Roofed;

        /// <summary>Chimney stacks, spaced along the ridge - one per home in the building.</summary>
        private static void AddChimneys(TileRect bounds, int stacks, Massing m, MeshChunk into,
                                        int submesh)
        {
            if (stacks < 1) stacks = 1;

            bool ridgeAlongX = m.RidgeAcross ? bounds.H > bounds.W : bounds.W >= bounds.H;
            float ridgeY = m.Eaves + m.Pitch;

            // Along the RIDGE, not along the building.
            //
            // A hip roof's ridge is inset from each end by half the building's depth, so it is
            // shorter than the building is long. Spreading the stacks over (length - 2) put
            // the outer ones out on the hip slope, where the roof has already dropped away
            // beneath them - and their height is pinned to the ridge, so they hung in the air.
            // On Ash Terrace the end stacks floated 46 cm clear with their shadows landing on
            // the slope detached from the base; on Mill Buildings, 58 cm. The middle two of
            // each four were correct, which is exactly what makes it read as a texture fault.
            float inset = (ridgeAlongX ? bounds.H : bounds.W) * 0.5f + Overhang;
            float along = (ridgeAlongX ? bounds.W : bounds.H) + Overhang * 2f;
            float from = inset, to = along - inset;
            if (to < from) from = to = along * 0.5f;   // square building: the ridge is a point

            for (int i = 0; i < stacks; i++)
            {
                float t = (i + 0.5f) / stacks;
                float at = Mathf.Lerp(from, to, t) - Overhang;

                float cx = ridgeAlongX ? bounds.X + at : bounds.X + bounds.W * 0.5f;
                float cz = ridgeAlongX
                    ? -(bounds.Y + bounds.H * 0.5f)
                    : -(bounds.Y + at);

                AddBox(into, submesh,
                       new Vector3(cx, ridgeY + 0.55f, cz),
                       new Vector3(0.85f, 1.5f, 0.85f));
            }
        }

        /// <summary>
        /// Give every triangle its own three vertices, in every chunk.
        ///
        /// A hip roof is six vertices carrying eight triangles, and RecalculateNormals averages
        /// the faces meeting at each one - it does not split by angle. So the two slopes and
        /// the two hips blended into each other and the ridge, the one line that says "this is
        /// a roof and not a mound", was shaded away completely. Both slopes are mirror images,
        /// so they converged on exactly the same value where they met. Every roof in the
        /// village came out as a soft inflated pillow, next to walls that were correctly flat -
        /// and the geometry was right the whole time.
        ///
        /// Chimneys had it worse: eight vertices, three faces each, normals along the box
        /// diagonals, so a brick stack was shaded like a sphere.
        ///
        /// Unwelding costs about five hundred extra vertices across the village. Being done per
        /// chunk changes nothing about the result - no roof shares a vertex with another roof,
        /// so nothing that was welded together can be pulled apart by the split.
        /// </summary>
        private static void Unweld(MeshChunks chunks)
        {
            foreach (var chunk in chunks.All)
            {
                var verts = chunk.Verts;
                var uvs = chunk.Uvs;

                var splitVerts = new List<Vector3>(verts.Count * 2);
                var splitUvs = new List<Vector2>(verts.Count * 2);

                foreach (var submesh in chunk.Tris)
                    for (int i = 0; i < submesh.Count; i++)
                    {
                        splitVerts.Add(verts[submesh[i]]);
                        splitUvs.Add(uvs[submesh[i]]);
                        submesh[i] = splitVerts.Count - 1;
                    }

                verts.Clear(); verts.AddRange(splitVerts);
                uvs.Clear(); uvs.AddRange(splitUvs);
            }
        }

        /// <summary>An axis-aligned box, appended to its chunk of the roof mesh.</summary>
        private static void AddBox(MeshChunk into, int submesh, Vector3 centre, Vector3 size)
        {
            var verts = into.Verts;
            var uvs = into.Uvs;
            var tris = into.Tris[submesh];

            var h = size * 0.5f;
            int b = verts.Count;

            verts.Add(centre + new Vector3(-h.x, -h.y, -h.z));   // 0
            verts.Add(centre + new Vector3(h.x, -h.y, -h.z));    // 1
            verts.Add(centre + new Vector3(h.x, -h.y, h.z));     // 2
            verts.Add(centre + new Vector3(-h.x, -h.y, h.z));    // 3
            verts.Add(centre + new Vector3(-h.x, h.y, -h.z));    // 4
            verts.Add(centre + new Vector3(h.x, h.y, -h.z));     // 5
            verts.Add(centre + new Vector3(h.x, h.y, h.z));      // 6
            verts.Add(centre + new Vector3(-h.x, h.y, h.z));     // 7

            // A chimney is small enough that planar XZ mapping on all six faces is fine; the
            // sides get a vertical smear of about a metre, which nothing will ever notice.
            for (int i = b; i < verts.Count; i++)
                uvs.Add(new Vector2(verts[i].x, -verts[i].z));

            // Wound the other way round from how this was written. Unity takes a face from
            // Cross(v1-v0, v2-v0), and every quad here was inside-out: the flat cap on top of
            // each stack was culled, so you saw the inside of the far wall instead and the
            // chimney had no top to it, and the shadow caster pass - which culls back faces
            // too - dropped or mangled the stack's shadow on the roof slope beneath it.
            void Quad(int i, int j, int k, int l)
            {
                tris.Add(b + i); tris.Add(b + k); tris.Add(b + j);
                tris.Add(b + i); tris.Add(b + l); tris.Add(b + k);
            }

            Quad(4, 5, 6, 7);   // top
            Quad(0, 3, 2, 1);   // bottom
            Quad(0, 1, 5, 4);   // north
            Quad(2, 3, 7, 6);   // south
            Quad(1, 2, 6, 5);   // east
            Quad(3, 0, 4, 7);   // west
        }

        /// <summary>
        /// A hip roof: two trapezoid slopes meeting at a ridge, closed at each end by a
        /// triangle. The ridge runs along the building's longer axis, which is what a real
        /// roof does and what stops a row of cottages looking like it was extruded.
        /// </summary>
        /// <summary>
        /// The roof, in whichever form this kind of building wears.
        ///
        /// NOTE WHAT THIS SWITCHES ON. RoofForm is a CLOSED set of four shapes; PlaceKind is an
        /// OPEN set that a content row can extend. Switching on the shape is fine and switching
        /// on the kind would put PlaceKind back into geometry code and make a new amenity cost
        /// C# again, which is exactly what the kind table was built to stop. The mapping from
        /// kind to shape lives in the massing grammars, one indirection away.
        /// </summary>
        private static void AddRoof(TileRect bounds, Massing m, MeshChunk into, int submesh)
        {
            switch (m.Roof)
            {
                case RoofForm.Flat:   AddFlatRoof(bounds, m, into, submesh); break;
                case RoofForm.LeanTo: AddLeanToRoof(bounds, m, into, submesh); break;
                case RoofForm.Gable:  AddPitchedRoof(bounds, m, into, submesh, gable: true); break;
                default:              AddPitchedRoof(bounds, m, into, submesh, gable: false); break;
            }
        }

        /// <summary>
        /// A hip or a gable roof: two slopes meeting at a ridge, closed at each end.
        ///
        /// The two forms are the SAME SIX VERTICES and the same triangle list, differing only in
        /// how far the ridge is inset from the ends. A hip insets it by half the building's
        /// depth, which tips the end triangles over into sloping hips; a gable runs it out to the
        /// building's own ends, which stands those same triangles up vertical and turns them into
        /// gable walls. Writing gable as a separate mesh would have been two functions that must
        /// agree about winding and UVs forever.
        /// </summary>
        private static void AddPitchedRoof(TileRect bounds, Massing m, MeshChunk into, int submesh,
                                           bool gable)
        {
            var verts = into.Verts;
            var uvs = into.Uvs;
            var tris = into.Tris[submesh];

            float y = m.Eaves;

            float x0 = bounds.X - Overhang;
            float x1 = bounds.X + bounds.W + Overhang;
            float z0 = -(bounds.Y - Overhang);
            float z1 = -(bounds.Y + bounds.H + Overhang);

            float w = x1 - x0;
            float d = z0 - z1;              // z1 is more negative, so this is positive
            float ridgeY = y + m.Pitch;

            // The ridge follows the long axis unless the grammar asks for the other one.
            bool alongX = m.RidgeAcross ? d > w : w >= d;

            int baseIndex = verts.Count;

            var a = new Vector3(x0, y, z0);
            var b = new Vector3(x1, y, z0);
            var c = new Vector3(x1, y, z1);
            var e = new Vector3(x0, y, z1);

            Vector3 r0, r1;
            if (alongX)
            {
                float zc = (z0 + z1) * 0.5f;
                float inset = gable ? 0f : d * 0.5f;
                r0 = new Vector3(x0 + inset, ridgeY, zc);
                r1 = new Vector3(x1 - inset, ridgeY, zc);
            }
            else
            {
                float xc = (x0 + x1) * 0.5f;
                float inset = gable ? 0f : w * 0.5f;
                r0 = new Vector3(xc, ridgeY, z0 - inset);
                r1 = new Vector3(xc, ridgeY, z1 + inset);
            }

            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(e);   // 0..3 eaves
            verts.Add(r0); verts.Add(r1);                             // 4,5 ridge

            // UVs in metres of ground covered, so tiles run at a consistent size across every
            // roof in the village whatever the building's size. The slope stretches them by
            // about a tenth, which is less than the variation in a real roof.
            for (int i = baseIndex; i < verts.Count; i++)
                uvs.Add(new Vector2(verts[i].x, -verts[i].z));

            void Tri(int i, int j, int k)
            {
                tris.Add(baseIndex + i); tris.Add(baseIndex + j); tris.Add(baseIndex + k);
            }

            // A HIP end is roof and gets tiled. A GABLE end is WALL - it is the masonry of the
            // building carried up to the ridge - and tiling it puts roof tiles on a vertical
            // surface. That was the single worst thing in the church's first elevation: a big
            // red triangular slab where its west wall should be, which read as a marquee rather
            // than as a building.
            //
            // The walling is now carried in the roof material array too, so a gable end is drawn
            // in the same stuff as the wall below it rather than in chimney brick.
            var endTris = gable ? into.Tris[Materials3D.WallIndex] : tris;

            void End(int i, int j, int k)
            {
                endTris.Add(baseIndex + i); endTris.Add(baseIndex + j); endTris.Add(baseIndex + k);
            }

            if (alongX)
            {
                // ridge runs east-west
                Tri(0, 5, 4); Tri(0, 1, 5);   // front slope  a-b-r1-r0
                Tri(2, 4, 5); Tri(2, 3, 4);   // back slope   c-e-r0-r1
                End(3, 0, 4);                 // west end - a hip, or a gable wall
                End(1, 2, 5);                 // east end
            }
            else
            {
                // ridge runs north-south
                Tri(0, 4, 3); Tri(3, 4, 5);   // west slope
                Tri(1, 2, 5); Tri(1, 5, 4);   // east slope
                End(0, 1, 4);                 // north end
                End(2, 3, 5);                 // south end
            }
        }

        /// <summary>
        /// One slope, low at the front and high at the back. A workshop or an outbuilding.
        /// </summary>
        private static void AddLeanToRoof(TileRect bounds, Massing m, MeshChunk into, int submesh)
        {
            var verts = into.Verts;
            var uvs = into.Uvs;
            var tris = into.Tris[submesh];

            float lo = m.Eaves, hi = m.Eaves + m.Pitch;

            float x0 = bounds.X - Overhang;
            float x1 = bounds.X + bounds.W + Overhang;
            float z0 = -(bounds.Y - Overhang);
            float z1 = -(bounds.Y + bounds.H + Overhang);

            int baseIndex = verts.Count;

            // Rises towards the back (more negative z), so the tall side faces away from the road.
            verts.Add(new Vector3(x0, lo, z0));
            verts.Add(new Vector3(x1, lo, z0));
            verts.Add(new Vector3(x1, hi, z1));
            verts.Add(new Vector3(x0, hi, z1));

            for (int i = baseIndex; i < verts.Count; i++)
                uvs.Add(new Vector2(verts[i].x, -verts[i].z));

            // Wound so cross(v1-v0, v2-v0) points UP. The first version of this had it the other
            // way and the roof was backface-culled from every camera above it - which looks
            // exactly like no roof at all, and is the same fault the ground had once.
            tris.Add(baseIndex + 0); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
            tris.Add(baseIndex + 0); tris.Add(baseIndex + 2); tris.Add(baseIndex + 3);
        }

        /// <summary>
        /// No slope at all. A garage reads as a garage partly because it does not have a roof
        /// like a house's - a flat top over a wide opening is the whole silhouette.
        /// </summary>
        private static void AddFlatRoof(TileRect bounds, Massing m, MeshChunk into, int submesh)
        {
            var verts = into.Verts;
            var uvs = into.Uvs;
            var tris = into.Tris[submesh];

            // Clear of the wall caps rather than level with them.
            //
            // A pitched roof rises away from the eaves, so it never shares a plane with the top
            // of a wall. A flat one does, and coplanar surfaces let the depth buffer pick a
            // winner per pixel per frame - which showed up as the wall tops flickering through
            // the roof in slivers. Twenty centimetres also happens to be about what a parapet
            // stands anyway.
            float y = m.Eaves + 0.2f;

            float x0 = bounds.X - Overhang;
            float x1 = bounds.X + bounds.W + Overhang;
            float z0 = -(bounds.Y - Overhang);
            float z1 = -(bounds.Y + bounds.H + Overhang);

            int baseIndex = verts.Count;

            verts.Add(new Vector3(x0, y, z0));
            verts.Add(new Vector3(x1, y, z0));
            verts.Add(new Vector3(x1, y, z1));
            verts.Add(new Vector3(x0, y, z1));

            for (int i = baseIndex; i < verts.Count; i++)
                uvs.Add(new Vector2(verts[i].x, -verts[i].z));

            // Upward, for the same reason as the flat roof above.
            tris.Add(baseIndex + 0); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
            tris.Add(baseIndex + 0); tris.Add(baseIndex + 2); tris.Add(baseIndex + 3);
        }
    }
}
