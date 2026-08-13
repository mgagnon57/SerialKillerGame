using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// "IS THIS SOMEBODY'S HOME" IS A COLUMN IN kinds.txt, NOT A MEMBER OF AN ENUM.
    ///
    /// `PlaceKind` holds the kinds C# was compiled against. `Content/kinds.txt` may add more, and
    /// it does - `apartment` declares `home yes` and is numbered PAST the enum's members, because
    /// content is allowed to invent a kind without anybody recompiling. That is the whole design,
    /// and it is stated in PlaceKindTable's own docstring.
    ///
    /// The consequence is that `place.Kind == PlaceKind.Dwelling` is not the question anybody
    /// means. It was asked in SIX places, and every one of them was wrong about the same seven
    /// buildings - the rooms over the grocer, the meat market, the barber and the laundry, and
    /// 102 Stewart Ave, 204 Dale Ave and 206 Dale Ave:
    ///
    ///     Eyewitness        the people in them had no home, in the instrument the 2:1 rule is
    ///                       built on - they would have landed at the bottom of the first
    ///                       Rossville measurement this project ever recorded, as an artefact
    ///     EncounterReport   the same, one file over
    ///     VocabReport       the town's own census under-counted its dwellings and its units
    ///     RoofBuilder       one chimney for four households stacked under one roof
    ///     Materials3D       three apartment houses in residential streets built of Main Street
    ///                       brick
    ///     SmokeTest         the rooms-per-house census left them out
    ///
    /// Not every one of these is wrong, which is why this is an ALLOW-LIST WITH A REASON rather
    /// than a ban. `HouseLayers` and `MassingGrammars` genuinely mean the enum member: they are
    /// choosing which era of FRAME HOUSE to build, and an apartment block over a shop is not one.
    ///
    /// Add a site here only with the sentence explaining why the enum member is the right
    /// question. If the answer is "because it is a home", the answer is `IsHome`.
    /// </summary>
    [TestFixture]
    public class HomeIsAColumnNotAnEnumMemberTests
    {
        /// <summary>
        /// Files that may ask `Kind == PlaceKind.Dwelling`, and why each is not asking about home.
        /// </summary>
        private static readonly Dictionary<string, string> Allowed = new Dictionary<string, string>
        {
            ["PlaceKindTable.cs"] =
                "the DEFAULT for the IsHome column itself - a kind that does not declare `home` "
              + "is a home if and only if it is the enum's own Dwelling. This is where the "
              + "question is answered, so it cannot ask itself.",

            ["HouseLayers.cs"] =
                "which ERA of frame house a lot gets - foursquare, bungalow, ranch. An apartment "
              + "block over a shop is not a frame house of any era, so excluding it is correct.",

            ["MassingGrammars.cs"] =
                "routes dwellings to HouseLayers, for the same reason. `apartment` carries its own "
              + "`massing` column and never reaches this arm.",

            ["Snapshot.cs"] =
                "an editor render filter picking which buildings to photograph. Cosmetic, and the "
              + "frame is chosen by eye anyway.",
        };

        [Test]
        public void NothingAsksTheEnumWhenItMeansIsHome()
        {
            var offenders = new List<string>();
            var allowedSeen = new List<string>();

            foreach (string file in SourceFiles())
            {
                string name = Path.GetFileName(file);
                string text = Strip(File.ReadAllText(file));
                if (!Regex.IsMatch(text, @"Kind\s*[!=]=\s*PlaceKind\.Dwelling")) continue;

                if (Allowed.ContainsKey(name)) { allowedSeen.Add(name); continue; }
                offenders.Add(name);
            }

            TestContext.Out.WriteLine("asking the enum about Dwelling: "
                + $"{allowedSeen.Count} allowed ({string.Join(", ", allowedSeen)}), "
                + $"{offenders.Count} not");

            Assert.That(offenders, Is.Empty,
                "These ask `Kind == PlaceKind.Dwelling`: " + string.Join(", ", offenders) + "\n\n"
              + "`apartment` declares `home yes` in kinds.txt and is NOT in the PlaceKind enum, so "
              + "that test is false for seven of Rossville's places and everybody living in them. "
              + "If the question is \"is this somebody's home\", ask "
              + "`PlaceKindTable.Current.Row(kind).IsHome`. If it genuinely is about the enum "
              + "member - which frame-house era to build, say - add the file to Allowed WITH the "
              + "sentence saying why.");
        }

        /// <summary>Every .cs under Assets/Noir and tools, except the tests themselves.</summary>
        private static IEnumerable<string> SourceFiles()
        {
            string root = RepoRoot();
            foreach (string dir in new[] { Path.Combine(root, "Assets", "Noir"),
                                           Path.Combine(root, "tools") })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains("Noir.Core.Tests")) continue;
                    if (file.Contains(Path.Combine("Assets", "Noir", "PlayTests"))) continue;
                    if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                    if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
                    yield return file;
                }
            }
        }

        private static string Strip(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(source, @"//[^\n]*", " ");
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
