using System;
using NUnit.Framework;
using Noir.Core.Contracts;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The calendar bolted onto the simulation clock.
    ///
    /// Checked against System.DateTime here, which Core itself may not use — the point of the
    /// hand-rolled integer arithmetic is that it is bit-identical on every machine forever, and
    /// the point of the tests is to prove it agrees with the framework anyway.
    /// </summary>
    [TestFixture]
    public class GameClockCalendarTests
    {
        [Test]
        public void DayZeroIsMondaySeventhJanuary1991()
        {
            // The epoch is a Monday ON PURPOSE. DayOfWeek is Day % 7 and OpenWindow masks bit 0
            // as Monday, so every opening hour in the content depends on this. 1 January 1991
            // was a Tuesday - anchoring there would have shifted every business by a day.
            var t = new GameClock(0);

            Assert.That(t.Year, Is.EqualTo(1991));
            Assert.That(t.Month, Is.EqualTo(1));
            Assert.That(t.DayOfMonth, Is.EqualTo(7));
            Assert.That(t.DayOfWeek, Is.EqualTo(0), "day zero must be a Monday");
            Assert.That(new DateTime(1991, 1, 7).DayOfWeek, Is.EqualTo(System.DayOfWeek.Monday),
                        "and 7 Jan 1991 really was one");
        }

        [Test]
        public void TheWeekdayNeverDriftsFromTheRealCalendar()
        {
            // Fifteen years of it - the whole span the game runs over.
            var epoch = new DateTime(1991, 1, 7);
            for (int day = 0; day < 365 * 16; day++)
            {
                var t = new GameClock(GameClock.TickAt(day, 0));
                var real = epoch.AddDays(day);

                Assert.That(t.Year, Is.EqualTo(real.Year), $"year on day {day}");
                Assert.That(t.Month, Is.EqualTo(real.Month), $"month on day {day}");
                Assert.That(t.DayOfMonth, Is.EqualTo(real.Day), $"day on day {day}");
                Assert.That(t.DayOfYear, Is.EqualTo(real.DayOfYear), $"day-of-year on day {day}");

                // 0 = Monday here, 0 = Sunday in the framework.
                int expected = ((int)real.DayOfWeek + 6) % 7;
                Assert.That(t.DayOfWeek, Is.EqualTo(expected), $"weekday on day {day}");
            }
        }

        [Test]
        public void LeapDaysAreRealDays()
        {
            // 1992, 1996, 2000 and 2004 are leap years inside the window; 2000 is the awkward
            // one, being divisible by 100 AND by 400.
            foreach (int year in new[] { 1992, 1996, 2000, 2004 })
            {
                var feb29 = new GameClock(GameClock.TickOn(year, 2, 29));
                Assert.That(feb29.Month, Is.EqualTo(2), $"{year} should have a 29 February");
                Assert.That(feb29.DayOfMonth, Is.EqualTo(29));
                Assert.That(feb29.IsLeapYear, Is.True);
                Assert.That(new GameClock(GameClock.TickOn(year, 12, 31)).DayOfYear, Is.EqualTo(366));
            }

            foreach (int year in new[] { 1991, 1993, 1999, 2001 })
            {
                Assert.That(new GameClock(GameClock.TickOn(year, 12, 31)).DayOfYear, Is.EqualTo(365));
                Assert.That(new GameClock(GameClock.TickOn(year, 6, 1)).IsLeapYear, Is.False);
            }
        }

        [Test]
        public void TheDatesTheResearchTurnsOnLandWhereTheyShould()
        {
            // Every one of these is a dated event in docs/research, and the whole reason the
            // clock grew a calendar. If any of them is off, something downstream will fire on
            // the wrong day and nothing else will notice.
            var fire = new GameClock(GameClock.TickOn(2004, 2, 27));
            Assert.That(fire.Date, Is.EqualTo("Fri 27 Feb 2004"), "the downtown fire");

            var broodX = new GameClock(GameClock.TickOn(2004, 5, 15));
            Assert.That(broodX.Year, Is.EqualTo(2004), "Brood X - 1987, 2004, 2021");
            Assert.That(broodX.Season, Is.EqualTo(Season.Spring));

            // Corn is 50% harvested around 17 October; grain dryers run all that month.
            var harvest = new GameClock(GameClock.TickOn(1991, 10, 17));
            Assert.That(harvest.Month, Is.EqualTo(10));
            Assert.That(harvest.Season, Is.EqualTo(Season.Autumn));

            // Corn is 50% planted around 5 May.
            Assert.That(new GameClock(GameClock.TickOn(1991, 5, 5)).Season, Is.EqualTo(Season.Spring));
        }

        [Test]
        public void SeasonsAreWholeMonths()
        {
            foreach (int m in new[] { 12, 1, 2 })
                Assert.That(new GameClock(GameClock.TickOn(1995, m, 15)).Season, Is.EqualTo(Season.Winter));
            foreach (int m in new[] { 3, 4, 5 })
                Assert.That(new GameClock(GameClock.TickOn(1995, m, 15)).Season, Is.EqualTo(Season.Spring));
            foreach (int m in new[] { 6, 7, 8 })
                Assert.That(new GameClock(GameClock.TickOn(1995, m, 15)).Season, Is.EqualTo(Season.Summer));
            foreach (int m in new[] { 9, 10, 11 })
                Assert.That(new GameClock(GameClock.TickOn(1995, m, 15)).Season, Is.EqualTo(Season.Autumn));
        }

        [Test]
        public void TickOnRoundTripsAndKeepsTheTimeOfDay()
        {
            long t = GameClock.TickOn(2004, 2, 27, 14 * 60 + 35);
            var c = new GameClock(t);
            Assert.That(c.Year, Is.EqualTo(2004));
            Assert.That(c.Month, Is.EqualTo(2));
            Assert.That(c.DayOfMonth, Is.EqualTo(27));
            Assert.That(c.HourOfDay, Is.EqualTo(14));
            Assert.That(c.MinuteOfHour, Is.EqualTo(35));
        }

        [Test]
        public void NothingThatAlreadyWorkedHasMoved()
        {
            // Day, MinuteOfDay and DayOfWeek are what the existing content and schedules are
            // written against. The calendar is additive; if any of these three changed, opening
            // hours and day plans would quietly shift.
            var c = new GameClock(GameClock.TickAt(9, 13 * 60 + 45));
            Assert.That(c.Day, Is.EqualTo(9));
            Assert.That(c.MinuteOfDay, Is.EqualTo(825));
            Assert.That(c.HourOfDay, Is.EqualTo(13));
            Assert.That(c.DayOfWeek, Is.EqualTo(2), "day 9 is still Wednesday");
            Assert.That(c.IsWeekend, Is.False);

            var sat = new GameClock(GameClock.TickAt(5, 0));
            Assert.That(sat.DayOfWeek, Is.EqualTo(5));
            Assert.That(sat.IsWeekend, Is.True);
        }

        [Test]
        public void CivilConversionSurvivesAThousandYears()
        {
            // The round trip is the whole safety argument for hand-rolling this.
            for (int z = -200000; z < 200000; z += 37)
            {
                GameClock.CivilFromDays(z, out int y, out int m, out int d);
                Assert.That(GameClock.DaysFromCivil(y, m, d), Is.EqualTo(z), $"round trip at {z}");
            }
        }
    }
}
