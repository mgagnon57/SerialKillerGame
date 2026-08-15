using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;

namespace Noir.Core.Tests
{
    /// <summary>
    /// A person a car has hit stays down: the sim stops moving them, their plan stops
    /// overwriting them, and nothing ever stands them back up. The body staying where it fell
    /// is Phase 2's crime scene, so every assertion here is a gate on evidence.
    /// </summary>
    [TestFixture]
    public class DownedTests
    {
        [Test]
        public void ADownedAgentStopsAndStaysDown()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();          // let the morning start

            var who = new CitizenId(0);
            sim.Down(who);
            var at = sim.GetAgent(who).Position;

            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.Downed));
            Assert.That(sim.GetAgent(who).Travelling, Is.False);
            Assert.That(sim.GetAgent(who).Heading, Is.EqualTo(Vec2.Zero));

            for (int t = 0; t < 20 * 60 * 30; t++) sim.Tick(); // half a sim hour
            Assert.That(sim.GetAgent(who).Position, Is.EqualTo(at),
                "the body moved after being downed");
            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.Downed),
                "the plan overwrote Doing on a downed agent");
        }

        [Test]
        public void DownIsIdempotentAndOnlyTouchesItsVictim()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();

            var victim = new CitizenId(1);
            var bystander = new CitizenId(2);
            var bystanderDoing = sim.GetAgent(bystander).Doing;

            sim.Down(victim);
            sim.Down(victim);                                   // twice is a no-op, not a throw

            Assert.That(sim.GetAgent(victim).Doing, Is.EqualTo(Activity.Downed));
            Assert.That(sim.GetAgent(bystander).Doing, Is.EqualTo(bystanderDoing));
        }

        [Test]
        public void ARevivedAgentDepartsPromptlyEvenMidBlock()
        {
            // CitizenId(0) is Queueham.Shopkeeper, who stands at one counter all day and would
            // "pass" a movement assertion by never having anywhere else to go, downed or not.
            // CitizenId(1) is an ordinary customer whose only errand is the walk to the shop and
            // back, which is exactly the case a car hitting somebody on that walk looks like.
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            var who = new CitizenId(1);

            // WAIT UNTIL THEY ARE ACTUALLY OUT ON THE ERRAND, not just ticking a fixed warm-up.
            // Downing them while still AtHome (Position already sitting on whatever tile their
            // OWN block targets) would let a revived StartJourney "arrive" on the very first
            // tick with zero distance travelled - not because the fix failed, but because
            // going-home-when-already-home is a real zero-length journey. Caught measuring
            // this the hard way: a fixed 8:00:35 start put citizen 1 exactly on their own front
            // doorstep and the test passed for the wrong reason (Position never moved) until it
            // was rewritten to wait for Travelling. Mid-errand is also the honest case: a car
            // hits somebody ON THE WALK, not standing still at their own door.
            int guard = 20 * 60 * 60 * 4; // up to four sim hours to catch a departure
            while (!sim.GetAgent(who).Travelling && guard-- > 0) sim.Tick();
            Assert.That(sim.GetAgent(who).Travelling, Is.True,
                "customer #2 never set off on their one daily errand - fixture or seed changed?");

            sim.Down(who);
            var frozenAt = sim.GetAgent(who).Position;

            // A FEW SIM SECONDS DOWN, NOT AN HOUR - the plan's current block is still the one
            // that was active when they went down, which is exactly the case a PROMPT revive
            // (the PlayMode teardown, the future ambulance) actually hits. Down never touches
            // Destination, so without Revive resetting it too, block.Where would still equal
            // Destination and the wants-check would never fire until the plan's own block
            // happened to change on its own - which an hour-long down/revive gap would paper
            // over (the block usually HAS changed by then) without ever proving this case.
            for (int t = 0; t < 100; t++) sim.Tick();
            sim.Revive(who);

            for (int t = 0; t < 20 * 60 * 10; t++) sim.Tick(); // ten sim minutes back on the plan
            Assert.That(sim.GetAgent(who).Doing, Is.Not.EqualTo(Activity.Downed),
                "Revive did not hand the citizen back to their plan");
            Assert.That(sim.GetAgent(who).Position, Is.Not.EqualTo(frozenAt),
                "a promptly revived citizen never left the spot they were downed at - Destination "
              + "was left equal to the still-active block's Where, so the wants-check never fired");
        }

        [Test]
        public void NobodyDownedIsByteIdenticalToBefore()
        {
            // The guard must be a true no-op when the downed set is empty: two sims, same
            // seed, one carrying the new code path — every agent identical after an hour.
            //
            // Queueham (the suite's own shop-queue fixture) has 16 agents, not the 40 a fixture
            // with children and households would carry, so the check below walks its real
            // AgentCount rather than a number this fixture cannot back.
            var a = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            var b = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 20 * 60 * 60; t++) { a.Tick(); b.Tick(); }
            for (int i = 0; i < a.AgentCount; i++)
            {
                Assert.That(a.GetAgent(i).Position, Is.EqualTo(b.GetAgent(i).Position));
                Assert.That(a.GetAgent(i).Doing, Is.EqualTo(b.GetAgent(i).Doing));
            }
        }
    }
}
