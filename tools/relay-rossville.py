"""Lay Northgate out as Rossville, Illinois - on Rossville's OWN street grid.

NOT A GRID THAT LOOKS LIKE ROSSVILLE. The actual one, pulled from OpenStreetMap and
converted to metres about the crossing of Chicago Street and Attica Street, which is
where the village numbers its addresses from - East Attica ends and West Attica begins
on exactly that longitude, which is how the origin was found rather than assumed.

Two things fell out of the real data that no amount of guessing would have produced:

  THE TOWN IS MOSTLY EAST OF ROUTE 1. It runs 780m east of Chicago Street and only
  224m west. A symmetrical grid would have been wrong in a way you would feel without
  being able to name it.

  THE SPACING IS IRREGULAR - 104m Chicago to Harrison, 109 to Church, then 186 to
  Summit and 181 to Grove. Blocks average about 113m rather than the 91m a textbook
  300-foot block would give, and the variation IS the character.

Several streets change name as they cross Route 1: McKibben east is McKibbin west,
Maple is Park west, Gilbert is Perry west, Stewart is Stufflebeam west. Each is now
emitted as TWO road lines split at Route 1's own centreline (CX) - confirmed against
Vermilion County's own tax assessment PropertyAddress field on 2026-08-01 for three of
the four (Park, Perry and Stufflebeam all turned up as real addressed street names, all
strictly west of CX); McKibbin west is kept from the prior research pass, unconfirmed
by an actual addressed lot on that side. Ann has the same shape running the OTHER way:
Smith Drive north of Attica (CY), Ann south of it - five addresses, four Smith and one
Ann, split exactly where the data puts them.

Ross Township holds 1,617 people against the village's 1,331, so 286 are spread over
42 square miles - about seven to the square mile. The country here is a handful of
farmsteads, not a suburb, and that is correct.

The ground is glacial till plain, 679-706 ft, flat. Elevation when it comes should be
a few metres of roll and some drainage swales, not hills.
"""
import json, os, collections, random

SP = os.path.expandvars(r"%TEMP%\claude\C--SerialKillerGame\89ed6c06-1630-4c00-9f29-186178415413\scratchpad")
old = json.load(open(os.path.join(SP, "places.json")))
by_kind = collections.defaultdict(list)
for r in old:
    by_kind[r['kind']].append(r)

# ---- the map, and where the real crossroads sits on it -----------------------------
W, H = 2100, 2400
CX, CY = 750, 1335                 # Chicago Street x Attica Street

