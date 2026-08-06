"""Grow each downtown terrace group to cover every lot its building actually stands on.

WHAT A TERRACE IS IN THIS DATA. FEMA traces a continuous run of joined brick as ONE polygon.
Downtown Rossville is exactly that - COMMERCIAL-ROW.md: "not a scatter of buildings, a
continuous two-storey brick terrace" - so a single 28,883 sq ft polygon lands on whichever
parcel holds its centroid and every other lot under it reads as empty ground.

The first pass at this grouped the lots that were FLAGGED as disagreements, which is not the
same set as the lots the building is standing on. Measured afterwards, the Chicago Street
terrace was only 49% inside the five lots it had been given, and the Attica row 45% - the rest
was sitting on lots nobody had grouped. So this asks the geometry instead: sample each terrace
polygon, find every parcel underneath it, and put them all in the property.

    python tools/group-terraces.py            # say what it would do
    python tools/group-terraces.py --apply    # do it

WHAT IT DELIBERATELY DOES NOT DO. It never renames a property somebody has already named, and
it never changes a `was` or a `kind` that has been set by hand - it only widens the membership.
"""
import json, math, os, re, shutil, sys, urllib.request
import importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
PORT = int(os.environ.get("VIEWER_PORT", "8750"))


def _load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


ft = _load("fit_transform", os.path.join(HERE, "fit-transform.py"))
sv = _load("serve_viewer", os.path.join(HERE, "serve-viewer.py"))

MIN_SHARE = 0.04        # a lot has to be really under the building, not clipped by a corner
MIN_AREA = 3000         # square feet: below this it is a shop unit, not a terrace


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


def under(ring, village, boxes, n=26):
    """Every parcel beneath this polygon, as a share of the polygon's own area, plus the share
    that is on no parcel at all - which is the public right of way."""
    xs = [p[0] for p in ring]
    ys = [p[1] for p in ring]
    x0, x1 = min(xs), max(xs)
    y0, y1 = min(ys), max(ys)
    hits, none, tot = {}, 0, 0
    for a in range(n):
        py = y0 + (y1 - y0) * (a + 0.5) / n
        for b in range(n):
            px = x0 + (x1 - x0) * (b + 0.5) / n
            if not inside((px, py), ring):
                continue
            tot += 1
            found = None
            for i, (bx0, by0, bx1, by1) in enumerate(boxes):
                if px < bx0 or px > bx1 or py < by0 or py > by1:
                    continue
                if inside((px, py), village[i]):
                    found = i
                    break
            if found is None:
                none += 1
            else:
                hits[found] = hits.get(found, 0) + 1
    if not tot:
        return {}, 0.0
    return {k: c / tot for k, c in hits.items()}, none / tot


def main():
    apply = "--apply" in sys.argv
    village = ft.load_village()
    boxes = [(min(p[0] for p in r), min(p[1] for p in r),
              max(p[0] for p in r), max(p[1] for p in r)) for r in village]

    data = json.load(open(os.path.join(HERE, "rossville-buildings-seated.json"), encoding="utf-8"))
    v = sv.read_verdicts()

    # Every property that already has a name, and the biggest building standing on it.
    props = {}
    for pid, e in v.items():
        name = (e.get("property") or "").strip()
        if name:
            props.setdefault(name, set()).add(pid)

    changes = []
    for name, pids in sorted(props.items()):
        bs = [b for b in data["buildings"] if b["parcel"] in pids]
        if not bs:
            continue
        big = max(bs, key=lambda b: b["sqft"] or 0)
        if (big["sqft"] or 0) < MIN_AREA:
            continue

        cover, row = under(big["ring"], village, boxes)
        want = {p for p, s in cover.items() if s >= MIN_SHARE}
        add = sorted(want - pids)
        if not add:
            continue
        changes.append((name, sorted(pids), add, row, big["sqft"], cover))

    if not changes:
        print("every terrace already covers the lots it stands on.")
        return

    for name, have, add, row, sqft, cover in changes:
        print(f'\n"{name}"  ({sqft:,.0f} sq ft)')
        print(f"   has  {have}")
        print(f"   ADD  {add}")
        for p in add:
            print(f"        parcel {p}: {100*cover[p]:.0f}% of the building stands on it")
        print(f"   {100*row:.0f}% of the polygon is on no parcel - the right of way")

    if not apply:
        print("\nnothing written. Re-run with --apply.")
        return

    shutil.copy2(sv.VERDICTS, sv.VERDICTS + ".before-terrace-grow")
    n = 0
    for name, have, add, row, sqft, cover in changes:
        model = v.get(have[0], {})
        for p in add:
            e = v.setdefault(p, {})
            if (e.get("property") or "").strip():
                continue                      # already spoken for; never re-home a lot
            e.setdefault("was", model.get("was", "built"))
            e.setdefault("kind", model.get("kind", "shop"))
            e["property"] = name
            e.setdefault("note", "")
            n += 1
    sv.write_verdicts(v)
    print(f"\nadded {n} lots to {len(changes)} terraces. "
          f"Content/parcel-1991.txt now holds {len(v)} rulings.")
    try:
        urllib.request.urlopen(f"http://127.0.0.1:{PORT}/__verdicts", timeout=2).read()
    except Exception:
        pass


if __name__ == "__main__":
    main()
