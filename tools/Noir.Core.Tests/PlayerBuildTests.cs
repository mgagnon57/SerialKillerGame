using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Assets/Noir/Unity is a RUNTIME assembly: Unity compiles it for a standalone player, where
    /// nothing inside `#if UNITY_EDITOR` exists. A reference from unguarded code to something
    /// declared inside a guard is a compile error that CANNOT HAPPEN IN THE EDITOR, because in the
    /// editor the symbol is right there. So it does not fail when you write it, it does not fail
    /// when you test it, and it does not fail when you press Play. It fails the first time anybody
    /// tries to build the game - and this project has never built the game.
    ///
    /// That is how `SunRig` came to call `CityBuildings.CollectGlazing` and `SetGlazing`, both of
    /// which live inside CityBuildings' own editor-only block, and sit there compiling perfectly
    /// for months while Noir.Unity could not be built at all.
    ///
    /// These are greps for the same reason the other firewalls are: this assembly compiles Core,
    /// and the property being defended is about code it cannot see. A real build is the only
    /// complete proof; this is the cheap check that runs in two minutes on every change.
    /// </summary>
    [TestFixture]
    public class PlayerBuildTests
    {
        private static readonly Regex Directive = new Regex(@"^\s*#\s*(if|elif|else|endif)\b(.*)$");
        private static readonly Regex Member = new Regex(
            @"^\s*public\s+(?:static\s+)?(?:readonly\s+)?[\w<>\[\],\.\?]+\s+(\w+)\s*[\(\{=;]");
        private static readonly Regex ClassDecl = new Regex(
            @"^\s*(?:public|internal)?\s*(?:static\s+|sealed\s+|partial\s+)*class\s+(\w+)");

        /// <summary>
        /// Editor-only TYPES, reached by their bare name.
        ///
        /// `using UnityEditor;` is itself normally inside a guard, so runtime code never writes
        /// the word "UnityEditor" - it writes `AssetDatabase.LoadAssetAtPath` and the name simply
        /// resolves in the editor and vanishes outside it. Both of the CityStreets failures in the
        /// first ever player build looked like this, and the UnityEditor grep below sailed past
        /// them. These are the ones this project actually uses.
        /// </summary>
        private static readonly string[] EditorOnlyTypes =
        {
            "AssetDatabase", "PrefabUtility", "EditorApplication", "EditorUtility",
            "AssetImporter", "ModelImporter", "EditorBuildSettings", "EditorSceneManager",
            "MenuItem", "EditorGUILayout", "EditorGUI", "Handles", "SceneView", "BuildPipeline",
        };

        /// <summary>
        /// The simple invariant: the runtime assembly may not reach UnityEditor outside a guard -
        /// by namespace, or by the bare name of one of its types.
        /// </summary>
        [Test]
        public void RuntimeCodeNeverNamesUnityEditorOutsideAGuard()
        {
            var offenders = new List<string>();

            foreach (string file in RuntimeSources())
            {
                string[] lines = File.ReadAllLines(file);
                bool[] guarded = GuardMap(lines);

                for (int i = 0; i < lines.Length; i++)
                {
                    if (guarded[i]) continue;
                    string s = lines[i].Trim();
                    if (s.StartsWith("//") || s.StartsWith("*")) continue;

                    if (Regex.IsMatch(lines[i], @"\bUnityEditor\b"))
                    { offenders.Add($"{Rel(file)}:{i + 1}  {s}"); continue; }

                    foreach (string type in EditorOnlyTypes)
                        if (Regex.IsMatch(lines[i], $@"(?<![\w.]){Regex.Escape(type)}\s*\."))
                        { offenders.Add($"{Rel(file)}:{i + 1}  uses {type}  |  {s}"); break; }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Runtime code names UnityEditor outside `#if UNITY_EDITOR`:\n  " +
                string.Join("\n  ", offenders) + "\n\n" +
                "Assets/Noir/Unity is compiled into the player, where UnityEditor does not exist. " +
                "Wrap it, or move the code to Assets/Noir/Editor.");
        }

        /// <summary>
        /// The invariant that actually bit: a member DECLARED inside a guard, CALLED outside one.
        /// </summary>
        [Test]
        public void RuntimeCodeNeverCallsAnEditorOnlyMember()
        {
            var editorOnly = new Dictionary<string, HashSet<string>>();
            var sources = new List<string>(RuntimeSources());

            foreach (string file in sources)
            {
                string[] lines = File.ReadAllLines(file);
                bool[] guarded = GuardMap(lines);
                string current = null;

                for (int i = 0; i < lines.Length; i++)
                {
                    var c = ClassDecl.Match(lines[i]);
                    if (c.Success) current = c.Groups[1].Value;
                    if (!guarded[i] || current == null) continue;

                    var m = Member.Match(lines[i]);
                    if (!m.Success) continue;
                    if (!editorOnly.TryGetValue(current, out var set))
                        editorOnly[current] = set = new HashSet<string>();
                    set.Add(m.Groups[1].Value);
                }
            }

            var offenders = new List<string>();
            foreach (string file in sources)
            {
                string[] lines = File.ReadAllLines(file);
                bool[] guarded = GuardMap(lines);
                string current = null;

                for (int i = 0; i < lines.Length; i++)
                {
                    var c = ClassDecl.Match(lines[i]);
                    if (c.Success) current = c.Groups[1].Value;

                    if (guarded[i]) continue;
                    string s = lines[i].Trim();
                    if (s.StartsWith("//") || s.StartsWith("*")) continue;

                    foreach (var kv in editorOnly)
                        foreach (string mem in kv.Value)
                        {
                            // Qualified: SomeClass.Member
                            if (Regex.IsMatch(lines[i], $@"\b{Regex.Escape(kv.Key)}\s*\.\s*{Regex.Escape(mem)}\b"))
                            { offenders.Add($"{Rel(file)}:{i + 1}  calls {kv.Key}.{mem}  |  {s}"); continue; }

                            // BARE, INSIDE ITS OWN CLASS. `_glazing.Clear()` in CityBuildings is
                            // an editor-only field used from unguarded code in the very class that
                            // declares it - no dot-prefix, so the qualified check above walks
                            // straight past. That is exactly what broke the first player build,
                            // and this test as first written did not see it either.
                            if (current == kv.Key &&
                                Regex.IsMatch(lines[i], $@"(?<![\w.]){Regex.Escape(mem)}\b"))
                                offenders.Add($"{Rel(file)}:{i + 1}  uses its own editor-only " +
                                              $"{kv.Key}.{mem}  |  {s}");
                        }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Runtime code calls members that only exist in the editor:\n  " +
                string.Join("\n  ", offenders) + "\n\n" +
                "These compile in the editor and fail the moment anybody builds a player.\n" +
                "Either guard the CALL SITE with `#if UNITY_EDITOR` - and make sure the code that\n" +
                "runs instead is correct rather than merely absent - or move the member out of the\n" +
                "declaring class's editor-only block.");
        }

        /// <summary>For each line, whether it sits inside a UNITY_EDITOR-only region.</summary>
        private static bool[] GuardMap(string[] lines)
        {
            var result = new bool[lines.Length];
            var stack = new List<bool>();

            for (int i = 0; i < lines.Length; i++)
            {
                var d = Directive.Match(lines[i]);
                if (d.Success)
                {
                    string kind = d.Groups[1].Value, rest = d.Groups[2].Value;
                    if (kind == "if")
                        stack.Add(rest.Contains("UNITY_EDITOR") && !rest.Contains("!"));
                    else if (kind == "elif")
                    { if (stack.Count > 0) stack[stack.Count - 1] = false; }
                    else if (kind == "else")
                    { if (stack.Count > 0) stack[stack.Count - 1] = !stack[stack.Count - 1]; }
                    else if (kind == "endif")
                    { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }

                    result[i] = false;
                    continue;
                }

                bool inside = false;
                foreach (bool b in stack) if (b) { inside = true; break; }
                result[i] = inside;
            }
            return result;
        }

        private static IEnumerable<string> RuntimeSources()
        {
            string dir = Path.Combine(RepoRoot(), "Assets", "Noir", "Unity");
            Assert.That(Directory.Exists(dir), Is.True, "Missing " + dir);
            return Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
        }

        private static string Rel(string file) =>
            file.Substring(RepoRoot().Length).TrimStart('\\', '/').Replace('\\', '/');

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Noir", "Core", "Observation")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not find Assets/Noir/Core/Observation above " + AppContext.BaseDirectory);
        }
    }
}
