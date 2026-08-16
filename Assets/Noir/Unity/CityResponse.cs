using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// The two vehicles the county sends: a sheriff's car and an ambulance, driving in from the
    /// edge of the map to a scene and back out again.
    ///
    /// THEY ARE NOT PART OF THE TRAFFIC, AND THAT IS DELIBERATE. Every response vehicle drives
    /// the same <see cref="LaneGraph"/> the ambient fleet does, through the same public wrappers
    /// (<see cref="CityTraffic.PointOn"/>, <see cref="CityTraffic.TurnArc"/>,
    /// <see cref="CityTraffic.PointInTurn"/>) — but none of them ever enters
    /// <c>CityTraffic._movers</c>. Eight loops in that file walk <c>_movers</c> to decide who is
    /// following whom, who may pull out and who is about to be driven into, and every one of them
    /// assumes a car that WANDERS: it is recycled at the map edge, it picks its turns at random,
    /// and <see cref="CityTraffic.Retime"/> garages it when the hour says the town is quiet. A
    /// vehicle answering a call does none of those things. Putting one in that list would mean a
    /// guard in eight places and a ninth one missed. So it drives beside the fleet instead: the
    /// ambient cars see it through <see cref="CityTraffic.Obstacles"/> once it is standing at a
    /// scene, and it sees them through <see cref="CityTraffic.AnyMoverWithin"/> while it moves.
    ///
    /// THEY RUN ON SIM TIME, NOT ON FRAMES. Everything here advances by the SIMULATION clock's
    /// own delta — `(Tick - lastTick) / GameClock.TicksPerSecond` — so fast-forward compresses
    /// the drive exactly as it compresses a citizen's walk to work, and a paused sim stops the
    /// car dead. The ambient traffic is scenery and is entitled to run off `Time.deltaTime`; a
    /// response that the case machine is timing in SIM MINUTES is not.
    ///
    /// THEY DO NOT STOP AT RED LIGHTS. There is no signal test anywhere in this file, on purpose:
    /// what holds a response car is what is physically in front of it, and nothing else. An
    /// official car crossing a junction against the light is what the town would see.
    /// </summary>
    public sealed class CityResponse : MonoBehaviour
    {
        /// <summary>Which vehicle. The county car answers first; the ambulance comes for a body.</summary>
        public enum Rig { County, Ambulance }

        /// <summary>
        /// Metres per second. A shade over the ambient fleet's 8 — an official car moving with
        /// purpose — and under the player's 12, so a driver can still outrun the response.
        /// </summary>
        private const float ResponseSpeed = 10f;

        /// <summary>
        /// The gap kept beyond the two vehicles' own half-lengths, how far off the centre line
        /// another vehicle still counts as being in this lane, how long a stationary obstacle is
        /// assumed to be, and where the nose starts easing off for the stop.
        ///
        /// All four are <see cref="CityTraffic"/>'s own numbers (`Headway`, `LookWide`, the 2.2 m
        /// in `Blocked`'s obstacle arm, and `Braking`), restated here rather than made public
        /// there. They are copied because a response car has to judge a gap the way an ambient
        /// one does, and they are documented as copies so that changing one there is known to
        /// mean changing it here.
        /// </summary>
        private const float Headway = 3.5f, LookWide = 2.4f, ObstacleReach = 2.2f, Braking = 14f;

        /// <summary>Half a vehicle, when there is no mesh to measure. CityTraffic's own fallback.</summary>
        private const float DefaultReach = 2.2f;

        /// <summary>
        /// How long a slice of driving is, in SIM seconds, and how many a frame may run.
        ///
        /// The same argument as <see cref="CityTraffic"/>'s `Slice`/`MostSlices` with the frame
        /// rate swapped for the sim rate: at 600x fast-forward a single frame can carry minutes
        /// of sim time, and a car that advanced all of it in one step would jump whole segments,
        /// skip its stop and drive through the junction geometry rather than round it. Fifteen
        /// sim seconds a frame is enough to keep a fast-forwarded response looking driven; past
        /// that it falls behind rather than teleports.
        /// </summary>
        private const float Step = 0.25f;
        private const int MostSteps = 60;

        /// <summary>
        /// How far off the scene a lane may be and still count as "the road it happened on".
        ///
        /// FIFTEEN METRES BECAUSE A ROAD HAS TWO SIDES AND A VERGE. The scene is a tile the
        /// player's car struck somebody on, which may be the far carriageway, the pavement, or a
        /// yard just off the kerb — so the nearest lane by centre-line distance is frequently not
        /// a lane anything can legally route to. See <see cref="PlanRoute"/> for why this is a
        /// LIST rather than a winner.
        /// </summary>
        private const float SceneReach = 15f;

        /// <summary>
        /// How far from the extreme an entry still counts as being at that edge, and the fewest
        /// entries the fallback will consider whatever the distances say.
        ///
        /// Only the FALLBACK path reads these: the first entry tried is always the southmost or
        /// northmost outright, which is Route 1 at both edges of this map.
        /// </summary>
        private const float EdgeBand = 150f;
        private const int LeastEdgeEntries = 4;

        /// <summary>
        /// The most route plans one <see cref="DriveIn"/> may run before giving up and arriving
        /// off-stage. `LaneRoutes.Plan` is a Dijkstra over every lane segment in the town, so a
        /// full entries-by-candidates sweep of a graph this size is a visible hitch. Twenty-four
        /// is several entries' worth of candidates and still a fraction of a frame.
        /// </summary>
        private const int MostPlans = 24;

        /// <summary>
        /// How long an off-stage arrival takes, in SIM seconds.
        ///
        /// THE CASE MUST NEVER WEDGE ON ROUTING. The lane graph offers no U-turns and no
        /// lane-change turns, so `LaneRoutes.Plan` returning false is a LEGAL answer about two
        /// real segments rather than a bug — a scene on a one-way stub reachable only by turning
        /// round has no route from anywhere. When every candidate at every edge entry refuses,
        /// the vehicle is reported as having arrived after two sim minutes with no drive drawn,
        /// and the response carries on. Silence is not an option here and neither is a hang; see
        /// the log line in <see cref="DriveIn"/>.
        /// </summary>
        private const float OffStageSeconds = 120f;

        /// <summary>
        /// One vehicle's whole state, mirroring <see cref="CityTraffic"/>'s `Mover` — and a CLASS
        /// for the same reason that one is: it is mutated in place from several methods, and a
        /// struct in a field array would hand each of them a copy to change and throw away.
        /// </summary>
        private sealed class Car
        {
            public Transform What;

            /// <summary>LaneTurn indices in driving order, from `LaneRoutes.Plan`.</summary>
            public readonly List<int> Turns = new List<int>();

            /// <summary>How many of them are behind it.</summary>
            public int Leg;

            public int Segment;
            public float S;

            /// <summary>0..1 through the turn at <c>Turns[Leg]</c>, while <see cref="InTurn"/>.</summary>
            public float T;
            public bool InTurn;

            /// <summary>Travel coordinate on the LAST segment at which it stops. See Park.</summary>
            public float StopAtS;

            public bool Arrived;

            /// <summary>Driving OUT rather than in: it despawns at the end of the last segment.</summary>
            public bool Leaving;

            /// <summary>Half this vehicle's measured length, nose to tail.</summary>
            public float Reach = DefaultReach;

            public Vector3 Forward = Vector3.forward;

            /// <summary>Sim seconds left of an un-routable arrival; negative when it is driving.</summary>
            public float OffStage = -1f;

            /// <summary>Fired once, at the moment it is parked. Nulled as it fires.</summary>
            public System.Action OnArrived;

            /// <summary>The scene it was sent to, in world space — where it stands if it never drove.</summary>
            public Vector3 SceneAt;

            /// <summary>Where it ended up. Task 12's officer gets out of the car here.</summary>
            public Vector3 ParkedAt;
        }

        private readonly Car[] _cars = new Car[2];

        private WorldModel _world;
        private CityTraffic _traffic;
        private VillageHost _host;

        /// <summary>The sim tick the last slice of driving was measured from. -1 before the first.</summary>
        private long _lastTick = -1;

        // Scratch, reused rather than allocated per call — these run on a hit, not per frame, but
        // the lists are small and permanent and there is no reason to churn them.
        private readonly List<(int Seg, float S, float Lateral)> _candidates =
            new List<(int Seg, float S, float Lateral)>();
        private readonly List<int> _edge = new List<int>();

        private const string CarsCity =
            "Assets/polyperfect/Poly Universal Pack/Prefabs/Cars/Cars City/";

        /// <summary>
        /// The pack's one cruiser and its one ambulance — the same two paths
        /// <see cref="CityBuildings"/> parks outside the precinct and the hospital
        /// (`CityBuildings.cs:540-541`). A bespoke county livery is not in the pack and is not
        /// worth chasing for a car seen from across a street.
        /// </summary>
        private static string PrefabOf(Rig rig) => rig == Rig.County
            ? CarsCity + "Car_Police_Modern.prefab"
            : CarsCity + "Car_Ambulance_Modern.prefab";

        private static string NameOf(Rig rig) => rig == Rig.County ? "county car" : "ambulance";

        public static CityResponse Create(WorldModel world, Transform parent, CityTraffic traffic)
        {
            var go = new GameObject("CityResponse");
            go.transform.SetParent(parent, false);
            var response = go.AddComponent<CityResponse>();
            response._world = world;
            response._traffic = traffic;
            response._host = VillageHost.Instance;

            // AUDIBLE, NEVER SILENT. The prefabs are reached through AssetDatabase, which exists
            // only in the editor, so a shipped player drives two vehicles nobody can see. That is
            // a deliberate degradation — the response still happens, the case still closes — and
            // it says so once at build time rather than leaving somebody to wonder why no car ever
            // turned up. Same shape as the `[people] all 1400 of Rossville is PRIMITIVE CAPSULES`
            // line and for the same reason.
            int drawn = 0, invisible = 0;
            foreach (Rig rig in System.Enum.GetValues(typeof(Rig)))
            {
#if UNITY_EDITOR
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabOf(rig)) != null)
                { drawn++; continue; }
#endif
                invisible++;
            }
            Debug.Log($"[response] {drawn} actors drawn, {invisible} invisible (editor-only prefabs)");

            return response;
        }

        // ---- orders ---------------------------------------------------------------------

        /// <summary>
        /// Send a vehicle in from the named map edge to the scene. It spawns off-stage at the
        /// southmost (or northmost) lane entry — Route 1 owns both edges of this map — drives the
        /// lane graph, and fires <paramref name="onArrived"/> exactly once as it stops beside the
        /// scene.
        /// </summary>
        public void DriveIn(Rig rig, bool edgeSouth, Tile scene, System.Action onArrived)
        {
            int slot = (int)rig;
            if (slot < 0 || slot >= _cars.Length) return;

            // ALREADY OUT. The case machine emits each order once per case, but cases queue and a
            // second one can want the same vehicle while the first still has it. The newest order
            // wins: the standing vehicle is taken away rather than left parked at a scene that is
            // no longer being worked.
            if (_cars[slot] != null)
            {
                Debug.LogWarning($"[response] the {NameOf(rig)} was still out; recalling it");
                Despawn(slot);
            }

            var car = new Car
            {
                SceneAt = Space3D.ToWorld(scene),
                OnArrived = onArrived,
            };
            _cars[slot] = car;

            if (_traffic == null || _traffic.Graph == null || !PlanRoute(car, edgeSouth, scene))
            {
                Debug.Log($"[response] no route from the {(edgeSouth ? "south" : "north")} edge "
                        + $"to tile ({scene.X},{scene.Y}) — arriving off-stage");
                car.OffStage = OffStageSeconds;
                return;
            }

            Dress(car, rig);
            Seat(car);
        }

        /// <summary>
        /// Send it home: from where it stands to an exit at the same edge it came in by, and off
        /// the map. It stops being an obstacle the moment it is ordered away, and the GameObject
        /// goes when it reaches the end of the last lane.
        /// </summary>
        public void Depart(Rig rig, bool edgeSouth)
        {
            int slot = (int)rig;
            if (slot < 0 || slot >= _cars.Length) return;

            var car = _cars[slot];
            if (car == null) return;

            // It never took the stage, so there is nothing to drive away.
            if (car.What == null || _traffic == null || _traffic.Graph == null)
            { Despawn(slot); return; }

            _traffic.Obstacles.Remove(car.What);

            if (!PlanOut(car, edgeSouth))
            {
                Debug.Log($"[response] no route off the {(edgeSouth ? "south" : "north")} edge "
                        + $"for the {NameOf(rig)} — leaving off-stage");
                Despawn(slot);
                return;
            }

            car.Arrived = false;
            car.Leaving = true;
            car.OnArrived = null;
        }

        /// <summary>Is that vehicle standing at the scene? False before it gets there and false
        /// again the moment it is ordered away.</summary>
        public bool Arrived(Rig rig)
        {
            int slot = (int)rig;
            return slot >= 0 && slot < _cars.Length && _cars[slot] != null && _cars[slot].Arrived;
        }

        /// <summary>Where that vehicle is standing, or null when it is not parked anywhere.</summary>
        public Vector3? ParkedAt(Rig rig)
        {
            int slot = (int)rig;
            if (slot < 0 || slot >= _cars.Length) return null;
            var car = _cars[slot];
            return car != null && car.Arrived ? car.ParkedAt : (Vector3?)null;
        }

        // ---- routing --------------------------------------------------------------------

        /// <summary>
        /// Find an entry at the named edge and a lane beside the scene that are joined by legal
        /// turns, and seat the car on it.
        ///
        /// WHY THIS IS A SEARCH RATHER THAN TWO LOOKUPS. `LaneRoutes.NearestSegment` answers with
        /// the single nearest lane by centre-line distance and breaks ties by lowest index with no
        /// regard for which way that lane travels — so for a scene on a two-way street it returns
        /// one of the two directions arbitrarily, and the lane graph offers no U-turn to fix the
        /// choice. Worse, the graph has no lane-change turns either, so "no route" is a perfectly
        /// legal answer about two real segments. Asking once and believing the answer would strand
        /// the response on about half the scenes in the town.
        ///
        /// So: every lane within <see cref="SceneReach"/> of the scene, both directions, nearest
        /// first; every entry at that edge, the outright southmost or northmost first; the first
        /// pair that routes wins. If none does, the caller reports an off-stage arrival.
        /// </summary>
        private bool PlanRoute(Car car, bool edgeSouth, Tile scene)
        {
            var graph = _traffic.Graph;

            CandidatesNear(Vec2.CentreOf(scene));
            if (_candidates.Count == 0) return false;

            EdgeEntries(edgeSouth, entry: true);

            int plans = 0;
            for (int e = 0; e < _edge.Count; e++)
            {
                int from = _edge[e];
                float fromS = graph.Segments[from].FromS;

                for (int c = 0; c < _candidates.Count && plans < MostPlans; c++)
                {
                    var (seg, s, _) = _candidates[c];

                    // A STOP BEHIND THE SPAWN IS NOT A DRIVE. Only reachable when the scene sits
                    // on the entry lane itself, which on a village edge is a real case: the car
                    // would be parked before it had moved.
                    if (seg == from && s <= fromS + car.Reach + 1f) continue;

                    plans++;
                    if (!LaneRoutes.Plan(graph, from, seg, car.Turns)) continue;

                    car.Segment = from;
                    car.S = fromS;
                    car.StopAtS = s;
                    car.Leg = 0;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The reverse: from where it stands to a lane that runs off the same edge.</summary>
        private bool PlanOut(Car car, bool edgeSouth)
        {
            var graph = _traffic.Graph;
            EdgeEntries(edgeSouth, entry: false);

            for (int e = 0; e < _edge.Count && e < MostPlans; e++)
            {
                if (!LaneRoutes.Plan(graph, car.Segment, _edge[e], car.Turns)) continue;
                car.Leg = 0;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Every lane segment whose road passes within <see cref="SceneReach"/> of this point,
        /// with the travel coordinate of the nearest spot on it, nearest first.
        ///
        /// The projection is `LaneRoutes.NearestSegment`'s own, arc → point → axis coordinate →
        /// `TravelOf`, kept identical because FromS/ToS are travel-signed AXIS coordinates while
        /// `RoadPath.Project` returns ARC length, and the two disagree on every curve.
        /// </summary>
        private void CandidatesNear(Vec2 point)
        {
            _candidates.Clear();

            var graph = _traffic.Graph;
            var roads = _world.Roads;

            for (int i = 0; i < graph.Segments.Count; i++)
            {
                var seg = graph.Segments[i];
                var line = roads.Lines[seg.Line];
                if (line.Path == null) continue;

                var (arc, lateral) = line.Path.Project(point);
                var on = line.Path.PointAt(arc);
                float axis = line.IsNorthSouth ? on.Y : on.X;
                float travel = LaneGraph.TravelOf(seg.Way, axis);
                if (travel < seg.FromS || travel > seg.ToS) continue;

                float d = lateral < 0f ? -lateral : lateral;
                if (d > SceneReach) continue;

                _candidates.Add((i, travel, d));
            }

            _candidates.Sort((a, b) => a.Lateral.CompareTo(b.Lateral));
        }

        /// <summary>
        /// The lanes at one edge of the map, the extreme one first: entries to come in on, or
        /// exits to leave by.
        ///
        /// Village y increases SOUTH and world z is its negation, so the southmost lane is the one
        /// with the greatest village y. The extreme is what the brief calls for and what the
        /// county would use — Route 1 owns both edges of this map — and the rest of the band is
        /// only ever reached when the extreme one cannot be routed to.
        /// </summary>
        private void EdgeEntries(bool south, bool entry)
        {
            var graph = _traffic.Graph;

            _edge.Clear();
            if (entry) { for (int i = 0; i < graph.Entries.Count; i++) _edge.Add(graph.Entries[i]); }
            else
            {
                for (int i = 0; i < graph.Segments.Count; i++)
                    if (graph.Segments[i].IsExit) _edge.Add(i);
            }
            if (_edge.Count == 0) return;

            _edge.Sort((a, b) =>
            {
                float ya = Southness(a, entry), yb = Southness(b, entry);
                return south ? yb.CompareTo(ya) : ya.CompareTo(yb);
            });

            float best = Southness(_edge[0], entry);
            for (int i = _edge.Count - 1; i >= LeastEdgeEntries; i--)
            {
                float d = Southness(_edge[i], entry) - best;
                if (d < 0f) d = -d;
                if (d > EdgeBand) _edge.RemoveAt(i);
            }
        }

        /// <summary>Village y of a lane's outer end — its start for an entry, its finish for an
        /// exit. World -z, as everywhere.</summary>
        private float Southness(int segment, bool entry)
        {
            var seg = _traffic.Graph.Segments[segment];
            return -_traffic.PointOn(segment, entry ? seg.FromS : seg.ToS).z;
        }

        // ---- driving --------------------------------------------------------------------

        private void Update()
        {
            // Unity's own ==, not `??=`, so a host destroyed and rebuilt between scenes is picked
            // up again rather than left as a "fake null" the C# operators walk past.
            if (_host == null) _host = VillageHost.Instance;
            if (_host == null || _host.Sim == null) return;

            long now = _host.Sim.Clock.Tick;
            if (_lastTick < 0) { _lastTick = now; return; }

            float dtSim = (now - _lastTick) / (float)GameClock.TicksPerSecond;
            _lastTick = now;
            if (dtSim <= 0f) return;                      // paused, or the same tick twice

            int steps = 0;
            while (dtSim > 0f && steps++ < MostSteps)
            {
                float slice = Mathf.Min(Step, dtSim);
                dtSim -= slice;
                for (int i = 0; i < _cars.Length; i++) Advance(i, slice);
            }
        }

        private void Advance(int slot, float dt)
        {
            var car = _cars[slot];
            if (car == null) return;

            // The un-routable case: no drive is drawn, the clock still runs, and the arrival is
            // reported. See OffStageSeconds.
            if (car.OffStage >= 0f)
            {
                car.OffStage -= dt;
                if (car.OffStage <= 0f) Park(car);
                return;
            }

            if (car.Arrived || car.What == null) return;
            if (_traffic == null || _traffic.Graph == null) return;

            if (car.InTurn) CrossJunction(slot, car, dt);
            else RunSegment(slot, car, dt);

            // Destroyed by the branch above, or parked by it — Park has already put it where it
            // stands and Seat would undo the offset.
            if (_cars[slot] == null || car.Arrived || car.What == null) return;
            Seat(car);
        }

        /// <summary>Along a straight piece of lane, up to the stop beside the scene or the end.</summary>
        private void RunSegment(int slot, Car car, float dt)
        {
            var segment = _traffic.Graph.Segments[car.Segment];
            float step = ResponseSpeed * dt;

            bool last = car.Leg >= car.Turns.Count;

            if (last && !car.Leaving)
            {
                // THE NOSE STOPS AT THE SCENE, NOT THE MIDDLE OF THE CAR — `RunSegment`'s own
                // arithmetic (`CityTraffic.cs:1039`), for the same reason it is written there:
                // `S` is the car's CENTRE, so easing the centre to the stop leaves the front
                // `Reach` metres past it. The taper is the same one a car eases to a stop line
                // with, so a response vehicle arrives rather than stopping dead on the spot.
                float toStop = car.StopAtS - car.S;
                float allowed = Mathf.Max(0f, toStop - car.Reach - 0.4f);
                step = Mathf.Min(step, allowed, Mathf.Max(step * (toStop / Braking), 0f));

                if (allowed <= 0.05f) { Park(car); return; }
            }

            if (Held(car)) return;

            car.S += step;
            if (car.S < segment.ToS) return;

            if (last)
            {
                // Off the end of the last lane: away over the edge of the map if it is leaving,
                // and otherwise a stop it drove past — park where it stands rather than carry on
                // into a lane nothing planned.
                if (car.Leaving) { Despawn(slot); return; }
                car.S = segment.ToS;
                Park(car);
                return;
            }

            car.InTurn = true;
            car.T = 0f;
        }

        /// <summary>Through the junction on the next planned turn.</summary>
        private void CrossJunction(int slot, Car car, float dt)
        {
            int turn = car.Turns[car.Leg];

            // The chord rather than the arc: a quadratic through three points a junction wide is
            // barely longer than the straight line between its ends, and the difference is a few
            // tenths of a second of crossing time at this scale.
            _traffic.TurnArc(turn, out var a, out _, out var c);
            float length = Mathf.Max(0.5f, Vector3.Distance(a, c));

            if (Held(car)) return;

            car.T += ResponseSpeed * dt / length;
            if (car.T < 1f) return;

            var to = _traffic.Graph.Segments[_traffic.Graph.Turns[turn].To];
            car.Segment = to.Index;
            car.S = to.FromS;
            car.Leg++;
            car.InTurn = false;
            car.T = 0f;
        }

        /// <summary>
        /// Is there something close in front, going roughly this way?
        ///
        /// The moving half is <see cref="CityTraffic.AnyMoverWithin"/>, which is `Blocked`'s own
        /// loop — parallel-only, because cross traffic at a junction is the signals' business and
        /// counting it stops a car dead in a box it has already claimed. The standing half is the
        /// obstacle list, which has no heading to compare against, so it is judged on the box
        /// alone. A response vehicle skips ITSELF and nothing else: the other one, parked at the
        /// same scene, is exactly the sort of thing worth not driving into.
        /// </summary>
        private bool Held(Car car)
        {
            var at = car.What.position;

            if (_traffic.AnyMoverWithin(at, car.Forward, car.Reach)) return true;

            for (int i = 0; i < _traffic.Obstacles.Count; i++)
            {
                var what = _traffic.Obstacles[i];
                if (what == null || what == car.What) continue;

                var gap = what.position - at;
                float ahead = Vector3.Dot(gap, car.Forward);
                if (ahead <= 0f) continue;
                if (ahead > car.Reach + ObstacleReach + Headway) continue;
                if (Vector3.Cross(car.Forward, gap).magnitude > LookWide) continue;
                return true;
            }
            return false;
        }

        /// <summary>Put the vehicle where its position says it is, pointing where it is going —
        /// <see cref="CityTraffic"/>'s `Seat`, through the public wrappers.</summary>
        private void Seat(Car car)
        {
            Vector3 at, ahead;

            if (car.InTurn)
            {
                int turn = car.Turns[car.Leg];
                at = _traffic.PointInTurn(turn, car.T);
                ahead = _traffic.PointInTurn(turn, Mathf.Min(1f, car.T + 0.05f));
                if ((ahead - at).sqrMagnitude < 0.0001f) ahead = at + car.Forward;
            }
            else
            {
                at = _traffic.PointOn(car.Segment, car.S);
                ahead = _traffic.PointOn(car.Segment, car.S + 1f);
            }

            var forward = ahead - at;
            if (forward.sqrMagnitude > 0.0001f) car.Forward = forward.normalized;

            car.What.position = at;
            car.What.rotation = Quaternion.LookRotation(car.Forward, Vector3.up);
        }

        /// <summary>
        /// Stop, pull over, and tell the case it is here.
        ///
        /// HALF A LANE TOWARD THE VERGE, MEASURED RATHER THAN TYPED: `CityStreets.LaneOffset` is
        /// the distance from the centre line to the middle of a lane, so its lane-0 value is
        /// exactly half a lane width off the asphalt this road was actually paved with. A vehicle
        /// answering a call stands at the kerb, not in the running lane — and once it is there it
        /// joins <see cref="CityTraffic.Obstacles"/>, so the ambient fleet goes round it.
        /// </summary>
        private void Park(Car car)
        {
            car.Arrived = true;
            car.OffStage = -1f;

            if (car.What != null)
            {
                var line = _world.Roads.Lines[_traffic.Graph.Segments[car.Segment].Line];
                car.What.position += car.What.right * CityStreets.LaneOffset(line.Class, 0);
                car.ParkedAt = car.What.position;

                if (!_traffic.Obstacles.Contains(car.What)) _traffic.Obstacles.Add(car.What);
            }
            else car.ParkedAt = car.SceneAt;

            // Nulled BEFORE it fires, so a callback that orders this vehicle away — or sends it
            // somewhere else — cannot be answered by a second arrival.
            var fire = car.OnArrived;
            car.OnArrived = null;
            fire?.Invoke();
        }

        private void Despawn(int slot)
        {
            var car = _cars[slot];
            if (car == null) return;

            if (car.What != null)
            {
                if (_traffic != null) _traffic.Obstacles.Remove(car.What);
                Destroy(car.What.gameObject);
            }
            _cars[slot] = null;
        }

        private void OnDestroy()
        {
            if (_traffic == null) return;
            for (int i = 0; i < _cars.Length; i++)
                if (_cars[i] != null && _cars[i].What != null)
                    _traffic.Obstacles.Remove(_cars[i].What);
        }

        // ---- the vehicle itself ---------------------------------------------------------

        /// <summary>
        /// Give the car a body. In the editor that is the pack's prefab; anywhere else it is a
        /// bare GameObject — INVISIBLE BUT REAL, so the drive, the arrival, the obstacle it makes
        /// of itself and the case it is answering all happen identically in a shipped build. What
        /// is missing is the mesh, and <see cref="Create"/> has already said so.
        /// </summary>
        private void Dress(Car car, Rig rig)
        {
            GameObject go = null;
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabOf(rig));
            if (prefab != null) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
#endif
            // Unity's own == , not `??=`: a destroyed or unloadable prefab instance is a "fake
            // null" that the C# null-coalescing operators walk straight past.
            if (go == null) go = new GameObject(NameOf(rig));
            go.name = NameOf(rig);
            go.transform.SetParent(transform, false);

            car.What = go.transform;
            car.Reach = LengthOf(go) * 0.5f;
        }

        /// <summary>A vehicle's length nose to tail, off the mesh — <see cref="CityTraffic"/>'s
        /// `Length`, restated because it is private there. These are modelled facing +z.</summary>
        private static float LengthOf(GameObject car)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var mf in car.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                lo = Mathf.Min(lo, b.min.z);
                hi = Mathf.Max(hi, b.max.z);
            }
            return hi > lo ? hi - lo : DefaultReach * 2f;
        }
    }
}
