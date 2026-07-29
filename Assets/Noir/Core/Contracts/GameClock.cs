using System;

namespace Noir.Core.Contracts
{
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

        public static readonly string[] DayNames =
            { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        public override string ToString() =>
            $"{DayNames[DayOfWeek]} d{Day} {HourOfDay:00}:{MinuteOfHour:00}";
    }
}
