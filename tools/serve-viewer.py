"""Serve the Rossville map locally, and tell the page when it has been rebuilt.

WHY THIS EXISTS. The page is a validation tool, so looking at a stale copy of it is the one
failure that matters: every figure on it is computed and correct, and none of that helps if the
browser is showing the build before last. Opening it as a file:// meant refreshing by hand and
remembering to, and the attempts to do that refresh FOR the owner were worse than useless -
SetForegroundWindow fails silently from a background process, so the Ctrl+F5 went to whichever
window happened to have focus rather than to the browser.

So: the page asks. `/__version` returns the mtime of the page itself, the page polls it every
couple of seconds, and reloads when it changes. Rebuild the map and the browser follows within
about two seconds without anybody touching anything.

    python tools/serve-viewer.py            # then open http://127.0.0.1:8750/

Bound to the loopback address only. Nothing here should be reachable from anywhere else: the
page carries the real addresses of a real town.
"""
import http.server
import json
import os
import socketserver
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.join(HERE, "..", "docs")
PAGE = "rossville-buildings.html"
def _port_from_argv():
    """The port, if one was given AS a port.

    Guarded because this module is imported by other tools for its read/write of the rulings
    file, and a bare `int(sys.argv[1])` read THEIR flags: `merge-back-strips.py --apply` died on
    import with "invalid literal for int: '--apply'". A module that does work at import time has
    to cope with not being the program.
    """
    for a in sys.argv[1:]:
        if a.isdigit():
            return int(a)
    return 8750


PORT = _port_from_argv()


VERDICTS = os.path.join(HERE, "..", "Content", "parcel-1991.txt")

VALID = {"built", "vacant", "unsure", "absent"}

HEADER = """# ============================================================================
#  WHAT STOOD ON THIS LOT IN 1991 - THE OWNER'S OWN RULING
#
#  AUTHORED, not derived. Everything else about these lots is measured: the county's
#  tax roll, the federal imagery, the parcel geometry. None of them can answer this
#  one. The earliest tax year Vermilion County publishes is 2007 and the imagery is
#  2016, and the game is set around 2000 - so where the sources disagree about what
#  was on a lot, the tie is broken by somebody who was there.
#
#  Written by the browser map (docs/rossville-buildings.html, served by
#  tools/serve-viewer.py): click a lot, say what was there, and it lands here.
#  Hand-editing is fine - the file is read back on the next load.
#
#  THIS FILE IS THE ONE THING HERE NO RE-RUN CAN REBUILD. seat-buildings.py,
#  build-roads.py and the rest can all be regenerated from the downloads; a
#  recollection cannot. Do not let a tool overwrite it.
#
#  FIELDS, one per line, keyed by parcel id
#    parcel <id> was built|vacant|unsure|absent
#      built   a building stood here in 1991
#      vacant  the lot was there and nothing stood on it
#      absent  THERE WAS NO SUCH LOT IN 1991, which is not the same as vacant: the
#              parcel itself did not exist. The county's boundaries are TODAY'S, and
#              ground subdivided out of a field in 1998 has no business on a map of
#              1991. A lot marked absent stops being drawn as a lot.
#      unsure  looked at, and not settled
#    parcel <id> kind <word>        what it was. The game's own place kinds where one
#                                   fits - school, church, shop, dwelling, elevator -
#                                   see Content/kinds.txt. Free text is allowed and is
#                                   the right answer when nothing in that list is what
#                                   was actually there.
#    parcel <id> property "<name>"  WHICH PROPERTY THIS LOT IS PART OF, and this is how
#                                   several lots become one building. The county splits
#                                   ground for its own reasons: the grade school stands
#                                   on parcels 719, 718 and 590, which is three lots and
#                                   one school. Give them the same property name and
#                                   they are the same property. There is no group id to
#                                   keep in step - the name IS the grouping.
#    parcel <id> note "<text>"      anything else worth saying
#
#  Parcel id is the line number in parcels.txt, 0-based, comments and blanks not
#  counted - the same key parcel-county.txt and parcel-buildings.txt use.
# ============================================================================
"""


