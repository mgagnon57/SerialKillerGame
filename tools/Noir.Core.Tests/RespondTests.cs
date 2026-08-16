using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The first off-plan walk: an officer sent to a scene nobody's plan staged for them.
    /// Respond/Release is Down/Revive's own shape reused for a live citizen rather than a
    /// downed one — same entry cleanup, same "Destination = None fires the wants-check within
    /// the minute" trick on the way out — so most of this suite is proving that shape holds
    /// for a person who is still walking around under their own power the whole time.
    /// </summary>
    [TestFixture]
    public class RespondTests
    {
        // Out on Queueham's main road (y=13), far enough from any one house that no test here
        // can pass by already standing on it.
        private static readonly Tile Scene = new Tile(42, 13);

        [Test]
        public void ARespondingCitizenWalksToTheTileAndStandsThere()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            var who = new CitizenId(1);

            sim.Respond(who, Scene);

            bool arrived = false;
            for (int t = 0; t < 20 * 60 * 20; t++)   // up to 20 sim minutes
            {
                sim.Tick();
                if (!sim.GetAgent(who).Travelling
                    && sim.GetAgent(who).Doing == Activity.Responding
                    && sim.GetAgent(who).Position.ToTile() == Scene)
                {
                    arrived = true;
                    break;
                }
            }

            Assert.That(arrived, Is.True, "the officer never reached the scene within 20 sim minutes");
            Assert.That(sim.GetAgent(who).Position.ToTile(), Is.EqualTo(Scene));
            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.Responding));
            Assert.That(sim.GetAgent(who).Heading, Is.EqualTo(Vec2.Zero));
            Assert.That(sim.GetAgent(who).Travelling, Is.False);
        }

        [Test]
        public void ReleaseHandsThemBackToThePlanPromptly()
        {
            // Same shape as ARevivedAgentDepartsPromptlyEvenMidBlock: Release resets
            // Destination the way Revive does, so the citizen is picked back up onto their
            // plan within the minute rather than waiting for the plan's own block to change.
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            var who = new CitizenId(1);

            sim.Respond(who, Scene);

            bool arrived = false;
            for (int t = 0; t < 20 * 60 * 20; t++)
            {
                sim.Tick();
                if (!sim.GetAgent(who).Travelling
                    && sim.GetAgent(who).Doing == Activity.Responding
                    && sim.GetAgent(who).Position.ToTile() == Scene)
                {
                    arrived = true;
                    break;
                }
            }
            Assert.That(arrived, Is.True, "setup: the officer never reached the scene");

            sim.Release(who);

            bool released = false;
            for (int t = 0; t < 20 * 60 * 10; t++)   // ten sim minutes
            {
                sim.Tick();
                if (sim.GetAgent(who).Doing != Activity.Responding) { released = true; break; }
            }
            Assert.That(released, Is.True,
                "Release did not hand the citizen back to their plan within ten sim minutes");
            Assert.That(sim.GetAgent(who).Responding, Is.False);
        }

        [Test]
        public void ReleaseMidWalkHandsThemBackToThePlanPromptly()
        {
            // The reviewer's own hand-trace said this already works via the ordinary
            // wants-check (Release resets Destination, same as Revive), and this pins it: catch
            // the officer WHILE Travelling, before arrival, and confirm the interruption lands
            // the same way the post-arrival case does.
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            var who = new CitizenId(1);

            sim.Respond(who, Scene);

            int guard = 20 * 60 * 5;
            while (!sim.GetAgent(who).Travelling && guard-- > 0) sim.Tick();
            Assert.That(sim.GetAgent(who).Travelling, Is.True, "setup: the officer never set off");
            Assert.That(sim.GetAgent(who).Position.ToTile(), Is.Not.EqualTo(Scene),
                "setup: arrived before Release could catch them mid-walk - Scene moved closer?");

            sim.Release(who);

            bool released = false;
            for (int t = 0; t < 20 * 60 * 10; t++)   // ten sim minutes
            {
                sim.Tick();
                if (sim.GetAgent(who).Doing != Activity.Responding) { released = true; break; }
            }
            Assert.That(released, Is.True,
                "Release mid-walk did not hand the citizen back to their plan within ten sim minutes");
            Assert.That(sim.GetAgent(who).Responding, Is.False);

            // Genuinely back on the plan, not just relabelled: either still walking (now toward
            // wherever the plan actually wants them) or already arrived somewhere.
            Assert.That(sim.GetAgent(who).Travelling || sim.GetAgent(who).At != PlaceId.None, Is.True,
                "a released mid-walk citizen should be travelling toward, or arrived at, a plan "
              + "destination, not stood still with nowhere to be");
        }

        [Test]
        public void ARespondingOfficerWhoCannotPathBecomesStrandedAndStaysPut()
        {
            // pathNodeCap: 1 is the constructor's own diagnostic seam (Simulation -> Pathfinder's
            // maxNodesExamined): every search examines at most a node or two before running out
            // of budget, so a ~30-tile walk GaveUp's rather than Found's every single time. This
            // is the case the backoff exists for - NoRouteExists costs nothing to ask again
            // (Regions answers it in O(1)), but GaveUp means a real search ran, and this fixture
            // forces every attempt to be one.
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60,
                                      pathNodeCap: 1);
            var who = new CitizenId(1);

            sim.Respond(who, Scene);

            bool stranded = false;
            Vec2 positionAtStrand = default;
            for (int t = 0; t < 20 * 60 * 5; t++)   // five sim minutes - many retry cycles
            {
                sim.Tick();
                if (sim.GetAgent(who).Stranded)
                {
                    stranded = true;
                    positionAtStrand = sim.GetAgent(who).Position;
                    break;
                }
            }
            Assert.That(stranded, Is.True,
                "an officer who cannot path within the node budget should be marked Stranded - "
              + "the honest state - rather than silently standing forever");
            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.Responding));
            Assert.That(sim.GetAgent(who).Travelling, Is.False);
            Assert.That(sim.GetAgent(who).Heading, Is.EqualTo(Vec2.Zero));

            // Keep ticking well past several backoff cycles: the officer neither creeps nor
            // wedges the sim - the tick loop keeps running, they just keep standing exactly
            // where the failed attempt left them.
            for (int t = 0; t < 20 * 60 * 3; t++) sim.Tick();
            Assert.That(sim.GetAgent(who).Position, Is.EqualTo(positionAtStrand),
                "a stranded responder moved without ever having found a path");
            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.Responding));
        }

        [Test]
        public void TheBackoffItselfIsByteIdenticalAcrossTwoIdenticalRuns()
        {
            // NobodyRespondingIsByteIdenticalToBefore proves the gate is a no-op when unused.
            // This is the companion case: the gate FIRING every tick, hitting Strand's own
            // shared state (_retryDelay, _retryAtTick, _strandedCount) the whole way through,
            // still consumes no RNG and produces the same result on two identical runs.
            var a = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60,
                                    pathNodeCap: 1);
            var b = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60,
                                    pathNodeCap: 1);
            a.Respond(new CitizenId(1), Scene);
            b.Respond(new CitizenId(1), Scene);

            for (int t = 0; t < 20 * 60 * 10; t++) { a.Tick(); b.Tick(); }

            for (int i = 0; i < a.AgentCount; i++)
            {
                Assert.That(a.GetAgent(i).Position, Is.EqualTo(b.GetAgent(i).Position));
                Assert.That(a.GetAgent(i).Doing, Is.EqualTo(b.GetAgent(i).Doing));
                Assert.That(a.GetAgent(i).Stranded, Is.EqualTo(b.GetAgent(i).Stranded));
            }
        }

        [Test]
        public void RespondWorksFromAsleep()
        {
            // 02:00 - well before WakeTime's earliest case (05:00, Farmer/FarmHand), and
            // Queueham's fixture has neither, so every citizen is Asleep at construction.
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 2 * 60);

            var sleeper = CitizenId.None;
            for (int i = 0; i < sim.AgentCount; i++)
            {
                if (sim.GetAgent(i).Doing == Activity.Asleep) { sleeper = new CitizenId(i); break; }
            }
            Assert.That(sleeper.IsValid, Is.True,
                "nobody in the fixture is Asleep at 02:00 - fixture or WakeTime changed?");

            var home = sim.GetAgent(sleeper).Position.ToTile();
            var target = new Tile(home.X, 13);   // out on the main road, near their own door

            sim.Respond(sleeper, target);

            bool arrived = false;
            for (int t = 0; t < 20 * 60 * 20; t++)
            {
                sim.Tick();
                if (!sim.GetAgent(sleeper).Travelling
                    && sim.GetAgent(sleeper).Doing == Activity.Responding
                    && sim.GetAgent(sleeper).Position.ToTile() == target)
                {
                    arrived = true;
                    break;
                }
            }
            Assert.That(arrived, Is.True,
                "an officer called out of Asleep never reached the scene - the plan should not "
              + "be consulted at all while Responding holds");
        }

        [Test]
        public void DownClearsAResponse()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            var who = new CitizenId(1);

            sim.Respond(who, Scene);
            for (int t = 0; t < 50; t++) sim.Tick();   // mid-walk, nowhere near arrived

            sim.Down(who);

            Assert.That(sim.GetAgent(who).Responding, Is.False,
                "Down did not clear Responding on a responding officer");
            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.Downed));
        }

        [Test]
        public void RespondIsANoOpWhenDownedOrAwayOrAlreadyResponding()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();

            var downed = new CitizenId(1);
            sim.Down(downed);
            sim.Respond(downed, Scene);
            Assert.That(sim.GetAgent(downed).Responding, Is.False,
                "Respond must not pick up a downed citizen");

            var away = new CitizenId(2);
            sim.Down(away);
            sim.TakeAway(away, int.MaxValue);
            sim.Respond(away, Scene);
            Assert.That(sim.GetAgent(away).Responding, Is.False,
                "Respond must not pick up a citizen the ambulance already has");

            var officer = new CitizenId(3);
            var elsewhere = new Tile(50, 13);
            sim.Respond(officer, Scene);
            sim.Respond(officer, elsewhere);   // already Responding - must not retarget
            Assert.That(sim.GetAgent(officer).RespondTarget, Is.EqualTo(Scene));
        }

        [Test]
        public void ReleaseIsANoOpWhenNotResponding()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();

            var who = new CitizenId(1);
            var doingBefore = sim.GetAgent(who).Doing;
            var destinationBefore = sim.GetAgent(who).Destination;

            sim.Release(who);   // never was Responding

            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(doingBefore));
            Assert.That(sim.GetAgent(who).Destination, Is.EqualTo(destinationBefore));
        }

        [Test]
        public void NobodyRespondingIsByteIdenticalToBefore()
        {
            // Same shape as NobodyDownedIsByteIdenticalToBefore / NobodyTakenAwayIsByteIdenticalToBefore:
            // proves the new tick-loop gate is a true no-op when Responding is never set on anybody.
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
