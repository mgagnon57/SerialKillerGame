using System;
using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// AN AMBER OUTLINE ROUND WHICHEVER LOT IS SELECTED - the one you have clicked and have
    /// open in the panel. The green one is HoverHighlight, which traces whatever the mouse is
    /// merely pointing at: green means "about to", amber means "is".
    ///
    /// CityOutlines is one mesh, built once and baked for the whole town - exactly right for
    /// 794 lot lines that never change, and the wrong shape entirely for a selection that
    /// changes on every click. This is the other half: a single small mesh, refilled each
    /// frame, drawn over the real county parcel under it rather than the small generated
    /// footprint - see VillageUI.LotSize for why those two differ.
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
        private static readonly Color Amber = new Color(1.00f, 0.72f, 0.20f);

        private VillageHost _host;
        private MeshFilter _mf;
        private MeshRenderer _mr;

        /// <summary>ONE mesh, refilled. Not a new one per selection - see Commit.</summary>
        private Mesh _mesh;

        public static SelectionHighlight Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("SelectionHighlight");
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<SelectionHighlight>();
            h._host = host;
            h._mf = go.AddComponent<MeshFilter>();

            h._mr = go.AddComponent<MeshRenderer>();
            h._mr.sharedMaterial = Paint();
            h._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            h._mr.receiveShadows = false;
            return h;
        }

        /// <summary>
        /// LATEUPDATE, NOT UPDATE, and rebuilt every frame rather than cached. Both of those were
        /// wrong and for the reasons HoverHighlight already carries in its own comment.
        ///
        /// ORDER: OrbitCamera.HandleSelection writes SelectedPlace and SelectedParcel in Update.
        /// This read them in Update too, and Unity does not define which MonoBehaviour's Update
        /// runs first - so on any frame where this one won, the outline traced the lot you had
        /// selected BEFORE the click. A mark that is sometimes a frame behind the thing it marks
        /// is exactly "it is not syncing up". LateUpdate always runs after every Update, so there
        /// is no order left to get wrong.
        ///
        /// CACHE: it used to skip the rebuild when the selected place id and the parcel's
        /// BOUNDING RECTANGLE were unchanged - while the outline it draws is the parcel's
        /// POLYGON. A key narrower than the thing it guards is a cache that can be right about
        /// its key and wrong about the screen, which is the same fault that made the hover mark
        /// skip every other lot. The cost of not caching is one lot perimeter of vertices a
        /// frame, which is beneath measurement, and it cannot be wrong.
        /// </summary>
        private void LateUpdate()
        {
            var id = _host.SelectedPlace;
            var parcel = _host.SelectedParcel;

            var place = id.IsValid ? _host.World?.GetPlace(id) : null;
            if (place != null) { BuildMesh(place); _mr.enabled = true; return; }
            if (parcel.HasValue) { BuildMesh(parcel.Value); _mr.enabled = true; return; }

            // Nothing selected. The renderer is switched off rather than handed an empty mesh,
            // so there is no frame in which a stale outline is still on screen.
            _mr.enabled = false;
        }

        private void BuildMesh(Place place)
        {
            var b = place.Bounds;
            var parcel = ParcelIndex.FindFor(place);
            if (parcel.HasValue) { BuildMesh(parcel.Value); return; }

            // No county record under this place - the footprint itself is the only boundary
            // there is, so trace that rather than showing no selection at all.
            var verts = new List<Vector3>();
            var tris = new List<int>();
            _cols.Clear(); _tangents.Clear();
            Edge(verts, tris, new Vector2(b.X, b.Y), new Vector2(b.X + b.W, b.Y));
            Edge(verts, tris, new Vector2(b.X + b.W, b.Y), new Vector2(b.X + b.W, b.Y + b.H));
            Edge(verts, tris, new Vector2(b.X + b.W, b.Y + b.H), new Vector2(b.X, b.Y + b.H));
            Edge(verts, tris, new Vector2(b.X, b.Y + b.H), new Vector2(b.X, b.Y));
            Commit(verts, tris);
        }

        /// <summary>
        /// Outline the PROPERTY, not the county's piece of it.
        ///
        /// Clicking any one of the grade school's three lots marks the school. The splits between
        /// them are left out by the same rule CityOutlines draws by, so the mark lands exactly on
        /// the lot lines that are actually on screen rather than cutting across the middle of a
        /// building. An ungrouped lot is its own property and takes the identical path - there is
        /// no separate single-lot case to keep in step.
        /// </summary>
        private void BuildMesh(ParcelIndex.Parcel parcel)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            _cols.Clear(); _tangents.Clear();

            foreach (int id in Rulings.OneProperty(parcel.Id))
            {
                var lot = ParcelIndex.ById(id);
                if (lot == null) continue;
                var pts = lot.Value.Points;
                for (int i = 0; i < pts.Length; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Length];
                    if (ParcelIndex.SharedInsideOneProperty(a, b)) continue;
                    Edge(verts, tris, a, b);
                }
            }

            // A property whose lots all resolved to nothing would leave no mark at all, which
            // reads as a click that missed. Trace what was actually clicked instead.
            if (verts.Count == 0)
            {
                var pts = parcel.Points;
                for (int i = 0; i < pts.Length; i++)
                    Edge(verts, tris, pts[i], pts[(i + 1) % pts.Length]);
            }

            Commit(verts, tris);
        }

        /// <summary>
        /// Fill THE mesh, rather than make another one.
        ///
        /// This used to `new Mesh(...)` on every selection change and hand it to the MeshFilter,
        /// leaving the previous one unreferenced and undestroyed - a Unity object leak of one
        /// mesh per click, which nothing collects because Unity meshes are not managed by the GC.
        /// Now that the rebuild happens every frame instead of on change, allocating here would
        /// have leaked sixty a second. HoverHighlight already reuses its mesh; this is the same.
        /// </summary>
        private void Commit(List<Vector3> verts, List<int> tris)
        {
            var mesh = _mesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = "SelectionHighlight" };
                _mesh = mesh;
                _mf.sharedMesh = mesh;
            }
            mesh.Clear();
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
                if (_screenSpace) Ribbon.ScreenEdge(verts, _cols, _tangents, tris, previous, next, Amber, Lift);
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
            if (m.HasProperty("_Color")) m.SetColor("_Color", Amber);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Amber);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 60;
            return m;
        }
    }
}
