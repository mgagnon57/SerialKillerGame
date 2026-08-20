using System;
using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// The owner's hand-drawn floor plan, converted from Content/floorplans/&lt;parcel&gt;-
    /// &lt;index&gt;.json into the oriented, tile-space <see cref="AuthoredInterior"/> Core
    /// consumes as <c>PlaceSpec.AuthoredInterior</c>.
    ///
    /// Core never reads Content/floorplans/ and never learns what a parcel is - see
    /// AuthoredInterior's own header. This is the one place the conversion happens.
    ///
    /// THE PLAN IS FITTED TO THE SEATED FOOTPRINT, NOT SCALED INTO ITS CORNER. Until
    /// 2026-08-19 this file multiplied feet by 0.3048, anchored the result at the building's
    /// north-west corner, and let Core's clamp shear off whatever hung over the far side. It
    /// hung over a long way: 673-0.json is 45.5 x 38.5 ft (14 x 12 tiles) and 408 Holmes
    /// Street seats at 12 x 15 tiles, whose usable interior is 10 x 13 - no rotation of the
    /// one fits inside the other. Measured, the result was a warren of closets covering 43%
    /// of the floor with a ONE-TILE kitchen and two of the three bedrooms a single tile wide.
    ///
    /// So the two sources are reconciled the way this project reconciles every other pair:
    /// the SURVEY wins on size and the PLAN wins on topology. <see cref="FittedAxis"/>
    /// collects the plan's own room-edge coordinates on one axis, and maps them monotonically
    /// onto the seated interior's tiles - every room keeps at least one tile on each axis,
    /// every wall the plan draws between two rooms keeps at least one tile of its own, and
    /// the whole plan spans the full interior. Rooms, interior doors and furniture all travel
    /// through the same two fitted axes, so they cannot disagree with each other. What is
    /// given up is exact proportion: at 408 the plan's 44 ft of width is fitted into 10 m,
    /// so every room is about a quarter narrower than the owner drew it. That is the price of
    /// a footprint the county measured at 12.4 x 14.8 m against a model the owner built at
    /// 13.9 x 11.7 - a ruling for him, not a number this file may invent.
    ///
    /// Spec: docs/superpowers/specs/2026-08-19-authored-interiors-design.md.
    /// </summary>
    public static class FloorPlans
    {
        /// <summary>
        /// A safety cap on the FINAL tile gap <see cref="WallGap"/> will accept between two
        /// rooms already known - from <see cref="PlanNeighbourAcross"/>, decided in the plan's
        /// own feet coordinates - to sit on either side of one wall. The fitted mapping should
        /// never open a multi-tile gap between two rooms that share a real wall; if one somehow
        /// did, this is what stops a door being placed across it anyway rather than skipped.
        /// </summary>
        private const int MaxWallGap = 3;

        /// <summary>Two plan coordinates closer than this are the same edge. The editor draws
        /// to the half foot, so anything under it is float noise, not a wall.</summary>
        private const float CoordEpsilon = 0.1f;

        /// <summary>The narrowest gap between two rooms that counts as a wall worth a tile of
        /// its own. Below it the two rooms were drawn sharing an edge - one wall centreline,
        /// no space between them - and they convert to neighbouring tiles with no wall line.
        /// </summary>
        private const float WallGapFeet = 0.2f;

        /// <summary>1 ft in metres - the same 0.3048 this file used before the 2026-08-19 fit
        /// rewrite (see this class's own header), needed again here because an outline override
        /// is placed straight onto the full seated <see cref="TileRect"/>, not through
        /// <see cref="Fit"/>'s room-band machinery - see <see cref="OutlineFromPlan"/>.</summary>
        private const float FeetToMetres = 0.3048f;

        // ---- the JSON shape, matching the editor's own field names exactly ----------------

        [Serializable]
        private class PlanFile
        {
            public string name;
            public Shell shell;
            public List<PlanRoom> rooms;
            public List<PlanOpening> openings;
            public List<PlanFurniture> furniture;
            public List<PlanPoint> outline;
        }

        [Serializable]
        private class Shell { public float w; public float d; }

        [Serializable]
        private class PlanRoom
        {
            public string id;
            public string name;
            public float x, y, w, h;
        }

        [Serializable]
        private class PlanOpening
        {
            public string id;
            public string roomId;
            public string side;
            public float off;
            public float w;
            public string kind;
        }

        [Serializable]
        private class PlanFurniture
        {
            public string id;
            public string name;
            public string model;
            public float x, y, w, h;
            public float rot;
        }

        /// <summary>
        /// One vertex of an authored outline override, in plan feet - the owner redrawing a
        /// footprint the 2016 imagery got wrong. The cross-task contract states this array as
        /// bare <c>[x,y]</c> tuples, but <c>JsonUtility</c> - the only JSON reader this file (or
        /// anything else in Noir.Unity) uses, deliberately, so nothing here depends on a second
        /// parser - cannot deserialize a jagged array (an array of arrays) at all; it silently
        /// drops the field rather than throwing, which is exactly the kind of silent failure this
        /// project does not tolerate. Every other array in this same file (rooms, openings,
        /// furniture) is already an array of named-field objects for the same reason, so the
        /// editor writes <c>{"x":..,"y":..}</c> entries here too - same JSON meaning the contract
        /// describes (plan feet, same frame as rooms), a shape that actually round-trips.
        /// </summary>
        [Serializable]
        private class PlanPoint { public float x, y; }

        /// <summary>One room's or one piece's extent on one plan axis, in feet.</summary>
        private struct Span
        {
            public float Lo, Hi;
            public Span(float lo, float hi) { Lo = lo; Hi = hi; }
        }

        // ---- the census, one line for the whole build ---------------------------------

        private static int _consumed, _refused, _doorBlind, _unseated;
        private static int _outlineRedrawn, _outlineRejected;

        /// <summary>Zero the tallies. Called once, before a survey pass seats any building.</summary>
        public static void ResetCensus()
        {
            _consumed = 0; _refused = 0; _doorBlind = 0; _unseated = 0;
            _outlineRedrawn = 0; _outlineRejected = 0;
        }

        /// <summary>
        /// Count every plan file whose building never reached the survey's attach point at all -
        /// refused for standing in a street, yielded to a clash, or simply not among the measured
        /// buildings. Such a plan is never read, so without this it is neither consumed nor
        /// refused and the census says nothing about it: a lie by omission in exactly the case
        /// the owner would ask about ("I drew one - where did it go?").
        /// </summary>
        public static void NoteUnseatedPlans(HashSet<string> attachedTags)
        {
            string dir = System.IO.Path.Combine(ContentLoader.Root, "floorplans");
            if (!System.IO.Directory.Exists(dir)) return;
            foreach (string file in System.IO.Directory.GetFiles(dir, "*.json"))
            {
                string tag = System.IO.Path.GetFileNameWithoutExtension(file);
                if (attachedTags.Contains(tag)) continue;
                _unseated++;
                Debug.LogWarning($"[floorplans] {tag}: the building this plan was drawn for was "
                                + "never seated, so the plan was never read.");
            }
        }

        /// <summary>The one line for the whole build, in the survey passes' own bracket style.</summary>
        public static void LogCensus() =>
            Debug.Log($"[floorplans] {_consumed} consumed, {_refused} refused, "
                    + $"{_doorBlind} door-blind, {_unseated} unseated, "
                    + $"{_outlineRedrawn} outline{(_outlineRedrawn == 1 ? "" : "s")} redrawn, "
                    + $"{_outlineRejected} outline{(_outlineRejected == 1 ? "" : "s")} rejected");

        /// <summary>
        /// The owner's floor plan for this seated building, fitted to it and oriented to its
        /// door - or null if he never drew one, which is the ordinary case for every lot but the
        /// ones he has actually opened the tool on. A plan file that exists and cannot be turned
        /// into a usable interior is REFUSED rather than silently dropped: named in the log and
        /// counted, same as a plan that opens its front door into a wall (door-blind).
        /// </summary>
        /// <param name="outline">The owner's redrawn footprint, in the same <see cref="Tile"/>[]
        /// ring shape and world frame as the measured one <see cref="SeatOnSurvey"/> already
        /// assigns to <c>PlaceSpec.Outline</c> - null whenever this plan carries no valid
        /// override, on every early-return path, and whenever <see cref="For"/> itself refuses
        /// the whole plan. The caller's job, not this method's: SeatOnSurvey already set the
        /// measured ring before calling here, and the authored one only OVERWRITES it when this
        /// comes back non-null - see the call site's own comment for why that seam won over a
        /// second file read.</param>
        public static AuthoredInterior For(int parcel, int index, TileRect bounds, Tile door,
                                            bool ownerModel, int units, out Tile[] outline)
        {
            outline = null;
            string tag = parcel + "-" + index;
            string relPath = "floorplans/" + tag + ".json";

            // NO PLAN DRAWN IS THE ORDINARY CASE. Hundreds of seated buildings have no
            // Content/floorplans/ file at all, and that is not worth a line in the log - only
            // a file that exists and still could not be used is.
            string fullPath = System.IO.Path.Combine(ContentLoader.Root, relPath);
            if (!System.IO.File.Exists(fullPath)) return null;

            // A MULTI-UNIT BUILDING KEEPS ITS BSP, so the plan is refused HERE rather than
            // converted, attached, counted consumed and then quietly ignored by Core's
            // `Units == 1` guard - which is how the census came to over-count. The spec's own
            // failure table says the survey report should say why.
            if (units != 1)
            {
                _refused++;
                Debug.LogWarning($"[floorplans] {tag}: a {units}-unit building keeps its "
                                + "generated interior - the plan format has no per-unit story.");
                return null;
            }

            PlanFile plan;
            try
            {
                string text = ContentLoader.Read(relPath);
                plan = JsonUtility.FromJson<PlanFile>(text);
                if (plan == null) throw new Exception("empty JSON");
                if (plan.shell == null || plan.shell.w <= 0f || plan.shell.d <= 0f)
                    throw new Exception("missing or zero shell dimensions");
                if (plan.rooms == null || plan.rooms.Count == 0)
                    throw new Exception("no rooms");
            }
            catch (Exception e)
            {
                _refused++;
                Debug.LogWarning($"[floorplans] {tag}: unreadable, generated interior kept: "
                                + e.Message);
                return null;
            }

            if (!door.IsValid)
            {
                _refused++;
                Debug.LogWarning($"[floorplans] {tag}: this place has no door to orient the "
                                + "plan by, generated interior kept.");
                return null;
            }

            int rot = RotationFor(bounds, door, tag);

            // THE OWNER'S REDRAWN OUTLINE, if he drew one - onto the FULL bounds, not the
            // room-fitted interior. A wall drawn at the shell's own edge has to land on the
            // shell's own edge: MaskToOutline (WorldBuilder) stamps the whole bounds rect and
            // then masks it down to the ring, so an outline built through the rooms' inset Fit
            // would eat the exterior wall it was meant to draw. RotationFor already found the
            // one rigid turn this building's door puts it through; OutlineFromPlan applies that
            // same turn algebraically (no room-band fitting to invert - a boundary is geometry,
            // not a layout) and rounds to the nearest tile the same way SeatOnSurvey's own
            // TilesOf does for the measured ring, so the two are the same kind of object.
            if (plan.outline != null && plan.outline.Count > 0)
            {
                if (plan.outline.Count < 3)
                {
                    _outlineRejected++;
                    Debug.LogWarning($"[floorplans] {tag}: the redrawn outline has fewer than 3 "
                                    + "points - the measured footprint was kept.");
                }
                else
                {
                    var ring = OutlineFromPlan(plan.outline, bounds, rot);
                    if (ring.Length < 3)
                        Debug.LogWarning($"[floorplans] {tag}: the redrawn outline collapsed to "
                                        + "fewer than 3 distinct tiles once rounded - the "
                                        + "measured footprint was kept.");
                    else if (SelfIntersects(ring))
                        Debug.LogWarning($"[floorplans] {tag}: the redrawn outline crosses "
                                        + "itself - the measured footprint was kept.");
                    else
                    {
                        outline = ring;
                    }

                    if (outline != null) _outlineRedrawn++; else _outlineRejected++;
                }
            }

            // THE INTERIOR CORE WILL KEEP, computed the same way WorldBuilder.Adopt computes
            // it - the unit's one-tile inset, its perimeter being the shell wall. Fitting to
            // anything wider would hand Core rooms it then clamps, which is the bug this whole
            // pass exists to end.
            var inner = new TileRect(bounds.X + 1, bounds.Y + 1,
                                     Mathf.Max(1, bounds.W - 2), Mathf.Max(1, bounds.H - 2));

            var xs = new List<Span>(plan.rooms.Count);
            var ys = new List<Span>(plan.rooms.Count);
            foreach (var r in plan.rooms)
            {
                xs.Add(new Span(r.x, r.x + Mathf.Max(0.01f, r.w)));
                ys.Add(new Span(r.y, r.y + Mathf.Max(0.01f, r.h)));
            }

            // THE SPEC'S OWN FAILURE TABLE promises a log for a room drawn outside the shell.
            // The fit brings it back in by construction - it fits the ROOMS, so a stray one
            // simply widens what everything else is squeezed into - but the owner is told,
            // because a room outside the shell is a slip of the mouse, not a design.
            for (int i = 0; i < plan.rooms.Count; i++)
                if (xs[i].Lo < -CoordEpsilon || ys[i].Lo < -CoordEpsilon
                    || xs[i].Hi > plan.shell.w + CoordEpsilon || ys[i].Hi > plan.shell.d + CoordEpsilon)
                    Debug.LogWarning($"[floorplans] {tag}: '{plan.rooms[i].name}' is drawn outside "
                                    + "the shell - fitted back inside it.");

            var fit = new Fit(xs, ys, inner, rot, tag);

            var byId = new Dictionary<string, PlanRoom>();
            for (int i = 0; i < plan.rooms.Count; i++)
                if (!string.IsNullOrEmpty(plan.rooms[i].id)) byId[plan.rooms[i].id] = plan.rooms[i];

            var result = new AuthoredInterior { Furnish = !ownerModel };
            var rectById = new Dictionary<string, TileRect>();
            for (int i = 0; i < plan.rooms.Count; i++)
            {
                var r = plan.rooms[i];
                var rect = fit.RoomRect(i);
                rectById[r.id ?? ""] = rect;
                result.Rooms.Add(new AuthoredRoom(rect, RoomWords.KindFor(r.name), r.name ?? ""));
            }

            // INTERIOR DOORS. An opening's own room, side and offset already say exactly which
            // wall segment it sits on, in the plan's own feet coordinates - so the neighbour
            // across it is found THERE (PlanNeighbourAcross), not by scanning every other
            // room's final tile rect for whichever one happens to be nearest in world space.
            // Two real rooms can sit within a few tiles of an opening in completely different
            // directions - 408 Holmes' front door (din, south, facing the street) sits a few
            // tiles from both br1 and kit, neither of which is across the wall it is actually
            // cut into - and a nearest-in-any-direction search matches whichever of them wins a
            // tile-distance tie-break. That is how a front door facing the street and a back
            // door facing the yard, both genuinely exterior, ended up punched through the
            // br1/din and fam/liv walls instead. An opening with no plan room specifically
            // across its own side - every exterior door, and every window - has nothing for
            // PlanNeighbourAcross to find, and is correctly left unmatched: PlaceSpec.Door
            // stays the one legal front door.
            if (plan.openings != null)
            {
                var seenDoors = new HashSet<(int X, int Y)>();
                foreach (var op in plan.openings)
                {
                    if (op.kind != "door") continue;
                    if (op.roomId == null || !byId.TryGetValue(op.roomId, out var room)) continue;
                    if (!rectById.TryGetValue(op.roomId, out var aRect)) continue;
                    if (!EdgePoint(room, op, out float fx, out float fy)) continue;

                    var neighbour = PlanNeighbourAcross(room, op, plan.rooms);
                    if (neighbour == null) continue;                        // exterior wall
                    if (!rectById.TryGetValue(neighbour.id, out var bRect)) continue;
                    if (!WallGap(aRect, bRect, out bool vertical, out int fixedLo, out int fixedHi,
                                 out int bandLo, out int bandHi))
                        continue;    // the two final rects no longer face each other at all

                    var pt = fit.Point(fx, fy);
                    var doorTile = vertical
                        ? new Tile(Mathf.Clamp(pt.X, bandLo, bandHi), Mathf.Clamp(pt.Y, fixedLo, fixedHi))
                        : new Tile(Mathf.Clamp(pt.X, fixedLo, fixedHi), Mathf.Clamp(pt.Y, bandLo, bandHi));

                    if (seenDoors.Add((doorTile.X, doorTile.Y))) result.Doors.Add(doorTile);
                }
            }

            // FURNITURE: the owner's own pieces, through the same two fitted axes the rooms
            // travelled, so a bed drawn against a wall stays against that wall. Whether they
            // replace the generated furnishing wholesale is AuthoredInterior.Furnish /
            // WorldBuilder.Adopt's call, not this one - this only converts what he placed.
            if (plan.furniture != null)
            {
                foreach (var f in plan.furniture)
                {
                    int yaw = ((Mathf.RoundToInt(f.rot) % 360) + 360) % 360;
                    bool swapped = yaw % 180 != 0;      // a piece's own rot swaps w/h first
                    float fw = Mathf.Max(0.01f, swapped ? f.h : f.w);
                    float fh = Mathf.Max(0.01f, swapped ? f.w : f.h);
                    var footprint = fit.Rect(new Span(f.x, f.x + fw), new Span(f.y, f.y + fh));
                    ReportFurnitureFit(tag, f.name, footprint, result.Rooms);
                    result.Furniture.Add(new AuthoredFurniture(FurnitureWords.KindFor(f.name),
                                                                footprint, f.model));
                }
            }

            // THE CARRIED RULING (Task 1's review). ConnectFrontDoor tunnels inward from the
            // door through anything at all - including one of the owner's own walls - until it
            // meets floor, and it is the one legal caller that may. If the tile one step inside
            // this door does not land inside an authored room, it WILL eat a wall to find
            // somewhere, silently. Named here, not fixed: moving a room to dodge it is not this
            // pass's call to make.
            var inside = StepInFromDoor(bounds, door);
            bool doorOk = inside.IsValid && result.Rooms.Exists(r => r.Bounds.Contains(inside));
            if (!doorOk)
            {
                _doorBlind++;
                Debug.LogWarning($"[floorplans] {tag}: the front door does not open into any "
                                + "authored room - ConnectFrontDoor may tunnel through a wall.");
            }

            _consumed++;
            return result;
        }

        /// <summary>
        /// Which 90-degree turn puts the plan's south edge - the street side the editor always
        /// draws downwards - on the wall this building's front door actually stands in.
        ///
        /// NEAREST EDGE, NOT AN EXACT MATCH. This used to be a chain of equality tests, which
        /// is right for a rectangle and wrong for every shaped building: SeatOnSurvey.DoorFor
        /// walks a shaped building's door INWARD from the box edge until it meets the outline,
        /// so the door sits inside the box, matches no edge, and the chain fell through to its
        /// last branch - stamping the whole plan mirrored onto the west wall, silently. No plan
        /// hits that today (673 is rectangular, so no outline is kept) and it was armed for the
        /// next plan the owner draws on a shaped lot.
        /// </summary>
        private static int RotationFor(TileRect bounds, Tile door, string tag)
        {
            int toBottom = Mathf.Abs(door.Y - bounds.Bottom);
            int toTop = Mathf.Abs(door.Y - bounds.Y);
            int toRight = Mathf.Abs(door.X - bounds.Right);
            int toLeft = Mathf.Abs(door.X - bounds.X);

            // Ties keep the old chain's precedence: south, then north, then east, then west.
            int best = toBottom, rot = 0;
            if (toTop < best) { best = toTop; rot = 2; }
            if (toRight < best) { best = toRight; rot = 1; }
            if (toLeft < best) { best = toLeft; rot = 3; }

            if (best > 1)
                Debug.LogWarning($"[floorplans] {tag}: the door at {door.X},{door.Y} is {best} "
                                + $"tiles from the nearest wall of {bounds} - the plan is turned "
                                + "to the nearest one, which may not be the street side.");
            return rot;
        }

        /// <summary>The spec's failure row for furniture: a piece whose CENTRE is inside a room
        /// but whose footprint pokes out is clamped in by Core; one whose centre is outside every
        /// room is dropped. Core has no logger, so the owner hears about it here.</summary>
        private static void ReportFurnitureFit(string tag, string name, TileRect footprint,
                                                List<AuthoredRoom> rooms)
        {
            foreach (var room in rooms)
            {
                if (!room.Bounds.Contains(footprint.Centre)) continue;
                if (footprint.X < room.Bounds.X || footprint.Y < room.Bounds.Y
                    || footprint.Right > room.Bounds.Right || footprint.Bottom > room.Bounds.Bottom)
                    Debug.LogWarning($"[floorplans] {tag}: '{name}' overhangs '{room.Name}' - "
                                    + "clamped into the room.");
                return;
            }
            Debug.LogWarning($"[floorplans] {tag}: '{name}' sits outside every authored room - "
                            + "dropped.");
        }

        /// <summary>The feet-space centre of an opening, along the side of the room it is cut
        /// into. Plan coordinates: x from the west face, y from the north face.</summary>
        private static bool EdgePoint(PlanRoom room, PlanOpening op, out float fx, out float fy)
        {
            switch (op.side)
            {
                case "S": fx = room.x + op.off + op.w * 0.5f; fy = room.y + room.h; return true;
                case "N": fx = room.x + op.off + op.w * 0.5f; fy = room.y;          return true;
                case "E": fx = room.x + room.w;               fy = room.y + op.off + op.w * 0.5f; return true;
                case "W": fx = room.x;                        fy = room.y + op.off + op.w * 0.5f; return true;
                default:  fx = 0; fy = 0; return false;
            }
        }

        /// <summary>
        /// The one other plan room, if any, that sits directly across this opening's own wall -
        /// decided entirely in the plan's own feet coordinates, before any rounding or
        /// orientation, from the room the opening is cut into, which side of it (the JSON's own
        /// <c>side</c>), and where along that side (<c>off</c>/<c>w</c>). A generous tolerance
        /// allows for the real wall's own thickness; nothing farther than that is a candidate at
        /// all, so a room that merely happens to sit a few tiles away in world space - on a
        /// completely different wall - can never be matched to this opening. Null means the
        /// opening's own wall has nothing on its far side: an exterior door or a window.
        /// </summary>
        private static PlanRoom PlanNeighbourAcross(PlanRoom room, PlanOpening op, List<PlanRoom> allRooms)
        {
            const float Tolerance = 2.0f;   // feet - generous against a real wall's own thickness

            // The opening's own span along the wall - x for a N/S wall, y for an E/W one -
            // measured from the room's own near edge, same as EdgePoint reads it.
            bool ns = op.side == "S" || op.side == "N";
            float segLo = (ns ? room.x : room.y) + op.off;
            float segHi = segLo + op.w;

            foreach (var other in allRooms)
            {
                if (other == room) continue;
                switch (op.side)
                {
                    case "S":
                        if (Mathf.Abs(other.y - (room.y + room.h)) <= Tolerance
                            && Overlaps1D(other.x, other.x + other.w, segLo, segHi)) return other;
                        break;
                    case "N":
                        if (Mathf.Abs((other.y + other.h) - room.y) <= Tolerance
                            && Overlaps1D(other.x, other.x + other.w, segLo, segHi)) return other;
                        break;
                    case "E":
                        if (Mathf.Abs(other.x - (room.x + room.w)) <= Tolerance
                            && Overlaps1D(other.y, other.y + other.h, segLo, segHi)) return other;
                        break;
                    case "W":
                        if (Mathf.Abs((other.x + other.w) - room.x) <= Tolerance
                            && Overlaps1D(other.y, other.y + other.h, segLo, segHi)) return other;
                        break;
                }
            }
            return null;
        }

        private static bool Overlaps1D(float aLo, float aHi, float bLo, float bHi) => aLo < bHi && bLo < aHi;

        /// <summary>
        /// Whether two final room rects face each other across a thin gap - the wall a door
        /// opening between them would sit in - and if so, that gap's tile band: a fixed [lo,hi]
        /// range on the axis the rects are stacked along, and the [lo,hi] range of tiles
        /// available on the other axis where the two rects' ranges overlap.
        /// </summary>
        private static bool WallGap(TileRect a, TileRect b, out bool vertical,
                                     out int fixedLo, out int fixedHi,
                                     out int bandLo, out int bandHi)
        {
            int xOverlap = Mathf.Min(a.Right, b.Right) - Mathf.Max(a.Left, b.Left) + 1;
            if (xOverlap > 0)
            {
                if (a.Bottom + 1 <= b.Top - 1 && b.Top - a.Bottom - 1 <= MaxWallGap)
                {
                    vertical = true; fixedLo = a.Bottom + 1; fixedHi = b.Top - 1;
                    bandLo = Mathf.Max(a.Left, b.Left); bandHi = Mathf.Min(a.Right, b.Right);
                    return true;
                }
                if (b.Bottom + 1 <= a.Top - 1 && a.Top - b.Bottom - 1 <= MaxWallGap)
                {
                    vertical = true; fixedLo = b.Bottom + 1; fixedHi = a.Top - 1;
                    bandLo = Mathf.Max(a.Left, b.Left); bandHi = Mathf.Min(a.Right, b.Right);
                    return true;
                }
            }

            int yOverlap = Mathf.Min(a.Bottom, b.Bottom) - Mathf.Max(a.Top, b.Top) + 1;
            if (yOverlap > 0)
            {
                if (a.Right + 1 <= b.Left - 1 && b.Left - a.Right - 1 <= MaxWallGap)
                {
                    vertical = false; fixedLo = a.Right + 1; fixedHi = b.Left - 1;
                    bandLo = Mathf.Max(a.Top, b.Top); bandHi = Mathf.Min(a.Bottom, b.Bottom);
                    return true;
                }
                if (b.Right + 1 <= a.Left - 1 && a.Left - b.Right - 1 <= MaxWallGap)
                {
                    vertical = false; fixedLo = b.Right + 1; fixedHi = a.Left - 1;
                    bandLo = Mathf.Max(a.Top, b.Top); bandHi = Mathf.Min(a.Bottom, b.Bottom);
                    return true;
                }
            }

            vertical = false; fixedLo = fixedHi = bandLo = bandHi = 0;
            return false;
        }

        /// <summary>The tile one step in from the door, along whichever wall of
        /// <paramref name="bounds"/> it stands in - the same walk <c>WorldBuilder</c>'s
        /// <c>ConnectFrontDoor</c> takes. <see cref="Tile.None"/> if the door is not on the
        /// perimeter at all.</summary>
        private static Tile StepInFromDoor(TileRect bounds, Tile door)
        {
            int dx = 0, dy = 0;
            if (door.X == bounds.X) dx = 1;
            else if (door.X == bounds.Right) dx = -1;
            else if (door.Y == bounds.Y) dy = 1;
            else if (door.Y == bounds.Bottom) dy = -1;
            else return Tile.None;
            return new Tile(door.X + dx, door.Y + dy);
        }

        // ---- the outline override ---------------------------------------------------------

        /// <summary>
        /// The owner's outline points, in plan feet against the full shell (NW origin, south
        /// edge = street), turned onto the seated <paramref name="bounds"/> the same rigid way
        /// <see cref="RotationFor"/>'s turn places everything else - algebraically, not through
        /// <see cref="FittedAxis"/>'s band-fitting, because a polygon boundary has no rooms to
        /// fit. This is the exact forward counterpart of the browser's own point-inverse
        /// (tools/viewer-template.html's fpUnrotatePoint/fpUnrotateRect) - the same four turns,
        /// run the other way: rot 0 identity, rot 2 mirrors both axes, rot 1 and rot 3 swap the
        /// axes and mirror one of them, and are two different turns rather than mirror images of
        /// each other (see <see cref="Fit"/>'s own header for why that distinction matters).
        /// Rounded to the nearest tile the same way <c>SeatOnSurvey.TilesOf</c> rounds the
        /// measured ring, and repeats collapsed the same way, so the two rings are the same kind
        /// of object to everything downstream.
        /// </summary>
        private static Tile[] OutlineFromPlan(List<PlanPoint> pts, TileRect bounds, int rot)
        {
            var outp = new List<Tile>(pts.Count);
            foreach (var pt in pts)
            {
                float mx = pt.x * FeetToMetres, my = pt.y * FeetToMetres;
                float lx, ly;
                switch (rot)
                {
                    case 1: lx = my; ly = bounds.H - mx; break;
                    case 2: lx = bounds.W - mx; ly = bounds.H - my; break;
                    case 3: lx = bounds.W - my; ly = mx; break;
                    default: lx = mx; ly = my; break;
                }
                var t = new Tile(bounds.X + Mathf.RoundToInt(lx), bounds.Y + Mathf.RoundToInt(ly));
                if (outp.Count == 0 || outp[outp.Count - 1].X != t.X || outp[outp.Count - 1].Y != t.Y)
                    outp.Add(t);
            }
            while (outp.Count > 1 && outp[0].X == outp[outp.Count - 1].X
                                  && outp[0].Y == outp[outp.Count - 1].Y)
                outp.RemoveAt(outp.Count - 1);
            return outp.ToArray();
        }

        /// <summary>
        /// A simple O(n^2) segment-crossing test - nothing in Core tests polygon simplicity
        /// (only <see cref="Polygon"/>'s convex overlap and even-odd containment, both of which
        /// silently give a plausible-looking wrong answer on a self-crossing ring rather than
        /// refusing it), so this is the whole check rather than a call to a shared one. Every
        /// edge is tested against every other non-adjacent edge (adjacent edges share an
        /// endpoint by construction and are never a crossing); a proper crossing OR one edge
        /// merely touching the other's interior both count, which is the conservative reading
        /// the brief calls for.
        /// </summary>
        private static bool SelfIntersects(Tile[] ring)
        {
            int n = ring.Length;
            for (int i = 0; i < n; i++)
            {
                Tile a1 = ring[i], a2 = ring[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i + 1 || (i == 0 && j == n - 1)) continue;   // shares an endpoint
                    if (SegmentsCross(a1, a2, ring[j], ring[(j + 1) % n])) return true;
                }
            }
            return false;
        }

        private static bool SegmentsCross(Tile a1, Tile a2, Tile b1, Tile b2)
        {
            long d1 = Cross(b1, b2, a1), d2 = Cross(b1, b2, a2);
            long d3 = Cross(a1, a2, b1), d4 = Cross(a1, a2, b2);
            if (((d1 > 0) != (d2 > 0)) && d1 != 0 && d2 != 0
                && ((d3 > 0) != (d4 > 0)) && d3 != 0 && d4 != 0)
                return true;
            if (d1 == 0 && OnSegment(b1, b2, a1)) return true;
            if (d2 == 0 && OnSegment(b1, b2, a2)) return true;
            if (d3 == 0 && OnSegment(a1, a2, b1)) return true;
            if (d4 == 0 && OnSegment(a1, a2, b2)) return true;
            return false;
        }

        private static long Cross(Tile o, Tile a, Tile b) =>
            (long)(a.X - o.X) * (b.Y - o.Y) - (long)(a.Y - o.Y) * (b.X - o.X);

        private static bool OnSegment(Tile p, Tile q, Tile r) =>
            Mathf.Min(p.X, q.X) <= r.X && r.X <= Mathf.Max(p.X, q.X)
         && Mathf.Min(p.Y, q.Y) <= r.Y && r.Y <= Mathf.Max(p.Y, q.Y);

        // ---- the fit ---------------------------------------------------------------------

        /// <summary>
        /// The plan's two axes fitted onto the seated interior's two, plus which world axis each
        /// plan axis landed on and which way round it runs.
        ///
        /// rot 1 and rot 3 are NOT mirror images of each other the way 0 and 2 are - they are
        /// two different 90-degree turns, and the plan's x and y trade places between them.
        /// Picking the wrong one sends the plan's south edge - the one that is supposed to land
        /// on the door - to the world wall OPPOSITE the door instead, silently: it still
        /// compiles, still builds a full interior, just mirrored onto the wrong side of the
        /// building. The four cases below are the same four the feet-to-tiles version was
        /// verified into by direct substitution, restated as an axis and a direction:
        /// at rot 1 the plan's y (its depth, street at the far end) runs along world X the right
        /// way up, and its x runs backwards along world Y.
        ///
        /// Because each axis SPANS the interior, the plan's south edge lands on the last tile
        /// of the door's own wall at every rotation - which is what makes door alignment hold on
        /// four rotations rather than the two the corner-anchored version managed.
        /// </summary>
        private sealed class Fit
        {
            private readonly FittedAxis _planX, _planY;
            private readonly bool _swap;      // world X takes the plan's y
            private readonly bool _revX, _revY;
            private readonly TileRect _inner;

            public Fit(List<Span> xs, List<Span> ys, TileRect inner, int rot, string tag)
            {
                _inner = inner;
                _swap = rot == 1 || rot == 3;
                _revX = rot == 2 || rot == 3;
                _revY = rot == 2 || rot == 1;
                int forPlanX = _swap ? inner.H : inner.W;
                int forPlanY = _swap ? inner.W : inner.H;
                _planX = new FittedAxis(xs, ys, forPlanX, "x", tag);
                _planY = new FittedAxis(ys, xs, forPlanY, "y", tag);
            }

            public TileRect RoomRect(int i)
            {
                _planX.RoomSpan(i, out int px0, out int px1);
                _planY.RoomSpan(i, out int py0, out int py1);
                return Assemble(px0, px1, py0, py1);
            }

            public TileRect Rect(Span x, Span y)
            {
                _planX.Extent(x.Lo, x.Hi, out int px0, out int px1);
                _planY.Extent(y.Lo, y.Hi, out int py0, out int py1);
                return Assemble(px0, px1, py0, py1);
            }

            public Tile Point(float fx, float fy)
            {
                int px = _planX.Point(fx), py = _planY.Point(fy);
                var r = Assemble(px, px, py, py);
                return new Tile(r.X, r.Y);
            }

            private TileRect Assemble(int px0, int px1, int py0, int py1)
            {
                int x0 = _swap ? py0 : px0, x1 = _swap ? py1 : px1;
                int y0 = _swap ? px0 : py0, y1 = _swap ? px1 : py1;
                if (_revX) { int t = x0; x0 = _inner.W - 1 - x1; x1 = _inner.W - 1 - t; }
                if (_revY) { int t = y0; y0 = _inner.H - 1 - y1; y1 = _inner.H - 1 - t; }
                return new TileRect(_inner.X + x0, _inner.Y + y0, x1 - x0 + 1, y1 - y0 + 1);
            }
        }

        /// <summary>
        /// One plan axis, fitted onto a run of tiles.
        ///
        /// Every coordinate any room's edge sits on is a BREAKPOINT; the stretches between two
        /// consecutive breakpoints are BANDS, and the fit is a choice of how many tiles each
        /// band gets. That decomposition is what makes a wall local rather than global: the half
        /// foot between Bedroom 3 and Bedroom 2 is its own band, and the Living room - which
        /// spans straight across it at a different x - simply includes that band's tile in its
        /// own run. Give every band a tile and every wall the plan draws exists, everywhere it
        /// is drawn, without anything having to hunt for pairs of rooms.
        ///
        /// Tiles are then handed out in proportion to each band's real width, EXCEPT that a band
        /// which is some room's whole extent, or the only thing separating two rooms that would
        /// otherwise touch, is floored at one tile however thin it is. That is the whole rule:
        /// proportion where there is room for it, topology where there is not. When even the
        /// floors do not fit - a plan with more distinct edges on one axis than the seated
        /// building has tiles - the widest droppable bands survive, the narrowest are merged
        /// away, and each merge is logged, because a wall the owner drew has just gone.
        /// </summary>
        private sealed class FittedAxis
        {
            private readonly List<float> _coords = new List<float>();
            private readonly int[] _count;    // tiles per band
            private readonly int[] _pos;      // tile offset of each breakpoint
            private readonly int[] _lo, _hi;  // per room, the band range [lo, hi)
            private readonly int _extent;

            public FittedAxis(List<Span> spans, List<Span> others, int extent,
                               string axis, string tag)
            {
                _extent = Mathf.Max(1, extent);

                var raw = new List<float>(spans.Count * 2);
                foreach (var s in spans) { raw.Add(s.Lo); raw.Add(s.Hi); }
                raw.Sort();
                foreach (float c in raw)
                    if (_coords.Count == 0 || c - _coords[_coords.Count - 1] > CoordEpsilon)
                        _coords.Add(c);
                if (_coords.Count < 2) _coords.Add(_coords[0] + 1f);   // one degenerate band
                int n = _coords.Count - 1;

                _lo = new int[spans.Count];
                _hi = new int[spans.Count];
                for (int i = 0; i < spans.Count; i++)
                {
                    _lo[i] = Nearest(spans[i].Lo);
                    _hi[i] = Nearest(spans[i].Hi);
                    if (_hi[i] <= _lo[i])
                    {
                        _lo[i] = Mathf.Min(_lo[i], n - 1);
                        _hi[i] = _lo[i] + 1;
                    }
                }

                var width = new float[n];
                for (int k = 0; k < n; k++) width[k] = _coords[k + 1] - _coords[k];

                // A band nothing but one room stands on cannot be merged away without that room
                // losing its only tile on this axis.
                var sole = new bool[n];
                for (int i = 0; i < spans.Count; i++)
                    if (_hi[i] - _lo[i] == 1) sole[_lo[i]] = true;

                // A band with a room ending at its near edge and another starting at its far one
                // is the wall between them - but only if the two really do face each other, which
                // is decided on the OTHER axis, in feet, before any of this rounding.
                var must = new bool[n];
                for (int k = 0; k < n; k++)
                {
                    must[k] = sole[k];
                    if (must[k] || width[k] < WallGapFeet) continue;
                    for (int a = 0; a < spans.Count && !must[k]; a++)
                    {
                        if (_hi[a] != k) continue;
                        for (int b = 0; b < spans.Count; b++)
                        {
                            if (_lo[b] != k + 1) continue;
                            if (others[a].Lo < others[b].Hi - CoordEpsilon
                                && others[b].Lo < others[a].Hi - CoordEpsilon)
                            { must[k] = true; break; }
                        }
                    }
                }

                _count = new int[n];
                var dropped = new bool[n];

                int needed = 0;
                for (int k = 0; k < n; k++) if (must[k]) needed++;
                if (needed > _extent)
                {
                    // Merge the narrowest walls first, and a band carrying a whole room only
                    // when nothing else is left. Both are losses; both are said out loud.
                    var order = new List<int>();
                    for (int k = 0; k < n; k++) if (must[k]) order.Add(k);
                    order.Sort((p, q) =>
                    {
                        if (sole[p] != sole[q]) return sole[p] ? 1 : -1;
                        int c = width[p].CompareTo(width[q]);
                        return c != 0 ? c : p.CompareTo(q);
                    });
                    for (int i = 0; i < order.Count && needed > _extent; i++)
                    {
                        int k = order[i];
                        dropped[k] = true; must[k] = false; needed--;
                        Debug.LogWarning($"[floorplans] {tag}: the seated footprint is too small "
                                        + $"for the plan - the {axis} band at {_coords[k]:0.0} ft "
                                        + (sole[k] ? "carrying a whole room " : "carrying a wall ")
                                        + "was merged away.");
                    }
                }

                float total = 0f;
                for (int k = 0; k < n; k++) total += width[k];
                if (total <= 0f) total = 1f;

                var ideal = new float[n];
                int used = 0;
                for (int k = 0; k < n; k++)
                {
                    ideal[k] = width[k] / total * _extent;
                    _count[k] = dropped[k] ? 0
                              : must[k] ? Mathf.Max(1, Mathf.FloorToInt(ideal[k]))
                              : Mathf.FloorToInt(ideal[k]);
                    used += _count[k];
                }

                // Hand out what is left, and take back what was over-spent, always at the band
                // furthest from its own fair share - largest remainder, both directions.
                while (used < _extent)
                {
                    int best = -1; float gap = float.NegativeInfinity;
                    for (int k = 0; k < n; k++)
                    {
                        if (dropped[k]) continue;
                        float g = ideal[k] - _count[k];
                        if (g > gap) { gap = g; best = k; }
                    }
                    if (best < 0) break;
                    _count[best]++; used++;
                }
                while (used > _extent)
                {
                    int best = -1; float over = float.NegativeInfinity;
                    for (int k = 0; k < n; k++)
                    {
                        if (_count[k] <= (must[k] ? 1 : 0)) continue;
                        float o = _count[k] - ideal[k];
                        if (o > over) { over = o; best = k; }
                    }
                    if (best < 0) break;
                    _count[best]--; used--;
                }

                // EVERY ROOM MUST END UP WITH A TILE, and a band being some room's ONLY band is
                // not the whole of that: a room spanning two thin bands can have both rounded to
                // nothing, and then its run collapses onto the boundary tile - which the room on
                // the far side of that boundary already owns. Measured on a deliberately tiny
                // 10x10 box: the Hall, the Bathroom and both Dinings landed on the same tiles.
                // So any starved room's widest band is given a tile, taken from whichever band
                // is furthest above its own fair share. Each repair protects one more band, so
                // it cannot cycle; when there is no tile left to give, the owner is told.
                var floors = new int[n];
                for (int k = 0; k < n; k++) floors[k] = must[k] ? 1 : 0;
                for (int guard = 0; guard <= n; guard++)
                {
                    int starved = -1;
                    for (int i = 0; i < spans.Count && starved < 0; i++)
                    {
                        int have = 0;
                        for (int k = _lo[i]; k < _hi[i]; k++) have += _count[k];
                        if (have > 0) continue;
                        starved = _lo[i];
                        for (int k = _lo[i]; k < _hi[i]; k++)
                            if (width[k] > width[starved]) starved = k;
                    }
                    if (starved < 0) break;

                    int donor = -1; float spare = float.NegativeInfinity;
                    for (int k = 0; k < n; k++)
                    {
                        if (k == starved || _count[k] <= floors[k]) continue;
                        float o = _count[k] - ideal[k];
                        if (o > spare) { spare = o; donor = k; }
                    }
                    if (donor < 0)
                    {
                        Debug.LogWarning($"[floorplans] {tag}: the seated footprint has no tile "
                                        + $"left to give a room on the {axis} axis - two of the "
                                        + "plan's rooms will share floor.");
                        break;
                    }
                    _count[starved]++; _count[donor]--; floors[starved] = 1;
                }

                _pos = new int[n + 1];
                for (int k = 0; k < n; k++) _pos[k + 1] = _pos[k] + _count[k];
            }

            private int Nearest(float v)
            {
                int best = 0; float bd = float.MaxValue;
                for (int i = 0; i < _coords.Count; i++)
                {
                    float d = Mathf.Abs(_coords[i] - v);
                    if (d < bd) { bd = d; best = i; }
                }
                return best;
            }

            /// <summary>Room <paramref name="i"/>'s tile run on this axis, at least one tile.</summary>
            public void RoomSpan(int i, out int lo, out int hi)
            {
                lo = _pos[_lo[i]];
                hi = _pos[_hi[i]] - 1;
                if (hi < lo) hi = lo;
                lo = Mathf.Clamp(lo, 0, _extent - 1);
                hi = Mathf.Clamp(hi, lo, _extent - 1);
            }

            /// <summary>An arbitrary feet extent - a piece of furniture, not a room - through the
            /// same mapping, at least one tile.</summary>
            public void Extent(float lo, float hi, out int t0, out int t1)
            {
                t0 = Point(lo);
                t1 = Point(hi);
                if (t1 > t0) t1--;
                if (t1 < t0) t1 = t0;
            }

            /// <summary>One feet coordinate as a tile, interpolated inside its own band.</summary>
            public int Point(float f)
            {
                if (f <= _coords[0]) return 0;
                if (f >= _coords[_coords.Count - 1]) return _extent - 1;
                int k = 0;
                while (k < _count.Length - 1 && f >= _coords[k + 1] - CoordEpsilon) k++;
                float w = _coords[k + 1] - _coords[k];
                int t = (w <= 0f || _count[k] == 0)
                        ? _pos[k]
                        : _pos[k] + Mathf.FloorToInt((f - _coords[k]) / w * _count[k]);
                return Mathf.Clamp(t, 0, _extent - 1);
            }
        }
    }
}
