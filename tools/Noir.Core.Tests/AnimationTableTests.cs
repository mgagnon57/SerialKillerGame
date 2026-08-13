using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Noir.Core.People;

namespace Noir.Core.Tests
{
    /// <summary>
    /// THE TABLE PARSER, WHICH UNTIL NOW COULD ONLY BE REACHED THROUGH A PLAYMODE RUN.
    ///
    /// It lived inside `AgentAnimation` in the Unity assembly, so exercising it cost six to
    /// fifteen minutes and in practice nothing exercised it at all. Every one of these cases is
    /// a thing the file can actually contain and nothing had ever asserted on.
    /// </summary>
    [TestFixture]
    public class AnimationTableTests
    {
        [Test]
        public void ASituationTakesTheFirstWordAndTheRestAreClips()
        {
            var table = AnimationTable.Parse("atwork  Typing, Writing, Counting\ndefault  Idle");

            Assert.That(table.ClipsFor("atwork"), Is.EqualTo(new[] { "Typing", "Writing", "Counting" }));
        }

        [Test]
        public void ASituationIsFoundWhateverItsCase()
        {
            // Resolve() lowercases an Activity name to look it up, and the file is written by hand.
            var table = AnimationTable.Parse("AtWork  Typing\ndefault  Idle");

            Assert.That(table.ClipsFor("atwork"), Is.EqualTo(new[] { "Typing" }));
        }

        [Test]
        public void APaceIsReadOffTheRowAndRemovedFromTheClips()
        {
            var table = AnimationTable.Parse("moving  1.5m/s  Walking, Walking Male\ndefault  Idle");

            Assert.That(table.PaceFor("moving"), Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(table.ClipsFor("moving"), Is.EqualTo(new[] { "Walking", "Walking Male" }),
                        "the pace must not survive into the clip list");
        }

        [Test]
        public void ARowWithNoPaceReportsZeroRatherThanGuessing()
        {
            var table = AnimationTable.Parse("atwork  Typing\ndefault  Idle");

            Assert.That(table.PaceFor("atwork"), Is.EqualTo(0f));
        }

        [Test]
        public void AClipWhoseNameStartsWithANumberIsNotEatenAsAPace()
        {
            // Mixamo ships a clip called "180 Turn". The unit is on the pace deliberately so a
            // bare leading number cannot be mistaken for one.
            var table = AnimationTable.Parse("turning  180 Turn\ndefault  Idle");

            Assert.That(table.ClipsFor("turning"), Is.EqualTo(new[] { "180 Turn" }));
            Assert.That(table.PaceFor("turning"), Is.EqualTo(0f));
        }

        [Test]
        public void APeriodInAClipNameIsReportedBecauseUnityWillRenameTheState()
        {
            var table = AnimationTable.Parse("travellingto  Standing Idle Looking Ver. 1\ndefault  Idle");

            Assert.That(table.Warnings.Any(w => w.Contains("Ver. 1") && w.Contains("period")),
                        "the fault that cost one villager in six a treadmilling walk cycle has to "
                      + "be named by the parser, not just by a test that reads the controller: "
                      + $"got [{string.Join(" | ", table.Warnings)}]");
        }

        [Test]
        public void AMissingDefaultRowIsReported()
        {
            var table = AnimationTable.Parse("atwork  Typing");

            Assert.That(table.Warnings.Any(w => w.Contains("default")),
                        "a situation with no row of its own has nothing to fall back to");
        }

        [Test]
        public void AnEmptyFileIsSurvivableAndSaysSo()
        {
            var table = AnimationTable.Parse("");

            Assert.That(table.Rows, Is.Empty);
            Assert.That(table.ClipsFor("atwork"), Is.Null, "a lookup must be null, not a throw");
            Assert.That(table.Warnings, Is.Not.Empty);
        }
    
        /// <summary>
        /// EVERY `Activity` HAS A ROW IN THE REAL FILE, ASKED OF THE ENUM RATHER THAN OF THE TABLE.
        ///
        /// `AgentAnimation.Resolve` falls back to `default` when a situation has no row, so an
        /// Activity nobody wrote a row for makes everybody doing it play a generic idle FOREVER,
        /// with nothing said - the exact "does not throw, it falls through to a default" trap
        /// CLAUDE.md names for kinds.txt, in a second file.
        ///
        /// `AnimationCheck` in the Editor assembly already sweeps this, correctly and with the
        /// reasoning written out. But it WARNS, and it only runs when somebody invokes it from
        /// the Noir menu - which is a rule where a gate would do. Both `Activity` and
        /// `AnimationTable.Parse` are Core and `Content/animations.txt` is copied to the test
        /// output, so this costs nothing and runs on every commit.
        ///
        /// It earned its place immediately: `Activity.OnTheAllotment` was renamed to `InTheGarden`
        /// in the same commit that added this, and the row key is derived as
        /// `Doing.ToString().ToLowerInvariant()` - so the rename and the content row had to move
        /// together or every gardener in Rossville would have stood still.
        /// </summary>
        [Test]
        public void EveryActivityHasARowInTheRealFile()
        {
            var table = AnimationTable.Parse(File.ReadAllText(
                Path.Combine(RepoRoot(), "Content", "animations.txt")));

            var rowless = new List<string>();
            foreach (Activity doing in Enum.GetValues(typeof(Activity)))
            {
                // AwayFromTown is deliberately not drawn - those people are off the map and
                // AgentMeshView switches their figure off entirely, so a row would never be
                // reached. Its absence is correct rather than a gap; same reasoning as
                // AnimationCheck, which is where this rule was written down first.
                if (doing == Activity.AwayFromTown) continue;

                if (table.ClipsFor(doing.ToString().ToLowerInvariant()) == null)
                    rowless.Add(doing.ToString());
            }

            Assert.That(rowless, Is.Empty,
                "These Activity states have no row in Content/animations.txt: "
              + string.Join(", ", rowless) + ". "
              + "AgentAnimation.Resolve falls back to `default`, so everybody doing them plays a "
              + "generic idle forever and nothing is logged. The row key is the Activity name "
              + "lowercased, so renaming one means renaming the row in the same commit.");
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Noir", "Core", "Observation")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not find Assets/Noir/Core/Observation above " + AppContext.BaseDirectory);
        }
}
}
