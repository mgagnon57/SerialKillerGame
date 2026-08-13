"""Bundle everything the browser view needs into one JSON blob.

Five layers, all in village metres so they overlay without any further conversion:
the county's lot lines, what the county records about each lot, the newly seated building
footprints, the roads, and the buildings the game currently draws - which is the comparison
the whole page exists to make.
"""
import json, os, re, importlib.util, datetime

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(HERE, "..", "Content")


def parcels():
    out = []
    with open(os.path.join(CONTENT, "parcels.txt"), encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            out.append([[round(float(v), 1) for v in tok.split(",")] for tok in line.split()])
    return out


def county():
    rec = {}
    with open(os.path.join(CONTENT, "parcel-county.txt"), encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line.startswith("parcel "):
                continue
            p = line.split(" ", 3)
            pid = int(p[1])
            e = rec.setdefault(pid, {})
            key = p[2]
            val = p[3] if len(p) > 3 else ""
            if key == "address":
                e["addr"] = val.strip('"')
            elif key == "pin":
                e["pin"] = val.strip('"')
            elif key == "class":
                m = re.match(r'(\S+)\s+"(.*)"', val)
                if m:
                    e["cls"], e["clsname"] = m.group(1), m.group(2)
            elif key == "occupancy":
                e["occ"] = val
            elif key == "over65":
                e["o65"] = 1
            elif key == "assessed":
                n = val.split()
                e["home"], e["dwell"] = int(n[0]), int(n[1])
            elif key == "acres":
                e["acres"] = float(val)
    return rec


def buildings():
    d = json.load(open(os.path.join(HERE, "rossville-buildings-seated.json"), encoding="utf-8"))
    out = []
    # WHICH BUILDING ON ITS LOT, counted the way Content/parcel-buildings.txt numbers them: the
    # records arrive largest-first per parcel, 0 downwards. The placement overlay is keyed
    # "<parcel> <index>", so without this the page can name a lot but not a house on it.
    seen = {}
    for b in d["buildings"]:
        idx = seen.get(b["parcel"], 0)
        seen[b["parcel"]] = idx + 1
        out.append({
            "i": idx,
            "p": b["parcel"],
            "r": [[round(x, 1), round(y, 1)] for x, y in b["ring"]],
            "a": round((b["sqft"] or 0) * 0.09290304, 1),
            "sf": round(b["sqft"] or 0),
            "role": b["role"],
            "skew": round(b.get("skew", 0.0), 1),
            "bskew": round(b.get("blockskew", b.get("skew", 0.0)), 1),
            "occ": b["occ"],
            "prim": b["prim"],
            "addr": b["addr"] or "",
        })
    return out


def roads_and_places():
    roads, places = [], []
    with open(os.path.join(CONTENT, "city.txt"), encoding="utf-8") as fh:
        for line in fh:
            line = line.split("#")[0].strip()
            if line.startswith("road "):
                p = line.split()
                pts = [[round(float(v), 1) for v in t.split(",")] for t in p[3:]]
                roads.append({"n": p[1], "w": float(p[2]), "pts": pts})
            elif line.startswith("place "):
                m = re.match(r'place\s+(\S+)\s+(-?[\d.]+),(-?[\d.]+)\s+(\d+)x(\d+)(?:\s+"(.*)")?', line)
                if m:
                    places.append({"k": m.group(1), "x": float(m.group(2)), "y": float(m.group(3)),
                                   "w": int(m.group(4)), "h": int(m.group(5)),
                                   "n": m.group(6) or ""})
    return roads, places


def proposed_roads():
    """The survey-derived network from build-roads.py, so the page can show it against the
    ruled straight lines it replaces."""
    path = os.path.join(HERE, "roads-proposed.json")
    if not os.path.exists(path):
        return []
    return json.load(open(path, encoding="utf-8"))


def _stats_module():
    spec = importlib.util.spec_from_file_location("viewer_stats",
                                                  os.path.join(HERE, "viewer-stats.py"))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def main():
    r, pl = roads_and_places()

    # `r` AND `pl` USED TO BE COMPUTED AND THEN DROPPED ON THE FLOOR. The next line took neither,
    # so the viewer's whole "here are the ruled lines the survey replaces" comparison was parsed,
    # built and discarded - and `road_scores` below was handed an empty list, which scores nothing
    # and reports it as though it had. The function was working; nobody was reading its answer.
    bundle = {"parcels": parcels(), "county": county(), "buildings": buildings(),
              "newroads": proposed_roads(), "roads": r, "places": pl}

    # EVERY FIGURE THE PAGE SHOWS IS COMPUTED HERE, off the very arrays it is about to draw.
    # The page is a validation tool, so a hand-typed number on it is worse than none: it reads
    # as a measurement and goes stale silently. It did - "96.9%" and "53 ft" were both true when
    # typed and both wrong an hour later, after the registration fix moved every footprint.
    vs = _stats_module()
    st = vs.compute(bundle["parcels"], bundle["county"], bundle["buildings"],
                    r, bundle["newroads"])
    st.update(vs.road_scores(bundle["parcels"], r, bundle["newroads"]))
    bundle["disagree"] = vs.disagreements(bundle["parcels"], bundle["county"], bundle["buildings"])
    import collections as _c
    st["disagree_counts"] = dict(_c.Counter(bundle["disagree"].values()))
    st["built"] = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")
    bundle["stats"] = st
    print("  computed: " + ", ".join(f"{k}={st[k]}" for k in
          ("buildings", "lots_built", "inside_own_lot_pct", "addr_pct", "setback_ft",
           "old_pct", "new_pct") if k in st))
    blob = json.dumps(bundle, separators=(",", ":"))
    print(f"parcels {len(bundle['parcels'])}  county {len(bundle['county'])}  "
          f"buildings {len(bundle['buildings'])}  roads {len(bundle['newroads'])}")

    # One self-contained file. The page has to open off the filesystem with no server and no
    # network - the town's addresses are not going anywhere that would cache them.
    with open(os.path.join(HERE, "viewer-template.html"), encoding="utf-8") as fh:
        html = fh.read()
    if "/*__DATA__*/" not in html:
        raise SystemExit("template has lost its /*__DATA__*/ marker")
    html = html.replace("/*__DATA__*/ null", blob)

    out = os.path.join(HERE, "..", "docs", "rossville-buildings.html")
    with open(out, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(html)
    print(f"wrote docs/rossville-buildings.html  ({os.path.getsize(out)/1024:.0f} KB)")


if __name__ == "__main__":
    main()
