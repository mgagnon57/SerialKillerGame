using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Noir.Core.People;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The rows of the real `Content/animations.txt`, and the traps in editing it.
    /// </summary>
    [TestFixture]
    public class AnimationRowTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Noir", "Core", "World")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("no repo root above " + AppContext.BaseDirectory);
        }

        private static AnimationTable Real() =>
            AnimationTable.Parse(File.ReadAllText(
                Path.Combine(RepoRoot(), "Content", "animations.txt")));

        /// <summary>
        /// THE MOST VALUABLE TEST IN THIS FILE, and the cheapest fault to introduce.
        ///
        /// The parser writes `Paces[row] = 0` for EVERY row, and a pace of 0 means "play at
        /// exactly 1.00x". So splitting `moving` into `moving:male` and `moving:female` and
        /// forgetting the `1.5m/s` on one of them does not throw, does not warn, and silently
        /// puts the sliding feet back into half the town - the exact fault the figure exists to
        /// cure, reintroduced by an edit that looks obviously correct.
        /// </summary>
        [Test]
        public void EveryLocomotionRowCarriesTheSpeedItWasAnimatedAt()
        {
            var table = Real();
            var naked = table.Rows.Keys
                             .Where(k => k.StartsWith("moving", StringComparison.OrdinalIgnoreCase))
                             .Where(k => table.PaceFor(k) <= 0f)
                             .OrderBy(k => k, StringComparer.Ordinal)
                             .ToList();

            Assert.That(naked, Is.Empty,
                        $"{naked.Count} locomotion row(s) have no m/s figure, so they play at "
                      + "exactly 1.00x and those people's feet slide: " + string.Join(", ", naked));
        }

        [Test]
        public void TheDeadRunningRowIsGone()
        {
            var table = Real();

            Assert.That(table.Rows.ContainsKey("hurrying"), Is.False,
                        "`hurrying` required moving while at the playground, which the simulation "
                      + "never produces - it never played a frame. Deleted 2026-08-08.");
            Assert.That(table.Rows.Values.SelectMany(v => v), Has.No.Member("Running"));
        }

        [Test]
        public void EverySelectorNamesARealPlaceKind()
        {
            // A qualified key that matches nothing falls through IN SILENCE, so a typo costs
            // nothing and never fires. The only defence is checking the names against kinds.txt.
            var kinds = File.ReadAllLines(Path.Combine(RepoRoot(), "Content", "kinds.txt"))
                            .Where(l => l.TrimStart().StartsWith("kind ", StringComparison.Ordinal))
                            .Select(l => l.Trim().Split(' ')[1])
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var bogus = Real().Rows.Keys
                            .Where(k => k.Contains('@'))
                            .Select(k => k.Split('@')[1].Split(':')[0])
                            .Where(place => !kinds.Contains(place))
                            .Distinct()
                            .ToList();

            Assert.That(bogus, Is.Empty,
                        "row(s) qualified by a place that is not a kind in kinds.txt, so they can "
                      + "never fire: " + string.Join(", ", bogus));
        }

        [Test]
        public void EveryPersonSelectorIsOneOfTheFiveTheLadderKnows()
        {
            var known = new[] { "child", "adult", "elder", "male", "female" };

            var bogus = Real().Rows.Keys
                            .Where(k => k.Contains(':'))
                            .Select(k => k.Split(':')[1])
                            .Where(p => !known.Contains(p, StringComparer.OrdinalIgnoreCase))
                            .Distinct()
                            .ToList();

            Assert.That(bogus, Is.Empty,
                        "person selector(s) the ladder does not know, so they never fire: "
                      + string.Join(", ", bogus));
        }

        [Test]
        public void ChildrenDoNotSmokeAtHome()
        {
            var athome = Real().ClipsFor("athome:child");

            Assert.That(athome, Is.Not.Null, "athome:child should exist - see SIM-7");
            Assert.That(athome, Has.No.Member("Smoking"));
            Assert.That(athome, Has.No.Member("Gaming"), "a games console is not a 1991 farm town");
        }

        [Test]
        public void TheDinerIsNotTheTavern()
        {
            var diner = Real().ClipsFor("atthepub@diner");

            Assert.That(diner, Is.Not.Null, "atthepub@diner should exist - see SIM-4");
            Assert.That(diner, Has.No.Member("Drunk Idle"),
                        "four in ten lunchtime diner customers were playing Drunk Idle");
            Assert.That(diner, Has.No.Member("Drunk Idle Variation"));
        }

        [Test]
        public void AFarmerDoesNotUseAFaxMachine()
        {
            var farm = Real().ClipsFor("atwork@farm");

            Assert.That(farm, Is.Not.Null, "atwork@farm should exist - see SIM-5");
            Assert.That(farm, Has.No.Member("Using A Fax Machine"));
            Assert.That(farm, Has.No.Member("Typing"));
        }

        [Test]
        public void IdleAndStandingIdle03HaveARowSomebodyCanActuallyReach()
        {
            var table = Real();

            // They were stranded on `default`, which is the net rather than a destination.
            bool reachable = table.Rows
                .Where(r => !r.Key.Equals("default", StringComparison.OrdinalIgnoreCase))
                .Any(r => r.Value.Contains("Idle") && r.Value.Contains("Standing Idle 03"));

            Assert.That(reachable, Is.True,
                        "Idle and Standing Idle 03 exist only on the default net - see ROW-7");
        }
    }
}
