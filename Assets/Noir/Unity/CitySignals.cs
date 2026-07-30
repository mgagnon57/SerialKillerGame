using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// The traffic signals: a post on every approach to every junction, and a phase that cycles.
    ///
    /// Signals used to be dressing. CityStreets dropped a Traffic_Light prefab on the corner of
    /// each crossing and nothing ever looked at it, so cars drove through red lights at speed -
    /// which for a game about finding out who was where at what time is not a small thing. The
    /// city's own traffic has to obey its own rules or the player cannot reason about it.
    ///
    /// THE PACK'S LIGHT CANNOT CHANGE COLOUR. Its head is one mesh whose lens colours are baked
    /// into the atlas UVs, so it is a permanently red light: overriding either of its two glass
    /// submeshes does nothing, and overriding all of them turns the whole post that colour. So
    /// the bought post stays as the thing that reads as a traffic light, and the STATE is shown
    /// by a lens of our own mounted on its head - an unlit emissive sphere, plus a small point
    /// light so it still says something at three in the morning.
    ///
    /// The cycle is north-south, then east-west, with an amber and a moment of all-red between.
    /// Every junction runs the same cycle offset by its position, so the city does not blink in
    /// unison.
    ///
    /// Built OUTSIDE the node CityChunker bakes: a combined mesh cannot change colour.
    /// </summary>
    public sealed class CitySignals : MonoBehaviour
    {
        /// <summary>Seconds. A green long enough to clear a queue, an amber long enough to see.</summary>
        private const float Green = 14f, Amber = 3f, AllRed = 1.5f;

        private static float Cycle => (Green + Amber + AllRed) * 2f;

        public enum Light { Red, Amber, Green }

        private static readonly Color RedLens   = new Color(3.0f, 0.10f, 0.06f);
        private static readonly Color AmberLens = new Color(3.0f, 1.30f, 0.05f);
        private static readonly Color GreenLens = new Color(0.10f, 2.60f, 0.30f);

        private sealed class Head
        {
            public Renderer Lens;
            public UnityEngine.Light Lamp;
            public bool NorthSouth;      // which flow this head governs
            public Light Showing = (Light)(-1);
        }

        private sealed class Node
        {
            public float X, Y, Reach;
            public readonly List<Head> Heads = new List<Head>();
            public float Offset;         // so junctions are not all in step
        }

        private readonly List<Node> _nodes = new List<Node>();
        private MaterialPropertyBlock _block;

        public int Count => _nodes.Count;
        public Vector2 Where(int node) => new Vector2(_nodes[node].X, _nodes[node].Y);
        public float Reach(int node) => _nodes[node].Reach;

        /// <summary>
        /// May traffic on this axis enter junction <paramref name="node"/> right now?
        ///
        /// Amber counts as stop. A car already past the stop line has no stop line ahead of it,
        /// so it clears the junction regardless - which is what amber is for.
        /// </summary>
        public bool MayEnter(int node, bool northSouth) =>
            State(node, northSouth) == Light.Green;

        public Light State(int node, bool northSouth)
        {
            if (node < 0 || node >= _nodes.Count) return Light.Green;

            float t = Mathf.Repeat(Time.time + _nodes[node].Offset, Cycle);
            float half = Green + Amber + AllRed;

            // First half of the cycle belongs to north-south, second half to east-west.
            bool firstHalf = t < half;
            float within = firstHalf ? t : t - half;
            bool mine = northSouth == firstHalf;

            if (!mine) return Light.Red;
            if (within < Green) return Light.Green;
            if (within < Green + Amber) return Light.Amber;
            return Light.Red;
        }

        public static CitySignals Create(WorldModel world, Transform parent)
        {
            var go = new GameObject("CitySignals");
            go.transform.SetParent(parent, false);
            var signals = go.AddComponent<CitySignals>();
#if UNITY_EDITOR
            signals.Erect(world);
#endif
            return signals;
        }

#if UNITY_EDITOR
        private void Erect(WorldModel world)
        {
            _block = new MaterialPropertyBlock();

            var post = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/polyperfect/Poly Universal Pack/Prefabs/City/TrafficLights City/"
                + "Traffic_Light_A_City.prefab");
            var lensMaterial = LensMaterial();

            int heads = 0;
            for (int i = 0; i < world.Roads.Junctions.Count; i++)
            {
                var j = world.Roads.Junctions[i];
                var node = new Node
                {
                    X = j.X,
                    Y = j.Y,
                    Reach = j.Reach,
                    // Deterministic, and spread across the cycle by position.
                    Offset = Materials3D.Scatter(Mathf.RoundToInt(j.X), Mathf.RoundToInt(j.Y), 977)
                             % 1000 / 1000f * Cycle,
                };

                // One head per approach, on the corner to the RIGHT of the car arriving - which
                // is the side it is read from, because this city drives on the right.
                var approaches = new (Vector2 travel, bool northSouth)[]
                {
                    (new Vector2( 0f, -1f), true),    // northbound: village -y
                    (new Vector2( 0f,  1f), true),    // southbound
                    (new Vector2( 1f,  0f), false),   // eastbound
                    (new Vector2(-1f,  0f), false),   // westbound
                };

                foreach (var (travel, northSouth) in approaches)
                {
                    // Back down the approach to the stop line, then out to the right-hand kerb.
                    var right = new Vector2(-travel.y, travel.x);   // village-space right of travel
                    float back = j.Reach + 1.5f;
                    float side = j.Reach - 2.5f;

                    float hx = j.X - travel.x * back + right.x * side;
                    float hy = j.Y - travel.y * back + right.y * side;

                    var head = Mount(post, lensMaterial, hx, hy, travel, northSouth);
                    if (head != null) { node.Heads.Add(head); heads++; }
                }

                _nodes.Add(node);
            }

            // Light them once now. Update() only runs in Play, so without this every still
            // taken of the city shows four junctions of blank white bulbs.
            Refresh();

            Debug.Log($"[signals] {_nodes.Count} junctions, {heads} heads, "
                    + $"{Green:0}s green / {Amber:0}s amber / {AllRed:0.0}s all-red "
                    + $"({Cycle:0.0}s cycle).");
        }

        /// <summary>
        /// One post and its lens.
        ///
        /// The lens is seated on the MEASURED top of the bought head rather than at a typed
        /// height, because the three Traffic_Light variants are different heights and a number
        /// picked off one of them floats above the other two.
        /// </summary>
        private Head Mount(GameObject post, Material lensMaterial,
                           float vx, float vy, Vector2 travel, bool northSouth)
        {
            if (post == null) { Debug.LogWarning("[signals] no traffic light prefab"); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(post);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(vx, 0f, -vy);

            // Face the car that is arriving. The head's lens faces +z at rest, and a car
            // travelling `travel` in village space moves along (travel.x, -travel.y) in Unity,
            // so the head must look back down that.
            var facing = new Vector3(-travel.x, 0f, travel.y);
            go.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);

            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return null;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lens.name = "Lens_Emission";       // 'Emission' keeps CityChunker's hands off it
            Object.DestroyImmediate(lens.GetComponent<Collider>());
            lens.transform.SetParent(go.transform, false);
            lens.transform.localScale = Vector3.one * 0.3f;

            // On the face of the head, measured: the top of the post less a tenth of its height
            // is the lamp cluster, and half the post's depth puts the lens proud of the front
            // of it rather than buried inside. Typing a height instead would only fit whichever
            // of the three Traffic_Light variants it was measured from.
            float depth = Mathf.Max(b.size.x, b.size.z);
            lens.transform.position =
                new Vector3(b.center.x, b.max.y - b.size.y * 0.10f, b.center.z)
                + facing * (depth * 0.5f + 0.05f);

            var lr = lens.GetComponent<Renderer>();
            lr.sharedMaterial = lensMaterial;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lampGo = new GameObject("Lamp");
            lampGo.transform.SetParent(lens.transform, false);
            var lamp = lampGo.AddComponent<UnityEngine.Light>();
            lamp.type = LightType.Point;
            lamp.range = 9f;
            lamp.intensity = 1.6f;
            lamp.shadows = LightShadows.None;

            return new Head { Lens = lr, Lamp = lamp, NorthSouth = northSouth };
        }

        /// <summary>
        /// An unlit material for the lens, so it reads as lit in daylight too.
        ///
        /// Unlit rather than emissive-Lit: at noon a Lit surface with emission is washed out by
        /// the sun and the signal cannot be read, which defeats the point of showing it.
        /// </summary>
        private static Material LensMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color");
            return new Material(shader) { name = "M_Signal_Lens_Emission" };
        }
#endif

        private void Update() => Refresh();

        /// <summary>Bring every head into line with the phase its junction is in.</summary>
        private void Refresh()
        {
            if (_block == null) _block = new MaterialPropertyBlock();

            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                for (int h = 0; h < node.Heads.Count; h++)
                {
                    var head = node.Heads[h];
                    var want = State(i, head.NorthSouth);
                    if (want == head.Showing) continue;      // only touch it when it changes
                    head.Showing = want;

                    var colour = want == Light.Green ? GreenLens
                               : want == Light.Amber ? AmberLens
                               : RedLens;

                    if (head.Lens != null)
                    {
                        _block.SetColor("_BaseColor", colour);
                        _block.SetColor("_Color", colour);
                        head.Lens.SetPropertyBlock(_block);
                    }
                    if (head.Lamp != null) head.Lamp.color = colour;
                }
            }
        }
    }
}
