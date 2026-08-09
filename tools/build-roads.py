"""Rebuild Rossville's road network from survey sources instead of from straight lines.

WHAT WAS THERE. 32 of the 37 road lines in city.txt are TWO POINTS - a straight line ruled
from one edge of the town to the other. `road attica 30 0,1353 2099,1353` is the whole of
Attica Street. They were then bulk-shifted onto their rights of way in the 2026-08-04 refit,
which is the best a rigid shift can do and cannot help a road whose SHAPE is wrong: Harrison
turns 15.4 degrees at Benton, and no amount of sliding a straight line fixes that.

WHAT REPLACES IT, and where each piece comes from:

  NAMED STREETS - Vermilion County's own street centreline layer, 146 segments over Rossville,
  found on the same ArcGIS server that supplied the parcels and the property records. It
  carries address ranges, ZIP, speed limits and the street's own name, and it is real surveyed
  geometry rather than a ruled line. Measured against the parcel gaps it runs on private land
  2.2% of the time where city.txt manages 9.0%.

  ALLEYS - derived from the parcels themselves, because no county maps its alleys and OSM has
  them no better. See derive-alleys.py: the right of way is the hole the lots leave, so the
  alley is already in the survey as an absence. 0.5% on private land against city.txt's 13.4%.

  CHICAGO STREET - LEFT EXACTLY AS IT IS, and this is not a technicality. SOURCES-OF-TRUTH.md
  records it as authored, corrected by the owner personally, and never to be regenerated. The
  measurement agrees with the ruling: the authored curve scores 0.0% on private land and the
  county's own line scores 3.4%, so the owner's road is the better one and the county's would
  be a downgrade. It is copied across untouched.

WHAT THIS DOES NOT DO. It does not write city.txt. That file is 477 authored places, human
lines and story anchors, and rewriting its roads is a separate act that should be taken
deliberately rather than as a side effect of a data refresh.
"""
import json, math, os, re, sys
import numpy as np

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
sv = _load("serve_viewer", os.path.join(HERE, "serve-viewer.py"))

# ---------------------------------------------------------------------------------------------
#  THIS SCRIPT USED TO OVERWRITE Content/roads.txt THE MOMENT YOU RAN IT, WITH NO BACKUP.
#
#  No flag, no confirmation, no copy aside - `open(path, "w")` unconditionally, at the bottom of a
#  600-line file. So a single dry run to check a hypothesis silently reverted the survey network
#  AND deleted the twelve IDOT traffic counts that fixed the starving junction at church x maple,
#  because the writer below emits no `aadt` line. Recoverable from git, but you would not know it
#  had happened: the tool prints "wrote Content/roads.txt" either way and looks like it worked.
#
#  Same idiom as the sibling that got it right - build-road-blocks.py:26. No flag is a DRY RUN
#  that prints what it would do and writes nothing. `--write` backs up first, through the helper
#  that refuses to overwrite an earlier backup, and only then writes.
#
#  tools/roads-proposed.json is behind the same flag on purpose: it is the browser map's copy, and
#  a dry run must not refresh what the owner is looking at.
# ---------------------------------------------------------------------------------------------
WRITE = "--write" in sys.argv

# A path argument writes THERE instead of over Content/roads.txt - no backup needed, nothing at
# risk, and it is the only safe way to diff what this generator would produce against what is
# actually in use. That comparison is what proved the generator faithful: 24 lines differ and every
# one is an `aadt` line changing position, nothing else.
ELSEWHERE = next((a for a in sys.argv[1:] if not a.startswith("--")), None)


def idot_counts():
    """The county's traffic counts, by road name, from tools/rossville-aadt.txt.

    READ RATHER THAN INVENTED, AND READ RATHER THAN EATEN. These twelve numbers used to live only
    as hand-typed `aadt` lines inside Content/roads.txt - the file this script REGENERATES - and
    this writer emitted no aadt line at all. So a single regeneration deleted them silently, and
    the starving junction at church x maple would have come straight back. A measured fact a tool
    quietly drops is not stored; it is borrowed until somebody runs the tool.

    A road absent from the file is not an error: IDOT does not count twenty-one of this map's
    named roads, because there is nothing on them to count.
    """
    counts = {}
    path = os.path.join(HERE, "rossville-aadt.txt")
    if not os.path.exists(path):
        print("WARNING: no tools/rossville-aadt.txt - the IDOT counts will NOT be written.")
        return counts
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.split("#")[0].strip()
            if not line:
                continue
            bits = line.split()
            if len(bits) >= 2:
                counts[bits[0].lower()] = int(bits[1])
    return counts

# The county's traffic counts, keyed by road name. Read once at import; see idot_counts.
AADT = idot_counts()


