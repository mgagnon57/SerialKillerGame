using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// The village with its buildings taken away, so you can watch everybody at once.
    ///
    /// WHY. The one checkpoint this project has never run is "does it feel alive — you, following
    /// one villager through a whole day". It is hard to answer while the answer is behind a wall:
    /// the `street` instrument says only about 4% of the village is outdoors at any moment, so
    /// ninety-five people in a hundred are, correctly, invisible. That is the right way for the
    /// game to look and the wrong way for the author to look at it.
    ///
    /// So this is a LOOKING TOOL, not a game mode. It hides the roofs, walls, frontage, furniture
    /// and props, and replaces them with a wireframe — building footprints boxed at wall height,
    /// room footprints drawn on the floor. Everything else is untouched: the ground stays, the
    /// countryside stays, the sun and the lights stay, and every person carries on doing exactly
    /// what they were doing. Nothing here touches the simulation, so a run with X-ray on is
    /// tick-for-tick identical to one without it.
    ///
    /// Two lines rather than a transparent wall on purpose. A hundred overlapping transparent
    /// surfaces is a sorting problem and reads as fog; an outline reads as a plan, and a plan is
    /// what you want when the question is "who is in which room".
    /// </summary>
    public sealed class XRay : MonoBehaviour
    {
        /// <summary>The parts of the village that stand between the camera and the people.</summary>
        private static readonly string[] Hidden =
        {
            "Roofs", "Walls", "Furniture", "Props", "Frontage",
        };

        private Transform _village;
        private GameObject _lines;
        private readonly List<Renderer> _hidden = new List<Renderer>();

        public bool On { get; private set; }

        public static XRay Create(WorldModel world, GameObject village)
        {
            var xray = village.AddComponent<XRay>();
            xray._village = village.transform;
            xray._lines = BuildOutlines(world, village.transform);
            xray._lines.SetActive(false);

            // Collected once. Walking the hierarchy on every keypress would be fine, but the
            // list is also what guarantees we re-enable exactly what we disabled — including
            // anything that was already off for its own reasons, which a blanket "turn it all
            // back on" would silently switch on for the first time.
            // FOUND ANYWHERE UNDER THE VILLAGE, NOT ONLY AS ITS DIRECT CHILDREN.
            //
            // This used to be village.transform.Find(name), which looks one level down and no
            // further. The moment the generated massing was gathered under a "Dressing" root so
            // it could have a layer switch of its own, Walls, Roofs, Furniture, Props and
            // Frontage all moved a level - Find returned null for every one of them, nothing was
            // collected, and the x-ray hid nothing while reporting no error at all. The smoke
            // test caught it as "hid nothing: 14,284 -> 14,286": the only renderers it could
            // still account for were the two the wireframe itself adds.
            var wanted = new HashSet<string>(Hidden);
            var seen = new HashSet<Renderer>();
            foreach (var part in village.GetComponentsInChildren<Transform>(true))
            {
                if (!wanted.Contains(part.name)) continue;
                foreach (var r in part.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled && seen.Add(r)) xray._hidden.Add(r);
            }

            return xray;
        }

        public void Toggle() => Set(!On);

        public void Set(bool on)
        {
            if (_village == null) return;
            On = on;

            for (int i = 0; i < _hidden.Count; i++)
                if (_hidden[i] != null) _hidden[i].enabled = !on;

            if (_lines != null) _lines.SetActive(on);

            // The roof cutaway drives the same renderers from the camera every frame, so it has
            // to be told to stand down or it simply switches them back on.
            var roofs = _village.GetComponentInChildren<RoofBuilder>(true);
            if (roofs != null) roofs.Suppress(on);
        }

        // ---- the wireframe ----

        /// <summary>
        /// Where the building outline sits relative to the real wall. Half a centimetre out, so
        /// the line is not fighting the wall face for the same depth value when both are drawn —
        /// which is what makes a wireframe shimmer as the camera moves.
        /// </summary>
        private const float Skin = 0.005f;

        private static GameObject BuildOutlines(WorldModel world, Transform parent)
        {
            var root = new GameObject("X-Ray");
            root.transform.SetParent(parent, false);

            var buildings = new List<Vector3>();
            var rooms = new List<Vector3>();

            foreach (var place in world.AllPlaces)
            {
                // Open ground has no walls to outline, and boxing the green in a wireframe cage
                // would say the one thing that is not true about it.
                if (!PlaceKindTable.Current.Row(place.Kind).IsBuilding) continue;

                var b = place.Bounds;
                float x0 = b.X - Skin, x1 = b.X + b.W + Skin;
                float y0 = b.Y - Skin, y1 = b.Y + b.H + Skin;
                float top = MassingGrammars.Of(place).Eaves;

                Rect(buildings, x0, y0, x1, y1, 0f);
                Rect(buildings, x0, y0, x1, y1, top);

                // The four corner posts, which is what makes it read as a box rather than as two
                // unrelated rectangles floating one above the other.
                Post(buildings, x0, y0, top);
                Post(buildings, x1, y0, top);
                Post(buildings, x0, y1, top);
                Post(buildings, x1, y1, top);
            }

            // Rooms on the floor only. Boxing every room to full height turns a terrace into a
            // thicket you cannot see a person through, and the floor plan alone already answers
            // "which room is she in".
            foreach (var room in world.AllRooms)
            {
                var b = room.Bounds;
                Rect(rooms, b.X, b.Y, b.X + b.W, b.Y + b.H, 0.02f);
            }

            Emit(root.transform, "Building outlines", buildings, new Color(1f, 0.86f, 0.55f, 1f));
            Emit(root.transform, "Room outlines", rooms, new Color(0.45f, 0.62f, 0.85f, 1f));

            Debug.Log($"X-Ray: {buildings.Count / 2} building edges, {rooms.Count / 2} room edges.");
            return root;
        }

        private static void Rect(List<Vector3> into, float x0, float y0, float x1, float y1, float h)
        {
            var a = Space3D.ToWorld(x0, y0); a.y = h;
            var b = Space3D.ToWorld(x1, y0); b.y = h;
            var c = Space3D.ToWorld(x1, y1); c.y = h;
            var d = Space3D.ToWorld(x0, y1); d.y = h;

            into.Add(a); into.Add(b);
            into.Add(b); into.Add(c);
            into.Add(c); into.Add(d);
            into.Add(d); into.Add(a);
        }

        private static void Post(List<Vector3> into, float x, float y, float top)
        {
            var foot = Space3D.ToWorld(x, y);
            var head = foot; head.y = top;
            into.Add(foot);
            into.Add(head);
        }

        /// <summary>
        /// One line mesh. <see cref="MeshTopology.Lines"/> rather than thin quads: the lines want
        /// to stay one pixel wide at every distance, which is exactly what a wireframe is for and
        /// exactly what geometry would not do.
        /// </summary>
        private static void Emit(Transform parent, string name, List<Vector3> points, Color colour)
        {
            if (points.Count == 0) return;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var indices = new int[points.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;

            var mesh = new Mesh { name = name };
            if (points.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(points);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = LineMaterial(colour);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static Material LineMaterial(Color colour)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");

            var m = new Material(shader) { name = "X-Ray line" };
            m.SetColor("_BaseColor", colour);
            m.SetColor("_Color", colour);
            return m;
        }
    }
}
