using System;
using System.Collections.Generic;

namespace Noir.Core.People
{
    /// <summary>
    /// Names and particulars, parsed from text. Core never touches the filesystem — the host
    /// (the harness, or Unity) reads the file and hands over the string. That keeps Core
    /// portable and keeps content loading in one obvious place.
    /// </summary>
    public sealed class NameTable
    {
        private readonly List<string> _male = new List<string>();
        private readonly List<string> _female = new List<string>();
        private readonly List<string> _surnames = new List<string>();

        public IReadOnlyList<string> Male => _male;
        public IReadOnlyList<string> Female => _female;
        public IReadOnlyList<string> Surnames => _surnames;

        public static NameTable Parse(string text)
        {
            var table = new NameTable();
            foreach (string rawLine in SplitLines(text))
            {
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                int space = line.IndexOf(' ');
                if (space < 0) continue;

                string kind = line.Substring(0, space).ToLowerInvariant();
                string value = line.Substring(space + 1).Trim();
                if (value.Length == 0) continue;

                switch (kind)
                {
                    case "male": table._male.Add(value); break;
                    case "female": table._female.Add(value); break;
                    case "surname": table._surnames.Add(value); break;
                }
            }

            if (table._male.Count == 0 || table._female.Count == 0 || table._surnames.Count == 0)
                throw new InvalidOperationException("names.txt must supply male, female and surname entries");

            return table;
        }

        internal static string[] SplitLines(string text) =>
            text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        internal static string StripComment(string line)
        {
            int hash = line.IndexOf('#');
            return hash >= 0 ? line.Substring(0, hash) : line;
        }
    }

    /// <summary>
    /// What a particular makes somebody DO, if anything.
    ///
    /// Almost all of them are just true - he does the pools and never checks the results, and
    /// nothing in the simulation could show you that. A few describe a habit that is visible
    /// from across the road, and those carry a tag in Content/particulars.txt so the sentence
    /// you read in the inspector and the thing you can watch him do are the same fact rather
    /// than two facts that happen to agree.
    ///
    /// The 2:1 instrument is what made this necessary. It reported that Citizen.Particulars is
    /// read by two inspectors and by NO line of simulation code: nine hundred clauses of texture
    /// existed and not one was ever enacted, so none of them could reach a watcher.
    ///
    /// NOTE which manners are here and which are not. Texture is keyed on what a watcher saw,
    /// and being ALONE is a tactical observation - so a habit that reliably puts somebody on
    /// their own at the same time every day makes them MORE of a lock, not less. These three
    /// were chosen because none of them answers a predator's question.
    /// </summary>
    [Flags]
    public enum Beat : byte
    {
        None = 0,
        Carries = 1 << 0,      // never goes out empty-handed - a bag, a stick, a dog lead
        Lingers = 1 << 1,      // takes longer over the same journey than anybody else does

        // There was a third, RoundAbout — "does not take the direct way, and never has". It is
        // gone, and the reason is worth keeping. It could only ever have shown up as
        // ObservedManner.SomewhereNew, and the act-by-manner table reads 158 of 158 on both
        // "came out" and "walked past": every villager already earns that manner in the first
        // days of any watch. Texture is a SET of distinct (act, manner) keys, so a manner
        // everybody already has cannot become a new proposition for anybody. It was also the
        // only beat needing a routing change, which would have put the determinism guarantee at
        // risk to buy nothing. Do not reintroduce it without a manner that is actually scarce.
    }

    /// <summary>The useless human details. See Content/particulars.txt for why they matter.</summary>
    public sealed class ParticularsTable
    {
        private readonly List<string> _clauses = new List<string>();
        private readonly List<Beat> _beats = new List<Beat>();

        public IReadOnlyList<string> Clauses => _clauses;
        public int Count => _clauses.Count;

        public string this[int index] => _clauses[index];

        /// <summary>What holding this clause makes somebody do, which is usually nothing.</summary>
        public Beat BeatAt(int index) => _beats[index];

        public static ParticularsTable Parse(string text)
        {
            var table = new ParticularsTable();
            foreach (string rawLine in NameTable.SplitLines(text))
            {
                string line = NameTable.StripComment(rawLine).Trim();
                if (line.Length == 0) continue;
                table._clauses.Add(line);
                table._beats.Add(BeatIn(rawLine));
            }
            if (table._clauses.Count == 0)
                throw new InvalidOperationException("particulars.txt is empty");
            return table;
        }

        /// <summary>
        /// The beat tagged on a line, read out of the part the parser otherwise throws away.
        ///
        /// The tags live after a `#` so that every existing clause keeps working untouched and
        /// so that an untagged line means exactly what it always meant. An unrecognised word in
        /// there is ignored rather than refused: the file already carries `# elder`, `# m` and
        /// `# f` for a scoping system that is not built yet, and a parser that threw on those
        /// would make writing content a matter of remembering what the code knows about.
        /// </summary>
        private static Beat BeatIn(string rawLine)
        {
            int hash = rawLine.IndexOf('#');
            if (hash < 0) return Beat.None;

            string tags = rawLine.Substring(hash + 1);
            var beat = Beat.None;
            if (tags.IndexOf("carries", StringComparison.OrdinalIgnoreCase) >= 0) beat |= Beat.Carries;
            if (tags.IndexOf("lingers", StringComparison.OrdinalIgnoreCase) >= 0) beat |= Beat.Lingers;
            return beat;
        }

        /// <summary>"Ivy Pethick whistles badly, and constantly."</summary>
        public string Sentence(string name, int index) => $"{name} {_clauses[index]}.";
    }
}
