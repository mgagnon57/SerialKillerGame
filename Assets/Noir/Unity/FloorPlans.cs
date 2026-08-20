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

        /// <summary>How wide a gap between two rooms is still "a wall" for the purpose of
        /// matching an interior door opening to it, rather than two rooms on opposite sides
        /// of the house that happen to share an x- or y-range.</summary>
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

        /// <summary>A room mid-conversion: oriented tile rect, and whether wall re-insertion
        /// shrank it away entirely.</summary>
        private struct Acc
        {
            public string Id;
            public string Name;
            public RoomKind Kind;
            public TileRect Rect;
            public bool Dropped;
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
                    Dropped = false,
                });
            }

            // WALL RE-INSERTION. The editor's rooms are interior rects separated by a real
            // wall thickness - four to six inches, which rounds to zero tiles at one tile per
            // metre - so two rooms drawn edge to edge convert to tile rects that touch, or
            // even overlap by a tile. Left alone that is one open room, not two: for each
            // ordered pair, the LATER room in the plan's own list gives way, shrunk on the
            // side facing the earlier one by the overlap plus one.
            for (int i = 0; i < acc.Count; i++)
            {
                if (acc[i].Dropped) continue;
                for (int j = i + 1; j < acc.Count; j++)
                {
                    if (acc[j].Dropped) continue;
                    ShrinkForWall(acc, i, j, tag);
                }
            }

            var result = new AuthoredInterior { Furnish = !ownerModel };
            var rectById = new Dictionary<string, TileRect>();
            foreach (var a in acc)
            {
                if (a.Dropped) continue;
                rectById[a.Id] = a.Rect;
                result.Rooms.Add(new AuthoredRoom(a.Rect, a.Kind, a.Name));
            }

            if (result.Rooms.Count == 0)
            {
                _refused++;
                Debug.LogWarning($"[floorplans] {tag}: every room was shrunk away by wall "
                                + "re-insertion, generated interior kept.");
                return null;
            }

            // INTERIOR DOORS: the wall tile between the two rooms an opening actually
            // connects, nearest the opening's own centre. An opening with no second room
            // across it - the front door, the back door, and every window, none of which
            // carry a facing room in the plan - is an exterior opening and is skipped;
            // PlaceSpec.Door stays the one legal front door.
            if (plan.openings != null)
            {
                var seenDoors = new HashSet<(int X, int Y)>();
                foreach (var op in plan.openings)
                {
                    if (op.kind != "door") continue;
                    if (op.roomId == null || !byId.TryGetValue(op.roomId, out var room)) continue;
                    if (!rectById.TryGetValue(op.roomId, out var aRect)) continue;
                    if (!EdgePoint(room, op, out float fx, out float fy)) continue;

                    var pt = OrientPoint(fx, fy, plan.shell, bounds, rot);

                    bool found = false;
                    Tile bestTile = default;
                    int bestDist = int.MaxValue;
                    foreach (var kv in rectById)
                    {
                        if (kv.Key == op.roomId) continue;
                        if (!WallGap(aRect, kv.Value, out bool vertical, out int fixedLo,
                                     out int fixedHi, out int bandLo, out int bandHi))
                            continue;

                        var candidate = vertical
                            ? new Tile(Mathf.Clamp(pt.X, bandLo, bandHi),
                                       Mathf.Clamp(pt.Y, fixedLo, fixedHi))
                            : new Tile(Mathf.Clamp(pt.X, fixedLo, fixedHi),
                                       Mathf.Clamp(pt.Y, bandLo, bandHi));
                        int dist = Tile.DistanceSquared(candidate, pt);
                        if (dist < bestDist) { bestDist = dist; bestTile = candidate; found = true; }
                    }

                    if (!found) continue;
                    if (seenDoors.Add((bestTile.X, bestTile.Y))) result.Doors.Add(bestTile);
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

        /// <summary>Shrinks room <paramref name="j"/> (the later of an ordered pair) on the
        /// side facing room <paramref name="i"/> by the overlap between their oriented tile
        /// rects, plus one - reinstating the wall the editor's real thickness rounded away.
        /// Drops <paramref name="j"/>, with a warning, if that leaves it under 1x1.</summary>
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

            var r = rooms[j];
            var rect = r.Rect;
            if (vertical)
            {
                int shrink = Mathf.Max(yOverlap, 0) + 1;
                bool jBelow = (rj.Y * 2 + rj.H) > (ri.Y * 2 + ri.H);
                rect = jBelow
                    ? new TileRect(rect.X, rect.Y + shrink, rect.W, rect.H - shrink)
                    : new TileRect(rect.X, rect.Y, rect.W, rect.H - shrink);
            }
            else
            {
                int shrink = Mathf.Max(xOverlap, 0) + 1;
                bool jRight = (rj.X * 2 + rj.W) > (ri.X * 2 + ri.W);
                rect = jRight
                    ? new TileRect(rect.X + shrink, rect.Y, rect.W - shrink, rect.H)
                    : new TileRect(rect.X, rect.Y, rect.W - shrink, rect.H);
            }

            if (rect.W < 1 || rect.H < 1)
            {
                Debug.LogWarning($"[floorplans] {tag}: room '{r.Name}' shrunk below 1x1 by the "
                                + $"wall against '{rooms[i].Name}' - dropped.");
                r.Dropped = true;
                rooms[j] = r;
                return;
            }
            r.Rect = rect;
            rooms[j] = r;
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
                case 1: return new TileRect(b.X + (sd - y - h), b.Y + x,            h, w);
                default: return new TileRect(b.X + y,            b.Y + (sw - x - w), h, w);
            }
        }

        /// <summary>The same conversion as <see cref="Orient"/> for a single point - an
        /// opening's centre, not a room - so no width or height is clamped to a minimum of
        /// one tile the way a room's is.</summary>
        private static Tile OrientPoint(float fx, float fy, Shell shell, TileRect b, int rot)
        {
            int x = Mathf.RoundToInt(fx * Feet), y = Mathf.RoundToInt(fy * Feet);
            int sw = Mathf.Max(1, Mathf.RoundToInt(shell.w * Feet));
            int sd = Mathf.Max(1, Mathf.RoundToInt(shell.d * Feet));
            switch (rot)
            {
                case 0: return new Tile(b.X + x,          b.Y + y);
                case 2: return new Tile(b.X + (sw - x),   b.Y + (sd - y));
                case 1: return new Tile(b.X + (sd - y),   b.Y + x);
                default: return new Tile(b.X + y,          b.Y + (sw - x));
            }
        }
    }
}
