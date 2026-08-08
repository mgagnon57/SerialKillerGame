using System;
using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Survey;

namespace Noir.Core.Tests
{
    /// <summary>
    /// THE PARSER FOR THE ONE FILE NOTHING CAN REBUILD, tested at last.
    ///
    /// `Content/parcel-1991.txt` holds 173 rulings the owner made by hand about what stood on
    /// each lot in 1991. Every other file in Content/ can be regenerated from the downloads in
    /// tools/; a recollection cannot. Until 2026-08-07 the code that reads it lived in
    /// Assets/Noir/Unity, which `dotnet test` structurally cannot compile, so the parser for the
    /// most valuable data in the project had never had a single unit test.
    ///
    /// It is in Noir.Core.Survey now - an assembly with noEngineReferences, referencing Contracts
    /// and nothing else - and these are the first tests it has ever had. The move cost nothing in
    /// the file itself: it used no engine type at all. What pinned it there was ContentLoader,
    /// which reaches for Application.dataPath. It asks Content for its text now.
    /// </summary>
    [TestFixture]
    public class RulingsTests
    {
        /// <summary>A content source made of strings, which is the whole point of the seam.</summary>
        private sealed class Fake : IContentSource
        {
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>();

            /// <summary>
            /// A DIFFERENT STAMP FOR EVERY FAKE, and this matters more than it looks.
            ///
            /// Rulings is a static cache that reparses only when the file's timestamp moves -
            /// deliberately, because tools and the browser map rewrite Content/parcel-1991.txt
            /// while the editor is open. A fake that always answered the same DateTime made every
            /// test after the first read the FIRST test's data, which is how these tests failed
            /// the first time they ran. The behaviour was right; the double was wrong.
            /// </summary>
            private static int _tick;
            private readonly DateTime _stamp = new DateTime(2026, 8, 7).AddSeconds(++_tick);

            public Fake Put(string name, string text) { _files[name] = text; return this; }
            public string Read(string name) =>
                _files.TryGetValue(name, out var t) ? t : throw new System.IO.FileNotFoundException(name);
            public DateTime WrittenAt(string name) => _files.ContainsKey(name) ? _stamp : default;
        }

        /// <summary>
        /// A fresh source AND a fresh parse. Rulings only asks the filesystem once a second, so a
        /// test that swapped the content underneath it would keep reading the previous test's
        /// data - which is exactly what happened the first time these ran.
        /// </summary>
        private static void Given(string body)
        {
            Content.Install(new Fake().Put("parcel-1991.txt", body));
            Rulings.Forget();
        }

        [Test]
        public void AVerdictIsReadBackAsItWasWritten()
        {
            Given("parcel 7 was built\nparcel 7 kind dwelling\n\nparcel 10 was absent\n");

            Assert.That(Rulings.For(7).Was, Is.EqualTo(Rulings.Stood.Built));
            Assert.That(Rulings.For(7).Kind, Is.EqualTo("dwelling"));
            Assert.That(Rulings.For(10).Was, Is.EqualTo(Rulings.Stood.Absent));
        }

        /// <summary>
        /// `footprint later` is the verb the whole downtown turns on: the lot was built on in
        /// 1991 but the MEASURED shape postdates it, so SeatOnSurvey must skip it and
        /// DowntownFromSanborn must pick it up. It was hand-written into the file and very nearly
        /// deleted by a writer that did not know the word.
        /// </summary>
        [Test]
        public void FootprintLaterIsReadAndNothingElseIsMistakenForIt()
        {
            Given("parcel 237 was built\nparcel 237 footprint later\n" +
                  "parcel 238 was built\nparcel 238 footprint earlier\n" +
                  "parcel 239 was built\n");

            Assert.That(Rulings.For(237).FootprintIsLater, Is.True);
            Assert.That(Rulings.For(238).FootprintIsLater, Is.False,
                "only the word `later` means later - a future word must not silently mean this one");
            Assert.That(Rulings.For(239).FootprintIsLater, Is.False);
        }

        [Test]
        public void AbsentIsNotVacantAndTheDifferenceMatters()
        {
            Given("parcel 1 was vacant\nparcel 2 was absent\n");

            Assert.That(Rulings.For(1).Was, Is.EqualTo(Rulings.Stood.Vacant),
                "the lot existed and nothing stood on it");
            Assert.That(Rulings.For(2).Was, Is.EqualTo(Rulings.Stood.Absent),
                "there was no such lot in 1991 - ground subdivided out of a field later has no " +
                "business on a map of 1991, and must stop being drawn as a lot at all");
        }

