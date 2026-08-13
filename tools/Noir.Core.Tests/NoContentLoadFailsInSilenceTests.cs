using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// A SURVIVABLE FALLBACK THAT SAYS NOTHING IS INDISTINGUISHABLE FROM A SYSTEM THAT WORKED.
    ///
    /// This project keeps learning the same lesson from a different direction:
    ///
    ///   `watched.floor`        absent -> five zero thresholds, so three of the four moving
    ///                          ratchets became `x >= 0` and the suite went green saying the floor
    ///                          held. Fixed by REFUSING, because a gate has no degraded mode.
    ///   `Content/textures/`    absent -> every surface a flat colour, in every shipped build,
    ///                          reported by a line that counted a cache the main path never filled
    ///   `elevation.txt`        absent -> THE WHOLE TOWN GOES FLAT. 195.3 m of relief and every
    ///                          camera height read zero, and nothing was logged at all
    ///   `parcel-county.txt`    absent -> 4,534 lines of zoning gone; every lot draws as fiction
    ///   `roads.txt`            absent -> the surveyed 68 roads fall back to city.txt's 37. This
    ///                          one was already a named trap in CLAUDE.md, with "confirm the line
    ///                          appears" as the mitigation - which is a rule where a log line
    ///                          would do
    ///
    /// THE ANSWER IS NOT ALWAYS `throw`. A floor that fails open turns a gate into a tautology and
    /// must refuse. A town with its old roads is a working town and must not. The rule that covers
    /// both is narrower and it is the one asserted here: **whatever you do, say it.** A `catch`
    /// around a content read may return quietly only if it is on this list with the sentence
    /// explaining why the absence is ordinary.
    ///
    /// This is a text test for the same reason `TownPipelineTests` is: the property is about which
    /// code paths exist, not about a value any run produces, and Core cannot execute the Unity
    /// layer where these live.
    /// </summary>
    [TestFixture]
    public class NoContentLoadFailsInSilenceTests
    {
        /// <summary>
        /// Silent catches that are correct, and why the absence they swallow is the ordinary case.
        /// </summary>
        private static readonly Dictionary<string, string> Allowed = new Dictionary<string, string>
        {
            ["ElevationGrid.cs"] =
                "ONE of its two catches is allowed: the elevation DELTA file is optional and "
              + "absent by default, so its absence is the ordinary state and not news. The base "
              + "elevation.txt catch beside it logs, because that one costs 195 m of relief.",

            ["AgentAnimation.cs"] =
                "no animation table is a survivable state that the file's own summary reasons "
              + "about, and AnimationCheck is the committed gate that reports on it instead.",

            ["SurveyRoads.cs"] =
                "the File.Exists probe is a QUESTION - `Available` - not a load. Apply() logs the "
              + "survey network line whether or not it fired, which is the check CLAUDE.md names.",
        };

        [Test]
        public void EveryContentLoadThatCanFailSaysSoOrIsListedWithAReason()
        {
            var offenders = new List<string>();
            var allowedSeen = new List<string>();

            foreach (string file in SourceFiles())
            {
                string name = Path.GetFileName(file);
                string text = File.ReadAllText(file);

                // A catch whose entire body is `return;` - nothing logged, nothing rethrown.
                if (!Regex.IsMatch(text, @"catch\s*(\([^)]*\))?\s*\{\s*return\s*;\s*\}")) continue;

                // ...and only where it is swallowing a CONTENT read, which is the class this is
                // about. A silent catch around something else is a different argument.
                if (!text.Contains("ContentLoader.Read") && !text.Contains("File.ReadAllText"))
                    continue;

                if (Allowed.ContainsKey(name)) { allowedSeen.Add(name); continue; }
                offenders.Add(name);
            }

            TestContext.Out.WriteLine($"silent content catches: {allowedSeen.Count} allowed "
                + $"({string.Join(", ", allowedSeen)}), {offenders.Count} not");

            Assert.That(offenders, Is.Empty,
                "These swallow a failed content read and return without a word: "
              + string.Join(", ", offenders) + "\n\n"
              + "The town then builds WITHOUT whatever that file carried, and the run looks "
              + "exactly like one where it worked. Either log what was lost - see ElevationGrid, "
              + "which says the town is flat - or throw if there is no useful degraded mode - see "
              + "Ratio.Floor, where failing open turned a gate into a tautology. If the absence "
              + "really is ordinary, add the file to Allowed WITH the sentence saying why.");
        }

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
                    if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                    if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
                    yield return file;
                }
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
