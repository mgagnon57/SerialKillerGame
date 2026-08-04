using System;

namespace Noir.Core.World
{
    /// <summary>What is growing in a field this year. They alternate, so a field is never both.</summary>
    public enum Crop : byte { Corn = 0, Soybean = 1 }

    /// <summary>
    /// Where a field is in its year. The order is the order it happens in, starting from January.
    /// </summary>
    public enum FieldState : byte
    {
        /// <summary>Last season's residue. December to April, and again from harvest to Christmas.</summary>
        Stubble = 0,
        /// <summary>Worked black soil, waiting for the drill. The mud season.</summary>
        Tilled = 1,
        /// <summary>In the ground and showing - rows of green on black.</summary>
        Seedling = 2,
        /// <summary>Rising. Knee-high to head-high, and the month it stops being see-over.</summary>
        Growing = 3,
        /// <summary>Full height. For corn this is the wall.</summary>
        Standing = 4,
        /// <summary>Full height, colour turned - beans gold from early September, corn browning.</summary>
        Turning = 5,
    }

    /// <summary>
    /// What a field looks like on a given day, and how tall it is.
    /// </summary>
    public readonly struct FieldCondition
    {
        public readonly Crop Crop;
        public readonly FieldState State;
        /// <summary>Metres. What actually matters - this is a sightline question, not a texture one.</summary>
        public readonly float Height;

        public FieldCondition(Crop crop, FieldState state, float height)
        {
            Crop = crop; State = state; Height = height;
        }

        /// <summary>True when a person on the far side of this field cannot be seen across it.</summary>
        public bool BlocksSightline => Height >= Fields.EyeLevel;

        public override string ToString() => $"{Crop} {State} {Height:0.0}m";
    }

    /// <summary>
    /// The countryside as a sequence rather than a texture.
    ///
    /// THE FIELDS ARE MOST OF THE MAP. The town/country split is about 5:1, so the majority of
    /// visible surface in this game is cropland, and cropland goes from black soil in April to a
    /// wall of corn eight feet high in August to stubble and mud in November. THE-YEAR.md calls
    /// that the largest visual change anywhere in this world.
    ///
    /// IT IS ALSO THE SAME MECHANIC AS DARKNESS. Standing corn is a 2.5 m barrier over half the
    /// map for a quarter of the year, and then it is gone in three weeks. Anything that reasons
    /// about what a person could have seen has to know the month for exactly the reason it has to
    /// know the hour - which is why this is in Core beside Daylight and not in the renderer.
    ///
    /// THE PERCENTAGES ARE A DISTRIBUTION, AND THAT IS THE TRICK. Illinois Agricultural
    /// Statistics 2000 tabulates what fraction of the state's acreage has reached each stage by
    /// each date. That curve is not an average to be applied to every field - it IS the spread of
    /// dates across fields. So each field gets a fixed rank in [0,1) and its own planting date is
    /// read off by inverting the curve. The field ranked 0.1 is always among the first drilled and
    /// the first cut, every year, because that is a real farmer with real equipment.
    ///
    /// ERA-MATCHED, AND THAT ORDER IS A PERIOD FACT. These figures average 1990-99, the decade the
    /// game sits at the end of. In the 1990s CORN WENT IN THREE WEEKS BEFORE BEANS - 50% planted
    /// around 5 May against 25 May. Modern Illinois has flipped and often drills beans first. A
    /// modern crop calendar would plant this town in the wrong order.
    /// </summary>
    public static class Fields
    {
        /// <summary>
        /// A field is a forty-acre quarter-quarter section: 402.336 m square. This is not a
        /// tuning number - it is the Public Land Survey System, which is why the Illinois
        /// countryside is a grid of squares in the first place and why field edges, section roads
        /// and hedgerows all line up with each other.
        /// </summary>
        public const float FortyAcres = 402.336f;

        /// <summary>Metres. Above this a crop hides a standing adult from another standing adult.</summary>
        public const float EyeLevel = 1.7f;

        /// <summary>Full height, metres. Corn is the eight-foot wall; beans never reach the eye.</summary>
        public const float CornHeight = 2.5f;
        public const float SoybeanHeight = 0.9f;

        /// <summary>What is left after the combine. Corn stubble is knee-high and coarse; beans are flat.</summary>
        public const float CornStubbleHeight = 0.4f;
        public const float SoybeanStubbleHeight = 0.15f;

        /// <summary>Days from drilling to rows showing green.</summary>
        public const int Emergence = 21;

        /// <summary>Days the ground stands worked and black before the drill comes.</summary>
        public const int TillageLead = 14;

        // ---- which field, and what is in it ------------------------------------------------