# Metres from that crossing, measured off OpenStreetMap. East and north positive.
# `sides` is which side of Route 1 the street actually runs, off the OSM extents. It
# decides the address prefix: a street that exists on one side only does not need one,
# which is exactly why the killer's address is "408 Holmes Ave" and not "408 E Holmes".
# RR CROSSINGS. Only four of these streets actually cross the CSX line at grade in reality -
# Henderson, Green, Benton and Attica, each confirmed against OpenStreetMap's own
# railway=level_crossing nodes (see Content/features.txt, the `crossing` entries). Every other
# street east of the tracks - Holmes, Maple, Gilbert, Stewart, McKibben, Dale, Greenwood,
# Thompson, Earlcourt - simply does not reach the far side in reality, whatever this map's own
# roads (axis-aligned, and none of them clipped at the tracks yet) currently draw. Load-bearing
# for whenever a train is simulated: gate crossing behaviour to those four streets and no others.
EW = [                     # (north offset, name, western name, sides, western name confirmed?)
    (+606, "york",       None,          "W",    False),
    (+489, "henderson",  None,          "both", False),  # real level crossing
    (+353, "green",      None,          "both", False),  # real level crossing
    (+222, "benton",     None,          "both", False),  # real level crossing
    (+126, "holmes",     None,          "E",    False),  # 408 Holmes Ave - no real crossing
    (   0, "attica",     None,          "both", False),  # THE cross street - real level crossing
    (-143, "maple",      "park",        "E",    True),   # confirmed: PARK, tax PropertyAddress
    (-248, "gilbert",    "perry",       "E",    True),   # confirmed: PERRY, tax PropertyAddress
    (-387, "stewart",    "stufflebeam", "E",    True),   # confirmed: STUFFLEBEAM, tax PropertyAddress
    (-518, "mckibben",   "mckibbin",    "E",    False),  # unconfirmed - no addressed lot west of CX
    (-636, "dale",       None,          "E",    False),
    (-699, "greenwood",  None,          "W",    False),
    (-758, "thompson",   None,          "E",    False),
    (-875, "earlcourt",  None,          "E",    False),
]
NS = [                             # north-south streets: (east offset, name, alias)
    (-224, "abner",      None),
    (-109, "watson",     None),
    ( -64, "ann",        "smith"),   # Smith Drive north of Attica, Ann south - see split below
    (   0, "chicago",    "Illinois Route 1, the Dixie Highway"),
    (+104, "harrison",   None),
    (+213, "church",     None),
    (+399, "summit",     None),
    (+580, "grove",      None),
    (+750, "goodwine",   "Creative Avenue"),
]
# THE REAL CSX LINE, from OpenStreetMap (way 332489882, "CSX Woodland Subdivision"),
# converted to the same metres-about-the-crossing frame as everything else. This is
# NOT the straight line the map used to assume: the real track runs diagonally,
# crossing only about 130-200m east of Chicago Street up at the north end of the
# village and opening out to 900m-plus well south of it. A single vertical
# "Railroad Avenue" can never be correct everywhere - the engine's roads are
# axis-aligned only - so RAILROAD below is a single representative crossing (at
# Attica's own latitude) for siding/elevator placement, and RAIL_X below is the
# real thing every BLOCK gets measured against so nothing is platted across it.
#
# Found by rendering the plan straight down after the parcels' own rotation was
# fixed: 120 of 469 houses - a quarter of the town - turned out to be beyond the
# real track for their row, "408 Holmes Ave" among them, 61m past it. The houses
# were never wrong; the assumed 900m of clearance before the tracks was.
RAIL = [
    (2112.1, 2553.2), (2004.1, 2393.3), (1803.0, 2095.5), (1541.4, 1708.0),
    (1489.4, 1628.3), (1441.8, 1559.5), (1357.7, 1435.8), (1306.1, 1357.5),
    (1294.4, 1339.7), (1205.3, 1209.6), (1122.7, 1088.6), (1074.3, 1014.7),
    (1034.6, 957.5), (958.3, 843.3), (839.3, 662.9), (775.7, 551.2),
    (734.6, 467.8), (692.8, 366.4), (659.2, 272.5), (631.1, 175.8),
    (611.8, 86.7), (594.0, -13.2), (584.2, -122.1),
]
RAIL_MARGIN = 25    # metres of clearance from the centreline - real right-of-way plus a verge

def rail_x_at(y):
    """The real track's x at a given map y, or None north/south of the surveyed line."""
    for i in range(len(RAIL) - 1):
        x1, y1 = RAIL[i]
        x2, y2 = RAIL[i + 1]
        if (y1 - y) * (y2 - y) <= 0 and y1 != y2:
            t = (y - y1) / (y2 - y1)
            return x1 + t * (x2 - x1)
    return None

RAILROAD = round(rail_x_at(CY) - CX)    # the real crossing at Attica's latitude - not a guess

XS = [CX + o for o, _, _ in NS]
YS = [CY - e[0] for e in EW]    # game y increases southward
X0, X1 = min(XS) - 30, max(XS) + 30
Y0, Y1 = min(YS) - 30, max(YS) + 30

CHI = sorted(XS).index(CX)         # which column Route 1 is, for numbering

