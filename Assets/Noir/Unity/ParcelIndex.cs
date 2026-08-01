using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// The real county lot lines, parsed once and kept for lookup rather than only for drawing.
    ///
    /// Content/parcels.txt is village-space metres - the same frame Place.Bounds and Place.Door
    /// already use - so a place can be matched to the parcel under its own roof without going
    /// anywhere near Unity's 3D space or the camera at all. CityOutlines draws these; this is
    /// the other half, the one that answers "how big is this lot really" when somebody clicks it.
    /// </summary>
    public static class ParcelIndex
    {
        public readonly struct Parcel
        {
            /// <summary>Its line number in Content/parcels.txt (0-based, comments and blank lines
            /// not counted). Stable across loads as long as the file itself keeps its order -
            /// nothing in this project resorts it - which is what lets ParcelNotes key authored
            /// text and hand-drawn footprints to a parcel without the parcel needing a name.</summary>
            public readonly int Id;

            /// <summary>The ring, for anyone who needs the real shape rather than its box.</summary>
            public readonly Vector2[] Points;

            /// <summary>Axis-aligned - honest because the parcels are, to within four hundredths
            /// of a degree, since the plan's own rotation fix. See Content/parcels.txt.</summary>
            public readonly Rect Bounds;

            public Parcel(int id, Vector2[] points, Rect bounds)
            {
                Id = id; Points = points; Bounds = bounds;
            }
        }

        private static List<Parcel> _all;

        /// <summary>Every parcel, parsed once. Empty rather than null if the content is missing.</summary>
        public static IReadOnlyList<Parcel> All { get { Load(); return _all; } }

        public static Parcel? ById(int id)
        {
            Load();
            return id >= 0 && id < _all.Count ? _all[id] : (Parcel?)null;
        }

        /// <summary>
        /// The real parcel under a place's own centre - the "which real lot is this generated
        /// footprint standing on" question, asked identically by VillageUI's lot-size and
        /// household lookups and by SelectionHighlight's outline. All three used to compute the
        /// centre inline; one drifting out of step with the others was only a matter of time.
        /// </summary>
        public static Parcel? FindFor(Place place)
        {
            var b = place.Bounds;
            return Find(new Vector2(b.X + b.W / 2f, b.Y + b.H / 2f));
        }

        /// <summary>
        /// The parcel whose ring actually contains a point, not merely whose box does - two lots
        /// can share a bounding box near an alley cut, and the box alone would pick either.
        /// </summary>
        public static Parcel? Find(Vector2 at)
        {
            Load();
            foreach (var p in _all)
            {
                if (!p.Bounds.Contains(at)) continue;
                if (Inside(p.Points, at)) return p;
            }
            return null;
        }

        private static void Load()
        {
            if (_all != null) return;
            _all = new List<Parcel>();

            string text;
            try { text = ContentLoader.Read("parcels.txt"); }
            catch { return; }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var pts = new List<Vector2>();
                foreach (var piece in line.Split(' '))
                {
                    int comma = piece.IndexOf(',');
                    if (comma <= 0) continue;
                    if (float.TryParse(piece.Substring(0, comma), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out float x)
                     && float.TryParse(piece.Substring(comma + 1), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out float y))
                        pts.Add(new Vector2(x, y));
                }
                if (pts.Count < 3) continue;

                float minX = pts[0].x, maxX = pts[0].x, minY = pts[0].y, maxY = pts[0].y;
                foreach (var p in pts)
                {
                    if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                }
                _all.Add(new Parcel(_all.Count, pts.ToArray(), Rect.MinMaxRect(minX, minY, maxX, maxY)));
            }
        }

        /// <summary>Standard ray-casting point-in-polygon test - even-odd crossings of a
        /// horizontal ray through the point.</summary>
        private static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) &&
                    p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
