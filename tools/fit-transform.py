"""Recover the lat/lon -> village-metre transform by FITTING it, not by re-deriving it.

Content/parcels.txt and tools/rossville-parcels.geojson are the same 794 county lots: one
in village metres, one in WGS84, written in the same order. So the conversion that produced
the first from the second can be recovered by least squares against 794 matched centroids,
and - the part that matters - its RESIDUAL can be reported. A reconstructed transform that
is subtly wrong looks identical to a right one until something is drawn on the map; a fitted
one states its own error in metres before anything is built on it.

Similarity only - rotation, uniform scale, translation. Reflection is permitted in the solve
purely so that a wrong handedness shows up as a huge residual rather than being silently
absorbed. Prints the parameters and the residual distribution; writes nothing.
"""
import json, math, os, random

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(HERE, "..", "Content")


def ring_centroid(pts):
    """Area centroid of a closed ring, falling back to the mean for degenerate rings.

    SHIFTED TO A LOCAL ORIGIN FIRST, and that is not a nicety. On a raw lon/lat ring the
    cross products are around 87 x 40 = 3,500 while the quantity they encode is around 1e-5,
    so the result is the difference of two nearly equal large numbers and double precision
    eats it. Computed the naive way, this function disagreed with itself by up to 11.7 m
    depending on whether a ring was projected before or after taking its centroid - which
    showed up downstream as a "3 m discrepancy between the county download and parcels.txt"
    that was not in the data at all. Subtracting the first vertex makes every term small and
    the cancellation harmless.
    """
    ox, oy = pts[0]
    a = cx = cy = 0.0
    for i in range(len(pts) - 1):
        x0, y0 = pts[i][0] - ox, pts[i][1] - oy
        x1, y1 = pts[i + 1][0] - ox, pts[i + 1][1] - oy
        cross = x0 * y1 - x1 * y0
        a += cross
        cx += (x0 + x1) * cross
        cy += (y0 + y1) * cross
    if abs(a) < 1e-15:
        n = len(pts) - 1 or len(pts)
        return (sum(p[0] for p in pts[:n]) / n, sum(p[1] for p in pts[:n]) / n)
    a *= 0.5
    return cx / (6 * a) + ox, cy / (6 * a) + oy


