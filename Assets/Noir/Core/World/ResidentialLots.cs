using System;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// The three date-layers of housing this town is made of, plus the outlier the 1913 survey
    /// calls the taller minority. What each one looks like is the massing grammars' business;
    /// deciding which one a lot gets is this file's.
    /// </summary>
    public enum HouseEra : byte
    {
        /// <summary>1857–1910s. 1½ storey, steep gable, an ell running back. The commonest thing on the 1913 map.</summary>
        Farmhouse = 0,

        /// <summary>1857–1910s, the taller minority. 2 storey, low hip.</summary>
        Foursquare,

        /// <summary>1920s–40s. The layer that filled the empty lots — median build year 1943.</summary>
        Bungalow,

        /// <summary>Postwar, and on the edges. Low, wide, single storey.</summary>
        Ranch,
    }

    /// <summary>What is at the back of the lot. It belongs to the lot, not to the house.</summary>
    public enum Outbuilding : byte
    {
        None = 0,

        /// <summary>Median 11 m² across 67 measured. A privy, a woodshed, a coal store.</summary>
        Shed,

        /// <summary>Median 27 m² across 46 measured. A barn or stable, later a garage.</summary>
        Barn,
    }

    /// <summary>One platted lot and what stands on it.</summary>
    public readonly struct Homestead
    {
        public readonly bool Built;

        /// <summary>Ground area of the house in m², excluding the porch. Zero if nothing is built.</summary>
        public readonly float Footprint;

        /// <summary>1, 1.5 or 2. Nothing in this town was ever three.</summary>
        public readonly float Storeys;

        public readonly HouseEra Era;
        public readonly Outbuilding Outbuilding;

        public Homestead(bool built, float footprint, float storeys, HouseEra era, Outbuilding outbuilding)
        {
            Built = built;
            Footprint = footprint;
            Storeys = storeys;
            Era = era;
            Outbuilding = outbuilding;
        }

        public override string ToString() =>
            Built ? $"{Era} {Footprint:0}m2 {Storeys}st + {Outbuilding}" : $"vacant + {Outbuilding}";
    }

    /// <summary>
    /// Decides what stands on a residential lot: whether anything does, how big it is, how tall,
    /// which decade it came from, and what is behind it.
    ///
    /// EVERY CONSTANT HERE WAS MEASURED, not chosen. The footprint distribution comes from
    /// classifying all four 1913 Sanborn sheets by their own colour key and measuring every
    /// building on them — 503 buildings, of which 268 are dwelling-sized. See
    /// docs/research/BUILDING-CENSUS-1913.md for the method and its limits, and
    /// docs/research/PARCEL-STATISTICS.md for the modern parcel figures.
    ///
    /// THE TWO DATES, AND WHY BOTH ARE USED. The fabric is 1913 because that is the only survey
    /// that measured this town building by building. The density is 2000 because that is when the
    /// game is set. Those are different numbers — 1913 leaves far more lots empty than 2000 does —
    /// and the gap between them is not an inconsistency to be averaged away. It is the bungalows.
    /// The lots standing empty in 1913 are where the 1920s–40s layer went, which is exactly why
    /// the parcel data's median build year is 1943. So a lot that would have been built by 1913
    /// gets an early house, and a lot that filled in later gets a bungalow or a ranch, and the
    /// town comes out with its real age-rings rather than one uniform vintage.
    ///
    /// WHAT IS NOT DECIDED HERE: where the house sits on the lot (front, shallow setback), its
    /// plan shape (L or T — never a rectangle), its porch, or its roof. Those are geometry and
    /// belong to the massing grammars. This file answers only "what is on this lot".
    /// </summary>
    public static class ResidentialLots
    {
        /// <summary>
        /// Share of residential lots carrying a house in 2000. The assessor's own records:
        /// 517 improved of 623 residential parcels. Not a sample — the whole class.
        /// </summary>
        public const float Occupancy2000 = 517f / 623f;

        /// <summary>
        /// How many houses stood in 1913: 268 dwelling-sized footprints counted off the Sanborn
        /// sheets. Against the 517 standing today, that is the size of the surviving old layer.
        /// </summary>
        public const float Dwellings1913 = 268f;

        /// <summary>Improved residential parcels today, from the assessor.</summary>
        public const float Dwellings2000 = 517f;

        /// <summary>
        /// Measured footprint percentiles in m², from 268 dwelling-sized frame footprints.
        /// Sampled by linear interpolation between these — no curve is fitted, because a fitted
        /// curve would claim more than five percentiles know.
        /// </summary>
        public static readonly float[] FootprintQuantiles = { 0.00f, 0.10f, 0.25f, 0.50f, 0.75f, 0.90f, 1.00f };

        /// <summary>
        /// The areas those quantiles fall at. The p10–p90 values are measured; the two ends are
        /// extrapolated — 45 m² is the threshold the census used to separate a small house from a
        /// large barn, and 200 m² is past the largest ordinary house and short of the
        /// institutional buildings.
        /// </summary>
        public static readonly float[] FootprintAreas = { 45f, 54f, 75f, 97f, 125f, 163f, 200f };

        /// <summary>
        /// Share of pre-1913 houses that are the two-storey foursquare rather than the 1½ storey
        /// farmhouse. The survey writes 1½ far more often than 2; this is the taller minority.
        /// </summary>
        public const float FoursquareShare = 0.25f;

        /// <summary>
        /// Outbuildings per house: 226 counted against 268 dwellings. Applied per lot including
        /// vacant ones, because lots 57 and 81 on the surveyed block carry an outbuilding and no
        /// house — the shed belongs to the lot.
        /// </summary>
        public const float OutbuildingRate = 226f / 268f;

        /// <summary>Of those outbuildings, the share that are the smaller shed class: 67 of 113.</summary>
        public const float ShedShare = 67f / 113f;

        /// <summary>
        /// Share of today's built lots carrying a pre-1913 house: 268 counted in 1913 against 517
        /// standing now, so a little over half.
        ///
        /// This used to be derived from a 60% occupancy figure that came from eight lots on one
        /// crop — and RESIDENTIAL-1913.md flagged in its own caveats that the figure was a sample
        /// and should be counted before being used as a constant. It has been counted now, and the
        /// two full surveys divide directly into each other without needing the sample at all.
        ///
        /// A THIRD SOURCE AGREES, and bounds it. The Census ACS puts **48.2%** of Rossville's
        /// current housing in its bottom bucket, "1939 or earlier". Pre-1913 is necessarily a
        /// subset of pre-1940, so this figure cannot really exceed 0.482 — the count of 268 says
        /// 0.518, which overshoots by 8% and lands inside the slack in a 45 m² threshold that has
        /// to separate a small house from a large barn. Two independent surveys agreeing to
        /// within 8% is the strongest confirmation in this file; the exact value is not knowable
        /// to better than "about half", and nothing downstream should depend on which half-percent.
        /// </summary>
        public const float EarlyShareOfBuilt = Dwellings1913 / Dwellings2000;

        /// <summary>
        /// Where the bungalow layer gives out and the postwar one starts, as a share of built
        /// lots. The 1920s–40s layer is much the larger of the two — the parcel data's median
        /// build year is 1943, not 1963.
        /// </summary>
        public const float RanchShareOfBuilt = 0.08f;

        /// <summary>
        /// How much a lot's vintage wanders from its position, plus or minus.
        ///
        /// Age rings are a tendency, not a rule. Somebody's house burned down in 1961 and was
        /// replaced by a ranch two doors from the crossing; somebody else's grandmother would not
        /// sell, so there is an 1890s farmhouse marooned in a postwar street. Zero jitter gives
        /// perfectly stratified rings, which no real town has.
        /// </summary>
        public const float VintageJitter = 0.28f;

        /// <summary>
        /// What stands on one lot.
        /// </summary>
        /// <param name="edgeness">
        /// Where this lot is, 0 at the crossing and 1 at the edge of town. Drives which decade
        /// built here: the old core first, the postwar layer only on the fringe.
        /// </param>
        /// <param name="rng">Pass a substream keyed to the lot, so adding a street elsewhere does
        /// not rebuild this one.</param>
        public static Homestead Occupy(float edgeness, IRng rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (edgeness < 0f) edgeness = 0f;
            if (edgeness > 1f) edgeness = 1f;

            // Draw every value up front, unconditionally. If the vacant branch drew fewer numbers
            // than the built branch, one lot going vacant would shift every lot after it — the
            // same additive-content problem Place.Key exists to solve.
            float roll = rng.NextFloat();
            float sizeRoll = rng.NextFloat();
            float typeRoll = rng.NextFloat();
            float outRoll = rng.NextFloat();
            float classRoll = rng.NextFloat();
            float styleRoll = rng.NextFloat();

            var outbuilding = outRoll < OutbuildingRate
                ? (classRoll < ShedShare ? Outbuilding.Shed : Outbuilding.Barn)
                : Outbuilding.None;

            if (roll >= Occupancy2000)
                return new Homestead(false, 0f, 0f, HouseEra.Farmhouse, outbuilding);

            float footprint = FootprintAt(sizeRoll);
            float vintage = VintageOf(edgeness, typeRoll);

            HouseEra era;
            float storeys;

            if (vintage < EarlyShareOfBuilt)
            {
                // The 1857–1910s layer. 1½ storey dominates; the foursquare is the tall minority.
                bool tall = styleRoll < FoursquareShare;
                era = tall ? HouseEra.Foursquare : HouseEra.Farmhouse;
                storeys = tall ? 2f : 1.5f;
            }
            else if (vintage < 1f - RanchShareOfBuilt)
            {
                era = HouseEra.Bungalow;
                storeys = 1.5f;
            }
            else
            {
                era = HouseEra.Ranch;
                storeys = 1f;
            }

            return new Homestead(true, footprint, storeys, era, outbuilding);
        }

        /// <summary>
        /// How late a lot was built, 0 oldest and 1 newest.
        ///
        /// THE TOWN GREW OUTWARD, so where a lot sits is most of what says when it was built: the
        /// four blocks at Chicago × Attica were platted in 1857 and everything else is an addition
        /// bolted onto the edge of what was already there. A generator that scatters vintages
        /// evenly puts 1890s farmhouses on the postwar fringe and ranches in the old core, which
        /// is the single most visible way to get a small town wrong — and is exactly what the
        /// first version of this file did, until a street of it was printed out and looked at.
        ///
        /// <paramref name="jitterRoll"/> blurs the rings by <see cref="VintageJitter"/> either
        /// way, so the layers interleave at their boundaries the way real ones do.
        /// </summary>
        public static float VintageOf(float edgeness, float jitterRoll)
        {
            float v = edgeness + (jitterRoll - 0.5f) * 2f * VintageJitter;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        /// <summary>
        /// Footprint in m² for a uniform draw in [0,1), by linear interpolation between the
        /// measured percentiles.
        /// </summary>
        public static float FootprintAt(float u)
        {
            if (u <= 0f) return FootprintAreas[0];
            if (u >= 1f) return FootprintAreas[FootprintAreas.Length - 1];

            for (int i = 1; i < FootprintQuantiles.Length; i++)
            {
                if (u > FootprintQuantiles[i]) continue;

                float span = FootprintQuantiles[i] - FootprintQuantiles[i - 1];
                float t = span <= 0f ? 0f : (u - FootprintQuantiles[i - 1]) / span;
                return FootprintAreas[i - 1] + (FootprintAreas[i] - FootprintAreas[i - 1]) * t;
            }

            return FootprintAreas[FootprintAreas.Length - 1];
        }
    }
}
