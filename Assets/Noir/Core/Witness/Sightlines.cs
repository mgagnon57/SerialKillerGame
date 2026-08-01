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

        public static SightingClarity HowGoodALook(Tile watcher, Tile subject, int minuteOfDay,
                                                   Citizen who)
        {
            int distance = Tile.ChebyshevDistance(watcher, subject);
            if (distance > NeverBeyond) return SightingClarity.Glimpsed;   // caller gates on range

            int light = LightAt(minuteOfDay);   // 2 day, 1 dusk, 0 night

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
        /// </summary>
        public static bool SawAnythingAtAll(SightingClarity clarity, Tile watcher, Tile subject) =>
            Tile.ChebyshevDistance(watcher, subject) <= NeverBeyond;

        /// <summary>2 in daylight, 1 at dusk, 0 at night. 1979 street lighting is not lighting.</summary>
        private static int LightAt(int minuteOfDay)
        {
            int hour = minuteOfDay / 60;
            if (hour >= 7 && hour < 19) return 2;
            if ((hour >= 5 && hour < 7) || (hour >= 19 && hour < 21)) return 1;
            return 0;
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