        /// <summary>
        /// The field containing a point. Fields are not entities anywhere in this project - the
        /// countryside is terrain tiles - so identity comes from the survey grid, which is both
        /// free and correct.
        /// </summary>
        public static long KeyAt(float x, float y)
        {
            int cx = FloorDiv(x, FortyAcres), cy = FloorDiv(y, FortyAcres);
            // Splitmix-style avalanche so neighbouring cells share no low bits - CropOn and
            // RankOf both read the bottom of this and would otherwise stripe the map.
            ulong h = (ulong)(long)cx * 0x9E3779B97F4A7C15UL ^ (ulong)(long)cy * 0xC2B2AE3D27D4EB4FUL;
            h ^= h >> 33; h *= 0xFF51AFD7ED558CCDUL;
            h ^= h >> 33; h *= 0xC4CEB9FE1A85EC53UL;
            h ^= h >> 33;
            return (long)h;
        }

        private static int FloorDiv(float v, float cell)
        {
            float q = v / cell;
            int i = (int)q;
            return q < 0f && i != q ? i - 1 : i;             // (int) truncates toward zero
        }

        /// <summary>
        /// Corn or beans this year. They alternate on a field year to year, so the same ground is
        /// a different colour in consecutive Augusts and adjacent fields mostly disagree - a
        /// scatter rather than a strict chessboard, because real ownership is not interleaved.
        /// </summary>
        public static Crop CropOn(long key, int year) =>
            (((key >> 7) ^ year) & 1L) == 0L ? Crop.Corn : Crop.Soybean;

        /// <summary>
        /// This field's fixed place in the county's working order, 0 = first drilled and first
        /// cut. Stable across years on purpose: it is the same farmer with the same equipment and
        /// the same wet corner, and a map whose planting order reshuffles every spring would read
        /// as noise.
        /// </summary>
        public static float RankOf(long key) => ((key >> 11) & 0xFFFFL) / 65536f;

        // ---- the dates this field works to -------------------------------------------------

        /// <summary>Day of year this field goes in the ground.</summary>
        public static int PlantedOn(Crop crop, float rank) =>
            DayWhen(PlantedDays(crop), PlantedPercent(crop), rank);

        /// <summary>
        /// Day of year this field reaches full height - corn tasselling, beans in bloom. This is
        /// the day it becomes a barrier, and for corn it is the most important date in this file.
        /// </summary>
        public static int FullHeightOn(Crop crop, float rank) =>
            AtLeast(DayWhen(TallDays(crop), TallPercent(crop), rank), PlantedOn(crop, rank) + Emergence + 14);

        /// <summary>Day of year the colour turns - beans yellow, corn mature and browning.</summary>
        public static int TurnsOn(Crop crop, float rank) =>
            AtLeast(DayWhen(TurnDays(crop), TurnPercent(crop), rank), FullHeightOn(crop, rank) + 7);

        /// <summary>Day of year the combine reaches this field.</summary>
        public static int CutOn(Crop crop, float rank) =>
            AtLeast(DayWhen(CutDays(crop), CutPercent(crop), rank), TurnsOn(crop, rank) + 7);

        // ---- what it looks like on the day -------------------------------------------------

        /// <summary>The state of a field with this crop and this rank on this day of the year.</summary>
        public static FieldState StateOf(Crop crop, float rank, int dayOfYear)
        {
            int planted = PlantedOn(crop, rank);

            if (dayOfYear >= CutOn(crop, rank)) return FieldState.Stubble;
            if (dayOfYear >= TurnsOn(crop, rank)) return FieldState.Turning;
            if (dayOfYear >= FullHeightOn(crop, rank)) return FieldState.Standing;
            if (dayOfYear >= planted + Emergence) return FieldState.Growing;
            if (dayOfYear >= planted) return FieldState.Seedling;
            if (dayOfYear >= planted - TillageLead) return FieldState.Tilled;
            return FieldState.Stubble;                        // last year's, still standing
        }

        /// <summary>
        /// Crop height in metres. Growing ramps rather than stepping, because the six weeks in
        /// which corn goes from see-over to not is the interesting part - the barrier arrives
        /// gradually through June and July and then leaves in three weeks in October.
        /// </summary>
        public static float HeightOf(Crop crop, float rank, int dayOfYear)
        {
            float full = crop == Crop.Corn ? CornHeight : SoybeanHeight;
            switch (StateOf(crop, rank, dayOfYear))
            {
                case FieldState.Tilled: return 0f;
                case FieldState.Seedling: return full * 0.06f;
                case FieldState.Growing:
                {
                    int from = PlantedOn(crop, rank) + Emergence, to = FullHeightOn(crop, rank);
                    float t = to > from ? (dayOfYear - from) / (float)(to - from) : 1f;
                    return full * (0.06f + 0.94f * Era.Clamp01(t));
                }
                case FieldState.Standing:
                case FieldState.Turning: return full;
                default: return crop == Crop.Corn ? CornStubbleHeight : SoybeanStubbleHeight;
            }
        }

