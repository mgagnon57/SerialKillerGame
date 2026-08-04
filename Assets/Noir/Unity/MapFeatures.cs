using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// Content/features.txt - the things that are neither a road nor a lot - parsed once, in one
    /// place.
    ///
    /// The file holds real OpenStreetMap geometry already converted to map metres about the
    /// Chicago x Attica crossing: the CSX Woodland Subdivision, the North Fork Vermilion, the
    /// school campuses, the ponds, and the four surveyed level crossings. The coordinates in it
    /// ARE map coordinates - there is no transform to apply and, more to the point, none to get
    /// wrong twice.
    ///
    /// WHY IT IS ITS OWN CLASS. CityOutlines read this file inline and was the only thing that
    /// did, which was fine while the plan was the only consumer. It is not any more: the rail bed
    /// draws the same `rail` polyline on the ground, and tools/relay-rossville.py rasterises the
    /// same `water` and `river` shapes into the map. Three readers of one file is three chances
    /// for the plan and the ground to disagree about where the railroad is - and the railroad is
    /// the one thing in this town somebody who grew up here would notice being in the wrong
    /// place. The generator is a separate language and has its own reader by necessity; these two
    /// do not need to be.
    /// </summary>
    public static class MapFeatures
    {
        public sealed class Feature
        {
            public string Kind;
            public List<Vector2> Points;
        }

        private static List<Feature> _cache;

        /// <summary>
        /// When the file the cache was built from was last written.
        ///
        /// THE CACHE HAD NO WAY TO BE INVALIDATED, and a static survives a domain reload when the
        /// editor is not set to reload domains on play. So editing Content/features.txt and then
        /// measuring the railway returned the geometry from BEFORE the edit - the file on disk and
        /// the line being drawn silently disagreeing, which is the precise shape of "I fixed it"
        /// followed by "it looks exactly the same". Found on 2026-08-04 while moving the rail onto
        /// its right of way: the check that caught it compared the first vertex on disk against
        /// the first vertex in memory, and they differed.
        ///
        /// One stat call per read is nothing next to re-parsing, and far less than the cost of one
        /// wrong measurement.
        /// </summary>
        private static System.DateTime _stamp;

        /// <summary>Every feature in the file, in file order. Empty if the file is missing -
        /// a checkout without Content/features.txt still builds, it just has no railway.</summary>
        public static IReadOnlyList<Feature> All()
        {
            var written = ContentLoader.WrittenAt("features.txt");
            if (_cache != null && written == _stamp) return _cache;

            _stamp = written;
            _cache = new List<Feature>();
            string text;
            try { text = ContentLoader.Read("features.txt"); }
            catch { return _cache; }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int gap = line.IndexOf(' ');
                if (gap <= 0) continue;

                var pts = new List<Vector2>();
                foreach (var piece in line.Substring(gap).Split(' '))
                {
                    int comma = piece.IndexOf(',');
                    if (comma <= 0) continue;
                    if (float.TryParse(piece.Substring(0, comma), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float x)
                     && float.TryParse(piece.Substring(comma + 1), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float y))
                        pts.Add(new Vector2(x, y));
                }
                if (pts.Count < 2) continue;

                _cache.Add(new Feature { Kind = line.Substring(0, gap), Points = pts });
            }
            return _cache;
        }

        /// <summary>
        /// Catmull-Rom through the real vertices, unchanged - every original point is still on
        /// the curve exactly where it was, only the straight segments between them are replaced
        /// by an arc. The end points are their own neighbour (clamped), which is the standard fix
        /// for a spline with nothing before its first or after its last control point.
        ///
        /// SMOOTHED FOR DRAWING ONLY. A real survey way is a sparse set of straight-line vertices
        /// - a few points every hundred metres - which is honest data and reads as a slightly
        /// kinked series of straight segments the moment it is drawn at any zoom closer than the
        /// whole town. The real line does not have those corners; the survey just did not sample
        /// it any finer. A river or a field ditch kinking is unremarkable; a railroad doing it
        /// looks like an error, which is why only the rail asks for this.
        ///
        /// (The curve itself is Noir.Core.World.RoadPath.Smooth. It moved to Core so the roads
        /// and the railway bend along the same arithmetic rather than two copies of it, and so
        /// it can be tested under dotnet test - which it never could be here.)
        /// </summary>
        public static List<Vector2> Smoothed(List<Vector2> pts)
        {
            if (pts.Count < 3) return pts;

            var input = new Noir.Core.Contracts.Vec2[pts.Count];
            for (int i = 0; i < pts.Count; i++) input[i] = new Noir.Core.Contracts.Vec2(pts[i].x, pts[i].y);

            var smoothed = Noir.Core.World.RoadPath.Smooth(input);

            var result = new List<Vector2>(smoothed.Length);
            foreach (var p in smoothed) result.Add(new Vector2(p.X, p.Y));
            return result;
        }
    }
}
