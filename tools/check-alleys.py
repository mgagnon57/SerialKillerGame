"""Do Rossville's alleys actually reach a street?

WHAT THIS MEASURES AND WHY IT EXISTS. `derive-alleys.py` traces the alleys out of the gaps the
parcels leave - which is the right idea, because a right of way is the ground nobody owns and no
county maps its alleys. But it BLANKS 11 m around every street centreline before tracing, so that a
street's own corridor is not mistaken for an alley, and **nothing ever puts the mouth back**.

The result is a town whose back lanes connect to nothing. An alley that does not touch a street is
not a shortcut, a service road or a place to keep a car: it is a stripe of gravel in the middle of a
block that no vehicle can enter from any direction. It looks completely correct from above, which is
why it survived - the alleys are in the right places, they are the right width, and they are islands.

So this counts mouths. An alley END within `TOUCH` metres of a street centreline is a mouth; an
alley with no mouth at either end is stranded. It prints the distribution, because "0 of 62" and
"58 of 62" want different work, and a median gap tells you whether the fix is a nudge or a rebuild.

    python tools/check-alleys.py                 measure Content/roads.txt
    python tools/check-alleys.py <file>          measure some other roads file

Exit code is 0 when every alley has at least one mouth, 1 otherwise, so `build-roads.py --write`
can refuse to write a network whose alleys are islands. That is ALLEY-6: the mouth count is proved
BEFORE the file is written rather than predicted after it.
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(HERE, "..", "Content")

#: How near an alley end must come to a street centreline to count as meeting it, in metres.
#:
#: Not zero, and not tight. A street's own corridor is 10 m wide, so an alley end that stops 5 m
#: from the centreline has reached the carriageway edge and is a mouth in every sense that matters
#: to a car. Beyond about 8 m it is stopping short of the road surface itself.
TOUCH = 8.0


def roads_of(path):
    """Road runs from a file in city.txt's road syntax: name -> list of polylines, plus the class."""
    runs, klass, name = {}, {}, None
    with open(path, encoding="utf-8") as fh:
        for raw in fh:
            line = raw.split("#")[0].strip()
            m = re.match(r"^road (\S+) (\S+) (.*)$", line)
            if m:
                name = m.group(1)
                pts = [tuple(float(v) for v in t.split(",")) for t in m.group(3).split() if "," in t]
                if len(pts) >= 2:
                    runs.setdefault(name, []).append(pts)
                continue
            if line.startswith("class ") and name:
                klass[name] = line.split()[1]
    return runs, klass


def _point_to_segment(p, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    if dx == 0 and dy == 0:
        return math.dist(p, a)
    t = max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / (dx * dx + dy * dy)))
    return math.dist(p, (a[0] + t * dx, a[1] + t * dy))


def nearest_street(p, streets):
    """Metres from a point to the closest street centreline, and which street it was."""
    best, who = float("inf"), None
    for name, polylines in streets.items():
        for pl in polylines:
            for i in range(len(pl) - 1):
                d = _point_to_segment(p, pl[i], pl[i + 1])
                if d < best:
                    best, who = d, name
    return best, who


def measure(path):
    runs, klass = roads_of(path)

    alleys = {n: pls for n, pls in runs.items()
              if klass.get(n) == "alley" or n.startswith("alley")}
    streets = {n: pls for n, pls in runs.items() if n not in alleys}

    if not alleys:
        print(f"no alleys in {path}")
        return 0, 0, []

    gaps, stranded, mouths = [], [], 0
    for name in sorted(alleys, key=lambda n: (len(n), n)):
        ends = []
        for pl in alleys[name]:
            ends.append(pl[0])
            ends.append(pl[-1])

        here = []
        for e in ends:
            d, who = nearest_street(e, streets)
            here.append((d, who))
            gaps.append(d)

        touching = [x for x in here if x[0] <= TOUCH]
        mouths += len(touching)
        if not touching:
            closest = min(here, key=lambda x: x[0])
            stranded.append((name, closest[0], closest[1]))

    gaps.sort()
    median = gaps[len(gaps) // 2] if gaps else 0.0

    print(f"ALLEY MOUTHS in {os.path.basename(path)}")
    print(f"  {len(alleys)} alleys, {len(gaps)} ends")
    print(f"  {mouths} of {len(gaps)} ends reach a street (within {TOUCH:g} m)")
    print(f"  {len(stranded)} alleys touch NO street at either end "
          f"({100.0 * len(stranded) / len(alleys):.0f}%)")
    print(f"  median end-to-street gap {median:.1f} m ({median * 3.28084:.0f} ft)")

    if stranded:
        print("\n  stranded - a car cannot enter these from anywhere:")
        for name, d, who in stranded[:12]:
            print(f"    {name:<10} closest street {who or '(none)'} at {d:.1f} m")
        if len(stranded) > 12:
            print(f"    ...and {len(stranded) - 12} more")

    return mouths, len(gaps), stranded


def main():
    path = next((a for a in sys.argv[1:] if not a.startswith("--")), None)
    path = path or os.path.join(CONTENT, "roads.txt")
    if not os.path.exists(path):
        print(f"no such file: {path}")
        return 1

    mouths, ends, stranded = measure(path)
    if stranded:
        print(f"\nFAIL: {len(stranded)} alleys are islands. An alley no vehicle can enter is a "
              "stripe of gravel, not a road.")
        return 1
    print("\nOK: every alley reaches a street at least once.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