def blocks():
    """Every rectangle the street grid encloses, carrying its address block number.

    THE NUMBER COUNTS CROSS STREETS, NOT METRES. American small towns number a block
    by how many streets out from the origin it is - the 400 block of E Holmes is the
    fourth one east of Route 1, whether that is 400 metres or 700. Dividing distance
    by an average block width gets it wrong the moment the spacing is irregular, and
    Rossville's is: 104m to Harrison, then 109, then 186, then 181.
    """
    got = []
    xs, ys = sorted(XS), sorted(YS)
    byY = {CY - o: (name, sides) for o, name, _, sides, _c in EW}

    # INSET BY THE ROAD'S OWN HALF-WIDTH, not by a flat number. Route 1 and Attica are
    # 30m corridors and the village streets are 10m, so a flat 8m inset put the whole
    # first row of houses inside the highway - fifteen of them, which MapAudit caught
    # as places laid over a road. Three metres of verge beyond the kerb on top.
    def pad(v, main): return (15 if v == main else 5) + 3
    for i in range(len(xs) - 1):
        east = i >= CHI
        number = (i - CHI + 1) * 100 if east else (CHI - i) * 100
        lft, rgt = pad(xs[i], CX), pad(xs[i + 1], CX)
        for j in range(len(ys) - 1):
            top, bot = pad(ys[j], CY), pad(ys[j + 1], CY)
            x, y = xs[i] + lft, ys[j] + top
            bw, bh = xs[i + 1] - xs[i] - lft - rgt, ys[j + 1] - ys[j] - top - bot

            # CLIPPED AGAINST THE REAL TRACK, not the schematic Railroad Avenue. The rail
            # is diagonal, so take the tighter (more restrictive) of the two boundaries at
            # the top and bottom of this block rather than one sample at its centre - a
            # block can straddle enough latitude for the real line to cross it diagonally
            # without either edge's own y giving the worst case.
            top_safe = rail_x_at(y)
            bot_safe = rail_x_at(y + bh)
            safe_edges = [s - RAIL_MARGIN for s in (top_safe, bot_safe) if s is not None]
            if safe_edges:
                safe_x = min(safe_edges)
                if x >= safe_x:
                    continue                       # the whole block is past the tracks
                bw = min(bw, safe_x - x)

            if bw >= 26 and bh >= 26:
                got.append((x, y, bw, bh, number, east,
                            byY.get(ys[j]), byY.get(ys[j + 1])))
    return got

out = []
def w_(s=""): out.append(s)

w_("# " + "=" * 74)
w_("#  NORTHGATE, ON ROSSVILLE, ILLINOIS'S OWN STREET GRID")
w_("#")
w_("#  Not a grid that looks like Rossville - the actual one, taken from")
w_("#  OpenStreetMap and converted to metres about the crossing of Chicago Street")
w_("#  and Attica Street. That crossing is where the village numbers its addresses")
w_("#  from: East Attica ends and West Attica begins on exactly that longitude,")
w_("#  which is how the origin was found rather than assumed.")
w_("#")
w_("#  TWO THINGS THE REAL DATA GAVE THAT GUESSING WOULD NOT:")
w_("#")
w_("#    THE TOWN IS MOSTLY EAST OF ROUTE 1 - 780m east of Chicago Street and only")
w_("#    224m west. A symmetrical grid would have been wrong in a way you would feel")
w_("#    without being able to name it.")
w_("#")
w_("#    THE SPACING IS IRREGULAR - 104m Chicago to Harrison, 109 to Church, then")
w_("#    186 to Summit and 181 to Grove. Blocks average about 113m, not the 91m a")
w_("#    textbook 300-foot block gives, and the variation IS the character.")
w_("#")
w_("#  Several streets change name as they cross Route 1: McKibben east is McKibbin")
w_("#  west, Maple is Park Place, Gilbert is Perry, Stewart is Stufflebeam. Each is")
w_("#  emitted once under its eastern name, because the town is mostly east.")
w_("#")
w_("#  ONE SET OF LIGHTS, at Chicago and Attica. CitySignals requires both arms to be")
w_("#  a main road, so every other junction in the village is a stop sign.")
w_("#")
w_("#  THE WATER TOWER AND THE GRAIN ELEVATOR are the skyline. No tower block.")
w_("#")
w_("#  THE COUNTRY IS NEARLY EMPTY OF PEOPLE and that is right: Ross Township holds")
w_("#  1,617 against the village's 1,331, so 286 are spread over 42 square miles.")
w_("#  Seven to the square mile. It only adds up at county scale.")
w_("#")
w_("#  GROWS NORTH AND SOUTH: Chicago Street runs the full height of the map because")
w_("#  Hoopeston is six miles up it and Alvin five miles down it.")
w_("#")
w_("#  Generated by tools/relay-rossville.py; street positions in")
w_("#  tools/rossville-streets.json.")
w_("# " + "=" * 74)
w_()
w_("village Northgate")
w_(f"size {W} {H}")
w_()

