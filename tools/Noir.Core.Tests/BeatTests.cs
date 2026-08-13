using NUnit.Framework;
using Noir.Core.People;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.Sim;
using Noir.Sim;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The bridge from an authored clause to something a watcher could see.
    ///
    /// These assert on the PARSE and on the enum's shape. Whether a beat reaches anybody is a
    /// different question and lives in BeatsAreEnactedTests.
    /// </summary>
    [TestFixture]
    public class BeatTests
    {
        [Test]
        public void AnUnrecognisedTagIsIgnoredRatherThanRefused()
        {
            // The file already carries `# elder`, `# m` and `# f` for a scoping system that does
            // not exist yet. A parser that threw on those would make writing content a matter of
            // remembering what the code knows about. `roundabout` is now one of those words: the
            // beat is gone, and a line still tagged with it must parse to None rather than throw.
            var table = ParticularsTable.Parse(
                "walks the same lane every evening   # roundabout\n");

            Assert.That(table.Count, Is.EqualTo(1));
            Assert.That(table.BeatAt(0), Is.EqualTo(Beat.None),
                "a tag no beat answers to should leave the clause plain, not throw");
        }

        [Test]
        public void TheTwoSurvivingTagsStillParse()
        {
            var table = ParticularsTable.Parse(
                "carries a stick and does not lean on it   # carries\n"
              + "waits outside for eleven minutes   # lingers\n");

            Assert.That(table.BeatAt(0), Is.EqualTo(Beat.Carries));
            Assert.That(table.BeatAt(1), Is.EqualTo(Beat.Lingers));
        }
    }

    /// <summary>
    /// The door pause, with and without the beat.
    ///
    /// Queueham is used because everybody in it crosses a threshold twice or more a day by
    /// construction. Its own DoorwayTests already pin the 6-11 range for a village with no beats
    /// in it at all; this adds one lingerer and asserts that ONLY that person changes.
    /// </summary>
    [TestFixture]
    public class LingeringDoorTests
    {
        private const int StartHour = 6;
        private const int Minutes = 240;
        private const int Lingerer = 1;

        // 6 + [0,6) base (max 5, since Rolls.Int's upper bound is exclusive), plus
        // 400 + [0,400) for the beat (max 399, exclusive again): 11 + 400 + 399 = 810.
        private const int PlainShortest = 6, PlainLongest = 11;
        private const int LingerShortest = 406, LingerLongest = 810;

        [Test]
        public void ALingererStandsAtTheDoorLongEnoughToBeSeen()
        {
            var world = Queueham.World;
            var people = Queueham.PeopleWith(i => i == Lingerer ? Beat.Lingers : Beat.None);
            var sim = new Simulation(world, people, Queueham.Seed, StartHour * 60);

            var wasPaused = new int[sim.AgentCount];
            int lingererPauses = 0, plainPauses = 0;

            for (int tick = 0; tick < Minutes * GameClock.TicksPerMinute; tick++)
            {
                sim.Tick();

                for (int i = 0; i < sim.AgentCount; i++)
                {
                    int now = sim.GetAgent(i).DoorPauseTicks;

                    if (wasPaused[i] == 0 && now > 0)
                    {
                        if (i == Lingerer)
                        {
                            lingererPauses++;
                            Assert.That(now, Is.InRange(LingerShortest, LingerLongest),
                                $"the lingerer paused for {now} ticks");
                        }
                        else
                        {
                            plainPauses++;
                            Assert.That(now, Is.InRange(PlainShortest, PlainLongest),
                                $"agent {i} holds no beat but paused for {now} ticks — the "
                              + "base draw has moved, so the whole village has moved");
                        }
                    }

                    wasPaused[i] = now;
                }
            }

            Assert.That(lingererPauses, Is.GreaterThan(0), "the lingerer never crossed a threshold");
            Assert.That(plainPauses, Is.GreaterThan(0), "nobody else crossed one either");
        }

        /// <summary>
        /// The extra 20-40s per threshold defers arrival; it must not leave the lingerer stuck
        /// on a doorstep for the rest of the run. Mirrors
        /// <see cref="DoorwayTests.APauseNeverStrandsAnybodyMidJourney"/>, which pins this same
        /// invariant for a village with no beats in it at all.
        /// </summary>
        [Test]
        public void ALingererStillGetsEverywhere()
        {
            var world = Queueham.World;
            var people = Queueham.PeopleWith(i => i == Lingerer ? Beat.Lingers : Beat.None);
            var sim = new Simulation(world, people, Queueham.Seed, StartHour * 60);

            sim.Tick(GameClock.TicksPerMinute * Minutes);

            var lingerer = sim.GetAgent(Lingerer);

            Assert.That(lingerer.DoorPauseTicks, Is.LessThanOrEqualTo(LingerLongest),
                $"the lingerer has been on a threshold for {lingerer.DoorPauseTicks} ticks — " +
                "stranded, not merely slow");

            int travelling = 0;
            for (int i = 0; i < sim.AgentCount; i++)
                if (sim.GetAgent(i).Travelling) travelling++;

            Assert.That(travelling, Is.LessThan(sim.AgentCount / 3),
                $"{travelling} of {sim.AgentCount} still walking after four hours — the extra " +
                "pause is stranding somebody, not just delaying them");
        }
    }

    /// <summary>
    /// The whole point, asserted at the far end.
    ///
    /// Runs the real village rather than a fixture, because what is being tested is whether the
    /// authored content reaches a watcher — and the content is the thing under test. Two days
    /// rather than the instrument's fourteen: this asks whether the manner appears at all, not
    /// what the ratio is.
    /// </summary>
    [TestFixture]
    public class BeatsAreEnactedTests
    {
        [Test]
        public void SomebodyWhoseParticularsSayTheyLingerIsSeenLingering()
        {
            var ctx = VillageContext.Load();
            var logs = Eyewitness.WatchAll(ctx, 2);

            int lingerers = 0, seenLingering = 0;

            for (int i = 0; i < logs.Length; i++)
            {
                var who = ctx.People.Get(new CitizenId(i));
                if (who == null || (who.Beats & Beat.Lingers) == 0) continue;

                lingerers++;
                foreach (Observed o in logs[i].Entries)
                {
                    if ((o.Manner & ObservedManner.Lingering) == 0) continue;
                    seenLingering++;
                    break;
                }
            }

            Assert.That(lingerers, Is.GreaterThan(0),
                "no villager drew a clause tagged `# lingers` — the editorial pass has not "
              + "reached anybody, so this proves nothing either way");

            Assert.That(seenLingering * 2, Is.GreaterThanOrEqualTo(lingerers),
                $"only {seenLingering} of {lingerers} lingerers were ever seen on a threshold "
              + "in two days — the pause is too short for a watcher who looks once a minute");
        }

        [Test]
        public void TheSentenceAndTheBagAreTheSameFact()
        {
            // A citizen who drew a clause tagged `# carries` must BE a carrier. This is the
            // property that deriving beats from particulars exists to guarantee: the sentence an
            // inspector prints and the thing a watcher sees can never be two facts that merely
            // happen to agree.
            var ctx = VillageContext.Load();
            int carriers = 0;

            for (int i = 0; i < ctx.People.Count; i++)
            {
                var who = ctx.People.Get(new CitizenId(i));
                if (who == null) continue;

                bool clauseSaysSo = false;
                foreach (int p in who.Particulars)
                    if ((ctx.Particulars.BeatAt(p) & Beat.Carries) != 0) clauseSaysSo = true;

                bool beatSaysSo = (who.Beats & Beat.Carries) != 0;
                Assert.That(beatSaysSo, Is.EqualTo(clauseSaysSo),
                    $"citizen {i} holds Beat.Carries={beatSaysSo} but their clauses say "
                  + $"{clauseSaysSo} — the two have come apart");

                if (clauseSaysSo) carriers++;
            }

            Assert.That(carriers, Is.GreaterThan(0),
                "nobody in the village drew a clause tagged `# carries`");
        }

        /// <summary>
        /// The other half of the same claim as
        /// <see cref="SomebodyWhoseParticularsSayTheyLingerIsSeenLingering"/>: it is not enough
        /// for a citizen to hold <see cref="Beat.Carries"/>, a watcher must actually record
        /// <see cref="ObservedManner.Carrying"/> against them. 16 carriers in the village, so a
        /// majority-seen bar (like the lingering test's) is the right shape, not a bare
        /// greater-than-zero.
        ///
        /// Scoped to <see cref="ObservedAct.CameOut"/> specifically, not to any entry bearing
        /// Manner.Carrying: leaving Shopping or InTheGarden also sets
        /// <c>AgentState.Carrying</c> (Simulation.cs:677), so a citizen could be seen carrying on
        /// the way home from an errand whether or not `Beat.Carries` wired anything at all. Coming
        /// out of your OWN front door cannot follow either of those activities, so "carrying" on
        /// `CameOut` is reachable only through the beat — which is exactly what makes this
        /// discriminate rather than pass by coincidence.
        /// </summary>
        [Test]
        public void SomebodyWhoseParticularsSayTheyCarryIsSeenCarrying()
        {
            var ctx = VillageContext.Load();
            var logs = Eyewitness.WatchAll(ctx, 2);

            int carriers = 0, seenCarrying = 0;

            for (int i = 0; i < logs.Length; i++)
            {
                var who = ctx.People.Get(new CitizenId(i));
                if (who == null || (who.Beats & Beat.Carries) == 0) continue;

                carriers++;
                foreach (Observed o in logs[i].Entries)
                {
                    if (o.Act != ObservedAct.CameOut) continue;
                    if ((o.Manner & ObservedManner.Carrying) == 0) continue;
                    seenCarrying++;
                    break;
                }
            }

            Assert.That(carriers, Is.GreaterThan(0),
                "no villager drew a clause tagged `# carries` — the editorial pass has not "
              + "reached anybody, so this proves nothing either way");

            Assert.That(seenCarrying * 2, Is.GreaterThanOrEqualTo(carriers),
                $"only {seenCarrying} of {carriers} carriers were ever seen carrying while "
              + "coming out of their own door in two days — the flag is set but nothing "
              + "observable follows from it");
        }
    }
}
