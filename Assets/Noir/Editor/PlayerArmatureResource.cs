using System.IO;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Puts the player's body somewhere a SHIPPED BUILD can reach it.
    ///
    /// `Player.Spawn` loads `Assets/StarterAssets/.../PlayerArmature.prefab` through
    /// `AssetDatabase`, which does not exist outside the editor - so the whole method sits behind
    /// `#if UNITY_EDITOR` and **pressing P in a shipped Rossville does nothing at all**. No error,
    /// no log, no body: the one control that lets somebody walk down the street they have just
    /// built is editor-only, and always has been.
    ///
    /// `Resources` is the one folder Unity packs whole and exposes by name at runtime, so a prefab
    /// VARIANT of the armature living there is reachable from a build. A variant rather than a
    /// copy, deliberately: it keeps every override and every future change to the original, and it
    /// is one asset rather than a duplicated rig.
    ///
    /// WHY NOT JUST MOVE STARTER ASSETS INTO Resources. Not because scene references would break -
    /// Unity references by GUID and they survive an in-project move, and the plan's stated reason
    /// was wrong about that. The real reason is enough on its own: it is a VENDORED PACKAGE FOLDER
    /// and the next re-import puts it back, silently, taking the move with it.
    ///
    /// Run once per machine, or after re-importing Starter Assets:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.PlayerArmatureResource.Make
    /// </summary>
    public static class PlayerArmatureResource
    {
        private const string Source =
            "Assets/StarterAssets/ThirdPersonController/Prefabs/PlayerArmature.prefab";

        private const string Folder = "Assets/Noir/Resources";

        /// <summary>The name `Resources.Load` is given at runtime. Must match Player.ArmatureResource.</summary>
        public const string ResourceName = "PlayerArmature";

        private static string Target => Folder + "/" + ResourceName + ".prefab";

        [MenuItem("Noir/Make The Player Shippable")]
        public static void Make()
        {
            int code = 0;
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
                if (source == null)
                {
                    Debug.LogError($"[armature] {Source} is not here. Starter Assets is a vendored "
                        + "package - import it, then run this again.");
                    code = 1;
                }
                else
                {
                    Directory.CreateDirectory(Folder);
                    AssetDatabase.Refresh();

                    if (AssetDatabase.LoadAssetAtPath<GameObject>(Target) != null)
                    {
                        Debug.Log($"[armature] {Target} already exists - left alone. Delete it and "
                                + "run again to re-make it from the current Starter Assets prefab.");
                    }
                    else
                    {
                        // A variant, instantiated and saved as one, so it tracks the original.
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                        try
                        {
                            var made = PrefabUtility.SaveAsPrefabAsset(instance, Target);
                            if (made == null)
                            {
                                Debug.LogError("[armature] could not save the variant at " + Target);
                                code = 1;
                            }
                            else
                            {
                                Debug.Log($"[armature] {Target} made from {Source}. Pressing P in a "
                                        + "shipped build now puts a body on the street.");
                            }
                        }
                        finally { Object.DestroyImmediate(instance); }
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[armature] FAILED: " + ex);
                code = 1;
            }

            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
