using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Content/parcel-buildings.txt is written by tools/seat-buildings.py and read by
    /// Assets/Noir/Unity/ParcelBuildings.cs, and NOTHING CHECKED THAT THE TWO AGREED.
    ///
    /// They did not. The writer emitted `skew <deg> block <deg> "<address>"`; the file's own
    /// header documented neither angle; the reader split on a fixed column count written against
    /// the header. So the address field swallowed the rest of the line and all 824 records parsed
    /// their address as the literal text `block -2.8 "106 Smith Street"`. Because that is not
    /// empty, FillFromSurvey's fallback to the county's own address never fired either - one
    /// added column silently corrupted every record in the file, downstream, for as long as it
    /// had been there, and no test, audit or render went red.
    ///
    /// The reader now scans for keywords rather than counting columns, so a NEW field cannot
    /// repeat this. These tests pin the other half: that the file still says what the reader is
    /// scanning for. This assembly cannot reference ParcelBuildings - it compiles Core only - so
    /// this is a contract test on the data, which is the strongest guard available from here.
    /// If the survey layer ever moves into Core, replace this with a real round-trip test.
    /// </summary>
    [TestFixture]
    public class ParcelBuildingsFormatTests
    {
        private const string DataFile = "Content/parcel-buildings.txt";

        // parcel <id> building <n> <role> <area> <zone> skew <deg> block <deg> "<address>"
        private static readonly Regex Record = new Regex(
            @"^parcel\s+(?<pid>\d+)\s+building\s+(?<n>\d+)\s+(?<role>primary|outbuilding)\s+" +
            @"(?<area>[-+]?[\d.]+)\s+(?<zone>\S+)\s+skew\s+(?<skew>[-+]?[\d.]+)\s+" +
            @"block\s+(?<block>[-+]?[\d.]+)\s+""(?<addr>[^""]*)""\s*$",
            RegexOptions.Compiled);

        [Test]
        public void EveryBuildingRecordMatchesTheFormatTheReaderScansFor()
        {
            var offenders = new List<string>();
            int records = 0;

            foreach (string line in BuildingLines())
            {
                records++;
                if (!Record.IsMatch(line) && offenders.Count < 10) offenders.Add(line);
            }

            Assert.That(records, Is.GreaterThan(700),
                "Only " + records + " building records in " + DataFile + ". There should be ~824. " +
                "Has seat-buildings.py been re-run against a smaller download?");

            Assert.That(offenders, Is.Empty,
                "These records do not match the documented format:\n  " +
                string.Join("\n  ", offenders) + "\n\n" +
                "The contract is:\n" +
                "  parcel <id> building <n> <role> <area> <zone> skew <deg> block <deg> \"<address>\"\n\n" +
                "If you changed seat-buildings.py, update the header it writes AND check\n" +
                "Assets/Noir/Unity/ParcelBuildings.cs still finds what it scans for. The last time\n" +
                "these three disagreed, every address in the file was wrong and nothing went red.");
        }

        /// <summary>
        /// The specific corruption, named so it cannot come back wearing a different hat: an
        /// address that has swallowed a field name.
        /// </summary>
        [Test]
        public void NoAddressHasSwallowedAFieldName()
        {
            var offenders = new List<string>();

            foreach (string line in BuildingLines())
            {
                var m = Record.Match(line);
                if (!m.Success) continue;                 // the test above owns that failure
                string addr = m.Groups["addr"].Value;
                if (addr.Contains("skew") || addr.Contains("block") || addr.Contains("\""))
                    if (offenders.Count < 10) offenders.Add(addr);
            }

            Assert.That(offenders, Is.Empty,
                "These addresses contain a field name:\n  " + string.Join("\n  ", offenders) + "\n\n" +
                "That is the signature of a reader counting columns against a writer that added " +
                "one.");
        }

        /// <summary>
        /// And the file has to actually carry addresses, or the guard above passes trivially on a
        /// file full of empty strings while the county fallback quietly does all the work.
        /// </summary>
        [Test]
        public void MostBuildingsCarryASitusAddress()
        {
            int total = 0, addressed = 0;

            foreach (string line in BuildingLines())
            {
                var m = Record.Match(line);
                if (!m.Success) continue;
                total++;
                string addr = m.Groups["addr"].Value.Trim();
                if (addr.Length > 0) addressed++;
            }

            Assert.That(total, Is.GreaterThan(0), "No parsable building records at all.");
            double share = (double)addressed / total;
            Assert.That(share, Is.GreaterThan(0.5),
                $"Only {addressed} of {total} building records carry an address ({share:P0}).\n\n" +
                "The federal layer supplies a situs address for most structures here. If this has " +
                "collapsed, either the download changed or the writer stopped emitting the field - " +
                "and every building will fall through to the county record or to no name at all.");
        }

        private static IEnumerable<string> BuildingLines()
        {
            string path = Path.Combine(RepoRoot(), DataFile.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, "Missing " + DataFile);

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.Contains(" building ")) yield return line;
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
