using System.IO;
using Noir.Unity;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Builds the townsfolk's Animator Controller out of whatever is in the animations folder.
    ///
    /// NO TRANSITION GRAPH, AND THAT IS THE WHOLE TRICK. An Animator Controller is normally a
    /// state machine somebody draws by hand: states, arrows, conditions, parameters, and a
    /// combinatorial mess the moment there are nine of them. None of that is needed here, because
    /// `AgentAnimation.Drive` calls CrossFadeInFixedTime with a state hash - it names the state it
    /// wants directly, so the states only have to EXIST. Nine states and no arrows.
    ///
    /// That also means the simulation stays the thing that decides. A transition graph is a second
    /// opinion about what a person should be doing, sitting between the day planner and the screen
    /// and disagreeing with it; going straight to the state leaves `DayPlan` in charge, which is
    /// where the decision belongs.
    ///
    /// STATE NAMES ARE THE CLIP NAMES, which are Mixamo's names, which are what AgentAnimation
    /// asks for. Nothing is translated anywhere along that chain, so a clip called "Standing Idle"
    /// on the website is the state the code crossfades to, and adding a tenth animation is a
    /// download plus a rerun of this.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.AnimatorBuild.Run
    /// </summary>
    public static class AnimatorBuild
    {
        private const string Folder = "Assets/Noir/Animations";
        private const string Output = Folder + "/Townsfolk.controller";

        /// <summary>What a person does when nothing else has been asked for.</summary>
        private const string Default = "Standing Idle";

        [MenuItem("Noir/Build The Townsfolk Animator")]
        public static void Run()
        {
            if (!Directory.Exists(Folder))
            {
                Debug.LogError($"[animator] no {Folder} - nothing to build from.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // Rebuilt from scratch every time rather than edited in place: a controller that has
            // accumulated states from clips somebody has since deleted is a controller nobody can
            // reason about, and this is cheap enough to throw away.
            AssetDatabase.DeleteAsset(Output);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(Output);
            var machine = controller.layers[0].stateMachine;

            var clips = new System.Collections.Generic.SortedDictionary<string, AnimationClip>(
                System.StringComparer.Ordinal);

            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { Folder }))
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview")) continue;
                clips[clip.name] = clip;
            }

            if (clips.Count == 0)
            {
                Debug.LogError($"[animator] no clips in {Folder}.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // ---- only the clips something actually asks for ----
            //
            // A state per downloaded clip is right at nine and wrong at two thousand, which is
            // what a bulk Mixamo pull comes to: the controller becomes a multi-megabyte asset
            // full of states nothing will ever crossfade to, every one of which Unity loads and
            // keeps resident. AgentAnimation.Drive only ever names a clip that a row in
            // Content/animations.txt mentions, so anything else is dead weight by construction.
            //
            // Downloading more than you use is the CORRECT workflow - you cannot tell whether a
            // clip is right until you have it - so this filters at build time rather than
            // asking anybody to prune a folder.
            AgentAnimation.Reload();
            var wanted = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (var row in AgentAnimation.Rows)
            foreach (var name in row.Value) wanted.Add(name);
            wanted.Add(Default);

            int ignored = 0;
            foreach (var name in new System.Collections.Generic.List<string>(clips.Keys))
                if (!wanted.Contains(name)) { clips.Remove(name); ignored++; }

            if (clips.Count == 0)
            {
                Debug.LogError($"[animator] {ignored} clips in {Folder} and not one of them is "
                             + "named by Content/animations.txt, so the controller would be "
                             + "empty. Add a row before building.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            AnimatorState fallback = null;
            int placed = 0;

            foreach (var pair in clips)
            {
                // Laid out on a grid rather than added blind. AddState with no position drops
                // every state on the same spot, which is invisible at nine and is a single
                // illegible pile at four hundred - and four hundred is what a bulk download of
                // Mixamo's library comes to. Twenty to a column, alphabetical down and across, so
                // a named state can be found by eye without a search box.
                var state = machine.AddState(pair.Key, new Vector3(
                    (placed / 20) * 260f, (placed % 20) * 60f, 0f));
                placed++;

                state.motion = pair.Value;
                state.writeDefaultValues = false;
                if (pair.Key == Default) fallback = state;
            }

            // Something has to be playing before anybody is told what to play. An idle is the
            // honest choice; the alphabetically-first state, which is what Unity picks on its own,
            // means everybody in Northgate starts the day digging.
            machine.defaultState = fallback ?? machine.states[0].state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log($"[animator] {Output}: {clips.Count} states, no transitions, "
                    + $"default '{machine.defaultState.name}'.");

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