def load_village():
    rings = []
    with open(os.path.join(CONTENT, "parcels.txt"), encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            pts = [tuple(float(v) for v in tok.split(",")) for tok in line.split()]
            rings.append(pts)
    return rings


def load_geo():
    with open(os.path.join(HERE, "rossville-parcels.geojson"), encoding="utf-8") as fh:
        gj = json.load(fh)
    rings = []
    for feat in gj["features"]:
        geom = feat["geometry"]
        coords = geom["coordinates"]
        # Take the outer ring of the largest part, so a multipolygon does not silently
        # contribute a garage-sized sliver as if it were the lot.
        parts = coords if geom["type"] == "MultiPolygon" else [coords]
        best, best_n = None, -1
        for part in parts:
            outer = part[0]
            if len(outer) > best_n:
                best, best_n = outer, len(outer)
        rings.append([(p[0], p[1]) for p in best])
    return rings


def fit(src, dst):
    """Least-squares similarity src -> dst (Umeyama, uniform scale)."""
    n = len(src)
    sx = sum(p[0] for p in src) / n
    sy = sum(p[1] for p in src) / n
    dx = sum(p[0] for p in dst) / n
    dy = sum(p[1] for p in dst) / n

    saa = sab = sba = sbb = svar = 0.0
    for (ax, ay), (bx, by) in zip(src, dst):
        ax, ay, bx, by = ax - sx, ay - sy, bx - dx, by - dy
        saa += ax * bx
        sab += ax * by
        sba += ay * bx
        sbb += ay * by
        svar += ax * ax + ay * ay

    # Rotation that best aligns the two clouds, then the scale along it.
    theta = math.atan2(sab - sba, saa + sbb)
    cos_t, sin_t = math.cos(theta), math.sin(theta)
    scale = (cos_t * (saa + sbb) + sin_t * (sab - sba)) / svar

    a = scale * cos_t
    b = scale * sin_t
    tx = dx - (a * sx - b * sy)
    ty = dy - (b * sx + a * sy)
    return a, b, tx, ty, theta, scale


def apply(t, x, y):
    a, b, tx, ty = t[0], t[1], t[2], t[3]
    return a * x - b * y + tx, b * x + a * y + ty


def fit_affine(src, dst):
    """Full six-parameter fit: two independent axis scales and a shear, not just a rotation.

    WHY THE SIMILARITY WAS NOT ENOUGH. It carries one scale for both axes, and the conversion
    that produced parcels.txt did not use quite the same metres-per-degree of longitude that
    this pipeline reconstructs - the two axes differ by 0.197%. A uniform-scale fit cannot
    represent that, so it does the only thing it can: it spreads the mismatch over the whole
    town as position error, up to about 3 m out at the edges. On a lot 20 m wide that is
    enough to push a house across its own boundary, which is exactly what was showing up.

    Six parameters take the median residual from 0.42 m to 0.028 m - about an inch.
    """
    import numpy as np
    A = np.array([[p[0], p[1], 1.0] for p in src])
    B = np.array([[p[0], p[1]] for p in dst])
    sol, *_ = np.linalg.lstsq(A, B, rcond=None)
    return (float(sol[0][0]), float(sol[1][0]), float(sol[2][0]),
            float(sol[0][1]), float(sol[1][1]), float(sol[2][1]))


def apply_affine(T, x, y):
    a, b, tx, c, d, ty = T
    return a * x + b * y + tx, c * x + d * y + ty


def refine_affine(src, dst, pairs, T, tol):
    """Re-pair by position under the affine fit and refit, the same loop as `refine`."""
    import numpy as np
    D = np.array(dst)
    for _ in range(8):
        cur = np.array([apply_affine(T, *p) for p in src])
        d2 = ((cur[:, None, :] - D[None, :, :]) ** 2).sum(axis=2)
        s2d = d2.argmin(axis=1)
        d2s = d2.argmin(axis=0)
        keep = [i for i in range(len(cur)) if d2s[s2d[i]] == i and d2[i, s2d[i]] < tol ** 2]
        if len(keep) < 50:
            return T, pairs
        T = fit_affine([src[i] for i in keep], [dst[s2d[i]] for i in keep])
        pairs = [(i, int(s2d[i])) for i in keep]
    return T, pairs


def shape_sig(pts):
    """Area and perimeter of a ring, in whatever units it is given."""
    a = per = 0.0
    for i in range(len(pts) - 1):
        x0, y0 = pts[i]
        x1, y1 = pts[i + 1]
        a += x0 * y1 - x1 * y0
        per += math.hypot(x1 - x0, y1 - y0)
    return abs(a) * 0.5, per


def candidates(src_rings, dst_rings, tol=0.05, keep=3):
    """Plausible partners for each lot, by the SHAPE of it - area and perimeter, which are
    invariant under rotation and translation and so compare with no transform at all.

    Deliberately loose and deliberately ambiguous: a shortlist, not an answer. An earlier
    version tried to decide here, accepting a match whenever the runner-up was 8% worse, and
    that tested only whether a match was DISTINCTIVE - never whether it was GOOD. A farm
    parcel with a unique area happily paired with a village ring 300% off, and the fit came
    out at 148 degrees. RANSAC below does the deciding, against evidence.
    """
    import numpy as np

    S = np.log(np.maximum(np.array([shape_sig(r) for r in src_rings]), 1e-6))
    D = np.log(np.maximum(np.array([shape_sig(r) for r in dst_rings]), 1e-6))
    d2 = ((S[:, None, :] - D[None, :, :]) ** 2).sum(axis=2)

    out = []
    for i in range(len(S)):
        for j in np.argsort(d2[i])[:keep]:
            if math.sqrt(d2[i, j]) < tol:      # absolute agreement, not just distinctiveness
                out.append((i, int(j)))
    return out


def ransac(src, dst, cands, iters=4000, tol=6.0, seed=20260805):
    """Find the transform that the most candidate pairs agree on.

    Two correspondences fix a similarity, so: sample two, solve, and count how many of the
    other candidates land within `tol` metres of their proposed partner. A wrong pair proposes
    a transform nothing else agrees with; the right one is voted for by hundreds of lots.
    """
    import numpy as np
    rng = random.Random(seed)

    SRC = np.array(src)
    DST = np.array(dst)
    ci = np.array([i for i, _ in cands])
    cj = np.array([j for _, j in cands])

    best = (0, None)
    for _ in range(iters):
        p, q = rng.randrange(len(cands)), rng.randrange(len(cands))
        if p == q:
            continue
        # Two candidates sharing a source or sitting on top of each other fix nothing, and a
        # near-coincident pair fixes the scale so badly it can collapse: the first run of this
        # returned scale 0, which maps every lot onto one point and scored 170 spurious hits.
        (i1, j1), (i2, j2) = cands[p], cands[q]
        if i1 == i2 or j1 == j2:
            continue
        if math.dist(src[i1], src[i2]) < 1e-4 or math.dist(dst[j1], dst[j2]) < 200.0:
            continue
        try:
            t = fit([src[i1], src[i2]], [dst[j1], dst[j2]])
        except ZeroDivisionError:
            continue
        if not (t[5] > 1e-9):                  # scale must be positive and non-degenerate
            continue
        a, b, tx, ty = t[0], t[1], t[2], t[3]
        moved = np.column_stack([a * SRC[ci, 0] - b * SRC[ci, 1] + tx,
                                 b * SRC[ci, 0] + a * SRC[ci, 1] + ty])
        hit = int((((moved - DST[cj]) ** 2).sum(axis=1) < tol ** 2).sum())
        if hit > best[0]:
            best = (hit, t)

    if best[1] is None:
        return None
    # Refit on everything that agreed, rather than on the two that proposed it.
    a, b, tx, ty = best[1][0], best[1][1], best[1][2], best[1][3]
    moved = np.column_stack([a * SRC[ci, 0] - b * SRC[ci, 1] + tx,
                             b * SRC[ci, 0] + a * SRC[ci, 1] + ty])
    ok = ((moved - DST[cj]) ** 2).sum(axis=1) < tol ** 2
    inliers = [(int(ci[m]), int(cj[m])) for m in range(len(ok)) if ok[m]]
    return fit([src[i] for i, _ in inliers], [dst[j] for _, j in inliers]), inliers


def refine(src, dst, pairs, t, tol):
    """Re-pair by position now that a transform exists, and refit. Tightens the signature
    match into a full one: every lot the signature had to discard as ambiguous is now
    unambiguous by where it lands."""
    import numpy as np

    D = np.array(dst)
    for _ in range(8):
        cur = np.array([apply(t, *p) for p in src])
        d2 = ((cur[:, None, :] - D[None, :, :]) ** 2).sum(axis=2)
        s2d = d2.argmin(axis=1)
        d2s = d2.argmin(axis=0)
        keep = [i for i in range(len(cur)) if d2s[s2d[i]] == i and d2[i, s2d[i]] < tol ** 2]
        if len(keep) < 50:
            return t, pairs
        t = fit([src[i] for i in keep], [dst[s2d[i]] for i in keep])
        pairs = [(i, int(s2d[i])) for i in keep]
    return t, pairs


def main():
    village = load_village()
    geo = load_geo()
    print(f"parcels.txt rings: {len(village)}   geojson features: {len(geo)}")

    # Longitude is scaled by cos(lat) about the town's own centre so the fit is against a
    # locally-conformal plane; anything left over is real error rather than the projection.
    lat0 = sum(ring_centroid(r)[1] for r in geo) / len(geo)
    lon0 = sum(ring_centroid(r)[0] for r in geo) / len(geo)
    k = math.cos(math.radians(lat0))
    print(f"town centre: {lat0:.6f}, {lon0:.6f}   cos(lat)={k:.6f}")

    # Rough metres for the SIGNATURE only - so a lot's area and perimeter are in the same
    # units on both sides. The real scale is solved for below and is not assumed here.
    M = 111320.0
    geo_m = [[((x - lon0) * k * M, (y - lat0) * M) for x, y in g] for g in geo]

    cands = candidates(geo_m, village)
    print(f"candidate pairs by shape: {len(cands)}")
    if len(cands) < 30:
        print("!! too few candidate shapes to fit from")
        return

    # Fit in DEGREES (lon already scaled by cos lat), so the transform this writes out is the
    # one a caller applies straight to a lat/lon.
    dst = [ring_centroid(v) for v in village]

    # TRY BOTH HANDEDNESSES. A rotation-and-scale fit cannot turn a mirror image into its
    # original, so if the village map's Y runs south-up against latitude's north-up, every
    # rotation is equally wrong and the solve collapses - which is exactly what happened.
    # Whichever way round the map is, one of these two agrees with hundreds of lots and the
    # other with almost none, so this decides it by counting rather than by assuming.
    best = None
    for flip in (False, True):
        sgn = -1.0 if flip else 1.0
        src = [((ring_centroid(g)[0] - lon0) * k, sgn * (ring_centroid(g)[1] - lat0)) for g in geo]
        got = ransac(src, dst, cands)
        hits = 0 if got is None else len(got[1])
        print(f"  y {'flipped' if flip else 'as-is '}: {hits} pairs agreed")
        if got is not None and (best is None or hits > best[0]):
            best = (hits, flip, src, got[0], got[1])

    if best is None:
        print("!! nothing agreed - the two files may not be the same town")
        return
    _, flip, src, t, inliers = best
    print(f"handedness: y is {'FLIPPED' if flip else 'as-is'} between lat/lon and village metres")
    t, pairs = refine(src, dst, inliers, t, tol=20.0)
    a, b, tx, ty, theta, scale = t
    print(f"matched after refine: {len(pairs)} of {len(geo)} features to {len(village)} rings")

    resid = []
    for gi, vi in pairs:
        px, py = apply(t, *src[gi])
        resid.append(math.hypot(px - dst[vi][0], py - dst[vi][1]))
    resid.sort()
    n = len(resid)

    print()
    print("--- fitted transform (degrees about town centre -> village metres) ---")
    print(f"  a = {a:.10f}   b = {b:.10f}")
    print(f"  tx = {tx:.6f}   ty = {ty:.6f}")
    print(f"  rotation = {math.degrees(theta):+.4f} deg")
    print(f"  scale    = {scale:.4f} m per degree (= {scale * k:.1f} m/deg lon, {scale:.1f} m/deg lat)")
    print()
    print("--- residual, metres ---")
    for label, idx in (("p50", n // 2), ("p90", int(n * 0.9)), ("p99", int(n * 0.99)), ("max", n - 1)):
        print(f"  {label}: {resid[idx]:7.3f}")
    print(f"  mean: {sum(resid) / n:7.3f}")

    with open(os.path.join(HERE, "rossville-transform.json"), "w", encoding="utf-8") as fh:
        json.dump({"lat0": lat0, "lon0": lon0, "k": k, "flip_y": flip,
                   "a": a, "b": b, "tx": tx, "ty": ty,
                   "rotation_deg": math.degrees(theta), "scale_m_per_deg": scale,
                   "residual_p50": resid[n // 2], "residual_max": resid[-1]}, fh, indent=2)
    print("\nwrote tools/rossville-transform.json")


if __name__ == "__main__":
    main()
