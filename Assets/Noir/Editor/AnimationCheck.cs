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
    /// So this measures all three, and then says which of the thirteen `Activity` states still
    /// have no clip behind them.
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
            int faults = 0, found = 0;

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

                    if (wrong.Count > 0) faults++;

                    Debug.Log($"[anim] {clip.name,-24} {clip.length,5:0.00}s  "
                            + $"travel {travel,5:0.00} m/s  "
                            + (wrong.Count == 0 ? "ok" : string.Join("; ", wrong)));
                }
            }

            // ---- what the day plan still has nothing to play ----
            var missing = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (Activity doing in System.Enum.GetValues(typeof(Activity)))
            {
                string still = AgentAnimation.ClipFor(doing, moving: false);
                if (!string.IsNullOrEmpty(still) && !have.Contains(still)) missing.Add(still);
            }
            foreach (var move in new[] { AgentAnimation.Walking, AgentAnimation.Running })
                if (!have.Contains(move)) missing.Add(move);

            Debug.Log($"[anim] {found} clip{(found == 1 ? "" : "s")} in {Folder}, "
                    + $"{faults} with something wrong.");

            Debug.Log(missing.Count == 0
                ? "[anim] every Activity has a clip behind it."
                : $"[anim] still wanted: {string.Join(", ", missing)}");

            if (Application.isBatchMode) EditorApplication.Exit(faults == 0 ? 0 : 1);
        }
    }
}
