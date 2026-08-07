"""Put the back-of-lot strips back onto the lot they were split off.

THE OWNER'S FACT, 2026-08-05, from having had friends who lived along Maple in 1991:

    "the lots ran to the alley"

Which settles what these small parcels are. Behind the houses on E Maple - and elsewhere in
town - the county's map carries strips of about 66 x 33 ft: half of a standard 66 x 66 ft lot,
one surveyor's chain wide, sitting hard against the alley with no street address and, in eleven
of seventeen cases, no building. They look like lots and they make no sense as lots.

They are not lots. They are the BACK HALVES of the platted lots, deeded off separately at some
point after 1991 - to a neighbour, to a son, to whoever wanted a garage off the alley. The
county has to give any separately-owned scrap a parcel number whether or not anybody could
build on it, so the split shows up as a boundary on today's map that did not exist in the town
being built.

So this joins each strip to the lot in front of it, using the property mechanism the map
already has: same property name, one property, and the internal line dissolves. The name used
is the front lot's own street address, because that is what the property IS.

WHAT IT WILL NOT DO. It only joins a strip to a lot it genuinely shares a boundary with, and
only where exactly one addressed neighbour is on the street side of it. Anything ambiguous is
listed and left alone for a person to look at. It also backs the rulings file up first: that
file is the one thing here no re-run can rebuild.

    python tools/merge-back-strips.py            # say what it would do
    python tools/merge-back-strips.py --apply    # do it
"""
import json, math, os, re, shutil, sys, urllib.request, urllib.error
import importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(HERE, "..", "Content")
PORT = int(os.environ.get("VIEWER_PORT", "8750"))


def _load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


ft = _load("fit_transform", os.path.join(HERE, "fit-transform.py"))
sv = _load("serve_viewer", os.path.join(HERE, "serve-viewer.py"))

FT = 3.28084
FT2 = 10.7639


def addressed(c):
    """Does this parcel have a REAL street address - a number on a street?

    The county writes a bare street name on a parcel it has no house number for, which is not
    an address so much as a note about which street the scrap fronts. Treating any non-empty
    address as "this is somebody's lot" let parcel 481 through: 66 x 34 ft, the back half of
    213 E Maple, recorded as `E Maple` with no number, and so never joined to the lot in front.
    Only two parcels in the whole county file are like this, and one of them was the bug.
    """
    a = (c or {}).get("addr") or ""
    return bool(re.match(r"^\s*\d", a))


def area(r):
    a = 0.0
    for i in range(len(r) - 1):
        a += r[i][0] * r[i + 1][1] - r[i + 1][0] * r[i][1]
    return abs(a) / 2


def bbox_ft(r):
    xs = [p[0] for p in r]
    ys = [p[1] for p in r]
    return (max(xs) - min(xs)) * FT, (max(ys) - min(ys)) * FT


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


def shared_edge(a, b, step=1.0, eps=0.6):
    """How much boundary two rings have in common, in metres.

    Walked rather than compared vertex by vertex: neighbouring cadastral parcels routinely
    describe the same boundary with different numbers of points, so matching vertices would miss
    most of it. Stepping along each edge and asking whether a point just outside falls in the
    other parcel does not care how either was drawn.
    """
    total = 0.0
    for i in range(len(a) - 1):
        (x0, y0), (x1, y1) = a[i], a[i + 1]
        dx, dy = x1 - x0, y1 - y0
        L = math.hypot(dx, dy)
        if L < 0.1:
            continue
        nx, ny = -dy / L * eps, dx / L * eps
        n = max(1, int(L / step))
        for k in range(n):
            t = (k + 0.5) / n
            px, py = x0 + dx * t, y0 + dy * t
            if inside((px + nx, py + ny), b) or inside((px - nx, py - ny), b):
                total += L / n
    return total


