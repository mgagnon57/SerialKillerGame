using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// Which way along a road, in village coordinates. Village y increases SOUTH.
    /// </summary>
    public enum Heading { North, South, East, West }

    public enum TurnKind { Straight, Left, Right }

    public static class Headings
    {
        /// <summary>Does travelling this way increase the coordinate it runs along?</summary>
        public static bool Increasing(Heading way) => way == Heading.South || way == Heading.East;

        public static bool IsNorthSouth(Heading way) => way == Heading.North || way == Heading.South;

        /// <summary>
        /// Which side of the centre line traffic going this way keeps to: +1 for the greater
        /// cross-coordinate, -1 for the lesser. THIS CITY DRIVES ON THE RIGHT.
        ///
        /// Derived rather than tabulated by eye. Village coordinates are x east, y south - the
        /// same handedness as a screen - so for a travel vector (dx,dy) the right-hand side is
        /// (-dy,dx). North is (0,-1), whose right is (1,0): east, the greater x. East is (1,0),
        /// whose right is (0,1): south, the greater y.
        /// </summary>
        public static int Side(Heading way) =>
            way == Heading.North || way == Heading.East ? 1 : -1;

        /// <summary>The heading you are facing after turning right. A cycle: N, E, S, W.</summary>
        public static Heading Right(Heading way) => way switch
        {
            Heading.North => Heading.East,
            Heading.East  => Heading.South,
            Heading.South => Heading.West,
            _             => Heading.North,
        };

        public static Heading Left(Heading way) => Right(Right(Right(way)));

        public static Heading Back(Heading way) => Right(Right(way));

        /// <summary>Turning from one heading to another, or null if it is a U-turn.</summary>
        public static TurnKind? Between(Heading from, Heading to)
        {
            if (to == from) return TurnKind.Straight;
            if (to == Right(from)) return TurnKind.Right;
            if (to == Left(from)) return TurnKind.Left;
            return null;                      // a U-turn; not offered at a junction
        }
    }

    /// <summary>
    /// One lane of one road, running one way: a straight piece between two junctions, or between
    /// a junction and the edge of the map.
    ///
    /// Lanes are numbered OUTWARD FROM THE CENTRE LINE, so lane 0 is always the inside lane
    /// whatever the road's class. That numbering is what makes the turn rules expressible at all:
    /// a left turn leaves from lane 0 and a right turn from the outermost, and both of those stay
    /// true when a four-lane arterial meets a two-lane main road.
    /// </summary>
    public sealed class LaneSegment
    {
        /// <summary>This segment's own index in <see cref="LaneGraph.Segments"/>.</summary>
        public int Index;

        /// <summary>Index into <see cref="RoadNetwork.Lines"/>.</summary>
        public int Line;

        public Heading Way;

        /// <summary>0 is the lane against the centre line.</summary>
        public int Lane;

        /// <summary>
        /// Start and end in TRAVEL COORDINATES: a number that increases along the direction of
        /// travel whichever way that points, so a car's progress is always FromS rising to ToS
        /// and no code has to carry a sign around. <see cref="LaneGraph.AlongOf"/> turns it back
        /// into a village coordinate exactly for a straight road; for a curve it only offsets
        /// arc length onto the road's declared axis, a convenience rather than the road's real
        /// off-axis position - see the "From + s" note below, where junction stops are cut.
        /// </summary>
        public float FromS, ToS;

        /// <summary>Junction indices, or -1 where the segment runs off the edge of the map.</summary>
        public int FromJunction = -1, ToJunction = -1;

        public float Length => ToS - FromS;

        /// <summary>Nothing feeds this: it is where a car enters the city.</summary>
        public bool IsEntry => FromJunction < 0;

        /// <summary>Nothing follows it: a car reaching the end of this has left the city.</summary>
        public bool IsExit => ToJunction < 0;
    }

    /// <summary>One legal movement through one junction, from one lane into another.</summary>
    public readonly struct LaneTurn
    {
        public readonly int Junction;
        public readonly int From, To;          // indices into LaneGraph.Segments
        public readonly TurnKind Kind;

        public LaneTurn(int junction, int from, int to, TurnKind kind)
        {
            Junction = junction;
            From = from;
            To = to;
            Kind = kind;
        }
    }

    /// <summary>
    /// Every lane in the city, cut at the junctions, and every legal way through one.
    ///
    /// WHY THIS EXISTS. Traffic used to own one lane end to end and wrap round when it left the
    /// map, because there was nothing to tell a car which lane it could move into. That made
    /// turning impossible, and turning is not a flourish: bus routes need it, a pedestrian
    /// crossing needs to know which movements it conflicts with, and a city where every vehicle
    /// travels in a dead straight line for ever does not read as a place anyone lives.
    ///
    /// THE TOPOLOGY IS HERE AND THE METRES ARE NOT. What lane connects to what, and which turns
    /// are legal, follow from the map and from driving on the right - so they belong in Core
    /// where they can be tested. WHERE a lane sits across the carriageway is measured off the
    /// bought road tile and stays with the renderer.
    ///
    /// CURVES ARE CUT AND CLASSIFIED LIKE ANY OTHER ROAD, NOT SPECIAL-CASED. Lanes are cut at
    /// junction ARC LENGTHS rather than at a village coordinate (see the note below), and turns
    /// are classified FROM THE TANGENTS the roads meet at, not from Heading (see that note too)
    /// - so a curved road gets lanes and legal turns the same way a straight one does. Heading
    /// itself is unaffected: it stays a coarse N/S/E/W label, not the segment's own tangent,
    /// which is a real gap for a curve and a deliberate deferral rather than an oversight here.
    /// </summary>
    public sealed class LaneGraph
    {
        public readonly IReadOnlyList<LaneSegment> Segments;
        public readonly IReadOnlyList<LaneTurn> Turns;

        private readonly int[][] _fromSegment;

        /// <summary>How far past the edge of the map a lane runs, so cars arrive from off-stage.</summary>
        public readonly float Margin;

        /// <summary>The turns available to a car finishing this segment.</summary>
        public IReadOnlyList<int> TurnsFrom(int segment) =>
            segment >= 0 && segment < _fromSegment.Length ? _fromSegment[segment] : Array.Empty<int>();

        /// <summary>Segments a car can be introduced on: the ones fed by nothing.</summary>
        public readonly IReadOnlyList<int> Entries;

        /// <summary>Travel coordinate back to a village coordinate along the road's own axis.</summary>
        public static float AlongOf(Heading way, float s) => Headings.Increasing(way) ? s : -s;

        /// <summary>A village coordinate along the road's axis, as a travel coordinate.</summary>
        public static float TravelOf(Heading way, float along) =>
            Headings.Increasing(way) ? along : -along;

        public LaneGraph(RoadNetwork roads, float width, float height, float margin = 30f)
        {
            if (roads == null) throw new ArgumentNullException(nameof(roads));
            Margin = margin;

            var segments = new List<LaneSegment>();

            // ---- 1. cut every lane at the junctions it passes through --------------------
            for (int li = 0; li < roads.Lines.Count; li++)
            {
                var line = roads.Lines[li];

                // A road runs between the points it was DECLARED between, which for the city's
                // arterials is edge to edge and for a farm track is not. Using the map's size
                // here instead would have run every track the full width of the world, laying
                // lanes across fields nobody put a road in.
                int lanes = RoadClasses.LanesEachWay(line.Class);

                // ARC LENGTH, off the junction itself, rather than reading the crossing's village
                // coordinate back through this road's own Centre. The old way could only work
                // while every road was a constant cross-coordinate.
                var stops = new List<(float along, float reach, int index)>();
                for (int j = 0; j < roads.Junctions.Count; j++)
                {
                    var junction = roads.Junctions[j];
                    float s;
                    if (ReferenceEquals(junction.NorthSouth, line)) s = junction.SNorthSouth;
                    else if (ReferenceEquals(junction.EastWest, line)) s = junction.SEastWest;
                    else continue;                                        // not on this road

                    // From + s, deliberately, rather than raw arc length. For a straight road the
                    // two are identical and FromS/ToS keep the exact values they have today,
                    // which is what the recorded baseline pins. For a curve it is a convenience:
                    // arc length measured from the path start, offset onto the road's declared
                    // axis. Nothing carries traffic on a curve until Phase C, and Phase B should
                    // settle whether segments want a true arc-length origin before it does.
                    stops.Add((line.From + s, junction.Reach, j));
                }

                var ways = line.IsNorthSouth
                    ? new[] { Heading.North, Heading.South }
                    : new[] { Heading.East, Heading.West };

                foreach (var way in ways)
                {
                    // In travel order. Sorting in travel coordinates rather than village ones is
                    // what lets a single loop below serve both directions.
                    var ordered = new List<(float s, float reach, int index)>();
                    foreach (var (along, reach, index) in stops)
                        ordered.Add((TravelOf(way, along), reach, index));
                    ordered.Sort((a, b) => a.s.CompareTo(b.s));

                    // THE MARGIN IS FOR LEAVING THE MAP, NOT FOR LEAVING THE ROAD.
                    //
                    // Lanes run a little past the edge of the world so that traffic arrives from
                    // off-stage instead of appearing out of nothing. Applying that to an end
                    // that is NOT the edge sends cars beyond where the road stops - which for the
                    // farm track that comes off Second Street meant a van driving out into a
                    // field, in a straight line, on nothing.
                    //
                    // Whether the road reaches the edge is asked of the DECLARED extent
                    // (line.To), not of the arc length: a curve's arc length exceeds its chord,
                    // so a bend that ends well inside the map could otherwise trip this test and
                    // be handed the off-stage margin anyway. The run's end itself stays in arc
                    // length (pathEnd), consistent with where the stops above are measured. For a
                    // straight road line.To and pathEnd are identical, so nothing here moves it.
                    float span = line.IsNorthSouth ? height : width;
                    float pathEnd = line.From + line.Path.Length;
                    float low = line.From <= 0.01f ? line.From - margin : line.From;
                    float high = line.To >= span - 0.01f ? pathEnd + margin : pathEnd;

                    float start = TravelOf(way, Headings.Increasing(way) ? low : high);
                    float end = TravelOf(way, Headings.Increasing(way) ? high : low);

                    for (int lane = 0; lane < lanes; lane++)
                    {
                        float cursor = start;
                        int previous = -1;

                        foreach (var (s, reach, index) in ordered)
                        {
                            Add(segments, li, way, lane, cursor, s - reach, previous, index);
                            cursor = s + reach;
                            previous = index;
                        }
                        Add(segments, li, way, lane, cursor, end, previous, -1);
                    }
                }
            }

            Segments = segments;

            // ---- 2. join them up through the junctions -----------------------------------
            var turns = new List<LaneTurn>();
            var outgoing = new List<int>[segments.Count];
            for (int i = 0; i < outgoing.Length; i++) outgoing[i] = new List<int>();

            foreach (var into in segments)
            {
                if (into.IsExit) continue;

                var fromLine = roads.Lines[into.Line];
                int fromLanes = RoadClasses.LanesEachWay(fromLine.Class);

                foreach (var onward in segments)
                {
                    if (onward.FromJunction != into.ToJunction) continue;

                    // A road continuing into ITSELF in the same direction is going straight by
                    // definition, whatever its curvature - short-circuited before the tangent
                    // math because that math is sampled only ~Reach (about 15m) either side of
                    // the stop line, and a curve bending enough over that short a chord can
                    // otherwise read as a real Left or Right. On a one-lane road that
                    // misclassification is invisible - lane 0 is simultaneously the inside and
                    // outside lane, so either Kind still satisfies the legal check below by
                    // coincidence - but on a freeway a wrongly-Right continuation demands
                    // into.Lane == fromLanes - 1, so lane 0 would get no continuation at all and
                    // dead-end. See ACurvedFreewaysOwnContinuationDoesNotStrandEitherLane.
                    TurnKind? kind;
                    if (onward.Line == into.Line && onward.Way == into.Way)
                    {
                        kind = TurnKind.Straight;
                    }
                    else
                    {
                        // FROM THE TANGENTS, not the enum, so an oblique crossing classifies
                        // too. For axis-aligned roads this yields precisely what
                        // Headings.Between yields: the cross product's sign is which way the
                        // wheel turns, and the dot tells a straight-on from a U-turn. No angle
                        // is taken - see CoreDeterminismTests.
                        var tIn = roads.Lines[into.Line].Path.TangentAt(
                            AlongOf(into.Way, into.ToS) - roads.Lines[into.Line].From);
                        var tOut = roads.Lines[onward.Line].Path.TangentAt(
                            AlongOf(onward.Way, onward.FromS) - roads.Lines[onward.Line].From);

                        // Flip each tangent to point the way its OWN SEGMENT travels, rather
                        // than assuming the path already points that way. That assumption held
                        // for a straight road, which RoadLine always builds Path From->To
                        // regardless of declaration order, but a curve is
                        // RoadPath.Through(Points) in DECLARED order, and a road authored
                        // high-to-low reverses it. Comparing against the heading's own cardinal
                        // direction - never against declaration order - is right either way, and
                        // for an axis-aligned road the tangent is exactly cardinal, so this
                        // reduces to the old behaviour bit-for-bit.
                        var intoWay = Headings.IsNorthSouth(into.Way)
                            ? new Vec2(0f, Headings.Increasing(into.Way) ? 1f : -1f)
                            : new Vec2(Headings.Increasing(into.Way) ? 1f : -1f, 0f);
                        if (tIn.X * intoWay.X + tIn.Y * intoWay.Y < 0f) tIn = new Vec2(-tIn.X, -tIn.Y);

                        var onwardWay = Headings.IsNorthSouth(onward.Way)
                            ? new Vec2(0f, Headings.Increasing(onward.Way) ? 1f : -1f)
                            : new Vec2(Headings.Increasing(onward.Way) ? 1f : -1f, 0f);
                        if (tOut.X * onwardWay.X + tOut.Y * onwardWay.Y < 0f) tOut = new Vec2(-tOut.X, -tOut.Y);

                        float dot = tIn.X * tOut.X + tIn.Y * tOut.Y;
                        float cross = tIn.X * tOut.Y - tIn.Y * tOut.X;

                        if (dot <= -0.5f) kind = null;                    // a U-turn; never offered
                        else if (cross > 0.3f) kind = TurnKind.Right;     // right is (-y, x): +cross
                        else if (cross < -0.3f) kind = TurnKind.Left;
                        else kind = TurnKind.Straight;
                    }
                    if (kind == null) continue;                      // no U-turns

                    var toLine = roads.Lines[onward.Line];
                    int toLanes = RoadClasses.LanesEachWay(toLine.Class);

                    // Straight stays on the same road, so it stays in the same lane. A turn
                    // changes road: it leaves from the lane nearest the side it is turning
                    // towards and arrives in the matching lane of the new road. That is what
                    // keeps a two-lane arterial and a one-lane main road compatible without
                    // anybody writing down a special case for the pair.
                    // GOING STRAIGHT ON DOES NOT REQUIRE THE ROAD TO KEEP ITS NAME.
                    //
                    // This read `onward.Line == into.Line && onward.Lane == into.Lane`, so a car
                    // could only continue straight if the road it arrived on and the road it left
                    // on were the SAME LINE. That is a rule about names, not about geometry: where
                    // Maple runs into Park at a shared point, or 3550north into Attica, the driver
                    // is going straight along one continuous piece of tarmac and the two halves
                    // merely have different labels in the survey. The old rule refused the turn
                    // and made a car trap out of a street.
                    //
                    // Only the LANE has to match now, which is the part that is actually about the
                    // road: straight on keeps you in the lane you were in, and a turn is what moves
                    // you across. `kind` is already computed from the tangents, so a genuine turn
                    // cannot be mislabelled Straight just because the names happen to differ.
                    //
                    // MEASURED AS ZERO CHANGE ON BOTH MAPS TODAY, which is why it lands on its own:
                    // no same-axis junction exists yet for it to matter at, so this can be proved
                    // neutral before the junction model changes underneath it. It is the safe half
                    // of the fix, landed first on purpose.
                    bool legal = kind switch
                    {
                        TurnKind.Straight => onward.Lane == into.Lane,
                        TurnKind.Left     => into.Lane == 0 && onward.Lane == 0,
                        _                 => into.Lane == fromLanes - 1 && onward.Lane == toLanes - 1,
                    };
                    if (!legal) continue;

                    outgoing[into.Index].Add(turns.Count);
                    turns.Add(new LaneTurn(into.ToJunction, into.Index, onward.Index, kind.Value));
                }
            }

            Turns = turns;
            _fromSegment = new int[segments.Count][];
            for (int i = 0; i < segments.Count; i++) _fromSegment[i] = outgoing[i].ToArray();

            var entries = new List<int>();
            foreach (var segment in segments)
                if (segment.IsEntry) entries.Add(segment.Index);
            Entries = entries;
        }

        private static void Add(List<LaneSegment> into, int line, Heading way, int lane,
                                float fromS, float toS, int fromJunction, int toJunction)
        {
            // A junction wider than the gap between it and the next one would produce a segment
            // of negative length. It cannot happen on a grid whose blocks are wider than its
            // roads, but a map is authored by hand and this is cheaper than the bug would be.
            if (toS - fromS <= 0.01f) return;

            into.Add(new LaneSegment
            {
                Index = into.Count,
                Line = line,
                Way = way,
                Lane = lane,
                FromS = fromS,
                ToS = toS,
                FromJunction = fromJunction,
                ToJunction = toJunction,
            });
        }
    }
}
