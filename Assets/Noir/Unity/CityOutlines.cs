using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// The town as a survey plan.
    ///
    /// BY DEFAULT THIS DRAWS TWO THINGS ONLY: the 794 real county parcels (Parcels()) and the
    /// named features - the railway, the water, the schools, the RR crossings (Features()). The
    /// road corridors and the per-place footprint-with-door-and-colour-by-kind machinery below
    /// both exist and both still work, but VillageHost.ShowPlanRoads and ShowPlanFootprints
    /// default false - they read as a duplicate of the parcels once the county's own lot lines
    /// are the thing on screen, not an addition to them. They stay for VillageHost.ShowBuildings'
    /// comparison path and for anyone who wants the generated grid back rather than the real one.
    ///
    /// WHY THE FOOTPRINTS EXIST AT ALL. The Universal Pack contains exactly two house families,
    /// Bayhouse and Squarehouse, and both are Chicago brownstones - bay fronts, stoops, fire
    /// escapes. There is no clapboard, no porch and no gable roof anywhere in it. Rossville's
    /// houses cannot be built out of what we own, and a street of brick tenements is not a near
    /// miss, it is a different country. So until there is a kit that can build them, the
    /// footprint pass draws the FOOTPRINT and says nothing about the elevation - honest, but a
    /// generated 11m x 7m box standing in for a real 26m x 51m lot, which is why the parcels
    /// became the default the moment the real data existed to draw instead.
    ///
    /// It is also the thing that makes the geometry judgeable. Walking a plan tells you whether
    /// the blocks are the right size, whether the setbacks look like a street, whether the town
    /// is the right shape - and none of that is answerable through a wall of the wrong building.
    ///
    /// ONE MESH FOR THE WHOLE TOWN. Six hundred LineRenderers would be six hundred draw calls
    /// for something that never moves; this is one mesh, one material, built once. Ribbons
    /// rather than MeshTopology.Lines because a line has no width in URP - it is one pixel at
    /// any distance, which vanishes at fifty metres and aliases into a dashed mess at ten.
    ///
    /// COLOURED BY WHAT THE PLOT IS, when the footprint pass runs at all - homes, trade, civic
    /// and land each get their own. The parcels themselves are one dim colour; see ParcelNotes
    /// and AuthoredFootprints for how an individual lot gets picked back out of that.
    /// </summary>
    public static class CityOutlines
    {
        /// <summary>Width of the painted line, in metres. A surveyor's chalk, not a kerb.</summary>
        private const float Stroke = 0.9f;

        /// <summary>
        /// How far above the ground it floats.
        ///
        /// Small enough to read as paint on the grass and large enough to beat z-fighting with
        /// the ground mesh, which is flat and exactly coincident everywhere else.
        /// </summary>
        private const float Lift = 0.06f;

        /// <summary>
        /// showRoads and showFootprints default true so nothing outside VillageHost/CityShot -
        /// an editor tool, a test fixture - silently loses the overlays it never asked to drop.
        /// The game itself now passes both false: the county parcels read as a town on their
        /// own, and a road grid drawn over them competed with the very thing it exists to
        /// support, the same complaint as a place outline on a lot the parcel already draws.
        /// </summary>
        public static GameObject Build(WorldModel world, Transform parent,
                                       bool showRoads = true, bool showFootprints = true)
        {
            var go = new GameObject("CityOutlines");
            go.transform.SetParent(parent, false);

            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();

            // ---- the real lots, from the county's records ----
            int parcels = Parcels(verts, cols, tris);

            // ---- and the road corridors, in their own colour ----
            int roads = showRoads ? Roads(world, verts, cols, tris) : 0;

            // ---- the railway, the water, the schools ----
            int features = Features(verts, cols, tris);

            int footprints = 0;
            if (showFootprints)
            {
                var kinds = PlaceKindTable.Current;

                foreach (var place in world.AllPlaces)
                {
                    if (place == null) continue;

                    var row = kinds.Row(place.Kind);
                    var colour = ColourOf(row.Name, row.IsHome);
                    var b = place.Bounds;

                    Outline(verts, cols, tris, b.X, b.Y, b.W, b.H, colour);

                    // A doorway is a gap in the line and a stub pointing into the plot, which is
                    // how a plan says which way a building faces. It is also the only way to
                    // tell, on a footprint, that 408 Holmes fronts Holmes Avenue rather than the
                    // alley behind.
                    if (row.IsBuilding) Door(verts, cols, tris, place, colour);
                    footprints++;
                }
            }

            var mesh = new Mesh { name = "CityOutlines", indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Paint();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Debug.Log($"[outlines] {parcels} county parcels, {roads} road corridors, "
                    + $"{features} named features, {footprints} of {world.PlaceCount} places "
                    + $"outlined - {tris.Count / 3} triangles in one mesh.");
            return go;
        }

        /// <summary>
        /// The things that are neither a road nor a lot: the railway, the water, the schools.
        ///
        /// Content/features.txt, from OpenStreetMap. These are the landmarks somebody who grew up
        /// in the place navigates BY - the CSX line down the east side, the North Fork Vermilion
        /// out west, the ponds behind the school - and a plan without them is a grid of
        /// rectangles that could be any town on the survey.
        ///
        /// Each is drawn heavier than a lot line and in its own colour, because they are the
        /// features you should be able to find without looking for them.
        /// </summary>
        private static int Features(List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            string text;
            try { text = ContentLoader.Read("features.txt"); }
            catch { return 0; }

            int n = 0;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int gap = line.IndexOf(' ');
                if (gap <= 0) continue;
                string kind = line.Substring(0, gap);

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

                // SMOOTHED FOR DRAWING ONLY. A real survey way is a sparse set of straight-line
                // vertices - a few points every hundred metres - which is honest data and reads
                // as a slightly kinked series of straight segments the moment it is drawn at any
                // zoom closer than the whole town. The real line does not have those corners; the
                // survey just did not sample it any finer. Smoothing between the same vertices
                // (not moving or discarding a single one) is cosmetic, and confined to the one
                // feature this actually reads as wrong for - a river or a field ditch kinking is
                // unremarkable, a railroad doing it looks like an error.
                if (kind == "rail" || kind == "oldrail") pts = Smoothed(pts);

                (Color colour, float weight, bool closed) = kind switch
                {
                    "rail"    => (new Color(1.00f, 0.95f, 0.80f), 2.6f, false),   // the CSX line
                    "oldrail" => (new Color(0.62f, 0.58f, 0.48f), 1.6f, false),   // lifted, still a scar
                    "river"   => (new Color(0.30f, 0.60f, 1.00f), 3.0f, false),
                    "stream"  => (new Color(0.32f, 0.66f, 0.98f), 2.0f, false),
                    "ditch"   => (new Color(0.28f, 0.50f, 0.78f), 1.2f, false),
                    "water"   => (new Color(0.35f, 0.72f, 1.00f), 2.0f, true),    // the ponds
                    "school"  => (new Color(0.95f, 0.45f, 0.85f), 2.4f, true),

                    // A short tick across the rail, at the four OSM-tagged level crossings only
                    // (Henderson, Green, Benton, Attica) - not every street reaches the far side.
                    // Warning amber, the same colour a real crossbuck is lit.
                    "crossing" => (new Color(1.00f, 0.75f, 0.10f), 3.4f, false),
                    _         => (Color.gray, 1f, false),
                };

                int last = closed ? pts.Count : pts.Count - 1;
                for (int i = 0; i < last; i++)
                    Edge(verts, cols, tris, pts[i], pts[(i + 1) % pts.Count], colour, Stroke * weight);

                // TIES, for a real line, not just a coloured river with a different tint. A
                // plain line reads as "some line" - the cross-ties are the one mark that reads
                // as "railroad" on any survey drawing without needing the legend.
                if (kind == "rail" || kind == "oldrail")
                    Ties(verts, cols, tris, pts, colour);

                n++;
            }
            return n;
        }

        /// <summary>Sub-divisions inserted between each pair of real vertices - four turns a
        /// dozen-point survey way into a curve with no single straight-line segment longer than
        /// a fraction of the gap between real points, without moving any of those points.</summary>
        private const int SmoothSteps = 4;

        /// <summary>
        /// Catmull-Rom through the real vertices, unchanged - every original point is still on
        /// the curve exactly where it was, only the straight segments between them are replaced
        /// by an arc. The end points are their own neighbour (clamped), which is the standard fix
        /// for a spline with nothing before its first or after its last control point.
        /// </summary>
        private static List<Vector2> Smoothed(List<Vector2> pts)
        {
            if (pts.Count < 3) return pts;

            var out_ = new List<Vector2> { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p0 = pts[Mathf.Max(i - 1, 0)];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = pts[Mathf.Min(i + 2, pts.Count - 1)];

                for (int s = 1; s <= SmoothSteps; s++)
                {
                    float t = (float)s / SmoothSteps;
                    out_.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }
            return out_;
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1)
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>
        /// How far the tie spans across the line, in metres. Has to clear the rail line's own
        /// drawn width (2.34m: Stroke 0.9 x weight 2.6) by enough to stick out visibly on both
        /// sides - the first attempt at 2.6m overhung by 0.13m a side, which is sub-pixel at any
        /// distance this plan is ever viewed from and simply vanished under the line it was
        /// meant to mark.
        /// </summary>
        private const float TieWidth = 7f;

        /// <summary>Centre to centre along the rail - close enough to read as continuous ties
        /// without submitting one triangle pair per actual sleeper (every ~0.6m in reality).</summary>
        private const float TieSpacing = 9f;

        /// <summary>
        /// A short bar across the line at regular intervals along its whole length, walked
        /// continuously across every segment of the polyline so the spacing does not reset - and
        /// therefore does not visibly bunch or gap - at each vertex.
        /// </summary>
        private static void Ties(List<Vector3> verts, List<Color> cols, List<int> tris,
                                 List<Vector2> pts, Color colour)
        {
            float carry = 0f;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                Vector2 along = b - a;
                float len = along.magnitude;
                if (len < 0.01f) continue;
                Vector2 dir = along / len;
                Vector2 side = new Vector2(-dir.y, dir.x) * (TieWidth * 0.5f);

                float t = TieSpacing - carry;
                for (; t < len; t += TieSpacing)
                {
                    Vector2 p = a + dir * t;
                    Edge(verts, cols, tris, p - side, p + side, colour, Stroke * 1.1f);
                }
                carry = t - len;
            }
        }

        /// <summary>
        /// The edge of every road corridor, which on a plan is the thing everything else is
        /// measured from.
        ///
        /// The CORRIDOR, not the asphalt. A road in this map is a width about a centreline -
        /// thirty metres for Route 1, ten for a village street - and that full width is what a
        /// plat drawing shows as the right of way. The carriageway inside it is a detail of how
        /// the road was built, which is exactly the sort of claim this drawing is not making.
        ///
        /// Its own colour, and a heavier line than a lot boundary, because the street grid is
        /// the skeleton: read it first and the property lines hang off it.
        /// </summary>
        private static int Roads(WorldModel world, List<Vector3> verts, List<Color> cols,
                                 List<int> tris)
        {
            var kerb = new Color(0.30f, 0.86f, 1.00f);
            int n = 0;

            foreach (var line in world.Roads.Lines)
            {
                if (line == null || !line.IsStraight) continue;

                float lo = line.Centre - line.HalfWidth, hi = line.Centre + line.HalfWidth;

                // From and To bound the stretch that exists; Centre and HalfWidth bound its
                // width. Which of those is x and which is y depends on the way it runs.
                var a = line.IsNorthSouth ? new Vector2(lo, line.From) : new Vector2(line.From, lo);
                var b = line.IsNorthSouth ? new Vector2(hi, line.To) : new Vector2(line.To, hi);

                Wide(verts, cols, tris, new Vector2(a.x, a.y), new Vector2(b.x, a.y), kerb);
                Wide(verts, cols, tris, new Vector2(a.x, b.y), new Vector2(b.x, b.y), kerb);
                Wide(verts, cols, tris, new Vector2(a.x, a.y), new Vector2(a.x, b.y), kerb);
                Wide(verts, cols, tris, new Vector2(b.x, a.y), new Vector2(b.x, b.y), kerb);
                n++;
            }
            return n;
        }

        /// <summary>A road edge: the same ribbon as a lot line, drawn heavier.</summary>
        private static void Wide(List<Vector3> verts, List<Color> cols, List<int> tris,
                                 Vector2 a, Vector2 b, Color colour) =>
            Edge(verts, cols, tris, a, b, colour, Stroke * 1.8f);

        /// <summary>
        /// Every lot line in the village, as the county surveyed it.
        ///
        /// Content/parcels.txt is 794 cadastral parcels from Vermilion County's own property
        /// service, converted to metres about the Chicago x Attica crossing. They are POLYGONS,
        /// not rectangles - a real lot has a kink in it where the alley cuts the corner - so
        /// they are drawn edge by edge rather than as a box, and that irregularity is most of
        /// what makes a plan look surveyed rather than generated.
        ///
        /// A missing file is survivable: the places still outline themselves and the town is
        /// merely less detailed, which is better than refusing to build.
        ///
        /// Parsed by ParcelIndex rather than here, so the same 794 rings back both this drawing
        /// and PropertyInspector's lookup of what a click landed on - one parse, read twice,
        /// rather than two parsers that could quietly drift apart.
        /// </summary>
        private static int Parcels(List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            var all = ParcelIndex.All;
            if (all.Count == 0)
            {
                Debug.LogWarning("[outlines] no Content/parcels.txt - lot lines are missing.");
                return 0;
            }

            // Pale rather than chalk-white: at full brightness a lot line reads exactly as
            // strongly as the cyan road corridors, and the eye has nothing to sort first from
            // second. Dimmed so the roads - the skeleton the property lines hang off - are what
            // you see before you see anything else.
            var lot = new Color(0.55f, 0.56f, 0.60f);

            foreach (var parcel in all)
            {
                var pts = parcel.Points;
                for (int i = 0; i < pts.Length; i++)
                    Edge(verts, cols, tris, pts[i], pts[(i + 1) % pts.Length], lot);
            }
            return all.Count;
        }

        /// <summary>
        /// One ribbon between two points, at any angle. The rectangle version below cannot draw
        /// a lot line that is not square to the map, and almost none of them are. See Ribbon for
        /// the shared implementation - three other small renderers need exactly this shape.
        /// </summary>
        private static void Edge(List<Vector3> verts, List<Color> cols, List<int> tris,
                                 Vector2 a, Vector2 b, Color colour, float stroke = Stroke) =>
            Ribbon.Edge(verts, cols, tris, a, b, colour, stroke, Lift);

        /// <summary>
        /// Four ribbons round a rectangle. Drawn as quads laid flat rather than as lines,
        /// because a line primitive has no width and disappears.
        /// </summary>
        private static void Outline(List<Vector3> verts, List<Color> cols, List<int> tris,
                                    float x, float y, float w, float h, Color colour)
        {
            Bar(verts, cols, tris, x, y, w, Stroke, colour);                    // north
            Bar(verts, cols, tris, x, y + h - Stroke, w, Stroke, colour);       // south
            Bar(verts, cols, tris, x, y, Stroke, h, colour);                    // west
            Bar(verts, cols, tris, x + w - Stroke, y, Stroke, h, colour);       // east
        }

        private static void Bar(List<Vector3> verts, List<Color> cols, List<int> tris,
                                float x, float y, float w, float h, Color colour)
        {
            int n = verts.Count;

            // Village y runs south, world z runs north, so the quad is wound the other way round
            // than it reads here - the same flip Space3D makes everywhere else.
            verts.Add(Space3D.ToWorld(new Core.Contracts.Vec2(x, y), Lift));
            verts.Add(Space3D.ToWorld(new Core.Contracts.Vec2(x + w, y), Lift));
            verts.Add(Space3D.ToWorld(new Core.Contracts.Vec2(x + w, y + h), Lift));
            verts.Add(Space3D.ToWorld(new Core.Contracts.Vec2(x, y + h), Lift));

            for (int i = 0; i < 4; i++) cols.Add(colour);

            tris.Add(n); tris.Add(n + 2); tris.Add(n + 1);
            tris.Add(n); tris.Add(n + 3); tris.Add(n + 2);
        }

        /// <summary>A short stub from the authored front door into the plot.</summary>
        private static void Door(List<Vector3> verts, List<Color> cols, List<int> tris,
                                 Place place, Color colour)
        {
            var b = place.Bounds;
            var d = place.Door;
            const float Reach = 2.2f;

            bool vertical = d.X <= b.X || d.X >= b.X + b.W - 1;
            if (vertical)
            {
                float x = d.X <= b.X ? b.X : b.X + b.W - Reach;
                Bar(verts, cols, tris, x, d.Y - Stroke * 0.5f, Reach, Stroke, colour);
            }
            else
            {
                float y = d.Y <= b.Y ? b.Y : b.Y + b.H - Reach;
                Bar(verts, cols, tris, d.X - Stroke * 0.5f, y, Stroke, Reach, colour);
            }
        }

        /// <summary>
        /// What each sort of plot is painted. Four families, because more than that stops being
        /// readable at a glance and reading it at a glance is the entire point.
        /// </summary>
        private static Color ColourOf(string kind, bool home)
        {
            switch (kind)
            {
                case "shop": case "pub": case "postoffice": case "diner": case "bank":
                case "casino": case "cinema": case "newsstand": case "icecream":
                case "gasstation": case "carwash":
                    return new Color(1.00f, 0.72f, 0.20f);      // trade - amber

                case "school2": case "hospital": case "precinct": case "firestation":
                case "villagehall": case "restroom": case "watertower": case "elevator":
                    return new Color(0.76f, 0.55f, 1.00f);      // civic - violet, so roads keep cyan

                case "cornfield": case "paddock": case "orchard": case "copse":
                case "green": case "playground": case "allotments": case "churchyard":
                    return new Color(0.45f, 0.85f, 0.45f);      // land - green
            }

            // Homes still read a touch brighter than a bare lot line - that is the one thing
            // this tier of the palette needs to say - but dimmed to match the county parcels
            // below, so the two together sit under the road cyan rather than over it.
            return home ? new Color(0.74f, 0.72f, 0.66f)        // somebody lives here - warm pale
                        : new Color(0.52f, 0.52f, 0.55f);       // everything else - dim grey
        }

        /// <summary>
        /// Unlit and vertex-coloured, so a plan reads the same at noon and at midnight and the
        /// sun does not decide whether you can see the town.
        /// </summary>
        private static Material Paint()
        {
            // Sprites/Default, which is an odd-looking choice and is the right one: it is unlit,
            // it MULTIPLIES BY VERTEX COLOUR, and it ships with every Unity install including
            // URP. "Universal Render Pipeline/Unlit" was the obvious pick and it does not read
            // vertex colour at all - there is no _VERTEXCOLOR keyword on it, so enabling one
            // does nothing and every line comes out the base colour.
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color");

            var m = new Material(shader) { name = "Outline Paint" };
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);

            // Drawn after the ground but as opaque geometry, so a lot line is paint on the grass
            // rather than something the ground can win a depth fight against.
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 50;
            return m;
        }
    }
}
