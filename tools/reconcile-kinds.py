"""Make the place kinds in city.txt agree with the owner's 1991 rulings, where they genuinely
disagree - and refuse to, where the disagreement is only a difference of vocabulary.

WHY THIS IS NOT A ONE-LINE REPLACE. Three things make a naive "set the kind to what was ruled"
wrong, and all three were found by running it:

  THE SAME KIND UNDER TWO NAMES IS NOT A DISAGREEMENT. Content/kinds.txt gives every kind a
  `words` list - `dwelling house cottage` are one kind - so both sides are resolved through that
  table before anything is compared. Twelve of the twenty-six apparent conflicts were this.

  A COARSE RULING MUST NOT OVERWRITE A PRECISE FACT. Six lots ruled `shop` carry a casino, a pub,
  an ice cream parlour, a laundromat, a rest room and a village hall - all named. The ruling is
  shorthand for "commercial"; the map already knows which shop. Those are reported, not applied.

  ONLY GENERATED FILLER MAY BE RE-KINDED. A generic house or a generic field is placed by the
  generator and carries no information a ruling can destroy. Anything the map calls a specific
  institution - "the I.O.O.F. hall" on a lot ruled `school` - is a fact somebody put there on
  purpose. Reported, and left for the owner to settle.

It also refuses to turn several buildings into several pieces of open ground: eight houses on a
lot ruled `cornfield` is a request to take eight buildings down, which is a different act from
renaming one, and not one to make sideways through a kind column.

    python tools/reconcile-kinds.py             # say what it would do, write nothing
    python tools/reconcile-kinds.py --apply     # back up to city.txt.before-kinds, then write

Content/city.txt is hand-edited and is backed up before every write.
"""
import os
import re
import shutil
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "Content")
CITY = os.path.join(ROOT, "city.txt")
APPLY = "--apply" in sys.argv

# A coarse word the game already answers more precisely.
GENERIC = {"shop"}
# What may be re-kinded: generated filler only.
FILLER = {"dwelling", "cornfield", "green"}


def vocabulary():
    """canonical kind per accepted word, and building|open per kind, from Content/kinds.txt."""
    canon, form, cur = {}, {}, None
    with open(os.path.join(ROOT, "kinds.txt"), encoding="utf-8") as f:
        for raw in f:
            s = raw.strip()
            m = re.match(r"^kind\s+(\S+)", s)
            if m:
                cur = m.group(1)
                canon[cur.lower()] = cur
                continue
            if not cur:
                continue
            m = re.match(r"^words\s+(.+)", s)
            if m:
                for w in m.group(1).split():
                    canon[w.lower()] = cur
            m = re.match(r"^form\s+(\S+)", s)
            if m:
                form[cur] = m.group(1)
    return canon, form


def rings():
    out = []
    with open(os.path.join(ROOT, "parcels.txt"), encoding="utf-8") as f:
        for raw in f:
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            pts = []
            for piece in line.split(" "):
                if "," in piece:
                    a, b = piece.split(",", 1)
                    try:
                        pts.append((float(a), float(b)))
                    except ValueError:
                        pass
            out.append(pts)
    return out


def rulings():
    out = {}
    with open(os.path.join(ROOT, "parcel-1991.txt"), encoding="utf-8") as f:
        for raw in f:
            s = raw.strip()
            if not s.startswith("parcel "):
                continue
            p = s.split(" ", 3)
            if len(p) >= 4 and p[2] == "kind":
                out[int(p[1])] = p[3].strip().strip('"')
    return out


def inside(poly, x, y):
    r, j = False, len(poly) - 1
    for i in range(len(poly)):
        ax, ay = poly[i]
        bx, by = poly[j]
        if (ay > y) != (by > y) and x < (bx - ax) * (y - ay) / (by - ay) + ax:
            r = not r
        j = i
    return r


def main():
    canon, form = vocabulary()
    parcels = rings()
    box = [(min(p[0] for p in r), min(p[1] for p in r),
            max(p[0] for p in r), max(p[1] for p in r)) if r else (0, 0, 0, 0)
           for r in parcels]
    ruled = rulings()

    def k(word):
        return canon.get((word or "").strip().lower())

    def lot_at(x, y):
        for i, (x0, y0, x1, y1) in enumerate(box):
            if x0 <= x <= x1 and y0 <= y <= y1 and inside(parcels[i], x, y):
                return i
        return None

    rx = re.compile(r'^(\s*place\s+)(\S+)(\s+([\d.]+),([\d.]+)\s+([\d.]+)x([\d.]+).*)$')
    lines = open(CITY, encoding="utf-8").read().split("\n")

    per_lot = defaultdict(list)
    for n, raw in enumerate(lines):
        m = rx.match(raw)
        if not m:
            continue
        x, y, w, h = (float(m.group(i)) for i in (4, 5, 6, 7))
        i = lot_at(x + w / 2, y + h / 2)
        if i is not None:
            per_lot[i].append((n, m))

    changes, skipped, guarded = [], [], []
    for pid, word in sorted(ruled.items()):
        want = k(word)
        if want is None or pid not in per_lot:
            continue
        here = [(n, m) for n, m in per_lot[pid] if k(m.group(2)) != want]
        if not here:
            continue
        names = [m.group(2) for _, m in here]

        if word.strip().lower() in GENERIC:
            skipped.append((pid, word, names, "the game is more specific, and named"))
            continue
        if form.get(want) == "open" and len(here) > 1:
            skipped.append((pid, word, names,
                            "%d places would become %d pieces of open ground"
                            % (len(here), len(here))))
            continue
        if form.get(want) == "open" and any(form.get(k(m.group(2))) == "building" for _, m in here):
            skipped.append((pid, word, names, "that is taking a building down, not renaming it"))
            continue
        for n, m in here:
            if k(m.group(2)) not in FILLER:
                guarded.append((pid, word, m.group(2), m.group(0).strip()))
                continue
            changes.append((pid, n, m.group(2), want, m.group(0).strip()))

    print("WOULD CHANGE  (%d places on %d lots)" % (len(changes), len({c[0] for c in changes})))
    for pid, n, was, now, text in changes:
        print("  lot %-4d line %-5d %-10s -> %-10s  %s" % (pid, n + 1, was, now, text[:56]))
    print()
    print("LEFT ALONE  (%d lots)" % len(skipped))
    for pid, word, names, why in skipped:
        print("  lot %-4d ruled %-10s map has %-28s %s" % (pid, word, ",".join(names)[:28], why))
    print()
    print("NAMED BUILDINGS PROTECTED  (%d) - the owner's call, not this script's" % len(guarded))
    for pid, word, was, text in guarded:
        print("  lot %-4d ruled %-10s but the map has a %-12s %s" % (pid, word, was, text[:52]))

    if not APPLY:
        print("\ndry run - nothing written. Pass --apply to write.")
        return
    if not changes:
        print("\nnothing to do.")
        return

    shutil.copy2(CITY, CITY + ".before-kinds")
    for pid, n, was, now, text in changes:
        m = rx.match(lines[n])
        lines[n] = m.group(1) + now + m.group(3)
    open(CITY, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    print("\nbacked up to Content/city.txt.before-kinds")
    print("wrote Content/city.txt - %d place kinds corrected" % len(changes))


if __name__ == "__main__":
    main()