def read_verdicts():
    out = {}
    try:
        with open(VERDICTS, encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                parts = line.split(" ", 3)
                if len(parts) < 4 or parts[0] != "parcel":
                    continue
                try:
                    pid = int(parts[1])
                except ValueError:
                    continue
                key, val = parts[2], parts[3].strip()
                if val.startswith('"') and val.endswith('"') and len(val) >= 2:
                    val = val[1:-1]
                e = out.setdefault(pid, {})
                if key in ("was", "kind", "property", "note"):
                    e[key] = val
    except OSError:
        pass
    return out


def write_verdicts(v):
    """Whole file, written beside and swapped - the same care ParcelNotes takes, and for the
    same reason: this is the only file here that cannot be rebuilt from anything."""
    def q(t):
        clean = str(t).replace('"', "'").replace("\n", " ").replace("\r", " ").strip()
        return '"' + clean + '"'

    lines = [HEADER]
    for pid in sorted(v):
        e = v[pid]
        if not e.get("was"):
            continue
        lines.append(f'parcel {pid} was {e["was"]}')
        if e.get("kind"):
            lines.append(f'parcel {pid} kind {str(e["kind"]).strip().replace(" ", "-")}')
        if e.get("property"):
            lines.append(f'parcel {pid} property {q(e["property"])}')
        if e.get("note"):
            lines.append(f'parcel {pid} note {q(e["note"])}')
        lines.append("")
    body = "\n".join(lines) + "\n"
    tmp = VERDICTS + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(body)
    os.replace(tmp, VERDICTS)


# ---- where the sidewalks were -------------------------------------------------------------
#
# The same kind of fact as the 1991 rulings and kept the same way: authored, never derived, whole
# file written beside and swapped. Keyed by the road's NAME rather than by a line number, because
# the survey splits some streets into two runs - Harrison is two lines and one street, and a walk
# is a property of the street.

WALKS = os.path.join(HERE, "..", "Content", "roads-1991.txt")

WALK_SIDES = {"none", "both", "north", "south", "east", "west"}

WALK_HEADER = """# ============================================================================
#  WHERE THE SIDEWALKS WERE - THE OWNER'S OWN RULING
#
#  AUTHORED, not derived, and the same kind of fact as Content/parcel-1991.txt: no
#  survey, tax roll or aerial layer in this project records which Rossville streets
#  had a walk in 1991. OpenStreetMap does not tag them for a village this size, the
#  federal imagery is 2016 and shows the town twenty-five years too late, and the
#  county measured the right of way but not what was laid in it.
#
#  Written by the browser map (docs/rossville-buildings.html, served by
#  tools/serve-viewer.py): click a street, say which side. Hand-editing is fine - the
#  file is read back on the next load.
#
#  WHY THIS FILE CAN BE ACTED ON. Content/roads.txt carries a measured right of way for
#  all 66 roads. A street paves a 10 m corridor down the middle of a 20 m easement, so
#  five metres each side is public ground that is NOT road. The walk has somewhere to go
#  the moment this file says it exists.
#
#  FIELDS, one per line, keyed by the road's name in roads.txt
#    road <name> walk none            no sidewalk on either side
#    road <name> walk both            one on each side
#    road <name> walk north|south     an east-west street with a walk on that side only
#    road <name> walk east|west       a north-south street with a walk on that side only
#
#  A road with no line here has not been ruled on yet, which is not the same as `none`.
#
#  THE ALLEYS ARE RULED BY MEASUREMENT, not by memory: 4 m of track in 4 to 8 m of
#  ground leaves no room for a walk on any of the 33.
# ============================================================================
"""


def read_walks():
    out = {}
    try:
        with open(WALKS, encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                parts = line.split()
                if len(parts) != 4 or parts[0] != "road" or parts[2] != "walk":
                    continue
                if parts[3] in WALK_SIDES:
                    out[parts[1]] = parts[3]
    except OSError:
        pass
    return out


def write_walks(w):
    lines = [WALK_HEADER]
    for name in sorted(w):
        if w[name] in WALK_SIDES:
            lines.append(f"road {name} walk {w[name]}")
    body = "\n".join(lines) + "\n"
    tmp = WALKS + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(body)
    os.replace(tmp, WALKS)


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=DOCS, **kw)

    def _json(self, obj, code=200):
        body = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        if self.path.startswith("/__walk"):
            self._walk()
            return
        if not self.path.startswith("/__verdict"):
            self.send_error(404)
            return
        try:
            n = int(self.headers.get("Content-Length") or 0)
            data = json.loads(self.rfile.read(n) or b"{}")
            pid = int(data["parcel"])
            was = str(data.get("was", "")).strip()
            if was and was not in VALID:
                raise ValueError(f"unknown verdict {was!r}")
        except Exception as e:
            self._json({"ok": False, "error": str(e)}, 400)
            return

        v = read_verdicts()
        if was:
            v[pid] = {
                "was": was,
                "kind": str(data.get("kind", "")).strip(),
                "property": str(data.get("property", "")).strip(),
                "note": str(data.get("note", "")).strip(),
            }
        else:
            v.pop(pid, None)          # empty verdict clears the lot again
        write_verdicts(v)
        print(f"  [1991] parcel {pid} -> {was or 'cleared'}"
              + (f' {data.get("kind","")}' if was else "")
              + (f' / {data.get("property","")}' if data.get("property") else ""))
        self._json({"ok": True, "count": len(v)})

    def _walk(self):
        """One street's sidewalk, saved. An empty side clears the road back to unruled, which is
        not the same as ruling it `none` - see the header of Content/roads-1991.txt."""
        try:
            n = int(self.headers.get("Content-Length") or 0)
            data = json.loads(self.rfile.read(n) or b"{}")
            road = str(data["road"]).strip()
            side = str(data.get("walk", "")).strip()
            if not road:
                raise ValueError("no road named")
            if side and side not in WALK_SIDES:
                raise ValueError(f"unknown side {side!r}")
        except Exception as e:
            self._json({"ok": False, "error": str(e)}, 400)
            return

        w = read_walks()
        if side:
            w[road] = side
        else:
            w.pop(road, None)
        write_walks(w)
        print(f"  [walk] {road} -> {side or 'unruled'}")
        self._json({"ok": True, "count": len(w)})

    def do_GET(self):
        if self.path.startswith("/__walks"):
            self._json(read_walks())
            return
        if self.path.startswith("/__verdicts"):
            self._json({str(k): v for k, v in read_verdicts().items()})
            return
        if self.path.startswith("/__version"):
            # TWO STAMPS: the page, and the rulings. They mean different things to the browser -
            # a rebuilt page has to be reloaded, whereas rulings changed on disk only need
            # re-fetching, which keeps the view and the scroll exactly where they were. Tools
            # that edit the rulings file (merge-back-strips.py) used to change it under an open
            # map with no way for the map to find out.
            def mtime(path):
                try:
                    return os.path.getmtime(path)
                except OSError:
                    return 0
            body = f"{mtime(os.path.join(DOCS, PAGE)):.0f}:{mtime(VERDICTS):.0f}".encode()
            self.send_response(200)
            self.send_header("Content-Type", "text/plain")
            self.send_header("Content-Length", str(len(body)))
            # The whole point is to notice a change, so this answer must never be cached.
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
            self.end_headers()
            self.wfile.write(body)
            return
        super().do_GET()

    def end_headers(self):
        # Same reason: a rebuilt page that the browser serves from its own cache is exactly the
        # staleness this is here to prevent.
        if self.path.endswith(".html") or self.path == "/":
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
        super().end_headers()

    def log_message(self, fmt, *args):
        # STRINGIFY BEFORE TESTING. These arguments are not all strings - an error log passes an
        # HTTPStatus, so `"__version" in args[0]` raised TypeError on the first 404 the browser
        # asked for, which is always /favicon.ico. Written as a one-line filter, shipped as
        # "verified" because the happy path answered, and caught by reading the server's own log.
        text = " ".join(str(a) for a in args)
        if "__version" in text or "__verdicts" in text or "__walks" in text:
            return                       # a poll every two seconds would bury everything else
        super().log_message(fmt, *args)


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


if __name__ == "__main__":
    with Server(("127.0.0.1", PORT), Handler) as httpd:
        print(f"Rossville map on http://127.0.0.1:{PORT}/{PAGE}")
        print("  the page polls /__version and reloads itself when the map is rebuilt.")
        print(f"  1991 rulings are saved to Content/parcel-1991.txt "
              f"({len(read_verdicts())} so far).")
        print("  Ctrl+C to stop.")
        httpd.serve_forever()
