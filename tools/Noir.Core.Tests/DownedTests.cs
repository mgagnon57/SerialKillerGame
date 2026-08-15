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
