using System;
using System.IO;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// Finds the repo's Content/ folder by walking up from the executable.
    /// Keeps content authored once, in one place, read by the harness, the tests and Unity alike.
    /// </summary>
    public static class ContentPath
    {
        private static string _root;

        public static string Root
        {
            get
            {
                if (_root != null) return _root;

                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Content");
                    if (Directory.Exists(candidate) &&
                        System.IO.File.Exists(Path.Combine(candidate, "fixture-village.txt")))
                    {
                        _root = candidate;
                        return _root;
                    }
                    dir = dir.Parent;
                }
                throw new DirectoryNotFoundException(
                    "Could not locate the Content/ folder above " + AppContext.BaseDirectory);
            }
        }

        public static string PathTo(string name) => Path.Combine(Root, name);

        public static string ReadAll(string name)
        {
            EnsureKinds();
            return System.IO.File.ReadAllText(PathTo(name));
        }

        /// <summary>
        /// Put the authored kinds table in place before anything reads a `place` line.
        ///
        /// Here rather than in each command because every command reaches content through this
        /// class, and one that forgot would be looking at a different set of kinds from the rest
        /// — which is how `check` and `who` come to disagree about what a shop is.
        /// </summary>
        private static void EnsureKinds()
        {
            if (PlaceKindTable.IsInstalled) return;
            PlaceKindTable.Install(
                PlaceKindTable.Parse(System.IO.File.ReadAllText(PathTo("kinds.txt"))));
        }
    }
}
