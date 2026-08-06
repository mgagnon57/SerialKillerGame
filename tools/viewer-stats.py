"""Every number the browser view puts on screen, computed from the data it is showing.

WHY THIS EXISTS. The page is being used to VALIDATE the data, so a figure on it that was typed
by hand is worse than no figure at all: it looks like a measurement and it goes stale silently.
The first version had "96.9% agree with the county's address" and "the median house centre
stands 53 ft in" written into the HTML, both true when they were measured and both wrong within
the hour - the registration fix moved every footprint 17 ft and took the count from 808 to 822,
and no hand-typed number followed it.

So nothing here is typed. Everything the rail displays is computed off the same arrays the map
draws, at build time, and injected. If the data changes and this is not re-run, the counts stop
matching the map and that is visible immediately.
"""
import json, math, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
FT = 3.28084
M2_FT2 = 10.7639


# ---- geometry ------------------------------------------------------------------------------

def ring_area(r):
    a = 0.0
    P = r[:-1] if tuple(r[0]) == tuple(r[-1]) else r
    n = len(P)
    for i in range(n):
        x0, y0 = P[i]
        x1, y1 = P[(i + 1) % n]
        a += x0 * y1 - x1 * y0
    return abs(a) * 0.5


def centroid(r):
    """AREA centroid, the same definition the seating pipeline uses.

    Not the mean of the vertices. They differ for any shape that is not symmetric, and the first
    version of this file used the vertex mean and reported 99.1% of footprints inside their own
    lot where seat-buildings.py reported 100% - the same question, two answers, from two
    definitions of "the middle of a building". A page whose job is validation cannot disagree
    with the pipeline it is validating.
    """
    P = r[:-1] if tuple(r[0]) == tuple(r[-1]) else r
    n = len(P)
    ox, oy = P[0]
    a = cx = cy = 0.0
    for i in range(n):
        x0, y0 = P[i][0] - ox, P[i][1] - oy
        x1, y1 = P[(i + 1) % n][0] - ox, P[(i + 1) % n][1] - oy
        cross = x0 * y1 - x1 * y0
        a += cross
        cx += (x0 + x1) * cross
        cy += (y0 + y1) * cross
    if abs(a) < 1e-12:
        return sum(p[0] for p in P) / n, sum(p[1] for p in P) / n
    a *= 0.5
    return cx / (6 * a) + ox, cy / (6 * a) + oy


def inside(pt, ring):
    x, y = pt
    hit = False
    for i in range(len(ring) - 1):
        x0, y0 = ring[i]
        x1, y1 = ring[i + 1]
        if (y0 > y) != (y1 > y):
            if x < x0 + (y - y0) / (y1 - y0) * (x1 - x0):
                hit = not hit
    return hit


def minrect(pts):
    """(angle, long side, short side) of the minimum-area box."""
    P = pts[:-1] if tuple(pts[0]) == tuple(pts[-1]) else pts
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
            cu = (max(us) + min(us)) / 2
            cv = (max(vs) + min(vs)) / 2
            best = (w * h, (ux, uy), w, h, (cu * ux - cv * uy, cu * uy + cv * ux))
    _, u, w, h, c = best
    if h > w:
        u = (-u[1], u[0])
        w, h = h, w
    return u, w, h, c


def pct(vals, q):
    v = sorted(vals)
    return v[min(len(v) - 1, int(len(v) * q))] if v else 0.0


# ---- address normalisation ----------------------------------------------------------------

SUF = {"STREET", "ST", "AVENUE", "AVE", "ROAD", "RD", "DRIVE", "DR", "LANE", "LN",
       "COURT", "CT", "PLACE", "PL", "BOULEVARD", "BLVD", "WAY", "CIRCLE", "CIR"}
DIRW = {"N", "S", "E", "W", "NORTH", "SOUTH", "EAST", "WEST"}


def norm_addr(a):
    if not a:
        return None
    toks = re.sub(r"[^A-Z0-9 ]", " ", a.upper()).split()
    if not toks:
        return None
    num = toks[0] if toks[0].isdigit() else None
    rest = [t for t in toks[1:] if t not in SUF and t not in DIRW]
    return (num, rest[0] if rest else None)


# ---- the numbers ---------------------------------------------------------------------------

