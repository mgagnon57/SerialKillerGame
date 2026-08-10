using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Noir.Sim;

namespace Noir.Core.Tests
{
    /// <summary>
    /// THE TEXTURE ESTATE, IN THE TWO-MINUTE GATE, WHICH IT HAS NEVER BEEN IN.
    ///
    /// It was stated as a wall: the Core suite has no UnityEngine, so it cannot name a Material or
    /// a Texture. True, and beside the point. WHICH NAMES EXIST, WHICH FILES BACK THEM, and WHICH
    /// NAMES THE TOWN ASKS FOR are pure text and pure file existence, and the precedent is next
    /// door - <see cref="TownPipelineTests"/> reads `Assets/Noir/Unity/*.cs` as text from inside
    /// this same gate, and PlayerBuildTests parses `#if` guards the same way.
    ///
    /// The faults these are aimed at are all silent by construction:
    ///   - `SurfaceTextures.Apply` RETURNS when the file is absent. A typo'd texture name is not
    ///     an error, it is a material that quietly keeps whatever colour it was built with.
    ///   - A material built `Color.white` and then handed to that loader draws PURE WHITE in a
    ///     shipped player, because `ApplyPack` is `#if UNITY_EDITOR` and `BuildPlayer` copies
    ///     Content's top level only - there is no `textures/` directory in a build at all.
    ///   - A PNG in Content/textures/ that nothing generates looks exactly like one that does.
    ///
    /// Every one of those cost this project a real fault: five roof materials that were white in
    /// the product and correct in the editor, four English coverings that outlived the village
    /// they were made for, and a `tiles/` directory of isometric art from before there was a 3D
    /// renderer.
    /// </summary>
    public sealed class SurfaceTextureTests
    {
        /// <summary>
        /// A name asked for that no file and no pack set answers to is a silent flat material, so
        /// the declared list and the files on disk must be the same set in both directions.
        /// </summary>
        [Test]
        public void EveryDeclaredSurfaceHasAFileAndEveryFileIsDeclared()
        {
            string dir = Path.Combine(RepoRoot(), "Content", "textures");
            Assert.That(Directory.Exists(dir), Is.True, dir + " is missing.");

            var onDisk = Directory.GetFiles(dir, "*.png")
                                  .Select(Path.GetFileNameWithoutExtension)
                                  .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var declared = TileGenerator.Surfaces.Select(s => s.Name)
                                  .OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Assert.That(onDisk, Is.EquivalentTo(declared),
                "Content/textures/ and TileGenerator.Surfaces disagree.\n\n" +
                "  on disk : " + string.Join(", ", onDisk) + "\n" +
                "  declared: " + string.Join(", ", declared) + "\n\n" +
                "A file nothing generates is dead weight that reads as live content - that is how " +
                "roof_thatch.png survived the village it was made for. A declared surface with no " +
                "file is a material that draws its fallback colour and says nothing. Fix whichever " +
                "is wrong, then run `dotnet run --project tools/Noir.Sim -- tiles` ONCE.");
        }

        /// <summary>
        /// Every texture name the town asks for is either a loose surface or a pack set. A typo
        /// here does not throw and does not log - it just leaves that material flat.
        /// </summary>
        [Test]
        public void EveryTextureNameTheTownAsksForResolves()
        {
            var asked = AskedNames(out var lines);
            Assert.That(asked, Is.Not.Empty,
                "No texture names were found in Materials3D.cs at all, which means this test has " +
                "stopped reading what it thinks it is reading.");

            var known = new HashSet<string>(TileGenerator.Surfaces.Select(s => s.Name), StringComparer.Ordinal);
            foreach (string key in PackKeys()) known.Add(key);

            var orphans = asked.Where(a => !known.Contains(a)).ToArray();
            Assert.That(orphans, Is.Empty,
                "Materials3D asks for texture name(s) nothing answers to: " +
                string.Join(", ", orphans) + "\n\n" +
                "SurfaceTextures.Apply returns silently when there is no file, so the material " +
                "keeps the colour it was built with and nothing is logged. Every name collected " +
                "from Materials3D.cs, so a false positive here is diagnosable in one read:\n  " +
                string.Join("\n  ", lines));
        }

