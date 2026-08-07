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
BLOCKS = os.path.join(HERE, "..", "Content", "road-blocks.txt")

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
#  A WALK IS A PROPERTY OF A BLOCK, not of a street. Chicago Street had walks both
#  sides through the middle of town and nothing out at the edges. So there are two
#  scopes, and the block wins:
#
#    road <name> walk <side>                the default for the whole street
#    block <road> <from> <to> walk <side>   this block only, overriding the street
#
#  <side> is none | both | north | south | east | west. North and south are for an
#  east-west street, east and west for a north-south one.
#
#  from/to name the crossing streets, or `end` where the road runs out - see
#  Content/road-blocks.txt, which is derived and lists every block. Named by its cross
#  streets rather than by a number so that re-surveying the roads cannot silently move
#  a ruling onto a different piece of road.
#
#  Saying it street-wide and correcting the odd block is a couple of clicks; saying it
#  one block at a time is 137.
#
#  A road with no line here has not been ruled on yet, which is not the same as `none`.
#
#  THE ALLEYS ARE RULED BY MEASUREMENT, not by memory: 4 m of track in 4 to 8 m of
#  ground leaves no room for a walk on any of the 33.
# ============================================================================
"""


def read_walks():
    """{"roads": {name: side}, "blocks": {"road|from|to": side}}.

    TWO SCOPES, because a walk in Rossville is a property of a BLOCK and saying so one block at a
    time would be 137 clicks. The road line is the default for the whole street; a block line
    overrides it for that block. Most streets are one click and a couple of exceptions."""
    out = {"roads": {}, "blocks": {}}
    try:
        with open(WALKS, encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                p = line.split()
                if len(p) == 4 and p[0] == "road" and p[2] == "walk" and p[3] in WALK_SIDES:
                    out["roads"][p[1]] = p[3]
                elif len(p) == 6 and p[0] == "block" and p[4] == "walk" and p[5] in WALK_SIDES:
                    out["blocks"][f"{p[1]}|{p[2]}|{p[3]}"] = p[5]
    except OSError:
        pass
    return out


def write_walks(w):
    lines = [WALK_HEADER]
    for name in sorted(w.get("roads", {})):
        if w["roads"][name] in WALK_SIDES:
            lines.append(f"road {name} walk {w['roads'][name]}")
    if w.get("blocks"):
        lines.append("")
        lines.append("#  ---- blocks that differ from their street ----")
        for key in sorted(w["blocks"]):
            side = w["blocks"][key]
            bits = key.split("|")
            if len(bits) == 3 and side in WALK_SIDES:
                lines.append(f"block {bits[0]} {bits[1]} {bits[2]} walk {side}")
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
        if self.path.startswith("/__publish"):
            self._publish()
            return
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

    def _publish(self):
        """Hand everything ruled here to the game, and say what it will make of it.

        WHY A BUTTON AT ALL, when the game reads these files directly. Two reasons, and neither is
        ceremony. Some of what the game needs is DERIVED from what was just ruled - cut the roads
        into blocks again, or a street renamed in the tool leaves rulings pointing at blocks that
        no longer exist - and that has to be regenerated by something. And the owner rules ten
        lots and wants to know they landed: a count read back is the difference between believing
        it worked and knowing.

        It does NOT touch the authored files. Everything here was already saved, one Save at a
        time; this regenerates what hangs off them and reports."""
        notes, problems = [], []

        # Blocks are arithmetic on roads.txt, and a ruling names its block by the streets that
        # bound it - so if the roads have moved, the blocks have to be recut before anything can
        # say whether a ruling still points at real ground.
        try:
            import subprocess
            r = subprocess.run(
                [sys.executable, os.path.join(HERE, "build-road-blocks.py"), "--write"],
                capture_output=True, text=True, timeout=120)
            if r.returncode != 0:
                problems.append("could not recut the blocks: " + (r.stderr or "").strip()[:200])
            else:
                for line in (r.stdout or "").splitlines():
                    if line.startswith("blocks:"):
                        notes.append(line.strip())
        except Exception as e:
            problems.append("could not recut the blocks: %s" % e)

        verdicts = read_verdicts()
        walks = read_walks()
        blocks = set()
        try:
            with open(BLOCKS, encoding="utf-8") as fh:
                for line in fh:
                    p = line.split()
                    if len(p) == 6 and p[0] == "block":
                        blocks.add("%s|%s|%s" % (p[1], p[2], p[3]))
        except OSError:
            pass

        # A ruling pointing at a block that no longer exists is the one way this can rot quietly.
        orphans = [k for k in walks["blocks"] if k not in blocks]
        for k in orphans:
            problems.append("no block called %s any more - that ruling will be ignored"
                            % k.replace("|", " "))

        built = sum(1 for v in verdicts.values() if v.get("was") == "built")
        absent = sum(1 for v in verdicts.values() if v.get("was") == "absent")
        props = len({v["property"] for v in verdicts.values() if v.get("property")})

        print("  [publish] %d lots, %d roads, %d blocks%s"
              % (len(verdicts), len(walks["roads"]), len(walks["blocks"]),
                 ", %d PROBLEM(S)" % len(problems) if problems else ""))

        self._json({
            "ok": not problems,
            "lots": len(verdicts), "built": built, "absent": absent, "properties": props,
            "streets": len(walks["roads"]), "blocks": len(walks["blocks"]),
            "notes": notes, "problems": problems,
        })

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

        frm = str(data.get("from", "")).strip()
        to = str(data.get("to", "")).strip()

        w = read_walks()
        if frm and to:
            key = f"{road}|{frm}|{to}"
            if side:
                w["blocks"][key] = side
            else:
                w["blocks"].pop(key, None)
            what = f"{road} {frm}..{to}"
        else:
            if side:
                w["roads"][road] = side
            else:
                w["roads"].pop(road, None)
            what = f"{road} (whole street)"

        write_walks(w)
        print(f"  [walk] {what} -> {side or 'unruled'}")
        self._json({"ok": True,
                    "roads": len(w["roads"]), "blocks": len(w["blocks"])})

    def do_GET(self):
        if self.path.startswith("/__blocks"):
            # DERIVED, and served rather than baked into the page: re-running
            # build-road-blocks.py after a re-survey should change what the browser offers
            # without anybody having to rebuild the page around it.
            out = []
            try:
                with open(BLOCKS, encoding="utf-8") as fh:
                    for line in fh:
                        p = line.split()
                        if len(p) == 6 and p[0] == "block":
                            out.append({"road": p[1], "from": p[2], "to": p[3],
                                        "a": float(p[4]), "b": float(p[5])})
            except OSError:
                pass
            self._json(out)
            return
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
        if ("__version" in text or "__verdicts" in text
                or "__walks" in text or "__blocks" in text):
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
