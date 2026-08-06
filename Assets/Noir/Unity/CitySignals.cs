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
    /// SIGNALS ARE FOR THE TOWN AND NOTHING ELSE. Every junction on the map used to be
    /// signalised, including the crossroads in open farmland - two of them where the dirt track
    /// to the big barn meets a road, so there were traffic lights on the farm track with one
    /// head facing an empty paddock. Out there a junction now works the way a country junction
    /// actually works: the bigger road runs through and the smaller one gives way, said out
    /// loud by a stop sign rather than by a light. See <see cref="CitySigns"/>.
    ///
    /// Which junctions are in the town is not a list. THE TOWN'S OWN PAVEMENT DECIDES: if the
    /// paving reaches the corners of a junction, it is a street corner and gets lights. That
    /// answer follows the map, so when the downtown grows outward its new junctions signalise
    /// themselves and nothing here has to be edited.
    ///
    /// THE PACK'S LIGHT CAN CHANGE COLOUR AFTER ALL, and everything this file used to say about
    /// that was wrong because it had been said about the wrong prefab. Traffic_Light_A_City is
    /// the HEAD - one metre of it, y -0.05..1.09, meant to be mounted on something - and it was
    /// being stood on the pavement with a primitive sphere glued to the top to show the state.
    /// A signal head lying on the ground with a glowing ball on it.
    ///
    /// Light_A_City is the actual signal: six metres of mast, a four-metre arm out over the
    /// carriageway, and its three lenses on THEIR OWN SUBMESHES with their own material slots.
    /// So the state is shown by lighting the pack's own lamps - one lit, two dark, which is what
    /// a signal is and is something a single sphere could never show. The earlier conclusion
    /// that the glass could not be driven came from tinting the SHARED material, which is the
    /// same glass every window in Northgate uses; the fix is a material of our own in the three
    /// lens slots, not a different prefab.
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

        /// <summary>A lens that is not showing. Dark glass, not black - it still catches the sun.</summary>
        private static readonly Color DeadLens = new Color(0.06f, 0.06f, 0.07f);

        private sealed class Head
        {
            public Renderer Lamps;

            /// <summary>
            /// The submesh index of each lens on <see cref="Lamps"/>, found by height: on a
            /// signal the red is on top, the amber in the middle and the green at the bottom.
            /// Found rather than typed, because the three Light_ variants order their submeshes
            /// differently and a number read off one of them is wrong on the other two.
            /// </summary>
            public int Red = -1, Amber = -1, Green = -1;

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
        /// <remarks>
        /// CARRIES, NOT CLASS. Which road runs through is about what the roads DO, and on the
        /// surveyed network no road is paved to a main road's corridor - Attica is a county
        /// highway on a village street's right of way. Comparing the corridor made every road in
        /// town equal, so the tie below fired at all 64 junctions at once and every north-south
        /// arm in Rossville gave way permanently to an east-west stream that never yielded back.
        /// Measured: nine tenths of stopped vehicles waited 119.9 s with clear road ahead.
        /// </remarks>
        public static bool GiveWayAxisOf(Junction j) =>
            j.NorthSouth.Carries <= j.EastWest.Carries;  // true: north-south gives way

#if UNITY_EDITOR
        private void Erect(WorldModel world)
        {
            _block = new MaterialPropertyBlock();

            var post = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/polyperfect/Poly Universal Pack/Prefabs/City/TrafficLights City/"
                + "Light_A_City.prefab");
            var lensMaterial = LensMaterial();

            int heads = 0, signalised = 0;
            for (int i = 0; i < world.Roads.Junctions.Count; i++)
            {
                var j = world.Roads.Junctions[i];

                // A LIGHT WHERE TWO MAIN ROADS CROSS, AND A STOP SIGN EVERYWHERE ELSE.
                //
                // "In the town" on its own is the rule for a downtown grid, and it is the wrong
                // rule for a village: it signalises every corner whose diagonal happens to hold
                // a shopfront, so a place with one junction that deserves lights got eight. A
                // village has ONE set, where the route through town meets the cross street, and
                // the rest are stop signs on the smaller arm - which is what GiveWayAxisOf below
                // already works out and what CitySigns already erects.
                //
                // Both arms have to be a main road. That makes the answer follow the map: class
                // a second street up to Mainroad in city.txt and it earns its own lights, with
                // nothing here to change.
                // Asked of what the arms CARRY rather than what they are paved to, for the reason
                // GiveWayAxisOf gives: on the surveyed network nothing is paved to a main road's
                // corridor, so reading Class here signalised nothing at all and the town lost the
                // one set of lights it had.
                bool crossroads = j.NorthSouth.Carries >= RoadClass.Mainroad
                               && j.EastWest.Carries >= RoadClass.Mainroad;
                bool town = crossroads && InTheTown(world, j);

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
                        // ON THE FAR CORNER, NOT THE NEAR ONE. A mast-arm signal stands
                        // BEYOND the junction with its arm reaching back across it, which is
                        // the whole reason the arm exists: a driver held at the stop line looks
                        // across the junction and up at a head thirty metres in front of them.
                        // Put on the near corner - which is where these were - the head hangs
                        // directly over the car waiting at the line, and the one person who
                        // most needs to read it cannot see it at all.
                        // The kerb the mast stands on follows the head's facing - see
                        // LensFacesPlusX in Mount. Turn the head and the arm turns with it, so
                        // the mast has to cross the road too or the arm reaches out over the
                        // pavement and the lamps hang above the shopfronts.
                        var kerb = new Vector2(travel.y, -travel.x);
                        float beyond = j.Reach + 2.5f;
                        float side = j.Reach - 2.5f;

                        float hx = j.X + travel.x * beyond + kerb.x * side;
                        float hy = j.Y + travel.y * beyond + kerb.y * side;

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
        /// One signal, standing on the kerb with its arm out over the carriageway.
        ///
        /// IT WAS THE WRONG PREFAB, AND IT HAD NO POST. Traffic_Light_A_City measures
        /// y -0.05..1.09 - it is the HEAD, one metre of it, meant to be mounted on something -
        /// and it was being set down on the pavement at kerb height with a primitive sphere
        /// glued to the top for the state. That is the same fault as the road signs, in the
        /// same folder, and it is what a junction actually looked like: a signal head lying on
        /// the ground with a glowing ball on it.
        ///
        /// Light_A_City is the real thing. Six metres of mast, a four-metre arm reaching out
        /// over the road, pivot at the base, and THREE LENSES ON THEIR OWN SUBMESHES - so the
        /// state is shown by lighting the pack's own lamp rather than by adding geometry. The
        /// sphere is gone.
        ///
        /// WHICH LENS IS WHICH IS MEASURED, not typed. On a signal the red is on top, the amber
        /// in the middle and the green at the bottom, so sorting the glass submeshes by height
        /// names all three - and it keeps naming them correctly on the B and C variants, whose
        /// submeshes are not in the same order.
        /// </summary>
        private Head Mount(GameObject post, Material lensMaterial,
                           float vx, float vy, Vector2 travel, bool northSouth)
        {
            if (post == null) { Debug.LogWarning("[signals] no traffic light prefab"); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(post);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(vx, ElevationGrid.HeightAt(vx, vy), -vy);

            // WHICH WAY THE LAMPS LOOK IS NOT DERIVABLE FROM THE BOUNDS, and this is the one
            // thing about this prefab that had to be found by looking. The lens plate measures
            // x -0.12..-0.08: that says where it SITS, four centimetres thick in x, and says
            // nothing at all about which of its two faces is the front. Both yaws below put the
            // arm across the carriageway and the head over the road; only one of them has the
            // lamps pointing at the traffic, and the other has them pointing at the cars going
            // the other way - which is exactly what it looked like on screen.
            //
            // So it is one constant rather than an argument. The arm and the head are rigid in
            // the model, so turning the head also turns the arm, and the mast has to change
            // kerbs with it or the arm swings out over the pavement instead of the road. Both
            // follow from this flag; neither is a separate decision.
            const float LensFacesPlusX = 180f;

            var along = new Vector3(travel.x, 0f, -travel.y);
            go.transform.rotation = Quaternion.Euler(
                0f, Mathf.Atan2(-along.z, along.x) * Mathf.Rad2Deg + LensFacesPlusX, 0f);

            var mf = FindLamps(go);
            if (mf == null) { Debug.LogWarning("[signals] no lamps on " + post.name); return null; }

            var mr = mf.GetComponent<MeshRenderer>();
            var mesh = mf.sharedMesh;

            // The glass submeshes, tallest first.
            var glass = new List<(int index, float y)>();
            var slots = mr.sharedMaterials;
            for (int i = 0; i < mesh.subMeshCount && i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                if (slots[i].name.IndexOf("Glass", System.StringComparison.Ordinal) < 0) continue;
                glass.Add((i, mesh.GetSubMesh(i).bounds.center.y));
            }
            glass.Sort((a, b) => b.y.CompareTo(a.y));
            if (glass.Count < 3) { Debug.LogWarning("[signals] " + post.name + " has "
                                                  + glass.Count + " lenses"); return null; }

            var head = new Head
            {
                Lamps = mr,
                Red = glass[0].index,
                Amber = glass[1].index,
                Green = glass[2].index,
                NorthSouth = northSouth,
            };

            // Our own unlit material in the three lens slots. The pack's glass is a shared
            // material used by every window in the city, so tinting it in place would turn every
            // pane in Northgate red - which is how the first attempt at this concluded, wrongly,
            // that the lenses could not be driven at all.
            for (int i = 0; i < 3; i++)
                slots[i == 0 ? head.Red : i == 1 ? head.Amber : head.Green] = lensMaterial;
            mr.sharedMaterials = slots;

            // A small lamp at the lens end of the arm, for the pool of colour on the tarmac.
            // Only the junctions nearest the camera keep theirs - see Nearest().
            var lampGo = new GameObject("Lamp_Emission");
            lampGo.transform.SetParent(go.transform, false);
            lampGo.transform.localPosition = mesh.GetSubMesh(head.Amber).bounds.center;

            var lamp = lampGo.AddComponent<UnityEngine.Light>();
            lamp.type = LightType.Point;
            lamp.range = 11f;
            lamp.intensity = 2.2f;
            lamp.shadows = LightShadows.None;
            head.Lamp = lamp;

            return head;
        }

        /// <summary>The renderer carrying the lamp cluster: the one with glass on it.</summary>
        private static MeshFilter FindLamps(GameObject go)
        {
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;

                foreach (var m in mr.sharedMaterials)
                    if (m != null && m.name.IndexOf("Glass", System.StringComparison.Ordinal) >= 0)
                        return mf;
            }
            return null;
        }

        /// <summary>
        /// An unlit material for the lenses, so they read as lit in daylight too.
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

        /// <summary>
        /// How many junctions have a live lamp on their heads at any moment.
        ///
        /// A downtown signalises every intersection, which is correct and which is also
        /// forty-nine junctions and a hundred and ninety-six point lights - more than the whole
        /// rest of the city put together, and every one of them rendering whether or not it is
        /// within a mile of the camera. The LENS is unlit, so a signal reads at any hour with no
        /// light at all; the lamp only adds the pool of colour on the tarmac, which is worth
        /// having and is worth having ONLY where somebody can see it.
        ///
        /// Six junctions is the length of a street you can see down.
        /// </summary>
        private const int LitJunctions = 6;

        /// <summary>Seconds between working out which those six are. It is not a per-frame question.</summary>
        private const float ReassignInterval = 0.4f;

        private float _assignedAt = -99f;
        private float[] _best;

        private void Update()
        {
            Refresh();
            Nearest();
        }

        /// <summary>Give the live lamps to the junctions nearest the camera, and nobody else.</summary>
        private void Nearest()
        {
            if (Time.unscaledTime - _assignedAt < ReassignInterval) return;
            _assignedAt = Time.unscaledTime;

            var cam = Camera.main;
            if (cam == null) return;

            var eye = cam.transform.position;

            // The cutoff distance, kept as a running shortlist rather than by sorting: this runs
            // several times a second over every junction on the map, and the shortlist is six
            // long.
            _best ??= new float[LitJunctions];
            for (int i = 0; i < LitJunctions; i++) _best[i] = float.MaxValue;

            foreach (var node in _nodes)
            {
                if (node.Heads.Count == 0) continue;
                float d = (new Vector3(node.X, 0f, -node.Y) - eye).sqrMagnitude;

                for (int i = 0; i < LitJunctions; i++)
                {
                    if (d >= _best[i]) continue;
                    for (int j = LitJunctions - 1; j > i; j--) _best[j] = _best[j - 1];
                    _best[i] = d;
                    break;
                }
            }

            float cut = _best[LitJunctions - 1];

            foreach (var node in _nodes)
            {
                if (node.Heads.Count == 0) continue;
                bool on = (new Vector3(node.X, 0f, -node.Y) - eye).sqrMagnitude <= cut;

                foreach (var head in node.Heads)
                    if (head.Lamp != null && head.Lamp.enabled != on) head.Lamp.enabled = on;
            }
        }

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

                    if (head.Lamps != null)
                    {
                        // One lamp lit and the other two dark, which is what a signal is. The
                        // sphere this replaced could only ever be one colour at a time and so
                        // could not show the other two lenses at all.
                        Paint(head.Lamps, head.Red, want == Light.Red ? RedLens : DeadLens);
                        Paint(head.Lamps, head.Amber, want == Light.Amber ? AmberLens : DeadLens);
                        Paint(head.Lamps, head.Green, want == Light.Green ? GreenLens : DeadLens);
                    }

                    if (head.Lamp != null)
                        head.Lamp.color = want == Light.Green ? GreenLens
                                        : want == Light.Amber ? AmberLens : RedLens;
                }
            }
        }

        private void Paint(Renderer r, int submesh, Color colour)
        {
            if (submesh < 0) return;
            _block.SetColor("_BaseColor", colour);
            _block.SetColor("_Color", colour);
            r.SetPropertyBlock(_block, submesh);
        }

    }
}
