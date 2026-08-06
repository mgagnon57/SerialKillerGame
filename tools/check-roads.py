"""Are the roads where the survey says the roads are?

THE TEST. A public right of way is a HOLE in the parcel coverage - nobody owns it, so the
assessor draws nothing there. So a correct centreline sits in that hole, and a wrong one runs
across somebody's lot. Sample a road every few metres, ask how many of those samples land
inside a parcel, and the answer is a percentage that needs no interpretation.

WHAT IS BEING COMPARED. Content/city.txt's roads, which came from OpenStreetMap and were then
shifted bodily onto their strips in the 2026-08-04 refit, against Vermilion County's own street
centreline layer, which nothing in this project has seen before. Both are put through the same
affine fit and measured the same way.

ONLY THE PLATTED TOWN COUNTS. Out in the country the parcels tile farmland edge to edge, so
"inside a lot" is true of everything and means nothing. Samples are only scored where the
surrounding parcels are town-sized.
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

CELL = 40.0


class Parcels:
    """Point-in-any-parcel, with a grid index so a few thousand samples stay cheap."""

    def __init__(self, rings):
        self.rings = rings
        self.box = []
        self.grid = {}
        for i, r in enumerate(rings):
            xs = [p[0] for p in r]
            ys = [p[1] for p in r]
            b = (min(xs), min(ys), max(xs), max(ys))
            self.box.append(b)
            for gx in range(int(b[0] // CELL), int(b[2] // CELL) + 1):
                for gy in range(int(b[1] // CELL), int(b[3] // CELL) + 1):
                    self.grid.setdefault((gx, gy), []).append(i)

    @staticmethod
    def _in(pt, ring):
        x, y = pt
        hit = False
        for i in range(len(ring) - 1):
            x0, y0 = ring[i]
            x1, y1 = ring[i + 1]
            if (y0 > y) != (y1 > y):
                if x < x0 + (y - y0) / (y1 - y0) * (x1 - x0):
                    hit = not hit
        return hit

    def hit(self, pt):
        """Index of a parcel containing the point, or None."""
        for i in self.grid.get((int(pt[0] // CELL), int(pt[1] // CELL)), ()):
            b = self.box[i]
            if b[0] <= pt[0] <= b[2] and b[1] <= pt[1] <= b[3] and self._in(pt, self.rings[i]):
                return i
        return None

    def town_here(self, pt, radius=90.0):
        """Is this the platted town? True when the parcels around the point are town-sized.
        Out in the country they are fields, and every sample sits inside one."""
        near = []
        g = (int(pt[0] // CELL), int(pt[1] // CELL))
        for dx in (-2, -1, 0, 1, 2):
            for dy in (-2, -1, 0, 1, 2):
                near.extend(self.grid.get((g[0] + dx, g[1] + dy), ()))
        if not near:
            return False
        areas = []
        for i in set(near):
            b = self.box[i]
            areas.append((b[2] - b[0]) * (b[3] - b[1]))
        areas.sort()
        return areas[len(areas) // 2] < 6000.0      # a town lot's box; a field is far bigger


def sample(pts, step=4.0):
    out = []
    for i in range(len(pts) - 1):
        (x0, y0), (x1, y1) = pts[i], pts[i + 1]
        d = math.hypot(x1 - x0, y1 - y0)
        n = max(1, int(d / step))
        for j in range(n):
            t = j / n
            out.append((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t))
    if pts:
        out.append(tuple(pts[-1]))
    return out


def city_roads():
    roads = {}
    with open(os.path.join(CONTENT, "city.txt"), encoding="utf-8") as fh:
        for line in fh:
            line = line.split("#")[0].strip()
            if not line.startswith("road "):
                continue
            p = line.split()
            roads.setdefault(p[1], []).append(
                [tuple(float(v) for v in t.split(",")) for t in p[3:]])
    return roads


def county_roads(to_village):
    with open(os.path.join(HERE, "rossville-streets-county.geojson"), encoding="utf-8") as fh:
        gj = json.load(fh)
    roads = {}
    for f in gj["features"]:
        g = f["geometry"]
        if g is None:
            continue
        parts = g["coordinates"] if g["type"] == "MultiLineString" else [g["coordinates"]]
        name = (f["properties"].get("StreetName") or "?").strip().lower().replace(" ", "")
        for part in parts:
            roads.setdefault(name, []).append([to_village(x, y) for x, y, *_ in part])
    return roads


def score(name, lines, P):
    inside = total = 0
    for pts in lines:
        for s in sample(pts):
            if not P.town_here(s):
                continue
            total += 1
            if P.hit(s) is not None:
                inside += 1
    return name, inside, total


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

    def to_village(lon, lat):
        return ft.apply_affine(T, (lon - lon0) * k, -(lat - lat0))

    P = Parcels(village)
    cur = city_roads()
    cty = county_roads(to_village)

    def total_of(roads):
        ins = tot = 0
        rows = []
        for n, lines in sorted(roads.items()):
            _, i, t_ = score(n, lines, P)
            if t_:
                rows.append((n, i, t_))
                ins += i
                tot += t_
        return ins, tot, rows

    ci, ct, crows = total_of(cur)
    yi, yt, yrows = total_of(cty)

    print("SHARE OF EACH CENTRELINE THAT RUNS ACROSS PRIVATE LAND")
    print("(sampled every 4 m, platted town only - lower is better, 0% is right)\n")
    print(f"  Content/city.txt   {100*ci/max(1,ct):5.1f}%   ({ci} of {ct} samples on a lot)")
    print(f"  county centrelines {100*yi/max(1,yt):5.1f}%   ({yi} of {yt} samples on a lot)")
    print()

    byname = {n: (i, t_) for n, i, t_ in crows}
    print("worst roads in city.txt:")
    for n, i, t_ in sorted(crows, key=lambda r: -(r[1] / max(1, r[2])))[:14]:
        c = cty.get(n)
        alt = ""
        if c:
            _, ci2, ct2 = score(n, c, P)
            if ct2:
                alt = f"   county: {100*ci2/ct2:5.1f}%"
        print(f"  {n:<12} {100*i/max(1,t_):5.1f}%  ({i}/{t_}){alt}")

    print("\ncounty streets, worst:")
    for n, i, t_ in sorted(yrows, key=lambda r: -(r[1] / max(1, r[2])))[:8]:
        print(f"  {n:<12} {100*i/max(1,t_):5.1f}%  ({i}/{t_})")


if __name__ == "__main__":
    main()
