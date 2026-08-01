using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// Every hand-drawn house shape, all at once - the plan's memory of what somebody actually
    /// told it, next to the county's own anonymous lot lines.
    ///
    /// One mesh, rebuilt only when ParcelNotes.Changed fires rather than every frame, the same
    /// division of labour as CityOutlines/SelectionHighlight: a thing that changes rarely gets a
    /// mesh that is rebuilt rarely.
    /// </summary>
    public sealed class AuthoredFootprints : MonoBehaviour
    {
        private const float Stroke = 1.3f;
        private const float Lift = 0.08f;      // between CityOutlines (0.06) and the highlight (0.09)
        private static readonly Color Rose = new Color(0.95f, 0.55f, 0.65f);

        private MeshFilter _mf;

        public static AuthoredFootprints Create(Transform parent)
        {
            var go = new GameObject("AuthoredFootprints");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AuthoredFootprints>();
            a._mf = go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Paint();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            a.Rebuild();
            ParcelNotes.Changed += a.Rebuild;
            return a;
        }

        private void OnDestroy() => ParcelNotes.Changed -= Rebuild;

        private void Rebuild()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            foreach (var note in ParcelNotes.All.Values)
            {
                var pts = note.Footprint;
                if (pts == null || pts.Length < 3) continue;
                for (int i = 0; i < pts.Length; i++)
                    Edge(verts, tris, pts[i], pts[(i + 1) % pts.Length]);
            }

            if (verts.Count == 0) { _mf.sharedMesh = null; return; }

            var mesh = new Mesh { name = "AuthoredFootprints" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            _mf.sharedMesh = mesh;
        }

        private static void Edge(List<Vector3> verts, List<int> tris, Vector2 a, Vector2 b) =>
            Ribbon.Edge(verts, tris, a, b, Stroke, Lift);

        private static Material Paint()
        {
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = "Authored Footprint Paint" };
            if (m.HasProperty("_Color")) m.SetColor("_Color", Rose);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Rose);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 55;
            return m;
        }
    }
}
