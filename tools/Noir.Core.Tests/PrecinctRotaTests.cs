using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    [TestFixture]
    public class PrecinctRotaTests
    {
        [Test]
        public void ThePrecinctRunsFourOfficersOnTwoWatches()
        {
            string kinds = File.ReadAllText(Path.Combine(RepoRoot(), "Content", "kinds.txt"));

            // Cut the precinct block: from "kind precinct" to the next "kind ".
            int start = kinds.IndexOf("kind precinct");
            int end = kinds.IndexOf("\nkind ", start + 1);
            var block = kinds.Substring(start, end - start);

            Assert.That(block, Does.Contain("jobs      4"),
                "the owner ruled four officers (SIM-FIXES.md:440); 12 on one 24h window is the known-wrong staffing");
            Assert.That(Regex.Matches(Regex.Match(block, @"hours\s+(.*)").Groups[1].Value,
                                      @"\d\d:\d\d-\d\d:\d\d").Count, Is.EqualTo(2),
                "two watch windows, so ShiftFor's split branch actually fires");
            Assert.That(block, Does.Contain("shifts    split"));
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
