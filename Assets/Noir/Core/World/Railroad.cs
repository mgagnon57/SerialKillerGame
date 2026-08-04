using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>Which way it is going. The line runs roughly north-east through the town.</summary>
    public enum Bound : byte { North = 0, South = 1 }

    /// <summary>What a person standing in the town can hear of a train, at one moment.</summary>
    public enum TrainSound : byte
    {
        /// <summary>Nothing. Most of the time.</summary>
        None = 0,
        /// <summary>Crossing bells. They start before the horn and outlast the train.</summary>
        Bells = 1,
        /// <summary>Two long, one short, one long - over the bells, a quarter mile out.</summary>
        Horn = 2,
        /// <summary>The train itself, going through. Loud enough to cover almost anything.</summary>
        Passing = 3,
    }

    /// <summary>One freight, going through town.</summary>
    public readonly struct TrainPass
    {
        /// <summary>Second of the day at which the locomotive reaches the town's crossing.</summary>
        public readonly int HeadSecond;
        public readonly Bound Bound;
        public readonly int Cars;
        /// <summary>Seconds from the locomotive reaching the crossing until the tail clears it.</summary>
        public readonly int SecondsToPass;

        public TrainPass(int headSecond, Bound bound, int cars, int secondsToPass)
        {
            HeadSecond = headSecond; Bound = bound; Cars = cars; SecondsToPass = secondsToPass;
        }

        public int MinuteOfDay => HeadSecond / 60;
        public int TailSecond => HeadSecond + SecondsToPass;

        /// <summary>First and last second at which any of this train is audible in town.</summary>
        public int FirstSecond => HeadSecond - Railroad.BellsLead;
        public int LastSecond => TailSecond + Railroad.BellsTrail;

        public TrainSound SoundAt(int secondOfDay)
        {
            if (secondOfDay < FirstSecond || secondOfDay > LastSecond) return TrainSound.None;
            if (secondOfDay >= HeadSecond && secondOfDay < TailSecond) return TrainSound.Passing;
            if (secondOfDay >= HeadSecond - Railroad.HornLead && secondOfDay < HeadSecond) return TrainSound.Horn;
            return TrainSound.Bells;
        }

        /// <summary>Gates are down. Nothing crosses the tracks, on foot or otherwise.</summary>
        public bool BlocksCrossing(int secondOfDay) =>
            secondOfDay >= FirstSecond && secondOfDay <= LastSecond;

        public override string ToString() =>
            $"{MinuteOfDay / 60:00}:{MinuteOfDay % 60:00} {Bound}bound, {Cars} cars, {SecondsToPass}s";
    }

    /// <summary>
    /// The railroad, which is the town's metronome.
    ///
    /// ABOUT FIFTEEN TRAINS A DAY, AROUND THE CLOCK - one every hundred minutes or so, which means
    /// a train is never far away and the nights are punctuated rather than silent. The line is
    /// CSX's Woodland Subdivision and Rossville sits on it at milepost 105.2. Passenger service
    /// ended in 1971, so every one of these is freight: long, slow, and as likely at three in the
    /// morning as at three in the afternoon.
    ///
    /// WHY THIS IS IN CORE AND NOT IN THE AUDIO. A train is four things at once, and three of them
    /// are simulation: it masks sound for a minute or two, it puts the gates down and stops anyone
    /// crossing the tracks, and IT IS A TIMESTAMP. "I heard the freight go through" is the most
    /// natural thing a witness in this town can say about when something happened, and it is worth
    /// having only if the schedule is real and the same on every replay.
    ///
    /// THE SCHEDULE IS IRREGULAR ON PURPOSE. Freight does not run to a passenger timetable; it
    /// runs when it runs. So the day is cut into as many slots as there are trains and each train
    /// falls somewhere inside its own slot - which keeps the daily count and the hundred-minute
    /// average honest while giving the bunching and the long empty stretches that a fixed interval
    /// would not. Two can arrive twenty minutes apart and then nothing for three hours.
    ///
    /// WHAT IS SOURCED AND WHAT IS NOT. Fifteen a day is sourced, but it is a MODERN count taken
    /// where the subdivision enters Danville, fifteen miles south - not a 1990s Rossville count.
    /// The horn pattern - two long, one short, one long, begun fifteen to twenty seconds out and
    /// no more than a quarter mile - was long-standing railroad practice by the game's period, but
    /// the federal rule everybody cites for it is 49 CFR 222 and that only arrives around 2005.
    /// The sound is right for the era; the citation is not. TRAIN LENGTH AND SPEED ARE NOT SOURCED
    /// AT ALL: the ranges below are ordinary Midwest through-freight figures and should be treated
    /// as placeholder if anything ever comes to depend on them closely.
    /// </summary>
    public static class Railroad
    {
        /// <summary>Rossville's position on the Woodland Subdivision.</summary>
        public const float Milepost = 105.2f;

        /// <summary>Trains a day. Fifteen on average, with the day-to-day wobble any line has.</summary>
        public const int FewestPerDay = 13;
        public const int MostPerDay = 17;

        /// <summary>Seconds before the locomotive that the crossing bells start.</summary>
        public const int BellsLead = 30;

        /// <summary>Seconds after the tail clears that they stop.</summary>
        public const int BellsTrail = 10;

        /// <summary>
        /// Seconds before the crossing that the horn begins - 49 CFR 222 says fifteen to twenty,
        /// and says it for a later decade than this game. Twenty, because the pattern has to fit.
        /// </summary>
        public const int HornLead = 20;

        // Not sourced. Ordinary through-freight figures: a mixed manifest of forty to a hundred
        // and thirty cars at fifty to sixty feet each, doing thirty-five to fifty miles an hour.
        private const int FewestCars = 40;
        private const int MostCars = 130;
        private const float CarMetres = 17.5f;
        private const float LocomotiveMetres = 60f;         // two or three units on the head end
        private const float SlowestMetresPerSecond = 15.6f; // 35 mph
        private const float FastestMetresPerSecond = 22.4f; // 50 mph

        private static readonly ulong Purpose = Rolls.Purpose("railroad.timetable");

        /// <summary>
        /// The day's trains, in order. Deterministic in the seed and the day and nothing else -
        /// no stream position, so this answers the same whether it is asked once at midnight or
        /// afresh every tick by whoever happens to be listening.
        /// </summary>
        public static TrainPass[] OnDay(ulong seed, int day)
        {
            int count = FewestPerDay +
                        Rolls.Int(seed, Purpose, 0UL, day, 1UL, MostPerDay - FewestPerDay + 1);

            var passes = new TrainPass[count];
            int slot = 86400 / count;

            for (int i = 0; i < count; i++)
            {
                ulong subject = (ulong)i;
                int head = i * slot + Rolls.Int(seed, Purpose, subject, day, 2UL, slot);
                var bound = Rolls.Int(seed, Purpose, subject, day, 3UL, 2) == 0 ? Bound.North : Bound.South;
                int cars = FewestCars + Rolls.Int(seed, Purpose, subject, day, 4UL, MostCars - FewestCars + 1);

                float length = LocomotiveMetres + cars * CarMetres;
                float speed = SlowestMetresPerSecond +
                              Rolls.Unit(seed, Purpose, subject, day, 5UL) *
                              (FastestMetresPerSecond - SlowestMetresPerSecond);

                passes[i] = new TrainPass(head, bound, cars, (int)(length / speed + 0.5f));
            }

            return passes;
        }

        /// <summary>What can be heard right now, and from which train.</summary>
        public static TrainSound SoundAt(ulong seed, GameClock clock, out TrainPass train)
        {
            int second = SecondOfDay(clock);
            var today = OnDay(seed, clock.Day);

            for (int i = 0; i < today.Length; i++)
            {
                var sound = today[i].SoundAt(second);
                if (sound != TrainSound.None) { train = today[i]; return sound; }
            }

            // A train that started before midnight is still going through at 00:00:30.
            var yesterday = OnDay(seed, clock.Day - 1);
            for (int i = yesterday.Length - 1; i >= 0; i--)
            {
                var sound = yesterday[i].SoundAt(second + 86400);
                if (sound != TrainSound.None) { train = yesterday[i]; return sound; }
            }

            train = default;
            return TrainSound.None;
        }

        /// <summary>
        /// True while a train is loud enough to cover ordinary sound - a shout, a car door, a
        /// scream. One to three minutes at a time, fifteen times a day.
        /// </summary>
        public static bool MasksSound(ulong seed, GameClock clock) =>
            SoundAt(seed, clock, out _) == TrainSound.Passing;

        /// <summary>True while the gates are down and the tracks cannot be crossed.</summary>
        public static bool CrossingBlocked(ulong seed, GameClock clock) =>
            SoundAt(seed, clock, out _) != TrainSound.None;

        /// <summary>
        /// The next train after this moment, and how many seconds away it is. Returns the first of
        /// tomorrow's if the day is spent. This is the "when is the next one" a person waiting at
        /// the crossing would know by heart.
        /// </summary>
        public static TrainPass NextAfter(ulong seed, GameClock clock, out int secondsAway)
        {
            int second = SecondOfDay(clock);

            var today = OnDay(seed, clock.Day);
            for (int i = 0; i < today.Length; i++)
                if (today[i].HeadSecond > second)
                {
                    secondsAway = today[i].HeadSecond - second;
                    return today[i];
                }

            var tomorrow = OnDay(seed, clock.Day + 1);
            secondsAway = tomorrow[0].HeadSecond + (86400 - second);
            return tomorrow[0];
        }

        /// <summary>Seconds since midnight. The clock counts minutes; horns happen inside one.</summary>
        public static int SecondOfDay(GameClock clock) =>
            (int)(clock.Tick % GameClock.TicksPerDay / GameClock.TicksPerSecond);
    }
}
