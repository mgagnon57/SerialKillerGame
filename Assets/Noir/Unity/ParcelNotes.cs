using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// What a person knows about a real parcel that the county's own data cannot: who lived
    /// there, how many of them, what they were like, and the shape of the house if it's still
    /// clear in memory. Keyed by ParcelIndex's stable per-parcel Id rather than by address - most
    /// of these 794 lots have no address at all, and the whole point is that somebody who grew up
    /// here knows who lived on a lot whether or not it was ever numbered.
    ///
    /// Content/parcel-notes.txt is the one Content file this project writes to at runtime rather
    /// than only reading - hand-editable like every other, but also a save file. Rewritten whole
    /// on every save rather than appended to, so a corrected entry replaces the old one instead of
    /// leaving it behind for the parser to find first.
    /// </summary>
    public static class ParcelNotes
    {
        public sealed class Note
        {
            /// <summary>What the family is like - the seed for behaviour, not just flavour text.
            /// See VillageUI's household editor and Noir.Core.People.ParticularsTable for the
            /// existing "useless human detail" system this is meant to feed the same way.</summary>
            public string Character = "";

            public int Adults;
            public int Kids;

            /// <summary>One name per line, adults then kids, in no particular enforced count -
            /// entering three names for two adults and one kid is the author's business, not a
            /// rule this file polices.</summary>
            public string Names = "";

            public Vector2[] Footprint;    // null if nobody has drawn one
        }

        private static Dictionary<int, Note> _byId;

        public static Note For(int parcelId)
        {
            Load();
            return _byId.TryGetValue(parcelId, out var n) ? n : null;
        }

        public static IReadOnlyDictionary<int, Note> All { get { Load(); return _byId; } }

        /// <summary>Fired after every save, so a renderer showing every authored footprint at
        /// once (see AuthoredFootprints) knows to rebuild rather than polling.</summary>
        public static event System.Action Changed;

        public static void Save(int parcelId, Note note)
        {
            Load();
            bool empty = note == null
                      || (string.IsNullOrWhiteSpace(note.Character)
                       && string.IsNullOrWhiteSpace(note.Names)
                       && note.Adults == 0 && note.Kids == 0
                       && (note.Footprint == null || note.Footprint.Length < 3));

            if (empty) _byId.Remove(parcelId);
            else _byId[parcelId] = note;

            Write();
            Changed?.Invoke();
        }

        /// <summary>Convenience for callers that only ever touch the footprint (FootprintDrawer)
        /// - keeps whatever household data already exists untouched.</summary>
        public static void SaveFootprint(int parcelId, Vector2[] footprint)
        {
            var note = For(parcelId) ?? new Note();
            note.Footprint = footprint;
            Save(parcelId, note);
        }

        private static void Load()
        {
            if (_byId != null) return;
            _byId = new Dictionary<int, Note>();

            string text;
            try { text = ContentLoader.Read("parcel-notes.txt"); }
            catch { return; }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var parts = line.Split(new[] { ' ' }, 3);
                if (parts.Length < 3 || parts[0] != "parcel") continue;
                if (!int.TryParse(parts[1], out int id)) continue;

                var note = _byId.TryGetValue(id, out var existing) ? existing : (_byId[id] = new Note());
                var rest = parts[2];

                if (rest.StartsWith("character "))
                    note.Character = Unquote(rest.Substring(10));
                else if (rest.StartsWith("names "))
                    note.Names = Unquote(rest.Substring(6)).Replace("|", "\n");
                else if (rest.StartsWith("household "))
                {
                    var nums = rest.Substring(10).Split(' ');
                    if (nums.Length >= 2 && int.TryParse(nums[0], out int a) && int.TryParse(nums[1], out int k))
                    {
                        note.Adults = a;
                        note.Kids = k;
                    }
                }
                else if (rest.StartsWith("shape "))
                {
                    var pts = new List<Vector2>();
                    foreach (var piece in rest.Substring(6).Split(' '))
                    {
                        int comma = piece.IndexOf(',');
                        if (comma <= 0) continue;
                        if (float.TryParse(piece.Substring(0, comma), NumberStyles.Float,
                                           CultureInfo.InvariantCulture, out float x)
                         && float.TryParse(piece.Substring(comma + 1), NumberStyles.Float,
                                           CultureInfo.InvariantCulture, out float y))
                            pts.Add(new Vector2(x, y));
                    }
                    if (pts.Count >= 3) note.Footprint = pts.ToArray();
                }
            }
        }

        private static void Write()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ============================================================================");
            sb.AppendLine("#  AUTHORED PARCEL NOTES - who lived here, the household, and the house shape");
            sb.AppendLine("#  if it's known.");
            sb.AppendLine("#");
            sb.AppendLine("#  Keyed by parcel id, which is that parcel's line number in parcels.txt (0-");
            sb.AppendLine("#  based, comments and blanks not counted) - NOT an address, because most of");
            sb.AppendLine("#  these lots never had a house number generated on them at all.");
            sb.AppendLine("#");
            sb.AppendLine("#  Written by the game itself (VillageUI's household editor). Hand-editing is");
            sb.AppendLine("#  fine; the next save from the game rewrites the whole file, so keep a copy");
            sb.AppendLine("#  of any edit you care about outside a Play session.");
            sb.AppendLine("# ============================================================================");
            sb.AppendLine();

            var ids = new List<int>(_byId.Keys);
            ids.Sort();
            foreach (int id in ids)
            {
                var note = _byId[id];
                if (!string.IsNullOrWhiteSpace(note.Character))
                    sb.AppendLine($"parcel {id} character \"{Quote(note.Character)}\"");
                if (note.Adults != 0 || note.Kids != 0)
                    sb.AppendLine($"parcel {id} household {note.Adults} {note.Kids}");
                if (!string.IsNullOrWhiteSpace(note.Names))
                    sb.AppendLine($"parcel {id} names \"{Quote(note.Names.Replace("\n", "|"))}\"");
                if (note.Footprint != null && note.Footprint.Length >= 3)
                {
                    sb.Append($"parcel {id} shape");
                    foreach (var p in note.Footprint)
                        sb.Append(' ').Append(p.x.ToString("0.0", CultureInfo.InvariantCulture))
                          .Append(',').Append(p.y.ToString("0.0", CultureInfo.InvariantCulture));
                    sb.AppendLine();
                }
            }

            File.WriteAllText(Path.Combine(ContentLoader.Root, "parcel-notes.txt"), sb.ToString());
        }

        private static string Quote(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                                                   .Replace("\n", "\\n");

        private static string Unquote(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            return s.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