def _alley_registry():
    """The frozen alley names and the polylines they were derived from."""
    out = []
    path = os.path.join(HERE, "rossville-alley-names.txt")
    if not os.path.exists(path):
        print("WARNING: no tools/rossville-alley-names.txt - alleys will be renamed by length rank.")
        return out
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.split("#")[0].strip()
            if not line:
                continue
            bits = line.split()
            pts = [tuple(float(v) for v in t.split(",")) for t in bits[1:] if "," in t]
            if len(pts) >= 2:
                out.append((bits[0], pts))
    return out


ALLEYS_NAMED = _alley_registry()


def _point_to_segment(p, a, b):
    (px, py), (ax, ay), (bx, by) = p, a, b
    dx, dy = bx - ax, by - ay
    if dx == 0 and dy == 0:
        return math.dist(p, a)
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.dist(p, (ax + t * dx, ay + t * dy))


def _polyline_gap(one, two):
    """How far apart two polylines are - the worst of each one's points to the other's SEGMENTS.

    NOT MIDPOINT TO MIDPOINT, and that is the whole correctness of the registry. Eleven of the 31
    alleys are stitched runs whose own joins land 39.5-92.5 m from their midpoint, so a midpoint
    rule with any sane radius orphans exactly the parent names it exists to protect - and renames
    them, which is the fault it was written to prevent.
    """
    def worst(src, dst):
        return max(min(_point_to_segment(p, dst[i], dst[i + 1]) for i in range(len(dst) - 1))
                   for p in src)

    # THE BETTER DIRECTION, NOT THE WORSE ONE, and that is deliberate. A registered polyline may be
    # the EXTENDED form of the alley - with a mouth on each end - while the thing being matched is
    # the freshly derived form that stops short. Taking the worst direction punishes exactly that
    # difference and renumbers the alley the registry exists to keep, which is the fault the whole
    # file is here to prevent. Taking the better one asks "does one lie along the other", which is
    # the real question. A 12 m ceiling still keeps two genuinely different corridors apart.
    return min(worst(one, two), worst(two, one))


#: How far a derived alley may sit from its registered polyline and still be the same alley.
#: Generous: the derivation moving a whole alley 12 m is a different alley, not a nudge.
ALLEY_SAME = 12.0


def registry_name(runs):
    """The frozen name for this derived alley, or None if it is genuinely new.

    `runs` is what clip_and_round returns - a LIST OF POLYLINES, because a run that leaves the map
    and comes back is split rather than bridged. Every run is compared against every registered
    polyline and the closest wins.
    """
    best, gap = None, ALLEY_SAME
    for name, pts in ALLEYS_NAMED:
        for run in runs:
            if len(run) < 2:
                continue
            d = _polyline_gap([tuple(q) for q in run], pts)
            if d < gap:
                best, gap = name, d
    return best


def registry_next():
    """The first alley number not already spoken for."""
    used = [int(n[5:]) for n, _ in ALLEYS_NAMED if n[5:].isdigit()]
    return max(used) + 1 if used else 1


#: How far an alley end may be carried to reach a street, in metres.
#:
#: The median gap is 14.9 m and the blanking radius that caused it is 11 m, so anything much beyond
#: 25 m is not a mouth that was blanked away - it is an alley that genuinely ends in the middle of a
#: block, and dragging it to the nearest street would be inventing a road rather than restoring one.
MOUTH_REACH = 25.0

#: Close enough to already be a mouth. Matches tools/check-alleys.py.
MOUTH_TOUCH = 8.0

MOUTHS = {"opened": 0, "already": 0, "too far": 0, "refused": 0}

_PARCEL_CACHE = {}


def _PARCELS(village):
    """One Parcels index, not one per alley - it builds a grid over every lot in the town."""
    if "p" not in _PARCEL_CACHE:
        _PARCEL_CACHE["p"] = cr.Parcels(village)
    return _PARCEL_CACHE["p"]


def _nearest_on_streets(p, roads):
    """The closest point on any non-alley centreline, and how far away it is."""
    best, at = float("inf"), None
    for name, _w, klass, lines, _src in roads:
        if klass == "alley":
            continue
        for pl in lines:
            for i in range(len(pl) - 1):
                a, b = pl[i], pl[i + 1]
                dx, dy = b[0] - a[0], b[1] - a[1]
                if dx == 0 and dy == 0:
                    continue
                t = ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / (dx * dx + dy * dy)
                t = max(0.0, min(1.0, t))
                q = (a[0] + t * dx, a[1] + t * dy)
                d = math.dist(p, q)
                if d < best:
                    best, at = d, q
    return best, at


