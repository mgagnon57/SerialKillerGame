using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Noir.Core.World;
using Noir.Core.Survey;

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
        private static List<Parcel> _in1991;
        private static int _in1991Revision;

        /// <summary>Every parcel in the file, including ones that did not exist in 1991. Empty
        /// rather than null if the content is missing. Most callers want <see cref="In1991"/> -
        /// this one is for anything that has to resolve a parcel id whatever its date.</summary>
        public static IReadOnlyList<Parcel> All { get { Load(); return _all; } }

        /// <summary>
        /// The lots that were there, which is what the town should be drawn from.
        ///
        /// Content/parcels.txt is the county's boundaries as they stand TODAY, and 37 of them are
        /// ground that was subdivided out of a field after the game's own date - see Rulings, and
        /// the header of Content/parcel-1991.txt. Drawing those puts lot lines across what should
        /// be open country, and letting them be clicked offers the player a property that did not
        /// exist. Filtering here rather than at each renderer means the plan view, the ground
        /// colour, the zoning patch and the click test cannot disagree about which lots are real.
        /// </summary>
        public static IReadOnlyList<Parcel> In1991
        {
            get
            {
                Load();
                // The browser map rewrites the rulings while the game is running, so this is
                // rebuilt when they change rather than cached once - rule a lot absent and it
                // leaves the map without a restart. Compared against Rulings.Revision rather than
                // the file's own mtime, so asking costs an int rather than a syscall.
                int rev = Rulings.Revision;
                if (_in1991 != null && rev == _in1991Revision) return _in1991;
                _in1991Revision = rev;
                _in1991 = new List<Parcel>(_all.Count);
                foreach (var p in _all) if (!Rulings.Absent(p.Id)) _in1991.Add(p);
                return _in1991;
            }
        }

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
        ///
        /// Lots ruled absent are not here to be found: a click landing on ground that was a field
        /// in 1991 falls through to the field, which is what the player is looking at.
        /// </summary>
        public static Parcel? Find(Vector2 at)
        {
            foreach (var p in In1991)
            {
                if (!p.Bounds.Contains(at)) continue;
                if (Inside(p.Points, at)) return p;
            }
            return null;
        }

        /// <summary>
        /// The parcel under a point WHATEVER its date, including lots ruled absent.
        ///
        /// For the one job that has to reach a lot the town has stopped drawing: deciding whether
        /// a building is standing on ground that was not a lot in 1991. Asking <see cref="Find"/>
        /// that question can only ever answer no, because the lot in question is precisely the one
        /// it filters out. Not for picking - a click should fall through those lots.
        /// </summary>
        public static Parcel? FindIncludingGone(Vector2 at)
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

        /// <summary>
        /// Whether this edge is a county split running through the middle of one property, rather
        /// than a boundary anybody standing there would see.
        ///
        /// The grade school stands on three parcels and the Chicago Street terrace on eleven. The
        /// lines between them are in the tax roll because the county sells ground in pieces, not
        /// because there is anything there - so drawing them makes one building look like three
        /// lots, which is exactly what the owner ruled it is not. Suppressing them is what turns
        /// a group into a property on the map.
        ///
        /// Safe to ask about any edge of any lot: an ungrouped lot has no interior edges, so this
        /// is false for the 771 lots that are their own property.
        ///
        /// THE SAME DISSOLVE THE BROWSER MAP DOES, deliberately - see dissolveEdges in
        /// tools/viewer-template.html. An edge with the property on BOTH sides is internal; an
        /// edge with it on one side is the outer boundary. Decided by stepping half a metre off
        /// the edge either way and asking whether that point is still inside some lot of the
        /// group, which needs no shared vertices between neighbours.
        ///
        /// Matching the two matters more than it looks. Vertex matching was the first cut here
        /// and it agrees with this on today's data - all 23 properties happen to share their
        /// interior vertices exactly - but cadastral lots frequently draw the same boundary with
        /// different vertex counts on either side, and the first grouping that did would have
        /// silently failed to merge in the game while merging correctly in the tool the ruling
        /// was made in. A rule that works until the data changes shape is not the rule the owner
        /// is looking at.
        /// </summary>
        public static bool SharedInsideOneProperty(Vector2 a, Vector2 b)
        {
            BuildInteriorEdges();
            return _interior.Count > 0 && _interior.Contains(EdgeKey(a, b));
        }

        private static HashSet<(int, int, int, int)> _interior;
        private static int _interiorRevision;

        private static void BuildInteriorEdges()
        {
            Load();
            // Asked once per lot edge while the plan mesh is built - a few thousand times - so
            // this compares an int rather than stat'ing the rulings file each time.
            int rev = Rulings.Revision;
            if (_interior != null && rev == _interiorRevision) return;
            _interiorRevision = rev;
            _interior = new HashSet<(int, int, int, int)>();

            foreach (var kv in Rulings.Properties)
            {
                var lots = kv.Value;
                if (lots.Count < 2) continue;

                var rings = new List<Vector2[]>(lots.Count);
                foreach (int id in lots)
                {
                    var p = ById(id);
                    if (p != null) rings.Add(p.Value.Points);
                }

                // TWO TESTS, AND AN EDGE IS INTERIOR IF EITHER SAYS SO. Neither is a superset of
                // the other on the real county geometry: the step test misses an edge whose
                // neighbour is thinner than DissolveStep, where standing half a metre off lands
                // outside the group entirely, and the vertex test misses a boundary the two lots
                // drew with different vertex counts. Taking both costs one extra pass over a few
                // hundred edges and cannot merge less than either alone.
                var owner = new Dictionary<(int, int, int, int), int>();
                foreach (int id in lots)
                {
                    var p = ById(id);
                    if (p == null) continue;
                    var pts = p.Value.Points;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var key = EdgeKey(pts[i], pts[(i + 1) % pts.Length]);
                        if (owner.TryGetValue(key, out int first) && first != id) _interior.Add(key);
                        else owner[key] = id;
                    }
                }

                foreach (var ring in rings)
                for (int i = 0; i < ring.Length; i++)
                {
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Length];
                    var d = b - a;
                    float len = d.magnitude;
                    if (len < 0.01f) continue;

                    var mid = (a + b) * 0.5f;
                    var step = new Vector2(-d.y / len, d.x / len) * DissolveStep;

                    bool left = false, right = false;
                    foreach (var q in rings)
                    {
                        if (!left && Inside(q, mid + step)) left = true;
                        if (!right && Inside(q, mid - step)) right = true;
                        if (left && right) break;
                    }
                    if (left && right) _interior.Add(EdgeKey(a, b));
                }
            }
        }

        /// <summary>How far off an edge to stand when asking which property is on that side. Half
        /// a metre, the same as the browser map's own dissolve - wide enough to clear the slivers
        /// between two lots drawn from separate surveys, narrow enough that no real lot is that
        /// thin.</summary>
        private const float DissolveStep = 0.5f;

        /// <summary>A segment's identity, direction-free, so the same edge read from either of the
        /// two lots that share it produces the same key.</summary>
        private static (int, int, int, int) EdgeKey(Vector2 a, Vector2 b)
        {
            int ax = Mathf.RoundToInt(a.x * 100f), ay = Mathf.RoundToInt(a.y * 100f);
            int bx = Mathf.RoundToInt(b.x * 100f), by = Mathf.RoundToInt(b.y * 100f);
            return (ax < bx || (ax == bx && ay <= by)) ? (ax, ay, bx, by) : (bx, by, ax, ay);
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
