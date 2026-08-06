"""Find the alleys by looking at what the lots leave empty.

WHY NOT JUST DOWNLOAD THEM. Vermilion County's street centreline layer has 146 segments in
Rossville and not one alley among them - counties map the streets they maintain and address
from, and an alley is neither. OpenStreetMap has them no better. But an alley does not need to
be downloaded, because it is already in the parcel data as an absence: the public right of way
is the ground nobody owns, so it is exactly the hole the assessor's polygons leave behind.
That is a survey-grade source, and it is the same one SOURCES-OF-TRUTH.md already says wins
whenever city.txt disagrees with it.

THE METHOD, which exploits the fact that a platted town is a grid:

  1. Rasterise every parcel at 1 m. What is left over is public land - streets, alleys and the
     odd scrap.
  2. Cut out the street corridors, using the county centrelines. What survives inside a block
     is the alley and nothing else.
  3. Take each connected leftover, and keep it if it is long and thin - an alley is 4.5 m wide
     and runs the length of a block. A yard-sized scrap between two lots is not an alley.
  4. Fit each one's own axis and walk its length, taking the middle of the gap at each step.
     That gives a centreline that follows the gap rather than a straight line drawn through it.

The result is an alley network that is on its right of way by construction rather than by
correction - which is the difference between this and the 2026-08-04 refit, which could only
slide a wrong line sideways.
"""
import json, math, os, sys
import numpy as np
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(HERE, "..", "Content")
sys.path.insert(0, HERE)
import importlib.util


def _load(name, path):
    s = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(s)
    s.loader.exec_module(m)
    return m


ft = _load("fit_transform", os.path.join(HERE, "fit-transform.py"))
cr = _load("check_roads", os.path.join(HERE, "check-roads.py"))

RES = 1.0            # metres per cell
STREET_CLEAR = 11.0  # half a street right of way plus a margin - see SOURCES-OF-TRUTH section 2
MIN_LEN = 40.0       # an alley runs a block; anything shorter is a gap between two lots
MAX_WIDE = 9.0       # an alley right of way measures about 4.5 m; 9 is generous
MIN_AREA = 150.0


def rasterise(rings, x0, y0, w, h):
    """Parcel coverage as a boolean grid. Scanline fill, so a 2000 x 2400 m town is instant."""
    grid = np.zeros((h, w), dtype=bool)
    for r in rings:
        ys = [p[1] for p in r]
        lo = max(0, int((min(ys) - y0) / RES))
        hi = min(h - 1, int((max(ys) - y0) / RES) + 1)
        for row in range(lo, hi + 1):
            yc = y0 + (row + 0.5) * RES
            xs = []
            for i in range(len(r) - 1):
                xa, ya = r[i]
                xb, yb = r[i + 1]
                if (ya > yc) != (yb > yc):
                    xs.append(xa + (yc - ya) / (yb - ya) * (xb - xa))
            xs.sort()
            for i in range(0, len(xs) - 1, 2):
                ca = max(0, int((xs[i] - x0) / RES))
                cb = min(w - 1, int((xs[i + 1] - x0) / RES))
                if cb >= ca:
                    grid[row, ca:cb + 1] = True
    return grid


def stamp_lines(grid, lines, x0, y0, w, h, radius):
    """Mark everything within `radius` of a polyline."""
    pts = []
    for pl in lines:
        for i in range(len(pl) - 1):
            (ax, ay), (bx, by) = pl[i], pl[i + 1]
            d = math.hypot(bx - ax, by - ay)
            n = max(1, int(d / RES))
            for j in range(n + 1):
                t = j / n
                pts.append((ax + (bx - ax) * t, ay + (by - ay) * t))
    seed = np.zeros((h, w), dtype=bool)
    for x, y in pts:
        c, rr = int((x - x0) / RES), int((y - y0) / RES)
        if 0 <= rr < h and 0 <= c < w:
            seed[rr, c] = True
    if not seed.any():
        return grid
    dist = ndimage.distance_transform_edt(~seed) * RES
    return grid | (dist <= radius)


