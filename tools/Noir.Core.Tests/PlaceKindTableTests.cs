using System;
using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The kinds table exists to turn a silent failure into a loud one, so most of what is worth
    /// testing about it is what it REFUSES. A row that loads when it should not is the whole bug
    /// this file is here to catch.
    /// </summary>
    [TestFixture]
    public class PlaceKindTableTests
    {
        /// <summary>A complete row, for tests that want to break exactly one thing about it.</summary>
        private const string Complete = @"
kind shed
  words shed
  form building
  rooms auto
  grammar domestic
  roof yes
  frontage plain
  props none
  counter no
  hours none
  jobs 0
  roles none
  shifts one
  catchment no
  describe yes
";

        /// <summary>The seventeen enum kinds, so a test row can be added to a loadable table.</summary>
        private static string WithEveryEnumKind(string extra) =>
            TestContent.ReadRaw("kinds.txt") + "\n" + extra;

        [Test]
        public void AuthoredTableCoversEveryKindTheEnumNames()
        {
            var table = PlaceKindTable.Parse(TestContent.ReadRaw("kinds.txt"));

            foreach (PlaceKind kind in Enum.GetValues(typeof(PlaceKind)))
                Assert.That(table.Row(kind), Is.Not.Null, $"{kind} has no row in kinds.txt");
        }

        [Test]
        public void AKindWithNoRowFailsAtLoad()
        {
            // A perfectly well-formed table that simply does not mention the kinds the enum has.
            // This is the failure the whole table exists for: before it, a kind with no answers
            // rendered as a walled box, was staffed by nobody, and said nothing about either.
            var ex = Assert.Throws<PlaceKindTableException>(() => PlaceKindTable.Parse(Complete));

            Assert.That(ex.Message, Does.Contain("dwelling"));
            Assert.That(ex.Message, Does.Contain("mill"));
        }

        [Test]
        public void RolesWithoutStaffFailAtLoad()
        {
            var ex = Assert.Throws<PlaceKindTableException>(() => PlaceKindTable.Parse(
                WithEveryEnumKind(Complete.Replace("roles none", "roles barber"))));
            Assert.That(ex.Message, Does.Contain("barber"));
        }

        [Test]
        public void StaffWithoutRolesFailAtLoad()
        {
            var ex = Assert.Throws<PlaceKindTableException>(() => PlaceKindTable.Parse(
                WithEveryEnumKind(Complete.Replace("jobs 0", "jobs 2"))));
            Assert.That(ex.Message, Does.Contain("names no trade"));
        }

        [Test]
        public void AnIncompleteRowNamesWhatIsMissing()
        {
            var ex = Assert.Throws<PlaceKindTableException>(() => PlaceKindTable.Parse(
                WithEveryEnumKind(Complete.Replace("counter no", ""))));
            Assert.That(ex.Message, Does.Contain("counter"));
        }

        [Test]
        public void AnAttributeThatCannotApplyIsRefused()
        {
            // A service time on somewhere with no counter is a line somebody wrote and then
            // wondered about for an afternoon. Saying so is cheaper than letting it sit there.
            var ex = Assert.Throws<PlaceKindTableException>(() => PlaceKindTable.Parse(
                WithEveryEnumKind(Complete.Replace("counter no", "counter no\n  service 5 7"))));
            Assert.That(ex.Message, Does.Contain("service"));
        }

        [Test]
        public void TwoKindsMayNotShareAWord()
        {
            var ex = Assert.Throws<PlaceKindTableException>(() => PlaceKindTable.Parse(
                WithEveryEnumKind(Complete.Replace("words shed", "words shed pub"))));
            Assert.That(ex.Message, Does.Contain("pub"));
        }

        [Test]
        public void AKindTheEnumHasNeverHeardOfGetsAValueOfItsOwn()
        {
            var table = PlaceKindTable.Parse(WithEveryEnumKind(Complete));

            Assert.That(table.TryKindOf("shed", out var shed), Is.True);
            Assert.That((int)shed, Is.EqualTo(Enum.GetValues(typeof(PlaceKind)).Length),
                        "a kind only content names should be numbered after every enum member");
            Assert.That(table.Row(shed).Name, Is.EqualTo("shed"));

            // And every enum member keeps the number the compiled switches elsewhere assume.
            foreach (PlaceKind kind in Enum.GetValues(typeof(PlaceKind)))
                Assert.That(table.Row(kind).Kind, Is.EqualTo(kind));
        }

        [Test]
        public void RolesRunOutIntoTheLastOneNamed()
        {
            var surgery = PlaceKindTable.Parse(TestContent.ReadRaw("kinds.txt")).Row(PlaceKind.Surgery);

            Assert.That(surgery.RoleAt(0), Is.EqualTo("doctor"));
            Assert.That(surgery.RoleAt(1), Is.EqualTo("nurse"));
            Assert.That(surgery.RoleAt(7), Is.EqualTo("nurse"), "a fourth nurse is still a nurse");
        }

        [Test]
        public void ARoomCountNoGrammarTakesIsRefused()
        {
            // Better a stopped load than a number content wrote down and nothing ever read.
            var ex = Assert.Throws<PlaceKindTableException>(() => PlaceKindTable.Parse(
                WithEveryEnumKind(Complete.Replace("rooms auto", "rooms 3-5"))));
            Assert.That(ex.Message, Does.Contain("3-5"));
        }

        [Test]
        public void AGrammarNothingAnswersToIsRefused()
        {
            // Left to itself, InteriorGenerator falls back to the house rules â€” which is never
            // wrong for a building and always wrong for a hospital. The table says so instead.
            var ex = Assert.Throws<PlaceKindTableException>(() => PlaceKindTable.Parse(
                WithEveryEnumKind(Complete.Replace("grammar domestic", "grammar no-such-grammar"))));
            Assert.That(ex.Message, Does.Contain("no-such-grammar"));
        }


        private static string Join(IReadOnlyList<string> parts)
        {
            var copy = new List<string>(parts);
            return string.Join(" ", copy.ToArray());
        }

        private static string Spell(IReadOnlyList<OpenWindow> hours)
        {
            var parts = new List<string>();
            foreach (var w in hours) parts.Add($"{w.StartMinute}-{w.EndMinute}@{w.DaysMask}");
            return string.Join(" ", parts.ToArray());
        }
    }
}
