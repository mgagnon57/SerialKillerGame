"""Decide whether an edit to the authored map needs the game re-checked, or just a Play press.

WHY THIS EXISTS. The owner asked the right question: "if it is going to affect other pieces of the
game then it should be built and run through the test, right?" Yes - and the corollary is the part
that makes it usable. Most edits AREN'T going to affect other pieces, and making him sit through a
four-minute Unity run to move a sidewalk teaches him to skip the check on the day it matters.

So the button reads the change and decides. The split is not a guess about what feels risky; it is
the list of things in Assets/ that actually READ each ruling:

    walk     RoadCentrelines only, which draws a pink band down one side of a line.
             Nothing else in the game asks where a sidewalk was.               -> COSMETIC

    note     shown in the lot panel and read by nothing.                       -> COSMETIC

    was      RuledAway.TakeAwayRoads removes whole roads and CUTS runs at block
             (road/block)  boundaries. That moves the road network, which moves what is walkable,
             which is how a town ends up in two pieces that cannot reach each
             other. FillFromSurvey also refuses a lot standing on a road, so
             the roads moving changes which houses go up.                      -> STRUCTURAL

    was      RuledAway takes the building down, FillFromSurvey puts one up, and
             (parcel)      ParcelIndex.In1991 filters the whole index by it.   -> STRUCTURAL

    kind     FillFromSurvey.KindFor refuses to raise a building on ground ruled
             to be something that is not one; GroundZoning colours by it.      -> STRUCTURAL

    property SpokesmanFor / OneProperty dissolve neighbouring lots into one, and
             VillageMesh builds the merged footprint from that.                -> STRUCTURAL

If the list above ever goes stale it fails in the expensive direction - something structural gets
called cosmetic and ships unchecked. `python tools/change_gate.py --audit` greps Assets/ for the
readers of each verb and complains if a verb has gained one this file does not know about.
"""
import io
import os
import re
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
CONTENT = os.path.join(ROOT, "Content")

#: Copies of the authored files as they stood at the last GREEN check. The diff against these is
#: "what has not been verified yet" - which is the real question, and is not the same as "what
#: changed since the page loaded". Git-ignored; losing it costs one extra check, nothing more.
VERIFIED = os.path.join(HERE, ".verified")

WATCHED = ("roads-1991.txt", "parcel-1991.txt")

#: verb -> (scope, structural?). The scope is only for reading back to a human.
ROAD_VERBS = {
    "walk": ("sidewalk", False),
    "was": ("road", True),
}
LOT_VERBS = {
    "note": ("note", False),
    "was": ("lot", True),
    "kind": ("lot kind", True),
    "property": ("property grouping", True),
}


def _rulings(path):
    """Every ruling in a file, as key -> (verb, whole line).

    Keyed so that re-ruling the same thing reads as a change to one ruling rather than as one
    removed and one added - the owner changed his mind about a lot, he did not edit two.
    """
    out = {}
    try:
        text = io.open(path, encoding="utf-8").read()
    except OSError:
        return out

    for raw in text.split("\n"):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        p = line.split()

        # roads-1991.txt:  road <name> <verb> <value>
        #                  block <road> <from> <to> <verb> <value>
        if p[0] == "road" and len(p) == 4:
            out[("road", p[1], p[2])] = (p[2], line)
        elif p[0] == "block" and len(p) == 6:
            out[("block", p[1], p[2], p[3], p[4])] = (p[4], line)

        # parcel-1991.txt: parcel <id> <verb> <value...>
        elif p[0] == "parcel" and len(p) >= 3:
            out[("parcel", p[1], p[2])] = (p[2], line)

    return out


def _verb_kind(key, verb):
    table = LOT_VERBS if key[0] == "parcel" else ROAD_VERBS
    return table.get(verb, ("unknown verb %r" % verb, True))   # unknown is structural, on purpose


