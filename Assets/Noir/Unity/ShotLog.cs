using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// A receipt beside every set of rendered frames: when they were taken, by which Unity, of
    /// which town - and the hash of each file, so two frames that came out identical say so.
    ///
    /// WHY THIS EXISTS, AND IT COST AN AUDIT. `docs/snapshots` is fully tracked and its working
    /// tree is normally clean, so a session comparing against it is comparing against HEAD. The
    /// newest image sat at 2026-08-07 while thirty commits landed on top of it, and NOTHING IN A
    /// PNG SAYS WHEN IT WAS TAKEN. "I rendered it and looked" and "I opened the committed one"
    /// are indistinguishable from the outside - which is how twelve identical frames of a field
    /// survived a fortnight, and how a roof got reported as flat from three cameras that could
    /// not see a roof.
    ///
    /// THE DUPLICATE CHECK IS THE SECOND HALF AND IT IS THE LOUDER ONE. A render whose frames
    /// collapse to the same bytes has not photographed a town, it has photographed one thing
    /// twelve times; the file count looks perfect either way, which is exactly what the three
    /// diagnostics asserting "the right number of image files appeared" can be fooled by.
    ///
    /// Lives in Noir.Unity rather than Noir.Editor only because both Noir.Editor and
    /// Noir.PlayTests need it. It writes a file when called and costs nothing when it is not.
    /// </summary>
    public static class ShotLog
    {
        /// <summary>
        /// Write `RENDERED-&lt;label&gt;.txt` beside the frames and log the same text.
        ///
        /// The stamp files land in `docs/snapshots`, which is tracked, so they show up as changes
        /// on every render. That is correct and it is the signal: a stamp whose timestamp is older
        /// than the code you are judging means you are looking at somebody else's picture. Stage
        /// them deliberately, by name - never `git add -A`.
        /// </summary>
        public static void Stamp(string label, string dir, IEnumerable<string> written)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(label).Append('\n');
            sb.Append("taken   ").Append(System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"))
              .Append(" UTC\n");
            sb.Append("unity   ").Append(Application.unityVersion).Append('\n');
            sb.Append("town    ShowBuildings=").Append(VillageHost.ShowBuildings)
              .Append("  batch=").Append(Application.isBatchMode).Append('\n');

            var byHash = new Dictionary<string, string>();
            var collided = new List<string>();
            int n = 0;

            foreach (var f in written)
            {
                if (string.IsNullOrEmpty(f) || !File.Exists(f)) continue;
                string hash, name = Path.GetFileName(f);
                long size;
                try
                {
                    var bytes = File.ReadAllBytes(f);
                    size = bytes.LongLength;
                    hash = Hash(bytes);
                }
                catch (System.Exception ex) { sb.Append("  unreadable  ").Append(name)
                                               .Append("  ").Append(ex.Message).Append('\n');
                                             continue; }

                sb.Append(hash).Append("  ").Append(size.ToString().PadLeft(9)).Append("  ")
                  .Append(name).Append('\n');
                n++;

                if (byHash.TryGetValue(hash, out var first)) collided.Add(first + " == " + name);
                else byHash[hash] = name;
            }

            sb.Append(n).Append(" frame(s), ").Append(byHash.Count).Append(" distinct\n");

            string path = Path.Combine(dir, "RENDERED-" + label + ".txt");
            try { File.WriteAllText(path, sb.ToString()); }
            catch (System.Exception ex) { Debug.LogWarning($"[shotlog] could not write {path}: {ex.Message}"); }

            Debug.Log("[shotlog]\n" + sb);

            if (collided.Count > 0)
                Debug.LogError($"[shotlog] {label}: {collided.Count} frame(s) came out BYTE-IDENTICAL "
                    + "to another in the same run - " + string.Join(", ", collided)
                    + ". A camera that photographs the same thing twice looks exactly like a render "
                    + "that worked, because the file count is right either way.");
        }

        /// <summary>
        /// FNV-1a over the file, printed as sixteen hex digits.
        ///
        /// Not a cryptographic hash and not trying to be: the question is only "are these two
        /// files the same bytes", and a PNG that differs by one pixel differs by many bytes.
        /// Deliberately not System.Security.Cryptography, which pulls a dependency into the
        /// runtime assembly for a question that does not need one.
        /// </summary>
        private static string Hash(byte[] bytes)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < bytes.Length; i++)
            {
                h ^= bytes[i];
                h *= 1099511628211UL;
            }
            return h.ToString("x16");
        }
    }
}
