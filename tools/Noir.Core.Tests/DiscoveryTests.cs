using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;
using Noir.Core.Witness;
using Noir.Sim;

namespace Noir.Core.Tests
{
    [TestFixture]
    public class DiscoveryTests
    {
        private const int Day = 3;
        private const int Noon = 12 * 60;

        /// <summary>
        /// THE WHOLE POINT OF THIS TASK, proven both directions. Discovery and
        /// Recollection.WhatTheySawOfEvents walk the identical gate sequence, so:
        ///
        ///   1. whoever Discovery names as the finder is exactly somebody the event arm would
        ///      credit with a sighting of a hit recorded at the same tile and minute, and
        ///   2. a citizen too far away to ever be named the finder (beyond Sightlines.NeverBeyond)
        ///      equally has nothing to say about that same hit.
        ///
        /// One optics, two consumers — this is the test that would fail first if Discovery ever
        /// drifted from Recollection's own gates.
        /// </summary>
        [Test]
        public void TheDiscovererIsExactlyAWitnessTheEventArmWouldCredit()
        {
            var v = VillageContext.Load();

            // A body sitting exactly on some citizen's own doorstep at noon - the same
            // distance-zero trick EventTestimonyTests and WitnessTests use - so SOMEBODY is
            // guaranteed to see it (SawAnythingAtAll only gates on range and blockers, and
            // distance zero clears the range gate regardless of light or attention).
            Citizen anchor = null;
            Tile body = default;
            foreach (Citizen candidate in v.People.Citizens)
            {
                DayPlan plan = DayPlanner.Plan(v.World, v.People, candidate, Day, v.Seed);
                Block block = plan.At(Noon);
                if (block.What == Activity.Asleep || block.What == Activity.TravellingTo ||
                    !block.Where.IsValid) continue;
                anchor = candidate;
                body = v.World.GetPlace(block.Where).Door;
                break;
            }
            Assert.That(anchor, Is.Not.Null,
                "no citizen in the fixture village is stationary at noon - the test needs a " +
                "different minute, not a different assertion");

            var discovery = new Discovery();
            CitizenId foundId = discovery.WhoSees(v.World, v.People, Day, Noon, body, v.Seed);
            Assert.That(foundId.IsValid, Is.True,
                "the anchor citizen stands exactly where the body is at this minute, so " +
                "somebody must be named the finder");

            Citizen discoverer = v.People.Get(foundId);
            Assert.That(discoverer, Is.Not.Null);

            int minute = Day * Sighting.MinutesPerDay + Noon;
            var hits = new HitEvents();
            hits.Record(minute, body, CarTone.Dark, CarShape.Van);

            EventSighting[] credited = Recollection.WhatTheySawOfEvents(
                v.World, v.People, discoverer, Day, hits, v.Seed);
            Assert.That(credited.Length, Is.GreaterThan(0),
                "whoever Discovery names as the finder must be exactly somebody the event arm " +
                "would credit with a sighting of the same hit - one optics, two consumers");

            // AND THE OTHER DIRECTION: a citizen standing beyond Sightlines.NeverBeyond from the
            // body can never be named the finder, by the range gate alone - regardless of light,
            // attention or anything else. The same gate lives in the event arm, so they must be
            // equally silent about a hit recorded at that same tile and minute.
            Citizen farAway = null;
            foreach (Citizen candidate in v.People.Citizens)
            {
                DayPlan plan = DayPlanner.Plan(v.World, v.People, candidate, Day, v.Seed);
                Block block = plan.At(Noon);
                if (!block.Where.IsValid) continue;
                Tile door = v.World.GetPlace(block.Where).Door;
                if (Tile.ChebyshevDistance(door, body) > Sightlines.NeverBeyond)
                { farAway = candidate; break; }
            }
            Assert.That(farAway, Is.Not.Null,
                "no citizen in the fixture village stands beyond NeverBeyond from the body - " +
                "the test needs a different body tile");

            var farHits = new HitEvents();
            farHits.Record(minute, body, CarTone.Dark, CarShape.Van);
            EventSighting[] farCredited = Recollection.WhatTheySawOfEvents(
                v.World, v.People, farAway, Day, farHits, v.Seed);
            Assert.That(farCredited.Length, Is.EqualTo(0),
                "a citizen too far away to ever be named the finder must equally have nothing " +
                "to say about the same hit");
        }

        /// <summary>A tile nowhere near the fixture village (170 x 120 tiles) is beyond
        /// Sightlines.NeverBeyond from every door in it, at any minute.</summary>
        [Test]
        public void ABodyOnAnEmptyRangeIsNotDiscovered()
        {
            var v = VillageContext.Load();
            var body = new Tile(-1000, -1000);

            var discovery = new Discovery();
            CitizenId found = discovery.WhoSees(v.World, v.People, Day, Noon, body, v.Seed);

            Assert.That(found, Is.EqualTo(CitizenId.None));
        }

        /// <summary>
        /// The victim's own door overlooking the body proves nothing once they are the body.
        /// Same interruption window as Recollection's - see IInterruptions - applied to exactly
        /// one citizen, so this isolates the gate rather than relying on nobody else being close.
        /// </summary>
        [Test]
        public void TheVictimNeverDiscoversTheirOwnBody()
        {
            var v = VillageContext.Load();

            // The lowest-index citizen stationary at noon: nobody earlier in the population can
            // preempt them (an earlier index would have been picked instead), so without any
            // interruption WhoSees must name exactly this citizen when the body sits on their
            // own doorstep.
            Citizen victim = null;
            Tile body = default;
            foreach (Citizen candidate in v.People.Citizens)
            {
                DayPlan plan = DayPlanner.Plan(v.World, v.People, candidate, Day, v.Seed);
                Block block = plan.At(Noon);
                if (block.What == Activity.Asleep || block.What == Activity.TravellingTo ||
                    !block.Where.IsValid) continue;
                victim = candidate;
                body = v.World.GetPlace(block.Where).Door;
                break;
            }
            Assert.That(victim, Is.Not.Null,
                "no citizen in the fixture village is stationary at noon");

            var discovery = new Discovery();
            CitizenId undisturbed = discovery.WhoSees(v.World, v.People, Day, Noon, body, v.Seed);
            Assert.That(undisturbed, Is.EqualTo(victim.Id),
                "sanity check: without any interruption the victim's own door overlooking the " +
                "body should name them the finder - if this fails the test picked the wrong " +
                "citizen, not a bug in Discovery");

            int minute = Day * Sighting.MinutesPerDay + Noon;
            var interruptions = new DownedAt(victim.Id, minute);

            CitizenId whileDown = discovery.WhoSees(v.World, v.People, Day, Noon, body, v.Seed,
                                                    interruptions: interruptions);
            Assert.That(whileDown, Is.Not.EqualTo(victim.Id),
                "the victim is downed from exactly this minute - their door still overlooks " +
                "the body, and they must still be skipped");
        }

        /// <summary>A stub IInterruptions naming exactly one citizen's downed-from minute, never
        /// back - the same shape EventTestimonyTests' DownedAt uses to isolate Recollection's own
        /// gate without a live Simulation.</summary>
        private sealed class DownedAt : IInterruptions
        {
            private readonly CitizenId _who;
            private readonly int _minute;
            public DownedAt(CitizenId who, int minute) { _who = who; _minute = minute; }
            public int DownedFromMinute(CitizenId who) =>
                who.Value == _who.Value ? _minute : int.MaxValue;
            public int BackFromMinute(CitizenId who) => int.MaxValue;
        }
    }
}
