using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Noir.Core.People;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Are the clips we have downloaded actually usable, and which ones are still missing?
    ///
    /// Three things can be wrong with a Mixamo download and NONE of them throws: the rig can be
    /// Generic instead of Humanoid, so it silently refuses to retarget; the cycle can be
    /// un-looped, so a walk stops dead after one stride; and the clip can carry ROOT MOTION,
    /// which is the expensive one - the animation would drive the transform itself and fight
    /// `Simulation`, which is what actually decides where everybody is, and people would walk off
    /// their own paths. That last one is invisible until there is a crowd to watch drift.
    ///
    /// So this measures all three, and then says which `Activity` states still have no clip
    /// behind them.
    ///
    /// THE COUNT IS NOT WRITTEN DOWN ANY MORE, AND THAT IS THE POINT. This said "the thirteen
    /// Activity states" while the enum had fourteen, and a hand-counted number in a comment is
    /// wrong the moment somebody adds a member - silently, because nothing checks a comment. The
    /// sweep below enumerates the enum itself, so a fifteenth Activity is covered the day it is
    /// added by somebody who never opens this file.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.AnimationCheck.Run
    /// </summary>
    public static class AnimationCheck
    {
        private const string Folder = "Assets/Noir/Animations";

        /// <summary>
        /// How much root travel still counts as standing still.
        ///
        /// An in-place clip is not exactly zero - a walk cycle shifts its weight, so the hips
        /// drift a centimetre or two a second and come back. A clip that actually travels moves at
        /// something like a metre a second, so anything under a tenth of that is in place and the
        /// gap between the two cases is an order of magnitude wide.
        /// </summary>
        private const float StandingStill = 0.1f;

        [MenuItem("Noir/Check The Animations")]
        public static void Run()
        {
            if (!Directory.Exists(Folder))
            {
                Debug.LogError($"[anim] no {Folder} - nothing downloaded yet.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var have = new HashSet<string>(System.StringComparer.Ordinal);
            int faults = 0, found = 0, travelling = 0, generic = 0, unlooped = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview"))
                        continue;

                    found++;
                    have.Add(clip.name);

                    // Metres a second the root travels over the clip. Mixamo's "In Place" box is
                    // what makes this ~0; without it the clip walks away with the character.
                    float travel = clip.averageSpeed.magnitude;

                    var wrong = new List<string>();
                    if (!clip.humanMotion) wrong.Add("NOT HUMANOID - will not retarget");
                    if (!clip.isLooping) wrong.Add("not looping - will stop after one cycle");
                    if (travel > StandingStill)
                        wrong.Add($"ROOT MOTION {travel:0.00} m/s - re-download with In Place");

                    if (wrong.Count == 0) continue;

                    // ONLY THE BROKEN ONES, and only the first twenty of those. A line per clip
                    // is readable at nine and is a wall at nine hundred, which is the scale a
                    // bulk download arrives at - and a wall is where a real fault hides.
                    faults++;
                    if (faults <= 20)
                        Debug.LogWarning($"[anim] {clip.name,-32} {clip.length,5:0.00}s  "
                                       + $"travel {travel,5:0.00} m/s  {string.Join("; ", wrong)}");
                    else if (faults == 21)
                        Debug.LogWarning("[anim] ...and more. Fix these first.");

                    if (travel > StandingStill) travelling++;
                    if (!clip.humanMotion) generic++;
                    if (!clip.isLooping) unlooped++;
                }
            }

            // ---- what the table asks for and the folder has not got ----
            AgentAnimation.Reload();          // the file may have been edited since Play

            var missing = new SortedSet<string>(System.StringComparer.Ordinal);
            var wanted = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var row in AgentAnimation.Rows)
            foreach (var clip in row.Value)
            {
                wanted.Add(clip);
                if (!have.Contains(clip)) missing.Add(clip);
            }

            // ---- and every Activity the simulation can actually be in ----
            //
            // ASKED OF THE ENUM, NOT OF THE TABLE. Sweeping the rows only tells you the rows are
            // satisfied; it cannot notice an Activity that HAS no row, which is the failure that
            // matters - `Resolve` falls back to `default` and the person plays a generic idle
            // instead of the thing they are doing, forever, with nothing said. Enumerating the
            // enum means a fifteenth Activity is covered on the day somebody adds it.
            var rowless = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (Activity doing in System.Enum.GetValues(typeof(Activity)))
            {
                // AwayFromTown is deliberately not drawn - those people are out of the map, and
                // AgentMeshView switches their figure off entirely. A row for it would never be
                // reached, so its absence is correct rather than a gap.
                if (doing == Activity.AwayFromTown) continue;

                if (!AgentAnimation.Rows.ContainsKey(doing.ToString().ToLowerInvariant()))
                    rowless.Add(doing.ToString());
            }

            if (rowless.Count > 0)
                Debug.LogWarning($"[anim] {rowless.Count} Activity state(s) have no row in "
                               + "Content/animations.txt, so everybody doing them falls back to "
                               + $"`default` and plays a generic idle: {string.Join(", ", rowless)}");

            // ---- and the other way round: downloaded, and nothing uses it ----
            //
            // The failure this catches is the quiet one. A clip nobody references is not an
            // error, throws nothing and looks exactly like a clip that works - so somebody
            // downloads Sweeping, waits for a caretaker to sweep, and nothing ever happens.
            var idle = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var name in have)
                if (!wanted.Contains(name)) idle.Add(name);

            if (idle.Count > 0)
            {
                var shown = new List<string>();
                foreach (var name in idle) { shown.Add(name); if (shown.Count >= 25) break; }

                Debug.Log($"[anim] {idle.Count} downloaded that no row in Content/animations.txt "
                        + $"mentions, so nothing will ever play them: {string.Join(", ", shown)}"
                        + (idle.Count > shown.Count ? $", ...and {idle.Count - shown.Count} more" : ""));
            }

            Inventory(have, wanted);

            Debug.Log($"[anim] {found} clip{(found == 1 ? "" : "s")} in {Folder}, "
                    + $"{found - faults} ready to use and {faults} with something wrong"
                    + (faults == 0 ? "." : $" - {generic} not humanoid, {unlooped} not looping, "
                                         + $"{travelling} carrying ROOT MOTION."));

            // The one worth calling out separately, because a bulk download is exactly how it
            // happens: a script that fetches everything does not tick Mixamo's In Place box, and
            // a clip that travels will fight the simulation for control of where somebody is.
            if (travelling > 0)
                Debug.LogWarning($"[anim] {travelling} clips carry root motion. Those will drag "
                               + "people off their own paths - re-download them with In Place.");

            Debug.Log(missing.Count == 0
                ? "[anim] every Activity has a clip behind it."
                : $"[anim] still wanted: {string.Join(", ", missing)}");

            // THE EXIT CODE HAS TO AGREE WITH THE LINE ABOVE IT.
            //
            // This was `faults == 0 ? 0 : 1` - so a run could print "still wanted: Sweeping,
            // Praying" and then exit 0, and any script gating on it was told everything was fine.
            // A check that reports a fault and returns success is worse than no check: it is a
            // green light with the fault printed underneath, and the green is what gets read.
            //
            // A clip the table names and the folder has not got is a person who plays nothing at
            // all, and an Activity with no row is a person who plays the wrong thing forever, so
            // both count.
            int broken = faults + missing.Count + rowless.Count;
            if (Application.isBatchMode) EditorApplication.Exit(broken == 0 ? 0 : 1);
        }

        private const string Listing = "docs/animations-downloaded.md";

        /// <summary>
        /// Write out everything in the folder, marked used or unused.
        ///
        /// A console line is the wrong shape for this once there are hundreds of clips: it scrolls,
        /// it cannot be sorted, and it cannot be read next to the table it is about. The job it has
        /// to support is a person going through a bulk Mixamo download deciding which clips are
        /// worth a row, and that is a job done with a file open beside `animations.txt` - so the
        /// unused ones come out already shaped like rows, ready to be cut and pasted into one.
        /// </summary>
        /// <summary>
        /// Rewrite `docs/animations-downloaded.md` from what is on disk, and nothing else.
        ///
        /// Exists so `AnimatorBuild` can call it. That build is the step nobody can skip when a
        /// clip is added - the controller only carries clips a row asked for - whereas running
        /// this checker is a second thing to remember, and remembering is not a mechanism. The
        /// listing had drifted for exactly that reason.
        ///
        /// It does none of the clip-quality measurement `Run` does: this is the inventory only,
        /// which is all the document contains.
        /// </summary>
        public static void WriteListing()
        {
            var have = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { Folder }))
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                    have.Add(clip.name);

            AgentAnimation.Reload();
            var wanted = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var row in AgentAnimation.Rows)
            foreach (var clip in row.Value)
                wanted.Add(clip);

            Inventory(have, wanted);
        }

        private static void Inventory(HashSet<string> have, HashSet<string> wanted)
        {
            var used = new SortedSet<string>(System.StringComparer.Ordinal);
            var spare = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var name in have) (wanted.Contains(name) ? used : spare).Add(name);

            var text = new System.Text.StringBuilder();
            text.Append("# Animations downloaded\n\n")
                .Append("Generated by `Noir/Check The Animations`. Do not edit - rerun it.\n\n")
                .Append($"{have.Count} clips in `Assets/Noir/Animations`: ")
                .Append($"{used.Count} wired into `Content/animations.txt`, {spare.Count} not.\n\n")
                .Append("## Wired up\n\n");

            foreach (var name in used) text.Append($"- {name}\n");

            text.Append("\n## Downloaded but unused\n\n")
                .Append("Nothing will ever play these. Move a line into a row in ")
                .Append("`Content/animations.txt` to use it - the situation word goes on the left, ")
                .Append("and several clips may share a row, in which case each person is given one ")
                .Append("of them off their own key.\n\n```\n");

            foreach (var name in spare) text.Append($"#                {name}\n");
            text.Append("```\n");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Listing));
                File.WriteAllText(Listing, text.ToString());
                Debug.Log($"[anim] wrote {Listing} - {have.Count} clips, {spare.Count} unused.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[anim] could not write {Listing}: {e.Message}");
            }
        }
    }
}
