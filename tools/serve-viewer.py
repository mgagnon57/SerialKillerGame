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
import shutil
import socketserver
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import change_gate      # noqa: E402  - which edits need the game re-checked, and which do not
import verify_run       # noqa: E402  - and the run itself, when one is needed
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
#  2016, and THE GAME IS SET IN 1991 - so every measured source here postdates the year
#  it is being used to describe, by between sixteen and twenty-five years. Where they
#  disagree about what was on a lot, the tie is broken by somebody who was there.
#
#  The year is decided in docs/research/THE-ERA.md and nowhere else. This header said
#  "around 2000" until 2026-08-07, which is not a rounding of 1991 - it is the far side
#  of the 2004 fire, and it was being written into the top of this file every time
#  anybody saved a lot.
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
#    parcel <id> footprint later    A BUILDING STOOD HERE IN 1991, BUT THE MEASURED SHAPE
#                                   IS NOT IT. The footprints are 2016 imagery; where a lot
#                                   has been cleared and rebuilt since, seating that shape
#                                   puts the wrong decade's building on the right ground.
#                                   112 S Chicago is the case: a row of antique shops and a
#                                   restaurant stood there in 1991, burned in Feb 2004, and
#                                   a Casey's was built on the cleared site. `vacant` is the
#                                   WRONG word for it - that erases the row instead of
#                                   correcting it. The lot was built on; it is the SHAPE
#                                   that postdates 1991, and the two facts need saying
#                                   separately. The game then skips the footprint and raises
#                                   a building by rule instead: a rule is a better guess
#                                   about 1991 than a photograph of 2016.
#    parcel <id> note "<text>"      anything else worth saying
#
#  Parcel id is the line number in parcels.txt, 0-based, comments and blanks not
#  counted - the same key parcel-county.txt and parcel-buildings.txt use.
# ============================================================================
"""


#: The verbs this server knows how to format on the way out. Anything else read from the file is
#: still KEPT and written back verbatim - see read_verdicts.
KNOWN_VERBS = ("was", "kind", "property", "note", "footprint")


def backup(path, tag):
    """Copy a file aside before rewriting it, NEVER overwriting an earlier backup.

    Every tool here used `shutil.copy2(path, path + ".before-" + tag)` with a fixed name, which
    is a backup that protects you exactly once. Run the tool a second time and the "backup" it
    takes is of the ALREADY-MODIFIED file, so the original - the thing you actually wanted back -
    is gone, overwritten by the safety mechanism that existed to preserve it. The second run is
    also precisely when you want it most, because the first run is what made you look.

    So: the first backup keeps the plain name, and every later one gets -2, -3, and so on. Nothing
    here ever deletes; disk is cheap and Content/parcel-1991.txt cannot be rebuilt from anything.
    """
    base = f"{path}.before-{tag}"
    dest = base
    n = 2
    while os.path.exists(dest):
        dest = f"{base}-{n}"
        n += 1
    shutil.copy2(path, dest)
    return dest


def read_verdicts():
    """Every ruling in the file, keyed by parcel.

    RAISES on any read failure except the file simply not being there. That distinction is the
    whole point and it used to be `except OSError: pass`, which returned {} - and since the POST
    handler does `v = read_verdicts()` and then `write_verdicts(v)`, ONE unreadable read rewrote
    the file from an empty dict and deleted every ruling in it. Atomically, too. A locked file, a
    permissions blip or a bad sector was all it would have taken to destroy the one file in this
    project that nothing can rebuild.

    A missing file is different in kind from an unreadable one: it means nobody has ruled on
    anything yet, which is a real state on a fresh clone.
    """
    out = {}
    try:
        fh = open(VERDICTS, encoding="utf-8")
    except FileNotFoundError:
        return out
    with fh:
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
            # EVERY VERB IS KEPT, known or not. This used to be an allowlist, and because
            # write_verdicts rewrites the whole file from what this read, a verb missing from that
            # list was not ignored - it was DELETED, silently, the next time anybody clicked any
            # lot. `footprint` was hand-written into the file and survived only because somebody
            # noticed and added it here in time. Hand-editing this file is supposed to be safe;
            # its own header says so. An allowlist is what made that untrue.
            out.setdefault(pid, {})[key] = val
    return out


def write_verdicts(v):
    """Whole file, written beside and swapped - the same care ParcelNotes takes, and for the
    same reason: this is the only file here that cannot be rebuilt from anything."""
    def q(t):
        clean = str(t).replace('"', "'").replace("\n", " ").replace("\r", " ").strip()
        return '"' + clean + '"'

    lines = [HEADER]
    written = 0
    for pid in sorted(v):
        e = v[pid]
        if not any(str(x).strip() for x in e.values()):
            continue
        if e.get("was"):
            lines.append(f'parcel {pid} was {e["was"]}')
        if e.get("kind"):
            lines.append(f'parcel {pid} kind {str(e["kind"]).strip().replace(" ", "-")}')
        if e.get("property"):
            lines.append(f'parcel {pid} property {q(e["property"])}')
        # `footprint later`: the lot was built on in 1991 but the MEASURED shape postdates it, so
        # the game must not seat it - see Rulings.Ruling.FootprintIsLater. Written unquoted because
        # it is a keyword and not free text.
        if e.get("footprint"):
            lines.append(f'parcel {pid} footprint {str(e["footprint"]).strip().split()[0]}')
        if e.get("note"):
            lines.append(f'parcel {pid} note {q(e["note"])}')
        # Anything this server does not know about, written back as it came in. A verb added to
        # the file by hand or by a later tool must survive a click on an unrelated lot.
        for key in sorted(e):
            if key in KNOWN_VERBS:
                continue
            if str(e[key]).strip():
                lines.append(f'parcel {pid} {key} {q(e[key])}')
        lines.append("")
        written += 1

    # THE CLIFF GUARD. Everything above is careful, and careful was not enough twice already, so
    # this is the backstop that does not depend on having foreseen the failure: count what is on
    # disk, and refuse to replace it with dramatically less. It costs one read of a small file and
    # it is the difference between losing an afternoon and losing the only copy of somebody's
    # recollection of a town in 1991.
    #
    # Deliberately not a clamp or a warning. If this fires, something upstream is wrong and the
    # right move is to stop and look, not to write "probably fine" over the evidence.
    on_disk = _count_ruled_parcels()
    if on_disk >= 10 and written < on_disk * 0.9:
        raise RuntimeError(
            f"REFUSING TO WRITE {VERDICTS}: it holds {on_disk} ruled parcels and this write "
            f"would leave {written}. That is a loss of {on_disk - written}, which is not what "
            f"saving one lot looks like. Nothing has been changed on disk. Look at what called "
            f"this before overriding it - this file cannot be rebuilt from anything.")

    body = "\n".join(lines) + "\n"
    tmp = VERDICTS + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(body)
    os.replace(tmp, VERDICTS)


def _count_ruled_parcels():
    """How many distinct parcels the file on disk currently rules on. Zero if it is not there."""
    seen = set()
    try:
        with open(VERDICTS, encoding="utf-8") as fh:
            for line in fh:
                p = line.split()
                if len(p) >= 3 and p[0] == "parcel":
                    try:
                        seen.add(int(p[1]))
                    except ValueError:
                        pass
    except FileNotFoundError:
        return 0
    return len(seen)


# ---- where the sidewalks were -------------------------------------------------------------
#
# The same kind of fact as the 1991 rulings and kept the same way: authored, never derived, whole
# file written beside and swapped. Keyed by the road's NAME rather than by a line number, because
# the survey splits some streets into two runs - Harrison is two lines and one street, and a walk
# is a property of the street.

WALKS = os.path.join(HERE, "..", "Content", "roads-1991.txt")
BLOCKS = os.path.join(HERE, "..", "Content", "road-blocks.txt")

WALK_SIDES = {"none", "both", "north", "south", "east", "west"}

#: Where the owner has moved or turned a building, over where the survey measured it. An OVERLAY
#: on Content/parcel-buildings.txt and never an edit to it - see that file's header and
#: Assets/Noir/Unity/Placements.cs. Keyed "<parcel>|<index>".
PLACES = os.path.join(HERE, "..", "Content", "placement-1991.txt")


def read_places():
    out = {}
    try:
        with open(PLACES, encoding="utf-8") as fh:
            for line in fh:
                p = line.split()
                # building <parcel> <index> move <dx> <dy> turn <deg>  -  eight tokens, not nine
                if len(p) == 8 and p[0] == "building" and p[3] == "move" and p[6] == "turn":
                    try:
                        out["%s|%s" % (p[1], p[2])] = {
                            "dx": float(p[4]), "dy": float(p[5]), "turn": float(p[7])}
                    except ValueError:
                        continue
    except FileNotFoundError:
        # NOT `except OSError`. A missing file means nobody has moved a building yet, which is a
        # real state on a fresh clone. An unreadable one - locked, permissions, a bad sector -
        # must RAISE, because the POST handler reads this, edits it and writes it straight back:
        # returning {} here rewrites the file from nothing. That is the exact fault read_verdicts
        # documents as already having destroyed data once.
        pass
    return out


def write_places(places):
    """Rewrite the overlay from scratch, header and all.

    Sorted by lot then by building so a diff of this file reads as a list of houses rather than as
    the order somebody happened to click them in.
    """
    lines = [_places_header()]
    for key in sorted(places, key=lambda k: (int(k.split("|")[0]), int(k.split("|")[1]))):
        v = places[key]
        pid, idx = key.split("|")
        lines.append("building %s %s move %+.2f %+.2f turn %+.2f"
                     % (pid, idx, v["dx"], v["dy"], v["turn"]))
    # THE CLIFF GUARD, as on write_verdicts. Saving one building does not remove most of the
    # others, so a write that would is a symptom of something upstream having gone wrong - and
    # the right move is to stop and let somebody look, not to write "probably fine" over the
    # evidence. These are hand-made adjustments and no script regenerates them.
    on_disk = _count_lines_starting(PLACES, "building ")
    if on_disk >= 10 and len(places) < on_disk * 0.9:
        raise RuntimeError(
            f"REFUSING TO WRITE {PLACES}: it holds {on_disk} placed buildings and this write "
            f"would leave {len(places)}. That is a loss of {on_disk - len(places)}, which is not "
            f"what saving one building looks like. Nothing has been changed on disk.")

    body = "\n".join(lines) + "\n"

    tmp = PLACES + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(body)
    os.replace(tmp, PLACES)          # atomic: a half-written overlay would lose adjustments


def _count_lines_starting(path, prefix):
    """How many real records the file on disk holds, for the cliff guards.

    Counted from the file rather than from anything in memory, because the whole point is to
    catch the case where what is in memory is wrong."""
    try:
        with open(path, encoding="utf-8") as fh:
            return sum(1 for line in fh if line.lstrip().startswith(prefix))
    except FileNotFoundError:
        return 0


def _places_header():
    """Keep whatever header the file already carries, so the explanation in it is never lost to a
    save. Falls back to a minimal one if the file has been deleted."""
    try:
        with open(PLACES, encoding="utf-8") as fh:
            head = []
            for line in fh:
                if line.startswith("#"):
                    head.append(line.rstrip("\n"))
                elif line.strip() == "":
                    continue
                else:
                    break
            if head:
                return "\n".join(head)
    except OSError:
        pass
    return ("# Where the owner put the building, over where the survey measured it.\n"
            "#     building <parcelId> <index> move <dx> <dy> turn <degrees>")

# Whether the road was there at all - the same act as ruling a lot absent, and for the same
# reason: roads.txt is the county's network as it stands TODAY, and a street cut through in 1998
# has no business on a map of 1991.
WAS_VALUES = {"built", "absent"}

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
#  WAS IT THERE AT ALL. Some roads were extended after 1991 and only the new end should come
#  off, so this takes a block as readily as a whole street - and the block wins.
#
#    road <name> was absent|built           the whole street, gone from the map
#    block <road> <from> <to> was absent    this stretch only; the run is cut, not dropped
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
    out = {"roads": {}, "blocks": {}, "was": {}, "wasBlocks": {}}
    try:
        with open(WALKS, encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                p = line.split()
                if len(p) == 4 and p[0] == "road" and p[2] == "walk" and p[3] in WALK_SIDES:
                    out["roads"][p[1]] = p[3]
                elif len(p) == 4 and p[0] == "road" and p[2] == "was" and p[3] in WAS_VALUES:
                    out["was"][p[1]] = p[3]
                elif len(p) == 6 and p[0] == "block" and p[4] == "walk" and p[5] in WALK_SIDES:
                    out["blocks"][f"{p[1]}|{p[2]}|{p[3]}"] = p[5]
                elif len(p) == 6 and p[0] == "block" and p[4] == "was" and p[5] in WAS_VALUES:
                    out["wasBlocks"][f"{p[1]}|{p[2]}|{p[3]}"] = p[5]
    except FileNotFoundError:
        # See read_places. A missing file is a fresh clone; an unreadable one must raise.
        pass
    return out


def write_walks(w):
    lines = [WALK_HEADER]

    # The roads that were not there at all come first: it is the bigger statement, and a street
    # ruled absent makes every other ruling about it moot.
    if w.get("was") or w.get("wasBlocks"):
        lines.append("#  ---- roads and stretches that did not exist in 1991 ----")
        for name in sorted(w.get("was", {})):
            if w["was"][name] in WAS_VALUES:
                lines.append(f"road {name} was {w['was'][name]}")
        for key in sorted(w.get("wasBlocks", {})):
            bits = key.split("|")
            if len(bits) == 3 and w["wasBlocks"][key] in WAS_VALUES:
                lines.append(f"block {bits[0]} {bits[1]} {bits[2]} was {w['wasBlocks'][key]}")
        lines.append("")

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
    # THE CLIFF GUARD, as on write_verdicts and write_places. Every `road` and `block` line here
    # is a ruling the owner made by hand about a street that existed in 1991 and which side of it
    # was walked; nothing regenerates them. Saving one street does not remove the others.
    records = sum(1 for L in lines if L.startswith("road ") or L.startswith("block "))
    on_disk = _count_lines_starting(WALKS, "road ") + _count_lines_starting(WALKS, "block ")
    if on_disk >= 10 and records < on_disk * 0.9:
        raise RuntimeError(
            f"REFUSING TO WRITE {WALKS}: it holds {on_disk} road and block rulings and this "
            f"write would leave {records}. That is a loss of {on_disk - records}, which is not "
            f"what saving one street looks like. Nothing has been changed on disk.")

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
        if self.path.startswith("/__place"):
            self._place()
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

        # READ FIRST, AND LET A FAILED READ STOP THE WRITE. write_verdicts rewrites the whole file
        # from whatever this returns, so "I could not read it" must never be allowed to look like
        # "it was empty". Reported as a 500 rather than swallowed: the browser shows the save
        # failing, which is the correct thing for the owner to see.
        try:
            v = read_verdicts()
        except OSError as e:
            print(f"  [1991] REFUSED to save parcel {pid}: could not read {VERDICTS}: {e}")
            self._json({"ok": False, "error": f"could not read the rulings file: {e}"}, 500)
            return

        if was:
            # `footprint` is CARRIED THROUGH rather than rebuilt. The panel does not offer it yet,
            # so a lot saved from the browser would otherwise arrive without it and the whole entry
            # is replaced here - meaning one click on an unrelated field would quietly undate a
            # footprint somebody had dated by hand. Only an explicit value in the request changes it.
            keep = v.get(pid, {}).get("footprint", "")
            v[pid] = {
                "was": was,
                "kind": str(data.get("kind", "")).strip(),
                "property": str(data.get("property", "")).strip(),
                "note": str(data.get("note", "")).strip(),
                "footprint": str(data.get("footprint", keep)).strip(),
            }
        else:
            v.pop(pid, None)          # empty verdict clears the lot again

        try:
            write_verdicts(v)
        except RuntimeError as e:
            # The cliff guard fired. Nothing was written; say so loudly in both places.
            print(f"  [1991] {e}")
            self._json({"ok": False, "error": str(e)}, 500)
            return
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
                    if len(p) == 7 and p[0] == "block":
                        blocks.add("%s|%s|%s" % (p[1], p[3], p[4]))
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

        # ---- DOES THIS CHANGE NEED THE GAME RE-CHECKED, OR JUST A PLAY PRESS? ----------------
        #
        # The owner: "if it is going to affect other pieces of the game then it should be built
        # and run through the test". Right - and the half that makes it usable is that most edits
        # do not. A sidewalk is read by the drawing and by nothing else; a road taken off the map
        # moves what is walkable and can leave a corner of town unreachable. Sitting through a
        # four-minute Unity run to move a sidewalk is how you teach somebody to skip the check on
        # the day it matters. See tools/change_gate.py for what is in which bucket and why.
        gate = change_gate.unverified()
        verify = {"needed": bool(gate["structural"]), "started": False, "why": ""}

        if gate["structural"]:
            started, why = verify_run.start(change_gate.mark_verified)
            verify["started"] = started
            verify["why"] = why
        else:
            # Nothing unchecked can affect anything but the drawing, so the verified mark can
            # move forward without a run. This is what keeps the next edit's diff small.
            change_gate.mark_verified()

        print("  [publish] %d lots, %d roads, %d blocks - %d structural, %d cosmetic%s"
              % (len(verdicts), len(walks["roads"]), len(walks["blocks"]),
                 len(gate["structural"]), len(gate["cosmetic"]),
                 ", %d PROBLEM(S)" % len(problems) if problems else ""))

        self._json({
            "ok": not problems,
            "lots": len(verdicts), "built": built, "absent": absent, "properties": props,
            "streets": len(walks["roads"]), "blocks": len(walks["blocks"]),
            "notes": notes, "problems": problems,
            "structural": gate["structural"], "cosmetic": gate["cosmetic"],
            "baseline": gate["baseline"], "verify": verify,
        })

    def _place(self):
        """One building moved or turned, saved.

        ALL ZEROES DELETES THE LINE rather than writing three noughts. "Put it back where the
        survey had it" and "leave it where the survey had it" are the same statement about the
        world, and the file should not be able to tell them apart - otherwise the overlay slowly
        fills with rows recording that nothing happened.
        """
        try:
            n = int(self.headers.get("Content-Length") or 0)
            data = json.loads(self.rfile.read(n) or b"{}")
            pid = int(data["parcel"])
            idx = int(data["index"])
            dx = float(data.get("dx", 0.0))
            dy = float(data.get("dy", 0.0))
            turn = float(data.get("turn", 0.0))
            if not all(abs(v) < 1e4 for v in (dx, dy)) or abs(turn) > 360.0:
                raise ValueError("out of range")
        except Exception as e:
            self._json({"ok": False, "error": str(e)}, 400)
            return

        places = read_places()
        key = "%d|%d" % (pid, idx)
        moved = abs(dx) > 1e-3 or abs(dy) > 1e-3 or abs(turn) > 1e-3
        if moved:
            places[key] = {"dx": dx, "dy": dy, "turn": turn}
        else:
            places.pop(key, None)
        write_places(places)

        print("  [place] lot %d building %d %s"
              % (pid, idx, ("moved %+.2f %+.2f turned %+.2f" % (dx, dy, turn)) if moved
                           else "put back where the survey had it"))
        self._json({"ok": True, "count": len(places), "moved": moved})

    def _walk(self):
        """One street's sidewalk, saved. An empty side clears the road back to unruled, which is
        not the same as ruling it `none` - see the header of Content/roads-1991.txt."""
        try:
            n = int(self.headers.get("Content-Length") or 0)
            data = json.loads(self.rfile.read(n) or b"{}")
            road = str(data["road"]).strip()
            side = str(data.get("walk", "")).strip()
            was = str(data.get("was", "")).strip()
            if not road:
                raise ValueError("no road named")
            if side and side not in WALK_SIDES:
                raise ValueError(f"unknown side {side!r}")
            if was and was not in WAS_VALUES:
                raise ValueError(f"unknown verdict {was!r}")
        except Exception as e:
            self._json({"ok": False, "error": str(e)}, 400)
            return

        frm = str(data.get("from", "")).strip()
        to = str(data.get("to", "")).strip()

        w = read_walks()

        # "was" arrives on its own, at whichever scope was asked for. SOME ROADS WERE EXTENDED
        # AFTER 1991 and only the new end should come off, so this takes a block as readily as a
        # whole street.
        if "was" in data:
            if frm and to:
                key = f"{road}|{frm}|{to}"
                if was:
                    w["wasBlocks"][key] = was
                else:
                    w["wasBlocks"].pop(key, None)
                what = f"{road} {frm}..{to}"
            else:
                if was:
                    w["was"][road] = was
                else:
                    w["was"].pop(road, None)
                what = road
            write_walks(w)
            print(f"  [walk] {what} was -> {was or 'unruled'}")
            self._json({"ok": True, "roads": len(w["roads"]), "blocks": len(w["blocks"]),
                        "gone": len([k for k, v in w["was"].items() if v == 'absent'])
                              + len([k for k, v in w["wasBlocks"].items() if v == 'absent'])})
            return

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
        self._json({"ok": True, "roads": len(w["roads"]), "blocks": len(w["blocks"]),
                    "gone": len([k for k, v in w["was"].items() if v == 'absent'])})

    def do_GET(self):
        if self.path.startswith("/__game"):
            # WHAT THE GAME DID WITH EACH RULING, written by the survey passes themselves - see
            # SurveyReport. Absent until the game has been run once, which is honest: nothing can
            # be said about what it did before it did it.
            try:
                with open(os.path.join(HERE, "game-verdict.json"), encoding="utf-8") as fh:
                    self._json(json.load(fh))
            except Exception:
                self._json({"when": None, "lots": []})
            return
        if self.path.startswith("/__blocks"):
            # DERIVED, and served rather than baked into the page: re-running
            # build-road-blocks.py after a re-survey should change what the browser offers
            # without anybody having to rebuild the page around it.
            out = []
            try:
                with open(BLOCKS, encoding="utf-8") as fh:
                    for line in fh:
                        # block <road> <line> <from> <to> <start> <end>
                        p = line.split()
                        if len(p) == 7 and p[0] == "block":
                            out.append({"road": p[1], "line": int(p[2]),
                                        "from": p[3], "to": p[4],
                                        "a": float(p[5]), "b": float(p[6])})
            except OSError:
                pass
            self._json(out)
            return
        if self.path.startswith("/__walks"):
            self._json(read_walks())
            return
        if self.path.startswith("/__places"):
            self._json(read_places())
            return
        if self.path.startswith("/__verify"):
            # How the check that publish set off is getting on. Polled, because it takes minutes
            # and an HTTP request that waits that long is a request that has already timed out.
            s = verify_run.snapshot()
            s["unverified"] = change_gate.unverified()
            self._json(s)
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
                or "__walks" in text or "__blocks" in text or "__game" in text):
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