def extend_to_streets(name, runs, roads, parcels=None):
    """Carry each end of an alley out to the street it stops short of.

    REFUSED RATHER THAN FORCED where the new stretch would run across somebody's lot. An alley
    mouth driven through a front garden is a worse fault than an alley with no mouth: the first is
    a road through a house, the second is only a road to nowhere. The refusals are counted and
    printed, because a mouth that cannot be opened is a thing somebody should look at rather than a
    silence.
    """
    out = []
    for pl in runs:
        pl = [tuple(p) for p in pl]
        if len(pl) < 2:
            out.append(pl)
            continue

        for which in (0, -1):
            gap, q = _nearest_on_streets(pl[which], roads)
            if q is None or gap <= MOUTH_TOUCH:
                MOUTHS["already"] += 1 if q is not None and gap <= MOUTH_TOUCH else 0
                continue
            if gap > MOUTH_REACH:
                MOUTHS["too far"] += 1
                continue

            # On the integer lattice, because VillageParser reads road points with ContentText.Int
            # and a fractional coordinate is a parse error rather than a rounding question.
            tip = (int(round(q[0])), int(round(q[1])))

            if parcels is not None and parcels.hit(tip) is not None:
                MOUTHS["refused"] += 1
                continue

            if which == 0:
                pl = [tip] + pl
            else:
                pl = pl + [tip]
            MOUTHS["opened"] += 1

        out.append(pl)
    return out

# Names the county spells differently from this project. The county is not automatically right
# about a name - it is right about geometry - so the project's own spelling wins and the
# county's is recorded beside it.
RENAME = {"mckidden": "mckibben"}

# Route 1 and Chicago Street are the same road (owner's standing fact #1), and the authored
# chicago line already spans the whole map, so the county's separate Route 1 run is redundant.
DROP = {"ilroute1"}

AUTHORED = {"chicago"}

MAINROADS = {"chicago", "attica"}
LANES = {"ann"}          # owner: Ann is a narrow lane, right of way 25 ft, fitted as an alley


def city_road_lines():
    """The existing road block, so widths and classes carry over and authored roads survive."""
    out = {}
    order = []
    name = None
    with open(os.path.join(CONTENT, "city.txt"), encoding="utf-8") as fh:
        for line in fh:
            l = line.split("#")[0].strip()
            if l.startswith("road "):
                p = l.split()
                name = p[1]
                if name not in out:
                    out[name] = {"width": float(p[2]), "class": None, "lines": []}
                    order.append(name)
                out[name]["lines"].append([tuple(float(v) for v in t.split(",")) for t in p[3:]])
            elif l.startswith("class ") and name:
                out[name]["class"] = l.split()[1]
    return out, order


def chain(segments, tol=1.5):
    """Join county segments that share an end into continuous runs.

    The layer is cut at every junction and address-range change, so a single street arrives as
    six or ten separate pieces. Walking them into runs matters because a road in city.txt is
    one polyline, and because a chain of stubs draws with a seam at every joint.
    """
    segs = [list(s) for s in segments if len(s) >= 2]
    runs = []
    while segs:
        run = segs.pop(0)
        grew = True
        while grew:
            grew = False
            for i, s in enumerate(segs):
                for cand, attach in ((s, "fwd"), (s[::-1], "rev")):
                    if math.dist(run[-1], cand[0]) <= tol:
                        run = run + cand[1:]
                        segs.pop(i)
                        grew = True
                        break
                    if math.dist(run[0], cand[-1]) <= tol:
                        run = cand[:-1] + run
                        segs.pop(i)
                        grew = True
                        break
                if grew:
                    break
        runs.append(run)
    return runs


def simplify(pts, tol=0.4):
    if len(pts) < 3:
        return pts
    a, b = np.array(pts[0]), np.array(pts[-1])
    ab = b - a
    L = float(np.hypot(*ab))
    if L < 1e-9:
        return [pts[0], pts[-1]]
    worst, wi = -1.0, 0
    for i in range(1, len(pts) - 1):
        ap = np.array(pts[i]) - a
        d = abs(ab[0] * ap[1] - ab[1] * ap[0]) / L
        if d > worst:
            worst, wi = d, i
    if worst <= tol:
        return [pts[0], pts[-1]]
    return simplify(pts[:wi + 1], tol)[:-1] + simplify(pts[wi:], tol)


def clip_and_round(pts, w=2100, h=2400):
    """Trim a run to the map and put its points on whole tiles.

    Both halves of this are parser requirements rather than tidiness, and the test found them:
    VillageParser reads road points with ContentText.Int, so `1234.5,678.9` is "not a whole
    number" and throws; and the county layer runs well past the town, so a street arrives with
    points at x = -2233 which are not on the map at all. A run that leaves the map and comes
    back is split rather than bridged, so a road never teleports across the edge.
    """
    runs, cur = [], []
    inside = lambda p: 0 <= p[0] <= w and 0 <= p[1] <= h

    def cross(a, b):
        """Where segment a->b meets the map edge, walking from a."""
        t0, t1 = 0.0, 1.0
        dx, dy = b[0] - a[0], b[1] - a[1]
        for p, q in ((-dx, a[0] - 0), (dx, w - a[0]), (-dy, a[1] - 0), (dy, h - a[1])):
            if p == 0:
                if q < 0:
                    return None
                continue
            r = q / p
            if p < 0:
                t0 = max(t0, r)
            else:
                t1 = min(t1, r)
        if t0 > t1:
            return None
        return ((a[0] + dx * t0, a[1] + dy * t0), (a[0] + dx * t1, a[1] + dy * t1))

    for i in range(len(pts) - 1):
        a, b = tuple(pts[i]), tuple(pts[i + 1])
        seg = cross(a, b)
        if seg is None:
            if cur:
                runs.append(cur)
                cur = []
            continue
        s, e = seg
        if not cur:
            cur = [s]
        elif math.dist(cur[-1], s) > 0.5:
            runs.append(cur)
            cur = [s]
        cur.append(e)
        if not inside(b):
            runs.append(cur)
            cur = []
    if cur:
        runs.append(cur)

    out = []
    for r in runs:
        rounded = []
        for x, y in r:
            p = (int(round(x)), int(round(y)))
            if not rounded or p != rounded[-1]:
                rounded.append(p)
        if len(rounded) >= 2:
            out.append(rounded)
    return out


