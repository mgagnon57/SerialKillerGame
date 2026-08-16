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
        /// <summary>Metres per SIM second. A town speed, not a motorway one.</summary>
        private const float Speed = 8f;

        /// <summary>Slower round a corner, as anybody is.</summary>
        private const float TurnSpeed = 5f;

        /// <summary>
        /// The gap a driver keeps beyond the two vehicles' own half-lengths, and how far off the
        /// centre line another vehicle still counts as being in this lane.
        ///
        /// THIS USED TO BE A FLAT EIGHT METRES from centre to centre, measured when the longest
        /// thing on the road was a hatchback. An articulated lorry is longer than eight metres by
        /// itself, so a car could be fully inside one and register nothing - which is exactly
        /// what NoTwoVehiclesOccupyTheSameSpace caught: a sports car and a sleeper-cab artic at
        /// 0.00m. Adding both half-lengths makes a lorry keep a lorry's distance and a hatchback
        /// a hatchback's, without either number being typed.
        /// </summary>
        private const float Headway = 3.5f, LookWide = 2.4f;

        /// <summary>Where a car begins easing off for a red light.</summary>
        private const float Braking = 14f;

        /// <summary>
        /// The pace an apparently stopped car is credited with when working out when it arrives.
        ///
        /// A CAR THAT IS NOT MOVING IS NOT COMING, and the old rules could not say so: both asked
        /// only how FAR away the nearest car was, so a queue standing at its own red light thirty
        /// metres up the crossing road blocked this junction for as long as it stood there. That
        /// is most of why the traffic starved. Time-to-arrival says it properly - a stopped car is
        /// infinitely far away in time - but crediting a genuinely stationary car with zero pace
        /// would let somebody pull out in front of a car about to move off, so it is floored here
        /// instead. Two metres a second: a car five metres back still counts as arriving in two and
        /// a half seconds, and one thirty metres back does not block at all.
        /// </summary>
        private const float Creep = 2f;

        /// <summary>
        /// How close two turn arcs have to pass before they are treated as the same ground.
        ///
        /// THIS IS A PATH-IDENTITY THRESHOLD, NOT A VEHICLE CLEARANCE, and the difference matters
        /// because the first guess at it was the second thing. It was 4.5m, reasoned from the
        /// pack's widest vehicle being 2.6m across - which sounds careful and is asking the wrong
        /// question: whether two cars would touch is decided by how far apart the paths are, and
        /// what this has to decide is whether two paths are the SAME path.
        ///
        /// MEASURED, over all 5,946 candidate pairs on the 84 junctions:
        ///
        ///     0-1m  2408    3-4m    17    6-8m  1639
        ///     1-2m   260    4-5m   162    8-12m  247
        ///     2-3m     0    5-6m   230    12+    983
        ///
        /// The geometry is bimodal and there is a CLEAN EMPTY BAND at two to three metres. Arcs
        /// either genuinely intersect, or they clear each other by three metres and more; nothing
        /// in this city lands in between. So any value in [2, 3] marks exactly the same 2,668
        /// pairs, and the constant is insensitive across a full metre rather than being a number
        /// somebody chose. The midpoint is taken for that reason.
        ///
        /// WHAT THE OLD 4.5m COST was 179 pairs that do not cross at all, and they were all one
        /// thing: opposing LEFT turns, lane 0 to lane 0, whose arcs pass 4.24m apart. Two cars
        /// turning left past each other from opposite arms is an ordinary simultaneous movement
        /// at a real junction, and 4.24m of separation is 1.6m of daylight for the widest pair of
        /// vehicles in the pack. Forbidding it made left turns - already the movement that waits
        /// for a gap - queue behind each other as well.
        /// </summary>
        private const float Clearance = 2.5f;

        /// <summary>
        /// Half a lane, for deciding when a turning car is clear of somebody else's lane rather
        /// than merely past the middle of it.
        /// </summary>
        private const float LaneHalf = 3f;

        /// <summary>
        /// How long a driver persists with a movement before deciding on a different one.
        ///
        /// A DRIVER WHO CANNOT TURN LEFT EVENTUALLY GOES STRAIGHT ON, and that is the whole of
        /// this. `Choose` is deterministic per car and junction, so a car that draws a left turn
        /// into a stream that never breaks will sit there wanting that same left turn until the
        /// heat death of the city - re-asking the question every frame and getting the same
        /// answer. Twelve seconds is about when a real driver gives up and goes round the block.
        ///
        /// THIS IS NOT THE `Patience` TIMEOUT THAT WAS TRIED AND REVERTED. That one let a car
        /// cross ANYWAY, which is to say it bypassed the only rule keeping the streams apart, and
        /// it put two vehicles in the same space. This bypasses nothing: the car still has to
        /// satisfy the signal, the claim on the junction, the room on the far side and the gap.
        /// All it changes is WHERE THE CAR WANTS TO GO, which no safety rule depends on.
        /// </summary>
        private const float Rethink = 12f;

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
        /// AND WHY IT IS NO LONGER 1.5. That figure was a fudge and said so: the downtown had
        /// grown from nine blocks to thirty-six without the traffic moving at all - ninety-seven
        /// vehicles before and ninety-seven after - because a `district` is an OPEN place with no
        /// residents, so twenty-seven new blocks added nothing to the count this is drawn from.
        /// Rather than fix that, the multiplier was inflated past one car per household to make
        /// the roads look right, which is a number covering for a different number being wrong.
        ///
        /// A district now says how many live in it and the budget reads
        /// <see cref="WorldModel.DeclaredHouseholds"/>, so the multiplier can go back to meaning
        /// something: not how many cars a household OWNS, but how many are ON THE ROAD AT ONCE.
        /// Roughly one household in four - the rest are parked, garaged, in a lot somewhere, or
        /// were never a car-owning household to begin with. That is why it is well under one, and
        /// why it no longer has to move when the city grows: the household count moves instead.
        ///
        /// IT IS A CURVE NOW, AND IT USED TO BE `CarsOutPerHousehold = 0.25f` - FLAT, ALL DAY.
        /// That constant was reasoned rather than measured, and it produced 159 cars from 624
        /// households: one household in four on the road at once, where IDOT's counts of
        /// Rossville by name put **~19 moving at an average instant and ~46 in the peak hour**.
        /// One in thirty-three. Eight times too many at the average, three and a half at the peak,
        /// and flat when the real thing is a curve.
        ///
        /// The docstring that held it at 0.25 gave two reasons to wait, and by 2026-08-10 only one
        /// of them was still true:
        ///
        ///  - "the class weighting is 4:1 where the county measures 21:1, so a small fleet would
        ///    spread evenly and the town would read as empty everywhere instead of quiet with a
        ///    busy Route 1." **That landed.** `RoadClasses.AmbientTrafficWeight(RoadLine)` reads
        ///    the county's own AADT, and the spawn pitch below asks it of the ROAD. So the cars
        ///    that remain concentrate where the traffic really is, which is what makes a fleet
        ///    this small survive being looked at.
        ///  - "it should stop being a constant - the real curve peaks when the town leaves for
        ///    Hoopeston and Danville." That is this.
        ///
        /// THE SHAPE IS THE COMMUTE. Rossville is a bedroom village on the Route 1 corridor
        /// between Danville, fourteen miles south, and Hoopeston, eight miles north; 75% drive
        /// alone and the mean travel time to work is 23.6 minutes. So the town empties at six and
        /// seven, fills again at four and five, and does very little in between - which is a
        /// different town at 07:00 from the one at 02:00, and neither of them is the flat 159.
        ///
        /// A TABLE, NOT A FORMULA, for the same reason `Daylight` embeds 365 entries: a curve you
        /// can read and correct beats one you have to run to find out what it does. These are
        /// absolute cars for the 624 households IDOT's counts were taken against, scaled linearly
        /// off <see cref="WorldModel.DeclaredHouseholds"/> so the number still moves when the town
        /// does. Sums to 464 over 24 hours - a mean of 19.3, against the measured 19.3.
        ///
        /// Full working and the IDOT query in docs/research/TRAFFIC-COUNTS.md.
        /// </summary>
        private static readonly int[] CarsOutByHour =
        {
            //  00  01  02  03  04  05
                 3,  2,  2,  3,  5, 12,
            //  06  07  08  09  10  11     06-07: out to Hoopeston and Danville
                32, 46, 34, 22, 19, 20,
            //  12  13  14  15  16  17     15: school out. 16-17: the shift home
                24, 20, 19, 26, 38, 46,
            //  18  19  20  21  22  23
                34, 22, 15, 10,  6,  4,
        };

        /// <summary>The household count IDOT's figures were measured against.</summary>
        private const int MeasuredHouseholds = 624;

        /// <summary>How many vehicles should be moving at this minute of the day.</summary>
        public static int CarsOutAt(int minuteOfDay, int households)
        {
            int hour = Mathf.Clamp(minuteOfDay / 60, 0, 23);
            float scale = households / (float)MeasuredHouseholds;
            return Mathf.Max(1, Mathf.RoundToInt(CarsOutByHour[hour] * scale));
        }

        /// <summary>
        /// The fleet actually instantiated: the busiest hour, because a car is built once and then
        /// parked in the garage for the hours it is not wanted. Spawning and destroying on the hour
        /// would mean `PrefabUtility.InstantiatePrefab` on the main thread twice a day per car.
        /// </summary>
        public static int PeakCarsOut(int households)
        {
            int peak = 0;
            foreach (int n in CarsOutByHour) if (n > peak) peak = n;
            return Mathf.Max(1, Mathf.RoundToInt(peak * (households / (float)MeasuredHouseholds)));
        }

        private sealed class Mover
        {
            public Transform What;
            public int Segment;        // where it is now
            public float S;            // travel coordinate along that segment
            public int Turn = -1;      // >= 0 while it is crossing a junction
            public float T;            // 0..1 through the turn
            public Vector3 Forward = Vector3.forward;

            /// <summary>Half this vehicle's MEASURED length, nose to tail.</summary>
            public float Reach = 2.2f;

            /// <summary>
            /// Metres per second actually covered last frame, which is not the same as
            /// <see cref="Speed"/>: a car easing down to a stop line, held behind somebody, or
            /// standing at a red is doing less, and how long it will take to arrive somewhere is
            /// a question about what it is doing rather than about what it is capable of.
            /// </summary>
            public float Pace;

            public int Choices;        // bumped each junction, so its route is not a loop

            /// <summary>Seconds held at this stop line. See <see cref="Rethink"/>.</summary>
            public float Waited;

            /// <summary>
            /// Seconds held WITHOUT the reset that <see cref="Rethink"/> performs, so it keeps
            /// counting across a change of mind about which way to go.
            ///
            /// Waited cannot express real impatience because it is zeroed every twelve seconds
            /// by the rethink, so it never reads higher than twelve however long the car has
            /// actually been sitting there. This is the number that says "this driver has been
            /// at this stop line for a minute and a half", and a minute and a half is a driver
            /// who takes the gap briskly. See <see cref="TurnPace"/>.
            /// </summary>
            public float Held;

            /// <summary>What stopped it last frame. Instrumentation; nothing steers by it.</summary>
            public Hold Why;
        }

        /// <summary>
        /// Why a car did not move. There is no way to tell from outside - a stationary car looks
        /// identical whichever rule is holding it - and guessing which one it was is how two
        /// wrong fixes got written.
        /// </summary>
        public enum Hold
        {
            None, Following, Signal, GiveWay, Oncoming, TurnBusy, NoRoomBeyond, NoLegalTurn, Arriving,
        }

        /// <summary>A tally of what is holding the traffic up right now, for the diagnostics.</summary>
        public string Holds()
        {
            var tally = new Dictionary<Hold, int>();
            int still = 0;

            foreach (var mover in _movers)
            {
                if (mover.What == null) continue;
                if (mover.Pace > 0.05f) continue;
                still++;
                tally[mover.Why] = tally.TryGetValue(mover.Why, out int was) ? was + 1 : 1;
            }

            var parts = new List<string>();
            foreach (var pair in tally) parts.Add($"{pair.Key}={pair.Value}");
            parts.Sort(System.StringComparer.Ordinal);
            return $"{still} of {_movers.Count} stopped: {string.Join(", ", parts)}";
        }

        private readonly List<Mover> _movers = new List<Mover>();

        /// <summary>
        /// Vehicles that exist but are not out at this hour.
        ///
        /// A SECOND LIST RATHER THAN A FLAG ON `Mover`, and that is the whole design. Eight loops
        /// in this file walk `_movers` to decide who is following whom, who has room to pull out
        /// and who is about to be driven into - `RoomAt`, `Ahead`, the junction checks. A flag
        /// would need a guard in every one of them and would be wrong the first time somebody
        /// added a ninth. Keeping `_movers` as exactly "the cars on the road right now" means all
        /// eight are correct without being touched.
        /// </summary>
        private readonly List<Mover> _garage = new List<Mover>();

        /// <summary>Things ambient cars must not drive through — the player's parked car, and
        /// every response vehicle from the moment it spawns to the moment it despawns, MOVING
        /// INCLUDED: `CityResponse` registers its rigs here at spawn, because this list and
        /// `_movers` are the whole of what `Blocked` consults and a vehicle in neither is one no
        /// ambient car can see. Phase 1 named this seam ("a second obstacle source those five
        /// queries consult") and the landing is still minimal — FOLLOWING only, and
        /// heading-blind: a box test against a bare Transform, which carries no travel
        /// direction to judge by.</summary>
        public readonly List<Transform> Obstacles = new List<Transform>();

        private int _households;
        private int _retimedAt = -1;

        /// <summary>How many are out, and how many are put away. Instrumentation.</summary>
        public string Fleet => $"{_movers.Count} out, {_garage.Count} garaged";

        /// <summary>
        /// Match the fleet to the hour. Cheap and idempotent within a minute, so it is safe to
        /// call every frame; it does its work only when the clock actually moves.
        ///
        /// NOT called from Update. The per-frame loop indexes `_movers` by position, and both
        /// ends of this method change that list - see the `for (int i = 0; i < _movers.Count; i++)`
        /// walk, which would step over a car or off the end. VillageHost drives it, the same way
        /// it already drives the driveways off `Sim.Clock.MinuteOfDay`.
        /// </summary>
        public void Retime(int minuteOfDay)
        {
            if (minuteOfDay == _retimedAt) return;
            _retimedAt = minuteOfDay;

            int want = CarsOutAt(minuteOfDay, _households);

            while (_movers.Count > want && _movers.Count > 0)
            {
                // From the END, because the front of the list is the oldest and most likely to be
                // mid-junction. A car vanishing out of a turn is the one removal somebody watching
                // would actually catch.
                var put = _movers[_movers.Count - 1];
                _movers.RemoveAt(_movers.Count - 1);
                if (put.What != null) put.What.gameObject.SetActive(false);
                _garage.Add(put);
            }

            while (_movers.Count < want && _garage.Count > 0)
            {
                var out_ = _garage[_garage.Count - 1];
                _garage.RemoveAt(_garage.Count - 1);
                if (out_.What == null) continue;

                // Added BEFORE Reintroduce, which is what makes RoomAt able to skip it: that guard
                // is `other == me`, and a car not yet in the list is not `me` to anybody.
                _movers.Add(out_);
                out_.What.gameObject.SetActive(true);
                out_.Turn = -1;
                out_.Waited = 0f;
                out_.Held = 0f;
                Reintroduce(out_);
                Seat(out_);
            }
        }
        private CitySignals _signals;
        private WorldModel _world;

        /// <summary>
        /// The lane graph the traffic runs on. Public because the bus routes will want the same
        /// one rather than a second copy that can disagree with it.
        /// </summary>
        public LaneGraph Graph { get; private set; }

        /// <summary>
        /// For each turn, the other turns through the same junction whose path it crosses.
        ///
        /// WHY THIS HAD TO EXIST. Nothing in this file ever tested whether two cars were about to
        /// occupy the same piece of junction. `Blocked()` looks like it does and does not: it
        /// projects a box 2.4m wide straight down the car's own heading, which is a FOLLOWING
        /// model. Two cars on crossing paths are not in front of one another until they are
        /// practically touching, so that box cannot separate them and never could. Measured on the
        /// live city before this existed, crossing pairs shared a junction for 17,395 frames and
        /// came within 2.84m of each other - two lorries at 2.84m are all but in contact.
        ///
        /// So the streams were being kept apart ENTIRELY by the signals and by the give-way rules,
        /// which is why the earlier `Patience` timeout produced a real collision the moment it let
        /// a car pull out anyway: it removed the only separation there was and leaned on a safety
        /// net that was never there. This IS that net, and it is what makes the gap rules below
        /// safe to relax.
        /// </summary>
        private int[][] _conflicts;

        public static CityTraffic Create(WorldModel world, Transform parent, CitySignals signals)
        {
            var go = new GameObject("CityTraffic");
            go.transform.SetParent(parent, false);
            var traffic = go.AddComponent<CityTraffic>();
            traffic._signals = signals;
            traffic._world = world;
            traffic.Graph = new LaneGraph(world.Roads, world.Width, world.Height);
            traffic.MapConflicts();
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

            // A ROAD THAT BENDS HAS NO SINGLE CENTRE to measure across from, and reading the
            // scalar one anyway is not a small error - it is a phantom straight road. Chicago
            // Street's Centre is the x of its FIRST point, x=314 up at the north edge, so every
            // car on the curve was drawn strung out along a line at x=327 while the asphalt it
            // was supposed to be on ran through x=735 at that latitude. The PlayMode suite read
            // it exactly right: "999.00m past the asphalt".
            //
            // Ask the path instead - and ask it for the arc length too. This read
            // `along - line.From`, which assumes arc length grows the way the declared axis
            // does; a curve declared high-to-low runs the other way and every car on it was
            // drawn at the far end of the road. See RoadPath.ArcAt, which is now the only place
            // that conversion is written down.
            if (!line.IsStraight && line.Path != null)
            {
                float arc = line.Path.ArcAt(along, line.IsNorthSouth);

                // THE MARGIN RUNS OFF THE END OF THE PATH, and RoadPath.PointAt clamps.
                //
                // LaneGraph extends every lane thirty metres past the map edge so traffic
                // arrives from off-stage rather than appearing out of nothing - so a segment on
                // Chicago Street legitimately runs to arc 2516 on a path 2486 long. Clamping put
                // every car in that margin on the same endpoint: they stacked up at one point
                // ("came within 0.00m") and sat where no asphalt was laid.
                //
                // Off the end, carry on in a straight line along the tangent there, which is
                // what a road running off the edge of the world should look like anyway.
                var at = line.Path.PointAt(arc);
                var normal = line.Path.NormalAt(arc);

                if (arc < 0f || arc > line.Path.Length)
                {
                    float edge = arc < 0f ? 0f : line.Path.Length;
                    var tangent = line.Path.TangentAt(edge);
                    var end = line.Path.PointAt(edge);
                    float past = arc - edge;
                    at = new Noir.Core.Contracts.Vec2(end.X + tangent.X * past,
                                                      end.Y + tangent.Y * past);
                    normal = line.Path.NormalAt(edge);
                }

                // The offset is measured PERPENDICULAR to the road here rather than along an
                // axis - which for a straight road is the same thing, and for a bend is the only
                // thing that means anything. Side() still decides which side of the centre line
                // the direction of travel keeps to; the normal only says which way that is here.
                // ASKED OF THE PATH, NOT OF THE COMPASS. `Headings.Side` answers in COORDINATES -
                // +1 is "the greater x or the greater y" - and `NormalAt` is the right-hand side of
                // the PATH's own direction, so multiplying them counts the handedness twice
                // wherever the local tangent has left the quadrant its declared heading names.
                // Measured: 5,438 of 13,154 lane positions in the ONCOMING carriageway, across 27
                // roads. See Headings.SideOfPath and CarsKeepRightOnABendTests, which was watched
                // failing on this exact line before it was changed.
                float offset = Headings.SideOfPath(line.Path, arc, segment.Way)
                             * CityStreets.LaneOffset(line.Class, segment.Lane);
                float vx = at.X + normal.X * offset;
                float vy = at.Y + normal.Y * offset;

                return new Vector3(vx, ElevationGrid.HeightAt(vx, vy), -vy);
            }

            float cross = CrossOf(segment);

            return line.IsNorthSouth
                ? new Vector3(cross, ElevationGrid.HeightAt(cross, along), -along)
                : new Vector3(along, ElevationGrid.HeightAt(along, cross), -cross);
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

            // WHERE THE TWO LANE CENTRE LINES WOULD MEET.
            //
            // This used to be "one lane holds x and the other holds z, so take one coordinate
            // from each", which is exact while every road is axis-aligned and meaningless the
            // moment one is not. Chicago Street crosses the grid about seventeen degrees off
            // vertical, so a.x was not the corner's x and cars cut turns onto ground that is not
            // road - reported by the PlayMode suite as 8.99m past the asphalt.
            //
            // Intersecting the two tangents is the same answer for a right-angled crossing and
            // the right one for an oblique. Height is not taken from either: a and c already
            // carry real terrain height, and a corner between them is close enough to flat over
            // a single junction that their average is the right ground.
            float midY = (a.y + c.y) * 0.5f;

            var dirA = PointOn(from, from.ToS) - PointOn(from, from.ToS - 1f);
            var dirC = PointOn(to, to.FromS + 1f) - PointOn(to, to.FromS);

            float ax = a.x, az = a.z, cx = c.x, cz = c.z;
            float denom = dirA.x * dirC.z - dirA.z * dirC.x;

            if (Mathf.Abs(denom) < 1e-4f)
            {
                // Near-parallel: there is no corner worth cutting, so run straight between them.
                b = new Vector3((ax + cx) * 0.5f, midY, (az + cz) * 0.5f);
                return;
            }

            float t = ((cx - ax) * dirC.z - (cz - az) * dirC.x) / denom;
            b = new Vector3(ax + dirA.x * t, midY, az + dirA.z * t);
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

        /// <summary>
        /// Which turns through a junction cross which others, worked out once from the arcs
        /// themselves rather than from a table of movements somebody wrote down.
        ///
        /// Deriving it from the geometry is what keeps it honest when the map changes: reclassify
        /// a road, move a lane, widen a carriageway, and the conflicts follow, because they are
        /// measured off the same arcs the cars actually drive. A hand-written table of "a left
        /// turn conflicts with the opposing straight" would be right for a four-arm crossroads
        /// and quietly wrong at the two three-way junctions this map already has.
        /// </summary>
        private void MapConflicts()
        {
            const int Steps = 12;

            int n = Graph.Turns.Count;
            _conflicts = new int[n][];
            if (n == 0) return;

            var path = new Vector3[n][];
            for (int t = 0; t < n; t++)
            {
                TurnArc(Graph.Turns[t], out var a, out var b, out var c);
                var points = new Vector3[Steps + 1];
                for (int i = 0; i <= Steps; i++) points[i] = Bezier(a, b, c, i / (float)Steps);
                path[t] = points;
            }

            // Only turns through the same junction can possibly meet.
            var byJunction = new Dictionary<int, List<int>>();
            for (int t = 0; t < n; t++)
            {
                int j = Graph.Turns[t].Junction;
                if (!byJunction.TryGetValue(j, out var list)) byJunction[j] = list = new List<int>();
                list.Add(t);
            }

            var found = new List<int>[n];
            int pairs = 0, considered = 0;

            // How close every candidate pair actually comes, so the threshold above can be
            // checked against the geometry instead of being asserted. See the log line below.
            var bands = new int[9];
            float tightest = float.MaxValue;
            string closest = "nothing";

            foreach (var list in byJunction.Values)
            for (int x = 0; x < list.Count; x++)
            for (int y = x + 1; y < list.Count; y++)
            {
                int p = list[x], q = list[y];

                // CARS LEAVING THE SAME LANE ARE IN SINGLE FILE and following distance already
                // holds them apart - inside the junction as well as outside it, since Blocked()
                // keeps a car off the back of the one on the same arc in front. Calling those a
                // conflict would make every car wait for the whole junction to empty before the
                // one behind it could set off, which is a worse jam than the one being fixed.
                if (Graph.Turns[p].From == Graph.Turns[q].From) continue;

                considered++;
                float apart = Separation(path[p], path[q]);
                bands[Band(apart)]++;

                if (apart >= Clearance)
                {
                    // The tightest pair anywhere in the city that is allowed to move at the same
                    // time. This is the safety number the threshold actually buys, and it is
                    // worth printing every run: if it ever drops towards a vehicle's width, two
                    // cars are being waved through the same piece of road.
                    if (apart < tightest)
                    {
                        tightest = apart;
                        var tp = Graph.Turns[p];
                        var tq = Graph.Turns[q];
                        closest = $"{tp.Kind} lane {Graph.Segments[tp.From].Lane}"
                                + $"->{Graph.Segments[tp.To].Lane} beside "
                                + $"{tq.Kind} lane {Graph.Segments[tq.From].Lane}"
                                + $"->{Graph.Segments[tq.To].Lane}";
                    }
                    continue;
                }

                (found[p] ??= new List<int>()).Add(q);
                (found[q] ??= new List<int>()).Add(p);
                pairs++;
            }

            for (int t = 0; t < n; t++)
                _conflicts[t] = found[t] != null ? found[t].ToArray() : System.Array.Empty<int>();

            Debug.Log($"[traffic] {pairs} conflicting turn pairs of {considered} considered, over "
                    + $"{byJunction.Count} junctions and {n} turns (threshold {Clearance}m).\n"
                    + $"[traffic] how close pairs come, in metres: "
                    + $"0-1:{bands[0]} 1-2:{bands[1]} 2-3:{bands[2]} 3-4:{bands[3]} 4-5:{bands[4]} "
                    + $"5-6:{bands[5]} 6-8:{bands[6]} 8-12:{bands[7]} 12+:{bands[8]}\n"
                    + $"[traffic] tightest pair allowed to move together: {tightest:0.00}m "
                    + $"({closest}).");
        }

        private static int Band(float d) =>
            d < 1f ? 0 : d < 2f ? 1 : d < 3f ? 2 : d < 4f ? 3 : d < 5f ? 4
          : d < 6f ? 5 : d < 8f ? 6 : d < 12f ? 7 : 8;

        /// <summary>The closest these two arcs come to each other, in metres.</summary>
        private static float Separation(Vector3[] a, Vector3[] b)
        {
            float closest = float.MaxValue;
            for (int i = 0; i < a.Length; i++)
            for (int j = 0; j < b.Length; j++)
            {
                float d = (a[i] - b[j]).sqrMagnitude;
                if (d < closest) closest = d;
            }
            return Mathf.Sqrt(closest);
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

            // THE PEAK, NOT THE HOUR THE TOWN HAPPENS TO BE BUILT AT. Every vehicle the day will
            // ever need is instantiated once here and then garaged; Retime moves them in and out
            // as the clock passes. Building only what noon wants would mean instantiating prefabs
            // at 06:00 on the main thread, which is the one time of day the frame is already busy.
            _households = world.DeclaredHouseholds;
            int budget = Mathf.Clamp(PeakCarsOut(_households),
                                     Mathf.Min(6, Graph.Segments.Count),
                                     Graph.Segments.Count);

            var counted = new Dictionary<RoadClass, int>();

            // WHERE THE CARS START, weighted the same way the driving is.
            //
            // Spreading evenly over segments hands each class traffic in proportion to how many
            // LANE SEGMENTS it has, which is not the same thing as how busy it should be: a
            // village has two main roads and sixteen residential streets, so an even spread put
            // one car on the highway and a hundred on the side streets. The driving now corrects
            // that over a few minutes, but a town should look right in the first frame rather
            // than settle into looking right, so the start is drawn from the same weights.
            var pitch = new List<int>();
            for (int i = 0; i < Graph.Segments.Count; i++)
            {
                // CARRIES, not Class: how busy a road is follows what it is FOR, not how wide it
                // was paved. See RoadLine.Carries - the route through this town is a county
                // highway on a village street's right of way.
                // Zero for an alley, so no car is ever introduced on a back lane - see
                // RoadClasses.AmbientTrafficWeight. The turn scoring reads the same table.
                //
                // AND IT IS THE COUNTY'S COUNT NOW, NOT THE CLASS. Asked of the road itself, so
                // Route 1 gets the 5,200 vehicles a day IDOT measured on it and Attica gets its
                // 1,100 - the class ladder called both of them `mainroad 8` and put 44.5% of the
                // town's traffic on side streets the county measures at 14-20%.
                int weight = RoadClasses.AmbientTrafficWeight(world.Roads.Lines[Graph.Segments[i].Line]);
                for (int w = 0; w < weight; w++) pitch.Add(i);
            }

            // ONE CAR TO A SEGMENT, WHICH THE WEIGHTING NEARLY THREW AWAY. The even spread got
            // this for free: 7919 is prime against the segment count, so it visited each segment
            // once. A weighted pitch lists a main road eight times over, so the same walk lands
            // on the same segment repeatedly - and two cars seated on one segment are not merely
            // close, they can be at 0.00m, which is the exact collision this suite already
            // caught once from Reintroduce. Sampling WITHOUT replacement keeps the weighting and
            // keeps the invariant: the busiest roads simply get picked first.
            var taken = new HashSet<int>();

            for (int n = 0, step = 0; n < budget && step < pitch.Count * 3; step++)
            {
                var segment = Graph.Segments[pitch[step * 7919 % pitch.Count]];
                if (!taken.Add(segment.Index)) continue;

                var onClass = world.Roads.Lines[segment.Line].Class;

                var cars = fleets[onClass];
                if (cars.Count == 0) continue;

                n++;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    cars[(int)(Materials3D.Scatter(n, 0, 811) % (uint)cars.Count)]);
                if (prefab == null) continue;

                counted[onClass] = counted.TryGetValue(onClass, out int was) ? was + 1 : 1;

                float s = Mathf.Lerp(segment.FromS, segment.ToS,
                                     Materials3D.Scatter(n, 1, 821) % 100 / 100f);

                // DO NOT "OPTIMISE" THIS INTO ONE MESH PER CAR. IT WAS TRIED AND MEASURED AND IT
                // MADE THE GAME HALF AS FAST.
                //
                // The reasoning was sound and the result was not: the pack ships a car as 11
                // MeshRenderers and 11 MeshColliders over 12 objects, so this fleet is ~1,750
                // renderers and ~1,750 mesh colliders being transformed every frame. Collapsing
                // each car with CarMesh.Flatten - which is a clear win for the 611 PARKED cars -
                // took the suite from 368 s to 688 s, and removing the kinematic Rigidbody that
                // came with it made it 728 s rather than better.
                //
                // The difference between the two fleets is that these MOVE and they are all drawn
                // from the same few prefabs. 159 instances of a shared mesh are GPU-instanced and
                // SRP-batched nearly for free; 159 UNIQUE merged meshes cannot be instanced at
                // all. Merging traded 1,750 cheap batched renderers for 159 expensive unbatched
                // ones. It also changed what `Length` measures - the merged bounds are the honest
                // car length where the old walk took each sub-mesh's own local bounds - which
                // moved every Reach and put two cars 0.26 m apart in
                // NoTwoVehiclesOccupyTheSameSpace.
                //
                // The mesh colliders are still worth removing on their own. That is a separate
                // change and it needs PerfHud pointed at it, not another guess. See docs/IDEAS.md.
                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                car.transform.SetParent(transform, false);

                var mover = new Mover
                {
                    What = car.transform,
                    Segment = segment.Index,
                    S = s,
                    Reach = Length(car) * 0.5f,
                };
                _movers.Add(mover);
                Seat(mover);
            }

            var byClass = new List<string>();
            foreach (var pair in counted)
                byClass.Add($"{pair.Value} on {pair.Key.ToString().ToLowerInvariant()}");

            Debug.Log($"[traffic] fleet built at the PEAK hour: {_movers.Count} vehicles for "
                    + $"{_households} households. IDOT measures ~19 moving at an average instant "
                    + $"and ~46 at peak; Retime holds it to the hour from there.");

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
                    // Only the ONE complete articulated unit. "Car_Truck_Trailer_Modern",
                    // "Car_Truck_Trailer_Car_Modern" and "Car_Truck_Trailer_Container_Large"
                    // were also in this list until they were checked against the pack directly:
                    // all three are BARE TRAILERS - rear wheels, no cab, no steering wheel - so a
                    // freeway that ran them span itself about one heavy in sixteen as a driverless
                    // box gliding down the road. "Car_Truck_Trailer_Sleepercab_Modern" is the only
                    // one of the four with a front axle and a windscreen.
                    "Car_Truck_Trailer_Sleepercab_Modern",
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

        /// <summary>
        /// How long a slice of driving is, in seconds.
        ///
        /// EVERY DECISION HERE IS MADE ONCE A FRAME, which quietly made the quality of the driving
        /// a property of the frame rate. A car looking for a gap gets one look per step: at sixty
        /// frames a second it gets sixty chances a second to take one, and on a frame long enough
        /// it gets a handful - so a busy junction that was passable becomes one where nobody ever
        /// catches the gap. That is not theoretical. Putting 365 rigged and animated people into
        /// the town lengthened the frame enough that one lane of the eastbound ring road starved:
        /// the median wait and the ninetieth percentile did not move at all, and the worst single
        /// wait went from 54 seconds to 120 - the whole observation window, three runs running,
        /// every one of them at x=1002.
        ///
        /// Thirtieths, and the rest carried, so the number of decisions a car gets per SECOND OF
        /// GAME TIME is the same whatever else is on screen. Exactly the fault AgentMeshView's
        /// leg swing had and for exactly the same reason: a renderer is entitled to know the frame
        /// rate, and nothing that decides anything is.
        /// </summary>
        private const float Slice = 1f / 30f;

        /// <summary>
        /// The most driving one frame may do, as slices.
        ///
        /// Sized for the 300x dial: at 60 fps one frame owes five TOWN seconds, which is 150
        /// slices, and a little headroom on top. Past that the traffic falls behind rather than
        /// trying to catch up, because catching up costs more frame time, which puts it further
        /// behind - and a spiral that ends in a locked editor is worse than a lorry arriving
        /// somewhere a little late. (It was 12 when a slice was a REAL thirtieth - the same
        /// philosophy, one clock earlier.)
        /// </summary>
        private const int MostSlices = 160;

        private float _owed;

        /// <summary>
        /// The sim clock's tick, pushed by VillageHost.Update every frame. -1 until first fed -
        /// a traffic system nobody feeds a clock does not move, which is what a preview town
        /// without a running sim should do. ONE CLOCK (owner, 2026-08-16, reversing the
        /// scenery-runs-on-Time.deltaTime ruling): the fleet advances by the SIM delta, so the
        /// speed dial scales cars and pedestrians together, and a paused town is paused.
        /// </summary>
        public long TownTick = -1;
        private long _lastTick = -1;

        private void Update()
        {
            if (Graph == null) return;
            if (TownTick < 0) return;
            if (_lastTick < 0) { _lastTick = TownTick; return; }

            float dtTown = (TownTick - _lastTick) / (float)GameClock.TicksPerSecond;
            _lastTick = TownTick;
            if (dtTown <= 0f) return;                     // paused, or the same tick twice

            _owed += dtTown;

            int slices = 0;
            while (_owed >= Slice && slices++ < MostSlices)
            {
                Drive(Slice);
                _owed -= Slice;
            }

            // Whatever is left over is dropped rather than banked, or a hitch is repaid over the
            // following second and the whole city surges.
            if (slices >= MostSlices) _owed = 0f;
        }

        private void Drive(float dt)
        {
            for (int i = 0; i < _movers.Count; i++)
            {
                var me = _movers[i];
                if (me.What == null) continue;

                var was = me.What.position;

                if (me.Turn >= 0) CrossJunction(me, i, dt);
                else RunSegment(me, i, dt);

                Seat(me);

                // MEASURED, not inferred from what the car was allowed to do. Everything that
                // asks when this car will arrive somewhere reads this, and a car easing down to a
                // stop line is not travelling at Speed however much its segment would permit.
                // Capped at Speed because Reintroduce TELEPORTS a car from one edge of the map to
                // another, and a car credited with nine hundred metres a second arrives
                // everywhere instantly and blocks its whole new junction for a frame.
                me.Pace = dt > 0f
                    ? Mathf.Min(Speed, Vector3.Distance(was, me.What.position) / dt)
                    : 0f;
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
                    // THE NOSE STOPS AT THE LINE, NOT THE MIDDLE OF THE CAR.
                    //
                    // `me.S` is the car's CENTRE - that is what me.Reach is half a car long for,
                    // and what every following-distance test in this file assumes. Easing the
                    // CENTRE to 0.4 m short of the segment end therefore left the front
                    // `Reach - 0.4` metres PAST it, about 1.8 m for a 4.4 m car. The segment end
                    // is the edge of the crossing carriageway (LaneGraph cuts at `s - reach`,
                    // and reach is the widest arm's half width - 5.0 m here), so that is a car
                    // sitting a third of its length inside the junction box. Reported by the
                    // owner watching Chicago x Attica: traffic stopping "under the light, half
                    // in the road, instead of behind the light".
                    float allowed = Mathf.Max(0f, toEnd - me.Reach - 0.4f);
                    step = Mathf.Min(step, allowed, Mathf.Max(step * (toEnd / Braking), 0f));

                    // Safe to do here and nowhere else: being held means the step above has
                    // already been clamped short of the line, so this car cannot commit to a
                    // turn on this frame and the movement MayCross just validated is not the
                    // one that will be acted on.
                    me.Waited += dt;
                    me.Held += dt;
                    if (me.Waited > Rethink) { me.Choices++; me.Waited = 0f; }
                }
                else { me.Waited = 0f; me.Held = 0f; }
            }

            if (Blocked(index)) { step = 0f; me.Why = Hold.Following; }

            me.S += step;

            if (me.S < segment.ToS) return;

            if (segment.IsExit) { Reintroduce(me); return; }

            int turn = Choose(me, segment);
            if (turn < 0) { me.S = segment.ToS; me.Why = Hold.NoLegalTurn; return; }

            // ASKED AGAIN AT THE MOMENT OF ENTRY, and not only on the approach. MayCross tests
            // this once the car is within two metres of the line, which is normally two frames'
            // travel - but a long frame can carry a car from beyond that window to over the line
            // in one step, and the one rule that must never be skipped is the one keeping two
            // cars off the same ground. Here it cannot be outrun by a frame rate.
            if (!TurnFree(turn, me)) { me.S = segment.ToS; me.Why = Hold.TurnBusy; return; }
            if (!RoomBeyond(me, turn)) { me.S = segment.ToS; me.Why = Hold.NoRoomBeyond; return; }

            me.Turn = turn;
            me.T = 0f;
        }

        /// <summary>Through the junction on the chosen turn.</summary>
        private void CrossJunction(Mover me, int index, float dt)
        {
            var turn = Graph.Turns[me.Turn];
            float length = Mathf.Max(0.5f, TurnLength(turn));

            // ONCE COMMITTED, IT CLEARS. Nothing stops a car inside a junction, and that is a
            // deliberate reversal of what was here before.
            //
            // `Blocked()` used to be called here, and it deadlocked the entire map: a car stalled
            // mid-turn keeps its claim on that ground, and both give-way rules treat anybody
            // inside a junction as an absolute blocker, so one stopped car shut its junction
            // permanently and the queues behind it grew until nothing on the map moved. Measured:
            // eighteen cars stuck on give-way in every sample fifteen seconds apart, following
            // traffic climbing 151 -> 167, and total distance travelled by the whole fleet ZERO.
            //
            // It is safe to drop because the two entry rules already prove the way out is clear,
            // and NOTHING CAN TAKE IT AWAY AFTERWARDS:
            //
            //   `RoomBeyond` proves the nearest car on the destination is a full stopping gap
            //   past the exit, and cars only ever move FORWARD, so that gap can only grow.
            //
            //   Nothing new can appear in between. Two turns that merge into one lane finish at
            //   the same point, so the arcs touch and they are already conflicting; a destination
            //   is fed by a junction and is therefore never a segment `Reintroduce` can drop a
            //   car onto.
            //
            //   `TurnFree` proves no conflicting path is occupied, and holds that for as long as
            //   this car is inside, because the same test blocks everyone else from entering.
            //
            // So the ground ahead of a committed car belongs to it - against CROSSING traffic.
            // It still has to keep off the back of whoever is on the same arc in front of it,
            // because two cars may hold the same turn at once: a turn does not conflict with
            // itself, and RoomBeyond cannot see a car that is still mid-turn. Without this a
            // follower enters the arc behind a leader and drives straight through it, which is
            // exactly what NoTwoVehiclesOccupyTheSameSpace reported at 0.00m.
            if (Blocked(index)) return;

            // THE SAME PACE THE GAP WAS JUDGED ON. WhenClear let this car out on the promise that
            // it would cross at TurnPace; crossing any slower than that turns the promise into a
            // collision with the traffic that was let through on it. Held is not cleared until
            // the arc is finished, three lines down, so the whole manoeuvre runs at the speed the
            // decision was made with.
            me.T += TurnPace(me) * dt / length;
            if (me.T < 1f) return;

            var to = Graph.Segments[turn.To];
            me.Segment = to.Index;
            me.S = to.FromS;
            me.Turn = -1;
            me.T = 0f;

            // Through, and patient again. Cleared HERE rather than on entering the junction so
            // the arc is driven at the pace the gap was granted on.
            me.Held = 0f;
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
            float toEnd = segment.ToS - me.S;

            bool signalised = _signals != null && _signals.IsSignalised(node);
            if (signalised && !_signals.MayEnter(node, northSouth))
            { me.Why = Hold.Signal; return false; }

            int turn = Choose(me, segment);
            if (turn < 0) return true;                  // nothing legal; RunSegment holds it

            // Giving way starts early. A car on the priority road is not slowing down for this,
            // so the decision has to be taken while there is still room to stop for it.
            if (!signalised && _signals != null && _signals.GivesWay(node, northSouth)
                && toEnd < Braking && !NothingCrossing(me, segment, turn))
            { me.Why = Hold.GiveWay; return false; }

            // The rest is only worth asking once it is nearly at the line.
            if (toEnd > 2f) return true;

            // NOTHING ENTERS A JUNCTION ON GROUND SOMEBODY IS ALREADY DRIVING OVER. This is the
            // one hard rule, and it is what makes the two gap tests below safe to be generous:
            // they decide whether pulling out would be RUDE, this decides whether it is possible.
            if (!TurnFree(turn, me)) { me.Why = Hold.TurnBusy; return false; }
            if (!RoomBeyond(me, turn)) { me.Why = Hold.NoRoomBeyond; return false; }

            if (Graph.Turns[turn].Kind == TurnKind.Left && !NothingComing(me, segment, turn))
            { me.Why = Hold.Oncoming; return false; }

            me.Why = Hold.None;
            return true;
        }

        /// <summary>
        /// Is every turn that crosses this one currently empty?
        ///
        /// A car holds its claim from the moment it enters the junction to the moment it leaves,
        /// and takes the claim only on ENTRY - never while it is waiting. Nothing therefore ever
        /// holds one path while queuing for another, which is the shape a deadlock needs.
        /// </summary>
        private bool TurnFree(int turn, Mover me)
        {
            if (_conflicts == null || turn < 0 || turn >= _conflicts.Length) return true;

            var against = _conflicts[turn];
            if (against.Length == 0) return true;

            foreach (var other in _movers)
            {
                if (other == me || other.What == null || other.Turn < 0) continue;

                for (int i = 0; i < against.Length; i++)
                    if (against[i] == other.Turn) return false;
            }
            return true;
        }

        /// <summary>
        /// Is there somewhere to BE on the far side, once through?
        ///
        /// DO NOT ENTER A BOX YOU CANNOT LEAVE, which is the rule painted on the road at every
        /// yellow-box junction in the country, and it turns out to be load-bearing here rather
        /// than decorative. A car already inside a junction can be stopped by traffic ahead of it
        /// - `CrossJunction` calls `Blocked()` and holds - and it keeps its claim on that ground
        /// for as long as it sits there. Without this the first car to stop mid-junction locks out
        /// every turn that crosses it, that junction backs up, and the jam propagates until
        /// NOTHING ON THE MAP MOVES. Measured: with claims and without this rule, total distance
        /// travelled by two hundred and thirty-six vehicles over ninety seconds was ZERO.
        ///
        /// A claim can now only be taken by a car that has room to complete the movement, so
        /// holding one while waiting for something else is no longer a state a car can be in.
        /// </summary>
        private bool RoomBeyond(Mover me, int turn)
        {
            var to = Graph.Segments[Graph.Turns[turn].To];

            foreach (var other in _movers)
            {
                if (other == me || other.What == null || other.Turn >= 0) continue;
                if (other.Segment != to.Index) continue;

                if (other.S - to.FromS < me.Reach + other.Reach + Headway) return false;
            }
            return true;
        }

        /// <summary>
        /// How long until this car is out of that lane, in seconds.
        ///
        /// Walks its own turn and finds the last point at which the arc is still inside the other
        /// lane, then adds its own length so a lorry is not counted clear while its trailer is
        /// still across the carriageway. That is the number a gap has to beat, and it is the whole
        /// difference between this and what was here before: the old rule compared a distance
        /// against a constant somebody chose, and could not express "long enough for ME" at all -
        /// so an articulated lorry and a hatchback waited for exactly the same gap.
        /// </summary>
        private float WhenClear(Mover me, int turn, LaneSegment theirs)
        {
            const int Steps = 12;

            var line = _world.Roads.Lines[theirs.Line];
            float cross = CrossOf(theirs);

            TurnArc(Graph.Turns[turn], out var a, out var b, out var c);

            float last = 0f;
            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                var p = Bezier(a, b, c, t);
                float off = line.IsNorthSouth ? Mathf.Abs(p.x - cross) : Mathf.Abs(-p.z - cross);
                if (off <= LaneHalf) last = t;
            }

            return (TurnLength(Graph.Turns[turn]) * last + me.Reach) / TurnPace(me);
        }

        /// <summary>
        /// How briskly THIS car will take a junction, given how long it has been sitting at the
        /// line.
        ///
        /// THE POINT IS THAT IT IS NOT A CHEAT. A give-way car needs a gap long enough to be out
        /// of the way before the other one arrives, and on a road with continuous traffic that
        /// gap may simply never come - a car starved at Ross and Attica for 111 of the 120
        /// seconds it was watched, which is not queuing, it is a driver who will die there.
        ///
        /// The obvious fix is to let it go anyway after a while, and that fix has been tried
        /// here and REVERTED: it produced a real 0.00m collision, because "go anyway" is exactly
        /// "enter the junction in front of a car that is still coming". This says something
        /// different and true: a driver who has waited gets impatient and LAUNCHES HARDER, so
        /// the manoeuvre genuinely takes less time and therefore genuinely needs a smaller gap.
        /// The safety condition - be clear before they arrive - is not relaxed by one inch. It is
        /// simply easier to satisfy, because the car really does move faster.
        ///
        /// Which is why this is used by <see cref="WhenClear"/> AND by the motion itself. Using
        /// it in only one of the two would be the cheat: claim a fast crossing to win the gap,
        /// then dawdle across in front of the traffic that was let through on that promise.
        ///
        /// Capped at the open-road speed. Nobody takes a corner faster than they drive.
        /// </summary>
        private static float TurnPace(Mover me)
        {
            const float Patience = 25f;      // seconds of waiting to reach the brisk end
            float eager = Mathf.Clamp01(me.Held / Patience);
            return Mathf.Lerp(TurnSpeed, Speed, eager);
        }

        /// <summary>Do these two turns cross? See <see cref="_conflicts"/>.</summary>
        private bool Conflicts(int turn, int other)
        {
            if (_conflicts == null || turn < 0 || turn >= _conflicts.Length) return true;

            var against = _conflicts[turn];
            for (int i = 0; i < against.Length; i++) if (against[i] == other) return true;
            return false;
        }

        /// <summary>When that car reaches its stop line, in seconds. See <see cref="Creep"/>.</summary>
        private static float Arrival(Mover other, LaneSegment theirs) =>
            Mathf.Max(0f, theirs.ToS - other.S) / Mathf.Max(other.Pace, Creep);

        /// <summary>
        /// Is the road this one is about to cross clear enough to pull out into?
        ///
        /// The other axis of the same junction, both directions of it, plus anybody already
        /// inside the junction on any arm - at a priority junction there is nothing else keeping
        /// the two streams apart, so a car that has committed has to be waited out whichever way
        /// it came from.
        /// </summary>
        private bool NothingCrossing(Mover me, LaneSegment mine, int turn)
        {
            bool mineIsNorthSouth = Headings.IsNorthSouth(mine.Way);

            foreach (var other in _movers)
            {
                if (other == me || other.What == null) continue;

                if (other.Turn >= 0)
                {
                    // ONLY SOMEBODY WHOSE PATH ACTUALLY CROSSES MINE. This was a blanket "anybody
                    // inside this junction", which is both wrong and dangerous: wrong because a
                    // car crossing on a path that never meets mine is no reason to wait, and
                    // dangerous because it made one stalled car an absolute permanent block.
                    if (Conflicts(turn, other.Turn)) return false;
                    continue;
                }

                var theirs = Graph.Segments[other.Segment];
                if (theirs.ToJunction != mine.ToJunction) continue;
                if (Headings.IsNorthSouth(theirs.Way) == mineIsNorthSouth) continue;   // my axis

                // WILL IT GET HERE BEFORE I AM OUT OF ITS WAY? Which is a question about time,
                // and the thing thirty-five metres of fixed distance could never ask. A stream of
                // cars eight metres apart at eight metres a second puts somebody inside any fixed
                // window every single second, so the old rule's answer was permanently no and a
                // give-way car could sit for the whole two minutes it was watched.
                if (Arrival(other, theirs) < WhenClear(me, turn, theirs)) return false;
            }
            return true;
        }

        /// <summary>Is the oncoming lane clear enough to turn across?</summary>
        private bool NothingComing(Mover me, LaneSegment mine, int turn)
        {
            var facing = Headings.Back(mine.Way);

            foreach (var other in _movers)
            {
                if (other == me || other.What == null) continue;

                // Someone already in the junction on a path that crosses this turn.
                if (other.Turn >= 0)
                {
                    if (Conflicts(turn, other.Turn)) return false;
                    continue;
                }

                var theirs = Graph.Segments[other.Segment];
                if (theirs.ToJunction != mine.ToJunction) continue;
                if (theirs.Way != facing) continue;

                if (Arrival(other, theirs) < WhenClear(me, turn, theirs)) return false;
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

            // WEIGHTED BY THE ROAD BEING ENTERED, not just by which way the wheel turns.
            //
            // This was a flat 60 straight / 20 right / 20 left whatever road you were on, and
            // the consequence only showed up once the map became a village: a car on Route 1
            // turned off it two junctions in five, so through traffic dissolved into the side
            // streets and the steady state was every lane equally busy. Rossville reported one
            // vehicle on the main road and a hundred and five on residential streets, which is
            // the exact opposite of a small town on a state highway.
            //
            // Scoring the destination's class fixes it from the map rather than from a table of
            // road names: on a main road, going straight scores 8x3 against 2 for a turn into a
            // street, so about six cars in seven carry on through - which is what a highway IS.
            // Coming the other way, off a street onto the highway, the turn outscores straight
            // on, so local traffic feeds the main road instead of circulating. Reclassify a
            // street in content and the pattern moves with it.
            int total = 0;
            for (int i = 0; i < options.Count; i++) total += Appeal(options[i]);
            if (total <= 0) return options[0];

            // The SAME roll as before, so the answer is still stable between the frame it is
            // asked on approach and the frame it is acted on - which is what stops a car
            // re-deciding mid-junction and cutting a corner it had already committed to.
            int roll = (int)(Materials3D.Scatter(segment.Index, me.Choices, 907) % (uint)total);
            for (int i = 0; i < options.Count; i++)
            {
                roll -= Appeal(options[i]);
                if (roll < 0) return options[i];
            }
            return options[options.Count - 1];
        }

        /// <summary>
        /// How attractive a turn is: how big a road it leads onto, times how ordinary the
        /// manoeuvre is.
        ///
        /// Straight on is three times a turn because most traffic at most junctions goes
        /// straight on - that much was right in the flat version and is kept. What is new is the
        /// class, and the numbers are ORDERED rather than tuned: a track is a farm entrance, a
        /// street is somewhere people live, a main road is the way through town, and each step
        /// up is meant to dominate the one below when both are on offer.
        /// </summary>
        private int Appeal(int turn)
        {
            var into = Graph.Segments[Graph.Turns[turn].To];
            var line = _world != null && into.Line >= 0 && into.Line < _world.Roads.Lines.Count
                ? _world.Roads.Lines[into.Line]
                : null;

            // THE COUNTY'S OWN COUNT WHERE THERE IS ONE. Asked of the ROAD rather than of its
            // class, so a car leaving a junction is drawn onto Route 1 over a side street in the
            // proportion IDOT actually measured - 5,200 a day against 200 - instead of the 4:1 the
            // class ladder could express. Alleys still score 0, so nothing turns down one on its
            // way somewhere. See RoadClasses.AmbientTrafficWeight, which the spawn pitch reads too.
            int road = line != null ? RoadClasses.AmbientTrafficWeight(line)
                                    : RoadClasses.AmbientTrafficWeight(RoadClass.Street);

            return road * (Graph.Turns[turn].Kind == TurnKind.Straight ? 3 : 1);
        }

        /// <summary>
        /// Off one edge of the map and back on at another - INTO A GAP, not on top of somebody.
        ///
        /// This used to drop a car at its new entry with no regard for what was already standing
        /// there, and two cars put on the same entry are not close together, they are at 0.00m
        /// exactly. That is the collision `NoTwoVehiclesOccupyTheSameSpace` reports, and it is
        /// not a driving fault: nothing drove anywhere, one car was placed inside another.
        ///
        /// IT ONLY BECAME COMMON ONCE THE TRAFFIC FLOWED. While the city was jamming, almost
        /// nothing ever reached an exit to be recycled, so the fault could hardly fire - which is
        /// also why it surfaces late in a run rather than early, and why isolated short reruns
        /// are no test of it. Fixing the jams is what exposed it.
        /// </summary>
        private void Reintroduce(Mover me)
        {
            if (Graph.Entries.Count == 0) return;

            me.Choices++;
            int start = (int)(Materials3D.Scatter(me.Choices, me.Segment, 911)
                              % (uint)Graph.Entries.Count);

            for (int i = 0; i < Graph.Entries.Count; i++)
            {
                var entry = Graph.Segments[Graph.Entries[(start + i) % Graph.Entries.Count]];
                if (!RoomAt(me, entry)) continue;

                me.Segment = entry.Index;
                me.S = entry.FromS;
                me.Turn = -1;
                me.Waited = 0f;
                return;
            }

            // Every way back on is occupied. Wait off-stage, which is out of shot anyway, rather
            // than materialise inside another vehicle.
            me.S = Graph.Segments[me.Segment].ToS;
        }

        /// <summary>Is the start of that lane clear enough to put this vehicle on it?</summary>
        private bool RoomAt(Mover me, LaneSegment entry)
        {
            foreach (var other in _movers)
            {
                if (other == me || other.What == null || other.Turn >= 0) continue;
                if (other.Segment != entry.Index) continue;

                if (other.S - entry.FromS < me.Reach + other.Reach + Headway) return false;
            }
            return true;
        }

        /// <summary>
        /// A vehicle's length nose to tail, off the mesh.
        ///
        /// These are modelled facing +z, so the length is the z extent. Measured once as the car
        /// is created rather than every frame, which is why it is on the Mover.
        /// </summary>
        private static float Length(GameObject car)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var mf in car.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                lo = Mathf.Min(lo, b.min.z);
                hi = Mathf.Max(hi, b.max.z);
            }
            return hi > lo ? hi - lo : 4.4f;
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
        /// Is any MOVING vehicle close in front of this pose?
        ///
        /// THE ONE THING CITYRESPONSE CANNOT ASK ANY OTHER WAY. A response vehicle drives the same
        /// lane graph and has to keep the same following distance, but it is deliberately not in
        /// `_movers` (see <see cref="CityResponse"/> for why), so it can neither call
        /// <see cref="Blocked"/> nor be seen by it. This is `Blocked`'s mover loop with the
        /// caller's own pose substituted for `_movers[index]`, and it exists so that `_movers`
        /// itself stays private: the eight loops that walk it all assume a wandering car, and the
        /// moment it is exposed somebody will add a ninth that does not.
        ///
        /// THE PARALLEL-ONLY DOT TEST IS KEPT, and it is not an oversight that cross traffic is
        /// invisible here. This is a FOLLOWING model — a 2.4 m box projected down the caller's own
        /// heading — and it cannot separate crossing paths anyway; counting them made a car with a
        /// green light sit still because somebody waiting at the red on the crossing street fell
        /// inside the box. See `Blocked`'s own note.
        ///
        /// <paramref name="reach"/> is the CALLER's half-length; each mover's own is added, as it
        /// is between two ambient cars, so a lorry is given a lorry's room.
        /// </summary>
        public bool AnyMoverWithin(Vector3 pos, Vector3 forward, float reach)
        {
            for (int j = 0; j < _movers.Count; j++)
            {
                var other = _movers[j];
                if (other.What == null) continue;
                if (Vector3.Dot(forward, other.Forward) < 0.7f) continue;

                var gap = other.What.position - pos;

                float ahead = Vector3.Dot(gap, forward);
                if (ahead <= 0f) continue;                                // behind me
                if (ahead > reach + other.Reach + Headway) continue;      // far enough off

                if (Vector3.Cross(forward, gap).magnitude > LookWide) continue;   // not in my lane

                return true;
            }
            return false;
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
                // PARALLEL TRAFFIC ONLY, INSIDE A JUNCTION AS WELL AS OUTSIDE IT.
                //
                // There used to be an exception here: a car part-way round a corner counted every
                // heading, on the reasoning that it crosses the others rather than following
                // them. That reasoning is right about the geometry and wrong about the remedy -
                // this is a FOLLOWING model, a 2.4m box projected down the car's own heading, and
                // it cannot separate crossing paths anyway. What it did instead was stop a car
                // dead in the middle of a junction, which locks that ground for everyone whose
                // path crosses it, and the whole map seized: every vehicle stationary, total
                // distance travelled zero.
                //
                // Crossing paths are now separated where they can be, by the claim on the turn
                // itself. What is left for this to do is what it was always good at: keeping a
                // car off the back of the one in front, which inside a junction means the car
                // ahead of it on the same arc and the one it is about to come out behind.
                if (Vector3.Dot(me.Forward, other.Forward) < 0.7f) continue;

                var gap = other.What.position - here;

                float ahead = Vector3.Dot(gap, me.Forward);
                if (ahead <= 0f) continue;                               // behind me
                if (ahead > me.Reach + other.Reach + Headway) continue;   // far enough off

                float side = Vector3.Cross(me.Forward, gap).magnitude;
                if (side > LookWide) continue;                           // not in my lane

                return true;
            }

            for (int j = 0; j < Obstacles.Count; j++)
            {
                var what = Obstacles[j];
                if (what == null) continue;
                var gap = what.position - here;
                float ahead = Vector3.Dot(gap, me.Forward);
                if (ahead <= 0f) continue;
                if (ahead > me.Reach + 2.2f + Headway) continue;
                if (Vector3.Cross(me.Forward, gap).magnitude > LookWide) continue;
                return true;
            }

            return false;
        }
    }
}
