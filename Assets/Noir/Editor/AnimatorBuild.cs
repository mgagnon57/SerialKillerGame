using System.Collections.Generic;
using System.IO;
using Noir.Core.Contracts;
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
    /// wants directly, so the states only have to EXIST.
    ///
    /// **Eighty-seven states and no arrows.** This said "nine" until 2026-08-08, which was true of
    /// the first version and had been wrong by an order of magnitude for a long time - and the
    /// number is the entire argument for the design, because eighty-seven states in a hand-drawn
    /// graph is exactly the combinatorial mess the paragraph above is describing. A stale number
    /// there quietly turns the case FOR this approach into an argument nobody can check.
    ///
    /// That also means the simulation stays the thing that decides. A transition graph is a second
    /// opinion about what a person should be doing, sitting between the day planner and the screen
    /// and disagreeing with it; going straight to the state leaves `DayPlan` in charge, which is
    /// where the decision belongs.
    ///
    /// STATE NAMES ARE THE CLIP NAMES, which are Mixamo's names, which are what AgentAnimation
    /// asks for. So a clip called "Standing Idle" on the website is the state the code crossfades
    /// to, and adding an animation is a download, a row in Content/animations.txt, and a rerun of
    /// this. THE RERUN IS NOT OPTIONAL: this controller carries only the clips a row asked for at
    /// the time it was built, so a row added without one is a clip nobody can play.
    ///
    /// AND "NOTHING IS TRANSLATED ALONG THAT CHAIN" IS WHAT THIS USED TO SAY, CONFIDENTLY, AND IT
    /// WAS FALSE. `AnimatorStateMachine.AddState` is not a setter - `MakeUniqueStateName` owns the
    /// name that comes back, and it sanitises. A clip named `Standing Idle Looking Ver. 1` became
    /// a state named `Standing Idle Looking Ver_ 1`; `Drive` hashed the dotted name from the
    /// table, matched nothing, and returned silently after already writing speed. One person in
    /// six treadmilled the walk cycle on the spot at every door pause, invisibly, for as long as
    /// that file existed - and the sentence asserting it could not happen is a large part of why
    /// nobody looked. The loop below compares what it asked for against what it got.
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

            // THE CONTENT SOURCE, WHICH THE GAME INSTALLS AND A MENU ITEM DOES NOT.
            // AgentAnimation reads through Noir.Core.Contracts.Content now that the parser
            // lives in Core, and Content throws "No content source installed" until somebody
            // hands it one. TownPipeline.Build does that for the game; this runs from a menu
            // with no pipeline anywhere near it, so it has to do it itself. Idempotent.
            Content.Install(ContentLoader.AsSource);
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

            // WHAT WAS ASKED FOR AGAINST WHAT CAME BACK.
            //
            // `AddState` is not a setter. `MakeUniqueStateName` owns the name that comes back, and
            // it sanitises: a clip called `Standing Idle Looking Ver. 1` became a state called
            // `Standing Idle Looking Ver_ 1`. `AgentAnimation.Drive` then hashes the DOTTED name,
            // finds no such state, and bails silently after having already written speed - so one
            // person in six treadmilled the walk cycle in place at every door pause, and no
            // instrument in the project could see it. It survived because nothing ever compared
            // these two strings.
            var renamed = new List<string>();

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

                if (state.name != pair.Key) renamed.Add($"'{pair.Key}' -> '{state.name}'");

                state.motion = pair.Value;
                state.writeDefaultValues = false;
                if (pair.Key == Default) fallback = state;
            }

            // Something has to be playing before anybody is told what to play. An idle is the
            // honest choice; the alphabetically-first state, which is what Unity picks on its own,
            // means everybody in Rossville starts the day digging.
            machine.defaultState = fallback ?? machine.states[0].state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log($"[animator] {Output}: {clips.Count} states, no transitions, "
                    + $"default '{machine.defaultState.name}'.");

            // SAVED FIRST, THEN FAILED. Deliberate: the controller is written either way, so 86 of
            // the 87 clips keep working while somebody fixes the one name. A build step that
            // refused to save would take the whole town's animation down over a full stop.
            //
            // No attempt is made to mirror Unity's sanitisation rule and none should be - it is
            // internal, undocumented, and does more than replace a period. The check is that the
            // two strings are equal, which needs no knowledge of the rule at all.
            if (renamed.Count > 0)
            {
                Debug.LogError($"[animator] Unity renamed {renamed.Count} state(s) on the way in, so "
                             + "AgentAnimation will hash a name the controller does not have and "
                             + "those people will silently not animate:\n  "
                             + string.Join("\n  ", renamed)
                             + "\n  Rename the CLIP so the name survives - see Content/animations.txt.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // THE LISTING IS REGENERATED HERE BECAUSE THIS IS THE STEP NOBODY CAN SKIP.
            //
            // `docs/animations-downloaded.md` says "Do not edit - rerun `Noir/Check The
            // Animations`", and it drifted anyway: it still named `Standing Idle Looking Ver. 1`
            // after the rename, because regenerating it was a SECOND thing to remember and
            // remembering is not a mechanism. Adding a clip already forces this rebuild - the
            // controller carries only the clips a row asked for - so hanging the listing off it
            // makes the document that describes the animations impossible to forget.
            AnimationCheck.WriteListing();

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
