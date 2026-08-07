using System.Collections.Generic;

namespace Noir.Unity
{
    /// <summary>
    /// Where the owner says the sidewalks were. Read from Content/roads-1991.txt, with the blocks
    /// they are keyed to read from Content/road-blocks.txt.
    ///
    /// THE BROWSER MAP IS THE SOURCE. It is the owner's memory of where everything was in 1991,
    /// and nothing measured can contradict it: the tax roll starts in 2007, the imagery is 2016,
    /// and neither records a walk at all. This is the sibling of <see cref="Rulings"/> and is read
    /// the same way - authored, never written by the game, and the last word.
    ///
    /// A WALK IS A PROPERTY OF A BLOCK. Chicago Street had walks both sides through the middle of
    /// town and nothing out at the edges, so a street-wide answer cannot say what the town looked
    /// like. Two scopes, and the block wins: `road <name> walk <side>` is the street's default and
    /// `block <road> <from> <to> walk <side>` overrides it for that block.
    ///
    /// BLOCKS ARE BOUNDED BY WHATEVER CROSSES, INCLUDING ALLEYS. `block attica chicago alley2` is
    /// a real block downtown. That is why this reads road-blocks.txt for the metre offsets rather
    /// than trying to work out where a block starts from the names: the names are how a person
    /// says it, and the offsets are how the ground answers.
    /// </summary>
    public static class RoadRulings
    {
        public const string FileName = "roads-1991.txt";
        public const string BlocksFileName = "road-blocks.txt";

        /// <summary>Which side of the road a walk was laid on. <see cref="Unruled"/> means nobody
        /// has said, which is not the same as saying there was none.</summary>
        public enum Walk { Unruled, None, Both, North, South, East, West }

        /// <summary>One run of street between two crossings, in metres along the road.</summary>
        public readonly struct Block
        {
            public readonly string Road, From, To;
            public readonly float Start, End;

            public Block(string road, string from, string to, float start, float end)
            {
                Road = road; From = from; To = to; Start = start; End = end;
            }
        }

        /// <summary>The verbs this understands. The smoke test reads it, so a ruling the browser
        /// map learns to write and this never learns to read is caught the day it lands rather
        /// than the day somebody notices the game ignoring it.</summary>
        public static readonly string[] KnownVerbs = { "road", "block" };

        private static Dictionary<string, Walk> _byRoad;
        private static Dictionary<string, Walk> _byBlock;      // "road|from|to"
        private static List<Block> _blocks;
        private static System.DateTime _stamp, _blockStamp;

        /// <summary>Every block of every road, in file order.</summary>
        public static IReadOnlyList<Block> Blocks { get { LoadBlocks(); return _blocks; } }

        /// <summary>How many roads and blocks carry a ruling.</summary>
        public static int Count { get { Load(); return _byRoad.Count + _byBlock.Count; } }

        /// <summary>The block of this road containing a point that far along it, or null.</summary>
        public static Block? BlockAt(string road, float metresAlong)
        {
            LoadBlocks();
            foreach (var b in _blocks)
                if (b.Road == road && metresAlong >= b.Start && metresAlong <= b.End)
                    return b;
            return null;
        }

        /// <summary>
        /// What was laid on this stretch of this road: the block's own ruling if it has one, else
        /// the street's default, else Unruled.
        /// </summary>
        public static Walk WalkAt(string road, float metresAlong)
        {
            Load();
            var block = BlockAt(road, metresAlong);
            if (block != null)
            {
                var key = road + "|" + block.Value.From + "|" + block.Value.To;
                if (_byBlock.TryGetValue(key, out var own)) return own;
            }
            return _byRoad.TryGetValue(road, out var street) ? street : Walk.Unruled;
        }

        /// <summary>The street's default on its own, ignoring any block that overrides it.</summary>
        public static Walk WalkOn(string road)
        {
            Load();
            return _byRoad.TryGetValue(road, out var w) ? w : Walk.Unruled;
        }

        private static void Load()
        {
            var written = ContentLoader.WrittenAt(FileName);
            if (_byRoad != null && written == _stamp) return;
            _stamp = written;
            _byRoad = new Dictionary<string, Walk>();
            _byBlock = new Dictionary<string, Walk>();

            string text;
            try { text = ContentLoader.Read(FileName); }
            catch { return; }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(' ');

                if (p.Length == 4 && p[0] == "road" && p[2] == "walk")
                {
                    var w = WalkOf(p[3]);
                    if (w != Walk.Unruled) _byRoad[p[1]] = w;
                }
                else if (p.Length == 6 && p[0] == "block" && p[4] == "walk")
                {
                    var w = WalkOf(p[5]);
                    if (w != Walk.Unruled) _byBlock[$"{p[1]}|{p[2]}|{p[3]}"] = w;
                }
            }
        }

        private static void LoadBlocks()
        {
            var written = ContentLoader.WrittenAt(BlocksFileName);
            if (_blocks != null && written == _blockStamp) return;
            _blockStamp = written;
            _blocks = new List<Block>();

            string text;
            try { text = ContentLoader.Read(BlocksFileName); }
            catch { return; }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(' ');
                if (p.Length != 6 || p[0] != "block") continue;
                if (!float.TryParse(p[4], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float a))
                    continue;
                if (!float.TryParse(p[5], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float b))
                    continue;
                _blocks.Add(new Block(p[1], p[2], p[3], a, b));
            }
        }

        private static Walk WalkOf(string word)
        {
            switch (word)
            {
                case "none":  return Walk.None;
                case "both":  return Walk.Both;
                case "north": return Walk.North;
                case "south": return Walk.South;
                case "east":  return Walk.East;
                case "west":  return Walk.West;
                default:      return Walk.Unruled;
            }
        }
    }
}
