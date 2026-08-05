using System;
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

        /// <summary>Pixels wide when the pixel-width shader is there. Heavier than the hover's
        /// 3.5: a selection is the standing state and should outweigh the thing you are merely
        /// pointing at.</summary>
        private const float WidthPixels = 5.0f;

        /// <summary>
        /// Above the lot lines, and this WAS 0.09 AND BROKEN.
        ///
        /// 0.09 cleared CityOutlines when its own lift was 0.06. That lift went to 0.25 when the
        /// lot lines were made to follow the contour, and nothing brought this with it - so the
        /// selection mark spent the evening drawing UNDERNEATH the boundary it was tracing, and
        /// clicking a lot appeared to do almost nothing. Reported as "when clicking I should
        /// make more clear", which it was: it was buried.
        ///
        /// Ordering now, lowest first: lot lines 0.25, roads 0.32, THIS 0.36, hover 0.40.
        /// </summary>
        private const float Lift = 0.36f;

        /// <summary>
        /// AMBER, not green, and that is the point.
        ///
        /// This was the same green as HoverHighlight to within a few hundredths, so the lot you
        /// had open and the lot you were pointing at were the same colour and told you nothing
        /// apart. Green means "about to", amber means "is".
        /// </summary>
        private static readonly Color Green = new Color(1.00f, 0.72f, 0.20f);

        private VillageHost _host;
        private MeshFilter _mf;
        private PlaceId _shownPlace = PlaceId.None;
        private Rect? _shownParcel;

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
            var parcel = _host.SelectedParcel;

            if (id.Value == _shownPlace.Value
                && Nullable.Equals(BoundsOf(parcel), _shownParcel)) return;

            _shownPlace = id;
            _shownParcel = BoundsOf(parcel);

            var place = id.IsValid ? _host.World?.GetPlace(id) : null;
            if (place != null) { _mf.sharedMesh = BuildMesh(place); return; }
            _mf.sharedMesh = parcel.HasValue ? BuildMesh(parcel.Value) : null;
        }

        private static Rect? BoundsOf(ParcelIndex.Parcel? p) => p?.Bounds;

        private static Mesh BuildMesh(Place place)
        {
            var b = place.Bounds;
            var parcel = ParcelIndex.FindFor(place);
            if (parcel.HasValue) return BuildMesh(parcel.Value);

            // No county record under this place - the footprint itself is the only boundary
            // there is, so trace that rather than showing no selection at all.
            var verts = new List<Vector3>();
            var tris = new List<int>();
            _cols.Clear(); _tangents.Clear();
            Edge(verts, tris, new Vector2(b.X, b.Y), new Vector2(b.X + b.W, b.Y));
            Edge(verts, tris, new Vector2(b.X + b.W, b.Y), new Vector2(b.X + b.W, b.Y + b.H));
            Edge(verts, tris, new Vector2(b.X + b.W, b.Y + b.H), new Vector2(b.X, b.Y + b.H));
            Edge(verts, tris, new Vector2(b.X, b.Y + b.H), new Vector2(b.X, b.Y));
            return Finish(verts, tris);
        }

        private static Mesh BuildMesh(ParcelIndex.Parcel parcel)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            _cols.Clear(); _tangents.Clear();
            var pts = parcel.Points;
            for (int i = 0; i < pts.Length; i++)
                Edge(verts, tris, pts[i], pts[(i + 1) % pts.Length]);
            return Finish(verts, tris);
        }

        private static Mesh Finish(List<Vector3> verts, List<int> tris)
        {
            var mesh = new Mesh { name = "SelectionHighlight" };
            mesh.SetVertices(verts);
            if (_screenSpace)
            {
                mesh.SetColors(_cols);
                mesh.SetUVs(0, _tangents);
            }
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            // Zero-thickness mesh seen edge on would be culled. Same guard as the lot lines.
            if (_screenSpace)
            {
                var b = mesh.bounds;
                b.Expand(2f);
                mesh.bounds = b;
            }
            return mesh;
        }

        /// <summary>Subdivided at 2 m so it follows the ground, like every other line on this
        /// plan - one flat quad across a long boundary is eaten by the first rise it crosses.</summary>
        private static void Edge(List<Vector3> verts, List<int> tris, Vector2 a, Vector2 b)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / 2f));
            var previous = a;
            for (int i = 1; i <= steps; i++)
            {
                var next = Vector2.Lerp(a, b, i / (float)steps);
                if (_screenSpace) Ribbon.ScreenEdge(verts, _cols, _tangents, tris, previous, next, Green, Lift);
                else Ribbon.Edge(verts, tris, previous, next, Stroke, Lift);
                previous = next;
            }
        }

        private static readonly List<Color> _cols = new List<Color>();
        private static readonly List<Vector4> _tangents = new List<Vector4>();

        /// <summary>True when the pixel-width shader bound, so the mesh carries tangents.</summary>
        private static bool _screenSpace;

        private static Material Paint()
        {
            var line = Shader.Find("Noir/ScreenSpaceLine");
            _screenSpace = line != null;
            if (_screenSpace)
            {
                var pixels = new Material(line) { name = "Selection Paint (pixel width)" };
                pixels.SetFloat("_WidthPixels", WidthPixels);
                pixels.SetColor("_Color", Color.white);
                pixels.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 60;
                return pixels;
            }

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
