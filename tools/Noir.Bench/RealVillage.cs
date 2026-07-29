using System;
using System.IO;
using Noir.Core.People;
using Noir.Core.World;

namespace Noir.Bench
{
    /// <summary>
    /// Ashcombe itself, when it can be found.
    ///
    /// The synthetic worlds are the measurement; this is the check on it. If the authored
    /// village and a synthetic one of the same population disagree by more than a little, the
    /// generator is wrong and every projection built on it is wrong too.
    /// </summary>
    public static class RealVillage
    {
        private static string _root;

        public static bool Available => Root != null;

        public static string Root
        {
            get
            {
                if (_root != null) return _root;

                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Content");
                    if (File.Exists(Path.Combine(candidate, "village.txt")))
                        return _root = candidate;
                    dir = dir.Parent;
                }
                return null;
            }
        }

        /// <summary>
        /// The authored kinds table, in place before anything reads a `place` line.
        ///
        /// Public because the SYNTHETIC settlements need it too, and they do not come through
        /// Load. Core cannot read a file, so a run that builds only synthetic worlds - which the
        /// JIT warm-up does, before any section has started - would otherwise reach a `place`
        /// line with no table installed and fail before printing a single number.
        /// </summary>
        public static void EnsureKinds()
        {
            if (Root == null || PlaceKindTable.IsInstalled) return;
            PlaceKindTable.Install(
                PlaceKindTable.Parse(File.ReadAllText(Path.Combine(Root, "kinds.txt"))));
        }

        public static Settlement Load(ulong seed = 1979)
        {
            if (Root == null) return null;
            EnsureKinds();

            var world = WorldBuilder.Build(
                VillageParser.Parse(File.ReadAllText(Path.Combine(Root, "village.txt"))), seed);
            var names = NameTable.Parse(File.ReadAllText(Path.Combine(Root, "names.txt")));
            var particulars = ParticularsTable.Parse(File.ReadAllText(Path.Combine(Root, "particulars.txt")));
            var people = PopulationGenerator.Generate(world, names, particulars, seed);

            return new Settlement("Ashcombe", world, people, seed);
        }

        public static VillageLayout Layout()
        {
            if (Root == null) return null;
            EnsureKinds();
            return VillageParser.Parse(File.ReadAllText(Path.Combine(Root, "village.txt")));
        }
    }
}
