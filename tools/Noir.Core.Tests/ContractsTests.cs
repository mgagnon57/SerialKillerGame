using NUnit.Framework;
using Noir.Core.Contracts;

namespace Noir.Core.Tests
{
    [TestFixture]
    public class GameClockTests
    {
        [Test]
        public void TickZeroIsMondayMidnight()
        {
            var c = new GameClock(0);
            Assert.That(c.Day, Is.EqualTo(0));
            Assert.That(c.MinuteOfDay, Is.EqualTo(0));
            Assert.That(c.HourOfDay, Is.EqualTo(0));
            Assert.That(c.DayOfWeek, Is.EqualTo(0));
            Assert.That(c.IsWeekend, Is.False);
        }

        [Test]
        public void MinuteOfDayAdvancesCorrectly()
        {
            var c = new GameClock(GameClock.TicksPerMinute * 465); // 07:45
            Assert.That(c.HourOfDay, Is.EqualTo(7));
            Assert.That(c.MinuteOfHour, Is.EqualTo(45));
        }

        [Test]
        public void DayRollsOverAtMidnight()
        {
            var c = new GameClock(GameClock.TicksPerDay);
            Assert.That(c.Day, Is.EqualTo(1));
            Assert.That(c.MinuteOfDay, Is.EqualTo(0));
            Assert.That(c.DayOfWeek, Is.EqualTo(1));
        }

        [Test]
        public void WeekendIsSaturdayAndSunday()
        {
            Assert.That(new GameClock(GameClock.TicksPerDay * 5).IsWeekend, Is.True);  // Sat
            Assert.That(new GameClock(GameClock.TicksPerDay * 6).IsWeekend, Is.True);  // Sun
            Assert.That(new GameClock(GameClock.TicksPerDay * 7).IsWeekend, Is.False); // Mon again
        }

        [Test]
        public void TickAtRoundTrips()
        {
            long tick = GameClock.TickAt(day: 3, minuteOfDay: 1000);
            var c = new GameClock(tick);
            Assert.That(c.Day, Is.EqualTo(3));
            Assert.That(c.MinuteOfDay, Is.EqualTo(1000));
        }

        [Test]
        public void MinutesUntilWrapsPastMidnight()
        {
            var c = new GameClock(GameClock.TicksPerMinute * 1380); // 23:00
            Assert.That(c.MinutesUntil(1380), Is.EqualTo(0));
            Assert.That(c.MinutesUntil(60), Is.EqualTo(120)); // 01:00 next day
        }
    }

    [TestFixture]
    public class RngTests
    {
        [Test]
        public void SameSeedProducesSameSequence()
        {
            var a = new Xoshiro256ss(12345);
            var b = new Xoshiro256ss(12345);
            for (int i = 0; i < 1000; i++)
                Assert.That(a.NextUInt64(), Is.EqualTo(b.NextUInt64()), $"diverged at draw {i}");
        }

        [Test]
        public void DifferentSeedsProduceDifferentSequences()
        {
            var a = new Xoshiro256ss(1);
            var b = new Xoshiro256ss(2);
            bool anyDifferent = false;
            for (int i = 0; i < 50; i++)
                if (a.NextUInt64() != b.NextUInt64()) { anyDifferent = true; break; }
            Assert.That(anyDifferent, Is.True);
        }

        [Test]
        public void SubstreamsAreIndependentAndStable()
        {
            // The point of substreams: adding a draw to one system must not shift another's.
            var pop1 = Xoshiro256ss.Substream(999, "population");
            var sched1 = Xoshiro256ss.Substream(999, "schedules");

            ulong popFirst = pop1.NextUInt64();
            ulong schedFirst = sched1.NextUInt64();

            Assert.That(popFirst, Is.Not.EqualTo(schedFirst));

            // Re-derived from the same master seed, they replay identically.
            Assert.That(Xoshiro256ss.Substream(999, "population").NextUInt64(), Is.EqualTo(popFirst));
            Assert.That(Xoshiro256ss.Substream(999, "schedules").NextUInt64(), Is.EqualTo(schedFirst));
        }

        [Test]
        public void NextIntStaysInRange()
        {
            var r = new Xoshiro256ss(7);
            for (int i = 0; i < 10000; i++)
            {
                int v = r.NextInt(10);
                Assert.That(v, Is.InRange(0, 9));
            }
        }

        [Test]
        public void NextIntWithMinStaysInRange()
        {
            var r = new Xoshiro256ss(7);
            for (int i = 0; i < 10000; i++)
                Assert.That(r.NextInt(5, 9), Is.InRange(5, 8));
        }

        [Test]
        public void NextFloatIsUnitInterval()
        {
            var r = new Xoshiro256ss(3);
            for (int i = 0; i < 10000; i++)
            {
                float f = r.NextFloat();
                Assert.That(f, Is.GreaterThanOrEqualTo(0f));
                Assert.That(f, Is.LessThan(1f));
            }
        }

        [Test]
        public void NextIntIsRoughlyUniform()
        {
            var r = new Xoshiro256ss(42);
            var counts = new int[8];
            const int n = 80000;
            for (int i = 0; i < n; i++) counts[r.NextInt(8)]++;
            foreach (int c in counts)
                Assert.That(c, Is.InRange(n / 8 * 0.9, n / 8 * 1.1), "distribution is skewed");
        }
    }

    [TestFixture]
    public class TileAndVecTests
    {
        [Test]
        public void ChebyshevIsDiagonalAware()
        {
            Assert.That(Tile.ChebyshevDistance(new Tile(0, 0), new Tile(3, 3)), Is.EqualTo(3));
            Assert.That(Tile.ManhattanDistance(new Tile(0, 0), new Tile(3, 3)), Is.EqualTo(6));
        }

        [Test]
        public void VecToTileFloorsCorrectlyIncludingNegatives()
        {
            Assert.That(new Vec2(3.7f, 2.2f).ToTile(), Is.EqualTo(new Tile(3, 2)));
            Assert.That(new Vec2(-0.3f, -1.8f).ToTile(), Is.EqualTo(new Tile(-1, -2)));
            Assert.That(new Vec2(-1.0f, 2.0f).ToTile(), Is.EqualTo(new Tile(-1, 2)));
        }

        [Test]
        public void CentreOfTileIsHalfOffset()
        {
            var v = Vec2.CentreOf(new Tile(4, 9));
            Assert.That(v.X, Is.EqualTo(4.5f));
            Assert.That(v.Y, Is.EqualTo(9.5f));
        }

        [Test]
        public void RoundTripTileToCentreToTile()
        {
            for (int x = -5; x < 5; x++)
            for (int y = -5; y < 5; y++)
            {
                var t = new Tile(x, y);
                Assert.That(Vec2.CentreOf(t).ToTile(), Is.EqualTo(t));
            }
        }
    }
}