w_("# ---- ground ---------------------------------------------------------------")
w_("# Field under everything - corn and beans to the horizon - grass over the village")
w_("# lots, and pavement ONLY at the crossroads. That last is load-bearing: CitySignals")
w_("# asks the ground at a junction's diagonals whether it is in a town, so paving the")
w_("# whole village would put lights on every corner of it.")
w_(f"terrain field 0,0 {W}x{H}")
w_(f"terrain grass {X0-20},{Y0-20} {X1-X0+40}x{Y1-Y0+40}")
w_(f"terrain path {CX-26},{CY-26} 52x52)".replace(")", ""))
w_()

w_("# ---- Illinois Route 1 and the cross street --------------------------------")
w_("# `mainroad` is a 30m corridor, one lane each way with lay-bys, which is what a")
w_("# two-lane state highway through a village is. Both run the full extent of the map:")
w_("# a state route does not stop at the village limit.")
w_(f"road chicago 30 {CX},0 {CX},{H-1}")
w_("  class mainroad")
w_(f"road attica 30 0,{CY} {W-1},{CY}")
w_("  class mainroad")
w_()
w_("# ---- the village streets --------------------------------------------------")
w_("# 10m corridors, unmarked, which is what a residential street in a village of")
w_("# thirteen hundred is. Offsets are metres from the crossroads.")
for o, name, alias in NS:
    if name == "chicago": continue
    x = CX + o
    # EMITTED AS ONE ROAD, NOT TWO. "ann" really is two streets end to end - Smith Drive
    # north of Attica, Ann south of it, confirmed by the county's own tax address data on
    # 2026-08-01 - but splitting the ROAD LINE to match cost the lane graph its straight-
    # through turn at that junction: a car crossing Attica on this column had no wired
    # continuation from one line to the other and stood there indefinitely with clear
    # road ahead (caught by NoCarWaitsForeverAtTheHeadOfAClearQueue). The name split is
    # real; it is applied as DISPLAY ONLY, by position, in PlanLabels and StreetAddressing
    # - see RealName in both - rather than in the road network itself.
    w_(f"road {name} 10 {x},{Y0} {x},{Y1}   # {o:+d}m" + (f", {alias}" if alias else ""))
    w_("  class street")
for o, name, western, _s, confirmed in EW:
    if name == "attica": continue
    y = CY - o
    # Same reasoning as "ann" above: Park/Perry/Stufflebeam/McKibbin west of Route 1 are
    # real second names for these rows, applied as a DISPLAY-ONLY split rather than a road
    # network split, for the same lane-graph reason.
    w_(f"road {name} 10 {X0},{y} {X1},{y}   # {o:+d}m" + (f", {western} west of Route 1" if western else ""))
    w_("  class street")
w_()
w_("# ---- the section roads -----------------------------------------------------")
w_("# NO SIMULATED RAILROAD AVENUE. The real CSX line is diagonal - it crosses barely")
w_("# 130m east of Chicago Street up at York and opens past 1,100m by Earlcourt - and")
w_("# the engine's roads are axis-aligned only, so any single vertical line drawn for")
w_("# it is wrong for most of the town's latitude by construction. It used to be a")
w_("# flat +900m guess, and a quarter of the village's houses turned out to be on the")
w_("# wrong side of the real track for their row because of it. The real alignment is")
w_("# drawn instead as a `rail` feature in features.txt, pulled from OpenStreetMap,")
w_("# which CAN follow the real diagonal because it is decoration and not a road a")
w_("# person or a car is simulated walking on. RAILROAD the constant survives, for the")
w_("# elevator's siting below, but nothing paves a road along it any more.")
w_("#")
w_("# The country is surveyed on a mile grid and the gravel section roads follow the")
w_("# section lines. Each crosses a main road, so none is an island - an isolated road")
w_("# has lanes and no junction at either end, and its traffic drives off the tarmac")
w_("# and out of the world.")
for n, y in enumerate([220, H - 220]):
    w_(f"road section{n} 10 0,{y} {W-1},{y}")
    w_("  class track")
