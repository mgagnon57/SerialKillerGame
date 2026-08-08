using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Survey;

namespace Noir.Unity
{
    /// <summary>
    /// A bright green outline round whichever lot the cursor is over.
    ///
    /// Separate from SelectionHighlight, which traces the SELECTED plot: the two can be
    /// different lots at the same moment and both want to be visible - what you have open and
    /// what you are about to open. This one is brighter and thinner; selection is the heavier
    /// mark because it is the standing state.
    ///
    /// Traces the parcel's real POLYGON, not its bounding box. A Rossville lot is rarely square
    /// to the map - plenty are wedges left over from the railroad cutting the grid diagonally -
    /// and a rectangle round one of those highlights ground belonging to three other people.
    /// </summary>
    public sealed class HoverHighlight : MonoBehaviour
    {
        /// <summary>Pixels wide, when the pixel-width shader is available. See the fallback.</summary>
        private const float WidthPixels = 3.5f;

        /// <summary>Metres wide, when it is not.</summary>
        private const float Stroke = 1.2f;

        /// <summary>Above the lot lines and above the selection mark, so a hover always reads on
        /// top of whatever it is tracing over.</summary>
        private const float Lift = 0.40f;

        private static readonly Color Green = new Color(0.35f, 1.00f, 0.40f);

        private VillageHost _host;
        private MeshFilter _mf;
        private MeshRenderer _mr;
        private bool _screenSpace;

        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color> _cols = new List<Color>();
        private readonly List<Vector4> _tangents = new List<Vector4>();
        private readonly List<int> _tris = new List<int>();

        public static HoverHighlight Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("HoverHighlight");
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<HoverHighlight>();
            h._host = host;
            h._mf = go.AddComponent<MeshFilter>();

            h._mr = go.AddComponent<MeshRenderer>();
            h._mr.sharedMaterial = h.Paint(out h._screenSpace);
            h._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            h._mr.receiveShadows = false;
            return h;
        }

        private void LateUpdate()
        {
            var hovered = _host.HoveredParcel;

            // REBUILT EVERY FRAME, DELIBERATELY, and this used to be cached on the lot id.
            //
            // The cache was the bug: it skipped every other lot the cursor crossed. Whatever the
            // mechanism - a frame where the hover reads null between two neighbours, the
            // renderer being toggled from outside, an ordering difference between the camera
            // writing HoveredParcel in Update and this reading it in LateUpdate - the shape of
            // the fault is always the same. A cache keyed on "the id has not changed" is
            // claiming the DRAWN STATE matches the id, and nothing here can guarantee that.
            //
            // The cost of not caching it is one parcel outline: a couple of hundred metres of
            // perimeter at one segment per two metres, so a few hundred vertices, once a frame.
            // That is beneath measurement, and it cannot be wrong.
            _mr.enabled = hovered != null;
            if (hovered == null) return;

            _verts.Clear(); _cols.Clear(); _tangents.Clear(); _tris.Clear();

            // THE PROPERTY, NOT THE COUNTY'S PIECE OF IT - the rule CityOutlines draws the lot
            // lines by, SelectionHighlight marks by, and the browser map has always hovered by.
            // Pointing at one third of the grade school outlined that third, which contradicts
            // the merged lot lines drawn underneath it.
            foreach (int id in Rulings.OneProperty(hovered.Value.Id))
            {
                var lot = ParcelIndex.ById(id);
                if (lot == null) continue;
                var p = lot.Value.Points;
                for (int i = 0; i < p.Length; i++)
                {
                    var a = p[i];
                    var b = p[(i + 1) % p.Length];
                    if (ParcelIndex.SharedInsideOneProperty(a, b)) continue;
                    Edge(a, b);
                }
            }

            var mesh = _mf.sharedMesh;
            if (mesh == null) { mesh = new Mesh { name = "HoverHighlight" }; _mf.sharedMesh = mesh; }
            mesh.Clear();
            mesh.SetVertices(_verts);
            mesh.SetColors(_cols);
            if (_screenSpace) mesh.SetUVs(0, _tangents);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();

            // A pixel-width line has no thickness in the mesh, so a lot boundary seen edge on
            // would have flat bounds and get culled. Same guard CityOutlines needs.
            if (_screenSpace)
            {
                var b = mesh.bounds;
                b.Expand(2f);
                mesh.bounds = b;
            }

            _mr.enabled = true;
        }

        /// <summary>Subdivided along the ground for the same reason the lot lines are: a long
        /// boundary drawn as one flat quad is eaten by every rise it crosses.</summary>
        private void Edge(Vector2 a, Vector2 b)
        {
            const float every = 2f;
            float length = (b - a).magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / every));

            var previous = a;
            for (int i = 1; i <= steps; i++)
            {
                var next = Vector2.Lerp(a, b, i / (float)steps);
                if (_screenSpace)
                    Ribbon.ScreenEdge(_verts, _cols, _tangents, _tris, previous, next, Green, Lift);
                else
                    Ribbon.Edge(_verts, _cols, _tris, previous, next, Green, Stroke, Lift);
                previous = next;
            }
        }

        private Material Paint(out bool screenSpace)
        {
            var line = Shader.Find("Noir/ScreenSpaceLine");
            screenSpace = line != null;
            if (screenSpace)
            {
                var m = new Material(line) { name = "Hover Paint (pixel width)" };
                m.SetFloat("_WidthPixels", WidthPixels);
                m.SetColor("_Color", Color.white);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 70;
                return m;
            }

            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color");
            var fallback = new Material(shader) { name = "Hover Paint" };
            if (fallback.HasProperty("_Color")) fallback.SetColor("_Color", Color.white);
            if (fallback.HasProperty("_BaseColor")) fallback.SetColor("_BaseColor", Color.white);
            fallback.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 70;
            return fallback;
        }
    }
}
