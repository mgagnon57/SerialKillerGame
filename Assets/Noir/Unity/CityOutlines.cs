using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// The town as a survey plan: every plot's footprint painted on the ground, and no
    /// buildings at all.
    ///
    /// WHY THIS EXISTS. The Universal Pack contains exactly two house families, Bayhouse and
    /// Squarehouse, and both are Chicago brownstones - bay fronts, stoops, fire escapes. There
    /// is no clapboard, no porch and no gable roof anywhere in it. Rossville's houses cannot be
    /// built out of what we own, and a street of brick tenements is not a near miss, it is a
    /// different country. So until there is a kit that can build them, this draws the FOOTPRINT
    /// and says nothing about the elevation - which is honest, and which is the half of the
    /// information that was researched: the plot outlines are the real ones, off the real grid.
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
    /// COLOURED BY WHAT THE PLOT IS, because a plan you cannot read is a plan. Homes, trade,
    /// civic and land each get their own, so the shape of the town is legible from the air.
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

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var go = new GameObject("CityOutlines");
            go.transform.SetParent(parent, false);

            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();

            // ---- the real lots, from the county's records ----
            int parcels = Parcels(verts, cols, tris);

            var kinds = PlaceKindTable.Current;

            foreach (var place in world.AllPlaces)
            {
                if (place == null) continue;

                var row = kinds.Row(place.Kind);
                var colour = ColourOf(row.Name, row.IsHome);
                var b = place.Bounds;

                Outline(verts, cols, tris, b.X, b.Y, b.W, b.H, colour);

                // A doorway is a gap in the line and a stub pointing into the plot, which is how
                // a plan says which way a building faces. It is also the only way to tell, on a
                // footprint, that 408 Holmes fronts Holmes Avenue rather than the alley behind.
                if (row.IsBuilding) Door(verts, cols, tris, place, colour);
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

            Debug.Log($"[outlines] {parcels} county parcels + {world.PlaceCount} places, "
                    + $"{tris.Count / 3} triangles in one mesh.");
            return go;
        }

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
        /// </summary>
        private static int Parcels(List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            string text;
            try { text = ContentLoader.Read("parcels.txt"); }
            catch { Debug.LogWarning("[outlines] no Content/parcels.txt - lot lines are missing."); return 0; }

            var lot = new Color(0.94f, 0.95f, 0.98f);      // chalk, and it has to read at 500m
            int n = 0;

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var pts = new List<Vector2>();
                foreach (var piece in line.Split(' '))
                {
                    int comma = piece.IndexOf(',');
                    if (comma <= 0) continue;
                    if (float.TryParse(piece.Substring(0, comma), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float x)
                     && float.TryParse(piece.Substring(comma + 1), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float y))
                        pts.Add(new Vector2(x, y));
                }
                if (pts.Count < 3) continue;

                for (int i = 0; i < pts.Count; i++)
                    Edge(verts, cols, tris, pts[i], pts[(i + 1) % pts.Count], lot);
                n++;
            }
            return n;
        }

        /// <summary>
        /// One ribbon between two points, at any angle. The rectangle version below cannot draw
        /// a lot line that is not square to the map, and almost none of them are.
        /// </summary>
        private static void Edge(List<Vector3> verts, List<Color> cols, List<int> tris,
                                 Vector2 a, Vector2 b, Color colour)
        {
            var along = b - a;
            float len = along.magnitude;
            if (len < 0.01f) return;

            var side = new Vector2(-along.y, along.x) / len * (Stroke * 0.5f);
            int n = verts.Count;

            foreach (var p in new[] { a - side, b - side, b + side, a + side })
                verts.Add(Space3D.ToWorld(new Core.Contracts.Vec2(p.x, p.y), Lift));
            for (int i = 0; i < 4; i++) cols.Add(colour);

            tris.Add(n); tris.Add(n + 2); tris.Add(n + 1);
            tris.Add(n); tris.Add(n + 3); tris.Add(n + 2);
        }

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
                    return new Color(0.35f, 0.78f, 1.00f);      // civic - blue

                case "cornfield": case "paddock": case "orchard": case "copse":
                case "green": case "playground": case "allotments": case "churchyard":
                    return new Color(0.45f, 0.85f, 0.45f);      // land - green
            }

            return home ? new Color(1.00f, 0.98f, 0.92f)        // somebody lives here - chalk
                        : new Color(0.70f, 0.70f, 0.74f);       // everything else - grey
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
