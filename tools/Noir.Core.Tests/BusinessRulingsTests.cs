using System;
using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Survey;

namespace Noir.Core.Tests
{
    /// <summary>
    /// A ruling in business-1991.txt is keyed on a Place's handle, and a terrace lot now hands
    /// out several of those instead of one. If the frontage or the RNG sequencing upstream of
    /// DowntownFromSanborn ever shifts, an old ruling can point at a storefront that no longer
    /// exists — silently, unless something says so. This is that something.
    /// </summary>
    [TestFixture]
    public class BusinessRulingsTests
    {
        /// <summary>Same double RulingsTests.cs uses: a content source made of strings, with a
        /// fresh timestamp per instance so a new Given() always forces a reparse.</summary>
        private sealed class Fake : IContentSource
        {
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
            private static int _tick;
            private readonly DateTime _stamp = new DateTime(2026, 8, 12).AddSeconds(++_tick);

            public Fake Put(string name, string text) { _files[name] = text; return this; }
            public string Read(string name) =>
                _files.TryGetValue(name, out var t) ? t : throw new System.IO.FileNotFoundException(name);
            public DateTime WrittenAt(string name) => _files.ContainsKey(name) ? _stamp : default;
        }

        private static void Given(string body) =>
            Content.Install(new Fake().Put(BusinessRulings.FileName, body));

        [Test]
        public void ARuledUnitThatMatchesAPlaceIsNotUnmatched()
        {
            Given("unit \"112 S Chicago #1\" kind shop\n"
                + "unit \"112 S Chicago #1\" business \"Ryan's Antiques\"\n");

            var unmatched = BusinessRulings.Unmatched(
                new[] { "112 S Chicago #1", "112 S Chicago #2" });

            Assert.That(unmatched, Is.Empty);
        }

        [Test]
        public void ARuledUnitThatMatchesNoPlaceIsReportedUnmatched()
        {
            Given("unit \"112 S Chicago #3\" business \"The Old Diner\"\n");

            var unmatched = BusinessRulings.Unmatched(
                new[] { "112 S Chicago #1", "112 S Chicago #2" });

            Assert.That(unmatched, Is.EquivalentTo(new[] { "112 S Chicago #3" }));
        }

        [Test]
        public void NoRulingsMeansNothingUnmatched()
        {
            Given("");

            var unmatched = BusinessRulings.Unmatched(new[] { "112 S Chicago #1" });

            Assert.That(unmatched, Is.Empty);
        }
    }
}
