using System;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// A "percent reached by year" curve, and one entity's own year for it.
    ///
    /// Linear between waypoints, flat outside them. Empty when nothing was authored, and an empty
    /// one answers 0 to everything rather than throwing - see <see cref="TechnologyTable"/> on why
    /// a missing content file has to mean the 1991 world rather than a crash.
    /// </summary>
    public readonly struct Adoption
    {
        private readonly int[] _years;          // ascending, at least one when not empty
        private readonly float[] _percent;      // 0..100, same length

        public Adoption(int[] years, float[] percent)
        {
            if (years == null || percent == null || years.Length == 0)
            {
                _years = null; _percent = null; return;
            }
            if (years.Length != percent.Length)
                throw new ArgumentException("a curve needs one percentage per year");
            for (int i = 1; i < years.Length; i++)
                if (years[i] <= years[i - 1])
                    throw new ArgumentException($"waypoint years must ascend: {years[i - 1]} then {years[i]}");

            _years = years; _percent = percent;
        }

        public bool IsEmpty => _years == null;
        public int Waypoints => _years == null ? 0 : _years.Length;
        public int FirstYear => _years == null ? 0 : _years[0];
        public int LastYear => _years == null ? 0 : _years[_years.Length - 1];

        /// <summary>What share of the population has it in a given year, 0..100.</summary>
        public float PercentIn(int year)
        {
            if (_years == null) return 0f;
            if (year <= _years[0]) return _percent[0];
            if (year >= _years[_years.Length - 1]) return _percent[_percent.Length - 1];

            for (int i = 1; i < _years.Length; i++)
            {
                if (year > _years[i]) continue;
                float t = (year - _years[i - 1]) / (float)(_years[i] - _years[i - 1]);
                return _percent[i - 1] + t * (_percent[i] - _percent[i - 1]);
            }
            return _percent[_percent.Length - 1];
        }

        /// <summary>
        /// The year THIS rank gets it - the curve inverted, not sampled.
        ///
        /// Rounded UP, and that is not arbitrary: a year is a bucket, not an instant. If the curve
        /// passes this rank halfway through 2004 then the rank does not have it in 2004 and does
        /// have it in 2005, which makes <see cref="HeldIn"/> agree with <see cref="PercentIn"/>
        /// exactly rather than approximately. <c>Fields.DayWhen</c> rounds to NEAREST off the same
        /// shared inverter, because a day is a point on a calendar and not a bucket.
        ///
        /// <see cref="Era.Never"/> when the curve never reaches this rank at all.
        /// </summary>
        public int YearWhen(float rank)
        {
            if (_years == null) return Era.Never;
            float target = Era.Clamp01(rank) * 100f;

            if (target <= _percent[0]) return _years[0];      // had it before the record starts

            for (int i = 1; i < _years.Length; i++)
            {
                if (_percent[i] < target) continue;
                if (_percent[i] <= _percent[i - 1]) return _years[i];
                float t = (target - _percent[i - 1]) / (_percent[i] - _percent[i - 1]);
                return Era.CeilingAt(_years[i - 1], _years[i], t);
            }
            return Era.Never;
        }

        /// <summary>
        /// The year this rank LOSES it, for a curve that falls. <see cref="Era.Never"/> if it
        /// never does.
        ///
        /// A falling curve is not a bug - payphones went 100 to 60 to 10 because they LEFT - and
        /// the households that keep one longest are the lowest ranks, which is why loss is the
        /// same inversion read the other way up.
        /// </summary>
        public int YearLost(float rank)
        {
            if (_years == null) return Era.Never;
            int got = YearWhen(rank);
            if (got == Era.Never) return Era.Never;

            float target = Era.Clamp01(rank) * 100f;
            for (int i = 1; i < _years.Length; i++)
            {
                if (_years[i] <= got) continue;
                if (_percent[i] >= target) continue;
                if (_percent[i] >= _percent[i - 1]) return _years[i];

                float t = (_percent[i - 1] - target) / (_percent[i - 1] - _percent[i]);
                int lost = Era.CeilingAt(_years[i - 1], _years[i], t);
                return lost > got ? lost : got + 1;
            }
            return Era.Never;
        }

        /// <summary>
        /// Whether this rank has it in this year. Defined from the two crossing years rather than
        /// by comparing against the curve, so there is exactly one year it arrives and at most one
        /// it goes - a rank cannot flicker in and out along a wobble in the authored waypoints.
        /// </summary>
        public bool HeldIn(int year, float rank)
        {
            int got = YearWhen(rank);
            if (got == Era.Never || year < got) return false;
            int lost = YearLost(rank);
            return lost == Era.Never || year < lost;
        }
    }

    /// <summary>
    /// Turning a population-wide curve into one household's own year.
    ///
    /// THE MECHANISM WAS ALREADY IN THIS CODEBASE AND THIS IS IT LIFTED OUT, NOT A SECOND COPY.
    /// <c>Fields</c> worked it out first for crops, and its commit put it better than the plan did:
    ///
    ///     "The percentages are a distribution, and that is the trick. The state tabulates what
    ///      fraction of acreage has reached each stage by each date, and that curve is not an
    ///      average to apply to every field - it IS the spread of dates across fields. So each
    ///      field gets a fixed rank in [0,1) and its own planting date is read off by inverting
    ///      the curve."
    ///
    /// Adoption of a technology is the identical shape one domain over: 40% of rural households
    /// had a computer in 1998 is not a coin flip to make per household per year, it is a statement
    /// about WHICH households and therefore about when each one bought theirs. So
    /// <see cref="Crossing"/> lives here and both callers use it, on the argument
    /// <c>ContentText</c>'s own header makes about parsers: two implementations that agree only by
    /// coincidence stop agreeing the first time one of them is corrected.
    ///
    /// Built general rather than technology-specific on purpose. <c>particulars.txt</c> is
    /// nineteen clauses deep in era-coupled English detail and will need year ranges when it is
    /// rewritten for Illinois; it can have them without a third mechanism.
    /// </summary>
    public static class Era
    {
        /// <summary>A crossing that never happens. Not a year anything in this game runs in.</summary>
        public const int Never = int.MaxValue;

        /// <summary>
        /// Where a "percent reached by X" curve crosses a target percentage, as a fractional X.
        ///
        /// THE SHARED INVERTER. <c>Fields.DayWhen</c> rounds this to nearest to get a planting
        /// date; <see cref="Adoption.YearWhen"/> takes the ceiling to get an adoption year. Same
        /// arithmetic, two roundings, one place to correct it.
        ///
        /// Flat outside the waypoints: a target at or below the first percentage returns the first
        /// X, and one above the last returns the last. That first case is what makes a FALLING
        /// curve work - <c>payphone</c> opens at 100% and every rank has it at the start.
        /// </summary>
        public static float Crossing(int[] at, float[] percent, float target)
        {
            if (at == null || percent == null || at.Length == 0) return 0f;
            if (target <= percent[0]) return at[0];

            for (int i = 1; i < at.Length; i++)
            {
                if (target > percent[i]) continue;
                float span = percent[i] - percent[i - 1];
                if (span <= 0f) return at[i];
                float t = (target - percent[i - 1]) / span;
                return at[i - 1] + t * (at[i] - at[i - 1]);
            }
            return at[at.Length - 1];
        }

        /// <summary>The fraction t of the way from one year to the next, rounded up to a whole one.</summary>
        internal static int CeilingAt(int from, int to, float t)
        {
            float exact = from + t * (to - from);
            int whole = (int)exact;
            return exact > whole ? whole + 1 : whole;
        }

        /// <summary>
        /// This key's fixed place in the queue for one named thing, 0 = first to get it.
        ///
        /// Stable forever and taking no year: the household that buys a computer early buys it
        /// early, and a rank that reshuffled each year would put a machine in a house one year and
        /// take it out the next.
        ///
        /// SALTED PER TECHNOLOGY. Without the salt every household would be in the same position
        /// in every queue - first to the computer, first to the mobile, first to the dish - which
        /// is a town of one rich street and one poor one. Being early to one thing says little
        /// about being early to another.
        ///
        /// The shift is <c>Fields.RankOf</c>'s and is deliberate: the low bits of a key carry
        /// whatever structure the key was built from, and using them stripes the map.
        /// </summary>
        public static float RankOf(ulong key, string salt) =>
            ((Rolls.Avalanche(key ^ Keys.Of(salt)) >> 11) & 0xFFFFUL) / 65536f;

        internal static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
