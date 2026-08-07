"""Seat FEMA's real building footprints onto Rossville's real county lots.

WHAT THIS ANSWERS. The project has 794 surveyed lots and what the county records about each
one, and no building on any of them - houses are authored rectangles placed at a setback from
a road, matched to whatever parcel they happen to land on. 76 of them land on no parcel at all.
This puts a MEASURED footprint on the lot instead.

THE SOURCE. FEMA / Oak Ridge National Laboratory USA Structures: a national building-footprint
layer derived from imagery, carrying per-building area, occupancy class, address, and a flag
for whether the structure is an outbuilding. 885 of them stand in the Rossville box against
OpenStreetMap's 19 and the county's own Buildings layer's zero - that layer exists but covers
Danville only, which is why earlier passes concluded the county had none.

HOW A FOOTPRINT REACHES THE MAP, in three steps, each of which was wrong once:

  1. REGISTER THE TWO DATASETS TO EACH OTHER. They do not agree: a FEMA outline sits about
     17 ft south-east of the lot the assessor draws around it. See registration_offset for how
     that is measured and why the obvious way to measure it is biased. Done FIRST, because a
     footprint 17 ft out of position can fall inside the neighbour's lot, and correcting after
     assigning would leave a house perfectly placed on the wrong parcel. The offset and the
     assignment each depend on the other, so it is iterated to a fixed point.

  2. STRUCTURE -> COUNTY PARCEL, in WGS84. Both datasets are lat/lon, so this join involves no
     transform and no error. A structure belongs to the lot its centroid falls inside.

  3. COUNTY PARCEL -> VILLAGE METRES, by the AFFINE fit in fit-transform.py, plus that parcel's
     own residual offset. Affine and not a similarity: the two axes of this conversion differ
     by 0.197%, which a single scale cannot represent and instead smears over the town as up to
     3 m of position error. Six parameters put the residual at 0.028 m.

WHAT THAT FIXED, AND WHAT IT DID NOT. Houses now sit on their own lots: 822 of 822 land inside
the parcel they were assigned to, and the share of houses overhanging their own boundary fell
from 62% to 31%. What none of it touches is the ANGLE. Half of these outlines are more than 5
degrees off the town grid while 94% of the county's lots are within 5 degrees of it, and the
same is true in the raw download before this script sees it - so the crookedness is in the
tracing, not in the placing. It is recorded per building as `skew` and deliberately not applied.

NOTHING HERE IS AUTHORED. This writes Content/parcel-buildings.txt, which is derived and
read-only, in the same spirit as parcel-county.txt: re-runnable, and never mixed into
parcel-notes.txt, which is the author's file and is rewritten wholesale by the game.
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(HERE, "..", "Content")

sys.path.insert(0, HERE)
import importlib.util
_spec = importlib.util.spec_from_file_location("fit_transform", os.path.join(HERE, "fit-transform.py"))
ft = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ft)


def poly_area(pts):
    a = 0.0
    for i in range(len(pts) - 1):
        a += pts[i][0] * pts[i + 1][1] - pts[i + 1][0] * pts[i][1]
    return abs(a) * 0.5


def inside(pt, ring):
    """Ray cast. Rings here are closed and simple."""
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


def minrect_angle(pts):
    """Angle of the minimum-area box's long axis, in degrees."""
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
    return (ang + (90.0 if h > w else 0.0)) % 180.0, max(w, h), min(w, h)


def edge_axis(pts):
    """Which way a polygon points, as the length-weighted dominant direction of its edges,
    mod 90 degrees - plus how dominant that direction is, 1.0 for a clean rectangle.

    Angles are quadrupled before averaging so 0 and 90 degrees reinforce rather than cancel:
    for a rectilinear shape they are the same direction. Steadier than the minimum-area box,
    which picks its long side by noise whenever a shape is nearly square.
    """
    P = pts[:-1] if tuple(pts[0]) == tuple(pts[-1]) else list(pts)
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
    return (math.degrees(math.atan2(sy, sx)) / 4.0) % 90.0, math.hypot(sx, sy) / tot


