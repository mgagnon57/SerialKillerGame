using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Prints the town's technology, year by year. NOT AN ASSERTION - a way of looking at the
    /// thing, on the same argument <see cref="CountrysideDiagnostic"/> makes: a curve can satisfy
    /// every test in the file and still describe a town nobody would recognise.
    ///
    ///     dotnet test -c Release tools/Noir.Core.Tests --filter "Name=PrintTheTechnologyYears" ^
    ///                 -l "console;verbosity=detailed"
    ///
    /// What to read it for:
    ///   - 1991 should look like 1991: telephones everywhere, no computers to speak of, nobody
    ///     carrying a phone, a payphone still standing
    ///   - 2006 should look like 2006 IN A VILLAGE OF 1,200, not like 2006 in a city - about two
    ///     thirds with a computer, well under half online, and forty percent still with no mobile
    ///   - the household timelines at the end should read like different households, not like one
    ///     household with noise on it. Being early to a computer must not mean early to everything.
    /// </summary>
    [TestFixture, Explicit("Diagnostic - prints the adoption years, asserts nothing")]
    public class TechnologyDiagnostic
    {
        /// <summary>517 improved residential parcels - the town's real count, from the assessor.</summary>
        private const int Households = 517;

        [Test]
        public void PrintTheTechnologyYears()
        {
            TechnologyTable.Install(TechnologyTable.Parse(File.ReadAllText(
                Path.Combine(RepoRoot(), "Content", "technology.txt"))));

            var years = new[] { 1991, 1994, 1997, 2000, 2003, 2006 };
            var keys = Enumerable.Range(1, Households)
                                 .Select(i => Keys.Of("house-" + i)).ToArray();

            Console.WriteLine($"\nRossville, {Households} households, share holding each technology\n");
            Console.WriteLine("  technology      scope       " +
                              string.Join("   ", years.Select(y => y.ToString())));
            Console.WriteLine("  " + new string('-', 74));

            foreach (string name in TechnologyTable.Current.Names)
            {
                TechnologyTable.TryScope(name, out var scope);
                var cells = years.Select(y =>
                {
                    if (scope == TechScope.Town)
                        return TechnologyTable.Has(name, y) ? "   yes" : "    no";
                    int n = keys.Count(k => TechnologyTable.Has(name, y, k));
                    return $"{n * 100 / keys.Length,5}%";
                });
                Console.WriteLine($"  {name,-15} {scope,-10} {string.Join("  ", cells)}");
            }

            Console.WriteLine("\n  (town rows are a fact about the place; the rest are counted over households)");

            // Four households, in full. This is the part worth staring at: if these read as one
            // household with noise on it, the per-technology salt is not doing its job.
            Console.WriteLine("\nFour households, and the year each thing arrives:\n");
            foreach (int i in new[] { 3, 91, 204, 488 })
            {
                ulong key = Keys.Of("house-" + i);
                Console.WriteLine($"  house {i}");
                foreach (string name in TechnologyTable.Current.Names)
                {
                    if (!TechnologyTable.TryScope(name, out var scope) || scope == TechScope.Town) continue;
                    int got = TechnologyTable.AdoptsIn(name, key);
                    int lost = TechnologyTable.LosesIn(name, key);
                    string when = got == Era.Never ? "never"
                                : lost == Era.Never ? got.ToString()
                                : $"{got}, gone by {lost}";
                    Console.WriteLine($"      {name,-15} {when}");
                }
                Console.WriteLine();
            }

            // And the sentence the dialogue prompt would actually carry.
            foreach (int year in new[] { 1991, 1999, 2006 })
            {
                ulong key = Keys.Of("house-91");
                var has = TechnologyTable.Current.Names
                    .Where(n => TechnologyTable.TryScope(n, out var s) && s != TechScope.Town)
                    .Where(n => TechnologyTable.Has(n, year, key))
                    .ToArray();
                Console.WriteLine($"  house 91 in {year}: {(has.Length == 0 ? "nothing" : string.Join(", ", has))}");
            }
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Content")))
                dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("could not find the repo root");
            return dir.FullName;
        }
    }
}
