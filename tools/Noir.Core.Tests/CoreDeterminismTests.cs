using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The ban Vec2 has always claimed was enforced, actually enforced.
    ///
    /// Transcendentals are implementation-defined: Math.Sin has changed result in the last bit
    /// between .NET runtimes before, and Core is the half of this project whose whole value is
    /// that the same seed replays the same village. A drifting sine would not fail loudly - it
    /// would move one villager one tile, two years from now, on somebody else's machine.
    ///
    /// SQRT IS DELIBERATELY ALLOWED. It is not a transcendental: IEEE-754 requires it to be
    /// correctly rounded, so it is bit-identical wherever it runs. RoadPath needs it for arc
    /// length and nothing else in Core needs it at all.
    /// </summary>
    [TestFixture]
    public class CoreDeterminismTests
    {
        private static readonly string[] Banned =
        {
            "Sin", "Cos", "Tan", "Asin", "Acos", "Atan", "Atan2", "Exp", "Log", "Log10", "Pow",
        };

        [Test]
        public void NoCoreFileCallsATranscendental()
        {
            var offenders = new List<string>();

            foreach (string file in Directory.GetFiles(
                         Path.Combine(RepoRoot(), "Assets", "Noir", "Core"), "*.cs",
                         SearchOption.AllDirectories))
            {
                // Comments stripped first, the same way TwoToOneTests strips them: a file must be
                // able to say "no Cos in here" in its own header without tripping its own guard.
                string source = Regex.Replace(File.ReadAllText(file), @"/\*.*?\*/", "",
                                              RegexOptions.Singleline);
                source = Regex.Replace(source, @"//[^\n]*", "");

                foreach (string name in Banned)
                {
                    // Match a CALL through Math/MathF only. A bare word would fire on any
                    // identifier containing it - Cost, Single, Login - and a guard that cries
                    // wolf is a guard somebody switches off.
                    if (Regex.IsMatch(source, @"\bMathF?\." + name + @"\s*\("))
                        offenders.Add(Path.GetFileName(file) + " -> Math." + name);
                }
            }

            Assert.That(offenders, Is.Empty,
                "Transcendentals in Core:\n  " + string.Join("\n  ", offenders) + "\n\n" +
                "Their results are implementation-defined and have changed between .NET\n" +
                "runtimes, which would silently break replay. See Vec2.cs. Sqrt is allowed.");
        }

        [Test]
        public void SqrtIsAllowedSoTheBanIsAboutDeterminismAndNotAboutMath()
        {
            // Falsification: the matcher must NOT fire on the one function RoadPath relies on.
            // Without this, a lazier regex that banned everything on Math would pass the test
            // above for the wrong reason and block Task 3 for no reason.
            const string sample = "var d = MathF.Sqrt(dx * dx + dy * dy);";
            foreach (string name in Banned)
                Assert.That(Regex.IsMatch(sample, @"\bMathF?\." + name + @"\s*\("), Is.False,
                            "Sqrt must not be caught by the " + name + " matcher");
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Noir", "Core", "World")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not find Assets/Noir/Core/World above " + AppContext.BaseDirectory);
        }
    }
}
