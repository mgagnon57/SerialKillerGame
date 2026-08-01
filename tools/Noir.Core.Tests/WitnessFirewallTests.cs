using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Noir.Core.Witness is the PRODUCER side of the firewall, and it is dangerous in the exact
    /// opposite way to Noir.Core.Observation. Observation is safe because it can see almost
    /// nothing. Witness can see everything — day plans, the world, the player's track — and is
    /// safe only because the one thing it hands out is a Sighting, which cannot hold an identity.
    ///
    /// That makes its CALLERS the thing to police. The moment one scope holds a Sighting[] and a
    /// DayPlan at once, the narrowing is decorative: whoever wrote it can simply look up the
    /// answer. So the second test below is the important one, and it is a grep, because the
    /// property it defends is about who references the assembly rather than about any type in it.
    /// </summary>
    [TestFixture]
    public class WitnessFirewallTests
    {
        [Test]
        public void WitnessAsmdefReferencesExactlyTheProducerSet()
        {
            string path = Path.Combine(RepoRoot(), "Assets", "Noir", "Core", "Witness",
                                       "Noir.Core.Witness.asmdef");
            Assert.That(File.Exists(path), Is.True, "Missing asmdef at " + path);

            string json = File.ReadAllText(path);

            var refs = new List<string>();
            Match block = Regex.Match(json, "\"references\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            Assert.That(block.Success, Is.True, "Could not find a \"references\" array in " + path);
            foreach (Match m in Regex.Matches(block.Groups[1].Value, "\"([^\"]*)\""))
                refs.Add(m.Groups[1].Value);

            Assert.That(refs, Is.EqualTo(new[]
            {
                "Noir.Core.Contracts",
                "Noir.Core.World",
                "Noir.Core.People",
                "Noir.Core.Observation",
            }), "Noir.Core.Witness.asmdef now references [" + string.Join(", ", refs) + "].\n\n" +
                "This assembly is allowed to see ground truth — that is its job. What it is NOT\n" +
                "allowed to do is grow a second way out. Adding Noir.Core.Sim here would let a\n" +
                "reconstruction read live agent state instead of replaying a plan, which is the\n" +
                "same cheat as reading the day plan from Observation, wearing a different hat.");

            Assert.That(Regex.IsMatch(json, "\"noEngineReferences\"\\s*:\\s*true"), Is.True,
                "Noir.Core.Witness.asmdef must keep \"noEngineReferences\": true. Core runs headless\n" +
                "under dotnet test; a UnityEngine reference ends that.");
        }

        [Test]
        public void NothingInTheGameReferencesWitnessYet()
        {
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "Noir"),
                                                       "*.cs", SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').Contains("/Core/Witness/")) continue;
                string text = File.ReadAllText(file);
                if (text.Contains("Noir.Core.Witness")) offenders.Add(file);
            }

            Assert.That(offenders, Is.Empty,
                "These files reference Noir.Core.Witness:\n  " + string.Join("\n  ", offenders) + "\n\n" +
                "Nothing may consume this assembly except the caller that asks it a question, and\n" +
                "that caller does not exist yet. When it does, this test changes to name it — one\n" +
                "file, deliberately, with a reason in the commit. It must never become a list.");
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
