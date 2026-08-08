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
    }
}
