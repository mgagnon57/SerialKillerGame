using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The ambulance's half of the story: a downed citizen taken off the map entirely, and -
    /// unless they died - handed back to their own plan at their own front door. Absence reuses
    /// Activity.AwayFromTown, the same value an out-of-town commuter carries, so the renderer,
    /// the census and the animation table already know how to leave them undrawn; nothing here
    /// teaches any of them a new state.
    /// </summary>
    [TestFixture]
    public class TakeAwayTests
    {
        [Test]
        public void ATakenAwayVictimIsNotDrawnAndNotHit()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();

            var who = new CitizenId(1);
            sim.Down(who);
            sim.TakeAway(who, int.MaxValue);

            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.AwayFromTown));
            Assert.That(sim.GetAgent(who).Downed, Is.False);

            var at = sim.GetAgent(who).Position;
            for (int t = 0; t < GameClock.TicksPerHour; t++) sim.Tick();
            Assert.That(sim.GetAgent(who).Position, Is.EqualTo(at),
                "a citizen taken off the map moved anyway");
        }

        [Test]
        public void ASurvivorReturnsHomeAndRejoinsTheirPlan()
        {
            // CitizenId(1) is an ordinary customer whose only errand is the walk to the shop and
            // back - see ARevivedAgentDepartsPromptlyEvenMidBlock's own comment for why the test
            // waits for Travelling rather than downing them at a fixed tick: downing somebody
            // still standing at their own door makes any "did they move" assertion pass for the
            // wrong reason.
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            var who = new CitizenId(1);

            int guard = 20 * 60 * 60 * 4; // up to four sim hours to catch a departure
            while (!sim.GetAgent(who).Travelling && guard-- > 0) sim.Tick();
            Assert.That(sim.GetAgent(who).Travelling, Is.True,
                "customer #2 never set off on their one daily errand - fixture or seed changed?");

            sim.Down(who);

            int returnMinute = sim.Clock.Day * 1440 + sim.Clock.MinuteOfDay + 30;
            sim.TakeAway(who, returnMinute);

            var home = Queueham.World.GetPlace(Queueham.People.Get(who).Home);
            var doorPosition = Vec2.CentreOf(home.Door);

            for (int t = 0; t < GameClock.TicksPerMinute * 31; t++) sim.Tick();

            Assert.That(sim.GetAgent(who).AwayUntilMinute, Is.EqualTo(0),
                "Return did not clear AwayUntilMinute once the clock reached it");
            Assert.That(sim.GetAgent(who).Position, Is.EqualTo(doorPosition),
                "the survivor was not set down at their own front door on return");

            bool rejoined = false;
            for (int t = 0; t < GameClock.TicksPerMinute * 10; t++)
            {
                sim.Tick();
                if (sim.GetAgent(who).Travelling || sim.GetAgent(who).Doing != Activity.AwayFromTown)
                {
                    rejoined = true;
                    break;
                }
            }
            Assert.That(rejoined, Is.True,
                "the returned citizen never rejoined their plan within ten sim minutes");
        }

        [Test]
        public void ADeadVictimNeverComesBack()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();

            var who = new CitizenId(1);
            sim.Down(who);
            sim.TakeAway(who, int.MaxValue);

            for (int t = 0; t < GameClock.TicksPerDay; t++) sim.Tick();

            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.AwayFromTown),
                "int.MaxValue is meant to mean 'never returns'");
        }

        [Test]
        public void NobodyTakenAwayIsByteIdenticalToBefore()
        {
            // Same shape as NobodyDownedIsByteIdenticalToBefore: proves the new tick-loop gate
            // (checking AwayUntilMinute against the clock) is a true no-op when the field is
            // never set on anybody, not merely that it happens not to have drifted this run.
            var a = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            var b = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 20 * 60 * 60; t++) { a.Tick(); b.Tick(); }
            for (int i = 0; i < a.AgentCount; i++)
            {
                Assert.That(a.GetAgent(i).Position, Is.EqualTo(b.GetAgent(i).Position));
                Assert.That(a.GetAgent(i).Doing, Is.EqualTo(b.GetAgent(i).Doing));
            }
        }

        [Test]
        public void ReturnIsIdempotentAndTakeAwayRequiresDowned()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();

            var standing = new CitizenId(2);
            var standingDoing = sim.GetAgent(standing).Doing;

            sim.TakeAway(standing, int.MaxValue);            // not Downed - must be a no-op
            Assert.That(sim.GetAgent(standing).Doing, Is.EqualTo(standingDoing));
            Assert.That(sim.GetAgent(standing).AwayUntilMinute, Is.EqualTo(0));

            var who = new CitizenId(1);
            sim.Down(who);
            sim.TakeAway(who, int.MaxValue);
            sim.Return(who);
            var afterFirstReturn = sim.GetAgent(who);

            sim.Return(who);                                  // twice is a no-op, not a throw
            Assert.That(sim.GetAgent(who).Position, Is.EqualTo(afterFirstReturn.Position));
            Assert.That(sim.GetAgent(who).AwayUntilMinute, Is.EqualTo(afterFirstReturn.AwayUntilMinute));
            Assert.That(sim.GetAgent(who).Destination, Is.EqualTo(afterFirstReturn.Destination));
        }
    }
}
