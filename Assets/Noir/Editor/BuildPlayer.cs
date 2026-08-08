using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Make a player. There was no way to do this, which is why it had never been done.
    ///
    /// WHY THIS MATTERS MORE THAN IT LOOKS. Everything this project knows about itself comes from
    /// the editor: the tests run in the editor, the stills are rendered in the editor, and Play is
    /// pressed in the editor. A build is the one measurement that cannot be argued with, because
    /// it is the only one that compiles the runtime assemblies WITHOUT UNITY_EDITOR defined - and
    /// this codebase leans on that define hard. `SunRig` was calling two methods that live inside
    /// `CityBuildings`' editor-only block and had been for months; it compiled every day, passed
    /// every test, ran fine on Play, and could not have been built. Nothing caught it because
    /// nothing ever tried.
    ///
    /// So the first useful result from this is not a game. It is a list of everything that
    /// silently assumes an editor.
    ///
    ///     Unity.exe -batchmode -quit -projectPath C:\SerialKillerGame ^
    ///       -executeMethod Noir.Editor.BuildPlayer.Windows64 -logFile build.log
    ///
    /// Exits non-zero on failure so a script can tell.
    /// </summary>
    public static class BuildPlayer
    {
        private const string OutputDir = "Build/Windows64";
        private const string Exe = "Rossville.exe";

        [MenuItem("Noir/Build Windows Player")]
        public static void Windows64() => Run(BuildTarget.StandaloneWindows64);

        private static void Run(BuildTarget target)
        {
            int code = 1;
            try
            {
                var scenes = EditorBuildSettings.scenes
                    .Where(s => s.enabled)
                    .Select(s => s.path)
                    .ToArray();

                if (scenes.Length == 0)
                {
                    Debug.LogError("[build] no scenes are enabled in Build Settings - a player "
                                 + "with no scene is not a game. Add the one the game runs from.");
                }
                else
                {
                    foreach (string s in scenes) Debug.Log("[build] scene: " + s);

                    string root = Path.GetDirectoryName(Application.dataPath);
                    string dir = Path.Combine(root ?? ".", OutputDir);
                    Directory.CreateDirectory(dir);

                    var options = new BuildPlayerOptions
                    {
                        scenes = scenes,
                        locationPathName = Path.Combine(dir, Exe),
                        target = target,
                        options = BuildOptions.None,
                    };

                    EnsureShadersSurvive();

                    Debug.Log($"[build] building {target} into {dir} ...");
                    BuildReport report = BuildPipeline.BuildPlayer(options);
                    var summary = report.summary;

                    Debug.Log($"[build] result={summary.result} errors={summary.totalErrors} "
                            + $"warnings={summary.totalWarnings} "
                            + $"size={summary.totalSize / (1024 * 1024)}MB "
                            + $"took={summary.totalTime}");

                    // EVERY ERROR, NAMED. A build report's step messages are where the
                    // editor-only assumptions actually surface, and the default log buries them
                    // among thousands of lines of asset imports.
                    foreach (var step in report.steps)
                        foreach (var msg in step.messages)
                            if (msg.type == LogType.Error || msg.type == LogType.Exception)
                                Debug.LogError($"[build] {step.name}: {msg.content}");

                    if (summary.result == BuildResult.Succeeded)
                    {
                        ShipTheContent(root, dir);
                        Debug.Log("[build] SUCCEEDED: " + options.locationPathName);
                        code = 0;
                    }
                    else
                    {
                        Debug.LogError("[build] FAILED: " + summary.result);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[build] THREW: " + ex);
            }

            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>
        /// Put Content/ beside the exe, because that is exactly where the game looks for it.
        ///
        /// ContentLoader.Root is `Application.dataPath/../Content`. In the editor dataPath is
        /// &lt;project&gt;/Assets so that resolves to the repo's Content/; in a player it is
        /// &lt;exe dir&gt;/Rossville_Data, so it resolves to &lt;exe dir&gt;/Content - which nothing had
        /// ever put there. The first build ran and said so in one line: "Content not found at
        /// ...\Build\Windows64\Content". The path convention was already right; the data was
        /// simply never shipped.
        ///
        /// NOT EVERYTHING IN Content/ MAY SHIP. Three things there are local-only and gitignored,
        /// and they are gitignored because they carry the real names of people who live in a real
        /// town. Copying the folder wholesale would put them in an artifact that can be handed to
        /// somebody - which is the whole thing the no-names rule exists to prevent, and the review
        /// already caught the test project doing exactly this.
        ///
        /// THE RULE IS THE REPOSITORY'S OWN: gitignored means local-only. If you add a file here
        /// that must not leave this machine, gitignore it AND add it below.
        /// </summary>
        private static readonly string[] NeverShip =
        {
            "parcel-notes.txt",       // the author's own notes, keyed by lot
            "parcel-notes.txt.bak",
            "private",                // whatever is in here is in here for a reason
        };

        /// <summary>
        /// Every shader the runtime asks for BY NAME, forced into the build.
        ///
        /// `Shader.Find` is a lookup at runtime, so the build has no way to know the shader is
        /// wanted - nothing references it, so it is stripped, so Find returns null, so
        /// `new Material(null)` throws. That is not a hypothetical: the first player build that
        /// got as far as running died on it with "ArgumentNullException: Parameter name: shader"
        /// out of Materials3D.Make, AFTER the whole town had been assembled correctly. 66 roads,
        /// 175 buildings seated, the 1913 terrace laid - and then no ground, because the ground
        /// material had no shader.
        ///
        /// There are 38 Shader.Find calls in this project across the seven names below. The
        /// built-in ones happen to be in Always Included already; the URP ones and Noir's own line
        /// shader were not, which is exactly the set that broke.
        /// </summary>
        private static readonly string[] FoundByName =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
            "Sprites/Default",
            "Standard",
            "Skybox/Procedural",
            "Noir/ScreenSpaceLine",
        };

        private static void EnsureShadersSurvive()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settings == null || settings.Length == 0)
            {
                Debug.LogWarning("[build] could not open GraphicsSettings - shaders found by name "
                               + "may be stripped and the player will fail on a null shader.");
                return;
            }

            var so = new SerializedObject(settings[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) { Debug.LogWarning("[build] no m_AlwaysIncludedShaders property"); return; }

            var already = new System.Collections.Generic.HashSet<Shader>();
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is Shader s) already.Add(s);

            int added = 0;
            foreach (string name in FoundByName)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning($"[build] Shader.Find(\"{name}\") is null IN THE EDITOR - the "
                                   + "name is wrong or the package is missing, and every runtime "
                                   + "call to it will return null in a player too.");
                    continue;
                }
                if (already.Contains(shader)) continue;

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                already.Add(shader);
                added++;
                Debug.Log($"[build] always-include: {name}");
            }

            if (added > 0)
            {
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[build] {added} shader(s) added to Always Included, "
                    + $"{FoundByName.Length} checked.");
        }

        private static void ShipTheContent(string root, string buildDir)
        {
            string from = Path.Combine(root, "Content");
            string to = Path.Combine(buildDir, "Content");

            if (!Directory.Exists(from))
            {
                Debug.LogError("[build] no Content/ at " + from + " - the player will boot to "
                             + "\"Content not found\" and show its load error.");
                return;
            }

            Directory.CreateDirectory(to);
            int copied = 0, held = 0;

            foreach (string path in Directory.GetFiles(from))
            {
                string name = Path.GetFileName(path);
                if (Array.Exists(NeverShip, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                { held++; continue; }

                // The .before-* backups of the rulings file are working copies, not content.
                if (name.Contains(".before-")) { held++; continue; }

                File.Copy(path, Path.Combine(to, name), overwrite: true);
                copied++;
            }

            Debug.Log($"[build] shipped {copied} content files beside the exe, held back {held} "
                    + "as local-only.");
        }
    }
}