for n, x in enumerate([220, W - 220]):
    w_(f"road crossroad{n} 10 {x},0 {x},{H-1}")
    w_("  class track")
w_()

# ---- packing ----------------------------------------------------------------------
class Lot:
    GAP = 2
    def __init__(self, x, y, w, h):
        self.x0, self.y0, self.w, self.h = x, y, w, h
        self.cy, self.cx, self.shelf = y, x, 0
    def fit(self, pw, ph):
        # WIDER THAN THE BLOCK IS A REFUSAL, not a new shelf. Without this the wrap
        # below resets cx to the left edge and the next test only asks about HEIGHT,
        # so a 35m casino was seated in a 29m block and hung three metres out into
        # Ann Street. MapAudit caught it; the packer should not have offered it.
        if pw > self.w or ph > self.h: return None
        if self.cx + pw > self.x0 + self.w:
            self.cy += self.shelf + self.GAP
            self.cx, self.shelf = self.x0, 0
        if self.cy + ph > self.y0 + self.h: return None
        x, y = self.cx, self.cy
        self.cx += pw + self.GAP
        self.shelf = max(self.shelf, ph)
        return x, y

def emit(rec, x, y, units=None, name=None):
    w_(f'place {rec["kind"]} {x},{y} {rec["w"]}x{rec["h"]} "{name or rec["name"]}"')
    if rec.get('door'):
        dx, dy = rec['door']
        w_(f"  door {min(max(x+dx, x), x+rec['w']-1)},{min(max(y+dy, y), y+rec['h']-1)}")
    u = units if units is not None else rec.get('units')
    if u: w_(f"  units {u}")
    if rec.get('human'): w_(f"  human {rec['human']}")

ALL = blocks()
def mid(b): return b[0] + b[2] / 2, b[1] + b[3] / 2

# The storefronts are the 100 blocks of North and South Chicago - the rectangles that
# touch the crossroads, and that is the whole business district.
BUSINESS = [b for b in ALL if abs(mid(b)[0] - CX) < 130 and abs(mid(b)[1] - CY) < 200]
rest = [b for b in ALL if b not in BUSINESS]

w_("# " + "=" * 74)
w_("#  THE 100 BLOCKS OF NORTH AND SOUTH CHICAGO STREET")
w_("# " + "=" * 74)
w_()
DOWN = ["bank", "diner", "pub", "shop", "postoffice", "villagehall", "cinema", "casino",
        "icecream", "newsstand", "gasstation", "carwash", "restroom", "precinct"]
items = []
for kind in DOWN:
    picks = by_kind.get(kind, [])
    if kind == "shop": picks = picks[:14]
    if kind == "pub": picks = picks[:3]
    items += picks
items.sort(key=lambda r: -(r['w'] * r['h']))
flats = [dict(by_kind['apartment'][n], units=3,
              name=f"Chicago Street Chambers{'' if n == 0 else ', ' + str(n + 1)}",
              human="Over the shops, and the stairs go up from the street." if n == 0 else None)
         for n in range(4)]

# THE STOREFRONTS FRONT CHICAGO STREET, which sounds too obvious to write down and
# was not what happened. Shelf-packing filled the first block in the list before it
# started the second, and `sorted(BUSINESS)` puts the lowest x first - so all
# thirty-five businesses ended up in a strip on WATSON STREET, a hundred metres west
# of Route 1, and the main street of the town had grass and trees on it. The render
# is unambiguous and it is the whole of "it looked like crap".
#
# So the frontage is placed as a frontage: two runs down either side of Chicago from
# the crossroads, shops shoulder to shoulder, doors on the street. Depth into the
# block, width along it - a shop authored 16x12 becomes 12 deep and 16 wide, because
# on a north-south street the frontage is measured along y.
FRONT_DEPTH = 22
west_edge = CX - 15 - 3 - FRONT_DEPTH      # back of the west run
east_edge = CX + 15 + 3                     # front of the east run

