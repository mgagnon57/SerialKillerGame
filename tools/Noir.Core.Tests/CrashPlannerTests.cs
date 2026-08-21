using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.World;
using Noir.Sim;

namespace Noir.Core.Tests
{
    [TestFixture]
    public class CrashPlannerTests
    {
        // Any day in the fixture's first fortnight plans a crash against the default seed - see
        // TheTownCrashesAboutDaily, which scans all fourteen and found every one of them
        // non-null. Day 5 is simply one of them, fixed here so the shape tests (2 and 3) anchor
        // to the same plan rather than re-deriving it.
        private const int Day = 5;

        [Test] // same seed, same day, twice -> byte-identical plan (or both null)
        public void TheSameDayCrashesTheSameWay()
        {
            var v = VillageContext.Load();

            var first = CrashPlanner.PlanFor(v.World, v.People, Day, v.Seed);
            var second = CrashPlanner.PlanFor(v.World, v.People, Day, v.Seed);

            Assert.That(first.HasValue, Is.EqualTo(second.HasValue),
                "PlanFor is pure in (world, population, day, seed) - two calls with the same " +
                "four inputs must agree on whether there was a crash at all before agreeing on " +
                "anything about it.");

            if (!first.HasValue) return;

            var a = first.Value;
            var b = second.Value;
            Assert.That(a.MinuteOfDay, Is.EqualTo(b.MinuteOfDay));
            Assert.That(a.Scene, Is.EqualTo(b.Scene));
            Assert.That(a.AtFault, Is.EqualTo(b.AtFault));
            Assert.That(a.Other, Is.EqualTo(b.Other));
            Assert.That(a.Injury, Is.EqualTo(b.Injury));
            Assert.That(a.Verdict, Is.EqualTo(b.Verdict));
            Assert.That(a.Tone, Is.EqualTo(b.Tone));
            Assert.That(a.Shape, Is.EqualTo(b.Shape));
        }

        [Test] // every field the plan stamps is internally consistent
        public void ACrashHappensWhereSomebodyWasActuallyDriving()
        {
            var v = VillageContext.Load();
            var plan = CrashPlanner.PlanFor(v.World, v.People, Day, v.Seed);
            if (!plan.HasValue) return; // an honest no-crash day - nothing to check

            var p = plan.Value;

            // Rule 3: the scene is a road tile, not wherever the search happened to give up.
            Assert.That(v.World.Grid.TerrainAt(p.Scene), Is.EqualTo(Terrain.Road),
                "the crash's own scene search promises to stop only on Terrain.Road");

            // Rule 2: a crash is between two people, never a citizen and themselves.
            Assert.That(p.AtFault, Is.Not.EqualTo(p.Other));

            // Rule 1: the at-fault driver was actually driving at that exact minute - either the
            // commute's own departure or return edge, or a pub-close drive off a block that
            // qualified (ending at or after 20:00, the same gate PlanFor itself applies).
            var atFault = v.People.Get(p.AtFault);
            Assert.That(atFault, Is.Not.Null);

            var atFaultPlan = DayPlanner.Plan(v.World, v.People, atFault, Day, v.Seed);
            bool wasDriving = false;
            foreach (var block in atFaultPlan.Blocks)
            {
                if (block.What == Activity.AwayFromTown &&
                    (block.StartMinute == p.MinuteOfDay || block.EndMinute == p.MinuteOfDay))
                {
                    wasDriving = true;
                    break;
                }
                if (block.What == Activity.AtThePub && block.EndMinute >= 20 * 60 &&
                    block.EndMinute == p.MinuteOfDay)
                {
                    wasDriving = true;
                    break;
                }
            }
            Assert.That(wasDriving, Is.True,
                $"nothing in the at-fault driver's own day plan puts them on the road at " +
                $"minute {p.MinuteOfDay} - the crash minute has to come from one of their own " +
                $"blocks, not from nowhere");
        }

        [Test] // the verdict table, driven from the plan's own data
        public void DrinkConvicts()
        {
            var v = VillageContext.Load();
            var plan = CrashPlanner.PlanFor(v.World, v.People, Day, v.Seed);
            if (!plan.HasValue) return; // an honest no-crash day - nothing to check

            var p = plan.Value;
            var atFault = v.People.Get(p.AtFault);
            var atFaultPlan = DayPlanner.Plan(v.World, v.People, atFault, Day, v.Seed);

            bool drankWithin180 = false;
            foreach (var block in atFaultPlan.Blocks)
            {
                if (block.What != Activity.AtThePub) continue;
                int minutesSince = p.MinuteOfDay - block.EndMinute;
                if (minutesSince >= 0 && minutesSince <= 180) { drankWithin180 = true; break; }
            }

            if (drankWithin180)
                Assert.That(p.Verdict, Is.EqualTo(CrashVerdict.Dui),
                    "a pub visit ending within three hours of the crash is Rule 5's own DUI gate");
            else
                Assert.That(p.Verdict, Is.Not.EqualTo(CrashVerdict.Dui),
                    "with no qualifying pub visit on the at-fault driver's day, DUI is not on " +
                    "the table - only Ticket or LetGo");
        }

        /// <summary>
        /// Days 0..13 scanned against the fixture village (commuters on the road every weekday,
        /// a tavern open every evening) for AT LEAST THREE crashes in a fortnight - the brief's
        /// own number. Measured against this fixture: 14 of 14 days produced a qualifying pair,
        /// comfortably clear of the threshold, so the brief's own escape hatch ("if this proves
        /// flaky against the fixture, tighten to >= 1") was not needed. Left at >= 3 rather than
        /// asserting the exact 14 so a future content change that makes a handful of days quiet
        /// does not make this test the first thing that breaks.
        /// </summary>
        [Test]
        public void TheTownCrashesAboutDaily()
        {
            var v = VillageContext.Load();

            int crashes = 0;
            for (int day = 0; day < 14; day++)
                if (CrashPlanner.PlanFor(v.World, v.People, day, v.Seed).HasValue)
                    crashes++;

            Assert.That(crashes, Is.GreaterThanOrEqualTo(3),
                "a fortnight with commuters and a tavern producing fewer than three qualifying " +
                "pairs would mean the pairing window or the weighting table is unreachable, not " +
                "just quiet");
        }
    }
}
