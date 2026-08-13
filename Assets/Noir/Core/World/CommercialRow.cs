using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>How a storefront is built. The Sanborn surveyors coloured this in.</summary>
    public enum Construction : byte
    {
        /// <summary>Sanborn red. The rebuilt core, from brick made inside the village.</summary>
        Brick = 0,

        /// <summary>Sanborn yellow. What the row degrades to at its ends, and what did not burn well.</summary>
        Frame,
    }

    /// <summary>
    /// One shopfront in a terrace: where it starts along the street, how wide it is, how tall,
    /// and what it is made of. Purely the fabric — what trade is inside is somebody else's
    /// question, and deliberately not answered here.
    /// </summary>
    public readonly struct Storefront
    {
        /// <summary>Metres along the frontage from the crossing, to this unit's near edge.</summary>
        public readonly float Offset;

        /// <summary>Metres of street frontage.</summary>
        public readonly float Width;

        /// <summary>One or two. Nothing downtown was ever three.</summary>
        public readonly int Storeys;

        public readonly Construction Construction;

        /// <summary>The corner unit at the crossing — the bank, the drug store. Never infilled.</summary>
        public readonly bool IsAnchor;

        public Storefront(float offset, float width, int storeys, Construction construction, bool isAnchor)
        {
            Offset = offset;
            Width = width;
            Storeys = storeys;
            Construction = construction;
            IsAnchor = isAnchor;
        }

        /// <summary>Metres along the frontage to this unit's far edge.</summary>
        public float End => Offset + Width;

        public override string ToString() =>
            $"{Offset:0.0}m +{Width:0.0}m {Storeys}st {Construction}{(IsAnchor ? " anchor" : "")}";
    }

    /// <summary>
    /// Lays a downtown terrace along a street frontage, running away from a crossing.
    ///
    /// THE RULE THIS ENCODES, in one line: <b>height and width decay with distance from the
    /// crossing.</b> It is not invented. It was read off the 1913 Sanborn fire-insurance sheets
    /// for Rossville, where the surveyor wrote every unit's storey count and its frontage in feet
    /// underneath it, on all four faces of the Attica × Chicago crossing — forty units in total.
    /// See docs/research/COMMERCIAL-ROW.md for the transcription this was fitted to.
    ///
    /// The corner of the crossing carries two-storey units of 22–26 ft with halls, offices, a
    /// dentist and a law office upstairs. Walk away from it in any direction and the buildings
    /// drop to one storey, narrow to 14–18 ft, the trades get heavier and dirtier, and at the far
    /// end the brick simply stops and the row finishes in frame. That gradient is land value
    /// written in brick, and it is why a generator does not need to place each unit by hand.
    ///
    /// WHAT IS DELIBERATELY NOT MODELLED. The row is a continuous terrace: party walls, no gaps,
    /// no variation in setback. Every unit fronts the pavement squarely. Depth is not modelled at
    /// all — the footprints show the units are not uniformly deep and several have rear
    /// extensions, but the surveyor annotated no depth figure, so there is no number to use and
    /// inventing one would be the loudest kind of guess. Trades are not assigned: what sits in a
    /// unit changes on a decade, what the unit is made of changes on a century, and only the
    /// second one is fabric.
    ///
    /// WHY THERE IS AN RNG HERE AT ALL. Two of the four real faces decay perfectly monotonically;
    /// the other two do not. A narrow single-storey unit turns up amid two-storey neighbours three
    /// times in forty. That is not noise in the survey, it is what a street looks like after
    /// something burns down and gets replaced more cheaply than what stood there — and this town
    /// lost a quarter of its downtown in 1893 and another quarter in 2004. A row generated with
    /// pure monotonic decay comes out *more regular than the real one*, which reads as generated
    /// at a glance. <see cref="InfillRate"/> puts the dips back.
    /// </summary>
    public static class CommercialRow
    {
        /// <summary>Feet to metres. The surveyor wrote feet; the world is in metres.</summary>
        public const float Foot = 0.3048f;

        /// <summary>
        /// How far the two-storey zone reaches from the crossing.
        ///
        /// Measured four ways off the 1913 sheet, by summing the annotated frontages of the
        /// two-storey run on each face: Chicago west 28 m, Chicago east 35 m, Attica east 43 m,
        /// Attica north 34 m to the dip. The spread is real and this is the middle of it.
        /// </summary>
        public const float TwoStoreyReach = 35f;

        /// <summary>
        /// Where the brick gives out. On the west side of Chicago the terrace runs 179 ft of
        /// brick and then finishes in a frame barber's shop; that is the only face where the row
        /// reaches its own end within the sheet, so this is one observation, not four.
        /// </summary>
        public const float BrickReach = 55f;

        /// <summary>Widest frontage near the crossing: 26 ft.</summary>
        public const float WidestFeet = 26f;

        /// <summary>Narrowest frontage at the far end: 14 ft.</summary>
        public const float NarrowestFeet = 14f;

        /// <summary>
        /// Over what distance the frontage narrows from <see cref="WidestFeet"/> to
        /// <see cref="NarrowestFeet"/>. Beyond this the row is all narrow units.
        /// </summary>
        public const float NarrowingReach = 70f;

        /// <summary>
        /// The frontages the surveyor actually wrote, in feet, widest first. Widths snap to these
        /// rather than interpolating freely: real frontages come in a few sizes because they were
        /// sold as lots, not cut to fit.
        /// </summary>
        public static readonly float[] FrontagesFeet = { 26f, 25f, 22f, 18f, 16f, 14f };

        /// <summary>
        /// How often a unit is an infill — dropped to one storey and narrowed, regardless of
        /// where it sits. Three of the forty surveyed units break the decay this way, so 3/40.
        /// </summary>
        public const float InfillRate = 0.075f;

        /// <summary>
        /// How wide an infill unit is, in feet.
        ///
        /// Not the narrowest possible. The three real dips are 18 ft, 18 ft and 15 ft — a
        /// replacement building is cheaper than its neighbours, not a broom cupboard. An earlier
        /// version slammed dips to the 14 ft minimum, which is narrower than anything actually
        /// surveyed and read as damage rather than as infill.
        /// </summary>
        public const float InfillFeet = 18f;

        /// <summary>
        /// How far from the crossing the upper-floor halls reach, and therefore how far infill is
        /// forbidden.
        ///
        /// The first-floor rooms at the corner are not divided the way the shops below them are:
        /// the survey shows a lodge hall spanning <i>two</i> shopfronts, an I.O.O.F. hall over the
        /// bank on one face and a G.A.R. hall two doors along on another. A single-storey infill
        /// dropped into that run would cut a hall in half. So the corner group is protected — and
        /// this is also why no dip appears in the first three units on any of the four surveyed
        /// faces. Three units at the wide end is about 22 m.
        /// </summary>
        public const float HallSpan = 22f;

        /// <summary>
        /// Lay a terrace from a crossing outward along <paramref name="frontage"/> metres.
        ///
        /// Unit 0 is the anchor on the corner: always two storeys, always the widest frontage,
        /// never infilled. Every later unit butts against the one before it. The last unit is
        /// truncated to the frontage remaining and is dropped entirely if what remains is too
        /// narrow to be a shop.
        /// </summary>
        /// <param name="frontage">Metres of street available, measured from the crossing.</param>
        /// <param name="rng">Draws the infill dips. Pass a substream so adding a row elsewhere
        /// does not reshuffle this one.</param>
        public static Storefront[] Lay(float frontage, IRng rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            float narrowest = NarrowestFeet * Foot;
            var units = new List<Storefront>();
            float offset = 0f;

            while (frontage - offset >= narrowest)
            {
                bool anchor = units.Count == 0;

                float width = anchor ? WidestFeet * Foot : WidthAt(offset);
                int storeys = offset < TwoStoreyReach ? 2 : 1;
                var construction = offset < BrickReach ? Construction.Brick : Construction.Frame;

                // The dip: cheaper than its neighbours, because it replaced something that burned.
                // Never under the halls — see HallSpan.
                if (offset >= HallSpan && rng.Chance(InfillRate))
                {
                    width = InfillFeet * Foot;
                    storeys = 1;
                }

                // Truncate rather than overrun. A unit that would hang off the end of the block
                // is not a unit; the terrace ends where the block ends.
                float remaining = frontage - offset;
                if (width > remaining) width = remaining;
                if (width < narrowest) break;

                units.Add(new Storefront(offset, width, storeys, construction, anchor));
                offset += width;
            }

            return units.ToArray();
        }

        /// <summary>
        /// The frontage a unit gets at a given distance out, snapped to a surveyed size.
        /// Linear from 26 ft at the crossing to 14 ft at <see cref="NarrowingReach"/>.
        /// </summary>
        public static float WidthAt(float distance)
        {
            if (distance < 0f) distance = 0f;

            float t = distance / NarrowingReach;
            if (t > 1f) t = 1f;

            float feet = WidestFeet + (NarrowestFeet - WidestFeet) * t;
            return Snap(feet) * Foot;
        }

        /// <summary>Nearest surveyed frontage, in feet.</summary>
        public static float Snap(float feet)
        {
            float best = FrontagesFeet[0];
            float bestGap = Abs(feet - best);

            for (int i = 1; i < FrontagesFeet.Length; i++)
            {
                float gap = Abs(feet - FrontagesFeet[i]);
                if (gap < bestGap) { bestGap = gap; best = FrontagesFeet[i]; }
            }

            return best;
        }

        /// <summary>
        /// The handle a generated storefront is known by before anyone has ruled a business onto
        /// it — what `BusinessRulings`/`business-1991.txt` and the in-game panel key on.
        /// Address-based so it reads the way the owner already writes rulings by hand
        /// ("112 S Chicago #1"), falling back to the parcel id for a footprint-later lot with no
        /// resolvable street address. 1-based index, left to right from the crossing.
        /// </summary>
        public static string HandleFor(string address, int parcelId, int index) =>
            string.IsNullOrEmpty(address)
                ? $"parcel {parcelId} unit {index}"
                : $"{address} #{index}";

        private static float Abs(float v) => v < 0f ? -v : v;
    }
}