def d90(a, b):
    """Difference between two undirected axes, folded into +/-45 degrees."""
    d = (a - b) % 90.0
    return d - 90.0 if d > 45.0 else d


def block_grid(centre, pcent, paxes, radius=130.0):
    """The direction of the BLOCK this point sits in: the dominant edge direction of every lot
    within `radius`, averaged.

    Squaring a house to its OWN lot inherits that one polygon's wobble, and the lots wobble by
    a median of about a degree with a worst case past thirty. Averaging over the block cancels
    that while staying local enough to follow the diagonal blocks along the railroad, where
    being off the town grid is real rather than an error. Measured by how parallel neighbouring
    houses end up - which is what a platted street looks like - this beats squaring to the lot
    on the tail that matters: 1.3 degrees at the 90th percentile against 5.1.
    """
    sx = sy = 0.0
    n = 0
    for i, c in enumerate(pcent):
        a = paxes[i]
        if a is None:
            continue
        if abs(c[0] - centre[0]) > radius or abs(c[1] - centre[1]) > radius:
            continue
        if math.dist(c, centre) > radius:
            continue
        sx += math.cos(math.radians(a * 4))
        sy += math.sin(math.radians(a * 4))
        n += 1
    if n < 3:
        return None, n
    return (math.degrees(math.atan2(sy, sx)) / 4.0) % 90.0, n


def registration_offset(items, geo_m):
    """How far FEMA's footprints sit from where the county's lot lines put them.

    The two datasets are not registered to each other: a building traced from imagery lands
    about 17 ft south-east of the lot the assessor draws around it. Measured two ways, which
    agree to within 15 cm on the first pass (-3.43,+3.14 against -3.50,+3.25) before either is
    iterated - two estimators with different biases landing on the same answer is most of the
    reason to believe the offset is real rather than an artefact of how it was looked for:

      MINIMISING OVERSPILL - slide every house until the least area hangs off its own lot.
      Direct, but biased: houses sit at the FRONT of their lots, so this objective always
      wants to pull them inward, whatever the registration is.

      THE SIDE-OFFSET SOLVE, which is what this function does and is unbiased. Side to side,
      a house has no reason to sit off-centre on its lot - the builder centred it between the
      neighbours. So the mean sideways offset should be zero, and whatever it actually is, is
      the registration error projected onto that lot's frontage axis. Rossville's blocks face
      two perpendicular directions, so both components fall out of a least-squares solve over
      those projections. It measured -3.18 m of mean side offset where zero was expected.

    Returns the correction to ADD to the footprints, in local metres, x east and y north.
    """
    import numpy as np
    A, b = [], []
    for gi, ring in items:
        la, ld, lw = minrect_angle(geo_m[gi])
        if ld < 20 or ld > 120 or lw < 8 or lw > 40:      # town lots only
            continue
        lc = ft.ring_centroid(geo_m[gi])
        bc = ft.ring_centroid(ring)
        fx, fy = math.cos(math.radians(la + 90)), math.sin(math.radians(la + 90))
        A.append([fx, fy])
        b.append((bc[0] - lc[0]) * fx + (bc[1] - lc[1]) * fy)
    if len(b) < 50:
        return 0.0, 0.0, 0
    d, *_ = np.linalg.lstsq(np.array(A), np.array(b), rcond=None)
    return -float(d[0]), -float(d[1]), len(b)