        [Test]
        public void QuotedTextSurvivesSpacesAndPunctuation()
        {
            Given("parcel 12 was built\n" +
                  "parcel 12 property \"101-105 N Chicago\"\n" +
                  "parcel 12 note \"one BUILDING, subdivided - not detached buildings\"\n");

            Assert.That(Rulings.For(12).Property, Is.EqualTo("101-105 N Chicago"));
            Assert.That(Rulings.For(12).Note, Does.Contain("subdivided - not detached"));
        }

        /// <summary>
        /// Several lots are one building, and the NAME is the grouping - there is no group id to
        /// keep in step. The grade school stands on three parcels.
        /// </summary>
        [Test]
        public void SeveralLotsWithOnePropertyNameAreOneProperty()
        {
            Given("parcel 719 was built\nparcel 719 property \"Grade School\"\n" +
                  "parcel 718 was built\nparcel 718 property \"grade school\"\n" +
                  "parcel 590 was built\nparcel 590 property \" Grade School \"\n");

            var lots = Rulings.OneProperty(719);
            Assert.That(lots, Is.Not.Null);
            Assert.That(lots.Count, Is.EqualTo(3),
                "keyed case-insensitively and trimmed, because the browser map joins them that " +
                "way and the two must agree about what is one property");
        }

        [Test]
        public void AnUnruledLotIsSimplyUnruled()
        {
            Given("parcel 7 was built\n");

            Assert.That(Rulings.For(999).Was, Is.EqualTo(Rulings.Stood.Unruled),
                "an unruled lot must not read as a ruling - most of the town is unruled");
        }

        [Test]
        public void CommentsAndBlankLinesAreNotRulings()
        {
            Given("# ===========================\n#  parcel 1 was built\n\n   \nparcel 5 was built\n");

            Assert.That(Rulings.For(1).Was, Is.EqualTo(Rulings.Stood.Unruled),
                "a ruling inside a comment is a comment - the file's header describes the format " +
                "using the format");
            Assert.That(Rulings.For(5).Was, Is.EqualTo(Rulings.Stood.Built));
        }

        [Test]
        public void AMissingFileIsNoRulingsRatherThanACrash()
        {
            Content.Install(new Fake());          // nothing in it at all
            Rulings.Forget();
            Assert.DoesNotThrow(() => { var _ = Rulings.For(7); });
            Assert.That(Rulings.For(7).Was, Is.EqualTo(Rulings.Stood.Unruled));
        }

        [Test]
        public void TheRealFileParsesToTheRulingsThatAreInIt()
        {
            Content.Install(new DiskContent());
            Rulings.Forget();

            int ruled = 0;
            for (int id = 0; id < 900; id++)
                if (Rulings.For(id).Was != Rulings.Stood.Unruled) ruled++;

            TestContext.Out.WriteLine($"rulings in Content/parcel-1991.txt = {ruled}");
            Assert.That(ruled, Is.GreaterThan(150),
                "Content/parcel-1991.txt carries the owner's hand-made rulings and cannot be " +
                "rebuilt from anything. If this collapses, something has overwritten it.");
            Assert.That(Rulings.For(237).FootprintIsLater, Is.True,
                "parcel 237 - the even side of the 100 block, burned Feb 2004 - is the lot the " +
                "whole downtown terrace depends on being ruled `footprint later`.");
        }

        /// <summary>The real Content/ directory, for the one test that reads it.</summary>
        private sealed class DiskContent : IContentSource
        {
            private static string Dir()
            {
                var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
                while (d != null)
                {
                    string c = System.IO.Path.Combine(d.FullName, "Content");
                    if (System.IO.Directory.Exists(c)) return c;
                    d = d.Parent;
                }
                throw new System.IO.DirectoryNotFoundException("no Content/ above " + AppContext.BaseDirectory);
            }
            public string Read(string name) => System.IO.File.ReadAllText(System.IO.Path.Combine(Dir(), name));
            public DateTime WrittenAt(string name) => System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine(Dir(), name));
        }
    }
}
