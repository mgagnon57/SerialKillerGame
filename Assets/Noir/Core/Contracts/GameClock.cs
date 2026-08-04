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
