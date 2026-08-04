using System;
using NUnit.Framework;
using Noir.Core.Contracts;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Sunrise, sunset and darkness.
    ///
    /// The embedded table came out of the NOAA solar algorithm run offline, because Core may not
    /// call a transcendental. That means nothing at runtime can re-derive it, so these tests are
    /// the only thing standing between a mistyped digit and a town where the sun sets at the wrong
    /// hour for a fortnight. They check the table three ways: against the independently computed
    /// figures in THE-YEAR.md, against its own internal shape, and against the two consequences
    /// WHO-SEES-WHOM.md draws from it.
    /// </summary>
    [TestFixture]
    public class DaylightTests
    {
        /// <summary>Minutes after midnight, so the expected values read like clock times.</summary>
        private static int At(int hour, int minute) => hour * 60 + minute;

        [Test]
        public void TheResearchTableReproducesToTheMinute()
        {
            // THE-YEAR.md, "Daylight, computed for 40.3793 N, -87.66897 W". Its clock times are
            // local - CST in winter, CDT in summer - so the summer rows are converted back to
            // standard time here, which is what the table holds.
            //
            // 21 MARCH IS DELIBERATELY ABSENT. See TheResearchTablesMarchRowUsesTheWrongDstEra.
            var rows = new[]
            {
                //  m   d   sunrise      sunset       daylight-saving in force on that date?
                (  1,  1, At( 7, 13), At(16, 34), false),   //  9 h 20
                (  5,  1, At( 5, 51), At(19, 44), true ),   // 13 h 53
                (  6, 21, At( 5, 20), At(20, 24), true ),   // 15 h 04 - the longest
                (  8,  1, At( 5, 47), At(20,  7), true ),   // 14 h 20
                (  9, 22, At( 6, 37), At(18, 50), true ),   // 12 h 13
                ( 11,  1, At( 6, 19), At(16, 49), false),   // 10 h 30
                ( 12, 21, At( 7, 10), At(16, 27), false),   //  9 h 17 - the shortest
            };

            foreach (var (month, day, rise, set, dst) in rows)
            {
                int shift = dst ? 60 : 0;
                Assert.That(Daylight.Sunrise(month, day) + shift, Is.EqualTo(rise).Within(1),
                            $"sunrise on {day:00}/{month:00}");
                Assert.That(Daylight.Sunset(month, day) + shift, Is.EqualTo(set).Within(1),
                            $"sunset on {day:00}/{month:00}");
            }
        }

        [Test]
        public void TheResearchTablesMarchRowUsesTheWrongDstEra()
        {
            // THE-YEAR.md gives 21 March as 06:54 / 19:03. That is daylight time - but the US did
            // not go onto daylight time until the FIRST SUNDAY IN APRIL until 2007, and this game
            // ends in 2006. On 21 March 1991 the clocks were still on CST.
            //
            // So the document's figures are an hour late for the game's era, and the table here is
            // right to keep standard time. This test pins the discrepancy so nobody "fixes" the
            // table to match the document later.
            Assert.That(GameClock.FirstSundayOfApril(1991), Is.EqualTo(7),
                        "DST began 7 April 1991, seventeen days after the equinox");

            var equinox = new GameClock(GameClock.TickOn(1991, 3, 21, At(12, 0)));
            Assert.That(equinox.IsDaylightSaving, Is.False, "21 March 1991 is standard time");

            Assert.That(Daylight.Sunrise(3, 21), Is.EqualTo(At(5, 54)).Within(1));
            Assert.That(Daylight.Sunset(3, 21), Is.EqualTo(At(18, 2)).Within(1));

            // And the document's numbers are exactly this plus an hour - which is the proof that
            // the disagreement is the DST rule and not a different calculation.
            Assert.That(Daylight.Sunrise(3, 21) + 60, Is.EqualTo(At(6, 54)).Within(1));
            Assert.That(Daylight.Sunset(3, 21) + 60, Is.EqualTo(At(19, 3)).Within(1));
        }

        [Test]
        public void DaylightSavingRunsAprilToOctoberForEveryYearOfTheGame()
        {
            // The rule in force 1987-2006. The dates below are the real ones; the point of listing
            // both ends of the window is that if somebody ever ports this to the post-2007 rule by
            // accident, 1991 would start in March and this fails loudly.
            var known = new[]
            {
                (1991, 7, 27), (1992, 5, 25), (1996, 7, 27),
                (2000, 2, 29), (2004, 4, 31), (2006, 2, 29),
            };

            foreach (var (year, april, october) in known)
            {
                Assert.That(GameClock.FirstSundayOfApril(year), Is.EqualTo(april), $"April {year}");
                Assert.That(GameClock.LastSundayOfOctober(year), Is.EqualTo(october), $"October {year}");
            }

            // And they really are Sundays, and really are the first and last ones.
            for (int year = 1991; year <= 2006; year++)
            {
                int a = GameClock.FirstSundayOfApril(year), o = GameClock.LastSundayOfOctober(year);
                Assert.That(new DateTime(year, 4, a).DayOfWeek, Is.EqualTo(DayOfWeek.Sunday));
                Assert.That(new DateTime(year, 10, o).DayOfWeek, Is.EqualTo(DayOfWeek.Sunday));
                Assert.That(a, Is.LessThanOrEqualTo(7), "no earlier Sunday can exist in April");
                Assert.That(o, Is.GreaterThan(24), "no later Sunday can exist in October");
            }
        }

        [Test]
        public void TheClocksChangeAtTwoInTheMorningAndNotAtMidnight()
        {
            // Spring forward: 02:00 standard becomes 03:00 daylight. So on the morning of the
            // changeover, 01:59 is still standard and 02:00 is not.
            var before = new GameClock(GameClock.TickOn(1991, 4, 7, At(1, 59)));
            var after = new GameClock(GameClock.TickOn(1991, 4, 7, At(2, 0)));
            Assert.That(before.IsDaylightSaving, Is.False, "01:59 on changeover morning");
            Assert.That(after.IsDaylightSaving, Is.True, "02:00 on changeover morning");

            // Fall back: 02:00 daylight becomes 01:00 standard, so the switch happens at 01:00
            // STANDARD - which is the time base this clock counts in.
            var stillSummer = new GameClock(GameClock.TickOn(1991, 10, 27, At(0, 59)));
            var backToWinter = new GameClock(GameClock.TickOn(1991, 10, 27, At(1, 0)));
            Assert.That(stillSummer.IsDaylightSaving, Is.True, "00:59 standard on the last Sunday");
            Assert.That(backToWinter.IsDaylightSaving, Is.False, "01:00 standard on the last Sunday");

            // The day either side of the window.
            Assert.That(new GameClock(GameClock.TickOn(1991, 4, 6, At(12, 0))).IsDaylightSaving, Is.False);
            Assert.That(new GameClock(GameClock.TickOn(1991, 4, 8, At(12, 0))).IsDaylightSaving, Is.True);
            Assert.That(new GameClock(GameClock.TickOn(1991, 10, 26, At(12, 0))).IsDaylightSaving, Is.True);
            Assert.That(new GameClock(GameClock.TickOn(1991, 10, 28, At(12, 0))).IsDaylightSaving, Is.False);
        }

        [Test]
        public void TheSimulationClockNeverSpringsForward()
        {
            // The load-bearing one. Daylight saving is presentation; if it ever moved the tick
            // counter, one day in April would be 1380 minutes and every OpenWindow in the content
            // would slide. Both changeover days must still be exactly 1440 minutes long.
            foreach (var (y, m, d) in new[] { (1991, 4, 7), (1991, 10, 27), (2004, 4, 4) })
            {
                long midnight = GameClock.TickOn(y, m, d);
                long nextMidnight = GameClock.TickOn(y, m, d + 1);
                Assert.That((nextMidnight - midnight) / GameClock.TicksPerMinute, Is.EqualTo(1440),
                            $"{d:00}/{m:00}/{y} must be a normal day to the simulation");

                // And the weekday still advances by exactly one.
                Assert.That(new GameClock(nextMidnight).DayOfWeek,
                            Is.EqualTo((new GameClock(midnight).DayOfWeek + 1) % 7));
            }
        }

        [Test]
        public void TheWallClockReadsAnHourLaterInSummerAndTheSameInWinter()
        {
            var summer = new GameClock(GameClock.TickOn(1991, 6, 21, At(14, 30)));
            Assert.That(summer.MinuteOfDay, Is.EqualTo(At(14, 30)), "the simulation is on standard time");
            Assert.That(summer.WallClock, Is.EqualTo("15:30"), "the kitchen clock is not");

            var winter = new GameClock(GameClock.TickOn(1991, 12, 21, At(14, 30)));
            Assert.That(winter.WallClock, Is.EqualTo("14:30"));

            // The documented consequence, both ends of the year: in June it is still light at half
            // past eight, and in late December it is dark by half past four. THE-YEAR.md.
            Assert.That(new GameClock(GameClock.TickOn(1991, 6, 21, At(19, 30))).IsDark, Is.False);
            Assert.That(new GameClock(GameClock.TickOn(1991, 6, 21, At(19, 30))).WallClock, Is.EqualTo("20:30"));
            Assert.That(new GameClock(GameClock.TickOn(1991, 12, 21, At(16, 30))).Light,
                        Is.EqualTo(LightLevel.Night).Or.EqualTo(LightLevel.Dusk));
            Assert.That(new GameClock(GameClock.TickOn(1991, 12, 21, At(17, 0))).IsDark, Is.True,
                        "sun down 16:27, civil twilight over well before five");
        }

        [Test]
        public void OctoberTakesAnHourAndThreeQuartersOfEveningOffTheTown()
        {
            // WHO-SEES-WHOM.md, section 2 - the month of least visibility. Sunset 18:35 on 1
            // October and 16:49 on 1 November, both as the town's clocks read them: October is
            // still on daylight time, November is not.
            var oct = new GameClock(GameClock.TickOn(1991, 10, 1, At(12, 0)));
            var nov = new GameClock(GameClock.TickOn(1991, 11, 1, At(12, 0)));
            Assert.That(oct.IsDaylightSaving, Is.True);
            Assert.That(nov.IsDaylightSaving, Is.False);

            int octSunset = oct.Sunset + 60, novSunset = nov.Sunset;
            Assert.That(octSunset, Is.EqualTo(At(18, 35)).Within(1));
            Assert.That(novSunset, Is.EqualTo(At(16, 49)).Within(1));
            Assert.That(octSunset - novSunset, Is.EqualTo(106).Within(2),
                        "an hour and three quarters, and an hour of it is the clocks going back");
        }

        [Test]
        public void TheDayIsTwiceAsLongInJuneAsInDecember()
        {
            int shortest = Daylight.Length(12, 21), longest = Daylight.Length(6, 21);
            Assert.That(shortest, Is.EqualTo(9 * 60 + 17).Within(2), "21 December, 9 h 17");
            Assert.That(longest, Is.EqualTo(15 * 60 + 4).Within(2), "21 June, 15 h 04");

            // No day of the year falls outside that pair - the solstices really are the extremes.
            for (int m = 1; m <= 12; m++)
                for (int d = 1; d <= DaysIn(m); d++)
                {
                    int len = Daylight.Length(m, d);
                    Assert.That(len, Is.InRange(shortest - 1, longest + 1), $"day length on {d:00}/{m:00}");
                }
        }

        [Test]
        public void EveryDayOfTheYearIsWellFormed()
        {
            for (int m = 1; m <= 12; m++)
                for (int d = 1; d <= DaysIn(m); d++)
                {
                    string when = $"{d:00}/{m:00}";
                    int dawn = Daylight.Dawn(m, d), rise = Daylight.Sunrise(m, d);
                    int set = Daylight.Sunset(m, d), dusk = Daylight.Dusk(m, d);

                    Assert.That(dawn, Is.LessThan(rise), $"dawn before sunrise on {when}");
                    Assert.That(rise, Is.LessThan(set), $"sunrise before sunset on {when}");
                    Assert.That(set, Is.LessThan(dusk), $"sunset before dusk on {when}");
                    Assert.That(dawn, Is.InRange(0, 1439), $"dawn inside the day on {when}");
                    Assert.That(dusk, Is.InRange(0, 1439), $"dusk inside the day on {when}");

                    // Twilight at this latitude is a little over half an hour, never the hours it
                    // becomes further north. If this ever fails the table has been mangled.
                    Assert.That(rise - dawn, Is.InRange(25, 36), $"morning twilight on {when}");
                    Assert.That(dusk - set, Is.InRange(25, 36), $"evening twilight on {when}");
                }
        }

        [Test]
        public void TheTableDoesNotJumpFromOneDayToTheNext()
        {
            // A mistyped digit inside a 365-entry literal is invisible to every other test in this
            // file unless it happens to land on one of the eight dates the research covers. What
            // it cannot hide from is its neighbours: sunrise moves by at most two minutes a day.
            int prevRise = Daylight.Sunrise(1, 1), prevSet = Daylight.Sunset(1, 1);
            for (int i = 1; i < 365; i++)
            {
                (int m, int d) = DateOfCommonYearIndex(i);
                int rise = Daylight.Sunrise(m, d), set = Daylight.Sunset(m, d);
                Assert.That(Math.Abs(rise - prevRise), Is.LessThanOrEqualTo(2), $"sunrise jump at {d:00}/{m:00}");
                Assert.That(Math.Abs(set - prevSet), Is.LessThanOrEqualTo(2), $"sunset jump at {d:00}/{m:00}");
                prevRise = rise; prevSet = set;
            }

            // The year has to close on itself too - 31 December's neighbour is 1 January.
            Assert.That(Math.Abs(Daylight.Sunrise(12, 31) - Daylight.Sunrise(1, 1)), Is.LessThanOrEqualTo(2));
            Assert.That(Math.Abs(Daylight.Sunset(12, 31) - Daylight.Sunset(1, 1)), Is.LessThanOrEqualTo(2));
        }

        [Test]
        public void TheLightStatesMeetExactlyAtTheirBoundaries()
        {
            const int month = 10, day = 15;                      // the month that matters most
            int dawn = Daylight.Dawn(month, day), rise = Daylight.Sunrise(month, day);
            int set = Daylight.Sunset(month, day), dusk = Daylight.Dusk(month, day);

            Assert.That(Daylight.At(month, day, dawn - 1), Is.EqualTo(LightLevel.Night));
            Assert.That(Daylight.At(month, day, dawn), Is.EqualTo(LightLevel.Dawn));
            Assert.That(Daylight.At(month, day, rise - 1), Is.EqualTo(LightLevel.Dawn));
            Assert.That(Daylight.At(month, day, rise), Is.EqualTo(LightLevel.Day));
            Assert.That(Daylight.At(month, day, set - 1), Is.EqualTo(LightLevel.Day));
            Assert.That(Daylight.At(month, day, set), Is.EqualTo(LightLevel.Dusk));
            Assert.That(Daylight.At(month, day, dusk - 1), Is.EqualTo(LightLevel.Dusk));
            Assert.That(Daylight.At(month, day, dusk), Is.EqualTo(LightLevel.Night));

            // Midnight either end of every day is night, all year - no gaps and no overlaps in
            // the state machine.
            for (int m = 1; m <= 12; m++)
                for (int d = 1; d <= DaysIn(m); d++)
                {
                    Assert.That(Daylight.At(m, d, 0), Is.EqualTo(LightLevel.Night), $"midnight on {d:00}/{m:00}");
                    Assert.That(Daylight.At(m, d, 1439), Is.EqualTo(LightLevel.Night), $"23:59 on {d:00}/{m:00}");
                }
        }

        [Test]
        public void DarknessIsCivilTwilightAndNotSunset()
        {
            // The witness gate. Twilight counts as light on purpose: "she thought it was him" is a
            // different testimony from "she saw nothing", and the half hour after sunset is where
            // that distinction lives.
            var duskOnTheFifteenth = new GameClock(
                GameClock.TickOn(1991, 10, 15, Daylight.Sunset(10, 15) + 5));
            Assert.That(duskOnTheFifteenth.Light, Is.EqualTo(LightLevel.Dusk));
            Assert.That(duskOnTheFifteenth.IsDark, Is.False, "five minutes after sunset you can still see");

            var afterTwilight = new GameClock(
                GameClock.TickOn(1991, 10, 15, Daylight.Dusk(10, 15) + 1));
            Assert.That(afterTwilight.IsDark, Is.True, "a minute past civil twilight you cannot");
        }

        [Test]
        public void TheLeapDayDoesNotSlideTheRestOfTheYear()
        {
            // The table is a common year. Indexing it by raw day-of-year would push every date
            // after February forward by one in a leap year - a small error, every fourth year,
            // that nothing would ever notice. 29 February borrows the 28th instead.
            Assert.That(Daylight.IndexOf(2, 29), Is.EqualTo(Daylight.IndexOf(2, 28)));

            foreach (int year in new[] { 1992, 1996, 2000, 2004 })
                foreach (var (m, d) in new[] { (3, 1), (6, 21), (10, 15), (12, 31) })
                {
                    var leap = new GameClock(GameClock.TickOn(year, m, d, 720));
                    var common = new GameClock(GameClock.TickOn(year + 1, m, d, 720));
                    Assert.That(leap.IsLeapYear, Is.True);
                    Assert.That(leap.Sunrise, Is.EqualTo(common.Sunrise),
                                $"{d:00}/{m:00} must not move between {year} and {year + 1}");
                    Assert.That(leap.Sunset, Is.EqualTo(common.Sunset));
                }

            // And 29 February itself is a real, sensible day rather than an index crash.
            var leapDay = new GameClock(GameClock.TickOn(2004, 2, 29, 720));
            Assert.That(leapDay.Light, Is.EqualTo(LightLevel.Day));
            Assert.That(leapDay.DaylightMinutes, Is.InRange(11 * 60, 11 * 60 + 30));
        }

        [Test]
        public void TheClockAgreesWithTheTableItIsReadingFrom()
        {
            // GameClock's daylight properties are thin wrappers, and thin wrappers are exactly
            // where a wrong month/day argument order hides.
            var t = new GameClock(GameClock.TickOn(2004, 2, 27, At(17, 30)));   // the downtown fire
            Assert.That(t.Date, Is.EqualTo("Fri 27 Feb 2004"));
            Assert.That(t.Sunrise, Is.EqualTo(Daylight.Sunrise(2, 27)));
            Assert.That(t.Sunset, Is.EqualTo(Daylight.Sunset(2, 27)));
            Assert.That(t.Dawn, Is.EqualTo(Daylight.Dawn(2, 27)));
            Assert.That(t.Dusk, Is.EqualTo(Daylight.Dusk(2, 27)));
            Assert.That(t.DaylightMinutes, Is.EqualTo(Daylight.Length(2, 27)));
            Assert.That(t.Light, Is.EqualTo(Daylight.At(2, 27, At(17, 30))));

            // Half past five on a late-February evening is the last of the light: the sun goes
            // down at 17:38, and civil twilight is over by ten past six.
            Assert.That(t.IsDaylightSaving, Is.False, "February is never daylight time");
            Assert.That(t.Light, Is.EqualTo(LightLevel.Day));
            Assert.That(t.Sunset, Is.EqualTo(At(17, 38)));
            Assert.That(new GameClock(GameClock.TickOn(2004, 2, 27, At(17, 45))).Light,
                        Is.EqualTo(LightLevel.Dusk));
            Assert.That(new GameClock(GameClock.TickOn(2004, 2, 27, At(18, 15))).IsDark, Is.True);
        }

        private static int DaysIn(int month) =>
            new[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 }[month - 1];

        private static (int month, int day) DateOfCommonYearIndex(int index)
        {
            for (int m = 1; m <= 12; m++)
            {
                if (index < DaysIn(m)) return (m, index + 1);
                index -= DaysIn(m);
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
