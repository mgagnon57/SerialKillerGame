using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// The houses of a small Illinois town, in the three layers the record actually shows.
    ///
    /// WHERE THESE NUMBERS COME FROM. Not taste. `docs/research/ROSSVILLE-HISTORY.md` §7a, off
    /// nine Sanborn fire-insurance sheets (1898, 1906, 1913) plus modern housing statistics:
    ///
    ///   - the Sanborn colour key is unambiguous - yellow is frame, red is brick - and on a
    ///     purely residential sheet there is NOT ONE BRICK HOUSE. Every building is yellow,
    ///     marked "D" for dwelling, annotated 1, 1.5 or 2 storeys, overwhelmingly 1 and 1.5,
    ///     each with a small rear outbuilding, on large and mostly empty lots.
    ///   - the town's median build year is 1943 and 54% predates 1950, so the biggest single
    ///     cohort is the 1920s-40s - a layer that POSTDATES every Sanborn map and is therefore
    ///     invisible in the primary sources.
    ///   - 82% detached single-family. There is essentially no attached housing.
    ///
    /// So: three grammars, all frame, none brick. Nothing here should ever produce masonry.
    ///
    /// THE PORCH IS THE POINT. Every one of these has a front porch and the existing grammars
    /// have none, because they were written for an English village where a house meets the
    /// pavement at its own wall. An American frame house of any of these three eras has a roofed
    /// porch across some or all of its front, and at the distance this game is usually looked
    /// at, the porch and the roof pitch are the whole of what tells the three apart.
    /// </summary>
    internal static class FrameHouse
    {
        /// <summary>
        /// A porch: a slab floor, a shallow roof over it, and posts holding the roof up.
        ///
        /// Built as extras geometry rather than as part of the massing, for the same reason the
        /// church tower is - it hangs off the front of the box rather than changing the box.
        /// Emitted into the roof mesh so it inherits the chunking and costs no renderer.
        ///
        /// `depth` is how far it projects, `fraction` how much of the frontage it covers: a
        /// foursquare and a bungalow carry the porch the full width, a farmhouse usually does not.
        /// </summary>
        /// <remarks>
        /// THERE WAS NEVER ANYTHING WRONG WITH THIS FUNCTION, and the note that used to sit here
        /// saying otherwise is worth remembering. It recorded a "known defect — draws SOLID
        /// rather than as an open porch", listed five things ruled out by careful reading, and
        /// concluded the fault was "in how it draws, not where".
        ///
        /// The fault was in the camera. HouseProto laid its prototype at tile (5,5) with the door
        /// on the +y wall, so the porch projects to +y — which is -z in world space — and the
        /// camera was parked at +z aimed at (w/2, -h/2), the opposite corner. The porch was
        /// behind the house in every prototype ever shot. The solid lump visible at the front,
        /// diagnosed at length as a broken porch, was the back ell doing its job.
        ///
        /// Every one of those five exonerations was correct. The reasoning was sound and the
        /// conclusion was wrong, because all of it assumed the thing in the picture was the thing
        /// being debugged. What settled it was logging the actual emitted centres and sizes and
        /// comparing them against where the camera was standing — thirty seconds of arithmetic
        /// against a day of reading geometry that was never broken.
        ///
        /// Ruled out by reading, all still true: the width fraction works; the grammars resolve;
        /// Extras is called from RoofBuilder after the roof and chimneys; the winding matches
        /// MassingExtras.Quad; post spacing and the outer-edge offset are arithmetically right.
        /// </remarks>
        internal static void Porch(Place place, Massing m, MeshChunk into, int submesh,
                                   float depth, float fraction, float postHeight)
        {
            var b = place.Bounds;

            // WHICH WAY THE HOUSE FACES. The door is authored, so ask it rather than guessing:
            // whichever edge of the footprint the door is nearest is the front.
            float dx = place.Door.X - (b.X + b.W * 0.5f);
            float dy = place.Door.Y - (b.Y + b.H * 0.5f);
            bool frontOnY = Mathf.Abs(dy) >= Mathf.Abs(dx);
            float sign = frontOnY ? Mathf.Sign(dy) : Mathf.Sign(dx);
            if (sign == 0f) sign = 1f;

            float span = (frontOnY ? b.W : b.H) * Mathf.Clamp01(fraction);
            float cx = b.X + b.W * 0.5f;
            float cy = b.Y + b.H * 0.5f;

            // Sit the porch just outside the wall it fronts.
            if (frontOnY) cy = (sign > 0f ? b.Y + b.H : b.Y) + sign * depth * 0.5f;
            else          cx = (sign > 0f ? b.X + b.W : b.X) + sign * depth * 0.5f;


            // Everything below stands on the ground the house stands on, not on y=0.
            float ground = Space3D.GroundUnder(b);

            var floorSize = frontOnY ? new Vector3(span, 0.25f, depth)
                                     : new Vector3(depth, 0.25f, span);
            Box(into, new Vector3(cx, ground + 0.15f, -cy), floorSize, submesh);

            // The roof: a thin slab at post height, slightly wider than the floor so it reads as
            // an overhang rather than a lid.
            var roofSize = frontOnY ? new Vector3(span + 0.3f, 0.18f, depth + 0.25f)
                                    : new Vector3(depth + 0.25f, 0.18f, span + 0.3f);
            Box(into, new Vector3(cx, ground + postHeight, -cy), roofSize, submesh);

            // Posts. Three across a narrow porch, four across a wide one - enough to read as a
            // colonnade at distance without becoming a fence.
            int posts = span > 7f ? 4 : 3;
            float outer = depth * 0.5f - 0.18f;
            for (int i = 0; i < posts; i++)
            {
                float t = posts == 1 ? 0.5f : i / (float)(posts - 1);
                float along = (t - 0.5f) * (span - 0.5f);

                float px = frontOnY ? cx + along : cx + sign * outer;
                float py = frontOnY ? cy + sign * outer : cy + along;

                Box(into, new Vector3(px, ground + postHeight * 0.5f, -py),
                    new Vector3(0.18f, postHeight, 0.18f), submesh);
            }
        }

        /// <summary>
        /// The rear outbuilding almost every lot on the Sanborn sheets has on it.
        ///
        /// A shed, a privy, a summer kitchen, later a garage - the maps do not always say which,
        /// but the SMALL SQUARE AT THE BACK OF THE LOT is on nearly every one, and a residential
        /// block without them reads as a subdivision rather than a town that grew.
        ///
        /// IT BELONGS TO THE LOT, NOT TO THE HOUSE, and this is the wrong place for it. The 1913
        /// sheet has lots carrying an outbuilding and NO HOUSE at all - lot 57 and lot 81 in
        /// docs/research/RESIDENTIAL-1913.md - which a hook hanging off the house grammar can
        /// never produce, because it only runs where a house exists. Moving it wants a lot-level
        /// pass rather than a massing Extras, and that is a change to whatever places dwellings
        /// rather than to this file. Left here, working, and labelled - see RESIDENTIAL-1913 §5.
        /// </summary>
        internal static void RearOutbuilding(Place place, MeshChunk into, int submesh, float size)
        {
            var b = place.Bounds;

            float dx = place.Door.X - (b.X + b.W * 0.5f);
            float dy = place.Door.Y - (b.Y + b.H * 0.5f);
            bool frontOnY = Mathf.Abs(dy) >= Mathf.Abs(dx);
            float sign = frontOnY ? Mathf.Sign(dy) : Mathf.Sign(dx);
            if (sign == 0f) sign = 1f;

            // Behind, which is the opposite side from the door, and offset to one side so it
            // does not sit dead centre like a wing of the house.
            float cx = b.X + b.W * 0.5f;
            float cy = b.Y + b.H * 0.5f;
            if (frontOnY) { cy = (sign > 0f ? b.Y : b.Y + b.H) - sign * (size * 0.5f + 1.5f); cx += b.W * 0.18f; }
            else          { cx = (sign > 0f ? b.X : b.X + b.W) - sign * (size * 0.5f + 1.5f); cy += b.H * 0.18f; }

            Box(into, new Vector3(cx, Space3D.GroundUnder(b) + size * 0.5f * 0.8f, -cy),
                new Vector3(size, size * 0.8f, size), submesh);
        }

        /// <summary>
        /// The back ell — a lower wing running off the rear of the main mass.
        ///
        /// THE SINGLE BIGGEST TELL OF A GENERATED TOWN IS A RECTANGULAR HOUSE, and the 1913 sheet
        /// is unambiguous that no real one is: every dwelling on it is L-shaped or T-shaped, a
        /// main block with a wing running back, usually a storey lower. It is how these houses
        /// were actually built - the ell is the kitchen, added or built cheap - and it is why a
        /// row of them does not read as extruded.
        ///
        /// Drawn as walls only, at `drop` below the main eaves, with a flat cap. A proper ridged
        /// roof over the ell wants AddRoof, which lives in RoofBuilder and takes a TileRect, so
        /// it is a bigger change than an Extras hook should make. A flat-capped wing at the right
        /// height and footprint already breaks the box, which is the thing that matters at the
        /// distance this game is looked at.
        /// </summary>
        internal static void BackEll(Place place, Massing m, MeshChunk into, int submesh,
                                     float widthFraction, float projection, float drop)
        {
            var b = place.Bounds;

            float dx = place.Door.X - (b.X + b.W * 0.5f);
            float dy = place.Door.Y - (b.Y + b.H * 0.5f);
            bool frontOnY = Mathf.Abs(dy) >= Mathf.Abs(dx);
            float sign = frontOnY ? Mathf.Sign(dy) : Mathf.Sign(dx);
            if (sign == 0f) sign = 1f;

            // Off the BACK - the opposite side from the door - and offset to one side, because an
            // ell centred on the rear wall reads as a symmetrical wing and no vernacular house
            // has one of those.
            float span = (frontOnY ? b.W : b.H) * Mathf.Clamp01(widthFraction);
            float height = Mathf.Max(2.2f, m.Eaves - drop);

            float cx = b.X + b.W * 0.5f;
            float cy = b.Y + b.H * 0.5f;

            if (frontOnY)
            {
                cy = (sign > 0f ? b.Y : b.Y + b.H) - sign * projection * 0.5f;
                cx += b.W * 0.20f;
            }
            else
            {
                cx = (sign > 0f ? b.X : b.X + b.W) - sign * projection * 0.5f;
                cy += b.H * 0.20f;
            }

            var size = frontOnY ? new Vector3(span, height, projection)
                                : new Vector3(projection, height, span);
            Box(into, new Vector3(cx, Space3D.GroundUnder(b) + height * 0.5f, -cy), size, submesh);
        }

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

            Quad(into, submesh, npp, ppp, pnp, nnp);
            Quad(into, submesh, ppn, npn, nnn, pnn);
            Quad(into, submesh, ppp, ppn, pnn, pnp);
            Quad(into, submesh, npn, npp, nnp, nnn);
            Quad(into, submesh, npn, ppn, ppp, npp);
            Quad(into, submesh, nnp, pnp, pnn, nnn);
        }

        /// <summary>
        /// One quad, four unshared vertices, wound so the face points OUT.
        ///
        /// Deliberately the same as MassingExtras.Quad rather than a call to it - that one is
        /// private, and the winding comment there is worth repeating because getting it backwards
        /// is not a subtle failure: it turned every tower and bell-cote inside out, and from most
        /// angles the thing simply was not there. The hip roof is this project's reference for
        /// which way round it goes.
        ///
        /// No normals are written; the roof mesh takes RecalculateNormals, as the walls do.
        ///
        /// AND THE COPY MUST NOW BE KEPT IN STEP ON UVs AS WELL AS ON WINDING. The same
        /// axis-aligned box is written three times in this project - here, MassingExtras.Box, and
        /// RoofBuilder.AddBox - which is why a single UV fault needed fixing in two places and why
        /// the chimney needs a third fix of its own. Consolidating all three into one
        /// `RoofMesh.Box` would make it one edit; it is deliberately NOT done, because it means
        /// rewriting winding-critical code in three files with no automated cover, and both
        /// MassingExtras and RoofBuilder record the winding being got wrong once with symptoms -
        /// towers invisible from most angles, chimneys with no top and no shadow - that a compile
        /// and the Core suite cannot see. Revisit it when something else is already opening all
        /// three files.
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

            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }
    }

    /// <summary>
    /// LAYER ONE, the 1857-1910s core: a vernacular frame farmhouse.
    ///
    /// One and a half storeys under a steep gable - the half storey is why the pitch is steep,
    /// because the upstairs rooms are inside the roof. This is the commonest thing on the 1913
    /// Sanborn sheets by a wide margin: "1½" written inside a small yellow rectangle, over and
    /// over, street after street.
    ///
    /// The porch covers part of the front rather than all of it. A full-width porch is the
    /// foursquare's signature and giving it to the farmhouse too would make the two layers
    /// indistinguishable from the air, which is the one thing this grammar exists to prevent.
    /// </summary>
    public sealed class FarmhouseMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(4.2f, RoofForm.Gable, 3.2f);

        public void Extras(Place place, MeshChunk into)
        {
            // The ell is the kitchen wing, a storey lower, off the back and to one side. It is
            // what stops a row of these reading as extruded - see RESIDENTIAL-1913 §4.
            FrameHouse.BackEll(place, Profile(place), into, Materials3D.WallIndex,
                               widthFraction: 0.5f, projection: 4.0f, drop: 1.4f);
            FrameHouse.Porch(place, Profile(place), into, Materials3D.WallIndex,
                             depth: 2.0f, fraction: 0.55f, postHeight: 2.7f);
            FrameHouse.RearOutbuilding(place, into, Materials3D.WallIndex, size: 3.2f);
        }
    }

    /// <summary>
    /// LAYER ONE, the showier half: an American Foursquare.
    ///
    /// Two full storeys on a nearly square plan, under a low HIPPED roof, with a full-width
    /// porch across the front and usually a dormer in the roof. Where a farmhouse is a farm
    /// building that people live in, a foursquare is a town house built by somebody doing well -
    /// so it is the minority type and belongs on the older streets near the centre.
    ///
    /// The hip against the farmhouse's gable is what separates them at a distance.
    /// </summary>
    public sealed class FoursquareMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(6.0f, RoofForm.Hip, 1.9f);

        public void Extras(Place place, MeshChunk into)
        {
            // Shallower than the farmhouse's: a foursquare is a town house on a town lot and its
            // back wing is a service range rather than half the building.
            FrameHouse.BackEll(place, Profile(place), into, Materials3D.WallIndex,
                               widthFraction: 0.42f, projection: 3.0f, drop: 2.6f);
            FrameHouse.Porch(place, Profile(place), into, Materials3D.WallIndex,
                             depth: 2.4f, fraction: 1.0f, postHeight: 3.0f);
            FrameHouse.RearOutbuilding(place, into, Materials3D.WallIndex, size: 3.4f);
        }
    }

    /// <summary>
    /// LAYER TWO, and the biggest: a 1920s-40s bungalow.
    ///
    /// The town's median build year is 1943 and 54% of it predates 1950, so THIS is the most
    /// common house in Rossville - and it appears on no Sanborn map, because the last of those
    /// was drawn in 1913 and most of these had not been built.
    ///
    /// Low and wide under a shallow gable, with a deep porch tucked under the main roof rather
    /// than added to the front of it. RidgeAcross is set so the GABLE FACES THE STREET, which is
    /// the bungalow's single most recognisable feature and the opposite of what the farmhouse
    /// does.
    /// </summary>
    public sealed class BungalowMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(3.2f, RoofForm.Gable, 1.9f, ridgeAcross: true);

        public void Extras(Place place, MeshChunk into)
        {
            FrameHouse.Porch(place, Profile(place), into, Materials3D.WallIndex,
                             depth: 2.6f, fraction: 0.95f, postHeight: 2.6f);
            FrameHouse.RearOutbuilding(place, into, Materials3D.WallIndex, size: 3.6f);
        }
    }

    /// <summary>
    /// LAYER THREE, the newest: a postwar ranch.
    ///
    /// One storey, long and low, under a shallow hip, with no porch to speak of - a stoop at the
    /// door and nothing more. A minority type, and it belongs on the edges of town where the last
    /// additions were platted rather than in the old core.
    ///
    /// The absence of a porch is deliberate and is the point: it is what makes a ranch read as
    /// postwar next to three generations of houses that all have one.
    /// </summary>
    public sealed class RanchMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(2.7f, RoofForm.Hip, 1.2f);

        public void Extras(Place place, MeshChunk into)
        {
            FrameHouse.Porch(place, Profile(place), into, Materials3D.WallIndex,
                             depth: 1.4f, fraction: 0.3f, postHeight: 2.4f);
            FrameHouse.RearOutbuilding(place, into, Materials3D.WallIndex, size: 4.0f);
        }
    }
}