def centreline(mask, x0, y0, dist):
    """Walk the long axis of a blob, taking the MEDIAL point at each step.

    Not the midpoint of the gap's extent, which was the first attempt and is wrong wherever an
    alley opens out - at a street junction, or where a lot has been cleared, the free space
    bulges to one side and the midpoint of the extent slides into somebody's back garden with
    it. The point furthest from any parcel cannot do that: it is the middle of the driveable
    gap by definition, and where the gap is a plain 13 ft strip the two agree anyway.
    """
    ys, xs = np.nonzero(mask)
    P = np.column_stack([x0 + (xs + 0.5) * RES, y0 + (ys + 0.5) * RES])
    D = dist[ys, xs]
    c = P.mean(axis=0)
    u, s, vt = np.linalg.svd(P - c, full_matrices=False)
    axis = vt[0]
    perp = np.array([-axis[1], axis[0]])
    tproj = (P - c) @ axis
    lo, hi = tproj.min(), tproj.max()
    if hi - lo < MIN_LEN:
        return None, 0.0, 0.0

    step = 5.0
    pts, widths = [], []
    n = max(2, int((hi - lo) / step))
    for i in range(n + 1):
        t = lo + (hi - lo) * i / n
        band = np.abs(tproj - t) <= step * 0.75
        if band.sum() < 3:
            continue
        idx = np.nonzero(band)[0]
        best = idx[np.argmax(D[idx])]
        pts.append((float(P[best][0]), float(P[best][1])))
        widths.append(float(D[best]) * 2.0)
    if len(pts) < 2:
        return None, 0.0, 0.0

    # A medial point jitters by up to a cell; smooth along the run so the line does not zigzag.
    arr = np.array(pts)
    if len(arr) >= 5:
        ker = np.ones(3) / 3.0
        arr[1:-1, 0] = np.convolve(arr[:, 0], ker, mode="same")[1:-1]
        arr[1:-1, 1] = np.convolve(arr[:, 1], ker, mode="same")[1:-1]
    return [tuple(p) for p in arr], hi - lo, float(np.median(widths)) if widths else 0.0


def simplify(pts, tol=0.6):
    """Douglas-Peucker, so a straight alley is two points rather than forty."""
    if len(pts) < 3:
        return pts
    a, b = np.array(pts[0]), np.array(pts[-1])
    ab = b - a
    L = np.hypot(*ab)
    if L < 1e-9:
        return [pts[0], pts[-1]]
    worst, wi = -1.0, 0
    for i in range(1, len(pts) - 1):
        ap = np.array(pts[i]) - a
        d = abs(ab[0] * ap[1] - ab[1] * ap[0]) / L   # 2-D cross; numpy 2 dropped the 2-vector form
        if d > worst:
            worst, wi = d, i
    if worst <= tol:
        return [pts[0], pts[-1]]
    return simplify(pts[:wi + 1], tol)[:-1] + simplify(pts[wi:], tol)


def stitch(alleys, gap=34.0, tol_deg=12.0):
    """Rejoin the fragments that a street crossing cut in two.

    Step 2 cuts the street corridors out of the free space, which is what isolates the alleys -
    but it also chops an alley into a separate piece per block. Rossville's alleys run the
    length of several blocks, and the game wants one continuous way to walk down, not four.

    Two fragments are joined when they point the same way, their ends are within a street's
    width of each other, and the join carries on in the same direction as both - so two
    parallel alleys either side of a street are never welded into one.
    """
    def head(a):
        return np.array(a["pts"][0]), np.array(a["pts"][1])

    def tail(a):
        return np.array(a["pts"][-1]), np.array(a["pts"][-2])

    def unit(p, q):
        v = p - q
        n = np.hypot(*v)
        return v / n if n > 1e-9 else v

    items = [dict(a) for a in alleys]
    merged = True
    while merged:
        merged = False
        for i in range(len(items)):
            if items[i] is None:
                continue
            for j in range(len(items)):
                if i == j or items[j] is None:
                    continue
                a, b = items[i], items[j]
                at, at2 = tail(a)
                bh, bh2 = head(b)
                d = float(np.hypot(*(bh - at)))
                if d > gap:
                    continue
                ua, ub = unit(at, at2), unit(bh2, bh)
                ubridge = unit(bh, at)
                cos_ab = float(np.dot(ua, ub))
                cos_bridge = float(np.dot(ua, ubridge))
                if cos_ab < math.cos(math.radians(tol_deg)):
                    continue
                if cos_bridge < math.cos(math.radians(tol_deg)):
                    continue
                items[i] = {"pts": a["pts"] + b["pts"],
                            "len": a["len"] + b["len"] + d,
                            "width": (a["width"] + b["width"]) * 0.5}
                items[j] = None
                merged = True
                break
    return [a for a in items if a is not None]


