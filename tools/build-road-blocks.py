"""Cut every street into BLOCKS at its crossings, and write them down.

WHY. A sidewalk in Rossville is a property of a block, not of a street: Chicago Street had walks
both sides through the middle of town, one side for a stretch, and nothing out at the edges. Ruling
a whole road at once cannot say that, and ruling every 2 m of it is not a thing anybody would sit
and do. The block - the run between one cross street and the next - is the unit the town is
actually described in, and it is the unit somebody who lived there remembers.

DERIVED, NOT AUTHORED. This is arithmetic on Content/roads.txt: two polylines cross, that is a
junction, and the pieces between consecutive junctions are the blocks. Re-runnable, and safe to
delete. Content/roads-1991.txt is the authored file that points AT these, and it names blocks by
their cross streets rather than by an index, so re-running this after a re-survey does not silently
move somebody's ruling onto a different piece of road.

    python tools/build-road-blocks.py            # report only
    python tools/build-road-blocks.py --write    # write Content/road-blocks.txt
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(HERE, "..", "Content")
OUT = os.path.join(CONTENT, "road-blocks.txt")
WRITE = "--write" in sys.argv

# A junction closer than this to the end of a road is the end of the road, not a block boundary -
# roads in the survey meet at a shared vertex and often overshoot it by a few centimetres.
SNAP = 6.0
# A block shorter than this is a kink at a crossroads rather than a piece of street anybody walked.
SHORTEST = 18.0


def read_roads():
    roads = []
    name = width = klass = None
    pts = []
    with open(os.path.join(CONTENT, "roads.txt"), encoding="utf-8") as fh:
        for raw in fh:
            line = raw.split("#")[0].rstrip()
            m = re.match(r"^road\s+(\S+)\s+(\d+)\s+(.*)$", line)
            if m:
                if name:
                    roads.append({"n": name, "w": width, "c": klass, "pts": pts})
                name, width, klass = m.group(1), int(m.group(2)), "street"
                pts = [tuple(map(float, t.split(","))) for t in m.group(3).split() if "," in t]
                continue
            m = re.match(r"^\s+class\s+(\S+)", line)
            if m and name:
                klass = m.group(1)
    if name:
        roads.append({"n": name, "w": width, "c": klass, "pts": pts})
    return roads


def seg_cross(p, q, r, s):
    """Where two segments cross, or None. Proper crossings only - touching endpoints are the
    same junction seen twice and are picked up by the snap below."""
    (x1, y1), (x2, y2) = p, q
    (x3, y3), (x4, y4) = r, s
    d = (x2 - x1) * (y4 - y3) - (y2 - y1) * (x4 - x3)
    if abs(d) < 1e-9:
        return None
    t = ((x3 - x1) * (y4 - y3) - (y3 - y1) * (x4 - x3)) / d
    u = ((x3 - x1) * (y2 - y1) - (y3 - y1) * (x2 - x1)) / d
    if -0.02 <= t <= 1.02 and -0.02 <= u <= 1.02:
        return (x1 + t * (x2 - x1), y1 + t * (y2 - y1))
    return None


def along(pts, at):
    """How far along a polyline a point sits, in metres from its start."""
    run = 0.0
    best, bestd = 0.0, 1e18
    for i in range(1, len(pts)):
        ax, ay = pts[i - 1]
        bx, by = pts[i]
        dx, dy = bx - ax, by - ay
        L = math.hypot(dx, dy)
        if L < 1e-9:
            continue
        t = max(0.0, min(1.0, ((at[0] - ax) * dx + (at[1] - ay) * dy) / (L * L)))
        px, py = ax + dx * t, ay + dy * t
        d = math.hypot(at[0] - px, at[1] - py)
        if d < bestd:
            bestd, best = d, run + t * L
        run += L
    return best


def length(pts):
    return sum(math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1])
               for i in range(1, len(pts)))


roads = read_roads()
print("roads: %d" % len(roads))

# WHICH LINE OF THE ROAD THIS IS. Five streets - green, grove, harrison, holmes, summit - are two
# separate runs in the survey, and each one's distances start again at zero. Blocks keyed on the
# NAME alone therefore had two different stretches both claiming metres 0 to 137, and everything
# that asks "which block is this point in" took whichever it met first: clicking the south strip of
# Summit could land on the north line's block, and the game's cut would use the wrong offsets.
seen_name = {}
for road in roads:
    road["line"] = seen_name.get(road["n"], 0)
    seen_name[road["n"]] = road["line"] + 1

blocks = []
for road in roads:
    if len(road["pts"]) < 2:
        continue
    total = length(road["pts"])

    # Every other road this one meets, and where.
    meets = []
    for other in roads:
        if other is road or len(other["pts"]) < 2:
            continue
        for i in range(1, len(road["pts"])):
            for j in range(1, len(other["pts"])):
                hit = seg_cross(road["pts"][i - 1], road["pts"][i],
                                other["pts"][j - 1], other["pts"][j])
                if hit:
                    meets.append((along(road["pts"], hit), other["n"]))

    # THE CUTS ARE THINNED, NOT THE BLOCKS. Dropping a block that came out too short deletes
    # that stretch of street from the file altogether - Chicago lost the piece between Park and
    # Maple, and the piece between Stewart and Stufflebeam, and the gaps were invisible because
    # what is left still reads like a list of blocks. Thinning the CUTS instead cannot lose any
    # ground: every metre of the road is still inside exactly one block, and a crossing too close
    # to the last one simply does not start a new one.
    meets.sort()
    cuts = []
    for d, who in meets:
        if d < SHORTEST or d > total - SHORTEST:
            continue                                   # too near an end to be its own block
        if cuts and d - cuts[-1][0] < SHORTEST:
            continue                                   # too near the last crossing
        cuts.append((d, who))

    edges = [(0.0, "end")] + cuts + [(total, "end")]
    for i in range(1, len(edges)):
        a, awho = edges[i - 1]
        b, bwho = edges[i]
        blocks.append({
            "road": road["n"], "c": road["c"], "line": road["line"],
            "from": awho, "to": bwho,
            "a": round(a, 1), "b": round(b, 1),
        })

streets = [b for b in blocks if b["c"] != "alley"]
alleys = [b for b in blocks if b["c"] == "alley"]
print("blocks: %d  (%d on streets, %d on alleys)" % (len(blocks), len(streets), len(alleys)))
print()
for name in ("chicago", "attica", "maple"):
    mine = [b for b in blocks if b["road"] == name]
    if not mine:
        continue
    print("  %s: %d blocks" % (name, len(mine)))
    for b in mine:
        print("     %-12s to %-12s %6.0f m" % (b["from"], b["to"], b["b"] - b["a"]))

if not WRITE:
    print()
    print("report only - pass --write to save Content/road-blocks.txt")
    sys.exit(0)

HEADER = """# ============================================================================
#  THE BLOCKS OF ROSSVILLE - DERIVED, NOT AUTHORED
#
#  Every street cut at its crossings. Regenerated by tools/build-road-blocks.py and
#  safe to delete: it is arithmetic on Content/roads.txt and nothing else.
#
#  WHY IT EXISTS. A sidewalk is a property of a BLOCK, not of a street - Chicago
#  Street had walks both sides through the middle of town and nothing out at the
#  edges. The run between one cross street and the next is the unit the town is
#  described in and the unit somebody who lived there remembers.
#
#  Content/roads-1991.txt is the AUTHORED file that rules on these. It names a block
#  by its two cross streets rather than by a number, so re-running this after a
#  re-survey cannot silently move a ruling onto a different piece of road.
#
#    block <road> <line> <from> <to> <start-m> <end-m>
#
#  from/to are the crossing streets, or `end` where the road runs out. Distances are
#  metres along THAT LINE of the road, from its first point.
#
#  <line> is which run of the road this is, 0 for the first. Five streets - green,
#  grove, harrison, holmes and summit - are two separate runs in the survey, and each
#  one's distances start again at zero. Without the line number two different stretches
#  both claim metres 0 to 137, and anything asking "which block is this point in" takes
#  whichever it meets first: clicking the south strip of Summit lands on the north
#  line's block.
# ============================================================================
"""

lines = [HEADER]
for b in blocks:
    lines.append("block %s %d %s %s %.1f %.1f"
                 % (b["road"], b["line"], b["from"], b["to"], b["a"], b["b"]))
with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    fh.write("\n".join(lines) + "\n")
print()
print("wrote %s" % OUT)
