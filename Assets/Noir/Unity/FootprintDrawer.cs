using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// Click-to-place house-shape drawing, for a parcel the player knows the real building on.
    ///
    /// Nothing here decides WHEN a click is a shape point rather than a selection - OrbitCamera
    /// checks <see cref="Active"/> first and routes here instead of through PlacePicker/
    /// ParcelIndex while it is true. This only owns the points, the live preview mesh, and
    /// handing the finished ring to ParcelNotes.
    /// </summary>
    public sealed class FootprintDrawer : MonoBehaviour
    {
        private const float Stroke = 1.2f;
        private const float Lift = 0.10f;      // above SelectionHighlight's 0.09
        private static readonly Color Amber = new Color(1.00f, 0.75f, 0.10f);

        private readonly List<Vector2> _points = new List<Vector2>();
        private MeshFilter _mf;
        private bool _dirty;

        public bool Active { get; private set; }
        public int TargetParcelId { get; private set; } = -1;
        public IReadOnlyList<Vector2> Points => _points;

        public static FootprintDrawer Create(Transform parent)
        {
            var go = new GameObject("FootprintDrawer");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<FootprintDrawer>();
            d._mf = go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Paint();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return d;
        }

        public void Begin(int parcelId, Vector2[] startFrom)
        {
            Active = true;
            TargetParcelId = parcelId;
            _points.Clear();
            if (startFrom != null) _points.AddRange(startFrom);
            _dirty = true;
        }

        public void AddPoint(Vector2 p) { _points.Add(p); _dirty = true; }

        public void UndoLast()
        {
            if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
            _dirty = true;
        }

        /// <summary>Saves the shape - three points or more, or nothing - without touching
        /// whatever household data already exists for this parcel, then exits draw mode.</summary>
        public void Finish(int parcelId)
        {
            ParcelNotes.SaveFootprint(parcelId, _points.Count >= 3 ? _points.ToArray() : null);
            Cancel();
        }

        public void Cancel()
        {
            Active = false;
            TargetParcelId = -1;
            _points.Clear();
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            _mf.sharedMesh = Active && _points.Count > 0 ? BuildMesh() : null;
        }

        private Mesh BuildMesh()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Every placed edge, plus a dashed-feeling final segment back to the start once
            // there is enough of a shape for "close" to mean anything.
            for (int i = 0; i < _points.Count - 1; i++)
                Edge(verts, tris, _points[i], _points[i + 1]);
            if (_points.Count >= 3)
                Edge(verts, tris, _points[_points.Count - 1], _points[0]);

            // A dot at every corner, so a single placed point is visible before there is a
            // second one to draw a line to.
            foreach (var p in _points) Dot(verts, tris, p);

            var mesh = new Mesh { name = "FootprintDrawer" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Dot(List<Vector3> verts, List<int> tris, Vector2 centre)
        {
            const float r = 0.9f;
            int n = verts.Count;
            verts.Add(Space3D.ToWorld(new Noir.Core.Contracts.Vec2(centre.x - r, centre.y - r), Lift));
            verts.Add(Space3D.ToWorld(new Noir.Core.Contracts.Vec2(centre.x + r, centre.y - r), Lift));
            verts.Add(Space3D.ToWorld(new Noir.Core.Contracts.Vec2(centre.x + r, centre.y + r), Lift));
            verts.Add(Space3D.ToWorld(new Noir.Core.Contracts.Vec2(centre.x - r, centre.y + r), Lift));
            tris.Add(n); tris.Add(n + 2); tris.Add(n + 1);
            tris.Add(n); tris.Add(n + 3); tris.Add(n + 2);
        }

        private static void Edge(List<Vector3> verts, List<int> tris, Vector2 a, Vector2 b) =>
            Ribbon.Edge(verts, tris, a, b, Stroke, Lift);

        private static Material Paint()
        {
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = "Footprint Draw Paint" };
            if (m.HasProperty("_Color")) m.SetColor("_Color", Amber);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Amber);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 65;
            return m;
        }
    }
}
