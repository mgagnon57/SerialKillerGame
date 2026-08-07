using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Every road as a thin line down its middle, and nothing else.
    ///
    /// WHY THIS EXISTS. CityStreets lays real road tiles - asphalt, kerbs, lane paint, crossings,
    /// junction pieces - which is what the town should look like and is the wrong drawing for the
    /// question "is this street on its right of way". A 33 ft band of textured asphalt covers the
    /// lot lines it is meant to be judged against, and the junction tiles snap square, so a road
    /// that is slightly off reads as a road that is fine.
    ///
    /// This draws the CENTRELINE: one ribbon, two metres wide, following RoadLine.Path exactly.
    /// The parcels stay visible underneath it, and where a centreline sits against them is the
    /// whole of what is being asked.
    ///
    /// STREETS AND ALLEYS COME BACK ON SEPARATE ROOTS, the same split CityStreets makes, so the
    /// alleys can be switched off while the streets are being read - see Layers.Kind.Alleys.
    ///
    /// It follows Path rather than Centre/IsNorthSouth on purpose. Three roads bend now - Chicago
    /// Street, Railroad Avenue, and Harrison at its Benton corner - and an axis-aligned drawing
    /// would quietly straighten exactly the roads whose shape is in question.
    /// </summary>
    public static class RoadCentrelines
    {
        /// <summary>Width of the drawn line in metres. Wide enough to follow at a distance,
        /// narrow enough that a 66 ft right of way is still readable either side of it.</summary>
        private const float Stroke = 2.0f;

        /// <summary>Clear of the ground, and above the parcel lines' own 0.25 lift so a road
        /// reads as crossing a lot boundary rather than disappearing under it. Raised with them:
        /// the two have to move together or the roads go under the lot lines instead.</summary>
        private const float Lift = 0.32f;

        public static GameObject Build(WorldModel world, Transform parent, out GameObject alleys)
        {
            var streets = new GameObject("RoadCentrelines");
            streets.transform.SetParent(parent, false);

            alleys = new GameObject("AlleyCentrelines");
            alleys.transform.SetParent(parent, false);

            int drawn = Fill(world, streets, false) + Fill(world, alleys, true);
            Debug.Log($"[centrelines] {drawn} roads drawn as bare centre lines.");
            return streets;
        }

        private static int Fill(WorldModel world, GameObject go, bool wantAlleys)
        {
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            var tangents = new List<Vector4>();
            int n = 0;

            // Matched to CityOutlines on purpose. Roads at a fixed width in metres beside lot
            // lines at a fixed width in PIXELS would part company as the camera pulls back - the
            // roads thinning to dashes over a plan whose boundaries stayed crisp, which reads as
            // the roads being the thing that is broken.
            var paint = Paint(out bool screenSpace);

            // One segment of one line, pushed sideways off the centre by `offset` metres.
            void Band(Vector2 a, Vector2 b, float offset, Color c)
            {
                var d = b - a;
                float len = d.magnitude;
                if (len < 1e-4f) return;

                var push = offset == 0f ? Vector2.zero
                                        : new Vector2(-d.y, d.x) / len * offset;
                if (screenSpace)
                    Ribbon.ScreenEdge(verts, cols, tangents, tris, a + push, b + push, c, Lift);
                else
                    Ribbon.Edge(verts, cols, tris, a + push, b + push, c, Stroke, Lift);
            }

            foreach (var line in world.Roads.Lines)
            {
                if (line == null || line.Path == null) continue;
                bool isAlley = line.Class == RoadClass.Alley;
                if (isAlley != wantAlleys) continue;

                var colour = Colour(line.Class);

                // THE ROAD AT THE WIDTH IT IS, not a line standing in for it.
                //
                // The centreline answers "is this street on its right of way" and answers nothing
                // about how much of that right of way it actually takes - which is the question a
                // sidewalk lives inside. A Rossville street paves a 10 m corridor down the middle
                // of a 20 m easement, so five metres each side is public ground that is NOT road,
                // and that gap is where a walk went on the streets that had one.
                //
                // Drawn as EDGES rather than filled, which is the whole reason this renderer
                // exists instead of CityStreets: a band of asphalt covers the lot lines it is
                // meant to be judged against. Two kerbs and two right-of-way lines say the same
                // thing and leave the parcels readable underneath.
                var kerb = colour;
                var middle = colour * 0.5f;
                var verge = colour * 0.34f;
                float half = line.Width / 2f;
                bool hasVerge = line.Easement > line.Width + 0.5f;
                float row = line.Easement / 2f;

                // Walked at a fixed pitch rather than vertex to vertex: a bend authored as three
                // points and a bend authored as thirty then draw the same weight of line.
                var prev = line.Path.PointAt(0f);
                for (float d = 2f; d <= line.Path.Length; d += 2f)
                {
                    var here = line.Path.PointAt(Mathf.Min(d, line.Path.Length));
                    var a = new Vector2(prev.X, prev.Y);
                    var b = new Vector2(here.X, here.Y);

                    Band(a, b, 0f, middle);
                    Band(a, b,  half, kerb);
                    Band(a, b, -half, kerb);
                    if (hasVerge)
                    {
                        Band(a, b,  row, verge);
                        Band(a, b, -row, verge);
                    }
                    prev = here;
                }
                n++;
            }

            var mesh = new Mesh
            {
                name = go.name,
                indexFormat = verts.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            if (screenSpace) mesh.SetUVs(0, tangents);
            mesh.SetTriangles(tris, 0);

            // A pixel-width line has no width in the mesh, so a dead straight road comes out as
            // bounds with no thickness and gets culled the moment you look along it.
            mesh.RecalculateBounds();
            if (screenSpace)
            {
                var bounds = mesh.bounds;
                bounds.Expand(2f);
                mesh.bounds = bounds;
            }

            // THE MESH GOES ON A CHILD, NOT ON THE LAYER ROOT. CityChunker.Bake combines every
            // renderer under a root and then DestroyImmediates the originals it consumed. Put the
            // renderer on the root itself and the bake destroys the root, and the very next line
            // of Bake reads it back - MissingReferenceException, Awake aborts half built, black
            // screen. CityStreets never hit this because its tiles are children.
            var holder = new GameObject("Lines");
            holder.transform.SetParent(go.transform, false);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = holder.AddComponent<MeshRenderer>();
            r.sharedMaterial = paint;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return n;
        }

        /// <summary>
        /// Dark on grass, and a different weight per class so the skeleton of the town reads
        /// before the detail does - the same argument CityOutlines makes for its road cyan.
        /// </summary>
        private static Color Colour(RoadClass klass)
        {
            switch (klass)
            {
                case RoadClass.Freeway:
                case RoadClass.Mainroad: return new Color(0.10f, 0.10f, 0.12f);   // Route 1, near black
                case RoadClass.Alley:    return new Color(0.55f, 0.42f, 0.28f);   // gravel brown
                case RoadClass.Track:    return new Color(0.45f, 0.40f, 0.30f);   // section roads
                default:                 return new Color(0.22f, 0.22f, 0.26f);   // streets
            }
        }

        /// <summary>Unlit and vertex-coloured, the same material argument CityOutlines makes:
        /// a plan reads the same at noon and at midnight.</summary>
        /// <summary>How wide a centreline reads on screen, in pixels, at any distance. Above
        /// CityOutlines' 2.5 for the same reason Lift is above theirs: a road crossing a lot
        /// boundary should read as the road.</summary>
        private const float WidthPixels = 3.0f;

        private static Material Paint(out bool screenSpace)
        {
            var line = Shader.Find("Noir/ScreenSpaceLine");
            screenSpace = line != null;
            if (screenSpace)
            {
                var pixels = new Material(line) { name = "Centreline Paint (pixel width)" };
                pixels.SetFloat("_WidthPixels", WidthPixels);
                pixels.SetColor("_Color", Color.white);
                pixels.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 60;
                return pixels;
            }

            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color");

            var m = new Material(shader) { name = "Centreline Paint" };
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 60;
            return m;
        }
    }
}
