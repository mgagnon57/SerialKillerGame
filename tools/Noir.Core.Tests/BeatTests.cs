using NUnit.Framework;
using Noir.Core.People;
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;

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

        // 6 + [0,6) base, plus 400 + [0,400) for the beat.
        private const int PlainShortest = 6, PlainLongest = 11;
        private const int LingerShortest = 406, LingerLongest = 811;

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
    }
}
