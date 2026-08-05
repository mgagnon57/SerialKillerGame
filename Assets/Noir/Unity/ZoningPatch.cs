using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// The lots whose zoning you have CHANGED since the ground was baked, painted in their new
    /// colour so the map keeps up with the editor.
    ///
    /// WHY THIS EXISTS AT ALL. The ground is one baked mesh of five million tiles, sorted into a
    /// submesh per zoning once in Awake. That is the right shape for something which never
    /// changes and completely the wrong shape for something you are editing: rebuilding it to
    /// recolour one lot would stall the frame for seconds. So the baked ground stays as it was,
    /// and the handful of lots that no longer agree with it get a small patch drawn on top.
    ///
    /// It is empty on a fresh Play, because nothing disagrees yet - which is also why it needs no
    /// cleanup. The next time the town is built, the ground itself is right and this has nothing
    /// to draw.
    ///
    /// SIX MESHES, ONE PER ZONING, rather than one mesh with vertex colours. Vertex colour needs
    /// a shader that reads it, and the unlit fallbacks in this project do not reliably - six
    /// meshes with a flat colour each work under any of them, and six draw calls for a few lots
    /// is nothing next to being wrong about the colour.
    ///
    /// Rebuilt on ParcelNotes.Changed, the same division of labour as AuthoredFootprints and
    /// CityOutlines: a thing that changes rarely gets a mesh rebuilt rarely.
    /// </summary>
    public sealed class ZoningPatch : MonoBehaviour
    {
        /// <summary>
        /// Under the lot lines at 0.25, and above the ground.
        ///
        /// A patch that covered its own boundary would hide the very line you are checking it
        /// against. Ordering, lowest first: THIS 0.15, lot lines 0.25, roads 0.32, selection
        /// 0.36, hover 0.40.
        /// </summary>
        private const float Lift = 0.15f;

        private VillageHost _host;
        private readonly Dictionary<ParcelNotes.Zoning, MeshFilter> _filters =
            new Dictionary<ParcelNotes.Zoning, MeshFilter>();
        private readonly Dictionary<ParcelNotes.Zoning, MeshRenderer> _renderers =
            new Dictionary<ParcelNotes.Zoning, MeshRenderer>();

        /// <summary>
        /// What the BAKED ground is showing for each lot, captured once.
        ///
        /// Taken here rather than handed over by VillageMesh because it is the same question
        /// asked the same way - author overrules county - and nothing edits a note between the
        /// ground being built and this being created. A lot is patched only when its zoning now
        /// differs from what this remembers.
        /// </summary>
        private readonly Dictionary<int, ParcelNotes.Zoning> _baked =
            new Dictionary<int, ParcelNotes.Zoning>();

        public static ZoningPatch Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("ZoningPatch");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<ZoningPatch>();
            p._host = host;

            foreach (ParcelNotes.Zoning z in System.Enum.GetValues(typeof(ParcelNotes.Zoning)))
            {
                if (z == ParcelNotes.Zoning.Unset) continue;

                var child = new GameObject("Patch " + z);
                child.transform.SetParent(go.transform, false);
                p._filters[z] = child.AddComponent<MeshFilter>();

                var mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Paint(z);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.enabled = false;
                p._renderers[z] = mr;
            }

            p.RememberWhatWasBaked();
            ParcelNotes.Changed += p.Rebuild;
            return p;
        }

        private void OnDestroy() => ParcelNotes.Changed -= Rebuild;

        /// <summary>Which zonings currently have anything to draw. Kept so the visibility check
        /// below can turn the patches off without throwing the meshes away.</summary>
        private readonly HashSet<ParcelNotes.Zoning> _drawn = new HashSet<ParcelNotes.Zoning>();

        /// <summary>
        /// The patch follows the same switch the ground does.
        ///
        /// Z re-tints the baked materials and does NOT fire ParcelNotes.Changed - it is a view
        /// setting, not an edit - so this cannot be folded into Rebuild. A bool per frame against
        /// six renderers is beneath measurement, and the alternative is the patch still glowing
        /// over a plan somebody has just turned the colours off on.
        /// </summary>
        private void Update()
        {
            bool show = Materials3D.ShowZoningColours && VillageHost.FlatGroundColour;
            foreach (var pair in _renderers)
                pair.Value.enabled = show && _drawn.Contains(pair.Key);
        }

        private void RememberWhatWasBaked()
        {
            foreach (var parcel in ParcelIndex.All) _baked[parcel.Id] = ZoningOf(parcel.Id);
        }

        /// <summary>The rule VillageMesh.ZoningMask uses: what somebody authored, else what the
        /// county's class code says, else nothing.</summary>
        private static ParcelNotes.Zoning ZoningOf(int parcelId)
        {
            var note = ParcelNotes.For(parcelId);
            if (note != null && note.Zoning != ParcelNotes.Zoning.Unset) return note.Zoning;
            return CountyRecord.For(parcelId)?.Zoning ?? ParcelNotes.Zoning.Unset;
        }

        private void Rebuild()
        {
            var verts = new Dictionary<ParcelNotes.Zoning, List<Vector3>>();
            var tris = new Dictionary<ParcelNotes.Zoning, List<int>>();

            int changed = 0;
            foreach (var parcel in ParcelIndex.All)
            {
                var now = ZoningOf(parcel.Id);
                if (!_baked.TryGetValue(parcel.Id, out var was)) was = ParcelNotes.Zoning.Unset;
                if (now == was) continue;

                changed++;

                // A lot changed TO Unset has no colour of its own, and the baked ground under it
                // is still showing the old one. Nothing sensible to draw - the honest fix for
                // that is a rebuild, and it happens on the next Play.
                if (now == ParcelNotes.Zoning.Unset) continue;

                if (!verts.ContainsKey(now))
                {
                    verts[now] = new List<Vector3>();
                    tris[now] = new List<int>();
                }
                Fill(verts[now], tris[now], parcel.Points);
            }

            _drawn.Clear();
            foreach (var pair in _filters)
            {
                var z = pair.Key;
                bool any = verts.ContainsKey(z) && verts[z].Count > 0;
                if (any) _drawn.Add(z);
                if (!any) { pair.Value.sharedMesh = null; continue; }

                var mesh = pair.Value.sharedMesh;
                if (mesh == null)
                {
                    mesh = new Mesh { name = "ZoningPatch " + z };
                    pair.Value.sharedMesh = mesh;
                }
                mesh.Clear();
                mesh.SetVertices(verts[z]);
                mesh.SetTriangles(tris[z], 0);
                mesh.RecalculateBounds();
            }

            if (changed > 0)
                Debug.Log($"[zoning] {changed} lot(s) re-zoned since the ground was built - "
                        + "patched on top. The baked ground catches up on the next Play.");
        }

        /// <summary>
        /// A filled lot, as a fan from its own centroid.
        ///
        /// Good enough because a cadastral lot is a convex quadrilateral almost without
        /// exception - these are surveyed town plots, not coastlines. A fan over a genuinely
        /// concave one would spill outside its boundary, and the lot line drawn on top at 0.25
        /// would show it immediately.
        ///
        /// Every vertex is placed through Space3D so it sits on the real ground, the same as
        /// every other line on this plan; a flat quad across a lot is eaten by the first rise it
        /// crosses.
        /// </summary>
        private static void Fill(List<Vector3> verts, List<int> tris, Vector2[] points)
        {
            if (points == null || points.Length < 3) return;

            var centre = Vector2.zero;
            foreach (var p in points) centre += p;
            centre /= points.Length;

            int c = verts.Count;
            verts.Add(Space3D.ToWorld(new Noir.Core.Contracts.Vec2(centre.x, centre.y), Lift));
            foreach (var p in points)
                verts.Add(Space3D.ToWorld(new Noir.Core.Contracts.Vec2(p.x, p.y), Lift));

            for (int i = 0; i < points.Length; i++)
            {
                tris.Add(c);
                tris.Add(c + 1 + i);
                tris.Add(c + 1 + (i + 1) % points.Length);
            }
        }

        private static Material Paint(ParcelNotes.Zoning zoning)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Sprites/Default")
                      ?? Shader.Find("Unlit/Color");

            var m = new Material(shader) { name = "Zoning Patch " + zoning };
            var c = Materials3D.ColourOf(zoning);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);

            // Just above the ground and below everything drawn on it, matching the lift.
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 40;
            return m;
        }
    }
}
