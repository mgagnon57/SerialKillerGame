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
            klass == RoadClass.Street || klass == RoadClass.Track ? 10 : 30;

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

        public static bool TryParse(string text, out RoadClass klass)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
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
    /// STRAIGHT AND AXIS-ALIGNED. Northgate's roads all are. A road that bends still rasterises
    /// correctly, because that is done from Points; it is only the centre line below that
    /// assumes a straight run, and <see cref="IsStraight"/> says whether it may be trusted.
    /// </summary>
    public sealed class RoadLine
    {
        public readonly string Name;
        public readonly RoadClass Class;

        /// <summary>Corridor width in tiles, as declared.</summary>
        public readonly int Width;

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

        public RoadLine(string name, RoadClass klass, int width, IReadOnlyList<Tile> points)
        {
            Name = name ?? "";
            Class = klass;
            Width = width;
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

        public Junction(RoadLine ns, RoadLine ew)
        {
            NorthSouth = ns;
            EastWest = ew;
            X = ns.Centre;
            Y = ew.Centre;
        }

        /// <summary>
        /// How far from the centre the crossing reaches - half the WIDER corridor, because the
        /// junction tile is square and sized to the road that needs the most room.
        /// </summary>
        public float Reach => Math.Max(NorthSouth.HalfWidth, EastWest.HalfWidth);
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
                if (!ns.IsStraight || !ns.IsNorthSouth) continue;

                for (int j = 0; j < Lines.Count; j++)
                {
                    var ew = Lines[j];
                    if (!ew.IsStraight || ew.IsNorthSouth) continue;

                    // They cross only where each one's centre falls inside the other's run.
                    if (ns.Centre < ew.From || ns.Centre > ew.To) continue;
                    if (ew.Centre < ns.From || ew.Centre > ns.To) continue;

                    crossings.Add(new Junction(ns, ew));
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
                if (!line.IsStraight) continue;
                float across = line.IsNorthSouth ? x : y;
                float along = line.IsNorthSouth ? y : x;
                if (Math.Abs(across - line.Centre) > line.HalfWidth) continue;
                if (along < line.From || along > line.To) continue;
                if (best == null || line.Width > best.Width) best = line;
            }
            return best;
        }
    }
}
