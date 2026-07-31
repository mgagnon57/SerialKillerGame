using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Who has the right of way at every junction on the map, and the posts that say so.
    ///
    /// Signals used to be dressing. CityStreets dropped a Traffic_Light prefab on the corner of
    /// each crossing and nothing ever looked at it, so cars drove through red lights at speed -
    /// which for a game about finding out who was where at what time is not a small thing. The
    /// city's own traffic has to obey its own rules or the player cannot reason about it.
    ///
    /// SIGNALS ARE FOR THE TOWN AND NOTHING ELSE. Every one of the thirty-eight junctions used
    /// to be signalised, which is a hundred and fifty-two posts and a hundred and fifty-two
    /// real-time point lights, and only FOUR of those junctions are in the built-up area. The
    /// other thirty-four are crossroads in open farmland - two of them where the dirt track to
    /// the big barn meets a road, so there were traffic lights on the farm track with one head
    /// facing an empty paddock. Out there a junction now works the way a country junction
    /// actually works: the bigger road runs through and the smaller one gives way, said out
    /// loud by a stop sign rather than by a light. See <see cref="CitySigns"/>.
    ///
    /// Which junctions are in the town is not a list. THE TOWN'S OWN PAVEMENT DECIDES: if the
    /// paving reaches the corners of a junction, it is a street corner and gets lights. That
    /// answer follows the map, so when the downtown grows outward its new junctions signalise
    /// themselves and nothing here has to be edited.
    ///
    /// THE PACK'S LIGHT CANNOT CHANGE COLOUR. Its head is one mesh whose lens colours are baked
    /// into the atlas UVs, so it is a permanently red light: overriding either of its two glass
    /// submeshes does nothing, and overriding all of them turns the whole post that colour. So
    /// the bought post stays as the thing that reads as a traffic light, and the STATE is shown
    /// by a lens of our own mounted on its head - an unlit emissive sphere, plus a small point
    /// light so it still says something at three in the morning.
    ///
    /// Built OUTSIDE the node CityChunker bakes: a combined mesh cannot change colour.
    /// </summary>
    public sealed class CitySignals : MonoBehaviour
    {
        /// <summary>Seconds. A green long enough to clear a queue, an amber long enough to see.</summary>
        private const float Green = 14f, Amber = 3f, AllRed = 1f;

        private static float Cycle => (Green + Amber + AllRed) * 2f;

        /// <summary>
        /// How the traffic on the two arms of a junction is separated.
        /// </summary>
        public enum Control
        {
            /// <summary>A signal head on each approach, running the cycle below.</summary>
            Signals,

            /// <summary>
            /// One road runs through and the other gives way. What a country crossroads is.
            /// </summary>
            Priority,
        }

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
            public float Offset;         // where in the cycle this junction starts
            public Control Control;

            /// <summary>Priority junctions only: which axis has to give way.</summary>
            public bool GiveWayIsNorthSouth;
        }

        private readonly List<Node> _nodes = new List<Node>();
        private MaterialPropertyBlock _block;

        public int Count => _nodes.Count;
        public Vector2 Where(int node) => new Vector2(_nodes[node].X, _nodes[node].Y);
        public float Reach(int node) => _nodes[node].Reach;

        public Control ControlAt(int node) =>
            node < 0 || node >= _nodes.Count ? Control.Priority : _nodes[node].Control;

        public bool IsSignalised(int node) => ControlAt(node) == Control.Signals;

        /// <summary>
        /// At an unsignalised junction, must traffic on this axis give way to the other?
        ///
        /// False at a signalised one - there the signal decides, and asking this instead would
        /// give one axis a permanent free run through a red.
        /// </summary>
        public bool GivesWay(int node, bool northSouth)
        {
            if (node < 0 || node >= _nodes.Count) return false;
            var it = _nodes[node];
            return it.Control == Control.Priority && it.GiveWayIsNorthSouth == northSouth;
        }

        /// <summary>
        /// May traffic on this axis enter junction <paramref name="node"/> right now, as far as
        /// the SIGNAL is concerned?
        ///
        /// Amber counts as stop. A car already past the stop line has no stop line ahead of it,
        /// so it clears the junction regardless - which is what amber is for. Always true where
        /// there is no signal; whether such a junction may be entered is a question about the
        /// other traffic, and CityTraffic answers it.
        /// </summary>
        public bool MayEnter(int node, bool northSouth) =>
            !IsSignalised(node) || State(node, northSouth) == Light.Green;

        public Light State(int node, bool northSouth)
        {
            if (!IsSignalised(node)) return Light.Green;

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

        // ---- who has the right of way ---------------------------------------------------

        /// <summary>
        /// Is this junction a street corner rather than a crossroads in a field?
        ///
        /// Asked of the ground, not of a list of coordinates. The junction itself is road and
        /// so are the four arms leading out of it, so the informative samples are the DIAGONALS:
        /// the corners, which are pavement in a town and a wheatfield everywhere else. Two rings
        /// of them, because a junction whose pavement starts a few metres back is still a
        /// junction in a town.
        /// </summary>
        public static bool InTheTown(WorldModel world, Junction j)
        {
            foreach (float outward in new[] { j.Reach + 6f, j.Reach + 14f })
            foreach (int dx in new[] { -1, 1 })
            foreach (int dy in new[] { -1, 1 })
            {
                int x = Mathf.RoundToInt(j.X + dx * outward);
                int y = Mathf.RoundToInt(j.Y + dy * outward);
                if (x < 0 || y < 0 || x >= world.Width || y >= world.Height) continue;

                switch (world.Grid.TerrainAt(x, y))
                {
                    // Pavement, or a building standing right on the corner.
                    case Noir.Core.World.Terrain.Path:
                    case Noir.Core.World.Terrain.Floor:
                    case Noir.Core.World.Terrain.Wall:
                        if (!OutOfTown(world, x, y)) return true;
                        break;
                }
            }
            return false;
        }

        /// <summary>
        /// Is this piece of hard ground a farm's rather than a town's?
        ///
        /// NOT EVERY PATH IS A PAVEMENT. A farmyard is authored `ground path` because it is mud
        /// and hardcore rather than grass, and the first run of the town test read that as the
        /// town reaching the farm - so the junction where the track to the big barn meets First
        /// Street was signalised, and the farm gate got a set of traffic lights. Which is the
        /// exact fault the whole change was made to remove.
        /// </summary>
        private static bool OutOfTown(WorldModel world, int x, int y)
        {
            var id = world.Grid.PlaceAt(x, y);
            if (!id.IsValid) return false;

            var place = world.GetPlace(id);
            if (place == null) return false;

            switch (PlaceKindTable.Current.Row(place.Kind).Name)
            {
                case "farm": case "farmyard": case "barn": case "silo":
                case "cornfield": case "paddock": case "orchard": case "copse":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// At an unsignalised junction, which arm gives way.
        ///
        /// THE BIGGER ROAD RUNS THROUGH. RoadClass is ordered from track up to freeway, so this
        /// is a comparison rather than a table, and it gets the case that actually matters right
        /// on its own: the dirt track to the big barn gives way to the road it joins.
        ///
        /// Where the two are the same class there is no honest answer, so the tie goes to the
        /// east-west road and the north-south arms carry the stop signs. It is arbitrary, but it
        /// is arbitrary ON THE MAP as well as in here - CitySigns reads this same answer, so
        /// what the junction does and what the junction says can never drift apart.
        /// </summary>
        public static bool GiveWayAxisOf(Junction j) =>
            j.NorthSouth.Class <= j.EastWest.Class;      // true: north-south gives way

#if UNITY_EDITOR
        private void Erect(WorldModel world)
        {
            _block = new MaterialPropertyBlock();

            var post = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/polyperfect/Poly Universal Pack/Prefabs/City/TrafficLights City/"
                + "Traffic_Light_A_City.prefab");
            var lensMaterial = LensMaterial();

            int heads = 0, signalised = 0;
            for (int i = 0; i < world.Roads.Junctions.Count; i++)
            {
                var j = world.Roads.Junctions[i];
                bool town = InTheTown(world, j);

                // EVERY junction gets a node, signalised or not. LaneSegment.ToJunction indexes
                // world.Roads.Junctions directly, so skipping the country ones here would slide
                // every index after them by one and hand the traffic the wrong junction's state.
                var node = new Node
                {
                    X = j.X,
                    Y = j.Y,
                    Reach = j.Reach,
                    Control = town ? Control.Signals : Control.Priority,
                    GiveWayIsNorthSouth = GiveWayAxisOf(j),

                    // IN STEP, ALL OF THEM. These used to be scattered across the cycle so the
                    // city would not blink in unison, which sounded right and drove appallingly:
                    // with random offsets the next junction's colour is uncorrelated with this
                    // one's, so a car that just cleared a green had an even chance of a red
                    // ninety metres later and no way to earn a clear run.
                    //
                    // In step, the queue released by one green arrives at the next junction
                    // eleven seconds into ITS green and drives through. Blinking in unison was
                    // only ever a problem across a map of thirty-eight junctions; across the
                    // four in the town it is what a coordinated grid looks like.
                    //
                    // When downtown is deep enough for a car to pass four or five junctions in a
                    // row, this becomes a progression - offset by distance along the arterial
                    // over DesignSpeed - and that buys one direction a clear run at the expense
                    // of the other. Two junctions deep, it is not worth the trade.
                    Offset = 0f,
                };

                if (town)
                {
                    signalised++;

                    // One head per approach, on the corner to the RIGHT of the car arriving -
                    // which is the side it is read from, because this city drives on the right.
                    var approaches = new (Vector2 travel, bool northSouth)[]
                    {
                        (new Vector2( 0f, -1f), true),    // northbound: village -y
                        (new Vector2( 0f,  1f), true),    // southbound
                        (new Vector2( 1f,  0f), false),   // eastbound
                        (new Vector2(-1f,  0f), false),   // westbound
                    };

                    foreach (var (travel, northSouth) in approaches)
                    {
                        // Back down the approach to the stop line, then out to the right kerb.
                        var right = new Vector2(-travel.y, travel.x);
                        float back = j.Reach + 1.5f;
                        float side = j.Reach - 2.5f;

                        float hx = j.X - travel.x * back + right.x * side;
                        float hy = j.Y - travel.y * back + right.y * side;

                        var head = Mount(post, lensMaterial, hx, hy, travel, northSouth);
                        if (head != null) { node.Heads.Add(head); heads++; }
                    }
                }

                _nodes.Add(node);
            }

            // Light them once now. Update() only runs in Play, so without this every still
            // taken of the city shows four junctions of blank white bulbs.
            Refresh();

            Debug.Log($"[signals] {_nodes.Count} junctions: {signalised} signalised in the town "
                    + $"({heads} heads), {_nodes.Count - signalised} on priority out in the "
                    + $"country. {Green:0}s green / {Amber:0}s amber / {AllRed:0.0}s all-red "
                    + $"({Cycle:0.0}s cycle), all in step.");
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
