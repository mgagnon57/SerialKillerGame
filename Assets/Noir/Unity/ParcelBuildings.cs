using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// The real building footprints, seated on the real lots. Read from
    /// Content/parcel-buildings.txt, which is DERIVED and read-only - the sibling of
    /// CountyRecord/parcel-county.txt, and deliberately not mixed into ParcelNotes, which is the
    /// author's file and is rewritten whole by the game.
    ///
    /// WHAT THESE ARE. 822 outlines from FEMA / Oak Ridge National Laboratory's USA Structures
    /// layer, registered to Vermilion County's parcels and carried into village metres. Every
    /// one of them lands inside the lot it was assigned to, and 96% carry the same street
    /// address the county assessor records for that lot. See docs/research/BUILDING-FOOTPRINTS.md.
    ///
    /// WHAT THEY ARE NOT. Not authored, not heights, not room counts, and not checked by a
    /// person - every record is machine-traced from 2016 imagery. Two consequences that matter
    /// at the point of use:
    ///
    ///   THE ANGLE IS LOOSE. Half of these outlines sit more than 5 degrees off the town grid
    ///   while 94% of the county's lot lines are within 5 degrees of it, so the crookedness is
    ///   the tracing rather than the seating. <see cref="Entry.Skew"/> is how far off square to
    ///   its own lot each one sits; <see cref="Entry.Squared"/> hands back the ring turned back.
    ///
    ///   THE DOWNTOWN IS THE WRONG DECADE. The commercial row at Attica x Chicago burned in
    ///   February 2004 and this imagery is later, so the shops here are the town AFTER the fire
    ///   and wrong for a build year around 2000. The residential streets are the part to trust.
    /// </summary>
    public static class ParcelBuildings
    {
        public const string FileName = "parcel-buildings.txt";

        public enum Role { Primary, Outbuilding }

        public sealed class Entry
        {
            /// <summary>Which parcel this stands on - its line number in parcels.txt.</summary>
            public int ParcelId;

            /// <summary>0 is the largest structure on the lot, then descending.</summary>
            public int Index;

            /// <summary>
            /// The biggest thing on a lot is called the building and the rest are behind it.
            /// DERIVED: FEMA ships an outbuilding flag and it is empty on every record here.
            /// Checked against "closest to the street it is addressed on", the two rules agree
            /// on 74% of the lots carrying more than one structure.
            /// </summary>
            public Role Which;

            /// <summary>Ground area in square metres, excluding nothing - unlike the 1913
            /// Sanborn footprints these include the porch, the attached garage and every later
            /// addition, which is most of why they run larger.</summary>
            public float Area;

            /// <summary>FEMA's occupancy class, mapped onto this project's zoning words.</summary>
            public ParcelNotes.Zoning Zoning;

            /// <summary>Situs address as the federal layer spells it, or empty.</summary>
            public string Address = "";

            /// <summary>
            /// Degrees off square to its own lot, signed. RECORDED, NOT CORRECTED - for some
            /// buildings it is real, since corner lots and the ones along the railroad's
            /// diagonal genuinely do sit askew.
            /// </summary>
            public float Skew;

            /// <summary>The footprint ring, closed, in village metres - the same frame as
            /// parcels.txt, Place.Bounds and ParcelNotes.Footprint.</summary>
            public Vector2[] Shape;

            /// <summary>
            /// The ring turned back square to its lot, about its own centroid. Returns the ring
            /// unchanged past <paramref name="limit"/> degrees, where the building is more
            /// likely to be genuinely askew than badly traced.
            /// </summary>
            public Vector2[] Squared(float limit = 20f)
            {
                if (Shape == null || Shape.Length < 3) return Shape;
                float s = Skew;
                if (Mathf.Abs(s) < 0.05f || Mathf.Abs(s) > limit) return Shape;

                var c = Vector2.zero;
                int n = Shape.Length - 1;          // the ring is closed; do not count the repeat
                if (n < 1) return Shape;
                for (int i = 0; i < n; i++) c += Shape[i];
                c /= n;

                float th = -s * Mathf.Deg2Rad, co = Mathf.Cos(th), si = Mathf.Sin(th);
                var outp = new Vector2[Shape.Length];
                for (int i = 0; i < Shape.Length; i++)
                {
                    var d = Shape[i] - c;
                    outp[i] = new Vector2(c.x + d.x * co - d.y * si, c.y + d.x * si + d.y * co);
                }
                return outp;
            }
        }

        private static Dictionary<int, List<Entry>> _byParcel;
        private static System.DateTime _stamp;

        /// <summary>Everything on one lot, largest first. Empty if nothing was found there.</summary>
        public static IReadOnlyList<Entry> For(int parcelId)
        {
            Load();
            return _byParcel.TryGetValue(parcelId, out var list) ? (IReadOnlyList<Entry>)list
                                                                : System.Array.Empty<Entry>();
        }

        /// <summary>The house on this lot - the largest structure - or null.</summary>
        public static Entry PrimaryOf(int parcelId)
        {
            var all = For(parcelId);
            return all.Count > 0 ? all[0] : null;
        }

        public static IReadOnlyDictionary<int, List<Entry>> All { get { Load(); return _byParcel; } }

        public static int Count
        {
            get
            {
                Load();
                int n = 0;
                foreach (var kv in _byParcel) n += kv.Value.Count;
                return n;
            }
        }

        private static void Load()
        {
            // Reparse when the file moves under us: it is regenerated by tools/seat-buildings.py
            // while the editor is open, and a static cache outlives a domain reload.
            var written = ContentLoader.WrittenAt(FileName);
            if (_byParcel != null && written == _stamp) return;
            _stamp = written;
            _byParcel = new Dictionary<int, List<Entry>>();

            string text;
            try { text = ContentLoader.Read(FileName); }
            catch { return; }

            var pending = new Dictionary<(int, int), Entry>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var parts = line.Split(new[] { ' ' }, 4);
                if (parts.Length < 4 || parts[0] != "parcel") continue;
                if (!int.TryParse(parts[1], out int pid)) continue;
                string kind = parts[2];
                var rest = parts[3];

                if (kind == "building")
                {
                    // <n> <role> <area> <zone> skew <deg> "<address>"
                    var t = rest.Split(new[] { ' ' }, 7);
                    if (t.Length < 6) continue;
                    if (!int.TryParse(t[0], out int idx)) continue;

                    var e = new Entry
                    {
                        ParcelId = pid,
                        Index = idx,
                        Which = t[1] == "outbuilding" ? Role.Outbuilding : Role.Primary,
                        Area = Num(t[2]),
                        Zoning = ZoningOf(t[3]),
                        Skew = t[4] == "skew" ? Num(t[5]) : 0f,
                        Address = t.Length >= 7 ? Unquote(t[6]) : "",
                    };
                    pending[(pid, idx)] = e;
                    if (!_byParcel.TryGetValue(pid, out var list))
                        _byParcel[pid] = list = new List<Entry>();
                    list.Add(e);
                }
                else if (kind == "shape")
                {
                    int sp = rest.IndexOf(' ');
                    if (sp <= 0) continue;
                    if (!int.TryParse(rest.Substring(0, sp), out int idx)) continue;
                    if (!pending.TryGetValue((pid, idx), out var e)) continue;

                    var toks = rest.Substring(sp + 1).Split(' ');
                    var pts = new List<Vector2>(toks.Length);
                    foreach (var tok in toks)
                    {
                        if (tok.Length == 0) continue;
                        int comma = tok.IndexOf(',');
                        if (comma <= 0) continue;
                        pts.Add(new Vector2(Num(tok.Substring(0, comma)), Num(tok.Substring(comma + 1))));
                    }
                    if (pts.Count >= 3) e.Shape = pts.ToArray();
                }
            }

            foreach (var kv in _byParcel) kv.Value.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        private static float Num(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        private static string Unquote(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            return s;
        }

        private static ParcelNotes.Zoning ZoningOf(string word)
        {
            switch (word)
            {
                case "residential":  return ParcelNotes.Zoning.Residential;
                case "commercial":   return ParcelNotes.Zoning.Commercial;
                case "industrial":   return ParcelNotes.Zoning.Industrial;
                case "civic":        return ParcelNotes.Zoning.Civic;
                case "agricultural": return ParcelNotes.Zoning.Agricultural;
                default:             return ParcelNotes.Zoning.Unset;
            }
        }
    }
}