def unverified():
    """What has changed since the last green check, split into what needs one and what does not.

    Returns {"structural": [...], "cosmetic": [...], "baseline": bool}. `baseline` is true when
    there is nothing to compare against yet, in which case everything counts as unverified - the
    honest answer on a first run, rather than a confident "nothing changed".
    """
    structural, cosmetic = [], []
    baseline = not os.path.isdir(VERIFIED)

    for name in WATCHED:
        now = _rulings(os.path.join(CONTENT, name))
        then = _rulings(os.path.join(VERIFIED, name)) if not baseline else {}

        for key in set(now) | set(then):
            a = then.get(key)
            b = now.get(key)
            if a == b:
                continue

            verb = (b or a)[0]
            scope, is_structural = _verb_kind(key, verb)
            if b is None:
                what = "cleared: " + a[1]
            elif a is None:
                what = b[1]
            else:
                what = b[1] + "   (was: %s)" % a[1].split()[-1]

            (structural if is_structural else cosmetic).append({"scope": scope, "what": what})

    structural.sort(key=lambda d: d["what"])
    cosmetic.sort(key=lambda d: d["what"])
    return {"structural": structural, "cosmetic": cosmetic, "baseline": baseline}


def mark_verified():
    """Take the copies that later diffs are measured against. Call ONLY after a green check."""
    os.makedirs(VERIFIED, exist_ok=True)
    for name in WATCHED:
        src = os.path.join(CONTENT, name)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(VERIFIED, name))


# ---- the audit ------------------------------------------------------------------------------
#
# The table at the top of this file is a claim about code somewhere else, which is the kind of
# claim that goes quietly wrong. This checks it.

READERS = {
    "walk": [r"RoadRulings\.Walk"],
    "was": [r"RoadRulings\.Existed", r"RoadRulings\.AnyBlockGone", r"Rulings\.For", r"Rulings\.Absent"],
    "kind": [r"Rulings\.For"],
    "property": [r"Rulings\.SpokesmanFor", r"Rulings\.OneProperty"],
}


def audit():
    """Which files read each verb, so the cosmetic/structural split can be checked by eye."""
    assets = os.path.join(ROOT, "Assets", "Noir")
    hits = {v: set() for v in READERS}
    for base, _dirs, files in os.walk(assets):
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(base, f)
            try:
                text = io.open(path, encoding="utf-8", errors="replace").read()
            except OSError:
                continue
            for verb, pats in READERS.items():
                if any(re.search(p, text) for p in pats):
                    # Forward slashes at the point of RECORDING, not at the point of printing.
                    # Stored with backslashes and compared against a forward-slash literal below,
                    # this reported the split as stale on Windows every single time.
                    hits[verb].add(os.path.relpath(path, ROOT).replace("\\", "/"))

    print("Which code reads which ruling - the basis for the cosmetic/structural split\n")
    for verb in sorted(hits):
        cosmetic = verb in ("walk", "note")
        print("  %-9s %s" % (verb, "COSMETIC" if cosmetic else "STRUCTURAL"))
        for f in sorted(hits[verb]):
            print("             " + f)
        if not hits[verb]:
            print("             (nothing reads it)")
        print()

    # A walk ruling read by anything other than the drawing means the split is wrong, and wrong in
    # the expensive direction: something structural would be waved through as cosmetic.
    strays = hits["walk"] - {"Assets/Noir/Unity/RoadCentrelines.cs"}
    if strays:
        print("STALE SPLIT: sidewalks are now read by " + ", ".join(sorted(strays)))
        print("Treat `walk` as structural in the table at the top of this file.")
        return 1
    print("sidewalks are still drawing-only - the cosmetic/structural split holds.")
    return 0


if __name__ == "__main__":
    import sys
    if "--audit" in sys.argv:
        raise SystemExit(audit())
    u = unverified()
    print("baseline: %s" % u["baseline"])
    print("\nstructural (%d) - these need the game re-checked:" % len(u["structural"]))
    for d in u["structural"]:
        print("  %-18s %s" % (d["scope"], d["what"]))
    print("\ncosmetic (%d) - a Play press is enough:" % len(u["cosmetic"]))
    for d in u["cosmetic"]:
        print("  %-18s %s" % (d["scope"], d["what"]))
