# Evaluate IDEAS 899 (Chicago Street's corridor through buildings) and 910 (CSX offset)
# against Content/ as it stands. No Unity, no guessing.
import io, math, re, sys

C = r"C:\SerialKillerGame\Content"

def read(name):
    with io.open(C + "\\" + name, "r", encoding="utf-8-sig") as f:
        return f.read().splitlines()

def polys_from_parcels():
    out = []
    for ln in read("parcels.txt"):
        ln = ln.strip()
        if not ln or ln.startswith("#"): continue
        pts = []
        for tok in ln.split():
            if "," in tok:
                a, b = tok.split(",", 1)
                try: pts.append((float(a), float(b)))
                except ValueError: pass
        if len(pts) >= 3: out.append(pts)
    return out

def polys_from_buildings():
    out = []
    for ln in read("parcel-buildings.txt"):
        ln = ln.strip()
        if " shape " not in ln: continue
        pts = []
        for tok in ln.split():
            if "," in tok:
                a, b = tok.split(",", 1)
                try: pts.append((float(a), float(b)))
                except ValueError: pass
        if len(pts) >= 3: out.append(pts)
    return out

def bbox(p):
    xs = [q[0] for q in p]; ys = [q[1] for q in p]
    return (min(xs), min(ys), max(xs), max(ys))

def inside(pt, poly):
    x, y = pt; n = len(poly); c = False; j = n - 1
    for i in range(n):
        xi, yi = poly[i]; xj, yj = poly[j]
        if ((yi > y) != (yj > y)) and (x < (xj - xi) * (y - yi) / (yj - yi + 1e-12) + xi):
            c = not c
        j = i
    return c

def centreline(name):
    for ln in read("roads.txt"):
        if ln.startswith("road " + name + " "):
            body = ln.split("#")[0].split()
            pts = []
            for tok in body:
                if "," in tok:
                    a, b = tok.split(",", 1)
                    pts.append((float(a), float(b)))
            width = float(body[2])
            return pts, width
    return None, None

def rails():
    out = []
    for ln in read("features.txt"):
        if not ln.startswith("rail "): continue
        pts = []
        for tok in ln.split()[1:]:
            if "," in tok:
                a, b = tok.split(",", 1)
                pts.append((float(a), float(b)))
        if len(pts) >= 2: out.append(pts)
    return out

def samples(pts, step=4.0):
    """Walk the polyline, yielding (point, unit normal)."""
    for i in range(len(pts) - 1):
        (x0, y0), (x1, y1) = pts[i], pts[i + 1]
        dx, dy = x1 - x0, y1 - y0
        L = math.hypot(dx, dy)
        if L < 1e-9: continue
        ux, uy = dx / L, dy / L
        nx, ny = -uy, ux
        k = int(L / step)
        for s in range(k + 1):
            t = s * step
            yield (x0 + ux * t, y0 + uy * t), (nx, ny)

def hits(points, polys, index):
    """Return (n_points_hit, set_of_poly_indices_hit)."""
    hit_pts = 0; which = set()
    for pt in points:
        got = False
        for pi in index:
            b = index[pi]
            if pt[0] < b[0] or pt[0] > b[2] or pt[1] < b[1] or pt[1] > b[3]: continue
            if inside(pt, polys[pi]):
                which.add(pi); got = True
        if got: hit_pts += 1
    return hit_pts, which

buildings = polys_from_buildings()
parcels = polys_from_parcels()
bidx = dict((i, bbox(p)) for i, p in enumerate(buildings))
pidx = dict((i, bbox(p)) for i, p in enumerate(parcels))
print("loaded %d building footprints, %d parcels" % (len(buildings), len(parcels)))

# ---- 899: Chicago Street's corridor against building footprints -------------
pts, width = centreline("chicago")
print("\n=== IDEAS 899 - Chicago Street ===")
print("declared corridor in roads.txt: %.1f m" % width)
for w in (width, 30.0):
    pl = []
    for (px, py), (nx, ny) in samples(pts, 4.0):
        half = w / 2.0
        o = -half
        while o <= half + 1e-9:
            pl.append((px + nx * o, py + ny * o))
            o += 1.0
    n, which = hits(pl, buildings, bidx)
    print("  corridor %5.1f m -> %5d of %5d carriageway points on a building, %3d distinct buildings"
          % (w, n, len(pl), len(which)))

# ---- 910: the CSX line against the parcels ---------------------------------
#
# ⚠ "IS THE RAIL INSIDE A PARCEL" IS THE WRONG QUESTION, AND ASKING IT COST A WRONG
# FINDING ON 2026-08-11. features.txt's own header says why:
#
#   "A STREET right of way is a GAP in the parcels because nobody owns it; a RAILROAD
#    right of way is a PARCEL, because the railroad bought it."
#
# So a correctly-placed railroad is inside a parcel 100% of the time, and comparing it
# against ROAD centrelines - which should be inside 0% of the time - compares two things
# with opposite correct answers. The real question is WHICH parcel: the county records
# the railroad's own land as 779, 781 and 785.
#
# PARCEL IDS ARE 0-BASED. parcel-county.txt opens at `parcel 0`. Printing pi+1 is what
# turned "779, 781, 785 - exactly right" into "780, 782, 786 - not the railroad's land".
RAILROAD_OWNS = (779, 781, 785)

print("\n=== IDEAS 910 - the CSX line ===")
from collections import Counter
c = Counter(); tot = 0; nowhere = 0
for r in rails():
    for pt, nrm in samples(r, 8.0):
        if not (600.0 <= pt[1] <= 1500.0): continue      # the platted town
        tot += 1
        got = []
        for pi, b in pidx.items():
            if pt[0] < b[0] or pt[0] > b[2] or pt[1] < b[1] or pt[1] > b[3]: continue
            if inside(pt, parcels[pi]): got.append(pi)   # 0-based, as the county numbers them
        if not got: nowhere += 1
        for g in got: c[g] += 1

own = sum(n for pid, n in c.items() if pid in RAILROAD_OWNS)
print("  in-town rail samples: %d   (on no parcel at all: %d)" % (tot, nowhere))
print("  on the railroad's OWN land (779/781/785): %d of %d (%.1f%%)  <- this is the number"
      % (own, tot, 100.0 * own / max(tot, 1)))
for pid, n in c.most_common(6):
    print("     parcel %4d : %4d samples%s"
          % (pid, n, "   (railroad)" if pid in RAILROAD_OWNS else "   <-- SOMEBODY ELSE'S"))

# ---- CONTROL: do parcels tile through rights of way, or leave them free? ----
# If they tile through everything, the rail figure above is meaningless. Measure a
# road the owner authored and the survey agrees with, the same way.
print("\n=== CONTROL - are parcels a valid instrument here? ===")
for road in ("chicago", "attica", "henderson"):
    pts, w = centreline(road)
    if pts is None:
        print("  %-12s not in roads.txt" % road); continue
    pl = [pt for pt, nrm in samples(pts, 8.0)]
    pl = [p for p in pl if 600.0 <= p[1] <= 1500.0]
    if not pl:
        print("  %-12s no in-town samples" % road); continue
    n, which = hits(pl, parcels, pidx)
    print("  %-12s %4d in-town centreline samples, %4d inside a parcel (%.1f%%)"
          % (road, len(pl), n, 100.0 * n / len(pl)))
