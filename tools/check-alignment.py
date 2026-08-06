"""Why do the houses sit crooked on their lots?

Three suspects, and this tells them apart instead of guessing:

  1. THE TRANSFORM IS ROTATED. If the fit that carries footprints into village metres is off
     by an angle, EVERY building is turned by the same amount against its lot. That shows up
     as a non-zero MEAN in the house-vs-lot angle, and it would be my bug.

  2. THE TRANSFORM IS SQUASHED. A similarity fit cannot absorb a difference in how longitude
     and latitude were scaled - if parcels.txt used a different cos(lat) than this pipeline
     does, the town is subtly stretched along one axis, which rotates everything that is not
     already axis-aligned. An affine fit CAN absorb it, so if affine beats similarity, that is
     the answer.

  3. THE FOOTPRINTS ARE JUST LOOSE. FEMA traced these automatically and never had a person
     check one. Then the angle error is centred on zero and simply wide, and the houses are
     as good as the source is.

Reports the overlap too, because "the house crosses the lot line" has an innocent explanation
that has to be ruled out: garages really are built on boundaries, and two lots held as one
really do get a house across the old line.
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import importlib.util
_spec = importlib.util.spec_from_file_location("fit_transform", os.path.join(HERE, "fit-transform.py"))
ft = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ft)


def minrect(pts):
    """Rotating-calipers-lite: the min-area oriented box, as (angle_deg, long, short)."""
    P = pts[:-1] if pts[0] == pts[-1] else pts
    best = None
    for i in range(len(P)):
        ax, ay = P[i]
        bx, by = P[(i + 1) % len(P)]
        dx, dy = bx - ax, by - ay
        L = math.hypot(dx, dy)
        if L < 1e-9:
            continue
        ux, uy = dx / L, dy / L
        us = [p[0] * ux + p[1] * uy for p in P]
        vs = [-p[0] * uy + p[1] * ux for p in P]
        w, h = max(us) - min(us), max(vs) - min(vs)
        if best is None or w * h < best[0]:
            best = (w * h, math.degrees(math.atan2(uy, ux)), w, h)
    _, ang, w, h = best
    if h > w:
        ang += 90.0
        w, h = h, w
    return ang % 180.0, w, h


def d90(a, b):
    """Angle between two undirected axes, folded into +/-45 degrees - a rectangle is the same
    rectangle turned 90 degrees, so 89 degrees off is really 1 degree off."""
    d = (a - b) % 90.0
    return d - 90.0 if d > 45.0 else d


def clip_area(sub, clip):
    """Sutherland-Hodgman: area of `sub` inside convex-ish `clip`. The lots are convex enough
    that this is honest for the overlap question; a few L-shaped lots will read slightly low."""
    out = sub[:-1] if sub[0] == sub[-1] else sub[:]
    C = clip[:-1] if clip[0] == clip[-1] else clip[:]
    # Ensure the clip runs counter-clockwise so the inside test has a consistent sign.
    a = 0.0
    for i in range(len(C)):
        x0, y0 = C[i]
        x1, y1 = C[(i + 1) % len(C)]
        a += x0 * y1 - x1 * y0
    if a < 0:
        C = C[::-1]

    for i in range(len(C)):
        if not out:
            return 0.0
        ax, ay = C[i]
        bx, by = C[(i + 1) % len(C)]
        ex, ey = bx - ax, by - ay
        nxt = []
        for j in range(len(out)):
            px, py = out[j]
            qx, qy = out[(j + 1) % len(out)]
            sp = ex * (py - ay) - ey * (px - ax)
            sq = ex * (qy - ay) - ey * (qx - ax)
            if sp >= 0:
                nxt.append((px, py))
            if (sp >= 0) != (sq >= 0):
                t = sp / (sp - sq) if (sp - sq) != 0 else 0.0
                nxt.append((px + t * (qx - px), py + t * (qy - py)))
        out = nxt
    if len(out) < 3:
        return 0.0
    a = 0.0
    for i in range(len(out)):
        x0, y0 = out[i]
        x1, y1 = out[(i + 1) % len(out)]
        a += x0 * y1 - x1 * y0
    return abs(a) * 0.5


def ring_area(r):
    a = 0.0
    P = r[:-1] if r[0] == r[-1] else r
    for i in range(len(P)):
        x0, y0 = P[i]
        x1, y1 = P[(i + 1) % len(P)]
        a += x0 * y1 - x1 * y0
    return abs(a) * 0.5


def pct(v, q):
    v = sorted(v)
    return v[min(len(v) - 1, int(len(v) * q))]


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

    # ---- SUSPECT 2: does an affine fit beat the similarity? -----------------------------
    A = np.array([[src[i][0], src[i][1], 1.0] for i, _ in pairs])
    B = np.array([dst[j] for _, j in pairs])
    sol, *_ = np.linalg.lstsq(A, B, rcond=None)
    ra = np.sqrt((((A @ sol) - B) ** 2).sum(axis=1))
    rs = np.sqrt(((np.array([ft.apply(t, *src[i]) for i, _ in pairs]) - B) ** 2).sum(axis=1))
    print("SUSPECT 2 - is the town squashed?")
    print(f"  similarity residual: p50 {np.median(rs):.3f} m")
    print(f"  affine     residual: p50 {np.median(ra):.3f} m")
    # Recover the two axis scales the affine fit chose; equal means no anisotropy.
    Ax = sol[:2, :].T                    # 2x2
    _, sv, _ = np.linalg.svd(Ax)
    print(f"  affine axis scales: {sv[0]:.1f} and {sv[1]:.1f} m/deg  "
          f"-> anisotropy {100 * (sv[0] / sv[1] - 1):.3f}%")
    print()

    # ---- SUSPECT 1 & 3: how are buildings turned against their lots? --------------------
    d = json.load(open(os.path.join(HERE, "rossville-buildings-seated.json"), encoding="utf-8"))
    rows = []
    for b in d["buildings"]:
        if b["role"] != "primary":
            continue
        lot = village[b["parcel"]]
        la, ld, lw = minrect(lot)
        if ld < 20 or ld > 120 or lw < 8:      # town lots only
            continue
        ba, bd, bw = minrect(b["ring"])
        area = ring_area(b["ring"])
        inside = clip_area(b["ring"], lot)
        rows.append({
            "d": d90(ba, la),
            "out": max(0.0, 1.0 - inside / area) if area > 0 else 0.0,
            "sq": bd / max(bw, 1e-6),
            "parcel": b["parcel"], "sf": round(b["sqft"] or 0),
        })

    ang = [r["d"] for r in rows]
    n = len(rows)
    mean = sum(ang) / n
    print(f"SUSPECT 1 & 3 - house axis against its own lot axis  (n={n})")
    print(f"  MEAN  {mean:+.2f} deg   <- a systematic rotation would show up here")
    print(f"  median {sorted(ang)[n//2]:+.2f}   p10 {pct(ang,.10):+.2f}   p90 {pct(ang,.90):+.2f}")
    absang = [abs(a) for a in ang]
    print(f"  |error|: median {sorted(absang)[n//2]:.2f} deg   p75 {pct(absang,.75):.2f}   "
          f"p90 {pct(absang,.90):.2f}   max {max(absang):.1f}")
    print(f"  within 5 deg of square to the lot: {100*sum(1 for a in absang if a<=5)/n:.0f}%")
    print(f"  more than 15 deg out:              {100*sum(1 for a in absang if a>15)/n:.0f}%")
    print()

    out = [r["out"] for r in rows]
    print("OVERLAP - share of each house's area falling outside its own lot")
    print(f"  fully inside:      {100*sum(1 for v in out if v<=0.001)/n:.0f}%")
    print(f"  crosses the line:  {100*sum(1 for v in out if v>0.001)/n:.0f}%")
    print(f"  more than a tenth outside: {100*sum(1 for v in out if v>0.10)/n:.0f}%")
    print(f"  median overspill among those that cross: "
          f"{100*(sorted([v for v in out if v>0.001])[max(0,len([v for v in out if v>0.001])//2)] if any(v>0.001 for v in out) else 0):.0f}%")
    print()

    worst = sorted(rows, key=lambda r: -abs(r["d"]))[:8]
    print("worst-turned houses (parcel, degrees off, sq ft, share outside):")
    for r in worst:
        print(f"  parcel {r['parcel']:<4} {r['d']:+6.1f} deg  {r['sf']:>6} sq ft  {100*r['out']:.0f}% out")


if __name__ == "__main__":
    main()
