using System;

namespace Noir.Core.Contracts
{
    /// <summary>Meteorological season - whole months, which is how the crop research is written.</summary>
    public enum Season : byte { Winter = 0, Spring, Summer, Autumn }

    /// <summary>
    /// The simulation clock. Fixed step, integer ticks, no floating-point accumulation —
    /// so a given tick index always means exactly the same moment, on any machine, forever.
    ///
    /// Speed is NOT a property of the clock. Running faster means the host calls Tick() more
    /// times per real second; the timestep itself never changes. That is what keeps movement,
    /// collision and determinism identical at 1x and at 600x.
    /// </summary>
    public readonly struct GameClock
    {
        /// <summary>Simulation steps per game second. 20 Hz: fine enough for walking, cheap enough for thousands of ticks per frame when fast-forwarding.</summary>
        public const int TicksPerSecond = 20;
        public const int TicksPerMinute = TicksPerSecond * 60;          // 1,200
        public const int TicksPerHour = TicksPerMinute * 60;            // 72,000
        public const int TicksPerDay = TicksPerHour * 24;               // 1,728,000

        /// <summary>Ticks elapsed since the start of the simulation.</summary>
        public readonly long Tick;

        public GameClock(long tick) { Tick = tick; }

        public GameClock Advance(int ticks) => new GameClock(Tick + ticks);

        /// <summary>Whole days elapsed. Day 0 is the first day.</summary>
        public int Day => (int)(Tick / TicksPerDay);

        /// <summary>Minutes since midnight, 0..1439. The unit schedules are written in.</summary>
        public int MinuteOfDay => (int)(Tick % TicksPerDay / TicksPerMinute);

        public int HourOfDay => MinuteOfDay / 60;
        public int MinuteOfHour => MinuteOfDay % 60;

        /// <summary>Day of week, 0 = Monday. Day 0 of the simulation is a Monday.</summary>
        public int DayOfWeek => Day % 7;

        public bool IsWeekend => DayOfWeek >= 5;

        /// <summary>Fractional progress through the current minute, for smooth interpolation.</summary>
        public float FractionOfMinute => (Tick % TicksPerMinute) / (float)TicksPerMinute;

        /// <summary>The tick at which the given day and minute-of-day occurs.</summary>
        public static long TickAt(int day, int minuteOfDay) =>
            (long)day * TicksPerDay + (long)minuteOfDay * TicksPerMinute;

        /// <summary>Minutes from now until the next occurrence of the given minute-of-day.</summary>
        public int MinutesUntil(int targetMinuteOfDay)
        {
            int now = MinuteOfDay;
            int delta = targetMinuteOfDay - now;
            return delta >= 0 ? delta : delta + 1440;
        }


        // ---- the calendar ----------------------------------------------------------------
        //
        // THE GAME OPENS IN 1991 AND RUNS FORWARD, through the February 2004 downtown fire and
        // the 2006 high-school closure - things the player watches happen. Until now this clock
        // had no calendar at all: Day was a counter from zero and there was no year, month or
        // season anywhere in the simulation. Three separate bodies of research are downstream of
        // fixing that, and none of them could be built without it:
        //
        //   THE-YEAR.md          crops are a SEQUENCE, not a texture - needs day-of-year
        //   THE-TRAJECTORY.md    the town declines on a dated calendar 1991-2006 - needs the year
        //   WHAT-IT-SOUNDS-LIKE  grain dryers are October; Brood X cicadas are 2004 - needs both
        //
        // THE EPOCH IS A MONDAY, AND THAT IS NOT A STYLE CHOICE. DayOfWeek is Day % 7 and
        // OpenWindow masks bit 0 as Monday, so every opening hour in the content depends on day
        // zero being a Monday. 1 January 1991 was a TUESDAY. Anchoring there would have shifted
        // every business's opening days by one and broken nothing loudly. The first Monday of
        // 1991 is the 7th, so that is the epoch: Day 0 is Monday 7 January 1991, DayOfWeek keeps
        // its meaning exactly, and no existing behaviour moves.

        public const int EpochYear = 1991;
        public const int EpochMonth = 1;
        public const int EpochDayOfMonth = 7;          // a Monday - see above

        private static readonly int EpochDays = DaysFromCivil(EpochYear, EpochMonth, EpochDayOfMonth);

        /// <summary>The calendar year. 1991 on the opening day.</summary>
        public int Year { get { Civil(out int y, out _, out _); return y; } }

        /// <summary>Calendar month, 1..12.</summary>
        public int Month { get { Civil(out _, out int m, out _); return m; } }

        /// <summary>Day of the month, 1..31.</summary>
        public int DayOfMonth { get { Civil(out _, out _, out int d); return d; } }

        /// <summary>
        /// Day of the year, 1..366. This is the one the crop calendar is written against:
        /// THE-YEAR.md gives corn 50% planted at ~5 May and 50% harvested at ~17 October, and
        /// those are dates, not months.
        /// </summary>
        public int DayOfYear
        {
            get
            {
                Civil(out int y, out _, out _);
                return EpochDays + Day - DaysFromCivil(y, 1, 1) + 1;
            }
        }

        /// <summary>True in a leap year, so day-of-year arithmetic can be checked against it.</summary>
        public bool IsLeapYear
        {
            get { Civil(out int y, out _, out _); return (y % 4 == 0 && y % 100 != 0) || y % 400 == 0; }
        }

        /// <summary>
        /// Meteorological season, which is the one that matches how the research is written -
        /// whole months, not solstices. Winter is Dec-Feb, and in this county that is a third of
        /// the year of bare stubble with snow cover only in the Dec-Mar window.
        /// </summary>
        public Season Season
        {
            get
            {
                Civil(out _, out int m, out _);
                if (m <= 2 || m == 12) return Season.Winter;
                if (m <= 5) return Season.Spring;
                if (m <= 8) return Season.Summer;
                return Season.Autumn;
            }
        }


        // ---- daylight --------------------------------------------------------------------
        //
        // THE CLOCK RUNS IN LOCAL STANDARD TIME - CST - ALL YEAR, AND THIS IS LOAD-BEARING.
        // Tick is a monotonic counter and Day is Tick / TicksPerDay, so every day must be exactly
        // 1440 minutes. If the simulation itself sprang forward in April, one day would be 1380
        // minutes and one in October 1500, and DayOfWeek, TickOn and every OpenWindow in the
        // content would slide by an hour twice a year. So daylight saving never moves the
        // simulation. It is a fact ABOUT the date that presentation adds on top - see
        // IsDaylightSaving and WallClockMinute - and the daylight table is in standard time to
        // match, so IsDark can compare against MinuteOfDay directly with no conversion at all.

        /// <summary>Sunrise today, in minutes after midnight, local standard time.</summary>
        public int Sunrise { get { Civil(out _, out int m, out int d); return Daylight.Sunrise(m, d); } }

        /// <summary>Sunset today, in minutes after midnight, local standard time.</summary>
        public int Sunset { get { Civil(out _, out int m, out int d); return Daylight.Sunset(m, d); } }

        /// <summary>Start of civil twilight today, local standard time.</summary>
        public int Dawn { get { Civil(out _, out int m, out int d); return Daylight.Dawn(m, d); } }

        /// <summary>End of civil twilight today, local standard time.</summary>
        public int Dusk { get { Civil(out _, out int m, out int d); return Daylight.Dusk(m, d); } }

        /// <summary>Minutes of sun today. 557 at the winter solstice, 904 at the summer one.</summary>
        public int DaylightMinutes { get { Civil(out _, out int m, out int d); return Daylight.Length(m, d); } }

        /// <summary>How light it is outside right now.</summary>
        public Light Light
        {
            get { Civil(out _, out int m, out int d); return Daylight.At(m, d, MinuteOfDay); }
        }

        /// <summary>
        /// True below civil twilight - the sun more than 6 degrees down, and too dark to
        /// recognise a face across a street without a lamp. THIS IS THE WITNESS GATE. Twilight
        /// deliberately counts as light: WHO-SEES-WHOM.md's whole point is that the margins are
        /// where it gets interesting, and "she thought it was him" is a different testimony from
        /// "she saw nothing".
        /// </summary>
        public bool IsDark => Light == Light.Night;

        // ---- daylight saving -------------------------------------------------------------
        //
        // THE PERIOD RULE, NOT THE ONE YOU REMEMBER. The US moved to the second Sunday in March
        // in 2007. This game ends in 2006. Every year it covers - 1991 to 2006 - runs under the
        // Energy Policy Act's predecessor: FIRST SUNDAY IN APRIL to LAST SUNDAY IN OCTOBER. In
        // 1991 that is 7 April to 27 October, so the last week of March is standard time, not
        // daylight time.
        //
        // This is not pedantry, and it caught a real error: THE-YEAR.md's daylight table gives
        // 21 March as 06:54/19:03, an hour later than the sun is actually up, because it was
        // computed with the modern rule. Seven of its eight rows are right to the minute; that
        // one is an era mismatch. Daylight.cs carries the note.

        /// <summary>Day of the month of the first Sunday in April - the year's DST changeover.</summary>
        public static int FirstSundayOfApril(int year)
        {
            int w = WeekdayOf(DaysFromCivil(year, 4, 1));        // 0 = Sunday
            return 1 + (7 - w) % 7;
        }

        /// <summary>Day of the month of the last Sunday in October - when the clocks go back.</summary>
        public static int LastSundayOfOctober(int year) => 31 - WeekdayOf(DaysFromCivil(year, 10, 31));

        /// <summary>Weekday of a days-since-1970 count, 0 = Sunday. 1970-01-01 was a Thursday.</summary>
        private static int WeekdayOf(int z) => (z + 4) % 7;

        /// <summary>
        /// Whether the town's clocks are an hour forward right now. The simulation is unaffected;
        /// this is what the kitchen clock says. Spring forward is 02:00 standard on the April
        /// Sunday; fall back is 02:00 daylight, which is 01:00 standard, on the October one.
        /// </summary>
        public bool IsDaylightSaving
        {
            get
            {
                Civil(out int y, out int m, out int d);
                int today = DaysFromCivil(y, m, d);
                int begins = DaysFromCivil(y, 4, FirstSundayOfApril(y));
                int ends = DaysFromCivil(y, 10, LastSundayOfOctober(y));

                if (today < begins || today > ends) return false;
                if (today == begins) return MinuteOfDay >= 120;      // 02:00 standard -> 03:00 daylight
                if (today == ends) return MinuteOfDay < 60;          // 02:00 daylight -> 01:00 standard
                return true;
            }
        }

        /// <summary>
        /// Minute of day as a clock on the wall shows it - standard time plus an hour in summer.
        /// Wraps at midnight, so on a summer night 23:30 standard reads 00:30 and belongs to the
        /// NEXT calendar day. Display code that shows a date beside this has to account for that;
        /// nothing in the simulation does, because nothing in the simulation uses this.
        /// </summary>
        public int WallClockMinute => (MinuteOfDay + (IsDaylightSaving ? 60 : 0)) % 1440;

        /// <summary>The wall clock, as a person in the town would read it out.</summary>
        public string WallClock => $"{WallClockMinute / 60:00}:{WallClockMinute % 60:00}";

        private void Civil(out int y, out int m, out int d) => CivilFromDays(EpochDays + Day, out y, out m, out d);

        /// <summary>
        /// Days since 1970-01-01 for a civil date, and back again. Howard Hinnant's algorithms,
        /// which are exact integer arithmetic over the proleptic Gregorian calendar - no
        /// floating point, no DateTime, and identical on every machine. Core may not use
        /// System.DateTime for the same reason it may not use Math.Sin: the answer has to be
        /// bit-identical forever or replay stops working.
        /// </summary>
        public static int DaysFromCivil(int y, int m, int d)
        {
            y -= m <= 2 ? 1 : 0;
            int era = (y >= 0 ? y : y - 399) / 400;
            int yoe = y - era * 400;                                        // [0, 399]
            int doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;       // [0, 365]
            int doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;                // [0, 146096]
            return era * 146097 + doe - 719468;
        }

        public static void CivilFromDays(int z, out int y, out int m, out int d)
        {
            z += 719468;
            int era = (z >= 0 ? z : z - 146096) / 146097;
            int doe = z - era * 146097;                                     // [0, 146096]
            int yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365; // [0, 399]
            int doy = doe - (365 * yoe + yoe / 4 - yoe / 100);              // [0, 365]
            int mp = (5 * doy + 2) / 153;                                   // [0, 11]
            d = doy - (153 * mp + 2) / 5 + 1;                               // [1, 31]
            m = mp + (mp < 10 ? 3 : -9);                                    // [1, 12]
            y = yoe + era * 400 + (m <= 2 ? 1 : 0);
        }

        /// <summary>The tick on which a given calendar date begins.</summary>
        public static long TickOn(int year, int month, int day, int minuteOfDay = 0) =>
            TickAt(DaysFromCivil(year, month, day) - EpochDays, minuteOfDay);

        public string Date => $"{DayNames[DayOfWeek]} {DayOfMonth:00} {MonthNames[Month - 1]} {Year}";

        public static readonly string[] MonthNames =
            { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        public static readonly string[] DayNames =
            { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        public override string ToString() => $"{Date} {HourOfDay:00}:{MinuteOfHour:00}";
    }
}