def compute(parcels, county, buildings, roads, newroads):
    s = {}

    s["parcels"] = len(parcels)
    s["buildings"] = len(buildings)
    s["primary"] = sum(1 for b in buildings if b["role"] == "primary")
    s["outbuildings"] = sum(1 for b in buildings if b["role"] == "outbuilding")

    per = {}
    for b in buildings:
        per.setdefault(b["p"], []).append(b)
    s["lots_built"] = len(per)

    # Does each footprint sit inside the lot it was assigned to?
    inside_n = 0
    for b in buildings:
        ring = parcels[b["p"]]
        if inside(centroid(b["r"]), ring):
            inside_n += 1
    s["inside_own_lot"] = inside_n
    s["inside_own_lot_pct"] = round(100.0 * inside_n / max(1, len(buildings)), 1)

    # Does the federal address agree with the county's, on the same lot?
    agree = differ = 0
    for b in buildings:
        c = county.get(str(b["p"])) or county.get(b["p"])
        ca = c.get("addr") if c else None
        if not ca or not b["addr"]:
            continue
        if norm_addr(ca) == norm_addr(b["addr"]):
            agree += 1
        else:
            differ += 1
    s["addr_agree"] = agree
    s["addr_differ"] = differ
    s["addr_pct"] = round(100.0 * agree / max(1, agree + differ), 1)

    # Do the two sources agree that a lot is built on at all?
    booked = set()
    for k, c in county.items():
        if (c.get("dwell") or 0) > 0:
            booked.add(int(k))
    found = set(per)
    s["county_books"] = len(booked)
    s["imagery_finds"] = len(found)
    s["both_agree"] = len(booked & found)
    s["county_only"] = len(booked - found)
    s["imagery_only"] = len(found - booked)

    # Where on its lot does the house sit? Town residential lots only.
    fracs, depths, fronts = [], [], []
    for b in buildings:
        if b["role"] != "primary" or b["occ"] != "Residential":
            continue
        lot = parcels[b["p"]]
        u, depth, front, c = minrect(lot)
        if depth < 20 or depth > 120 or front < 8:
            continue
        bc = centroid(b["r"])
        t = ((bc[0] - c[0]) * u[0] + (bc[1] - c[1]) * u[1]) / depth + 0.5
        fracs.append(min(t, 1 - t))
        depths.append(depth)
        fronts.append(front)
    if fracs:
        med_frac = pct(fracs, 0.5)
        med_depth = pct(depths, 0.5)
        s["setback_lots"] = len(fracs)
        s["lot_depth_ft"] = round(med_depth * FT)
        s["lot_front_ft"] = round(pct(fronts, 0.5) * FT)
        s["setback_ft"] = round(med_frac * med_depth * FT)
        s["setback_frac"] = round(med_frac, 2)
        s["front_third_pct"] = round(100.0 * sum(1 for f in fracs if f < 1 / 3) / len(fracs))

    # Is the second structure behind the first? Measured AGAINST THE STREET.
    #
    # Not against the lot's own axis, which is what this did first and is very nearly circular:
    # "further from whichever end the house is nearest" is true of almost anything else on the
    # lot, and it duly reported 95% where the street-based measurement in BUILDING-FOOTPRINTS.md
    # reports 74%. A validation page that quietly disagrees with its own research is worse than
    # one that shows nothing. The street is an outside reference and the alleys are excluded
    # from it deliberately - an alley runs along the BACK lot line, right past the outbuildings,
    # so including it would call the same structure both behind the house and next to the road.
    streets = [pl for rd in (newroads or []) if rd.get("c") != "alley" for pl in rd["lines"]]
    if not streets:
        streets = [rd["pts"] for rd in roads if not rd["n"].startswith("alley")]

    def seg_d(p, a, b):
        ax, ay = a
        bx, by = b
        dx, dy = bx - ax, by - ay
        L = dx * dx + dy * dy
        t = 0.0 if L == 0 else max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - ay) * dy) / L))
        return math.hypot(p[0] - (ax + t * dx), p[1] - (ay + t * dy))

    def street_dist(p):
        best = 1e18
        for pl in streets:
            for i in range(len(pl) - 1):
                d = seg_d(p, pl[i], pl[i + 1])
                if d < best:
                    best = d
        return best

    behind, dists, multi = 0, [], 0
    for pid, bl in per.items():
        if len(bl) < 2:
            continue
        u, depth, front, c = minrect(parcels[pid])
        if depth < 20 or depth > 120:
            continue
        multi += 1
        prim = min(bl, key=lambda b: b["role"] != "primary")
        dp = street_dist(centroid(prim["r"]))
        for b in bl:
            if b is prim:
                continue
            d = street_dist(centroid(b["r"])) - dp
            dists.append(d)
            if d > 0:
                behind += 1
    if dists:
        s["multi_lots"] = multi
        s["behind_pct"] = round(100.0 * behind / len(dists))
        back = [d for d in dists if d > 0]
        s["behind_ft"] = round(pct(back, 0.5) * FT) if back else 0

    # Footprint sizes.
    sf = sorted(b["sf"] for b in buildings
                if b["role"] == "primary" and b["occ"] == "Residential" and b["sf"])
    if sf:
        s["house_sf_p10"] = sf[len(sf) // 10]
        s["house_sf_med"] = sf[len(sf) // 2]
        s["house_sf_p90"] = sf[int(len(sf) * 0.9)]

    s["skew_med"] = round(pct([abs(b["skew"]) for b in buildings if b["role"] == "primary"], 0.5), 1)
    s["skew_p90"] = round(pct([abs(b["skew"]) for b in buildings if b["role"] == "primary"], 0.9), 1)

    s["roads_old"] = len(roads or [])
    s["roads_new"] = len(newroads)
    s["alleys_new"] = sum(1 for r in newroads if r.get("c") == "alley")
    s["streets_new"] = len(newroads) - s["alleys_new"]

    return s


def disagreements(parcels, county, buildings):
    """Where the tax roll and the imagery differ about a lot - AND WHY, which turns out to be
    the whole of it. Most of the 103 differences are not disagreements at all.

    Four causes, and they do not have the same answer about who to believe:

      exempt    the imagery finds a building and the county books no DWELLING - because it is a
                church, a school, the fire station. A tax roll records taxable dwellings; a
                tax-exempt building is correctly absent from one and correctly present on the
                ground. Both sources are right and the first version of this comparison was
                wrong to call it a conflict.
      merged    the county books a dwelling, the imagery finds nothing, and there is a footprint
                on a lot within 35 m. This is the downtown terrace: FEMA traces a continuous run
                of brick as ONE polygon, which lands on one parcel and leaves its neighbours
                looking empty. 15 of the 18 commercial cases are like this, a median 99 m from
                the crossing. The county is right and the imagery is under-counting.
      vanished  the county books a dwelling, the imagery finds nothing, and nothing stands
                nearby either. Scattered, a median 405 m out, median assessed value $7,811 - the
                cheapest housing in town. Demolitions the roll has not caught up with, most
                likely, or something small under a tree.
      untaxed   a real building on a lot the county calls vacant land. The genuine conflict, and
                the smallest group.
    """
    per = {}
    for b in buildings:
        per.setdefault(b["p"], []).append(b)

    def cls(i):
        c = county.get(str(i)) or county.get(i) or {}
        return c.get("clsname", ""), (c.get("dwell") or 0)

    cent = {}
    for i, r in enumerate(parcels):
        cent[i] = centroid(r)

    built = set(per)
    out = {}
    for i in range(len(parcels)):
        name, dwell = cls(i)
        has = i in built
        if has and dwell <= 0:
            out[i] = "exempt" if "Exempt" in name else "untaxed"
        elif dwell > 0 and not has:
            near = any(math.dist(cent[i], cent[j]) < 35.0 for j in built)
            out[i] = "merged" if near else "vanished"
    return out


def road_scores(parcels, roads, newroads):
    """Share of each network's centreline running across private land, and per road.

    Recomputed here rather than quoted from build-roads.py, so the figure on the page is a
    property of what the page is drawing.
    """
    CELL = 40.0
    box, grid = [], {}
    for i, r in enumerate(parcels):
        xs = [p[0] for p in r]
        ys = [p[1] for p in r]
        b = (min(xs), min(ys), max(xs), max(ys))
        box.append(b)
        for gx in range(int(b[0] // CELL), int(b[2] // CELL) + 1):
            for gy in range(int(b[1] // CELL), int(b[3] // CELL) + 1):
                grid.setdefault((gx, gy), []).append(i)

    def hit(pt):
        for i in grid.get((int(pt[0] // CELL), int(pt[1] // CELL)), ()):
            b = box[i]
            if b[0] <= pt[0] <= b[2] and b[1] <= pt[1] <= b[3] and inside(pt, parcels[i]):
                return True
        return False

    def town(pt):
        near = []
        g = (int(pt[0] // CELL), int(pt[1] // CELL))
        for dx in (-2, -1, 0, 1, 2):
            for dy in (-2, -1, 0, 1, 2):
                near.extend(grid.get((g[0] + dx, g[1] + dy), ()))
        if not near:
            return False
        areas = sorted((box[i][2] - box[i][0]) * (box[i][3] - box[i][1]) for i in set(near))
        return areas[len(areas) // 2] < 6000.0

    def walk(pl, step=4.0):
        out = []
        for i in range(len(pl) - 1):
            (x0, y0), (x1, y1) = pl[i], pl[i + 1]
            d = math.hypot(x1 - x0, y1 - y0)
            n = max(1, int(d / step))
            for j in range(n):
                t = j / n
                out.append((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t))
        out.append(tuple(pl[-1]))
        return out

    def score(lines):
        ins = tot = 0
        for pl in lines:
            for p in walk(pl):
                if not town(p):
                    continue
                tot += 1
                if hit(p):
                    ins += 1
        return ins, tot

    oi = ot = 0
    for rd in (roads or []):
        i, t = score([rd["pts"]])
        oi += i
        ot += t

    ni = nt = 0
    per_road = {}
    for rd in newroads:
        i, t = score(rd["lines"])
        ni += i
        nt += t
        if t:
            per_road[rd["n"]] = round(100.0 * i / t, 1)

    return {
        "old_pct": round(100.0 * oi / ot, 1) if ot else None,
        "new_pct": round(100.0 * ni / max(1, nt), 1),
        "per_road": per_road,
    }
