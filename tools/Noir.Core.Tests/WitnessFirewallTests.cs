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

        /// <summary>
        /// The ONE file allowed to name Noir.Core.Witness, by name.
        ///
        /// This test used to say "nothing in the game references Witness yet", and said that the
        /// caller which eventually would "must never become a list". On 2026-08-05 that caller
        /// arrived: VillageHost records the player's track, which is the one thing the whole
        /// observation system was waiting on - Recollection.WhatTheySaw can answer nothing
        /// without it, and until then this assembly had never run.
        ///
        /// IT IS STILL NOT A LIST, AND MUST NOT BECOME ONE. A second name here means somebody
        /// wanted the investigation's machinery somewhere convenient, and the whole point of the
        /// firewall is that convenience is exactly how ground truth leaks into testimony.
        /// Player.cs deliberately hands VillageHost a Vector3 rather than recording for itself,
        /// so that it stays out of this - that is what keeping it to one file costs, and it is
        /// cheap.
        /// </summary>
        [Test]
        public void OnlyTheOneCallerReferencesWitness()
        {
            const string TheCaller = "Assets/Noir/Unity/VillageHost.cs";

            var offenders = new List<string>();
            bool foundTheCaller = false;

            foreach (string file in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "Noir"),
                                                       "*.cs", SearchOption.AllDirectories))
            {
                string slashed = file.Replace('\\', '/');
                if (slashed.Contains("/Core/Witness/")) continue;

                string text = File.ReadAllText(file);
                if (!text.Contains("Noir.Core.Witness")) continue;

                if (slashed.EndsWith(TheCaller)) foundTheCaller = true;
                else offenders.Add(file);
            }

            Assert.That(offenders, Is.Empty,
                "These files reference Noir.Core.Witness:\n  " + string.Join("\n  ", offenders) + "\n\n" +
                "Exactly one file in the game may consume this assembly - the caller that asks it\n" +
                "a question - and that file is " + TheCaller + ". Adding a second is not a\n" +
                "convenience, it is the failure this firewall exists to prevent: not\n" +
                "Sighting.Culprit, but a convenience field three types deep added years from now\n" +
                "by somebody who never read any of this.\n\n" +
                "If the new caller is the right one and VillageHost is not, MOVE the name. Do not\n" +
                "add to it.");

            Assert.That(foundTheCaller, Is.True,
                TheCaller + " no longer references Noir.Core.Witness.\n\n" +
                "If the player's track has moved somewhere else, move the name above with it. If\n" +
                "it has been deleted, the observation system is back to never running and this\n" +
                "test should go back to asserting nothing in the game references Witness at all.");
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
