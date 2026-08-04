using System;
using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The freight line.
    ///
    /// The thing being protected here is that a train is a TIMESTAMP. "I heard the freight go
    /// through" is the most natural statement a witness in this town can make about when something
    /// happened, and it is worth nothing unless the same seed puts the same train through at the
    /// same second on every replay, however and whenever it is asked.
    /// </summary>
    [TestFixture]
    public class RailroadTests
    {
        private const ulong Seed = 0x0551E11EUL;

        [Test]
        public void FifteenADayAroundTheClock()
        {
            // "About fifteen trains a day... roughly one every hundred minutes, around the clock."
            var counts = Enumerable.Range(0, 400).Select(d => Railroad.OnDay(Seed, d).Length).ToList();

            Assert.That(counts.Average(), Is.EqualTo(15.0).Within(0.5), "fifteen a day on average");
            Assert.That(counts.Min(), Is.GreaterThanOrEqualTo(Railroad.FewestPerDay));
            Assert.That(counts.Max(), Is.LessThanOrEqualTo(Railroad.MostPerDay));

            // Around the clock, not just in daylight - the night hours must carry their share.
            var byHour = new int[24];
            for (int d = 0; d < 400; d++)
                foreach (var t in Railroad.OnDay(Seed, d))
                    byHour[t.HeadSecond / 3600]++;

            int total = byHour.Sum();
            for (int h = 0; h < 24; h++)
                Assert.That(byHour[h] / (double)total, Is.EqualTo(1 / 24.0).Within(0.02),
                            $"hour {h:00} carries its share - freight does not sleep");
        }

        [Test]
        public void TheScheduleIsIrregularRatherThanClockwork()
        {
            // A fixed hundred-minute interval would be wrong in a way that is instantly audible:
            // real freight bunches and then leaves long holes. The average gap must be about a
            // hundred minutes, and the spread around it must be wide.
            var gaps = new System.Collections.Generic.List<int>();
            for (int d = 0; d < 200; d++)
            {
                var day = Railroad.OnDay(Seed, d);
                for (int i = 1; i < day.Length; i++)
                    gaps.Add((day[i].HeadSecond - day[i - 1].HeadSecond) / 60);
            }

            Assert.That(gaps.Average(), Is.EqualTo(100).Within(12), "one every hundred minutes or so");
            Assert.That(gaps.Min(), Is.LessThan(20), "two can come almost together");
            Assert.That(gaps.Max(), Is.GreaterThan(150), "and then leave a long hole");
        }

        [Test]
        public void TheDayIsOrderedAndInsideTheDay()
        {
            for (int d = 0; d < 200; d++)
            {
                var day = Railroad.OnDay(Seed, d);
                for (int i = 0; i < day.Length; i++)
                {
                    Assert.That(day[i].HeadSecond, Is.InRange(0, 86399), $"day {d} train {i}");
                    if (i > 0)
                        Assert.That(day[i].HeadSecond, Is.GreaterThan(day[i - 1].HeadSecond),
                                    $"day {d} is out of order at {i}");
                }
            }
        }

        [Test]
        public void TheSameSeedAndDayAlwaysGiveTheSameTrains()
        {
            // The whole point. Rolls.Bits takes no stream position, so this holds no matter who
            // asks or in what order - which is the failure mode a shared RNG stream would have.
            for (int d = 0; d < 50; d++)
            {
                var once = Railroad.OnDay(Seed, d);
                var again = Railroad.OnDay(Seed, d);
                Assert.That(once.Length, Is.EqualTo(again.Length));
                for (int i = 0; i < once.Length; i++)
                {
                    Assert.That(once[i].HeadSecond, Is.EqualTo(again[i].HeadSecond));
                    Assert.That(once[i].Cars, Is.EqualTo(again[i].Cars));
                    Assert.That(once[i].Bound, Is.EqualTo(again[i].Bound));
                    Assert.That(once[i].SecondsToPass, Is.EqualTo(again[i].SecondsToPass));
                }
            }

            // A different seed is a different railroad.
            Assert.That(Railroad.OnDay(Seed, 7)[0].HeadSecond,
                        Is.Not.EqualTo(Railroad.OnDay(Seed ^ 1UL, 7)[0].HeadSecond));

            // And a different day is a different day.
            Assert.That(Railroad.OnDay(Seed, 7)[0].HeadSecond,
                        Is.Not.EqualTo(Railroad.OnDay(Seed, 8)[0].HeadSecond));
        }

        [Test]
        public void ATrainIsASequenceAndNotAnEvent()
        {
            // "a single train is a sequence - distant horn, bells, the horn twice more as it takes
            // the next crossings, the train itself, then bells stopping."
            var train = Railroad.OnDay(Seed, 3)[4];

            Assert.That(train.SoundAt(train.FirstSecond - 1), Is.EqualTo(TrainSound.None));
            Assert.That(train.SoundAt(train.FirstSecond), Is.EqualTo(TrainSound.Bells), "bells first");
            Assert.That(train.SoundAt(train.HeadSecond - Railroad.HornLead), Is.EqualTo(TrainSound.Horn),
                        "horn twenty seconds out, a quarter mile away");
            Assert.That(train.SoundAt(train.HeadSecond - 1), Is.EqualTo(TrainSound.Horn));
            Assert.That(train.SoundAt(train.HeadSecond), Is.EqualTo(TrainSound.Passing));
            Assert.That(train.SoundAt(train.TailSecond - 1), Is.EqualTo(TrainSound.Passing));
            Assert.That(train.SoundAt(train.TailSecond), Is.EqualTo(TrainSound.Bells), "bells outlast it");
            Assert.That(train.SoundAt(train.LastSecond), Is.EqualTo(TrainSound.Bells));
            Assert.That(train.SoundAt(train.LastSecond + 1), Is.EqualTo(TrainSound.None));

            // The bells are down for the whole of it, which is what stops people crossing.
            for (int s = train.FirstSecond; s <= train.LastSecond; s++)
                Assert.That(train.BlocksCrossing(s), Is.True, $"gates at {s}");
            Assert.That(train.BlocksCrossing(train.FirstSecond - 1), Is.False);
            Assert.That(train.BlocksCrossing(train.LastSecond + 1), Is.False);
        }

        [Test]
        public void AFreightTakesOneToThreeMinutesToGoThrough()
        {
            // Forty to a hundred and thirty cars at thirty-five to fifty miles an hour. NOT
            // SOURCED - these are ordinary Midwest through-freight figures, and this test exists
            // to keep them ordinary rather than to claim they are Rossville's.
            var all = Enumerable.Range(0, 200).SelectMany(d => Railroad.OnDay(Seed, d)).ToList();

            Assert.That(all.Min(t => t.SecondsToPass), Is.GreaterThan(30), "even a short one is not instant");
            Assert.That(all.Max(t => t.SecondsToPass), Is.LessThan(180), "and a long one is under three minutes");
            Assert.That(all.Average(t => t.SecondsToPass), Is.InRange(60, 110));
            Assert.That(all.Min(t => t.Cars), Is.GreaterThanOrEqualTo(40));
            Assert.That(all.Max(t => t.Cars), Is.LessThanOrEqualTo(130));

            // Both directions, roughly evenly - this is a through route, not a branch.
            double north = all.Count(t => t.Bound == Bound.North) / (double)all.Count;
            Assert.That(north, Is.EqualTo(0.5).Within(0.05));
        }

        [Test]
        public void SoundMasksAboutTwentyMinutesOfTheDay()
        {
            // Fifteen trains at a minute and a half each. That is a small fraction of the day, and
            // it should stay small: if this ever crept up to hours, "the train covered it" would
            // stop being a specific opportunity and become the default state of the town.
            int masked = 0;
            var day = Railroad.OnDay(Seed, 11);
            foreach (var t in day) masked += t.SecondsToPass;

            Assert.That(masked / 60.0, Is.InRange(12, 35), "twenty-odd minutes of cover in a day");
            Assert.That(masked / 86400.0, Is.LessThan(0.03));
        }

        [Test]
        public void TheClockAndTheTimetableAgree()
        {
            var day = Railroad.OnDay(Seed, 100);
            var train = day[6];

            // Sitting the clock exactly on a passing train must hear it.
            var passing = new GameClock(
                (long)100 * GameClock.TicksPerDay + (long)(train.HeadSecond + 5) * GameClock.TicksPerSecond);
            Assert.That(Railroad.SoundAt(Seed, passing, out var heard), Is.EqualTo(TrainSound.Passing));
            Assert.That(heard.HeadSecond, Is.EqualTo(train.HeadSecond));
            Assert.That(Railroad.MasksSound(Seed, passing), Is.True);
            Assert.That(Railroad.CrossingBlocked(Seed, passing), Is.True);

            // And a moment chosen to be between trains must hear nothing.
            int quiet = (train.LastSecond + day[7].FirstSecond) / 2;
            var between = new GameClock(
                (long)100 * GameClock.TicksPerDay + (long)quiet * GameClock.TicksPerSecond);
            Assert.That(Railroad.SoundAt(Seed, between, out _), Is.EqualTo(TrainSound.None));
            Assert.That(Railroad.MasksSound(Seed, between), Is.False);

            // The next one along is the next one along.
            var next = Railroad.NextAfter(Seed, between, out int away);
            Assert.That(next.HeadSecond, Is.EqualTo(day[7].HeadSecond));
            Assert.That(away, Is.EqualTo(day[7].HeadSecond - quiet));
        }

        [Test]
        public void ATrainRunningOverMidnightIsStillHeardAfterIt()
        {
            // The last train of a day can start before midnight and still be going through at
            // 00:00:30. Looking only at today's list would drop it, and the gap would be silent
            // in a way nothing else here would notice.
            int day = FindDayWithALateTrain(out TrainPass late);
            Assert.That(late.LastSecond, Is.GreaterThan(86400), "the setup found what it was looking for");

            int overflow = late.LastSecond - 86400;
            var justAfterMidnight = new GameClock(
                (long)(day + 1) * GameClock.TicksPerDay + (long)(overflow / 2) * GameClock.TicksPerSecond);

            Assert.That(Railroad.SoundAt(Seed, justAfterMidnight, out var heard),
                        Is.Not.EqualTo(TrainSound.None), "yesterday's train is still going through");
            Assert.That(heard.HeadSecond, Is.EqualTo(late.HeadSecond));
        }

        /// <summary>
        /// Prints a day's freight against the light. NOT AN ASSERTION - a way of looking at the
        /// two systems together, which is the only place their point shows:
        ///
        ///     dotnet test -c Release tools/Noir.Core.Tests --filter "Name=PrintADayOfTrains" ^
        ///                 -l "console;verbosity=detailed"
        ///
        /// What it shows: the same fifteen-odd trains run every day of the year, but the number
        /// of them that go through IN THE DARK swings from about five in June to about ten in
        /// December. Neither Railroad nor Daylight knows that; it only exists where they meet.
        ///
        /// Both clocks are printed because they disagree in summer. STD is what the simulation
        /// counts and is always in order; WALL is what a person in the town would say, and a
        /// train just before midnight on a summer night reads as the small hours of the NEXT day.
        /// </summary>
        [Test, Explicit("Diagnostic - prints a day of freight against the light, asserts nothing")]
        public void PrintADayOfTrains()
        {
            foreach (var (month, day) in new[] { (6, 21), (10, 15), (12, 21) })
            {
                var noon = new GameClock(GameClock.TickOn(1991, month, day, 12 * 60));
                Console.WriteLine($"\n=== {noon.Date} ===  sunrise {Wall(noon.Sunrise, noon)}  " +
                                  $"sunset {Wall(noon.Sunset, noon)}  " +
                                  $"dark {Wall(noon.Dusk, noon)} to {Wall(noon.Dawn, noon)}" +
                                  (noon.IsDaylightSaving ? "  (daylight time)" : "  (standard time)"));
                Console.WriteLine("     STD    WALL   bound  cars  clears  light");

                int inTheDark = 0;
                foreach (var train in Railroad.OnDay(Seed, noon.Day))
                {
                    var at = new GameClock(GameClock.TickOn(1991, month, day, train.MinuteOfDay));
                    if (at.IsDark) inTheDark++;
                    Console.WriteLine($"   {train.MinuteOfDay / 60:00}:{train.MinuteOfDay % 60:00}  " +
                                      $"{Wall(train.MinuteOfDay, at)}  {train.Bound,-6} {train.Cars,4}  " +
                                      $"{train.SecondsToPass,4}s  {at.Light,-7}{(at.IsDark ? " IN THE DARK" : "")}");
                }
                Console.WriteLine($"   -> {inTheDark} of them in the dark");
            }
        }

        private static string Wall(int standardMinute, GameClock on)
        {
            int w = (standardMinute + (on.IsDaylightSaving ? 60 : 0)) % 1440;
            return $"{w / 60:00}:{w % 60:00}";
        }

        private static int FindDayWithALateTrain(out TrainPass late)
        {
            for (int d = 0; d < 2000; d++)
            {
                var day = Railroad.OnDay(Seed, d);
                var last = day[day.Length - 1];
                if (last.LastSecond > 86400 + 20) { late = last; return d; }
            }
            throw new InvalidOperationException("no train crossed midnight in two thousand days");
        }
    }
}