def county_roads_classed(to_village):
    """County centrelines with the county's own road classification attached.

    CLASS COMES FROM HERE, NOT FROM THE GAP WIDTH. A first pass set the class from the measured
    right of way and got Attica wrong - it is 65 ft wide like every other street in town, so it
    read as an ordinary street when it is in fact a county highway. Width says how much room a
    road has; only the classification says what the road IS. The county's own fields say it
    plainly:
        FunctionalClass 30, RoadType STP  - state route. Chicago Street / IL Route 1.
        FunctionalClass 50, RoadType CTH  - county highway. Attica, and 3550 North.
        FunctionalClass 60, RoadType CVS  - village street. Everything else.
    """
    with open(os.path.join(HERE, "rossville-streets-county.geojson"), encoding="utf-8") as fh:
        gj = json.load(fh)
    out = {}
    for f in gj["features"]:
        g = f["geometry"]
        if g is None:
            continue
        p = f["properties"]
        name = (p.get("StreetName") or "?").strip().lower().replace(" ", "")
        e = out.setdefault(name, {"lines": [], "fclass": 99, "roadtype": None, "speed": None})
        e["fclass"] = min(e["fclass"], p.get("FunctionalClass") or 99)
        if p.get("RoadType"):
            e["roadtype"] = p["RoadType"]
        if (p.get("SpeedLimit") or -1) > 0:
            e["speed"] = p["SpeedLimit"]
        parts = g["coordinates"] if g["type"] == "MultiLineString" else [g["coordinates"]]
        for part in parts:
            e["lines"].append([to_village(x, y) for x, y, *_ in part])
    return out


def klass_of(fclass, roadtype):
    if fclass is None:
        return "street"
    if fclass <= 50 or roadtype in ("STP", "CTH"):
        return "mainroad"
    return "street"


def free_distance(village, res=1.0, pad=40):
    """Distance from every point to the nearest privately-owned ground.

    Twice this, at a point on a centreline, is the width of the right of way there - which is
    how the corridor widths in this file are arrived at rather than assumed.
    """
    from scipy import ndimage
    da = _load("derive_alleys", os.path.join(HERE, "derive-alleys.py"))
    xs = [p[0] for r in village for p in r]
    ys = [p[1] for r in village for p in r]
    x0, y0 = min(xs) - pad, min(ys) - pad
    w = int((max(xs) + pad - x0) / res) + 1
    h = int((max(ys) + pad - y0) / res) + 1
    owned = da.rasterise(village, x0, y0, w, h)
    return ndimage.distance_transform_edt(~owned) * res, x0, y0