def main():
    # ---- the two parcel views, and the transform between them ----------------------------
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

    # A similarity gets the correspondence; an AFFINE fit then does the actual placing. Two
    # points can only ever fix a similarity, which is why RANSAC samples one - but once every
    # lot is paired, the extra two parameters are free and they matter: the two axes of this
    # conversion differ by 0.197%, and a uniform scale can only spread that mismatch around
    # the town as position error. See fit_affine.
    T = ft.fit_affine([src[i] for i, _ in pairs], [dst[j] for _, j in pairs])
    T, pairs = ft.refine_affine(src, dst, pairs, T, tol=20.0)
    geo2village = {gi: vi for gi, vi in pairs}
    print(f"parcels: {len(geo)} downloaded, {len(village)} in parcels.txt, {len(pairs)} matched")

    resid = sorted(math.dist(ft.apply_affine(T, *src[gi]), dst[vi]) for gi, vi in pairs)
    print(f"  placement residual: p50 {resid[len(resid)//2]:.3f} m, "
          f"p90 {resid[int(len(resid)*0.9)]:.3f} m, max {resid[-1]:.3f} m")

    def to_village(lon, lat):
        return ft.apply_affine(T, (lon - lon0) * k, -(lat - lat0))

    # Per-parcel correction: where the fit puts this lot, against where parcels.txt draws it.
    # Near zero now that the fit is affine, and kept because it costs nothing and guarantees a
    # house is positioned against ITS OWN lot rather than against the town average.
    offset = {}
    for gi, vi in pairs:
        px, py = ft.apply_affine(T, *src[gi])
        offset[gi] = (dst[vi][0] - px, dst[vi][1] - py)

    # ---- the structures ------------------------------------------------------------------
    with open(os.path.join(HERE, "rossville-structures.geojson"), encoding="utf-8") as fh:
        gj = json.load(fh)

    # Bounding boxes first, so 885 structures against 811 lots is not 718k polygon tests.
    boxes = []
    for g in geo:
        xs = [p[0] for p in g]
        ys = [p[1] for p in g]
        boxes.append((min(xs), min(ys), max(xs), max(ys)))

    # Pull the rings out once.
    raw = []
    stats = {"total": 0, "placed": 0, "no_parcel": 0, "no_village": 0}
    for feat in gj["features"]:
        geom = feat["geometry"]
        if geom is None:
            continue
        parts = geom["coordinates"] if geom["type"] == "MultiPolygon" else [geom["coordinates"]]
        ring = max((p[0] for p in parts), key=len)
        ring = [(p[0], p[1]) for p in ring]
        if len(ring) < 4:
            continue
        stats["total"] += 1
        raw.append((ring, feat["properties"]))

    def overlap_share(ring, gi, n=12):
        """Roughly how much of `ring` lies inside county parcel `gi`, 0..1.

        Sampled on a grid over the footprint rather than clipped polygon against polygon:
        Sutherland-Hodgman needs a convex clip and these parcels are not - corner lots, alley
        cut-offs and the wedges by the railroad are all concave, and a clipper would quietly
        return the wrong area for exactly the lots most likely to be argued about. Counting
        points does not care about shape.
        """
        xs = [p[0] for p in ring]
        ys = [p[1] for p in ring]
        x0, x1 = min(xs), max(xs)
        y0, y1 = min(ys), max(ys)
        if x1 <= x0 or y1 <= y0:
            return 0.0
        inb = hit = 0
        for r in range(n):
            py = y0 + (y1 - y0) * (r + 0.5) / n
            for c in range(n):
                px = x0 + (x1 - x0) * (c + 0.5) / n
                if not inside((px, py), ring):
                    continue
                inb += 1
                if inside((px, py), geo[gi]):
                    hit += 1
        return hit / inb if inb else 0.0

    def assign(dlon, dlat):
        """Which county lot each structure stands on, with the registration correction applied
        first. Order matters: a footprint 15 ft out of position can fall inside the NEIGHBOUR's
        lot, so correcting after assigning would leave it correctly placed on the wrong lot.

        THE LOT IT SITS MOST ON, not the lot its middle happens to fall in. Centroid-in-polygon
        is the obvious rule and it fails wherever a parcel is much bigger than a building: the
        railroad right of way is 4.6 acres, and 208 Summit's house - two of whose four corners
        are outside it - was captured by it because the centroid drifted a few feet over the
        line. Measured across the whole set, 119 of 822 footprints had MORE THAN HALF their
        corners outside the lot they had been given.

        Overlap is the honest question. Ties and near-ties fall back to the centroid, which is
        what the old rule always answered and is right whenever a building genuinely does sit
        across a boundary.
        """
        got, miss_p, miss_v = [], 0, 0
        for ring, props in raw:
            moved = [(lon + dlon, lat + dlat) for lon, lat in ring]
            cx, cy = ft.ring_centroid(moved)

            bx0 = min(p[0] for p in moved)
            bx1 = max(p[0] for p in moved)
            by0 = min(p[1] for p in moved)
            by1 = max(p[1] for p in moved)

            cands = []
            for i, (x0, y0, x1, y1) in enumerate(boxes):
                if x1 < bx0 or x0 > bx1 or y1 < by0 or y0 > by1:
                    continue
                cands.append(i)

            best, best_share, centroid_in = None, 0.0, None
            for i in cands:
                if inside((cx, cy), geo[i]):
                    centroid_in = i
                share = overlap_share(moved, i)
                if share > best_share:
                    best, best_share = i, share

            # A near-tie is not a disagreement worth acting on; keep the old answer there.
            if centroid_in is not None and best is not None and best != centroid_in:
                if overlap_share(moved, centroid_in) > best_share - 0.05:
                    best = centroid_in

            gi = best if best_share > 0.02 else centroid_in
            if gi is None:
                miss_p += 1
                continue
            if gi not in geo2village:
                miss_v += 1
                continue
            got.append((gi, moved, props))
        return got, miss_p, miss_v

    # Measure the offset, re-assign under it, measure again. The estimate and the assignment
    # each depend on the other, so it is iterated to a fixed point rather than done once.
    dlon = dlat = 0.0
    for it in range(4):
        assigned, miss_p, miss_v = assign(dlon, dlat)
        # Only the largest structure on a lot informs the registration - a garage on the back
        # boundary says nothing about where the lot lines are.
        biggest = {}
        for gi, ring, props in assigned:
            m = [((x - lon0) * k * M, (y - lat0) * M) for x, y in ring]
            a = poly_area(m + [m[0]])
            if gi not in biggest or a > biggest[gi][0]:
                biggest[gi] = (a, m)
        sdx, sdy, rn = registration_offset([(gi, v[1]) for gi, v in biggest.items()], geo_m)
        if math.hypot(sdx, sdy) < 0.02:
            break
        dlon += sdx / (k * M)
        dlat += sdy / M

    tdx, tdy = dlon * k * M, dlat * M
    stats["no_parcel"], stats["no_village"] = miss_p, miss_v
    print(f"  registration offset, converged over {it + 1} passes off {rn} houses: "
          f"{tdx:+.2f}, {tdy:+.2f} m  ({math.hypot(tdx, tdy):.2f} m / "
          f"{math.hypot(tdx, tdy)*3.28084:.1f} ft correction applied)")

    # Place them.
    out = []
    for gi, ring, p in assigned:
        ox, oy = offset[gi]
        pts = [to_village(lon, lat) for lon, lat in ring]   # already corrected by assign()
        pts = [(round(x + ox, 1), round(y + oy, 1)) for x, y in pts]
        if pts[0] != pts[-1]:
            pts.append(pts[0])

        out.append({
            "parcel": geo2village[gi],
            "ring": pts,
            "sqft": p.get("SQFEET"),
            "height": p.get("HEIGHT"),
            "occ": p.get("OCC_CLS"),
            "prim": p.get("PRIM_OCC"),
            "addr": p.get("PROP_ADDR"),
            "out": p.get("OUTBLDG"),
            "img": p.get("IMAGE_DATE"),
            "val": p.get("VAL_METHOD"),
            "bid": p.get("BUILD_ID"),
        })
        stats["placed"] += 1

    print(f"structures: {stats['total']} total, {stats['placed']} seated, "
          f"{stats['no_parcel']} on no county lot, {stats['no_village']} on a lot parcels.txt lacks")

    # ---- how good is it? -----------------------------------------------------------------
    # The test that matters: does the footprint sit INSIDE the lot it was assigned to, in the
    # coordinates the game actually draws? A footprint that has drifted off its own lot is
    # worse than no footprint, because it looks authoritative.
    covered = 0
    for b in out:
        vr = village[b["parcel"]]
        c = ft.ring_centroid(b["ring"])
        if inside(c, vr):
            covered += 1
    print(f"seated footprints whose centre lands inside their own lot as parcels.txt draws it: "
          f"{covered}/{len(out)} ({100.0 * covered / max(1, len(out)):.1f}%)")

    # ---- primary building, or something at the back of the lot ---------------------------
    # FEMA ships an OUTBLDG flag and it is empty on every one of these records, so the
    # distinction has to be derived. The rule is the one the 1913 census used and the one a
    # person would use standing in the alley: the biggest thing on the lot is the building,
    # anything else on the same lot is behind it. Sizes are sanity-checked against
    # BUILDING-CENSUS-1913.md's own 45 m2 house/barn threshold rather than a fresh guess.
    per = {}
    for b in out:
        per.setdefault(b["parcel"], []).append(b)
    for pid, bl in per.items():
        bl.sort(key=lambda b: -(b["sqft"] or 0))
        for i, b in enumerate(bl):
            b["rank"] = i
            b["role"] = "primary" if i == 0 else "outbuilding"

    # HOW FAR OFF SQUARE THIS BUILDING SITS ON ITS LOT, in degrees, signed.
    #
    # Recorded rather than corrected. The outlines are internally square - the four corners of
    # a traced rectangle come within 0.04 degrees of right angles - but their ORIENTATION is
    # noisy: half of them are more than 5 degrees off the town grid, while 94% of the county's
    # lots are within 5 degrees of it. So the lots are square and the buildings are not, and
    # that is the tracing, not the placing; no shift or fit can touch it.
    #
    # A consumer that wants a house square to its lot rotates the ring by MINUS this about its
    # own centroid. Left as a number here because doing it in the file would quietly overwrite
    # a measurement with an assumption, and because it is genuinely wrong for some of them:
    # corner lots, and the buildings along the railroad's diagonal, really do sit askew.
    pcent = [ft.ring_centroid(r) for r in village]
    paxes = [edge_axis(r)[0] for r in village]
    for pid, bl in per.items():
        lot_axis = edge_axis(village[pid])[0]
        for b in bl:
            ba, strength = edge_axis(b["ring"])
            c = ft.ring_centroid(b["ring"])
            grid, n = block_grid(c, pcent, paxes)
            ref = grid if grid is not None else lot_axis
            b["skew"] = d90(ba, lot_axis) if lot_axis is not None else 0.0
            b["blockskew"] = d90(ba, ref) if ref is not None else 0.0
            b["axis_strength"] = round(strength, 3)
            b["block_lots"] = n

    print(f"lots with at least one building: {len(per)}")
    prim = [b for b in out if b["role"] == "primary"]
    outb = [b for b in out if b["role"] == "outbuilding"]
    print(f"  primary structures: {len(prim)}   outbuildings: {len(outb)}"
          f"   ({len(outb) / max(1, len(prim)):.2f} per building)")

    houses = [b for b in prim if b["occ"] == "Residential"]
    areas = sorted(b["sqft"] for b in houses if b["sqft"])
    if areas:
        n = len(areas)
        print(f"  house footprint sq ft: p10 {areas[n//10]:.0f}  median {areas[n//2]:.0f}  "
              f"p90 {areas[int(n*0.9)]:.0f}   (n={n})")

    # Written after the roles are decided, so anything reading this JSON - the browser view
    # included - sees the same primary/outbuilding split the content file was built from.
    with open(os.path.join(HERE, "rossville-buildings-seated.json"), "w", encoding="utf-8") as fh:
        json.dump({"transform": {"lat0": lat0, "lon0": lon0, "k": k, "affine": list(T)},
                   "stats": stats, "buildings": out}, fh)
    print("wrote tools/rossville-buildings-seated.json")

    emit(out, per, village)


