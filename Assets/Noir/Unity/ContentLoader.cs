using System.IO;
using UnityEngine;
using Noir.Core.Contracts;

namespace Noir.Unity
{
    /// <summary>
    /// Reads the authored content from Content/ at the repo root — the same files the headless
    /// harness and the tests read, so there is exactly one copy of the village.
    ///
    /// In the editor Application.dataPath is &lt;project&gt;/Assets, so ../Content resolves. A
    /// standalone build would need these copied into StreamingAssets; that is a build-time
    /// concern and deliberately not solved yet.
    /// </summary>
    public static class ContentLoader
    {
        /// <summary>
        /// This loader, behind the Core-side interface.
        ///
        /// The survey tables used to call ContentLoader directly, which is what pinned five
        /// thousand lines of pure parsing and plane geometry inside the Unity assembly where no
        /// test can compile them. They ask Noir.Core.Contracts.Content now; this is what answers.
        /// Install it once, early - TownPipeline.Build does it before anything reads a file.
        /// </summary>
        private sealed class Source : IContentSource
        {
            public string Read(string fileName) => ContentLoader.Read(fileName);
            public System.DateTime WrittenAt(string fileName) => ContentLoader.WrittenAt(fileName);
        }

        /// <summary>Hand this to Content.Install. Idempotent; the instance carries no state.</summary>
        public static IContentSource AsSource { get; } = new Source();

        public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Content"));

        /// <summary>
        /// Whether there is content to load, decided by the map the GAME builds.
        ///
        /// This used to look for village.txt, which is the small retired fixture the unit tests
        /// build their world from and has not been the town for months. Two consequences, both
        /// quiet: a Content folder holding a perfectly good Rossville but no fixture read as no
        /// content at all, and renaming the fixture - which is a test concern - would have taken
        /// the whole game down with it.
        /// </summary>
        public static bool Exists =>
            Directory.Exists(Root) && File.Exists(Path.Combine(Root, VillageHost.MapFile));

        public static string Read(string fileName)
        {
            string path = Path.Combine(Root, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Content file missing: {path}");
            return File.ReadAllText(path);
        }

        /// <summary>
        /// When a content file was last written, for anyone caching a parse of it.
        ///
        /// These files are edited by hand while the editor is open, and a static cache survives a
        /// domain reload - so "parsed once" quietly means "parsed before your edit" unless the
        /// holder of the cache can tell that the file moved underneath it. Returns
        /// <see cref="System.DateTime.MinValue"/> for a missing file, which differs from any real
        /// timestamp and so reads as "reparse".
        /// </summary>
        public static System.DateTime WrittenAt(string fileName)
        {
            string path = Path.Combine(Root, fileName);
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : System.DateTime.MinValue;
        }
    }
}
