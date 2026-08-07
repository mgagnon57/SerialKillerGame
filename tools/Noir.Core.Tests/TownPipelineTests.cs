using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// There must be exactly ONE way to build the town, and these are greps because the property
    /// being defended is about who calls what rather than about any value a normal test could read.
    ///
    /// WHAT WENT WRONG. The pipeline lived as a run of statements inside VillageHost.Awake,
    /// callable from nowhere. Every tool that wanted a town therefore rebuilt the sequence by
    /// hand, and on 2026-08-07 there were twenty such call sites across eighteen files, of which
    /// exactly one - Awake - ran all of it. Fourteen ran no survey passes whatsoever, so the
    /// offline renders, the audits and the probes were all measuring a town the game does not
    /// build. The menu command named "Render The Built Town" was one of the fourteen.
    ///
    /// This was diagnosed once already, correctly, inside MapAudit: "an audit that measures a
    /// different town than the one on screen is worse than no audit, because it is believed." The
    /// fix went into MapAudit and nowhere else. That is why the guard is a test and not a comment:
    /// the lesson had already been written down in prose, in the right words, and it did not hold.
    /// </summary>
    [TestFixture]
    public class TownPipelineTests
    {
        private const string ThePipeline = "Assets/Noir/Unity/TownPipeline.cs";

        /// <summary>
        /// Only TownPipeline may call WorldBuilder.Build. Core is exempt because that is where
        /// WorldBuilder lives and documents itself; the rule is about CONSUMERS.
        /// </summary>
        [Test]
        public void OnlyTheOnePipelineBuildsTheWorld()
        {
            var offenders = new List<string>();
            bool foundThePipeline = false;

            foreach (string file in ConsumerSources())
            {
                string text = Strip(File.ReadAllText(file));
                if (!text.Contains("WorldBuilder.Build(")) continue;

                if (file.Replace('\\', '/').EndsWith(ThePipeline)) foundThePipeline = true;
                else offenders.Add(file);
            }

            Assert.That(offenders, Is.Empty,
                "These files call WorldBuilder.Build directly:\n  " + string.Join("\n  ", offenders) + "\n\n" +
                "Exactly one file may construct a world, and it is " + ThePipeline + ".\n\n" +
                "If you want the real town, call TownPipeline.Build(). If you are deliberately\n" +
                "building something that is NOT the real town - a fixture, or a map assembled in\n" +
                "memory - call TownPipeline.BuildUnsurveyed(layout, seed, name), which is named for\n" +
                "what it gives up so that it cannot be reached for absent-mindedly.\n\n" +
                "Do not add an exemption here. The whole failure this guards against was fourteen\n" +
                "tools that each looked like a reasonable exception on their own.");

            Assert.That(foundThePipeline, Is.True,
                ThePipeline + " no longer calls WorldBuilder.Build.\n\n" +
                "If the pipeline has moved, move the constant above with it. If it has been deleted,\n" +
                "every tool is back to rolling its own town and this test should be failing loudly.");
        }

        /// <summary>
        /// And the one pipeline must actually run the whole survey layer. Deleting a pass from
        /// TownPipeline is now a silent way to give every tool AND the game a different town at
        /// once - which is a smaller blast radius than before only in the sense that it is one
        /// edit instead of eighteen.
        /// </summary>
        [Test]
        public void ThePipelineRunsEverySurveyPass()
        {
            string path = Path.Combine(RepoRoot(), ThePipeline.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, "Missing " + ThePipeline);

            string text = Strip(File.ReadAllText(path));

            // The order is asserted as well as the membership, because it is load-bearing:
            // RuledAway before SeatOnSurvey so nothing is seated and then taken down again, and
            // DowntownFromSanborn after SeatOnSurvey because that pass deliberately skips the lots
            // this one picks up.
            string[] passes =
            {
                "SurveyRoads.Apply",
                "RuledAway.Apply",
                "SeatOnSurvey.Apply",
                "DowntownFromSanborn.Apply",
                "FillFromSurvey.Apply",
            };

            int at = -1;
            foreach (string pass in passes)
            {
                int found = text.IndexOf(pass, StringComparison.Ordinal);
                Assert.That(found, Is.GreaterThanOrEqualTo(0),
                    ThePipeline + " no longer calls " + pass + ".\n\n" +
                    "Every offline render, audit and probe goes through this function now, so a pass\n" +
                    "dropped here is dropped everywhere at once - including out of the game.");
                Assert.That(found, Is.GreaterThan(at),
                    pass + " runs out of order in " + ThePipeline + ".\n\n" +
                    "The sequence is load-bearing: RuledAway before SeatOnSurvey so nothing is\n" +
                    "carefully seated and then taken down again, and DowntownFromSanborn after\n" +
                    "SeatOnSurvey because that pass skips the lots ruled `footprint later` and\n" +
                    "leaves them for this one. FillFromSurvey is last so it can see what is already\n" +
                    "standing and put nothing on top of it.");
                at = found;
            }
        }

        /// <summary>Comments do not count as calls. A file that merely NAMES the builder while
        /// explaining why it does not use it is behaving correctly.</summary>
        private static string Strip(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(source, @"//[^\n]*", " ");
        }

        /// <summary>The two assemblies that CONSUME the builder. Core is where it is defined.</summary>
        private static IEnumerable<string> ConsumerSources()
        {
            string root = RepoRoot();
            foreach (string area in new[] { "Unity", "Editor" })
            {
                string dir = Path.Combine(root, "Assets", "Noir", area);
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                    yield return file;
            }
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