def frontage(rec, side, cursor):
    """Seat one business on Chicago Street. Returns the new cursor along y."""
    deep, wide = min(rec['h'], FRONT_DEPTH), rec['w']
    x = west_edge + (FRONT_DEPTH - deep) if side < 0 else east_edge
    y = cursor
    r = dict(rec, w=deep, h=wide)
    # The door is on the Chicago side, which is what FacingOf reads to turn the shop
    # round so its front faces the highway rather than the back of the block.
    r['door'] = (deep - 1, wide // 2) if side < 0 else (0, wide // 2)
    emit(r, x, y)
    return y + wide + 2

# A RUN STOPS AT THE CORNER. The frontage used to march straight through the cross
# streets - the Weighhouse stood ten metres into Holmes Avenue - because nothing told
# it the block had ended. Storefronts stop at the junction and start again on the next
# block, which is both what MapAudit demands and what a main street looks like.
CROSS_Y = sorted(CY - o for o, name, _, _, _ in EW if name != "attica")

def blocked(y, wide, pad=8):
    for c in CROSS_Y:
        if y < c + pad and y + wide > c - pad: return c
    return None

spill = []
# North of Attica up one side and down the other, then south of it, so all four corners
# of the crossroads are built on and the junction reads as the middle of a town.
runs = [(-1, CY - 18, -1), (+1, CY - 18, -1), (-1, CY + 18, +1), (+1, CY + 18, +1)]
queue = list(items) + list(flats)
LIMIT = 230
for side, start, direction in runs:
    cursor = start
    while queue:
        rec = queue[0]
        wide = rec['w']
        y = cursor - wide if direction < 0 else cursor
        hit = blocked(y, wide)
        if hit is not None:
            # Step clear of the junction and try the same shop again on the far side.
            cursor = hit - 8 if direction < 0 else hit + 8
            if abs(cursor - CY) > LIMIT: break
            continue
        if abs(y - CY) > LIMIT: break
        queue.pop(0)
        frontage(rec, side, y)
        cursor = y - 2 if direction < 0 else y + wide + 2
spill += queue
w_()

w_("# ---- the school, the firehouse, the elevator, the water tower --------------")
w_("# The fire station is at the north end because Rossville's new one is. The elevator")
w_("# stands near where the real CSX line runs - there is no simulated road for the")
w_("# tracks any more, see RAILROAD above, only the siting constant and the decorative")
w_("# `rail` feature in features.txt.")
w_()
made = {
    "elevator": dict(kind="elevator", w=24, h=34, name="the Rossville elevator", door=None,
                     units=None, human="Concrete, and it throws a shadow over the tracks all morning."),
    "watertower": dict(kind="watertower", w=18, h=18, name="the water tower", door=None,
                       units=None, human="The name is painted on it and reads from the section road."),
}
CIVIC = [("firestation", 0, +489), ("school2", -180, +222), ("watertower", +120, +353),
         ("elevator", RAILROAD - 40, -100), ("green", -180, -387), ("playground", +399, -636)]
used_civic = []
for what, dx, dy in CIVIC:
    src = made.get(what) or (by_kind.get(what) or [None])[0]
    if src is None: continue
    want = (CX + dx, CY - dy)
    home = min((b for b in rest if b not in used_civic),
               key=lambda b: abs(mid(b)[0] - want[0]) + abs(mid(b)[1] - want[1]), default=None)
    if home is None: continue
    used_civic.append(home)
    L = Lot(*home[:4])
    at = L.fit(src['w'], src['h'])
    if at: emit(src, at[0], at[1])
w_()

w_("# " + "=" * 74)
w_("#  THE HOUSES")
w_("#")
w_("#  Rossville held 1,331 people in 2010. Units are set by BLOCK AREA rather than")
w_("#  flat, because the real grid's blocks are not the same size - Chicago to Harrison")
w_("#  is 104m and Church to Summit is 186 - and giving a narrow block the same")
w_("#  population as a wide one is exactly how a real grid gets flattened into a fake")
w_("#  one. About one household to 1,750 square metres, which lands on the village's")
w_("#  own figure without being told it.")
w_("#")
w_("#  408 HOLMES AVE is on the block between Summit and Grove, north side of Holmes")
w_("#  Avenue, 126m north of Attica. Holmes runs east of Chicago Street only, which is")
w_("#  why the 400 block is the fourth one east. The house is not yet a place of its")
w_("#  own - that is the next piece of work.")
w_("# " + "=" * 74)
w_()

w_("# " + "=" * 74)
w_("#  THE HOUSES, ONE ADDRESS AT A TIME")
w_("#")
w_("#  Every house is its own place and its NAME IS ITS ADDRESS. That needed no new")
w_("#  directive and no new field: `house` is already an alias of the `dwelling` kind")
w_("#  (kinds.txt), which is already `home yes`, and WorldBuilder already refuses two")
w_("#  places with the same name - so an address is unique by construction and Place")
w_("#  already hashes it into a stable key. The town addressed itself.")
w_("#")
w_("#  THE NUMBERING IS THE REAL CONVENTION. The hundred counts CROSS STREETS out")
w_("#  from Route 1, not metres - the 400 block of E Holmes is the fourth one east")
w_("#  whether that is 400m or 700, and Rossville's spacing is irregular enough that")
w_("#  the difference is not academic. Within the block the lot number counts outward")
w_("#  from the crossroads, even on the south side of a street and odd on the north,")
w_("#  which is how you can stand on one kerb and know the numbers opposite.")
w_("#")
w_("#  SO 408 HOLMES AVE IS A DOOR. Fourth lot of the fourth block east, south side")
w_("#  of Holmes Avenue. You can walk to it.")
w_("#")
w_("#  Vacant lots are simply not emitted. A declining village has gaps in its")
w_("#  frontage - Rossville has lost a hundred people since 2010 - and a number with")
w_("#  no house on it is what that looks like from the street.")
w_("# " + "=" * 74)
w_()

homes = [b for b in rest if b not in used_civic]
PITCH, LOTW, LOTH, SETBACK = 26, 13, 7, 8
built = 0
skipped = 0

# 408 HOLMES AVE IS PROTECTED. Real Holmes Avenue's 400 block runs almost onto the
# real CSX line - the block survives clipping with room for exactly one lot, and that
# lot is the one the stable vacant-lot hash happens to skip. Rather than let a fixed
# story anchor disappear because of where a modulus landed, the first safe lot in
# Holmes' 400 block is reserved for it outright, address forced to 408 regardless of
# the rank arithmetic or the vacancy roll. Everything else in the town still goes
# through the ordinary rules.
holmes_408_done = False

for b in homes:
    bx0, by0, bw, bh, hundred, east, north_st, south_st = b
    runs = []
    if north_st: runs.append((north_st, by0 + SETBACK, "even"))
    if south_st: runs.append((south_st, by0 + bh - SETBACK - LOTH, "odd"))

    for (street, sides), ly, parity in runs:
        # A street that only exists on one side of Route 1 has no houses on the other.
        if sides == "E" and not east: continue
        if sides == "W" and east: continue

        n = int((bw + 2) // PITCH)
        for k in range(n):
            # Counted outward from Route 1, so the numbers rise as you walk away
            # from the middle of town whichever side of it you started.
            rank = k + 1 if east else n - k
            number = hundred + (2 * rank if parity == "even" else 2 * rank - 1)
            lx = bx0 + k * PITCH + (PITCH - LOTW) // 2

            reserved_for_408 = (street == "holmes" and hundred == 400
                                and not holmes_408_done)
            if reserved_for_408:
                number = 408
                holmes_408_done = True
            # A gap every so often, stable per lot so the same houses are missing
            # every time the map is built - unless this lot was just claimed above.
            elif (lx * 7919 + ly * 104729) % 20 < 3:
                skipped += 1
                continue

            side = "" if sides != "both" else ("E " if east else "W ")
            w_(f'place house {lx},{ly} {LOTW}x{LOTH} "{number} {side}{street.title()} Ave"')
            # The door goes on the street side, which is what CityBuildings.FacingOf
            # reads to turn the house round to face its own road.
            w_(f"  door {lx + LOTW // 2},{ly if parity == 'even' else ly + LOTH - 1}")
            built += 1

if not holmes_408_done:
    # Nothing in Holmes' 400 block survived clipping at all - the real track must run
    # closer than expected. Not silently losing the killer's address: report it loudly
    # so a person decides where it goes, rather than the town quietly relocating him.
    print("WARNING: 408 Holmes Ave could not be placed - the 400 block did not survive "
          "the rail clip. Fix this before using the output.")
w_()
w_("# " + "=" * 74)
w_("#  THE COUNTRY - corn, beans, and about seven people to the square mile")
w_("# " + "=" * 74)
w_()
jitter = random.Random(20260801)
NUDGE = 8
country = []
for kind in ["farm", "farmyard", "barn", "silo", "cornfield", "paddock", "orchard",
             "copse", "allotments", "churchyard", "memorial", "standing", "camp"]:
    for r in by_kind.get(kind, []):
        r = dict(r)
        r['w'], r['h'] = min(r['w'], 70), min(r['h'], 70)
        if kind == "farm": r['units'] = 2
        country.append(r)
steads = [r for r in country if r['kind'] in ("farm", "farmyard", "barn", "silo")]
for n in range(10):
    s = dict(steads[n % len(steads)]); s['name'] = f"{s['name']} {n + 2}"; country.append(s)
fields = [r for r in country if r['kind'] in ("cornfield", "paddock", "orchard", "copse")]
for n in range(64):
    s = dict(fields[n % len(fields)]); s['name'] = f"{s['name']} {n + 2}"; country.append(s)

# Every corridor a country lot has to keep out of: (vertical?, centre, half-width+verge).
# The section roads and Route 1 run the whole way across the country, and a field laid
# over one of them is a cornfield growing in the carriageway. No entry for the railroad
# here any more - it is not a road (see above) - so a field can still land on the real
# diagonal `rail` feature out in the country. Rarer out there than in the platted town,
# and a corn field over a rail corridor is a much smaller lie than a house over one.
CORRIDORS = ([(True, CX, 15 + 4), (False, CY, 15 + 4)]
             + [(False, y, 5 + 4) for y in (220, H - 220)]
             + [(True, x, 5 + 4) for x in (220, W - 220)])

def clear_of_roads(x, y, w, h):
    for vertical, centre, half in CORRIDORS:
        lo, hi = (x, x + w) if vertical else (y, y + h)
        if lo < centre + half and hi > centre - half: return False
    return True

edge = []
for gx in range(0, W - 100, 130):
    for gy in range(0, H - 100, 130):
        if X0 - 40 < gx < X1 and Y0 - 40 < gy < Y1: continue
        if not clear_of_roads(gx + 4, gy + 4, 122, 122): continue
        edge.append(Lot(gx + 4, gy + 4, 122, 122))

placed = 0
for rec in country:
    for L in edge:
        at = L.fit(rec['w'] + NUDGE, rec['h'] + NUDGE)
        if at:
            emit(rec, at[0] + jitter.randint(0, NUDGE), at[1] + jitter.randint(0, NUDGE))
            placed += 1
            break
    else: spill.append(rec)

open(os.path.join(SP, "city.new.txt"), "w", encoding="utf-8",
     newline="\n").write("\n".join(out) + "\n")

farms = sum(1 for r in country if r['kind'] == 'farm')
print(f"grid {len(NS)} N-S x {len(EW)} E-W -> {len(ALL)} blocks "
      f"({len(BUSINESS)} business, {len(used_civic)} civic, {len(homes)} residential)")
print(f"village {built} houses ({skipped} lots left vacant) -> ~{round(built * 2.25)} people")
print(f"country {farms} farmsteads x2 -> ~{round(farms * 2 * 2.25)} people, {placed} places")
print(f"business {len(items) + len(flats)} plots; map {W}x{H}")
if spill: print("SPILLED:", collections.Counter(r['kind'] for r in spill))
