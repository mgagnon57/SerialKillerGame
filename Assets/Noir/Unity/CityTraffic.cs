using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Cars that drive the streets, turn at junctions, keep to their lane and stop at red lights.
    ///
    /// THEY FOLLOW THE LANE GRAPH. Until now a car owned one lane from one edge of the map to the
    /// other and wrapped round when it left, because nothing told it which lane it could move
    /// into. Core now cuts every lane at its junctions and works out the legal movements through
    /// each one (see LaneGraph), so a car here is only ever doing one of two things: running down
    /// a segment, or crossing a junction on a turn it was allowed to take.
    ///
    /// LANES COME FROM THE ROAD, NOT FROM THE TILE. Where a lane sits across the carriageway is
    /// derived from its road's class and the MEASURED width of that class's asphalt. Nothing here
    /// is a number somebody liked the look of, because every earlier version of this file was,
    /// and every one of them put a wheel on the kerb.
    ///
    /// WHAT STOPS THEM HITTING EACH OTHER, in order of how much work it does:
    ///
    ///   SIGNALS. Only one axis of a junction is ever green, so crossing streams are separated
    ///   before anything else has to think about them. A car holds at the stop line at the end of
    ///   its segment and eases to a halt rather than stopping dead.
    ///
    ///   GIVING WAY WHEN TURNING LEFT, which is the one movement a signal does not protect: a
    ///   left turn crosses the oncoming stream, which has the same green. A left-turner waits
    ///   until nothing is coming.
    ///
    ///   FOLLOWING, a look-ahead box against traffic going roughly the same way. Cross traffic is
    ///   the signals' business; counting it here made a car with a green light sit still because
    ///   a car waiting at the red on the crossing street fell inside the box.
    ///
    /// Built AFTER CityChunker and outside the node it bakes: a combined mesh cannot move.
    /// </summary>
    public sealed class CityTraffic : MonoBehaviour
    {
        /// <summary>Metres per second. A town speed, not a motorway one.</summary>
        private const float Speed = 8f;

        /// <summary>Slower round a corner, as anybody is.</summary>
        private const float TurnSpeed = 5f;

        /// <summary>How far ahead a car looks for the car in front, and how wide that look is.</summary>
        private const float LookAhead = 8f, LookWide = 2.4f;

        /// <summary>Where a car begins easing off for a red light.</summary>
        private const float Braking = 14f;

        /// <summary>How far up the road an oncoming car stops a left turn.</summary>
        private const float Oncoming = 22f;

        /// <summary>
        /// How far up the priority road a car has to be before it is safe to pull out across it.
        ///
        /// Longer than <see cref="Oncoming"/>, and deliberately: a left-turner at a signal is
        /// crossing traffic that has the same green and is already committed to a queue, whereas
        /// this is crossing a country arterial at speed. At 8 m/s a car thirty-five metres away
        /// arrives in four and a half seconds, which is about how long it takes to get across.
        /// </summary>
        private const float Crossing = 35f;

        /// <summary>
        /// Moving vehicles per HOUSEHOLD. The budget comes from how many people live here rather
        /// than from how much tarmac was laid, which is the only version of this that scales: a
        /// bigger map gets more lanes and the same traffic per resident, so a village stays quiet
        /// and a city fills up without anybody retuning a number.
        ///
        /// PER HOUSEHOLD AND NOT PER HOME BUILDING, which is what it used to be and why the roads
        /// looked empty. A terrace is one building with four front doors and a block of flats is
        /// one with a dozen; counting buildings gave a town of three hundred the traffic of a
        /// hamlet of twenty-seven, and spread across a 960m map that is no traffic at all.
        ///
        /// AND WHY IT IS NO LONGER 0.6. The downtown grew from nine blocks to thirty-six and the
        /// traffic did not move: ninety-seven vehicles before and ninety-seven after, because a
        /// `district` is an OPEN place with no residents in it, so twenty-seven new blocks added
        /// exactly nothing to the household count the budget is drawn from.
        ///
        /// A downtown is not driven only by the people who sleep in it. It is driven by everyone
        /// who works there, delivers to it, or is passing through - so once the town is bigger
        /// than the houses in it, cars-per-resident-household stops being the whole answer. This
        /// is the honest short-term number for that; the real fix is for a district to declare
        /// how many people live in the block, and then this drops back towards one.
        /// </summary>
        public static float CarsPerHome = 1.5f;

        private sealed class Mover
        {
            public Transform What;
            public int Segment;        // where it is now
            public float S;            // travel coordinate along that segment
            public int Turn = -1;      // >= 0 while it is crossing a junction
            public float T;            // 0..1 through the turn
            public Vector3 Forward = Vector3.forward;
            public int Choices;        // bumped each junction, so its route is not a loop
        }

        private readonly List<Mover> _movers = new List<Mover>();
        private CitySignals _signals;
        private WorldModel _world;

        /// <summary>
        /// The lane graph the traffic runs on. Public because the bus routes will want the same
        /// one rather than a second copy that can disagree with it.
        /// </summary>
        public LaneGraph Graph { get; private set; }

        public static CityTraffic Create(WorldModel world, Transform parent, CitySignals signals)
        {
            var go = new GameObject("CityTraffic");
            go.transform.SetParent(parent, false);
            var traffic = go.AddComponent<CityTraffic>();
            traffic._signals = signals;
            traffic._world = world;
            traffic.Graph = new LaneGraph(world.Roads, world.Width, world.Height);
#if UNITY_EDITOR
            traffic.Populate(world);
#endif
            return traffic;
        }

        // ---- geometry -------------------------------------------------------------------

        /// <summary>
        /// Where a lane runs across its corridor, in village coordinates.
        ///
        /// The centre line comes from the map, the offset from the MEASURED asphalt, and which
        /// side of the centre from the fact that this city drives on the right. All three have
        /// been got wrong separately at some point; none of them is guessed here.
        /// </summary>
        private float CrossOf(LaneSegment segment)
        {
            var line = _world.Roads.Lines[segment.Line];
            return line.Centre
                 + Headings.Side(segment.Way) * CityStreets.LaneOffset(line.Class, segment.Lane);
        }

        /// <summary>A point on a lane, by segment index. For checking from outside.</summary>
        public Vector3 PointOn(int segment, float s) => PointOn(Graph.Segments[segment], s);

        /// <summary>The corner a turn cuts, by turn index. For checking from outside.</summary>
        public void TurnArc(int turn, out Vector3 a, out Vector3 b, out Vector3 c) =>
            TurnArc(Graph.Turns[turn], out a, out b, out c);

        /// <summary>A point along a turn, 0 at the stop line and 1 on the far side.</summary>
        public Vector3 PointInTurn(int turn, float t)
        {
            TurnArc(turn, out var a, out var b, out var c);
            return Bezier(a, b, c, t);
        }

        /// <summary>A point on a lane, in Unity space. Village y runs into -z, as everywhere.</summary>
        private Vector3 PointOn(LaneSegment segment, float s)
        {
            var line = _world.Roads.Lines[segment.Line];
            float along = LaneGraph.AlongOf(segment.Way, s);
            float cross = CrossOf(segment);

            return line.IsNorthSouth
                ? new Vector3(cross, 0f, -along)
                : new Vector3(along, 0f, -cross);
        }

        /// <summary>
        /// The corner a turn cuts: start, the point where the two lane centre lines would meet,
        /// and finish. A quadratic through those three is a perfectly good motor-car turn and
        /// costs nothing.
        /// </summary>
        private void TurnArc(LaneTurn turn, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            var from = Graph.Segments[turn.From];
            var to = Graph.Segments[turn.To];

            a = PointOn(from, from.ToS);
            c = PointOn(to, to.FromS);

            if (turn.Kind == TurnKind.Straight)
            {
                b = (a + c) * 0.5f;      // no corner to cut; the two lanes are collinear
                return;
            }

            // One lane holds x and the other holds z, so where they cross is simply one
            // coordinate from each.
            bool fromIsNorthSouth = _world.Roads.Lines[from.Line].IsNorthSouth;
            b = fromIsNorthSouth ? new Vector3(a.x, 0f, c.z) : new Vector3(c.x, 0f, a.z);
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        private float TurnLength(LaneTurn turn)
        {
            TurnArc(turn, out var a, out var b, out var c);
            return Vector3.Distance(a, b) + Vector3.Distance(b, c);
        }

#if UNITY_EDITOR
        private void Populate(WorldModel world)
        {
            if (Graph.Segments.Count == 0) { Debug.LogWarning("[traffic] no lanes"); return; }

            // One fleet per road class, fetched once. A vehicle is drawn from the fleet of the
            // road it is actually going to be on - see Everyday.
            var fleets = new Dictionary<RoadClass, List<string>>();
            foreach (RoadClass klass in System.Enum.GetValues(typeof(RoadClass)))
                fleets[klass] = Everyday(klass);

            int budget = Mathf.Clamp(Mathf.RoundToInt(world.Households * CarsPerHome),
                                     Mathf.Min(6, Graph.Segments.Count),
                                     Graph.Segments.Count);

            var counted = new Dictionary<RoadClass, int>();

            for (int n = 0; n < budget; n++)
            {
                // Spread over the whole graph rather than queued at the edges, so the city has
                // traffic in it the moment it appears rather than thirty seconds later.
                var segment = Graph.Segments[n * 7919 % Graph.Segments.Count];
                var onClass = world.Roads.Lines[segment.Line].Class;

                var cars = fleets[onClass];
                if (cars.Count == 0) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    cars[(int)(Materials3D.Scatter(n, 0, 811) % (uint)cars.Count)]);
                if (prefab == null) continue;

                counted[onClass] = counted.TryGetValue(onClass, out int was) ? was + 1 : 1;

                float s = Mathf.Lerp(segment.FromS, segment.ToS,
                                     Materials3D.Scatter(n, 1, 821) % 100 / 100f);

                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                car.transform.SetParent(transform, false);

                var mover = new Mover { What = car.transform, Segment = segment.Index, S = s };
                _movers.Add(mover);
                Seat(mover);
            }

            var byClass = new List<string>();
            foreach (var pair in counted)
                byClass.Add($"{pair.Value} on {pair.Key.ToString().ToLowerInvariant()}");

            Debug.Log($"[traffic] {_movers.Count} vehicles on {Graph.Segments.Count} lane segments "
                    + $"({string.Join(", ", byClass)}), drawn from fleets of "
                    + $"{fleets[RoadClass.Freeway].Count} / {fleets[RoadClass.Mainroad].Count} / "
                    + $"{fleets[RoadClass.Track].Count} (freeway / mainroad / track). "
                    + $"{Graph.Turns.Count} turns through {world.Roads.Junctions.Count} junctions.");
        }

        /// <summary>
        /// WHAT USES A ROAD DEPENDS ON WHAT SORT OF ROAD IT IS.
        ///
        /// Every vehicle in the city came from one list, which meant a four-lane arterial carried
        /// exactly the same traffic as a farm track: hatchbacks. The pack has sixty-nine trucks in
        /// it - box, cistern, concrete, container, dump, garbage, gritter, logging, flatbed, and
        /// the tractor units and trailers to pull them - and ONE of them was ever placed. An
        /// arterial with no lorry on it does not read as an arterial.
        ///
        /// So: heavy goods take the arterials and nothing else, because that is where they are
        /// allowed and because a container lorry on a farm track is the same category of mistake
        /// as a stock car on the avenue. A track gets what a farm owns - pickups, old vans, an
        /// off-roader. Everything in between gets the everyday traffic of a town.
        ///
        /// STILL EXCLUDED: the 79-strong motorsport paddock. Formula, Nascar, Le Mans prototypes
        /// and a gokart have nowhere to be until there is somewhere to race them.
        /// </summary>
        private static List<string> Everyday(RoadClass klass)
        {
            // The everyday traffic of a town, on every road.
            var wanted = new List<string>
            {
                "Car_Modern", "Car_Pickup_Modern", "Car_Taxi_Modern",
                "Car_Cargovan_Modern", "Car_Van_Old", "Car_Offroad_Modern",
                "Car_Offroad_Roofless_Modern",
            };

            if (klass == RoadClass.Track)
            {
                // A farm track carries what the farm owns and nothing that could not turn round
                // at the end of it.
                wanted = new List<string>
                {
                    "Car_Pickup_Modern", "Car_Van_Old", "Car_Offroad_Modern",
                    "Car_Offroad_Roofless_Modern",
                    "Pickup_Truck_Old_Farm", "Tractor_Old", "Tractor_Big", "Atv",
                };
                return Found(wanted);
            }

            // WHAT A TOWN RUNS, as against what people own. A police car, an ambulance, a tow
            // truck and the school bus were bought, were sitting in the folder, and appeared
            // nowhere on the road - the ambulance's only outing was standing still in the
            // hospital car park. For a game about who was where at what time, a cruiser going
            // past at the wrong hour is not scenery.
            //
            // THEY WEIGHT THEMSELVES. A vehicle is drawn uniformly from the list of matching
            // PREFABS, and the everyday cars ship in six colours each while there is exactly one
            // police car and one ambulance - so putting them in the same list makes them roughly
            // one in ninety without anybody tuning a frequency. The pack's own variant counts
            // are the weighting.
            wanted.AddRange(new[]
            {
                "Car_Police_Modern", "Car_Ambulance_Modern", "Car_Towtruck_Modern",
                "Car_Bus_School_Modern",
            });

            if (klass == RoadClass.Freeway)
            {
                // Two lanes each way and room to pass: this is the road the freight is on, and
                // it is the only road wide enough for an articulated lorry to be on at all.
                wanted.AddRange(new[]
                {
                    "Car_Sport_Modern", "Car_Roadster_Cabrio_Modern", "Car_Firetruck_Modern",
                    "Car_Truck_Modern", "Car_Truck_Modern_Box", "Car_Truck_Modern_Container",
                    "Car_Truck_Modern_Cistern", "Car_Truck_Modern_Dump",
                    "Car_Truck_Modern_Logging", "Car_Truck_Modern_Garbage",
                    "Car_Truck_Modern_Concrete", "Car_Truck_Modern_Gritter",
                    "Car_Truck_Modern_Loadingplatform",
                    "Car_Truck_Trailer_Modern", "Car_Truck_Trailer_Sleepercab_Modern",
                    "Car_Truck_Trailer_Car_Modern", "Car_Truck_Trailer_Container_Large",
                });
            }
            else
            {
                // A caravan is a main-road vehicle: it is going somewhere out of town and it is
                // not going there quickly.
                wanted.Add("Car_Sport_Modern");
                wanted.Add("Car_Caravan_Modern");
            }

            return Found(wanted);
        }

        /// <summary>
        /// Every prefab in the vehicle folders whose name is one of these, or a colour variant
        /// of one.
        ///
        /// STILL EXCLUDED: the 79-strong motorsport paddock. Formula, Nascar, Le Mans prototypes
        /// and a gokart have nowhere to be until there is somewhere to race them.
        /// </summary>
        private static List<string> Found(List<string> wanted)
        {
            var found = new List<string>();
            foreach (var folder in new[]
                     {
                         "Assets/polyperfect/Poly Universal Pack/Prefabs/Cars",
                         "Assets/polyperfect/Poly Universal Pack/Prefabs/Farm/Vehicles Farm",
                     })
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (path.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (path.IndexOf("Racing", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                foreach (var w in wanted)
                    if (name == w || name.StartsWith(w + "_", System.StringComparison.Ordinal))
                    { found.Add(path); break; }
            }
            found.Sort(System.StringComparer.Ordinal);   // stable, so the city looks the same twice
            return found;
        }
#endif

        // ---- driving --------------------------------------------------------------------

        private void Update()
        {
            if (Graph == null) return;
            float dt = Time.deltaTime;

            for (int i = 0; i < _movers.Count; i++)
            {
                var me = _movers[i];
                if (me.What == null) continue;

                if (me.Turn >= 0) CrossJunction(me, dt);
                else RunSegment(me, i, dt);

                Seat(me);
            }
        }

        /// <summary>Along a straight piece of lane, up to the stop line at the far end.</summary>
        private void RunSegment(Mover me, int index, float dt)
        {
            var segment = Graph.Segments[me.Segment];
            float step = Speed * dt;

            float toEnd = segment.ToS - me.S;

            if (!segment.IsExit)
            {
                // The end of a segment IS the stop line. Ease down to it unless the way through
                // is clear, and only commit to a turn at the moment of entering the junction.
                bool clear = MayCross(me, segment);
                if (!clear)
                {
                    float allowed = Mathf.Max(0f, toEnd - 0.4f);
                    step = Mathf.Min(step, allowed, Mathf.Max(step * (toEnd / Braking), 0f));
                }
            }

            if (Blocked(index)) step = 0f;

            me.S += step;

            if (me.S < segment.ToS) return;

            if (segment.IsExit) { Reintroduce(me); return; }

            int turn = Choose(me, segment);
            if (turn < 0) { me.S = segment.ToS; return; }     // nothing legal: hold

            me.Turn = turn;
            me.T = 0f;
        }

        /// <summary>Through the junction on the chosen turn.</summary>
        private void CrossJunction(Mover me, float dt)
        {
            var turn = Graph.Turns[me.Turn];
            float length = Mathf.Max(0.5f, TurnLength(turn));

            me.T += TurnSpeed * dt / length;
            if (me.T < 1f) return;

            var to = Graph.Segments[turn.To];
            me.Segment = to.Index;
            me.S = to.FromS;
            me.Turn = -1;
            me.T = 0f;
        }

        /// <summary>
        /// May this car leave its stop line?
        ///
        /// Three things can hold it, and which of them apply depends on the junction:
        ///
        ///   IN THE TOWN the signal decides for everyone, and a left-turner additionally has to
        ///   wait for a gap in the oncoming stream, because the oncoming stream has the same
        ///   green it does. That is the one conflict signals never resolve on their own.
        ///
        ///   IN THE COUNTRY there is no signal, so the two streams are separated by priority
        ///   instead: the bigger road runs through and the smaller one waits for a gap. Without
        ///   this, taking the lights off the farmland junctions would have turned every one of
        ///   them into a crossing where both streams simply drive through each other.
        /// </summary>
        private bool MayCross(Mover me, LaneSegment segment)
        {
            bool northSouth = Headings.IsNorthSouth(segment.Way);
            int node = segment.ToJunction;

            if (_signals != null)
            {
                if (_signals.IsSignalised(node))
                {
                    if (!_signals.MayEnter(node, northSouth)) return false;
                }
                else if (_signals.GivesWay(node, northSouth))
                {
                    // Look further back than a signal would. A car on the priority road is not
                    // slowing down for this, so the gap has to be there before the wait starts.
                    if (segment.ToS - me.S < Braking && !NothingCrossing(me, segment)) return false;
                }
            }

            // Only worth asking once it is nearly there.
            if (segment.ToS - me.S > 2f) return true;

            int turn = Choose(me, segment);
            if (turn < 0 || Graph.Turns[turn].Kind != TurnKind.Left) return true;

            return NothingComing(me, segment);
        }

        /// <summary>
        /// Is the road this one is about to cross clear enough to pull out into?
        ///
        /// The other axis of the same junction, both directions of it, plus anybody already
        /// inside the junction on any arm - at a priority junction there is nothing else keeping
        /// the two streams apart, so a car that has committed has to be waited out whichever way
        /// it came from.
        /// </summary>
        private bool NothingCrossing(Mover me, LaneSegment mine)
        {
            bool mineIsNorthSouth = Headings.IsNorthSouth(mine.Way);

            foreach (var other in _movers)
            {
                if (other == me || other.What == null) continue;

                if (other.Turn >= 0)
                {
                    // Committed and moving through. Wait for it regardless of its arm.
                    if (Graph.Segments[Graph.Turns[other.Turn].From].ToJunction == mine.ToJunction)
                        return false;
                    continue;
                }

                var theirs = Graph.Segments[other.Segment];
                if (theirs.ToJunction != mine.ToJunction) continue;
                if (Headings.IsNorthSouth(theirs.Way) == mineIsNorthSouth) continue;   // my axis

                // A car on the priority road that is itself stopped is not coming, so the
                // distance is what counts rather than the mere fact of it being on the approach.
                if (theirs.ToS - other.S < Crossing) return false;
            }
            return true;
        }

        /// <summary>Is the oncoming lane clear enough to turn across?</summary>
        private bool NothingComing(Mover me, LaneSegment mine)
        {
            var facing = Headings.Back(mine.Way);

            foreach (var other in _movers)
            {
                if (other == me || other.What == null) continue;

                // Someone already in the junction, coming the other way.
                if (other.Turn >= 0)
                {
                    var crossing = Graph.Segments[Graph.Turns[other.Turn].From];
                    if (crossing.ToJunction == mine.ToJunction && crossing.Way == facing) return false;
                    continue;
                }

                var theirs = Graph.Segments[other.Segment];
                if (theirs.ToJunction != mine.ToJunction) continue;
                if (theirs.Way != facing) continue;
                if (theirs.ToS - other.S < Oncoming) return false;
            }
            return true;
        }

        /// <summary>
        /// Which way to go at the junction ahead. Mostly straight on, because most traffic does,
        /// and deterministic for a given car and junction so the answer does not change between
        /// the frame it is asked on approach and the frame it is acted on.
        /// </summary>
        private int Choose(Mover me, LaneSegment segment)
        {
            var options = Graph.TurnsFrom(segment.Index);
            if (options.Count == 0) return -1;

            uint roll = Materials3D.Scatter(segment.Index, me.Choices, 907) % 100;
            var want = roll < 60 ? TurnKind.Straight : roll < 80 ? TurnKind.Right : TurnKind.Left;

            foreach (int t in options)
                if (Graph.Turns[t].Kind == want) return t;

            return options[(int)(roll % (uint)options.Count)];
        }

        /// <summary>Off one edge of the map and back on at another.</summary>
        private void Reintroduce(Mover me)
        {
            if (Graph.Entries.Count == 0) return;

            me.Choices++;
            var entry = Graph.Segments[
                Graph.Entries[(int)(Materials3D.Scatter(me.Choices, me.Segment, 911)
                                    % (uint)Graph.Entries.Count)]];
            me.Segment = entry.Index;
            me.S = entry.FromS;
            me.Turn = -1;
        }

        /// <summary>Put the car where its position says it is, pointing where it is going.</summary>
        private void Seat(Mover me)
        {
            Vector3 at, ahead;

            if (me.Turn >= 0)
            {
                TurnArc(Graph.Turns[me.Turn], out var a, out var b, out var c);
                at = Bezier(a, b, c, me.T);
                ahead = Bezier(a, b, c, Mathf.Min(1f, me.T + 0.05f));
                if ((ahead - at).sqrMagnitude < 0.0001f) ahead = at + me.Forward;
            }
            else
            {
                var segment = Graph.Segments[me.Segment];
                at = PointOn(segment, me.S);
                ahead = PointOn(segment, me.S + 1f);
            }

            var forward = ahead - at;
            if (forward.sqrMagnitude > 0.0001f) me.Forward = forward.normalized;

            me.What.position = at;
            me.What.rotation = Quaternion.LookRotation(me.Forward, Vector3.up);
        }

        /// <summary>
        /// Is there a car close in front, going roughly my way?
        ///
        /// Parallel traffic only. Cross traffic at a junction is separated by the signals, and
        /// counting it here made a car with a green light sit still because a car waiting at the
        /// red on the crossing street happened to fall inside the box.
        /// </summary>
        private bool Blocked(int index)
        {
            var me = _movers[index];
            var here = me.What.position;

            for (int j = 0; j < _movers.Count; j++)
            {
                if (j == index) continue;
                var other = _movers[j];
                if (other.What == null) continue;
                if (Vector3.Dot(me.Forward, other.Forward) < 0.7f) continue;

                var gap = other.What.position - here;

                float ahead = Vector3.Dot(gap, me.Forward);
                if (ahead <= 0f || ahead > LookAhead) continue;          // behind, or far off

                float side = Vector3.Cross(me.Forward, gap).magnitude;
                if (side > LookWide) continue;                           // not in my lane

                return true;
            }
            return false;
        }
    }
}
