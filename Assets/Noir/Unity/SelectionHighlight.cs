using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// A green outline over whichever plot is currently selected.
    ///
    /// CityOutlines is one mesh, built once and baked for the whole town - exactly right for
    /// 794 lot lines that never change, and the wrong shape entirely for a selection that
    /// changes on every click. This is the other half: a single small mesh, rebuilt only when
    /// the selected place actually changes, drawn over the real county parcel under it rather
    /// than the small generated footprint - see VillageUI.LotSize for why those two differ.
    /// </summary>
    public sealed class SelectionHighlight : MonoBehaviour
    {
        /// <summary>Heavier than a lot line, so the selected one still reads as THE one.</summary>
        private const float Stroke = 1.6f;

        /// <summary>Above CityOutlines' own lines (0.06) so the highlight always wins the depth
        /// fight against the boundary it is tracing over.</summary>
        private const float Lift = 0.09f;

        private static readonly Color Green = new Color(0.30f, 1.00f, 0.35f);

        private VillageHost _host;
        private MeshFilter _mf;
        private PlaceId _shown = PlaceId.None;

        public static SelectionHighlight Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("SelectionHighlight");
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<SelectionHighlight>();
            h._host = host;
            h._mf = go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Paint();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return h;
        }

        private void Update()
        {
            var id = _host.SelectedPlace;
            if (id.Value == _shown.Value) return;
            _shown = id;

            var place = id.IsValid ? _host.World?.GetPlace(id) : null;
            _mf.sharedMesh = place != null ? BuildMesh(place) : null;
        }

        private static Mesh BuildMesh(Place place)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            var b = place.Bounds;
            var centre = new Vector2(b.X + b.W / 2f, b.Y + b.H / 2f);
            var parcel = ParcelIndex.Find(centre);

            if (parcel.HasValue)
            {
                var pts = parcel.Value.Points;
                for (int i = 0; i < pts.Length; i++)
                    Edge(verts, tris, pts[i], pts[(i + 1) % pts.Length]);
            }
            else
            {
                // No county record under this place - the footprint itself is the only
                // boundary there is, so trace that rather than showing no selection at all.
                Edge(verts, tris, new Vector2(b.X, b.Y), new Vector2(b.X + b.W, b.Y));
                Edge(verts, tris, new Vector2(b.X + b.W, b.Y), new Vector2(b.X + b.W, b.Y + b.H));
                Edge(verts, tris, new Vector2(b.X + b.W, b.Y + b.H), new Vector2(b.X, b.Y + b.H));
                Edge(verts, tris, new Vector2(b.X, b.Y + b.H), new Vector2(b.X, b.Y));
            }

            var mesh = new Mesh { name = "SelectionHighlight" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Edge(List<Vector3> verts, List<int> tris, Vector2 a, Vector2 b)
        {
            var along = b - a;
            float len = along.magnitude;
            if (len < 0.01f) return;

            var side = new Vector2(-along.y, along.x) / len * (Stroke * 0.5f);
            int n = verts.Count;
            foreach (var p in new[] { a - side, b - side, b + side, a + side })
                verts.Add(Space3D.ToWorld(new Vec2(p.x, p.y), Lift));

            tris.Add(n); tris.Add(n + 2); tris.Add(n + 1);
            tris.Add(n); tris.Add(n + 3); tris.Add(n + 2);
        }

        private static Material Paint()
        {
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color");

            var m = new Material(shader) { name = "Selection Paint" };
            if (m.HasProperty("_Color")) m.SetColor("_Color", Green);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Green);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 60;
            return m;
        }
    }
}
