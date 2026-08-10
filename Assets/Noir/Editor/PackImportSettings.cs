using System;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Clears the sRGB flag on the pack's ambient-occlusion maps, which ship with it set.
    ///
    /// AN OCCLUSION MASK IS LINEAR DATA. Every `*_AO.png.meta` under `Assets/polyperfect` carries
    /// `sRGBTexture: 1` with `textureType: 0`, and this project is a Linear-colour-space project,
    /// so each texel is decoded through a gamma curve that has no business being applied to a
    /// mask - about 2.4x too strong at the mid tones.
    ///
    /// SAY HOW MUCH THAT IS WORTH BEFORE ANYBODY BUDGETS A DAY FOR IT. The maps actually bound to
    /// Rossville's ground average 0.985, 0.946, 0.967 and 0.994 - they are nearly white, because
    /// they are 2048px TILING sheets that cannot know where a tree trunk or a wall foot is. With
    /// the keyword correctly enabled (see SurfaceTextures.ApplyPack) they darken the ground by
    /// 1-5%; decoded correctly as well, 0.8-6.7%. Both are invisible. If you are hunting for
    /// missing contact shadows, they are not in here.
    ///
    /// SO WHY COMMIT A TOOL FOR IT. Because `.gitignore` excludes `Assets/polyperfect`, which
    /// means these settings exist on ONE MACHINE AND NOWHERE ELSE: a hand fix in the Inspector is
    /// invisible to the repository forever, and a fresh clone re-imports at the pack's own
    /// defaults. That is exactly the problem `MeshReadable` was written for, and this is the same
    /// answer in the same shape - the fix travels as code because the settings cannot.
    ///
    /// TAKE IT AS A RIDER on an editor-closed window you are already spending. It reimports a few
    /// dozen 2048px textures, which is about a minute, and it is worth single-digit percentages.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.PackImportSettings.Fix -logFile &lt;log&gt;
    /// </summary>
    public static class PackImportSettings
    {
        private const string PackRoot = "Assets/polyperfect";

        [MenuItem("Noir/Fix Pack Import Settings")]
        public static void Fix()
        {
            int changed = 0, already = 0, skipped = 0;

            try
            {
                if (!System.IO.Directory.Exists(PackRoot))
                {
                    Debug.LogWarning($"[packimport] {PackRoot} is not here. It is gitignored, so a "
                        + "fresh clone has no pack at all - buy or re-import it, then run this and "
                        + "Noir/Make City Meshes Readable.");
                    return;
                }

                var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { PackRoot });
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var guid in guids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!path.EndsWith("_AO.png", StringComparison.OrdinalIgnoreCase)) continue;

                        if (AssetImporter.GetAtPath(path) is not TextureImporter ti) { skipped++; continue; }
                        if (!ti.sRGBTexture) { already++; continue; }

                        ti.sRGBTexture = false;
                        ti.SaveAndReimport();
                        changed++;
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }

                Debug.Log($"[packimport] {changed} AO map(s) taken off sRGB, {already} already "
                        + $"linear, {skipped} not importable as textures. Worth 1-5% of ground "
                        + "brightness and nothing else - see the docstring before reporting it as "
                        + "a fix for anything visible.");
            }
            catch (Exception ex) { Debug.LogError("[packimport] FAILED: " + ex); }
            finally { AssetDatabase.Refresh(); }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