        /// <summary>Everything about the field at a point on a date, in one call.</summary>
        public static FieldCondition At(float x, float y, int year, int dayOfYear)
        {
            long key = KeyAt(x, y);
            float rank = RankOf(key);
            Crop crop = CropOn(key, year);
            return new FieldCondition(crop, StateOf(crop, rank, dayOfYear), HeightOf(crop, rank, dayOfYear));
        }

        // ---- the curves --------------------------------------------------------------------
        //
        // Percent of Illinois acreage past each stage by each date, 1990-99, from Illinois
        // Agricultural Statistics 2000. The first and last point of each curve are extrapolated
        // to 0 and 100 so the inversion has somewhere to land for the extreme ranks; every point
        // between them is tabulated. Days are common-year day-of-year.
        //
        // The half-way dates these reproduce, which are the ones to check against:
        //                       corn        beans
        //   50% planted         ~5 May      ~25 May
        //   50% tall            ~22 Jul     ~20 Jul
        //   50% turned          ~18 Sep     ~5 Sep
        //   50% harvested       ~17 Oct     ~8 Oct

        private static readonly int[] CornPlantedDays = { 100, 110, 120, 130, 140, 150, 166 };
        private static readonly float[] CornPlantedPct = { 0, 8, 30, 63, 78, 90, 100 };

        private static readonly int[] CornTallDays = { 171, 181, 191, 201, 211, 222, 232 };
        private static readonly float[] CornTallPct = { 0, 3, 16, 45, 79, 93, 100 };

        private static readonly int[] CornTurnDays = { 222, 232, 242, 253, 263, 273, 283, 293 };
        private static readonly float[] CornTurnPct = { 0, 4, 11, 28, 54, 80, 95, 100 };

        private static readonly int[] CornCutDays = { 253, 263, 273, 283, 293, 303, 314, 334, 344 };
        private static readonly float[] CornCutPct = { 0, 9, 20, 35, 56, 77, 90, 97, 100 };

        private static readonly int[] BeanPlantedDays = { 110, 120, 130, 140, 150, 161, 171, 182 };
        private static readonly float[] BeanPlantedPct = { 0, 2, 12, 36, 61, 79, 91, 100 };

        private static readonly int[] BeanTallDays = { 171, 181, 191, 201, 211, 222, 232 };
        private static readonly float[] BeanTallPct = { 0, 9, 26, 51, 75, 90, 100 };

        private static readonly int[] BeanTurnDays = { 222, 232, 242, 253, 263, 273 };
        private static readonly float[] BeanTurnPct = { 0, 6, 27, 67, 89, 100 };

        private static readonly int[] BeanCutDays = { 253, 263, 273, 283, 293, 303, 314, 324 };
        private static readonly float[] BeanCutPct = { 0, 6, 22, 55, 79, 93, 98, 100 };

        private static int[] PlantedDays(Crop c) => c == Crop.Corn ? CornPlantedDays : BeanPlantedDays;
        private static float[] PlantedPercent(Crop c) => c == Crop.Corn ? CornPlantedPct : BeanPlantedPct;
        private static int[] TallDays(Crop c) => c == Crop.Corn ? CornTallDays : BeanTallDays;
        private static float[] TallPercent(Crop c) => c == Crop.Corn ? CornTallPct : BeanTallPct;
        private static int[] TurnDays(Crop c) => c == Crop.Corn ? CornTurnDays : BeanTurnDays;
        private static float[] TurnPercent(Crop c) => c == Crop.Corn ? CornTurnPct : BeanTurnPct;
        private static int[] CutDays(Crop c) => c == Crop.Corn ? CornCutDays : BeanCutDays;
        private static float[] CutPercent(Crop c) => c == Crop.Corn ? CornCutPct : BeanCutPct;

        /// <summary>
        /// The day on which the acreage curve passes this field's rank - i.e. this field's own
        /// date for that stage. Inverting the curve rather than sampling it is the whole point:
        /// see the class remarks.
        ///
        /// The inversion itself is <see cref="Era.Crossing"/>, shared with the technology adoption
        /// curves, which are the same idea one domain over. ROUNDED TO NEAREST here, where
        /// <see cref="Adoption.YearWhen"/> takes the ceiling, and the difference is real rather
        /// than sloppy: a planting date is a point on a calendar and the nearest day is the best
        /// answer, whereas a year is a bucket you are either inside or outside.
        /// </summary>
        private static int DayWhen(int[] days, float[] percent, float rank) =>
            (int)(Era.Crossing(days, percent, Era.Clamp01(rank) * 100f) + 0.5f);

        private static int AtLeast(int value, int floor) => value < floor ? floor : value;
    }
}
