using NUnit.Framework;
using Noir.Core.People;

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
}
