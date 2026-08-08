using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// A line of authored content that does not say what it looks like it says. Named for
    /// village.txt because that is where it started; it now covers kinds.txt too, which shares
    /// this file's syntax for times, days and terrain.
    /// </summary>
    public sealed class VillageParseException : Exception
    {
        /// <summary>
        /// NAMED "the map file", NOT "village.txt". This defaulted to the fixture's name, so a
        /// syntax error in city.txt - which is the file the game actually parses - reported a
        /// line number against a different, much shorter file. An error that names the wrong file
        /// sends whoever reads it to go and look at the wrong one, which is the same fault this
        /// project already fixed once for the map-load warnings.
        /// </summary>
        public VillageParseException(int line, string message)
            : this("the map file", line, message) { }

        public VillageParseException(string file, int line, string message)
            : base($"{file} line {line}: {message}") { }
    }

    /// <summary>
    /// Parser for the authored village format.
    ///
    /// A purpose-built text format rather than JSON, for two reasons: System.Text.Json is not in
    /// Unity's netstandard2.1 profile (so JSON would mean taking a dependency), and this format
    /// is simply nicer to hand-edit - a village is mostly coordinates and one-line prose.
    ///
    ///   village Rossville
    ///   size 120 90
    ///   terrain field 0,0 120x28
    ///   road main 5 4,46 116,46
    ///   place tavern 52,38 9x7 "the Commercial Hotel"
    ///     door 56,44
    ///     hours 11:00-14:30
    ///     hours 17:00-23:00
    ///     jobs 2
    ///     human Eleven men sit in the same order every night.
    ///
    /// Indented lines attach to the place above them. '#' begins a comment.
    /// </summary>
    public static class VillageParser
    {
        public static VillageLayout Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var kinds = PlaceKindTable.Current;
            var layout = new VillageLayout();

            // What an indented line attaches to. Exactly one of these is ever non-null: whichever
            // of `place` or `road` was declared most recently.
            PlaceSpec current = null;
            RoadRun currentRoad = null;

            string[] lines = ContentText.SplitLines(text);

            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                string raw = lines[i];

                int hash = raw.IndexOf('#');
                if (hash >= 0) raw = raw.Substring(0, hash);
                if (raw.Trim().Length == 0) continue;

                bool indented = raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t');
                string line = raw.Trim();

                var tokens = Tokenise(line);
                if (tokens.Count == 0) continue;
                string cmd = tokens[0].ToLowerInvariant();

                if (indented && currentRoad != null)
                {
                    ParseRoadAttribute(currentRoad, cmd, tokens, lineNo);
                    continue;
                }

                if (indented && current != null)
                {
                    ParsePlaceAttribute(current, cmd, tokens, line, lineNo);
                    continue;
                }

                switch (cmd)
                {
                    case "village":
                        layout.Name = line.Substring(cmd.Length).Trim();
                        break;

                    case "size":
                        Require(tokens, 3, lineNo, "size <width> <height>");
                        layout.Width = Int(tokens[1], lineNo);
                        layout.Height = Int(tokens[2], lineNo);
                        break;

                    case "terrain":
                    {
                        Require(tokens, 4, lineNo, "terrain <kind> <x>,<y> <w>x<h>");
                        layout.Terrain.Add(new TerrainPatch
                        {
                            Kind = TerrainKind(tokens[1], lineNo),
                            Area = RectAt(tokens[2], tokens[3], lineNo)
                        });
                        break;
                    }

                    case "road":
                    case "path":
                    {
                        // road <name> <width> <x,y> <x,y> ...
                        Require(tokens, 5, lineNo, "road <name> <width> <x>,<y> <x>,<y> ...");
                        var run = new RoadRun
                        {
                            Kind = cmd == "road" ? Terrain.Road : Terrain.Path,
                            Name = tokens[1],
                            Width = Int(tokens[2], lineNo)
                        };
                        for (int t = 3; t < tokens.Count; t++)
                            run.Points.Add(Point(tokens[t], lineNo));
                        if (run.Points.Count < 2)
                            throw new VillageParseException(lineNo, "a road needs at least two points");
                        layout.Roads.Add(run);

                        // An indented line after this attaches to the road, not to the last place.
                        current = null;
                        currentRoad = run;
                        break;
                    }

                    case "place":
                    {
                        Require(tokens, 5, lineNo, "place <kind> <x>,<y> <w>x<h> \"<name>\"");
                        var kind = PlaceKindOf(kinds, tokens[1], lineNo);
                        current = new PlaceSpec
                        {
                            Kind = kind,
                            Bounds = RectAt(tokens[2], tokens[3], lineNo),
                            Name = tokens[4],

                            // The kind's staffing, until the place says otherwise on a `jobs`
                            // line below. Set here rather than afterwards so that a place
                            // stating its own is a plain overwrite and needs no flag to
                            // remember that it did.
                            JobSlots = kinds.Row(kind).Jobs
                        };
                        layout.Places.Add(current);
                        currentRoad = null;
                        break;
                    }

                    default:
                        throw new VillageParseException(lineNo, $"unknown directive '{tokens[0]}'");
                }
            }

            ApplyKindDefaults(layout, kinds);
            Validate(layout);
            return layout;
        }

        /// <summary>
        /// Fill in what the place did not bother to say, from what its kind is.
        ///
        /// Only hours: staffing is settled at the `place` line, where a later `jobs` line simply
        /// overwrites it. Hours accumulate rather than replace, so the only honest test of
        /// "did the author state these" is that there are none, and it can only be asked once
        /// the whole block has been read.
        /// </summary>
        private static void ApplyKindDefaults(VillageLayout layout, PlaceKindTable kinds)
        {
            foreach (var place in layout.Places)
            {
                if (place.Hours.Count > 0) continue;
                foreach (var window in kinds.Row(place.Kind).Hours) place.Hours.Add(window);
            }
        }

        /// <summary>
        /// An indented line under a `road`. Only one so far:
        ///
        ///   road northgate 30 0,75 239,75
        ///     class freeway
        ///
        /// Saying it rather than inferring it, because a main road and an arterial occupy the
        /// same thirty-metre corridor and differ only in what is painted on them.
        /// </summary>
        private static void ParseRoadAttribute(RoadRun road, string cmd, List<string> tokens,
                                               int lineNo)
        {
            switch (cmd)
            {
                case "class":
                {
                    Require(tokens, 2, lineNo, "class <street|mainroad|freeway>");
                    if (!RoadClasses.TryParse(tokens[1], out var klass))
                        throw new VillageParseException(
                            lineNo, $"unknown road class '{tokens[1]}' - "
                                  + "expected street, mainroad or freeway");

                    int wanted = RoadClasses.CorridorWidth(klass);
                    if (road.Width != wanted)
                        throw new VillageParseException(
                            lineNo, $"a {tokens[1]} is a {wanted}m corridor, but "
                                  + $"'{road.Name}' is declared {road.Width}m wide. The road kit's "
                                  + "tiles are that size, so the width is not a free choice.");

                    road.Class = klass;
                    break;
                }

                case "carries":
                {
                    // WHAT THE ROAD'S JOB IS, which is a different question from what it is
                    // paved to and is the reason this exists at all.
                    //
                    // `class` above pins the corridor to the road kit's tile sizes and throws if
                    // the two disagree. That is right for geometry and wrong for function: Attica
                    // is a county highway with a village street's right of way, so it can never
                    // be declared `class mainroad` without laying asphalt across private lots.
                    // Priority, signals and ambient traffic ask this instead.
                    //
                    // NO WIDTH CHECK HERE, deliberately. The whole point is to say something the
                    // width cannot support.
                    if (tokens.Count < 2 || !RoadClasses.TryParse(tokens[1], out var carries))
                        throw new VillageParseException(
                            lineNo, $"unknown road class '{(tokens.Count > 1 ? tokens[1] : "")}' "
                                  + "after `carries` - expected street, mainroad or freeway");

                    road.Carries = carries;
                    break;
                }

                case "aadt":
                {
                    // WHAT THE COUNTY COUNTED, rather than what the class implies.
                    //
                    // IDOT's count program does village streets by name, and it does eleven of
                    // Rossville's: Route 1 at 5,200 a day, Attica at 1,100, Church Street at 200.
                    // Ambient traffic used to be read off the road CLASS, which is a four-step
                    // ladder - mainroad 8, street 2 - against a measured spread of twenty-one to
                    // one, and which gave Route 1 and Attica the identical weight when the first
                    // carries nearly five times the second.
                    //
                    // Optional, and 0 means NOT COUNTED rather than empty: IDOT does not count
                    // twenty-one of this map's named roads, and a road nobody counted still has
                    // cars on it. Anything reading this falls back to the class.
                    Require(tokens, 2, lineNo, "aadt <vehicles per day>");
                    road.Aadt = (int)ContentText.Number(tokens[1], File, lineNo);
                    if (road.Aadt < 0)
                        throw new VillageParseException(lineNo, "a traffic count cannot be negative");
                    break;
                }

                case "easement":
                {
                    // HOW MUCH PUBLIC LAND THIS ROAD HAS, which is a different thing from how
                    // much of it is paved and is the number nothing here used to carry.
                    //
                    // The right of way is the gap the lots leave - ground nobody owns - and it
                    // is wider than the corridor: a Rossville street sits in a 66 ft easement
                    // and paves a 33 ft corridor down the middle of it, leaving about 16 ft
                    // each side for verge, walk and utilities. Saying it lets anything laying a
                    // sidewalk know where the public land actually ends, instead of guessing
                    // outward from the asphalt and putting a walk through somebody's hedge.
                    //
                    // Optional, and 0 means NOT MEASURED rather than none - out in the country
                    // the fields tile edge to edge and there is no gap to measure.
                    Require(tokens, 2, lineNo, "easement <metres>");
                    road.Easement = ContentText.Number(tokens[1], File, lineNo);
                    if (road.Easement < 0f)
                        throw new VillageParseException(lineNo, "an easement cannot be negative");
                    if (road.Easement > 0f && road.Easement < road.Width)
                        throw new VillageParseException(
                            lineNo, $"'{road.Name}' declares a {road.Easement}m easement but a "
                                  + $"{road.Width}m corridor - the paving would lie on private "
                                  + "land for its whole length.");
                    break;
                }

                default:
                    throw new VillageParseException(lineNo, $"unknown road attribute '{cmd}'");
            }
        }

        private static void ParsePlaceAttribute(PlaceSpec place, string cmd, List<string> tokens,
                                                string line, int lineNo)
        {
            switch (cmd)
            {
                case "door":
                    Require(tokens, 2, lineNo, "door <x>,<y>");
                    place.Door = Point(tokens[1], lineNo);
                    break;

                case "jobs":
                    Require(tokens, 2, lineNo, "jobs <n>");
                    place.JobSlots = Int(tokens[1], lineNo);
                    break;

                case "units":
                    Require(tokens, 2, lineNo, "units <n>");
                    place.Units = Int(tokens[1], lineNo);
                    if (place.Units < 1)
                        throw new VillageParseException(lineNo, "a building needs at least one home in it");
                    break;

                case "human":
                    place.Human = line.Substring(cmd.Length).Trim();
                    break;

                case "key":
                    place.Key = line.Substring(cmd.Length).Trim();
                    if (place.Key.Length == 0)
                        throw new VillageParseException(lineNo, "a key line needs something to hash");
                    break;

                case "hours":
                {
                    Require(tokens, 2, lineNo, "hours <hh:mm>-<hh:mm> [days]");
                    var (start, end) = TimeRange(tokens[1], lineNo);
                    byte days = tokens.Count > 2 ? DaysMask(tokens[2], lineNo) : OpenWindow.AllDays;
                    place.Hours.Add(new OpenWindow(start, end, days));
                    break;
                }

                default:
                    throw new VillageParseException(lineNo, $"unknown place attribute '{tokens[0]}'");
            }
        }

        private static void Validate(VillageLayout layout)
        {
            for (int i = 0; i < layout.Places.Count; i++)
            {
                var p = layout.Places[i];
                if (p.Bounds.W <= 0 || p.Bounds.H <= 0)
                    throw new VillageParseException(0, $"place '{p.Name}' has an empty footprint");
                if (p.Bounds.X < 0 || p.Bounds.Y < 0 ||
                    p.Bounds.Right >= layout.Width || p.Bounds.Bottom >= layout.Height)
                    throw new VillageParseException(0, $"place '{p.Name}' at {p.Bounds} falls outside the {layout.Width}x{layout.Height} map");

                if (p.IsBuilding && !p.Door.IsValid)
                    throw new VillageParseException(0, $"building '{p.Name}' has no door");
            }
        }

        // ---- token helpers ----

        // The syntax below is shared with kinds.txt and lives in ContentText. These stay as
        // named wrappers so that every call site here reads the way it always did.

        /// <summary>What a parse error calls the file it is complaining about. Deliberately not
        /// "village.txt" - see VillageParseException. The parser is handed text, not a path, so
        /// it cannot name the real file; naming the wrong one is worse than naming none.</summary>
        private const string File = "the map file";

        private static List<string> Tokenise(string line) => ContentText.Tokenise(line);

        private static void Require(List<string> tokens, int count, int lineNo, string usage) =>
            ContentText.Require(tokens, count, File, lineNo, usage);

        private static int Int(string s, int lineNo) => ContentText.Int(s, File, lineNo);

        private static Tile Point(string s, int lineNo) => ContentText.Point(s, File, lineNo);

        private static TileRect RectAt(string origin, string size, int lineNo) =>
            ContentText.RectAt(origin, size, File, lineNo);

        private static (int, int) TimeRange(string s, int lineNo) =>
            ContentText.TimeRange(s, File, lineNo);

        private static byte DaysMask(string s, int lineNo) => ContentText.DaysMask(s, File, lineNo);

        private static Terrain TerrainKind(string s, int lineNo) =>
            ContentText.TerrainKind(s, File, lineNo);

        /// <summary>
        /// The word for a kind, resolved through the table rather than through a list here.
        ///
        /// Throwing on a word nothing answers to is the one behaviour of the old switch worth
        /// keeping: a misspelt kind is a building that would otherwise be silently something
        /// else, and this is the last point at which anyone can be told.
        /// </summary>
        private static PlaceKind PlaceKindOf(PlaceKindTable kinds, string s, int lineNo)
        {
            if (kinds.TryKindOf(s, out var kind)) return kind;
            throw new VillageParseException(lineNo, $"unknown place kind '{s}'");
        }
    }
}
