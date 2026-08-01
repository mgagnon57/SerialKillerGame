using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// One flat quad between two ground points, wide enough to actually be visible - the shape
    /// every hand-built plan overlay in this project turns out to need, because a
    /// MeshTopology.Lines line has no width in URP and vanishes at any real distance.
    ///
    /// Pulled out once a fourth copy of it showed up (CityOutlines, SelectionHighlight,
    /// FootprintDrawer, AuthoredFootprints all wrote their own before this existed) rather than
    /// at the second - three near-identical fourteen-line methods read as "this is how you draw
    /// a ribbon here", which is exactly the kind of copy that quietly drifts the moment one of
    /// them needs a fix the other three don't get.
    /// </summary>
    public static class Ribbon
    {
        /// <summary>Uncoloured - for a mesh with one material and no per-vertex tint.</summary>
        public static void Edge(List<Vector3> verts, List<int> tris, Vector2 a, Vector2 b,
                                float stroke, float lift)
        {
            var along = b - a;
            float len = along.magnitude;
            if (len < 0.01f) return;

            var side = new Vector2(-along.y, along.x) / len * (stroke * 0.5f);
            int n = verts.Count;
            foreach (var p in new[] { a - side, b - side, b + side, a + side })
                verts.Add(Space3D.ToWorld(new Noir.Core.Contracts.Vec2(p.x, p.y), lift));

            tris.Add(n); tris.Add(n + 2); tris.Add(n + 1);
            tris.Add(n); tris.Add(n + 3); tris.Add(n + 2);
        }

        /// <summary>Coloured - for a mesh (CityOutlines) whose single material multiplies by
        /// per-vertex colour so several palettes can share one draw call.</summary>
        public static void Edge(List<Vector3> verts, List<Color> cols, List<int> tris,
                                Vector2 a, Vector2 b, Color colour, float stroke, float lift)
        {
            int before = verts.Count;
            Edge(verts, tris, a, b, stroke, lift);
            for (int i = before; i < verts.Count; i++) cols.Add(colour);
        }
    }
}
