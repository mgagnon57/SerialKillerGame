using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Anything dropped in Assets/Noir/Animations imports itself correctly.
    ///
    /// A Mixamo FBX arrives as a GENERIC rig with no looping, which is Unity's default for a
    /// skeleton it has not been told anything about - and both of those are wrong for us. Generic
    /// does not retarget, so a clip authored on Mixamo's mannequin would refuse to play on our
    /// people at all; and a walk cycle that does not loop stops dead at the end of one stride.
    ///
    /// Neither is hard to fix in the Inspector. Both are easy to FORGET on the ninth file, and a
    /// forgotten one fails quietly - the clip is there, the controller has a state for it, and the
    /// person just stands still. So the folder does it, and the only step left for a human is
    /// dropping the file in. That is the same reasoning as everything else here booting itself off
    /// VillageHost rather than out of a scene: the number of steps that must be done by hand in
    /// the editor is kept at zero, because those are the ones I cannot do for you.
    ///
    /// LOOPING EVERYTHING IS DELIBERATE. A cycle that does not loop is always wrong - it stops
    /// dead after one stride - and a one-shot that loops merely repeats, which is the cheaper
    /// mistake. When the choice has to be made blind, on a folder somebody has just emptied a bulk
    /// download into, that is the right way round.
    ///
    /// "AND THERE ARE NONE ON THE LIST ANYWAY" IS WHAT THIS USED TO ADD, AND IT IS FALSE. There
    /// are about a dozen one-shot gestures among the 87 - waving, clapping, greeting, picking an
    /// object up - and every one of them is being looped. That is not a crisis, it is the cheaper
    /// mistake being taken knowingly, but it was being taken while claiming the situation did not
    /// arise, which is how it went unexamined.
    ///
    /// Whether to fix it is open and deliberately not decided here: see decision 7 in
    /// docs/ANIMATION-FIXES.md. `loopPose` does not merely mark a clip, it WARPS it to meet its
    /// own start - harmless on a true cycle, wrong on a gesture - so this wants measuring on a few
    /// clips before anybody rewrites 87 import settings.
    ///
    /// AND NOTHING HERE RUNS TWICE. The guard below is `importSettingsMissing`, which is true on
    /// first import and never again, so anything set by hand afterwards is permanent and this will
    /// not correct it. That is why docs/ASSETS.md no longer teaches a manual procedure.
    /// </summary>
    public sealed class AnimationImport : AssetPostprocessor
    {
        private const string Folder = "Assets/Noir/Animations/";

        private bool Ours => assetPath.StartsWith(Folder, System.StringComparison.Ordinal);

        private void OnPreprocessModel()
        {
            if (!Ours) return;

            var importer = (ModelImporter)assetImporter;

            // Only on the FIRST import. Reimporting a file somebody has since adjusted by hand
            // and stamping over their change would be worse than not helping at all.
            if (!importer.importSettingsMissing) return;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            // Downloaded without skin, so there is no material or texture to bring in and asking
            // for them leaves an empty Materials folder beside every clip.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;

            Debug.Log($"[anim] {System.IO.Path.GetFileName(assetPath)}: imported as Humanoid.");
        }

        private void OnPreprocessAnimation()
        {
            if (!Ours) return;

            var importer = (ModelImporter)assetImporter;
            if (!importer.importSettingsMissing) return;

            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = true;

                // Mixamo names the take after the file, which is what AgentAnimation asks for by
                // name - so a clip downloaded as "Standing Idle" stays "Standing Idle" and the
                // Animator state can be called the same thing with nothing in between.
                clips[i].name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            }

            importer.clipAnimations = clips;
        }
    }
}
