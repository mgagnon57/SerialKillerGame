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
    /// AuthoredInterior's own header. This is the one place the conversion happens: feet to
    /// tiles at one tile per metre, the plan (drawn street-side down, in the editor's own
    /// frame) turned to face whichever way the seated building's real front door actually
    /// faces, and the editor's real-thickness walls - which round to zero tiles between two
    /// rooms drawn edge to edge at one metre per tile - reinstated as at least one tile so
    /// adjacent rooms do not convert into a single open room.
    ///
    /// Spec: docs/superpowers/specs/2026-08-19-authored-interiors-design.md.
    /// </summary>
    public static class FloorPlans
    {
        private const float Feet = 0.3048f;

        /// <summary>
        /// A safety cap on the FINAL (post-shrink) tile gap <see cref="WallGap"/> will accept
        /// between two rooms already known - from <see cref="PlanNeighbourAcross"/>, decided in
        /// the plan's own feet coordinates - to sit on either side of one wall. Rounding and
        /// wall re-insertion should never open a multi-tile gap between two rooms that share a
        /// real wall; if one somehow did, this is what stops a door being placed across it
        /// anyway rather than skipped.
        /// </summary>
        private const int MaxWallGap = 3;

        // ---- the JSON shape, matching the editor's own field names exactly ----------------

        [Serializable]
        private class PlanFile
        {
            public string name;
            public Shell shell;
            public List<PlanRoom> rooms;
            public List<PlanOpening> openings;
            public List<PlanFurniture> furniture;
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

        /// <summary>A room mid-conversion: oriented tile rect. <see cref="ShrinkForWall"/>
        /// floors every shrink at 1x1 on both sides of a pair, so nothing here is ever
        /// dropped - there is no "did wall re-insertion kill this room" flag to carry.</summary>
        private struct Acc
        {
            public string Id;
            public string Name;
            public RoomKind Kind;
            public TileRect Rect;
        }

        // ---- the census, one line for the whole build ---------------------------------

        private static int _consumed, _refused, _doorBlind;

        /// <summary>Zero the tallies. Called once, before a survey pass seats any building.</summary>
        public static void ResetCensus() { _consumed = 0; _refused = 0; _doorBlind = 0; }

        /// <summary>The one line for the whole build, in the survey passes' own bracket style.</summary>
        public static void LogCensus() =>
            Debug.Log($"[floorplans] {_consumed} consumed, {_refused} refused, "
                    + $"{_doorBlind} door-blind");

        /// <summary>
        /// The owner's floor plan for this seated building, oriented to its door - or null if
        /// he never drew one, which is the ordinary case for every lot but the ones he has
        /// actually opened the tool on. A plan file that exists and cannot be turned into a
        /// usable interior is REFUSED rather than silently dropped: named in the log and
        /// counted, same as a plan that opens its front door into a wall (door-blind).
        /// </summary>
        public static AuthoredInterior For(int parcel, int index, TileRect bounds, Tile door,
                                            bool ownerModel)
        {
            string tag = parcel + "-" + index;
            string relPath = "floorplans/" + tag + ".json";

            // NO PLAN DRAWN IS THE ORDINARY CASE. Hundreds of seated buildings have no
            // Content/floorplans/ file at all, and that is not worth a line in the log - only
            // a file that exists and still could not be used is.
            string fullPath = System.IO.Path.Combine(ContentLoader.Root, relPath);
            if (!System.IO.File.Exists(fullPath)) return null;

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

            // rot 0: door on the south edge (door.Y == bounds.Bottom) - plan and world agree.
            // rot 2: door north - flip both axes. rot 1/3: door east/west - swap axes.
            int rot = door.Y == bounds.Bottom ? 0
                    : door.Y == bounds.Y ? 2
                    : door.X == bounds.Right ? 1 : 3;

            var byId = new Dictionary<string, PlanRoom>();
            foreach (var r in plan.rooms)
                if (!string.IsNullOrEmpty(r.id)) byId[r.id] = r;

            var acc = new List<Acc>(plan.rooms.Count);
            foreach (var r in plan.rooms)
            {
                acc.Add(new Acc
                {
                    Id = r.id ?? "",
                    Name = r.name ?? "",
                    Kind = RoomWords.KindFor(r.name),
                    Rect = Orient(r.x, r.y, r.w, r.h, plan.shell, bounds, rot),
                });
            }

            // WALL RE-INSERTION. The editor's rooms are interior rects separated by a real
            // wall thickness - four to six inches, which rounds to zero tiles at one tile per
            // metre - so two rooms drawn edge to edge convert to tile rects that touch, or
            // even overlap by a tile. Left alone that is one open room, not two: for each
            // ordered pair, the LATER room in the plan's own list gives way, shrunk on the
            // side facing the earlier one by the overlap plus one.
            for (int i = 0; i < acc.Count; i++)
                for (int j = i + 1; j < acc.Count; j++)
                    ShrinkForWall(acc, i, j, tag);

            var result = new AuthoredInterior { Furnish = !ownerModel };
            var rectById = new Dictionary<string, TileRect>();
            foreach (var a in acc)
            {
                rectById[a.Id] = a.Rect;
                result.Rooms.Add(new AuthoredRoom(a.Rect, a.Kind, a.Name));
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

                    var pt = OrientPoint(fx, fy, plan.shell, bounds, rot);
                    var doorTile = vertical
                        ? new Tile(Mathf.Clamp(pt.X, bandLo, bandHi), Mathf.Clamp(pt.Y, fixedLo, fixedHi))
                        : new Tile(Mathf.Clamp(pt.X, fixedLo, fixedHi), Mathf.Clamp(pt.Y, bandLo, bandHi));

                    if (seenDoors.Add((doorTile.X, doorTile.Y))) result.Doors.Add(doorTile);
                }
            }

            // FURNITURE: the owner's own pieces, in tile space. Whether they replace the
            // generated furnishing wholesale is AuthoredInterior.Furnish / WorldBuilder.Adopt's
            // call, not this one - this only converts what he placed.
            if (plan.furniture != null)
            {
                foreach (var f in plan.furniture)
                {
                    int yaw = ((Mathf.RoundToInt(f.rot) % 360) + 360) % 360;
                    bool swapped = yaw % 180 != 0;      // a piece's own rot swaps w/h first
                    float fw = swapped ? f.h : f.w;
                    float fh = swapped ? f.w : f.h;
                    var footprint = Orient(f.x, f.y, fw, fh, plan.shell, bounds, rot);
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
        /// Reinstates the wall between rooms <paramref name="i"/> (earlier in the plan's own
        /// list) and <paramref name="j"/> (later) by shrinking one or both of them on the side
        /// facing the other, by the overlap between their oriented tile rects plus one.
        ///
        /// <paramref name="j"/> gives way first, same as always - but only down to 1x1, never
        /// through it. A full one-tile wall can ask for more than a small, already-once-shrunk
        /// room has left: 408 Holmes Street's "Dining / entry" is narrowed to one column by the
        /// hall on one side, then asked for a second tile of height by the bathroom on the
        /// other, which taking the whole amount from it alone would consume entirely. Whatever
        /// <paramref name="j"/> cannot afford comes off <paramref name="i"/> instead, also
        /// floored at 1x1 - safe, because <paramref name="i"/> is only ever the EARLIER room in
        /// a pair here, so shrinking it now cannot reopen a wall already reinstated against some
        /// even-earlier neighbour of its own (a room can only ever be "i" against a strictly
        /// higher-indexed "j"; nothing that already leaned on it as "i" runs again). If even
        /// that is not enough - both already at 1x1 - whatever fits is taken and the shortfall
        /// is logged rather than dropping either room: a thinner wall than intended is still a
        /// wall, and a real one drawn by the owner does not stop being a room because two of its
        /// neighbours left it no floor to spare.
        /// </summary>
        private static void ShrinkForWall(List<Acc> rooms, int i, int j, string tag)
        {
            var ri = rooms[i].Rect;
            var rj = rooms[j].Rect;

            int xOverlap = Mathf.Min(ri.Right, rj.Right) - Mathf.Max(ri.Left, rj.Left) + 1;
            int yOverlap = Mathf.Min(ri.Bottom, rj.Bottom) - Mathf.Max(ri.Top, rj.Top) + 1;
            if (xOverlap <= 0 && yOverlap <= 0) return;   // a gap on both axes, or a bare corner

            bool vertical;
            if (xOverlap > 0 && yOverlap > 0) vertical = yOverlap <= xOverlap;
            else if (xOverlap > 0) { if (yOverlap < 0) return; vertical = true; }
            else { if (xOverlap < 0) return; vertical = false; }

            int required = (vertical ? Mathf.Max(yOverlap, 0) : Mathf.Max(xOverlap, 0)) + 1;
            bool jBelow = (rj.Y * 2 + rj.H) > (ri.Y * 2 + ri.H);
            bool jRight = (rj.X * 2 + rj.W) > (ri.X * 2 + ri.W);

            int axisJ = vertical ? rj.H : rj.W;
            int axisI = vertical ? ri.H : ri.W;
            int takeJ = Mathf.Min(required, axisJ - 1);
            int takeI = Mathf.Min(required - takeJ, axisI - 1);

            if (takeJ + takeI < required)
                Debug.LogWarning($"[floorplans] {tag}: '{rooms[j].Name}' and '{rooms[i].Name}' "
                                + "are pinched to 1x1 on both sides and still overlap - the wall "
                                + "between them is thinner than intended.");

            if (takeJ > 0)
            {
                var r = rooms[j];
                r.Rect = vertical
                    ? (jBelow ? new TileRect(rj.X, rj.Y + takeJ, rj.W, rj.H - takeJ)
                              : new TileRect(rj.X, rj.Y, rj.W, rj.H - takeJ))
                    : (jRight ? new TileRect(rj.X + takeJ, rj.Y, rj.W - takeJ, rj.H)
                              : new TileRect(rj.X, rj.Y, rj.W - takeJ, rj.H));
                rooms[j] = r;
            }

            if (takeI > 0)
            {
                // i shrinks on the side FACING j - the opposite edge from where j just gave
                // ground, since now i is the one handing tiles over.
                var r = rooms[i];
                r.Rect = vertical
                    ? (jBelow ? new TileRect(ri.X, ri.Y, ri.W, ri.H - takeI)
                              : new TileRect(ri.X, ri.Y + takeI, ri.W, ri.H - takeI))
                    : (jRight ? new TileRect(ri.X, ri.Y, ri.W - takeI, ri.H)
                              : new TileRect(ri.X + takeI, ri.Y, ri.W - takeI, ri.H));
                rooms[i] = r;
            }
        }

        /// <summary>
        /// Whether two final (post-shrink) room rects face each other across a thin gap - the
        /// wall a door opening between them would sit in - and if so, that gap's tile band:
        /// a fixed [lo,hi] range on the axis the rects are stacked along, and the [lo,hi] range
        /// of tiles available on the other axis where the two rects' ranges overlap.
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

        /// <summary>
        /// One rectangle, feet to oriented tiles. Plan coordinates: x from the west face, y
        /// from the north face, street at the bottom (south). Orientation: the plan's south
        /// edge maps onto the side of <paramref name="b"/> that carries the door.
        ///
        /// rot 1 and rot 3 are NOT mirror images of each other the way 0 and 2 are - they are
        /// two different 90-degree turns, and a point's own x and y trade places between them
        /// (case 1 reads the plan's y off world-X and its x off world-Y; the case-3/default
        /// swap does the opposite pairing). Picking the wrong one of the two sends the plan's
        /// south edge - the one that is supposed to land on the door - to the world wall
        /// OPPOSITE the door instead, silently: it still compiles, still builds a full interior,
        /// just mirrored onto the wrong side of the building. Verified against `bounds`/`shell`
        /// by direct substitution before trusting either: at rot 1 (selected because the real
        /// door sits on `bounds.Right`), plugging in a room's own south edge (y+h = sd, the
        /// plan's own depth) into each candidate and checking which one reaches world-X = sd
        /// (i.e. `bounds.Right`, relative to `b.X`) rather than world-X = 0 (the wall
        /// opposite it) is what decided which formula belongs to which case here.
        ///
        /// NO CLAMP AGAINST `b`/`shell` HAPPENS HERE ON PURPOSE. `WorldBuilder.Adopt` clamps
        /// every authored room to the unit's own 1-tile-inset interior before it ever reaches
        /// the grid (`WorldBuilder.cs`), so a room whose independently-rounded edges overshoot
        /// the shell by a tile is caught there, not here. Do not re-add a clamp in this file -
        /// it would just be the same clamp twice, one of them stale the moment Core's changes.
        /// </summary>
        private static TileRect Orient(float fx, float fy, float fw, float fh,
                                        Shell shell, TileRect b, int rot)
        {
            int x = Mathf.RoundToInt(fx * Feet), y = Mathf.RoundToInt(fy * Feet);
            int w = Mathf.Max(1, Mathf.RoundToInt(fw * Feet));
            int h = Mathf.Max(1, Mathf.RoundToInt(fh * Feet));
            int sw = Mathf.Max(1, Mathf.RoundToInt(shell.w * Feet));
            int sd = Mathf.Max(1, Mathf.RoundToInt(shell.d * Feet));
            switch (rot)
            {
                case 0: return new TileRect(b.X + x,            b.Y + y,            w, h);
                case 2: return new TileRect(b.X + (sw - x - w), b.Y + (sd - y - h), w, h);
                case 1: return new TileRect(b.X + y,            b.Y + (sw - x - w), h, w);
                default: return new TileRect(b.X + (sd - y - h), b.Y + x,            h, w);
            }
        }

        /// <summary>The same conversion as <see cref="Orient"/> for a single point - an
        /// opening's centre, not a room - so no width or height is clamped to a minimum of
        /// one tile the way a room's is. See <see cref="Orient"/>'s own header for why rot 1
        /// and rot 3 use the formulas they do, not the other way round.</summary>
        private static Tile OrientPoint(float fx, float fy, Shell shell, TileRect b, int rot)
        {
            int x = Mathf.RoundToInt(fx * Feet), y = Mathf.RoundToInt(fy * Feet);
            int sw = Mathf.Max(1, Mathf.RoundToInt(shell.w * Feet));
            int sd = Mathf.Max(1, Mathf.RoundToInt(shell.d * Feet));
            switch (rot)
            {
                case 0: return new Tile(b.X + x,        b.Y + y);
                case 2: return new Tile(b.X + (sw - x), b.Y + (sd - y));
                case 1: return new Tile(b.X + y,        b.Y + (sw - x));
                default: return new Tile(b.X + (sd - y), b.Y + x);
            }
        }
    }
}
