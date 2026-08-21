using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Noir.Core.Response is the same shape of firewall as Noir.Core.Observation, for the same
    /// reason: it must be PROVABLY unable to read ground truth, or the police in this game are
    /// cheating in the same way a detective who reads Observation's forbidden types would be.
    /// This is the asmdef-pinning half of that proof — see ObservationFirewallTests for the
    /// deeper reflection walk, which is not cloned here because the case file legitimately
    /// carries CitizenId (the spec says so): the machine is told who a witness or an officer is
    /// by its caller, it never derives an identity by reading ground truth itself.
    /// </summary>
    [TestFixture]
    public class ResponseFirewallTests
    {
        [Test]
        public void ResponseAsmdefReferencesContractsAndNothingElse()
        {
            string path = Path.Combine(RepoRoot(), "Assets", "Noir", "Core", "Response",
                                       "Noir.Core.Response.asmdef");
            Assert.That(File.Exists(path), Is.True, "Missing asmdef at " + path);

            string json = File.ReadAllText(path);

            var refs = new List<string>();
            Match block = Regex.Match(json, "\"references\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            Assert.That(block.Success, Is.True, "Could not find a \"references\" array in " + path);
            foreach (Match m in Regex.Matches(block.Groups[1].Value, "\"([^\"]*)\""))
                refs.Add(m.Groups[1].Value);

            Assert.That(refs, Is.EqualTo(new[] { "Noir.Core.Contracts" }),
                "Noir.Core.Response.asmdef now references [" + string.Join(", ", refs) + "].\n\n" +
                "It must reference Noir.Core.Contracts and nothing else. That list is not a build\n" +
                "detail — it is the proof that the case machine cannot read ground truth, because\n" +
                "the compiler cannot show it AgentState, Citizen or WorldModel. If code in Response\n" +
                "will not compile without a new reference, the code is wrong, not the reference list:\n" +
                "describe what the machine is told by its caller and resolve the rest elsewhere.\n\n" +
                "If a genuinely new response-side assembly has to be referenced, change it here too,\n" +
                "deliberately, and say why in the commit.");

            Assert.That(Regex.IsMatch(json, "\"noEngineReferences\"\\s*:\\s*true"), Is.True,
                "Noir.Core.Response.asmdef must keep \"noEngineReferences\": true, like every other\n" +
                "Core assembly. Core runs headless under dotnet test; a UnityEngine reference ends that.");
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
