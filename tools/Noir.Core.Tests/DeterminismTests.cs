using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// `Assets/Noir/Core/Contracts/Rng.cs` says it outright: "All randomness in the simulation
    /// routes through this. Nothing in Core may call System.Random or any ambient source - that is
    /// what makes a seed reproduce a village." Nothing enforced it. On 2026-08-07
    /// `VillageUI.RandomizeHousehold` was calling `new System.Random()`.
    ///
    /// THE SCOPE OF THIS TEST WAS ARGUED WITH ITSELF AND LOST ONCE, WHICH IS WHY IT IS WRITTEN
    /// DOWN. The first version banned every ambient source everywhere under Assets/Noir and
    /// immediately flagged four things:
    ///
    ///   - Rulings.cs reading Environment.TickCount to avoid stat-ing a file more than once a
    ///     second,
    ///   - SurveyReport.cs stamping "when" into a report with DateTime.Now,
    ///   - VillageAudio.cs varying footstep pitch with UnityEngine.Random.Range.
    ///
    /// None of those stop a seed reproducing a village, which is the property the doctrine names.
    /// A guard that fails on a report timestamp is a guard people learn to route around, and it
    /// would have been "fixed" by adding exceptions until it meant nothing. So the rule is scoped
    /// by CONSEQUENCE instead, and the scope is the honest part:
    ///
    ///   - System.Random and Guid.NewGuid are banned OUTRIGHT. IRng replaces them completely and
    ///     there is no case in this project that wants either.
    ///   - UnityEngine.Random is banned in Core and anywhere the TOWN IS BUILT. It is allowed in
    ///     presentation - audio, effects - where it cannot reach the world model.
    ///   - Clocks are not randomness and are not in scope. They would threaten replay, which is a
    ///     different property with a different argument, and conflating the two buys neither.
    /// </summary>
    [TestFixture]
    public class DeterminismTests
    {
        /// <summary>Never legitimate here, anywhere. IRng covers every use.</summary>
        private static readonly (string Pattern, string What)[] BannedEverywhere =
        {
            (@"new\s+System\.Random\s*\(", "System.Random"),
            (@"\bGuid\.NewGuid\s*\(",      "Guid.NewGuid"),
        };

        /// <summary>Fine for presentation; fatal anywhere that decides what the town IS.</summary>
        private static readonly (string Pattern, string What)[] BannedInTheBuild =
        {
            (@"\bUnityEngine\.Random\.", "UnityEngine.Random"),
            (@"(?<![\w.])Random\.(value|Range|insideUnitSphere|insideUnitCircle|onUnitSphere|rotation)\b",
                                         "UnityEngine.Random"),
        };

        /// <summary>
        /// The files that decide what the town is. Everything in Core, plus the survey layer and
        /// the pipeline that drives it. If a new pass joins TownPipeline, add it here.
        /// </summary>
        private static readonly string[] BuildPath =
        {
            "Assets/Noir/Core/",
            "Assets/Noir/Unity/TownPipeline.cs",
            "Assets/Noir/Unity/SurveyRoads.cs",
            "Assets/Noir/Unity/RuledAway.cs",
            "Assets/Noir/Unity/SeatOnSurvey.cs",
            "Assets/Noir/Unity/DowntownFromSanborn.cs",
            "Assets/Noir/Unity/FillFromSurvey.cs",
            "Assets/Noir/Unity/ParcelBuildings.cs",
            "Assets/Noir/Unity/ParcelIndex.cs",
            "Assets/Noir/Unity/Rulings.cs",
            "Assets/Noir/Unity/Placements.cs",
        };

        [Test]
        public void NothingAnywhereCallsSystemRandomOrNewGuid()
        {
            var offenders = Scan(BannedEverywhere, _ => true);

            Assert.That(offenders, Is.Empty,
                "These call an ambient random source that IRng exists to replace:\n  " +
                string.Join("\n  ", offenders) + "\n\n" +
                "Use Xoshiro256ss.Substream(seed, \"your-system\").\n\n" +
                "If you need output that genuinely VARIES between runs - an authoring button a\n" +
                "person presses - vary the substream NAME with a counter you own, the way\n" +
                "VillageUI.RandomizeHousehold does. That keeps the varying behaviour, keeps the\n" +
                "numbers out of every other system's stream, and keeps this rule absolute. An\n" +
                "absolute rule is the only kind that survives somebody in a hurry.");
        }

        [Test]
        public void TheTownIsBuiltWithoutAmbientRandomness()
        {
            var offenders = Scan(BannedInTheBuild,
                                 rel => Array.Exists(BuildPath, p => rel.StartsWith(p, StringComparison.Ordinal)));

            Assert.That(offenders, Is.Empty,
                "These decide what the town is, using randomness a seed cannot reproduce:\n  " +
                string.Join("\n  ", offenders) + "\n\n" +
                "UnityEngine.Random is ambient and unseedable from here. Anything on the build\n" +
                "path must draw from the IRng substream it was handed, or the same seed stops\n" +
                "producing the same Rossville - which is the one guarantee the whole survey layer\n" +
                "rests on.\n\n" +
                "This is allowed OUTSIDE the build path, in presentation code that cannot reach\n" +
                "the world model. VillageAudio varying footstep pitch is fine and deliberate.");
        }

        private static List<string> Scan((string Pattern, string What)[] banned, Func<string, bool> inScope)
        {
            var offenders = new List<string>();
            string root = Path.Combine(RepoRoot(), "Assets", "Noir");
            Assert.That(Directory.Exists(root), Is.True, "Missing " + root);

            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Rel(file);
                if (rel.Contains("/PlayTests/")) continue;          // a test may seed itself
                if (rel.EndsWith("Core/Contracts/Rng.cs")) continue; // it NAMES these to forbid them
                if (!inScope(rel)) continue;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string s = lines[i].Trim();
                    if (s.StartsWith("//") || s.StartsWith("*")) continue;

                    foreach (var (pattern, what) in banned)
                        if (Regex.IsMatch(lines[i], pattern))
                            offenders.Add($"{rel}:{i + 1}  {what}  |  {s}");
                }
            }
            return offenders;
        }

        private static string Rel(string file) =>
            file.Substring(RepoRoot().Length).TrimStart('\\', '/').Replace('\\', '/');

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
