"""Which buildings in Content/city.txt do not stand on a county parcel, and how far off?

The houses are laid out on the idealised street grid at a fixed setback from each block;
Content/parcels.txt is the county's real surveyed cadastre. Where the two disagree a building
ends up over the line - on a verge, a pavement, or the gap between two lots - and anything that
asks which lot a building is on gets no answer. ParcelIndex.FindFor takes the place's bounds
CENTRE and asks which parcel contains it, so that is exactly what this asks.

READ THE 40m BUCKET AS COUNTRYSIDE, NOT AS A FAULT. Corn, copses, paddocks and farmsteads were
never inside the village cadastre and are not supposed to be. The near misses are the fault.

    python tools/off-their-lot.py

43 of them were seated on 2026-08-05. The rest either have no seat that clears the roads and
their neighbours, or are countryside. Moving one is not free: a building nudged toward its
lot's centre can land on a road or across somebody else's doorway, and both have happened -
TheTownIsAllOnePiece and NoRoadRunsThroughABuilding both caught it. Run the suite after.
"""
import io
import math
import re

PARCELS = "Content/parcels.txt"
CITY = "Content/city.txt"


def parcels():
    out = []
    for raw in io.open(PARCELS, encoding="utf-8"):
        s = raw.strip()
        if not s or s.startswith("#"):
            continue
        pts = []
        for tok in s.split():
            if "," not in tok:
                continue
            a, b = tok.split(",", 1)
            try:
                pts.append((float(a), float(b)))
            except ValueError:
                pass
        if len(pts) >= 3:
            xs = [p[0] for p in pts]
            ys = [p[1] for p in pts]
            out.append((pts, min(xs), min(ys), max(xs), max(ys)))
    return out


def inside(pts, x, y):
    hit = False
    n = len(pts)
    j = n - 1
    for i in range(n):
        xi, yi = pts[i]
        xj, yj = pts[j]
        if (yi > y) != (yj > y):
            if x < (xj - xi) * (y - yi) / (yj - yi) + xi:
                hit = not hit
        j = i
    return hit


def dist_to_segment(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    if dx == 0 and dy == 0:
        return math.hypot(px - ax, py - ay)
    t = ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)
    t = max(0.0, min(1.0, t))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def dist_to_polygon(pts, x, y):
    best = 1e18
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % n]
        d = dist_to_segment(x, y, ax, ay, bx, by)
        if d < best:
            best = d
    return best


PLACE = re.compile(r'^place\s+(\S+)\s+(-?[\d.]+),(-?[\d.]+)\s+(\d+)x(\d+)\s+"(.*)"\s*$')

lots = parcels()
places = []
for raw in io.open(CITY, encoding="utf-8"):
    m = PLACE.match(raw.strip())
    if m:
        kind, x, y, w, h, name = m.groups()
        places.append((kind, float(x), float(y), int(w), int(h), name))

rows = []
for kind, x, y, w, h, name in places:
    cx, cy = x + w / 2.0, y + h / 2.0
    if any(inside(p[0], cx, cy) for p in lots
           if p[1] <= cx <= p[3] and p[2] <= cy <= p[4]):
        continue
    near, nd = None, 1e18
    for p in lots:
        d = dist_to_polygon(p[0], cx, cy)
        if d < nd:
            nd, near = d, p
    rows.append((nd, kind, x, y, w, h, name))

rows.sort()
print("orphaned places: %d of %d" % (len(rows), len(places)))
print("")

buckets = [(0, 2), (2, 5), (5, 10), (10, 20), (20, 40), (40, 1e18)]
print("how far the centre is from the NEAREST parcel edge:")
for lo, hi in buckets:
    n = sum(1 for r in rows if lo <= r[0] < hi)
    label = "%g-%g m" % (lo, hi) if hi < 1e18 else "%g m +" % lo
    bar = "#" * int(n / 2)
    print("   %-10s %4d  %s" % (label, n, bar))

print("")
print("by kind:")
kinds = {}
for r in rows:
    kinds[r[1]] = kinds.get(r[1], 0) + 1
for k, n in sorted(kinds.items(), key=lambda kv: -kv[1]):
    print("   %-14s %d" % (k, n))

print("")
print("the ten furthest out:")
for nd, kind, x, y, w, h, name in rows[-10:][::-1]:
    print("   %6.1f m  %-12s %-11s %s" % (nd, kind, "%g,%g" % (x, y), name))

print("")
print("the ten closest (a nudge would fix these):")
for nd, kind, x, y, w, h, name in rows[:10]:
    print("   %6.1f m  %-12s %-11s %s" % (nd, kind, "%g,%g" % (x, y), name))
