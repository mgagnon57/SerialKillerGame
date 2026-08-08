using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// What sort of road this is, which is to say how much of it is asphalt and how many lanes
    /// are painted on it.
    ///
    /// The class exists because WIDTH ALONE CANNOT SAY. An arterial and an ordinary main road
    /// both occupy a thirty-metre corridor; what differs is that one carries two lanes each way
    /// and the other one. Rasterising both to the same Terrain.Road and letting the renderer
    /// guess from the corridor width is exactly the sort of inference that puts cars on the
    /// pavement, so the map says it outright.
    /// </summary>
    public enum RoadClass
    {
        /// <summary>
        /// A dirt track. Ten-metre corridor, no markings and no kerbs, because it is not a made
        /// road at all - it is the way the tractor goes.
        /// </summary>
        Track,

        /// <summary>
        /// A mid-block service alley. SIX-metre corridor - the narrowest thing here, because
        /// that is what an alley is: two ruts and just enough room for one vehicle and the bins.
        ///
        /// It is its own class rather than a narrow Track because the width is not a free
        /// choice - the parser pins it to the class, since the road kit's tiles are built to it.
        /// Ten metres of alley made every block read as though it had a second street down the
        /// middle of it.
        /// </summary>
        Alley,

        /// <summary>A residential street. Ten-metre corridor, no markings.</summary>
        Street,

        /// <summary>A main road: thirty-metre corridor, one lane each way, bus lay-bys.</summary>
        Mainroad,

        /// <summary>An arterial: thirty-metre corridor, two lanes each way, divided.</summary>
        Freeway,
    }

    public static class RoadClasses
    {
        /// <summary>
        /// How wide a corridor of this class must be declared, in tiles.
        ///
        /// This is the CORRIDOR - asphalt plus its pavements - because that is what the map
        /// paints and what the road kit's tiles measure. How much of it is asphalt is a property
        /// of the models and is measured off the mesh by the renderer, not asserted here.
        /// </summary>
        public static int CorridorWidth(RoadClass klass) =>
            klass == RoadClass.Alley ? 4
          : klass == RoadClass.Street || klass == RoadClass.Track ? 10 : 30;

        /// <summary>
        /// How many running lanes there are in each direction.
        ///
        /// Read off the paint: the freeway tile carries a dashed line either side of a solid
        /// orange centre, and both the main road and the street carry one dashed centre line and
        /// nothing else. It lives here rather than with the renderer because it is a fact about
        /// the ROAD - it decides which turns are legal from which lane, and the lane graph has to
        /// know that without a renderer in the room. Where each lane sits in metres is a
        /// different question, and that one is measured off the mesh.
        /// </summary>
        public static int LanesEachWay(RoadClass klass) => klass == RoadClass.Freeway ? 2 : 1;

        /// <summary>
        /// How much passing traffic a road of this class carries, relative to the others. Used
        /// both for where cars are introduced and for which way they turn at a junction, so the
        /// two cannot drift apart.
        ///
        /// AN ALLEY SCORES ZERO, and that is the point of the function existing. Owner's
        /// standing fact 4 in docs/SOURCES-OF-TRUTH.md: *"Cars would not normally use an
        /// alley"* - they are for the bins and for the garage that faces them, and the way
        /// through town is the street. Both tables here used to end `_ => 1`, which quietly
        /// meant alleys, so ambient traffic was introduced onto back lanes and turned down them
        /// at half the rate of a street.
        ///
        /// ZERO IS ABOUT AMBIENT TRAFFIC, NOT ABOUT ACCESS. It says nothing is *routed* through
        /// an alley; it does not remove the alley from the lane graph, so a resident coming home
        /// to a garage that faces the lane, or a collection round, can still be put on one
        /// deliberately. Those are journeys with a reason, and this is the traffic that has
        /// none.
        /// </summary>
        public static int AmbientTrafficWeight(RoadClass klass) => klass switch
        {
            RoadClass.Freeway => 12,
            RoadClass.Mainroad => 8,
            RoadClass.Street => 2,
            RoadClass.Alley => 0,
            _ => 1,                       // a track is a farm entrance: rare, but not nothing
        };

        /// <summary>
        /// One counted side street's worth of traffic, in vehicles a day.
        ///
        /// The divisor that turns a real count into the weight above, chosen so a typical
        /// Rossville side street still scores the 2 the class ladder gave it and everything else
        /// moves relative to that. IDOT's counted side streets run 75-375 a day with a median
        /// near 250, so 125 puts the median at 2 and leaves the ladder's bottom rung where it was.
        /// </summary>
        public const int CountPerWeight = 125;

        /// <summary>
        /// How busy this road actually is — the county's own count where there is one, and the
        /// class ladder where there is not.
        ///
        /// THE LADDER WAS WRONG BY A FACTOR OF FIVE AND COULD NOT SEE ROUTE 1 AT ALL. `mainroad 8
        /// : street 2` is 4:1; IDOT measures Rossville at **21:1** — Chicago Street (Route 1) at
        /// 5,200 vehicles a day against a side street's ~250. Worse, `carries mainroad` is true of
        /// both Route 1 and Attica, so the ladder handed them the same weight when the first
        /// carries **4.7 times** the second. Measured against real counts the game was putting
        /// 44.5% of its vehicle-miles on side streets where the county measures 14-20%.
        ///
        /// An uncounted road keeps its class weight. IDOT not counting a road is weak evidence
        /// that it is quiet, but it is not a measurement, and inventing a number here would be the
        /// same mistake in the other direction. Alleys stay at zero: that is about ambient traffic
        /// having no reason to be there, not about access. See docs/research/TRAFFIC-COUNTS.md.
        /// </summary>
        public static int AmbientTrafficWeight(RoadLine line)
        {
            if (line == null) return 1;
            if (line.Carries == RoadClass.Alley) return 0;
            if (line.Aadt <= 0) return AmbientTrafficWeight(line.Carries);

            int weight = (line.Aadt + CountPerWeight / 2) / CountPerWeight;
            return weight < 1 ? 1 : weight;
        }

        public static bool TryParse(string text, out RoadClass klass)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "alley":    klass = RoadClass.Alley;    return true;
                case "track":    klass = RoadClass.Track;    return true;
                case "street":   klass = RoadClass.Street;   return true;
                case "mainroad": klass = RoadClass.Mainroad; return true;
                case "freeway":  klass = RoadClass.Freeway;  return true;
                default:         klass = RoadClass.Street;   return false;
            }
        }
    }

    /// <summary>
    /// One named road, kept whole.
    ///
    /// The layout used to rasterise its roads into the terrain grid and throw the roads
    /// themselves away, which left every later pass to reconstruct a corridor by sampling tiles
    /// and guessing where the middle was. Streets, traffic and signals all need the same three
    /// facts - where the centre line runs, which way it points, and what class it is - so the
    /// world keeps them instead of making each caller rediscover them.
    ///
    /// STRAIGHT AND AXIS-ALIGNED, Rossville's roads all are today - but the road itself no
    /// longer assumes that: <see cref="Path"/> below is where the centre line actually runs,
    /// straight or curved, and is the answer that always holds. Centre/From/To remain alongside
    /// it, not as a stale approximation but because for a straight road they are exactly right -
    /// the very numbers Path is built from - and 20-odd other files still read them directly
    /// rather than going through Path. <see cref="IsStraight"/> says whether that shortcut is
    /// available for a given road.
    /// </summary>
    public sealed class RoadLine
    {
        public readonly string Name;
        public readonly RoadClass Class;

        /// <summary>
        /// WHAT THIS ROAD CARRIES, which is not always what it is paved to.
        ///
        /// Class is a fact about the GROUND: the corridor the road kit's tiles are built to, and
        /// VillageParser refuses any road declared a width its class does not have. Carries is a
        /// fact about the ROAD'S JOB - the route through town, the county highway - and the two
        /// genuinely disagree here. Attica Street is a county highway whose right of way measures
        /// 67 ft, the same as every other street in Rossville; paving it to a main road's 98 ft
        /// would lay 16 ft of asphalt across private lots down its whole length, which is the
        /// exact fault the survey was done to remove. Being a county highway does not make the
        /// ground any wider.
        ///
        /// So the corridor steps down to fit the ground and this carries the function. Priority
        /// at a junction, where the signals go, and how much ambient traffic a road is given all
        /// read THIS; only the geometry reads Class. Defaults to Class, so every map that never
        /// mentions it behaves exactly as it did.
        /// </summary>
        public readonly RoadClass Carries;

        /// <summary>Corridor width in tiles, as declared.</summary>
        public readonly int Width;

        /// <summary>
        /// The public land this corridor sits inside, in metres, or 0 where it was not measured.
        ///
        /// Carried through from RoadRun.Easement for the same reason Carries is: the parser knows
        /// it, and everything that DRAWS a road had no way to ask. A Rossville street paves 10 m
        /// down the middle of a 20 m right of way, so five metres each side is public ground that
        /// is not road - verge, utilities, and a sidewalk where there was one. Anything laying a
        /// walk needs this number; measuring outward from the asphalt and guessing puts it in a
        /// hedge, and measuring outward from the lot line is what the survey measured.
        /// </summary>
        public readonly float Easement;

        public readonly IReadOnlyList<Tile> Points;

        /// <summary>Along the map's y axis rather than its x.</summary>
        public readonly bool IsNorthSouth;

        /// <summary>Two points, on one axis: the centre line below means something.</summary>
        public readonly bool IsStraight;

        /// <summary>
        /// Where the middle of the corridor runs, in CONTINUOUS village coordinates - the x of a
        /// north-south road, the y of an east-west one.
        ///
        /// Derived from how WorldBuilder actually strokes a road rather than from the declared
        /// number, because the two are not always the same. The brush covers offsets
        /// -(W/2) .. (W/2 + W%2 - 1), so an even width lands with its centre exactly on the
        /// declared coordinate and an odd width half a tile past it.
        /// </summary>
        public readonly float Centre;

        /// <summary>The declared extent along the road's own axis, in continuous coordinates.</summary>
        public readonly float From, To;

        /// <summary>
        /// Where the centre line actually runs.
        ///
        /// Centre above is A SINGLE FLOAT and cannot describe a road that bends, which is why
        /// Illinois Route 1 is drawn straight through 85% of the lots it passes. For a straight
        /// axis-aligned road - which is all 27 in the current map - this is the exact same
        /// geometry Centre/From/To describe, and RoadPath returns it bit for bit.
        /// </summary>
        public readonly RoadPath Path;

        /// <param name="carries">What the road's job is, when that differs from its corridor.
        /// Null means the two agree, which is every road on every map written before this.</param>
        /// <param name="easement">The public land the corridor sits in, in metres. 0 is "not
        /// measured", which is every map written before the survey.</param>
        /// <summary>
        /// Vehicles a day the county counted here, both directions, or 0 for "not counted".
        /// See <see cref="RoadClasses.AmbientTrafficWeight(RoadLine)"/>.
        /// </summary>
        public readonly int Aadt;

        public RoadLine(string name, RoadClass klass, int width, IReadOnlyList<Tile> points,
                        RoadClass? carries = null, float easement = 0f, int aadt = 0)
        {
            Name = name ?? "";
            Class = klass;
            Carries = carries ?? klass;
            Width = width;
            Easement = easement;
            Aadt = aadt;
            Points = points ?? Array.Empty<Tile>();

            if (Points.Count < 2)
            {
                IsStraight = false;
                return;
            }

            var a = Points[0];
            var b = Points[Points.Count - 1];
            int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
            IsNorthSouth = dy >= dx;

            // Straight means every point shares the cross-axis coordinate of the first.
            IsStraight = true;
            for (int i = 1; i < Points.Count && IsStraight; i++)
                IsStraight = IsNorthSouth ? Points[i].X == a.X : Points[i].Y == a.Y;

            float half = width % 2 == 0 ? 0f : 0.5f;
            Centre = (IsNorthSouth ? a.X : a.Y) + half;

            int lo = IsNorthSouth ? Math.Min(a.Y, b.Y) : Math.Min(a.X, b.X);
            int hi = IsNorthSouth ? Math.Max(a.Y, b.Y) : Math.Max(a.X, b.X);
            From = lo;
            To = hi + 1f;          // a tile covers [i, i+1), so the run ends past its last tile

            // Built from the DERIVED continuous centre line, not from the declared tiles: the
            // brush covers -(W/2)..(W/2 + W%2 - 1), so an odd width sits half a tile past the
            // declared coordinate, and a tile's run ends at hi+1 rather than hi. Path has to
            // describe the road WorldBuilder actually strokes.
            if (IsStraight)
            {
                Path = IsNorthSouth
                    ? RoadPath.Straight(new Vec2(Centre, From), new Vec2(Centre, To))
                    : RoadPath.Straight(new Vec2(From, Centre), new Vec2(To, Centre));
            }
            else
            {
                // A declared curve runs through its tile centres - Vec2.CentreOf, the convention
                // the rest of Core already means by "where a tile is". Phase A ships no curved
                // road, so nothing exercises this on real content yet; Phase C revisits it if the
                // half-width parity above turns out to matter on a bend.
                var through = new Vec2[Points.Count];
                for (int i = 0; i < Points.Count; i++) through[i] = Vec2.CentreOf(Points[i]);
                Path = RoadPath.Through(through);
            }
        }

        /// <summary>Half the corridor, for asking whether a point is on this road.</summary>
        public float HalfWidth => Width / 2f;

        public override string ToString() =>
            $"{Name} ({Class}, {Width}m, {(IsNorthSouth ? "N-S" : "E-W")} at {Centre})";
    }

    /// <summary>Where two roads cross. The place a signal goes and a car has to wait.</summary>
    public readonly struct Junction
    {
        /// <summary>The centre of the crossing, in continuous village coordinates.</summary>
        public readonly float X, Y;

        public readonly RoadLine NorthSouth, EastWest;

        /// <summary>
        /// How far along each road the crossing falls. THIS is what LaneGraph cuts lanes at;
        /// before Phase A it inferred the position from the other road's Centre, which only
        /// works while every road is straight.
        /// </summary>
        public readonly float SNorthSouth, SEastWest;

        /// <summary>
        /// The direction each road is heading THROUGH the crossing.
        ///
        /// Recorded here for Phase B, which has to face signal heads and stop lines square to a
        /// road that may no longer be axis-aligned. Task 9 does NOT read these: a turn's
        /// direction depends on which way the segment travels, not only on which way the road
        /// was declared, so LaneGraph asks the path for the tangent and flips it for a segment
        /// running against the declaration.
        /// </summary>
        public readonly Vec2 TangentNorthSouth, TangentEastWest;

        public Junction(RoadLine ns, RoadLine ew, float sNs, float sEw, float x, float y)
        {
            NorthSouth = ns;
            EastWest = ew;
            SNorthSouth = sNs;
            SEastWest = sEw;
            X = x;
            Y = y;
            TangentNorthSouth = ns.Path.TangentAt(sNs);
            TangentEastWest = ew.Path.TangentAt(sEw);
        }

        /// <summary>
        /// How far from the centre the crossing reaches - half the WIDER corridor, because the
        /// junction tile is square and sized to the road that needs the most room.
        ///
        /// Still half the wider corridor on an oblique crossing, which is an UNDER-estimate:
        /// two 30m corridors meeting at 45 degrees overlap further than 15m along each. Left as
        /// it is deliberately - Phase A ships no oblique junction, and widening the reach here
        /// would move every existing junction's lane cuts for no present gain.
        /// </summary>
        public float Reach => NorthSouth.HalfWidth > EastWest.HalfWidth
            ? NorthSouth.HalfWidth : EastWest.HalfWidth;
    }

    /// <summary>
    /// Every road in the city, and every place two of them cross.
    /// </summary>
    public sealed class RoadNetwork
    {
        public static readonly RoadNetwork Empty = new RoadNetwork(Array.Empty<RoadLine>());

        public readonly IReadOnlyList<RoadLine> Lines;
        public readonly IReadOnlyList<Junction> Junctions;

        public RoadNetwork(IReadOnlyList<RoadLine> lines)
        {
            Lines = lines ?? Array.Empty<RoadLine>();

            var crossings = new List<Junction>();
            for (int i = 0; i < Lines.Count; i++)
            {
                var ns = Lines[i];
                if (ns.Path == null || !ns.IsNorthSouth) continue;

                for (int j = 0; j < Lines.Count; j++)
                {
                    var ew = Lines[j];
                    if (ew.Path == null || ew.IsNorthSouth) continue;

                    // EVERY crossing, not one. A road that bends can meet the same straight road
                    // twice, and the old model held a single junction per pair by construction.
                    foreach (var hit in Crossings(ns.Path, ew.Path))
                        crossings.Add(new Junction(ns, ew, hit.SA, hit.SB, hit.X, hit.Y));

                    // A ROAD THAT ENDS ON ANOTHER ROAD IS A JUNCTION TOO.
                    //
                    // Crossings finds a junction by the projected lateral FLIPPING SIGN as one
                    // centre line passes THROUGH the other. A road that stops dead leaves on the
                    // side it arrived on and never flips, so a T - or a corner where both ends
                    // meet - produces no junction at all. Nothing then separates the two streams:
                    // LaneGraph makes both approaches exits, CityTraffic skips MayCross entirely
                    // on an exit segment, and Blocked() ignores the pair because the headings
                    // differ by more than 45 degrees. The lanes simply cross each other.
                    //
                    // city.txt never had this shape because it was FIXED BY HAND - forty-three
                    // road ends were extended past their cross street, and RoadGeometryBaselineTests
                    // records why: "an end that stops ON a centreline only touches it and the
                    // intersection finder wants a real crossing". Content/roads.txt is chained
                    // county segments and is exactly that shape, so the survey network brought
                    // every one of them back.
                    foreach (var hit in Touches(ns, ew, crossings))
                        crossings.Add(new Junction(ns, ew, hit.SA, hit.SB, hit.X, hit.Y));
                }
            }
            Junctions = crossings;
        }

        /// <summary>The road covering this point, or null. The widest wins where two overlap.</summary>
        public RoadLine At(float x, float y)
        {
            RoadLine best = null;
            foreach (var line in Lines)
            {
                if (line.Path == null) continue;

                // Through the path rather than against Centre, so this answers for a road that
                // bends. For a straight axis-aligned line Project reduces to exactly the old
                // Math.Abs(across - Centre) test - see RoadGeometryBaselineTests.
                var (s, lateral) = line.Path.Project(new Vec2(x, y));
                if ((lateral < 0f ? -lateral : lateral) > line.HalfWidth) continue;
                if (s <= 0f || s >= line.Path.Length)
                {
                    // Project clamps to the ends of a run, so a point past the end comes back
                    // with an end S and a lateral measured to that end point.
                    if (line.Path.IsStraightAxisAligned)
                    {
                        // Exactly the old test, and provably identical to it: for an
                        // axis-aligned line the lateral is invariant along the tangent.
                        float along = line.IsNorthSouth ? y : x;
                        if (along < line.From || along > line.To) continue;
                    }
                    else
                    {
                        // A bend has no single axis to measure "past the end" along, so ask the
                        // real distance to the nearest point on the centre line instead. Only
                        // reached at a clamped end, so it cannot change the interior answer.
                        var here = new Vec2(x, y);
                        var end = line.Path.PointAt(s);
                        if ((here - end).LengthSquared > line.HalfWidth * line.HalfWidth) continue;
                    }
                }
                if (best == null || line.Width > best.Width) best = line;
            }
            return best;
        }

        /// <summary>
        /// Junctions where one road ENDS on the other, which Crossings cannot see.
        ///
        /// Two shapes, and both are real corners of this town:
        ///   a TEE    - one road's end lands inside the other's run, and
        ///   a CORNER - both roads end at the same place, which is how the county's chained
        ///              segments record Maple meeting Grove: one point, in both roads.
        ///
        /// AN END LANDING INSIDE THE OTHER ROAD'S CORRIDOR IS TOUCHING IT. That is the same rule
        /// `At` uses to decide what a point is on, so a street stopping inside another street's
        /// carriageway meets it by exactly the definition the rest of this file already uses.
        /// Anything further off is a road that stops NEAR another road, which is not a junction
        /// and must not become one.
        /// </summary>
        private static List<Crossing> Touches(RoadLine ns, RoadLine ew, List<Junction> already)
        {
            var found = new List<Crossing>();
            if (ns.Path == null || ew.Path == null) return found;

            Consider(ns, ew, onNs: true);
            Consider(ew, ns, onNs: false);
            return found;

            void Consider(RoadLine ending, RoadLine through, bool onNs)
            {
                foreach (float end in new[] { 0f, ending.Path.Length })
                {
                    var tip = ending.Path.PointAt(end);
                    var (sThrough, lateral) = through.Path.Project(tip);

                    // TOUCHING MEANS TOUCHING, not "somewhere in the corridor". This gated on
                    // through.HalfWidth first, which is the rule `At` uses for what a point is ON -
                    // and it is far too loose for a junction: Chicago Street's corridor is wide
                    // enough that a road stopping 4.7m short of its centre line counted as meeting
                    // it. The junction then landed metres off one of its own roads and
                    // SurveyRoadNetworkTests said so, which is what that test is for.
                    //
                    // A metre. The county's chained segments record these corners at 0.00-0.61m
                    // apart, so this takes every real one and nothing else. A road that stops
                    // FURTHER off is a road that stops NEAR another road, and that is not a
                    // junction however much it looks like one on a map.
                    const float Touching = 1.0f;
                    if (Math.Abs(lateral) > Touching) continue;

                    var back = through.Path.PointAt(sThrough);
                    float bx = back.X - tip.X, by = back.Y - tip.Y;
                    if (bx * bx + by * by > Touching * Touching) continue;

                    // BETWEEN THE TWO, so neither road is misrepresented. The corner is where an
                    // end meets a centre line and the two are within a metre of each other; the
                    // midpoint is within half a metre of both, so PointAt on either road brings
                    // you back to the junction - the invariant SurveyRoadNetworkTests enforces.
                    float x = (back.X + tip.X) * 0.5f, y = (back.Y + tip.Y) * 0.5f;

                    float sNs = onNs ? end : sThrough;
                    float sEw = onNs ? sThrough : end;

                    // ALREADY FOUND. A tee whose stem overshoots slightly is a genuine crossing
                    // and Crossings has it; adding a second junction a metre away would put two
                    // stop lines inside one another's reach, which is the exact shape that strands
                    // lanes. Judged in metres on the ground rather than in S, since the two roads
                    // measure S along different paths.
                    bool duplicate = false;
                    foreach (var seen in already)
                    {
                        if (!ReferenceEquals(seen.NorthSouth, ns) || !ReferenceEquals(seen.EastWest, ew)) continue;
                        float dx = seen.X - x, dy = seen.Y - y;
                        if (dx * dx + dy * dy < Reach(ns, ew) * Reach(ns, ew)) { duplicate = true; break; }
                    }
                    foreach (var seen in found)
                    {
                        float dx = seen.X - x, dy = seen.Y - y;
                        if (dx * dx + dy * dy < Reach(ns, ew) * Reach(ns, ew)) { duplicate = true; break; }
                    }
                    if (duplicate) continue;

                    found.Add(new Crossing(sNs, sEw, x, y));
                }
            }

            static float Reach(RoadLine a, RoadLine b) =>
                a.HalfWidth > b.HalfWidth ? a.HalfWidth : b.HalfWidth;
        }

        private readonly struct Crossing
        {
            public readonly float SA, SB, X, Y;
            public Crossing(float sa, float sb, float x, float y) { SA = sa; SB = sb; X = x; Y = y; }
        }

        /// <summary>
        /// Where two centre lines actually meet.
        ///
        /// TWO BRANCHES, AND THE SPLIT IS ABOUT COST AS MUCH AS CORRECTNESS. The obvious
        /// implementation - every dense segment of one against every dense segment of the other -
        /// is 2400 x 2100 segment pairs for a full-height road against a full-width one, times
        /// the 160-odd pairs in the real grid. That is hundreds of millions of iterations at
        /// every world build, for a map where the answer is a one-line intersection.
        ///
        /// So: two axis-aligned straights get the closed form, which is every pair in the real
        /// map and is arithmetically the (ns.Centre, ew.Centre) the old constructor asserted
        /// outright. Anything involving a curve WALKS THE CURVED SIDE AND PROJECTS ONTO THE
        /// OTHER - where the signed lateral flips, the curve has crossed. That is one Project
        /// per sample rather than a nested loop, and Project on a STRAIGHT road is constant
        /// time - but on a curved one it walks every dense sample, so which side gets walked
        /// is not cosmetic. Walking `a` unconditionally would stay linear for a curved N-S
        /// road against straight E-W streets (every curve in today's map, and every test
        /// fixture below, since the constructor only ever calls this as Crossings(ns.Path,
        /// ew.Path)) but silently regress to the O(len_a x len_b) this function exists to
        /// avoid the day a straight N-S road meets a curved E-W one. So: walk whichever of
        /// a/b is curved and project onto the other; if both are curved, which one is walked
        /// no longer affects cost, so `a` is walked. Results are still handed back as
        /// (SA = along a, SB = along b) regardless of which side was actually walked.
        /// </summary>
        private static List<Crossing> Crossings(RoadPath a, RoadPath b)
        {
            var found = new List<Crossing>();

            if (a.IsStraightAxisAligned && b.IsStraightAxisAligned)
            {
                var a0 = a.PointAt(0f);
                var b0 = b.PointAt(0f);
                bool aVertical = a0.X == a.PointAt(a.Length).X;
                bool bVertical = b0.X == b.PointAt(b.Length).X;
                if (aVertical == bVertical) return found;              // parallel; never crosses

                float x = aVertical ? a0.X : b0.X;
                float y = aVertical ? b0.Y : a0.Y;

                // (x, y) sits exactly on BOTH infinite lines by construction, so latA/latB below
                // are 0 always - see RoadNetwork.At's own note that "for an axis-aligned line the
                // lateral is invariant along the tangent". They cannot tell us whether the
                // crossing falls off either road's finite run; only S can, and Project clamps S
                // to the run before returning it, so a clamped S looks no different from a real
                // one until you ask where it actually points.
                var (sa, latA) = a.Project(new Vec2(x, y));
                var (sb, latB) = b.Project(new Vec2(x, y));
                if (latA > 0.001f || latA < -0.001f) return found;
                if (latB > 0.001f || latB < -0.001f) return found;

                // So: play S back through PointAt. Off the end, Project clamped S to the
                // endpoint, and the endpoint is not (x, y) - the road never actually reaches the
                // other one's line. Inside the run, S is the true unclamped projection and
                // PointAt(S) reproduces (x, y) exactly, boundary included.
                var backA = a.PointAt(sa);
                if (Math.Abs(backA.X - x) > 0.001f || Math.Abs(backA.Y - y) > 0.001f) return found;
                var backB = b.PointAt(sb);
                if (Math.Abs(backB.X - x) > 0.001f || Math.Abs(backB.Y - y) > 0.001f) return found;

                found.Add(new Crossing(sa, sb, x, y));
                return found;
            }

            // At least one side is curved here - two straights already returned above. Walk
            // that side; project onto the other. Only when a is the straight one and b is the
            // curved one does that mean walking b instead of a.
            bool walkB = a.IsStraightAxisAligned && !b.IsStraightAxisAligned;
            RoadPath walk = walkB ? b : a;
            RoadPath other = walkB ? a : b;

            float pitch = RoadPath.ResamplePitch;
            var (_, previousLateral) = other.Project(walk.PointAt(0f));

            for (float s = pitch; s <= walk.Length; s += pitch)
            {
                var here = walk.PointAt(s);
                var (_, lateral) = other.Project(here);

                if ((previousLateral < 0f) != (lateral < 0f))
                {
                    float t = previousLateral / (previousLateral - lateral);
                    if (t >= 0f && t <= 1f)
                    {
                        float sWalk = s - pitch + pitch * t;
                        var hit = walk.PointAt(sWalk);
                        var (sOther, _) = other.Project(hit);

                        // Beyond the other road's end, Project clamps and the lateral is
                        // measured to the end point - which can flip sign for a road that
                        // merely passes the end without touching it. Only strictly inside the
                        // run is a crossing.
                        //
                        // AND THE HIT HAS TO BE ON THE OTHER ROAD, not merely somewhere beside
                        // it. The note above guards the ENDS; this is the same false positive in
                        // the MIDDLE of the run. Project answers with the nearest dense sample and
                        // measures the lateral against the normal THERE, so as this road passes a
                        // bend far away the nearest sample jumps from one segment to the next and
                        // the signed lateral turns over with it - a sign flip with no crossing
                        // under it. The straight branch already guards this by playing S back
                        // through PointAt; the walked branch never did.
                        //
                        // Measured on the survey network: Railroad Avenue "crossed" alley1 TWICE
                        // at (1453,1701), 214 m north of any part of that alley, and Route 1
                        // "crossed" it at (449,421), 1,118 m away. The two railroad ones land 5 m
                        // apart - closer than their own reach - so LaneGraph drops the lane
                        // between them and four lanes arrive at a junction they cannot leave,
                        // which parks those cars on Hold.NoLegalTurn for the rest of the run.
                        //
                        // One resample pitch of tolerance: that is the accuracy the interpolated
                        // hit is found to, a real crossing reprojects to well inside it, and on a
                        // straight `other` Project is exact.
                        var backOther = other.PointAt(sOther);
                        float offX = backOther.X - hit.X, offY = backOther.Y - hit.Y;
                        bool onOther = offX * offX + offY * offY
                                     <= RoadPath.ResamplePitch * RoadPath.ResamplePitch;

                        if (onOther && sOther > 0.001f && sOther < other.Length - 0.001f)
                        {
                            // Hand back (SA = along a, SB = along b) regardless of which side
                            // was walked, so callers never have to know.
                            float sa = walkB ? sOther : sWalk;
                            float sb = walkB ? sWalk : sOther;

                            bool duplicate = false;
                            foreach (var seen in found)
                                if ((seen.SA - sa) * (seen.SA - sa) < 1f) { duplicate = true; break; }
                            if (!duplicate) found.Add(new Crossing(sa, sb, hit.X, hit.Y));
                        }
                    }
                }

                previousLateral = lateral;
            }
            return found;
        }
    }
}
