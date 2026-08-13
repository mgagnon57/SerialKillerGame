using System.Collections.Generic;
using System.Globalization;

namespace Noir.Core.People
{
    /// <summary>
    /// `Content/animations.txt`, parsed. What each situation may be played with, and how fast the
    /// locomotive rows were animated.
    ///
    /// IN CORE BECAUSE IT IS A PARSER, AND PARSERS ARE WHERE THE SILENT FAULTS LIVE. This lived
    /// inside `AgentAnimation` in the Unity assembly, which means it was reachable only from
    /// PlayMode - a six-to-fifteen-minute round trip - so in practice nothing tested it at all.
    /// The dotted-clip bug sat in the gap between this table and the built controller for weeks.
    /// Two minutes of `dotnet test` proves the whole of it now.
    ///
    /// NOT CALLED `Animation`. `UnityEngine.Animation` exists, and Core does not use a name the
    /// engine also uses - see CLAUDE.md's list. A Core type that shadows an engine type compiles
    /// fine here and produces baffling errors in the assembly that has both.
    ///
    /// PURE. It takes text and returns a table; it does not read a file, and it does not log. A
    /// caller with a logger hands <see cref="Warnings"/> to it. That is what lets the tests feed
    /// it a string, and it is why nothing in here needs a content source installed.
    /// </summary>
    public sealed class AnimationTable
    {
        /// <summary>What a person does when nothing else has been asked for.</summary>
        public const string Default = "default";

        private readonly Dictionary<string, string[]> _rows =
            new Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, float> _paces =
            new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _warnings = new List<string>();

        /// <summary>Situation to the clips that suit it. Case-insensitive, as the file is.</summary>
        public IReadOnlyDictionary<string, string[]> Rows => _rows;

        /// <summary>
        /// Situation to the metres a second its clips were ANIMATED at, or 0 where unstated.
        ///
        /// The number is a fact about the clip, not about the town. It is what lets playback be
        /// matched to the ground a person actually covers, which is the whole of the difference
        /// between a walk and a glide.
        /// </summary>
        public IReadOnlyDictionary<string, float> Paces => _paces;

        /// <summary>
        /// Everything odd about the file, in the order it was met.
        ///
        /// Returned rather than logged because Core has no logger and should not acquire one. The
        /// host prints these; the tests assert on them, which is the half that never happened when
        /// this was a `Debug.LogWarning` buried in a property getter.
        /// </summary>
        public IReadOnlyList<string> Warnings => _warnings;

        private AnimationTable() { }

        /// <summary>
        /// Parse the table. A null or empty file is a survivable state, not an error: it leaves
        /// every situation empty, every lookup null and every figure standing still, which is
        /// exactly what the town looked like before any of this existed.
        /// </summary>
        public static AnimationTable Parse(string text)
        {
            var table = new AnimationTable();

            if (string.IsNullOrEmpty(text))
            {
                table._warnings.Add("no animations.txt, or it is empty - nobody will animate.");
                return table;
            }

            int lineNo = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNo++;
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                // `situation  clip, another clip` - the situation is the first word, the rest of
                // the line is a comma-separated list, and a row may name nothing at all.
                int gap = line.IndexOfAny(new[] { ' ', '\t' });
                string what = gap < 0 ? line : line.Substring(0, gap);
                string rest = gap < 0 ? "" : line.Substring(gap).Trim();

                if (table._rows.ContainsKey(what))
                    table._warnings.Add($"line {lineNo}: '{what}' is declared twice - the later "
                                      + "row wins and the earlier one is silently discarded.");

                // An optional `1.5m/s` before the clips: the speed the clip was ANIMATED at.
                // Written with the unit on it rather than as a bare number on purpose - Mixamo
                // ships clips called "180 Turn", and a bare leading number would silently eat one.
                table._paces[what] = 0f;
                int unit = rest.IndexOf("m/s", System.StringComparison.OrdinalIgnoreCase);
                if (unit > 0 && float.TryParse(rest.Substring(0, unit).Trim(),
                                               NumberStyles.Float, CultureInfo.InvariantCulture,
                                               out float pace) && pace > 0f)
                {
                    table._paces[what] = pace;
                    rest = rest.Substring(unit + 3).Trim();
                }

                var clips = new List<string>();
                foreach (var piece in rest.Split(','))
                {
                    var clip = piece.Trim();
                    if (clip.Length == 0) continue;

                    // A PERIOD IS A FAULT, NOT A CURIOSITY. Unity's AddState sanitises it, so a
                    // clip named `Standing Idle Looking Ver. 1` becomes a state named
                    // `...Ver_ 1`, the hash of the dotted name matches nothing, and the person
                    // silently does not animate. It cost one villager in six a treadmilling walk
                    // cycle for weeks. See AnimatorContractTests.
                    if (clip.Contains("."))
                        table._warnings.Add($"line {lineNo}: clip '{clip}' has a period in it. "
                                          + "Unity will rename the state and nobody will play it.");

                    clips.Add(clip);
                }

                table._rows[what] = clips.ToArray();
            }

            if (!table._rows.ContainsKey(Default))
                table._warnings.Add($"no '{Default}' row - a situation with no row of its own has "
                                  + "nothing at all to fall back to.");

            return table;
        }

        /// <summary>The clips for a situation, or null. Falls back to nothing, not to default.</summary>
        public string[] ClipsFor(string situation) =>
            situation != null && _rows.TryGetValue(situation, out var clips) ? clips : null;

        /// <summary>Metres a second the situation's clips were animated at, or 0.</summary>
        public float PaceFor(string situation) =>
            situation != null && _paces.TryGetValue(situation, out float pace) ? pace : 0f;
    }
}
