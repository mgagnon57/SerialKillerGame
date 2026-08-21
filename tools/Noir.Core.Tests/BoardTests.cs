using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The cruiser's half of the officer's journey: a dispatched (Responding) citizen boards
    /// the car — present in the population, absent from the world, the same AwayFromTown
    /// presentation the ambulance and the commuters already exercise — and alights at a kerb
    /// tile, from which RespondTick walks the last stretch to the scene. Board requires
    /// Responding because only a dispatched officer ever rides; Release clears the ride too,
    /// so the teardown/CloseLoudly path can never leak a ghost inside a car.
    /// </summary>
    [TestFixture]
    public class BoardTests
    {
        /// <summary>RespondTests' own known-walkable spot: y=13 is the fixture's main road.</summary>
        private static readonly Tile Scene = new Tile(42, 13);
        private static readonly Tile Kerb = new Tile(38, 13);

        [Test]
        public void ABoardedOfficerIsNotDrawnNotHitAndDoesNotWalk()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();

            var who = new CitizenId(1);
            sim.Respond(who, Scene);
            sim.Board(who);

            Assert.That(sim.GetAgent(who).Aboard, Is.True);
            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.AwayFromTown),
                "aboard must present as AwayFromTown so every not-drawn arm already applies");

            var at = sim.GetAgent(who).Position;
            for (int t = 0; t < GameClock.TicksPerMinute * 10; t++) sim.Tick();
            Assert.That(sim.GetAgent(who).Position, Is.EqualTo(at),
                "a boarded officer walked - RespondTick must not run while Aboard");
        }

        [Test]
        public void AlightPlacesThemAtTheKerbAndTheyWalkOn()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();

            var who = new CitizenId(1);
            sim.Respond(who, Scene);
            sim.Board(who);
            sim.Alight(who, Kerb);

            Assert.That(sim.GetAgent(who).Aboard, Is.False);
            Assert.That(sim.GetAgent(who).Position, Is.EqualTo(Vec2.CentreOf(Kerb)),
                "Alight must set them down at the kerb tile itself");
            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.Responding));

            bool arrived = false;
            for (int t = 0; t < GameClock.TicksPerMinute * 15 && !arrived; t++)
            {
                sim.Tick();
                arrived = !sim.GetAgent(who).Travelling
                       && sim.GetAgent(who).Position.ToTile() == Scene;
            }
            Assert.That(arrived, Is.True,
                "the alighted officer never walked the last stretch from the kerb to the scene");
        }

        [Test]
        public void ReleaseWhileAboardClearsEverything()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();

            var who = new CitizenId(1);
            sim.Respond(who, Scene);
            sim.Board(who);
            sim.Release(who);

            Assert.That(sim.GetAgent(who).Aboard, Is.False);
            Assert.That(sim.GetAgent(who).Responding, Is.False);

            bool rejoined = false;
            for (int t = 0; t < GameClock.TicksPerMinute * 10 && !rejoined; t++)
            {
                sim.Tick();
                rejoined = sim.GetAgent(who).Travelling
                        || sim.GetAgent(who).Doing != Activity.AwayFromTown;
            }
            Assert.That(rejoined, Is.True,
                "a released rider never rejoined their plan - the teardown path leaked a ghost");
        }

        [Test]
        public void BoardRequiresRespondingAndIsIdempotent()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();

            var bystander = new CitizenId(2);
            sim.Board(bystander);
            Assert.That(sim.GetAgent(bystander).Aboard, Is.False,
                "Board on an un-Responding citizen must be a no-op");

            var who = new CitizenId(1);
            sim.Respond(who, Scene);
            sim.Board(who);
            sim.Board(who);                      // double board is a no-op
            Assert.That(sim.GetAgent(who).Aboard, Is.True);

            sim.Alight(bystander, Kerb);         // never aboard: a no-op
            Assert.That(sim.GetAgent(bystander).Position, Is.Not.EqualTo(Vec2.CentreOf(Kerb)),
                "Alight on the un-boarded must not teleport anybody");
        }

        [Test]
        public void NobodyBoardedIsByteIdenticalToBefore()
        {
            // Same shape as NobodyTakenAwayIsByteIdenticalToBefore: the new tick-loop gate is
            // a true no-op when Aboard is never set on anybody.
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
