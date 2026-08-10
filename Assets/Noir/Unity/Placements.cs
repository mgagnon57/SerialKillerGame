using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// Where the owner has moved or turned a building, over where the survey measured it.
    ///
    /// WHY AN OVERLAY AND NOT AN EDIT. Content/parcel-buildings.txt is measurement - 822 polygons
    /// off federal imagery, with the query and the scripts that rebuilt them recorded in its own
    /// header. Editing it in place would mean a re-survey silently destroys every adjustment, and
    /// there would be no way left to draw where the measurement actually put the house. So the
    /// measurement stays exactly as it is and this file holds only the differences.
    ///
    /// Absent line means untouched. That is what makes "put it back" a deletion rather than a
    /// value, and it keeps the file to the size of what was actually changed rather than 822 rows
    /// of zeroes.
    ///
    ///     building &lt;parcelId&gt; &lt;index&gt; move &lt;dx&gt; &lt;dy&gt; turn &lt;degrees&gt;
    ///
    /// Metres and degrees, on the same grid as the shapes themselves. Turn is about the footprint's
    /// own centroid, which is the pivot a person expects when they take hold of a corner.
    ///
    /// ORDER MATTERS AND IT IS TURN, THEN MOVE. SeatOnSurvey already squares a footprint back to
    /// its lot before seating it, so what the owner sees and drags is the SQUARED ring - and his
    /// turn has to be the last word on the angle, not something the squaring gets to argue with.
    /// The browser map applies the same squaring before it draws, or the two disagree about where
    /// the house is by a couple of degrees and every adjustment inherits the difference.
    /// </summary>
    public static class Placements
    {
        public const string FileName = "placement-1991.txt";

        /// <summary>One building's adjustment. Zero on every field is the same as no line.</summary>
        public readonly struct Nudge
        {
            public readonly float DX, DY, Turn;
            public Nudge(float dx, float dy, float turn) { DX = dx; DY = dy; Turn = turn; }
            public bool Any => DX != 0f || DY != 0f || Turn != 0f;
        }

        public static readonly Nudge None = new Nudge(0f, 0f, 0f);

        /// <summary>The verbs this file is allowed to contain, for SmokeTest's drift guard.</summary>
        public static readonly string[] KnownVerbs = { "building" };

        private static Dictionary<long, Nudge> _byBuilding;
        private static System.DateTime _stamp;

        /// <summary>Bumped whenever the file is re-read, so anything caching a built shape can
        /// tell that the ground moved underneath it.</summary>
        public static int Revision { get; private set; }

        private static long Key(int parcelId, int index) => ((long)parcelId << 8) | (uint)(index & 0xFF);

        public static Nudge For(int parcelId, int index)
        {
            Load();
            return _byBuilding.TryGetValue(Key(parcelId, index), out var n) ? n : None;
        }

        public static int Count { get { Load(); return _byBuilding.Count; } }

        private static void Load()
        {
            var written = ContentLoader.WrittenAt(FileName);
            if (_byBuilding != null && written == _stamp) return;
            _stamp = written;
            _byBuilding = new Dictionary<long, Nudge>();
            Revision++;

            // NOTHING ADJUSTED YET IS GENUINELY THE USUAL CASE - the file only exists once
            // somebody has moved a house in the browser map - so an ABSENT file stays quiet. It
            // cannot stay quiet on the UNREADABLE one: thirty-seven hand-made adjustments
            // disappearing looks identical to nobody having ever made one.
            string path = System.IO.Path.Combine(ContentLoader.Root, FileName);
            if (!System.IO.File.Exists(path)) return;

            string text;
            try { text = ContentLoader.Read(FileName); }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[placements] {FileName} is there but did not read, "
                    + "so every house the owner moved is back where the generator put it. "
                    + ex.Message);
                return;
            }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(' ');

                // building <parcel> <index> move <dx> <dy> turn <deg>  -  EIGHT tokens. Counted
                // wrong first time and both parsers agreed with each other and with nothing else:
                // the file wrote a line, the server read none back, and it took a round trip
                // rather than a compile to notice.
                if (p.Length == 8 && p[0] == "building" && p[3] == "move" && p[6] == "turn"
                    && int.TryParse(p[1], out int pid) && int.TryParse(p[2], out int idx)
                    && Num(p[4], out float dx) && Num(p[5], out float dy) && Num(p[7], out float t))
                {
                    _byBuilding[Key(pid, idx)] = new Nudge(dx, dy, t);
                }
            }
        }

        private static bool Num(string s, out float v) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out v);

        /// <summary>
        /// A ring with one building's adjustment applied - turned about its own centroid, then
        /// moved. Returns the ring it was given when there is nothing to apply, allocating
        /// nothing: this is called once per building on every world build.
        /// </summary>
        public static Vector2[] Apply(Vector2[] ring, Nudge n)
        {
            if (ring == null || ring.Length < 3 || !n.Any) return ring;

            var outp = new Vector2[ring.Length];

            if (n.Turn != 0f)
            {
                // The ring is closed - the last point repeats the first - so the repeat must not
                // be counted or the centroid drags towards it and the building turns off-centre.
                var c = Vector2.zero;
                int count = ring.Length - 1;
                if (count < 1) return ring;
                for (int i = 0; i < count; i++) c += ring[i];
                c /= count;

                float th = n.Turn * Mathf.Deg2Rad, co = Mathf.Cos(th), si = Mathf.Sin(th);
                for (int i = 0; i < ring.Length; i++)
                {
                    var d = ring[i] - c;
                    outp[i] = new Vector2(c.x + d.x * co - d.y * si + n.DX,
                                          c.y + d.x * si + d.y * co + n.DY);
                }
                return outp;
            }

            for (int i = 0; i < ring.Length; i++)
                outp[i] = new Vector2(ring[i].x + n.DX, ring[i].y + n.DY);
            return outp;
        }
    }
}