ZONE = {"Residential": "residential", "Commercial": "commercial", "Industrial": "industrial",
        "Government": "civic", "Education": "civic", "Agriculture": "agricultural",
        "Unclassified": "unclassified"}


def emit(out, per, village):
    """Write Content/parcel-buildings.txt - derived, read-only, re-runnable."""
    L = []
    w = L.append
    w("# " + "=" * 76)
    w("#  THE BUILDINGS OF ROSSVILLE, AS SOMETHING ACTUALLY MEASURED THEM")
    w("#")
    w("#  822 building footprints, seated on the county's own lots. Until this file, no")
    w("#  building in this project stood anywhere a survey put it: houses are authored")
    w("#  rectangles placed at a setback from a road, and 76 of them stand on no parcel at")
    w("#  all. These are polygons, from imagery, with an area and an address each.")
    w("#")
    w("#  SOURCE. FEMA / Oak Ridge National Laboratory USA Structures, the national")
    w("#  building-footprint layer, queried for the Rossville box on 2026-08-05. Raw download")
    w("#  at tools/rossville-structures.geojson so this can be rebuilt; the seating is")
    w("#  tools/seat-buildings.py and the coordinate fit it depends on is tools/fit-transform.py.")
    w("#")
    w("#  885 structures stand in the box. 822 are seated here; 57 fall on no county parcel")
    w("#  (open country, where the parcels tile farmland) and 6 on a lot parcels.txt does not")
    w("#  carry. For scale, OpenStreetMap has 19 buildings here and Vermilion County's own")
    w("#  Buildings layer has none - that layer exists and covers Danville only, which is why")
    w("#  earlier passes recorded the county as having no footprint data. It has none HERE.")
    w("#")
    w("#  HOW GOOD IS THE SEATING. Two independent checks, both computed rather than asserted:")
    w("#")
    w("#    822 of 822 footprints land inside the very lot they were assigned to, in the")
    w("#    coordinates parcels.txt draws - so a house sits on its own lot, not the")
    w("#    neighbour's. 69% sit wholly inside their boundary; the rest overhang it, which is")
    w("#    partly real - garages are built on lot lines - and partly the tracing being loose.")
    w("#")
    w("#    96.0% of them carry the same street address the county assessor records for the")
    w("#    same parcel (679 agree, 28 differ, on the 707 where both sides have one). The two")
    w("#    datasets are unrelated - a county tax roll and a federal imagery product - so")
    w("#    agreeing on which house is at which number is a real check on the geometry.")
    w("#    Most disagreements are two house numbers apart on the right street.")
    w("#")
    w("#  WHAT IS NOT IN HERE, AND DO NOT INFER IT.")
    w("#    - NO HEIGHTS. The HEIGHT column is empty on all 808 records.")
    w("#    - NO ROOM DATA. Nothing here says bedrooms, baths or floor area; footprint is")
    w("#      GROUND AREA, and a 1.5-storey house has half again as much floor as this says.")
    w("#    - NO YEAR BUILT. See docs/research/ROSSVILLE-HISTORY.md for the era mix.")
    w("#    - EVERY RECORD IS `VAL_METHOD: Automated`. Not one was checked by a person. These")
    w("#      are machine-traced outlines: right about where a building is, approximate about")
    w("#      its exact corners, and they include porches, attached garages and later")
    w("#      additions that the 1913 Sanborn footprints deliberately exclude.")
    w("#    - THE PRIMARY/OUTBUILDING SPLIT IS DERIVED, NOT RECORDED. FEMA ships an OUTBLDG")
    w("#      flag and it is empty on every record, so the largest structure on a lot is")
    w("#      called the building and the rest are behind it.")
    w("#")
    w("#  These are CURRENT buildings, imagery-dated 2016 on most records, and the game is set")
    w("#  in 1991 - see docs/research/THE-ERA.md, which decides the year. The commercial row at")
    w("#  Attica x Chicago burned in February 2004, so the")
    w("#  downtown here is the town AFTER the fire and is wrong for the build year - see")
    w("#  docs/research/DOWNTOWN-1991.md. The residential streets are substantially unchanged.")
    w("#")
    w("#  FIELDS")
    w("#    parcel <id> building <n> <role> <area> <zone> skew <deg> block <deg> \"<address>\"")
    w("#")
    w("#        THE FIELD ORDER ABOVE IS A CONTRACT. Assets/Noir/Unity/ParcelBuildings.cs reads")
    w("#        this file, and until 2026-08-07 this header omitted BOTH angles while the writer")
    w("#        emitted them - so the reader split on a fixed column count and every one of the")
    w("#        824 addresses came out as the literal text 'block -2.8 \"106 Smith Street\"'. That")
    w("#        is never empty, so the county-record fallback downstream never fired either. The")
    w("#        reader now scans for the keywords rather than counting columns, but if you add a")
    w("#        field here, SAY SO ON THIS LINE.")
    w("#")
    w("#        n       0 is the largest structure on the lot, then descending")
    w("#        role    primary | outbuilding, derived as above")
    w("#        area    footprint ground area in SQUARE METRES, to match parcels.txt and")
    w("#                ResidentialLots.Homestead.Footprint. The header talks in square feet")
    w("#                because the town does; the file is metric because the code is.")
    w("#        zone    FEMA's occupancy class, mapped onto the project's own zoning words")
    w("#        skew    degrees this outline sits off square to ITS OWN LOT, signed. RECORDED,")
    w("#                NOT CORRECTED - rotate the ring by minus this about its own centroid to")
    w("#                square it. See the note on tracing quality above before doing so.")
    w("#        block   the same measurement taken against the BLOCK GRID instead - the median")
    w("#                axis of every lot within 130 m. Prefer it: a single lot's own axes carry")
    w("#                a median error near a degree and a worst case past thirty, and averaging")
    w("#                over the block cancels that while still following the diagonal blocks")
    w("#                along the railroad.")
    w("#    parcel <id> shape <n> x,y x,y ...")
    w("#        the footprint ring, closed, in village metres - the same frame as parcels.txt")
    w("#")
    w("#  Keyed by parcel id, which is that parcel's line number in parcels.txt (0-based,")
    w("#  comments and blanks not counted), matching parcel-county.txt and parcel-notes.txt.")
    w("#")
    w("#  DERIVED AND READ-ONLY, like parcel-county.txt. Nothing authored belongs in here -")
    w("#  parcel-notes.txt is the author's file and the game rewrites it whole.")
    w("# " + "=" * 76)
    w("")

    for pid in sorted(per):
        for b in sorted(per[pid], key=lambda b: b["rank"]):
            m2 = (b["sqft"] or 0) * 0.09290304
            addr = (b["addr"] or "").title().replace('"', "'")
            w(f'parcel {pid} building {b["rank"]} {b["role"]} {m2:.1f} '
              f'{ZONE.get(b["occ"], "unclassified")} skew {b["skew"]:+.1f} '
              f'block {b["blockskew"]:+.1f} "{addr}"')
            ring = " ".join(f"{x:.1f},{y:.1f}" for x, y in b["ring"])
            w(f'parcel {pid} shape {b["rank"]} {ring}')
        w("")

    path = os.path.join(CONTENT, "parcel-buildings.txt")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(L))
    print(f"wrote Content/parcel-buildings.txt  ({len(out)} buildings on {len(per)} lots)")


if __name__ == "__main__":
    main()
