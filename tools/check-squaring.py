"""Why does squaring a house to its lot work for some and not others?

The correction is only ever as good as the two angles it is the difference of: the building's
own axis, and the lot's. Both are currently taken from the MINIMUM-AREA RECTANGLE, and that is
a fragile way to ask a polygon which way it points:

  AN L-SHAPED HOUSE. Real houses here are L or T plans - a main mass with an ell running back,
  which is the one thing RESIDENTIAL-1913.md is most insistent about. The smallest box around
  an L is not aligned with either wing; it is aligned with whatever compromise has least area,
  and that can be tens of degrees off the walls.

  A NEARLY SQUARE FOOTPRINT. When the two sides are within a few percent, which is longer is
  decided by noise, so the "long axis" flips 90 degrees between one building and the next.

  AN IRREGULAR LOT. Corner lots, alley cut-offs and the wedge lots along the railroad have
  min-area boxes that point somewhere arbitrary - measured earlier, lots sit a median 0.86 deg
  off the town grid but the worst is 31.5 deg out.

THE ALTERNATIVE tested here is the LENGTH-WEIGHTED DOMINANT EDGE DIRECTION, mod 90 degrees:
add up every edge as a unit vector at four times its angle, weighted by how long it is, and
take the argument. Walls vote in proportion to how much wall they are. It is the same technique
parcels.txt's own header describes using to find the plat's skew, and it does not care whether
a shape is an L, a T, or nearly square.

Also measured: whether the shape HAS a dominant direction at all. The resultant length of that
sum, over the total, is near 1 for a clean rectangle and near 0 for a blob - which is exactly
the confidence signal the current rule lacks, since it will happily rotate a circle.
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import importlib.util


def _load(name, path):
    s = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(s)
    s.loader.exec_module(m)
    return m


ft = _load("fit_transform", os.path.join(HERE, "fit-transform.py"))
ca = _load("check_alignment", os.path.join(HERE, "check-alignment.py"))


def edge_axis(pts):
    """Dominant edge direction mod 90 deg, and how dominant it is (0..1).

    Angles are quadrupled before averaging so that 0 and 90 - the same direction for a
    rectilinear shape - reinforce instead of cancelling.
    """
    P = pts[:-1] if tuple(pts[0]) == tuple(pts[-1]) else pts
    sx = sy = tot = 0.0
    n = len(P)
    for i in range(n):
        x0, y0 = P[i]
        x1, y1 = P[(i + 1) % n]
        dx, dy = x1 - x0, y1 - y0
        L = math.hypot(dx, dy)
        if L < 0.3:
            continue
        a = math.degrees(math.atan2(dy, dx)) % 90.0
        sx += L * math.cos(math.radians(a * 4))
        sy += L * math.sin(math.radians(a * 4))
        tot += L
    if tot <= 0:
        return None, 0.0
    ang = (math.degrees(math.atan2(sy, sx)) / 4.0) % 90.0
    strength = math.hypot(sx, sy) / tot
    return ang, strength


def d90(a, b):
    d = (a - b) % 90.0
    return d - 90.0 if d > 45.0 else d


def pct(v, q):
    v = sorted(v)
    return v[min(len(v) - 1, int(len(v) * q))] if v else 0.0


def report(name, vals):
    a = sorted(abs(x) for x in vals)
    n = len(a)
    print(f"  {name:<34} n={n:<4} |err| median {a[n//2]:5.2f}  p75 {pct(a,.75):5.2f}  "
          f"p90 {pct(a,.90):5.2f}  max {a[-1]:5.1f}   within 2 deg: {100*sum(1 for v in a if v<=2)/n:3.0f}%")


def main():
    village = ft.load_village()
    d = json.load(open(os.path.join(HERE, "rossville-buildings-seated.json"), encoding="utf-8"))

    # The town grid, from the lot edges themselves - a stable outside reference to judge
    # every candidate correction against.
    gx = gy = 0.0
    for r in village:
        a, s = edge_axis(r)
        if a is None:
            continue
        gx += math.cos(math.radians(a * 4))
        gy += math.sin(math.radians(a * 4))
    grid = (math.degrees(math.atan2(gy, gx)) / 4.0) % 90.0
    print(f"town grid from lot edges: {grid:.3f} deg\n")

    rows = []
    for b in d["buildings"]:
        if b["role"] != "primary":
            continue
        lot = village[b["parcel"]]
        la_r, ld, lw = ca.minrect(lot)
        if ld < 20 or ld > 120 or lw < 8:
            continue
        ba_r, bd, bw = ca.minrect(b["ring"])
        la_e, ls = edge_axis(lot)
        ba_e, bs = edge_axis(b["ring"])
        if la_e is None or ba_e is None:
            continue
        rows.append({
            "b": b,
            "aspect": bd / max(bw, 1e-6),
            "bs": bs, "ls": ls,
            "skew_minrect": d90(ba_r, la_r),
            "skew_edges": d90(ba_e, la_e),
            "skew_to_grid": d90(ba_e, grid),
            "lot_off_grid_r": d90(la_r, grid),
            "lot_off_grid_e": d90(la_e, grid),
        })

    print("HOW FAR OFF THE TOWN GRID IS EACH REFERENCE, before any correction:")
    report("lot axis, min-area rect", [r["lot_off_grid_r"] for r in rows])
    report("lot axis, dominant edges", [r["lot_off_grid_e"] for r in rows])
    print("  ^ the lot is the thing buildings are squared TO, so its own wobble is a floor\n")

    print("RESIDUAL AFTER SQUARING - building vs the town grid, corrected each way:")
    report("uncorrected", [r["skew_to_grid"] for r in rows])
    report("current: min-rect vs lot min-rect",
           [d90(r["skew_to_grid"] - r["skew_minrect"], 0) for r in rows])
    report("edges vs lot edges",
           [d90(r["skew_to_grid"] - r["skew_edges"], 0) for r in rows])
    report("edges vs the town grid direct",
           [d90(r["skew_to_grid"] - r["skew_to_grid"], 0) for r in rows])
    print()

    print("WHERE THE CURRENT RULE DISAGREES WITH THE EDGE RULE:")
    diff = [r for r in rows if abs(d90(r["skew_minrect"] - r["skew_edges"], 0)) > 5]
    print(f"  {len(diff)} of {len(rows)} buildings differ by more than 5 deg "
          f"({100*len(diff)/len(rows):.0f}%)")
    sq = [r for r in diff if r["aspect"] < 1.25]
    print(f"    of those, {len(sq)} are nearly square footprints (aspect < 1.25) - the case where "
          f"a min-area box picks its long side by noise")
    print()

    print("CONFIDENCE - does the shape even have a dominant direction?")
    bs = [r["bs"] for r in rows]
    print(f"  building axis strength: p10 {pct(bs,.10):.2f}  median {pct(bs,.50):.2f}  "
          f"p90 {pct(bs,.90):.2f}   (1.0 = a clean rectangle)")
    weak = [r for r in rows if r["bs"] < 0.8]
    print(f"  {len(weak)} buildings score under 0.8 - shapes with no clear direction to square to")
    if weak:
        report("  their residual, edge-corrected",
               [d90(r["skew_to_grid"] - r["skew_edges"], 0) for r in weak])
    strong = [r for r in rows if r["bs"] >= 0.8]
    if strong:
        report("  strong shapes, edge-corrected",
               [d90(r["skew_to_grid"] - r["skew_edges"], 0) for r in strong])
    print()

    print("THE 20-DEGREE CUTOFF - what it currently refuses to touch:")
    over = [r for r in rows if abs(r["skew_edges"]) > 20]
    print(f"  {len(over)} buildings ({100*len(over)/len(rows):.0f}%) are left alone by it")
    if over:
        aligned = sum(1 for r in over if abs(r["lot_off_grid_e"]) > 5)
        print(f"    {aligned} of those sit on a lot that is itself off the grid - the diagonal "
              f"blocks, where being askew is real")


if __name__ == "__main__":
    main()