def measure_row(lines, dist, x0, y0, res=1.0, town=None):
    """Median right-of-way width along a road.

    The 40th percentile rather than the middle, because a junction is a hole in the parcel
    cover the size of both roads meeting there, and those bulges drag a plain median upward.
    Samples that find no gap at all are dropped - that is the road running over a lot, which
    the private-land test already counts and which should not also distort the width.
    """
    vals = []
    h, w = dist.shape
    for pl in lines:
        for sx, sy in cr.sample(pl, step=5.0):
            # Only inside the platted town. Out in the country the fields tile edge to edge
            # and the "gap" is whatever the farm boundaries happen to leave - 3550 North came
            # out 131 ft wide that way and was classed a main road on the strength of it.
            if town is not None and not town.town_here((sx, sy)):
                continue
            c, r = int((sx - x0) / res), int((sy - y0) / res)
            if 0 <= r < h and 0 <= c < w:
                d = float(dist[r, c])
                if d > 0.5:
                    vals.append(d * 2.0)
    if len(vals) < 5:
        return None, []
    vals.sort()
    return vals[int(len(vals) * 0.40)], vals


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

    old, old_order = city_road_lines()
    county = county_roads_classed(to_village)
    alleys = json.load(open(os.path.join(HERE, "rossville-alleys.json"), encoding="utf-8"))

    W, H = 2100, 2400
    def onmap(p):
        return -50 <= p[0] <= W + 50 and -50 <= p[1] <= H + 50

    roads = []          # (name, width, klass, [polylines], source)

    # HOW FAR A ROAD MAY BE DEMOTED. Fitting the corridor to the measured gaps is right for an
    # ordinary street, and nonsense for a through route: Chicago Street runs diagonally across a
    # square plat, so its parcel gap pinches wherever the diagonal clips a block corner, and
    # those pinch points are an artefact of the platting rather than the width of the road. Left
    # unguarded the ladder read them literally and fitted IL Route 1 as a 13 ft alley. Anything
    # the county classes a state route or a county highway therefore stops at `street`.
    FLOOR = {}

    for name in sorted(county):
        if name in DROP or name in AUTHORED:
            continue
        runs = []
        for r in chain(county[name]["lines"]):
            runs.extend(clip_and_round(simplify(r)))
        if not runs:
            continue
        pretty = RENAME.get(name, name)
        kl = klass_of(county[name]["fclass"], county[name]["roadtype"])
        # A through route never demotes below a street however tight the gaps get - see FLOOR.
        FLOOR[pretty] = "street" if (county[name]["fclass"] or 99) <= 50 else "alley"
        roads.append((pretty, None, kl, runs, "county"))

    for name in AUTHORED:
        if name in old:
            keep = []
            for pl in old[name]["lines"]:
                keep.extend(clip_and_round(pl))
            FLOOR[name] = "street"
            roads.append((name, None, "mainroad", keep, "authored"))

    # ---- ALLEY NAMES COME FROM THE REGISTRY, NOT FROM THE LENGTH RANK -----------------------
    #
    # This was `enumerate(sorted(alleys, key=lambda a: -a["len"]), start=1)`, so a name was never
    # a name - it was a RANK, and a rank is a property of the whole set. Add one alley, lengthen
    # one, change the derivation by a metre, and every alley below it in the order silently became
    # a different road. Anything referring to `alley12` then pointed at different ground with no
    # error and nothing in a diff to show it.
    named = []
    fresh = registry_next()
    for a in sorted(alleys, key=lambda a: -a["len"]):
        pl = clip_and_round([tuple(p) for p in a["pts"]])
        if not pl:
            continue
        name = registry_name(pl)
        if name is None:
            name, fresh = f"alley{fresh}", fresh + 1
            print(f"[alleys] not in the registry, numbered {name} "
                  f"(len {a['len']:.0f} m) - add it to tools/rossville-alley-names.txt to freeze it")
        named.append((name, pl))

    # ---- AND GIVE EVERY ALLEY A MOUTH ------------------------------------------------------
    #
    # derive-alleys.py blanks 11 m around every street centreline before tracing, so a street's own
    # corridor is not mistaken for an alley - and nothing ever put the mouth back. The result was a
    # town whose back lanes touch nothing: 2 of 66 alley ends within 8 m of a street, 31 of 33
    # alleys stranded at BOTH ends, median gap 14.9 m, which is about the blanking radius plus half
    # a street corridor. The alleys stopped exactly where the blanking stopped them.
    #
    # It survived because it looks right from above - correct places, correct widths, and islands.
    # A car could not enter one from any direction.
    #
    # DONE HERE AND NOT IN derive-alleys.py, which is the correction that matters: this runs AFTER
    # chain, simplify and clip_and_round, so the extension is aimed at the runs that actually ship.
    # Computed upstream against raw county centrelines it is three transforms away from the shipped
    # geometry and lands barely two thirds of the mouths inside tolerance.
    named = [(n, extend_to_streets(n, pl, roads, _PARCELS(village))) for n, pl in named]

    # Emit in the registry's own order so the file reads stably, with anything new after it.
    for name, pl in sorted(named, key=lambda t: int(t[0][5:])):
        roads.append((name, None, "alley", pl, "parcels"))

    # ---- MEASURE the right of way, rather than inherit it --------------------------------
    # city.txt calls every street 10 and every alley 4, and those numbers came along with the
    # ruled straight lines. None of it is trusted here: the right of way is the gap the lots
    # leave, so it can simply be measured off the parcels, road by road.
    # TWO DIFFERENT WIDTHS, and confusing them makes the file unloadable.
    #
    # The number in a `road` line is the CORRIDOR the road kit's tiles are built to, and
    # RoadClasses.CorridorWidth pins it to the class - alley 4, street 10, mainroad 30 - with
    # VillageParser throwing if a road is declared any other width. It is not the right of way.
    # The first version of this file wrote the MEASURED right of way there, 20 m for a street,
    # and would have been rejected by the parser on the first line it reached.
    #
    # So the corridor is taken from the class, and the measured right of way is written beside
    # it as a comment, where it is a research finding rather than a geometry the kit must match.
    CORRIDOR = {"mainroad": 30.0, "street": 10.0, "alley": 4.0, "track": 10.0}

    # THE CORRIDOR HAS TO FIT INSIDE THE RIGHT OF WAY, and it did not.
    #
    # A road's class was taken from the county's functional classification, which says what a
    # road IS - Attica is a county highway - and the corridor then came from the class. But a
    # mainroad corridor is 98 ft and Attica's right of way measures 67 ft, the same as Holmes
    # and Summit and every other street in town. The result was 16 ft of asphalt lying across
    # private lots down its whole length, and 24 ft on Chicago Street.
    #
    # Being a county highway does not make the ground any wider. So the class is now stepped
    # DOWN until the corridor fits the right of way that was actually measured off the parcel
    # gaps. It is never stepped up: a village street with a generous verge is still a village
    # street. The county's own classification is kept in the comment either way, because it is
    # true about the road's function even when it cannot be true about its width.
    #
    # THE TEST IS HOW MUCH OF THE ROAD OVERRUNS, not one percentile of its width. Judging by
    # the 40th percentile let Ann through as a street: its right of way measures 33 ft, exactly
    # the street corridor, so it "fitted" while still spilling over at 38% of the stations along
    # it. The owner had already called Ann by eye - "a narrow lane, its right of way measures
    # 25 ft, so it is fitted as an alley" - and the looser test disagreed with him. The stricter
    # one does not.
    LADDER = ["mainroad", "street", "alley"]

    dist, gx0, gy0 = free_distance(village)
    P0 = cr.Parcels(village)
    sized = []
    TOLERATED = 0.10          # a tenth of a road may pinch; more than that is a road too wide
    for n, _w, klass, lines, srcname in roads:
        row, gaps = measure_row(lines, dist, gx0, gy0, town=P0)
        fitted = klass
        floor = FLOOR.get(n, "alley")
        if gaps and klass in LADDER:
            allowed = LADDER[LADDER.index(klass):LADDER.index(floor) + 1] or [klass]
            for cand in allowed:
                fitted = cand
                over = sum(1 for g in gaps if g < CORRIDOR[cand]) / len(gaps)
                if over <= TOLERATED:
                    break
        if fitted != klass:
            over0 = sum(1 for g in gaps if g < CORRIDOR[klass]) / len(gaps)
            print(f"    {n}: a {klass} corridor ({CORRIDOR[klass]:g} m) overruns "
                  f"{100*over0:.0f}% of its own right of way (median {row:.1f} m) "
                  f"- fitted as {fitted}")

        # A GAP TOO NARROW FOR THE NARROWEST CLASS IS NOT A ROAD. Some of the strips the alley
        # derivation found are 6 ft across - a sliver left between two lot lines, not something
        # anybody drives down. They only became visible when the parser started refusing an
        # easement narrower than its own corridor, which is exactly what that check is for.
        if row is not None and row < CORRIDOR[fitted] and srcname == "parcels":
            print(f"    {n}: dropped - a {row:.1f} m gap ({row*3.28084:.1f} ft) is a lot-line "
                  f"sliver, not an alley")
            continue

        sized.append((n, CORRIDOR[fitted], fitted, lines, srcname, row, klass))
    roads = [(n, w, c, l, s, row) for n, w, c, l, s, row, _fc in sized]
    county_class = {n: fc for n, w, c, l, s, row, fc in sized}

    print("measured right of way (research) against the corridor the road kit needs (game):")
    byclass = {}
    for n, w, c, lines, s, row, _fc in sized:
        if row:
            byclass.setdefault(c, []).append(row)
    for c, ws in sorted(byclass.items()):
        ws.sort()
        m = ws[len(ws) // 2]
        print(f"  {c:<9} n={len(ws):<3} right of way {m:5.1f} m ({m*3.28084:5.1f} ft)"
              f"   corridor written: {CORRIDOR[c]:g} m")
    print()

    # ---- what did it cost and what did it buy -------------------------------------------
    P = cr.Parcels(village)

    def measure(lines):
        ins = tot = 0
        for pl in lines:
            for s in cr.sample(pl):
                if not P.town_here(s):
                    continue
                tot += 1
                if P.hit(s) is not None:
                    ins += 1
        return ins, tot

    oi = ot = 0
    for n, d in old.items():
        i, t_ = measure(d["lines"])
        oi += i
        ot += t_
    ni = nt = 0
    for n, w, c, lines, srcname, _row in roads:
        i, t_ = measure(lines)
        ni += i
        nt += t_

    print(f"old network: {len(old)} roads, {100*oi/max(1,ot):5.1f}% of it on private land")
    print(f"new network: {len(roads)} roads, {100*ni/max(1,nt):5.1f}% of it on private land")
    print()

    worse = []
    for n, w, c, lines, srcname, _row in roads:
        if n not in old:
            continue
        i, t_ = measure(lines)
        oi2, ot2 = measure(old[n]["lines"])
        if t_ and ot2:
            a, b = 100 * i / t_, 100 * oi2 / ot2
            if a > b + 0.5:
                worse.append((n, b, a))
    if worse:
        print("roads the new source makes WORSE (kept anyway only if authored):")
        for n, b, a in sorted(worse, key=lambda r: r[1] - r[2]):
            print(f"  {n:<12} {b:5.1f}%  ->  {a:5.1f}%")
        print()

    # ---- write it ------------------------------------------------------------------------
    L = []
    w_ = L.append
    w_("# " + "=" * 76)
    w_("#  THE ROADS OF ROSSVILLE, FROM SURVEY RATHER THAN FROM A RULER")
    w_("#")
    w_("#  DERIVED AND RE-RUNNABLE - build with tools/build-roads.py. This file does NOT")
    w_("#  replace the road lines in city.txt; nothing reads it yet. It is the proposal, and")
    w_("#  the numbers that justify it are below.")
    w_("#")
    w_("#  WHAT WAS WRONG. 32 of city.txt's 37 road lines are two points - a straight line")
    w_("#  ruled from one side of the town to the other. Attica Street is `0,1353 2099,1353`.")
    w_("#  The 2026-08-04 refit slid each of them bodily onto the nearest parcel-free strip,")
    w_("#  which is the most a rigid shift can do and no help at all to a road whose SHAPE is")
    w_("#  wrong - Harrison turns 15.4 degrees at Benton and a straight line cannot.")
    w_("#")
    w_("#  SOURCES, one per class of road:")
    w_("#")
    w_("#    STREETS - Vermilion County's own centreline layer, 146 segments over Rossville,")
    w_("#    on the same ArcGIS server that gave us the parcels and the property records. Real")
    w_("#    surveyed geometry, with address ranges and ZIP 60963 attached. Segments are")
    w_("#    chained into continuous runs here, since the layer cuts them at every junction.")
    w_("#")
    w_("#    ALLEYS - derived from the parcel gaps by tools/derive-alleys.py, because no county")
    w_("#    maps its alleys. A right of way is the ground nobody owns, so the alley is already")
    w_("#    in the survey as an absence and only needs tracing out of it.")
    w_("#")
    w_("#    CHICAGO STREET - copied from city.txt untouched. It is authored, the owner")
    w_("#    corrected it personally, and SOURCES-OF-TRUTH.md says it is never regenerated.")
    w_("#    The measurement backs the ruling: the authored curve runs on private land 0.0% of")
    w_("#    the time and the county's own Route 1 line manages 3.4%, so replacing it would be")
    w_("#    a downgrade. Route 1 is dropped as redundant - it is the same road.")
    w_("#")
    w_("#  HOW GOOD IS IT. Sampled every 4 m through the platted town, share of centreline")
    w_("#  running across somebody's lot - which for a public right of way should be zero:")
    w_("#")
    w_(f"#    city.txt as it stands   {100*oi/max(1,ot):5.1f}%")
    w_(f"#    this file               {100*ni/max(1,nt):5.1f}%")
    w_("#")
    w_("#  NAMES. The county spells McKibben as `McKidden`; this project's spelling is kept.")
    w_("#  The county also carries the west-of-Route-1 names the research already predicted -")
    w_("#  Park, Perry, Stufflebeam, Smith, Watson - as real addressed streets, so they are")
    w_("#  here as roads in their own right rather than as continuations.")
    w_("#")
    w_("#  TWO WIDTHS, DO NOT CONFUSE THEM. The number on a road line is the CORRIDOR the road")
    w_("#  kit is built to and VillageParser pins to the class - alley 4, street 10, mainroad")
    w_("#  30 - and declaring any other value is a parse error. The measured RIGHT OF WAY is a")
    w_("#  different quantity, written in the trailing comment: 20.0 m for a street, 6.0 m for")
    w_("#  an alley, taken off the parcel gaps. The right of way is what the town has; the")
    w_("#  corridor is what the tiles draw.")
    w_("#")
    w_("#  THREE WIDTHS, NESTED, AND ONLY THE MIDDLE ONE IS THE ROAD:")
    w_("#    easement  the public land - the gap the lots leave. Measured off the parcels.")
    w_("#              A Rossville street has 66 ft of it, the same as its main roads.")
    w_("#    corridor  what the road kit paves: asphalt plus its pavements. Pinned to the class")
    w_("#              at 33 ft for a street and 13 ft for an alley. Written on the road line.")
    w_("#    asphalt   measured off the tile mesh at build time by CityStreets, inside that.")
    w_("#  So a street leaves roughly 16 ft each side of public ground that is not road - verge,")
    w_("#  utilities, and a walk WHERE THERE IS ONE. Which roads have a sidewalk is not recorded")
    w_("#  here and is not known per road: the downtown photographs show a concrete walk set back")
    w_("#  behind a grass verge on Chicago Street, and nothing establishes it street by street.")
    w_("#  The easement is what a later pass needs to lay one without crossing a lot line.")
    w_("#")
    w_("#  FORMAT  road <name> <corridor width, metres> x,y x,y ...   then an indented class,")
    w_("#          a `carries` where the road's job outranks the ground it has, and, where it")
    w_("#          could be measured, an easement in metres.")
    w_("#  Village metres, the same frame as parcels.txt and city.txt.")
    w_("# " + "=" * 76)
    w_("")

    for n, width, klass, lines, srcname, row in roads:
        note = f"   # {srcname}" + (f", right of way {row:.1f} m measured" if row else "")
        if county_class.get(n) and county_class[n] != klass:
            note += f", county classes it {county_class[n]}"
        # THE COUNTY'S CLASSIFICATION IS WRITTEN DOWN, NOT JUST MENTIONED.
        #
        # The class above is stepped DOWN until the corridor fits the right of way, which is
        # right and is the whole point of the ladder - a county highway on a 67 ft right of way
        # is paved as a street or it lays asphalt across private lots. But the road's JOB does
        # not step down with its width, and until now the county's answer was noted in a trailing
        # comment and thrown away.
        #
        # Everything that decides how a junction behaves read the corridor: priority, where the
        # signals go, how much traffic a road is given. So on the surveyed network no road was a
        # main road, the town lost the only set of lights it had, and every north-south arm gave
        # way permanently to an east-west stream that never yielded - nine tenths of stopped
        # cars waiting two minutes with clear road ahead.
        #
        # `carries` says the function and has no width check, because saying something the width
        # cannot support is exactly what it is for. Written only when the two disagree.
        # ONLY EVER UP TO A MAIN ROAD, never up to a street.
        #
        # The first cut wrote `carries` wherever the county disagreed, which also promoted Ann
        # and Watson - both fitted as alleys - to street, and put ambient traffic straight back
        # onto them. Standing fact 4 says an alley is not for cars, and the owner called Ann by
        # eye: "a narrow lane, its right of way measures 25 ft, so it is fitted as an alley".
        # A county centreline file does not overrule the man who rode a bike down it.
        #
        # The case this exists for is the arterial with no ground: a state route or county
        # highway that has to be paved as a street because that is all the right of way there is.
        # Anything below that, the fitted class is already the honest answer.
        carries = county_class.get(n)
        if carries not in ("mainroad", "freeway"):
            carries = None
        for pl in lines:
            pts = " ".join(f"{int(round(x))},{int(round(y))}" for x, y in pl)
            w_(f"road {n} {width:g} {pts}{note}")
            w_(f"  class {klass}")
            if carries and carries != klass:
                w_(f"  carries {carries}")
            if row and row >= width:
                w_(f"  easement {row:.1f}")
            if n.lower() in AADT:
                w_(f"  aadt {AADT[n.lower()]}   # IDOT count, see tools/rossville-aadt.txt")
        w_("")

    path = os.path.join(CONTENT, "roads.txt")

    counted = sum(1 for line in L if line.startswith("  aadt "))
    print(f"[alleys] mouths opened {MOUTHS['opened']}, already touching {MOUTHS['already']}, "
          f"refused (would cross a lot) {MOUTHS['refused']}, too far to be a blanked mouth "
          f"{MOUTHS['too far']}")

    if ELSEWHERE:
        with open(ELSEWHERE, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("\n".join(L))
        print(f"wrote {ELSEWHERE}  ({len(roads)} roads, {counted} aadt lines) - "
              "Content/roads.txt untouched")
        return

    if not WRITE:
        have = 0
        if os.path.exists(path):
            with open(path, encoding="utf-8") as fh:
                have = sum(1 for line in fh if line.startswith("  aadt "))

        print(f"DRY RUN - nothing written. {len(roads)} roads would be written to")
        print(f"  {os.path.normpath(path)}")
        print("  and tools/roads-proposed.json (the browser map's copy).")
        print(f"  IDOT counts: {counted} aadt lines would be written, {have} are in the file now.")

        # ROADS vs ROAD RUNS - and confusing the two produced a false alarm worth recording.
        # `len(roads)` is 61 UNIQUE NAMES; the file has 66 `road ` LINES, because Summit, Grove,
        # Green, Harrison and Holmes each arrive as two runs. Comparing the two numbers looks like
        # five roads going missing and is nothing of the kind. Compare runs with runs.
        runs = 0
        if os.path.exists(path):
            with open(path, encoding="utf-8") as fh:
                runs = sum(1 for line in fh if line.startswith("road "))
        mine = sum(1 for line in L if line.startswith("road "))
        if runs != mine:
            print(f"  ** WARNING: {runs} road runs in the file, {mine} here. Diff before writing. **")
        else:
            print(f"  {mine} road runs, matching the file. {len(roads)} distinct names.")

        print("Re-run with --write to back up and write.")
        return

    sv.backup(path, "build-roads")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(L))
    print(f"wrote Content/roads.txt  ({len(roads)} roads), backup taken first")

    with open(os.path.join(HERE, "roads-proposed.json"), "w", encoding="utf-8") as fh:
        json.dump([{"n": n, "w": width, "c": klass, "src": s, "row": row,
                    "lines": [[[round(x, 1), round(y, 1)] for x, y in pl] for pl in lines]}
                   for n, width, klass, lines, s, row in roads], fh)
    print("wrote tools/roads-proposed.json")


if __name__ == "__main__":
    main()