def main():
    apply = "--apply" in sys.argv

    village = ft.load_village()
    county = {}
    with open(os.path.join(CONTENT, "parcel-county.txt"), encoding="utf-8") as fh:
        import re
        for line in fh:
            line = line.strip()
            if not line.startswith("parcel "):
                continue
            p = line.split(" ", 3)
            pid = int(p[1])
            e = county.setdefault(pid, {})
            if p[2] == "address":
                e["addr"] = p[3].strip('"')
            elif p[2] == "class":
                m = re.match(r'(\S+)\s+"(.*)"', p[3])
                if m:
                    e["cls"], e["clsname"] = m.group(1), m.group(2)

    cents = [ft.ring_centroid(r) for r in village]

    roads = json.load(open(os.path.join(HERE, "roads-proposed.json"), encoding="utf-8"))
    alleys = [pl for rd in roads if rd["c"] == "alley" for pl in rd["lines"]]

    def seg_d(p, a, b):
        ax, ay = a
        bx, by = b
        dx, dy = bx - ax, by - ay
        L = dx * dx + dy * dy
        t = 0.0 if L == 0 else max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - ay) * dy) / L))
        return math.hypot(p[0] - (ax + t * dx), p[1] - (ay + t * dy))

    def alley_dist(p):
        return min(seg_d(p, pl[i], pl[i + 1]) for pl in alleys for i in range(len(pl) - 1))

    # ---- who is a back strip -------------------------------------------------------------
    #
    # GEOMETRY ONLY. The address was in this test twice and was wrong twice. First it excluded
    # anything with an address at all, which missed parcel 481 - the back half of 213 E Maple,
    # recorded as `E Maple` with no house number. Then, with that fixed, it still missed parcel
    # 300: the back half of 103 E Maple, which the county addressed `103 Maple`, the SAME HOUSE
    # NUMBER as the lot in front. A back strip can be addressed however the assessor felt like
    # addressing it, so the address cannot be the test.
    #
    # What is reliable is the shape. On E Maple the lots come in two depths - 166 ft where the
    # lot still runs to the alley, and 133 ft where the back half was sold - and the strip is
    # the 66 x 33 ft remainder sitting against the lane. So: small, on an alley, and much
    # smaller than the lot it would be joined to. That last guard is what stops two ordinary
    # neighbouring lots being welded together.
    strips = []
    for i, r in enumerate(village):
        a = area(r) * FT2
        if a > 5000:
            continue
        if alley_dist(cents[i]) * FT > 45:
            continue
        strips.append(i)

    print(f"candidate back strips: {len(strips)}")

    # ---- which lot was each one split off ------------------------------------------------
    merges, skipped = [], []
    for s in strips:
        best = []
        for j, r in enumerate(village):
            if j == s:
                continue
            if abs(cents[j][0] - cents[s][0]) > 90 or abs(cents[j][1] - cents[s][1]) > 90:
                continue
            if not addressed(county.get(j, {})):
                continue
            share = shared_edge(village[s], r)
            if share > 8.0:                      # a real party line, not a corner touch
                best.append((share, j))
        best.sort(reverse=True)

        if not best:
            skipped.append((s, "no addressed lot shares a boundary"))
            continue
        if len(best) > 1 and best[1][0] > best[0][0] * 0.7:
            skipped.append((s, f"two lots share it about equally "
                               f"({best[0][1]} and {best[1][1]}) - needs a person"))
            continue

        front = best[0][1]
        # The lot in front must be on the street side: further from the alley than the strip.
        if alley_dist(cents[front]) <= alley_dist(cents[s]):
            skipped.append((s, f"parcel {front} is not between it and the street"))
            continue
        # And it must be substantially the bigger of the two, or this is not a lot and its
        # back half - it is two ordinary lots that happen to share a line.
        if area(village[front]) < area(village[s]) * 1.8:
            skipped.append((s, f"parcel {front} is not much bigger - two lots, not a split"))
            continue
        merges.append((s, front, county[front]["addr"], best[0][0] * FT))

    print(f"  can be joined to a lot in front : {len(merges)}")
    print(f"  left for a person to look at    : {len(skipped)}\n")

    for s, front, addr, share in sorted(merges, key=lambda m: m[2]):
        print(f"  parcel {s:<4} -> {addr:<22} (parcel {front}, {share:.0f} ft of shared line)")
    if skipped:
        print()
        for s, why in skipped:
            print(f"  parcel {s:<4} SKIPPED: {why}")

    if not apply:
        print("\nnothing written. Re-run with --apply to join them.")
        return

    # ---- write, having backed up first ---------------------------------------------------
    if os.path.exists(sv.VERDICTS):
        print(f"\nbacked up to {os.path.basename(sv.backup(sv.VERDICTS, 'merge'))}")

    v = sv.read_verdicts()
    kept = 0
    for s, front, addr, share in merges:
        # NEVER OVERWRITE A RULING ALREADY MADE. The point of the file is that a person said so.
        for pid in (s, front):
            e = v.setdefault(pid, {})
            if e.get("property") and e["property"] != addr:
                continue
            e.setdefault("was", "built")
            e["property"] = addr
            e.setdefault("kind", "")
            e.setdefault("note", "")
        v[s]["note"] = (v[s].get("note") or
                        "back of the lot; ran to the alley in 1991, split off later")
        kept += 1
    sv.write_verdicts(v)
    print(f"joined {kept} strips to the lot in front. Content/parcel-1991.txt now holds "
          f"{len(v)} rulings.")

    # Nudge the open map, if one is up.
    try:
        urllib.request.urlopen(f"http://127.0.0.1:{PORT}/__verdicts", timeout=2).read()
    except Exception:
        pass


if __name__ == "__main__":
    main()
