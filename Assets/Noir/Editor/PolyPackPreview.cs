using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Renders a couple of polyperfect's own demo scenes to PNG, headlessly, so the Poly
    /// Universal Pack's actual look can be judged against Ashcombe's muted palette without
    /// anybody opening the editor. These are the publisher's own assembled scenes - no assembly
    /// code of ours involved yet - so this is purely "what does the kit look like," not a test
    /// of any integration.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.PolyPackPreview.Render -logFile <log>
    ///
    /// Note: NO -nographics, same as Snapshot - rendering needs a graphics device.
    /// </summary>
    public static class PolyPackPreview
    {
        private const string PackRoot = "Assets/polyperfect/Poly Universal Pack/_Demo Scenes/";

        private static readonly (string file, string label)[] Scenes =
        {
            ("DEMO_12_PolyFantasyPack.unity", "fantasy-village"),
            ("DEMO_07_Farm.unity",            "farm"),
        };

        private static string OutputDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "snapshots"));

        [MenuItem("Noir/Render Poly Pack Preview")]
        public static void Render()
        {
            int written = 0;
            var originalScene = EditorSceneManager.GetActiveScene().path;

            try
            {
                Directory.CreateDirectory(OutputDir);

                foreach (var (file, label) in Scenes)
                {
                    string path = PackRoot + file;
                    if (!File.Exists(path))
                    {
                        Debug.LogWarning($"[polypack] missing scene: {path}");
                        continue;
                    }

                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                    var cam = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
                    if (cam == null)
                    {
                        Debug.LogWarning($"[polypack] no camera in {file}");
                        continue;
                    }

                    string outPath = Path.Combine(OutputDir, "polypack-" + label + ".png");
                    Capture(cam, outPath);
                    written++;
                    Debug.Log($"[polypack] {file} -> {outPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[polypack] FAILED: " + ex);
            }
            finally
            {
                if (!string.IsNullOrEmpty(originalScene))
                    EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
            }

            Debug.Log($"[polypack] wrote {written} of {Scenes.Length}");
            if (Application.isBatchMode) EditorApplication.Exit(written == Scenes.Length ? 0 : 1);
        }

        private static void Capture(Camera cam, string path)
        {
            const int width = 1600, height = 900;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };

            var previousTarget = cam.targetTexture;
            var previousActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            shot.Apply();

            File.WriteAllBytes(path, shot.EncodeToPNG());

            cam.targetTexture = previousTarget;
            RenderTexture.active = previousActive;

            UnityEngine.Object.DestroyImmediate(shot);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