        /// <summary>
        /// The pack paths are well formed. DELIBERATELY NOT that the files exist:
        /// `Assets/polyperfect` is gitignored, so a fresh clone has none of them and asserting
        /// existence would be a false red on every machine but this one. Whether the pack actually
        /// resolved is a RUNTIME question and SurfaceTextures reports it per name.
        /// </summary>
        [Test]
        public void EveryPackSetPathIsWellFormed()
        {
            string source = File.ReadAllText(Path.Combine(
                RepoRoot(), "Assets", "Noir", "Unity", "SurfaceTextures.cs"));

            var paths = Regex.Matches(source, "\"(Assets/[^\"]*?)\"")
                             .Select(m => m.Groups[1].Value)
                             .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal))
                             .ToArray();

            Assert.That(paths, Is.Not.Empty, "No pack paths found in SurfaceTextures.cs.");

            foreach (string path in paths)
            {
                Assert.That(path, Does.StartWith("Assets/polyperfect/"),
                    path + " is an asset path in SurfaceTextures that does not point at the pack.");

                // A path ending in a slash is a DIRECTORY PREFIX constant - `Roofs`, which the
                // roof entries concatenate onto. Not a texture and not a fault.
                if (path.EndsWith("/", StringComparison.Ordinal)) continue;

                Assert.That(path, Does.EndWith(".png"),
                    path + " is a pack texture path that is not a .png.");
            }
        }

        /// <summary>
        /// NO MATERIAL IS BUILT WHITE AND THEN HANDED TO A LOADER THAT CAN SILENTLY DO NOTHING.
        ///
        /// This is the shape of the fault the owner reported as "weird shit". `Make(name,
        /// Color.white, ...)` followed by a texture bind is correct in the editor and PURE WHITE
        /// in a shipped player: `ApplyPack` does not exist there, `Apply` finds no file because
        /// `BuildPlayer` copies Content's top level only, and neither says a word.
        ///
        /// Two are allowed BY NAME and with the reason, because for them white is the correct
        /// neutral: Person and Furniture are tinted per renderer through a MaterialPropertyBlock.
        /// </summary>
        [Test]
        public void NoMaterialIsBuiltWhiteAndThenTextured()
        {
            string[] tintedPerRenderer = { "Person", "Furniture" };

            string source = Strip(File.ReadAllText(Path.Combine(
                RepoRoot(), "Assets", "Noir", "Unity", "Materials3D.cs")));

            var offenders = new List<string>();
            foreach (Match m in Regex.Matches(source, @"Make\(\s*""(\w+)""\s*,\s*Color\.white"))
            {
                string name = m.Groups[1].Value;
                if (Array.IndexOf(tintedPerRenderer, name) >= 0) continue;
                offenders.Add(name);
            }

            Assert.That(offenders, Is.Empty,
                "Material(s) built white: " + string.Join(", ", offenders) + "\n\n" +
                "White is not a colour here, it is the absence of one. In the editor a texture " +
                "binds over it and it looks right; in a shipped player nothing binds and it draws " +
                "as paper. Five roof materials shipped that way. Give it the measured mean of the " +
                "sheet it stands in for, so a missing file degrades to the right colour instead of " +
                "to a guess - or add it to tintedPerRenderer WITH the reason if a " +
                "MaterialPropertyBlock really does colour it per renderer.");
        }

        // ---- helpers, the same shape TownPipelineTests uses ----

        /// <summary>Texture names asked for on an Apply/ApplyPack/Roofing/Walling line.</summary>
        private static string[] AskedNames(out List<string> quoted)
        {
            string source = Strip(File.ReadAllText(Path.Combine(
                RepoRoot(), "Assets", "Noir", "Unity", "Materials3D.cs")));

            var names = new SortedSet<string>(StringComparer.Ordinal);
            quoted = new List<string>();

            foreach (string raw in source.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.Contains("Apply(") && !line.Contains("ApplyPack(")
                    && !line.Contains("Roofing(") && !line.Contains("Walling(")) continue;

                foreach (Match m in Regex.Matches(line, "\"([a-z][a-z0-9_]*)\""))
                {
                    names.Add(m.Groups[1].Value);
                    quoted.Add(m.Groups[1].Value + "   <- " + line);
                }
            }
            return names.ToArray();
        }

        /// <summary>The keys of SurfaceTextures._packSets.</summary>
        private static IEnumerable<string> PackKeys()
        {
            string source = File.ReadAllText(Path.Combine(
                RepoRoot(), "Assets", "Noir", "Unity", "SurfaceTextures.cs"));
            foreach (Match m in Regex.Matches(source, @"\[""(\w+)""\]\s*="))
                yield return m.Groups[1].Value;
        }

        /// <summary>Comments do not count. A file explaining why it does NOT do something is
        /// behaving correctly - the same reasoning, and the same helper, as TownPipelineTests.</summary>
        private static string Strip(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(source, @"//[^\n]*", " ");
        }

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
