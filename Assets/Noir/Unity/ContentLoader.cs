using System.IO;
using UnityEngine;

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
        public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Content"));

        public static bool Exists => Directory.Exists(Root) && File.Exists(Path.Combine(Root, "village.txt"));

        public static string Read(string fileName)
        {
            string path = Path.Combine(Root, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Content file missing: {path}");
            return File.ReadAllText(path);
        }
    }
}
