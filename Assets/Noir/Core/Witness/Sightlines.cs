using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;

namespace Noir.Core.Witness
{
    /// <summary>
    /// Whether one person could have got a look at another: how far, how lit, how attentive.
    ///
    /// Distance is Chebyshev and there is no wall test, which is a deliberate simplification and
    /// not an oversight. A line-of-sight raycast through the tile grid would be more accurate and
    /// would make every number below untestable in isolation, because a test would have to build
    /// a world first. When somebody adds occlusion it belongs behind this same signature.
    ///
    /// Attention comes from the person's own particulars rather than from a separate stat. A
    /// villager who lingers is one who drew a sentence about lingering, so what he notices and
    /// why he notices it can never disagree.
    /// </summary>
    public static class Sightlines
    {
        /// <summary>Past this, in the best light there is, nobody has seen a thing.</summary>
        public const int NeverBeyond = 60;

        private const int Near = 12;
        private const int Middling = 30;

        /// <param name="when">
        /// THE WHOLE CLOCK, not a minute-of-day, because darkness is a fact about the DATE as
        /// much as the hour. Sunset over this crossing is 16:27 on 21 December and 20:24 on 21
        /// June - so half past seven in the evening is broad daylight in one season and an hour
        /// past dark in the other, and the same walk home is witnessed in June and unwitnessed
        /// in December. That is the whole argument of WHO-SEES-WHOM.md and a minute-of-day
        /// cannot express any of it.
        /// </param>
        public static SightingClarity HowGoodALook(Tile watcher, Tile subject, GameClock when,
                                                   Citizen who)
        {
            int distance = Tile.ChebyshevDistance(watcher, subject);
            if (distance > NeverBeyond) return SightingClarity.Glimpsed;   // caller gates on range

            int light = LightBand(when);   // 2 day, 1 twilight, 0 night

            // The table in the plan, as arithmetic — with one deliberate divergence. Band 2 is
            // Clear, 1 Partial, 0 Glimpsed, and below 0 is nothing at all, which the caller
            // detects via SawAnythingAtAll. THE DIVERGENCE: the plan's table says a night
            // sighting past 30 tiles is nothing, but the clamp below floors it at Glimpsed and
            // the range check knows nothing about light, so at 45 tiles in the dark a witness
            // still has something to say. That is pinned by a test and left open on purpose:
            // whether it is right is a question about how much thin night testimony the town
            // should produce, and the statement census answers it with numbers.
            int band = distance <= Near ? 2 : distance <= Middling ? 1 : 0;
            if (light == 0) band -= 1;

            band += Attention(who);

            if (band > 2) band = 2;
            if (band < 0) band = 0;
            return (SightingClarity)band;
        }

        /// <summary>
        /// False when there was nothing to remember. Separate from the clarity itself because
        /// Glimpsed is a real, useful, common statement and "out of range" is not a statement.
        ///
        /// STANDING CORN BELONGS HERE AND NOT IN THE CLARITY. A 2.5 metre wall between two people
        /// does not make the look Partial - there is nothing to be partial about. It is the same
        /// kind of answer as "too far": no statement rather than a poor one.
        ///
        /// `blocked` is null in every test that only cares about range and light, which is most
        /// of them, and null means nothing is in the way. See ISightBlocked for why this is asked
        /// rather than worked out here.
        /// </summary>
        public static bool SawAnythingAtAll(SightingClarity clarity, Tile watcher, Tile subject,
                                            GameClock when = default, ISightBlocked blocked = null)
        {
            if (Tile.ChebyshevDistance(watcher, subject) > NeverBeyond) return false;
            return blocked == null || !blocked.Between(watcher, subject, when);
        }

        /// <summary>
        /// 2 in daylight, 1 in civil twilight, 0 in the dark. Street lighting here is not lighting.
        ///
        /// THIS USED TO BE A THREE-BAND CONSTANT - day 07:00-19:00, dusk either side, night the
        /// rest - written before Daylight existed. It was wrong for most of the year in both
        /// directions: it called 18:00 in December daylight, when the sun set at 16:27, and
        /// 20:00 in June twilight, when the sun does not set until 20:24. A walk that a witness
        /// could not possibly have seen was being reported as a clear look, and one they saw
        /// plainly was being written down as a glimpse.
        ///
        /// Daylight is a real table for the Chicago x Attica crossing on the pre-2007 DST rule
        /// that actually applied in this window. Dawn and Dusk are both civil twilight and both
        /// band 1 - light enough to see by, not light enough to be sure, which is exactly what
        /// Partial means.
        /// </summary>
        private static int LightBand(GameClock when)
        {
            switch (Daylight.At(when.Month, when.DayOfMonth, when.MinuteOfDay))
            {
                case LightLevel.Day: return 2;
                case LightLevel.Night: return 0;
                default: return 1;
            }
        }

        /// <summary>+1 for somebody who watches the street, -1 for somebody who does not.</summary>
        private static int Attention(Citizen who)
        {
            if ((who.Beats & Beat.Lingers) != 0 || who.Sociability >= 192) return 1;
            if (who.Sociability < 64) return -1;
            return 0;
        }
    }
}
