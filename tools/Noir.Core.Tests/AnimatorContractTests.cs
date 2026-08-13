using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Noir.Core.People;

namespace Noir.Core.Tests
{
    /// <summary>
    /// THE CONTRACT BETWEEN THE TABLE, THE FILES ON DISK, AND THE CONTROLLER.
    ///
    /// `Content/animations.txt` names a clip. An `.fbx` of that name has to exist. The built
    /// `Townsfolk.controller` has to carry a state of **exactly** that name, because
    /// `AgentAnimation.Drive` finds a state by hashing the string the table gave it.
    ///
    /// Nothing had ever compared those three, and here is what lived in the gap: a clip called
    /// `Standing Idle Looking Ver. 1` reached `AnimatorStateMachine.AddState`, which does not
    /// simply take the name you hand it — `MakeUniqueStateName` sanitised the period, and the
    /// controller got `Standing Idle Looking Ver_ 1`. `Drive` then hashed the DOTTED name, found
    /// no such state, and returned silently **after** it had already written speed = 1. So one
    /// person in six on `travellingto` — the same people every run, the hash is stable —
    /// treadmilled the walk cycle on the spot at every door pause, and not one instrument in this
    /// project could see it.
    ///
    /// It reads only files, so it needs no Unity and runs in the 2-minute Core gate. The price,
    /// decided by the owner rather than assumed: editing `Content/animations.txt` without
    /// rebuilding the controller turns this red, and rebuilding the controller means closing the
    /// editor. That is the cost of the gate catching this class of fault at all.
    /// </summary>
    [TestFixture]
    public class AnimatorContractTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Noir", "Core", "World")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not find Assets/Noir/Core/World above " + AppContext.BaseDirectory);
        }

        private static string Animations() => Path.Combine(RepoRoot(), "Assets", "Noir", "Animations");

        /// <summary>
        /// Every clip name the table asks for — through the real parser, not a copy of it.
        ///
        /// This used to hand-roll the row format here: split the key off, spot an optional
        /// `1.5m/s`, take the rest as a comma list. That is a SECOND parser for a file that
        /// already has one, and a test whose parser disagrees with the game's tests nothing —
        /// it can pass while the game reads the file differently. `AnimationTable` moved to Core
        /// in W3 precisely so this could ask the real thing.
        /// </summary>
        private static HashSet<string> Wanted()
        {
            var table = AnimationTable.Parse(
                File.ReadAllText(Path.Combine(RepoRoot(), "Content", "animations.txt")));

            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            foreach (var clip in row.Value)
                wanted.Add(clip);
            return wanted;
        }

        /// <summary>
        /// The state names in the built controller.
        ///
        /// SCOPED, because the controller's YAML is a flat stream of documents and `m_Name` is not
        /// unique to states. Unscoped, `Townsfolk` (the controller) and `Base Layer` (the layer)
        /// come through as if they were states and the orphan check reports two faults that are
        /// not there. A line that IS `AnimatorState:` arms the flag; the next `m_Name:` while armed
        /// is the state's, and disarms it.
        /// </summary>
        private static HashSet<string> States()
        {
            var states = new HashSet<string>(StringComparer.Ordinal);
            string path = Path.Combine(Animations(), "Townsfolk.controller");
            Assert.That(File.Exists(path), $"no controller at {path} - run Noir/Build The Townsfolk Animator");

            bool armed = false;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line == "AnimatorState:") { armed = true; continue; }
                if (!armed || !line.StartsWith("m_Name:", StringComparison.Ordinal)) continue;

                states.Add(line.Substring("m_Name:".Length).Trim());
                armed = false;
            }
            return states;
        }

        [Test]
        public void EveryClipTheTableAsksForHasAStateOfExactlyThatName()
        {
            var missing = Wanted().Except(States()).OrderBy(s => s, StringComparer.Ordinal).ToList();

            Assert.That(missing, Is.Empty,
                        $"{missing.Count} clip(s) the table asks for have no state of that name, so "
                      + "AgentAnimation.Drive will hash a name the controller does not have and "
                      + "those people will silently not animate:\n  "
                      + string.Join("\n  ", missing)
                      + "\n  Either the clip was renamed without rebuilding the controller, or Unity "
                      + "sanitised the name on the way in - see AnimatorBuild.");
        }

        [Test]
        public void TheControllerCarriesNoStateNobodyAsksFor()
        {
            var orphans = States().Except(Wanted()).OrderBy(s => s, StringComparer.Ordinal).ToList();

            Assert.That(orphans, Is.Empty,
                        $"{orphans.Count} state(s) in the controller that no row in "
                      + "Content/animations.txt asks for. Either the row was deleted without a "
                      + "rebuild, or the name arrived mangled:\n  " + string.Join("\n  ", orphans));
        }

        [Test]
        public void NoAnimationFilenameCarriesAPeriod()
        {
            // A period is the one character known to be sanitised on the way into a state name.
            // This is the cheapest of the three - it reads the directory and nothing else, cannot
            // go stale against the controller, and stays in the gate whatever is decided about
            // the other two.
            var dotted = Directory.GetFiles(Animations(), "*.fbx")
                                  .Select(Path.GetFileNameWithoutExtension)
                                  .Where(stem => stem.Contains("."))
                                  .OrderBy(s => s, StringComparer.Ordinal)
                                  .ToList();

            Assert.That(dotted, Is.Empty,
                        $"{dotted.Count} animation file(s) carry a period in the name. Unity turns "
                      + "it into an underscore when it becomes a state, and Drive then hashes a "
                      + "name that does not exist:\n  " + string.Join("\n  ", dotted));
        }
    }
}
