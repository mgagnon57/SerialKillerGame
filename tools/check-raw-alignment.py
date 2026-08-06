"""Is the crookedness mine, or was it in the data before I touched it?

The previous check measured houses against lots in VILLAGE metres - after the fit. That cannot
settle blame, so this measures the same thing in RAW WGS84, straight off the two downloads,
with no transform of mine anywhere in it. Whatever shows up here was true before this project
saw the data.

It also stops measuring houses against LOTS and starts measuring both against the TOWN GRID.
A county parcel is often an L, or has an alley corner cut off it, and the minimum-area
rectangle of a shape like that points somewhere arbitrary - so "house against lot" was partly
measuring my own unstable choice of lot axis. Rossville is a platted grid: nearly everything
should be square to one dominant direction, and that direction can be measured from the parcel
edges themselves, length-weighted, the same way parcels.txt's own header describes doing it.

Decomposed that way the question splits in two, and only one of them is answerable:
    lot vs grid      - are the county's lot lines square to the town?
    building vs grid - are FEMA's outlines square to the town?
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import importlib.util
_spec = importlib.util.spec_from_file_location("fit_transform", os.path.join(HERE, "fit-transform.py"))
ft = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ft)
_spec2 = importlib.util.spec_from_file_location("check_alignment", os.path.join(HERE, "check-alignment.py"))
ca = importlib.util.module_from_spec(_spec2)
_spec2.loader.exec_module(ca)


def grid_axis(rings, lo=0.0, hi=90.0):
    """The town's dominant edge direction, length-weighted, mod 90 degrees.

    Averaged as unit vectors at DOUBLE the angle so that 89 degrees and 1 degree - which are
    the same direction in a grid - do not average to 45.
    """
    sx = sy = 0.0
    for r in rings:
        P = r[:-1] if r[0] == r[-1] else r
        for i in range(len(P)):
            x0, y0 = P[i]
            x1, y1 = P[(i + 1) % len(P)]
            dx, dy = x1 - x0, y1 - y0
            L = math.hypot(dx, dy)
            if L < 1.0:
                continue
            a = math.degrees(math.atan2(dy, dx)) % 90.0
            sx += L * math.cos(math.radians(a * 4))
            sy += L * math.sin(math.radians(a * 4))
    return (math.degrees(math.atan2(sy, sx)) / 4.0) % 90.0


def stats(name, vals):
    v = sorted(vals)
    n = len(v)
    a = sorted(abs(x) for x in vals)
    print(f"  {name:<22} n={n:<4} mean {sum(vals)/n:+6.2f}  median {v[n//2]:+6.2f}   "
          f"|err| median {a[n//2]:5.2f}  p90 {a[int(n*.9)]:5.2f}  max {a[-1]:5.1f}")


def main():
    geo = ft.load_geo()
    lat0 = sum(ft.ring_centroid(r)[1] for r in geo) / len(geo)
    lon0 = sum(ft.ring_centroid(r)[0] for r in geo) / len(geo)
    k = math.cos(math.radians(lat0))
    M = 111320.0

    def to_m(ring):
        return [((x - lon0) * k * M, (y - lat0) * M) for x, y in ring]

    geo_m = [to_m(g) for g in geo]

    with open(os.path.join(HERE, "rossville-structures.geojson"), encoding="utf-8") as fh:
        gj = json.load(fh)

    # Structures, in raw local metres, each assigned to the county parcel it stands in.
    boxes = [(min(p[0] for p in g), min(p[1] for p in g),
              max(p[0] for p in g), max(p[1] for p in g)) for g in geo]

    def inside(pt, ring):
        x, y = pt
        hit = False
        for i in range(len(ring) - 1):
            x0, y0 = ring[i]
            x1, y1 = ring[i + 1]
            if (y0 > y) != (y1 > y):
                xi = x0 + (y - y0) / (y1 - y0) * (x1 - x0)
                if x < xi:
                    hit = not hit
        return hit

    pairs = []
    for feat in gj["features"]:
        gm = feat["geometry"]
        if gm is None:
            continue
        parts = gm["coordinates"] if gm["type"] == "MultiPolygon" else [gm["coordinates"]]
        ring = [(p[0], p[1]) for p in max((p[0] for p in parts), key=len)]
        if len(ring) < 4:
            continue
        c = ft.ring_centroid(ring)
        for i, (x0, y0, x1, y1) in enumerate(boxes):
            if x0 <= c[0] <= x1 and y0 <= c[1] <= y1 and inside(c, geo[i]):
                pairs.append((i, to_m(ring), feat["properties"]))
                break

    # Town lots only, so 40-acre farm ground does not set the grid or the statistics.
    town = []
    for gi, bm, props in pairs:
        la, ld, lw = ca.minrect(geo_m[gi])
        if ld < 20 or ld > 120 or lw < 8:
            continue
        town.append((gi, bm, props, la))

    # Biggest structure per lot = the house, same rule the content file uses.
    best = {}
    for gi, bm, props, la in town:
        a = ca.ring_area(bm)
        if gi not in best or a > best[gi][0]:
            best[gi] = (a, bm, props, la)

    axis = grid_axis([geo_m[gi] for gi in best])
    print(f"THE TOWN GRID, measured off the county's own lot edges: {axis:.3f} deg")
    print(f"(everything below is RAW WGS84 - no transform of mine is involved)\n")

    lot_v, bld_v, b2l = [], [], []
    for gi, (a, bm, props, la) in best.items():
        ba, bd, bw = ca.minrect(bm)
        lot_v.append(ca.d90(la, axis))
        bld_v.append(ca.d90(ba, axis))
        b2l.append(ca.d90(ba, la))

    print("ANGLE, degrees off:")
    stats("lot vs town grid", lot_v)
    stats("building vs town grid", bld_v)
    stats("building vs its lot", b2l)
    print()

    sq = sum(1 for v in bld_v if abs(v) <= 5) / len(bld_v)
    print(f"  buildings within 5 deg of the town grid: {100*sq:.0f}%")
    sq = sum(1 for v in lot_v if abs(v) <= 5) / len(lot_v)
    print(f"  lots      within 5 deg of the town grid: {100*sq:.0f}%")
    print()

    # Overlap, again in raw metres.
    over = []
    for gi, (a, bm, props, la) in best.items():
        inside_a = ca.clip_area(bm, geo_m[gi])
        over.append(max(0.0, 1.0 - inside_a / a) if a > 0 else 0.0)
    n = len(over)
    print("OVERLAP in the raw data - share of each house outside its county parcel:")
    print(f"  fully inside {100*sum(1 for v in over if v<=0.001)/n:.0f}%   "
          f"crosses {100*sum(1 for v in over if v>0.001)/n:.0f}%   "
          f">10% out {100*sum(1 for v in over if v>0.10)/n:.0f}%")

    # How square is a FEMA outline to ITSELF? A traced rectangle whose corners are not right
    # angles is loose in a way no placement can fix.
    skew = []
    for gi, (a, bm, props, la) in best.items():
        P = bm[:-1] if bm[0] == bm[-1] else bm
        if len(P) != 4:
            continue
        for i in range(4):
            ax, ay = P[i]
            bx, by = P[(i + 1) % 4]
            cx, cy = P[(i + 2) % 4]
            v1 = (ax - bx, ay - by)
            v2 = (cx - bx, cy - by)
            n1 = math.hypot(*v1)
            n2 = math.hypot(*v2)
            if n1 < .5 or n2 < .5:
                continue
            cosang = (v1[0]*v2[0] + v1[1]*v2[1]) / (n1*n2)
            skew.append(abs(math.degrees(math.acos(max(-1, min(1, cosang)))) - 90.0))
    if skew:
        skew.sort()
        print(f"\nFOUR-CORNER outlines: corner angles differ from a right angle by "
              f"median {skew[len(skew)//2]:.2f} deg, p90 {skew[int(len(skew)*.9)]:.2f}, "
              f"max {skew[-1]:.1f}  (n={len(skew)} corners)")


if __name__ == "__main__":
    main()