def main():
    village = ft.load_village()
    geo = ft.load_geo()
    lat0 = sum(ft.ring_centroid(r)[1] for r in geo) / len(geo)
    lon0 = sum(ft.ring_centroid(r)[0] for r in geo) / len(geo)
    k = math.cos(math.radians(lat0))
    M = 111320.0
    geo_m = [[((x - lon0) * k * M, (y - lat0) * M) for x, y in g] for g in geo]
    cands = ft.candidates(geo_m, village)
    dst = [ft.ring_centroid(v) for v in village]
    src = [((ft.ring_centroid(g)[0] - lon0) * k, -(ft.ring_centroid(g)[1] - lat0)) for g in geo]
    t, inl = ft.ransac(src, dst, cands)
    t, pairs = ft.refine(src, dst, inl, t, tol=20.0)
    T = ft.fit_affine([src[i] for i, _ in pairs], [dst[j] for _, j in pairs])
    T, pairs = ft.refine_affine(src, dst, pairs, T, tol=20.0)
    to_village = lambda lon, lat: ft.apply_affine(T, (lon - lon0) * k, -(lat - lat0))

    # Only the platted town: out in the country the parcels tile farmland and there is no gap
    # to find. The town is where the lots are small.
    town = [r for r in village
            if (max(p[0] for p in r) - min(p[0] for p in r)) < 120
            and (max(p[1] for p in r) - min(p[1] for p in r)) < 120]
    xs = [p[0] for r in town for p in r]
    ys = [p[1] for r in town for p in r]
    pad = 30
    x0, y0 = min(xs) - pad, min(ys) - pad
    w = int((max(xs) + pad - x0) / RES) + 1
    h = int((max(ys) + pad - y0) / RES) + 1
    print(f"town envelope {w} x {h} m")

    owned = rasterise(village, x0, y0, w, h)
    print(f"  private land: {owned.sum() / 1e4:.1f} ha")

    streets = cr.county_roads(to_village)
    blocked = stamp_lines(owned.copy(), [pl for v in streets.values() for pl in v],
                          x0, y0, w, h, STREET_CLEAR)

    free = ~blocked
    # Only keep gaps that are actually inside the built-up town: a gap on the outside of the
    # edge lots is open country, not an alley.
    inside = ndimage.binary_closing(owned, structure=np.ones((41, 41)))
    free &= inside

    # Distance to the nearest owned cell: the middle of the gap is where this peaks.
    dist = ndimage.distance_transform_edt(free) * RES

    lab, n = ndimage.label(free, structure=np.ones((3, 3)))
    print(f"  leftover pieces: {n}")

    found = []
    for i in range(1, n + 1):
        m = lab == i
        area = m.sum() * RES * RES
        if area < MIN_AREA:
            continue
        pts, length, width = centreline(m, x0, y0, dist)
        if pts is None or length < MIN_LEN or width > MAX_WIDE:
            continue
        found.append({"pts": simplify(pts), "len": length, "width": width})

    found = stitch(found)
    found.sort(key=lambda a: -a["len"])
    total = sum(a["len"] for a in found)
    print(f"\n{len(found)} alleys derived, {total:.0f} m of them")
    wid = sorted(a["width"] for a in found)
    if wid:
        print(f"  right-of-way width: median {wid[len(wid)//2]:.1f} m "
              f"({wid[len(wid)//2]*3.28084:.1f} ft), min {wid[0]:.1f}, max {wid[-1]:.1f}")

    with open(os.path.join(HERE, "rossville-alleys.json"), "w", encoding="utf-8") as fh:
        json.dump([{"pts": [[round(x, 1), round(y, 1)] for x, y in a["pts"]],
                    "len": round(a["len"], 1), "width": round(a["width"], 1)} for a in found], fh)
    print("wrote tools/rossville-alleys.json")

    # How do they score on the very test the old ones failed?
    P = cr.Parcels(village)
    ins = tot = 0
    for a in found:
        for s in cr.sample(a["pts"]):
            if not P.town_here(s):
                continue
            tot += 1
            if P.hit(s) is not None:
                ins += 1
    print(f"\nderived alleys on private land: {100*ins/max(1,tot):.1f}%  ({ins}/{tot} samples)")

    old = {k: v for k, v in cr.city_roads().items() if k.startswith("alley")}
    oi = ot = 0
    for k2, lines in old.items():
        _, i2, t2 = cr.score(k2, lines, P)
        oi += i2
        ot += t2
    print(f"city.txt alleys on private land: {100*oi/max(1,ot):.1f}%  ({oi}/{ot} samples), "
          f"{len(old)} alleys")


if __name__ == "__main__":
    main()
