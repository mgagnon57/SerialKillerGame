"""Re-lay Northgate at the size a thousand people actually make.

Blocks are 60x60 with a 30m road corridor, so block k spans 30+90k..90+90k and
road k covers 90k..30+90k. Town is blocks 1..7; block 0 and 8 are the green edge.
"""
import json, os, re, collections, random

SP = os.path.expandvars(r"%TEMP%\claude\C--SerialKillerGame\89ed6c06-1630-4c00-9f29-186178415413\scratchpad")
old = json.load(open(os.path.join(SP, "places.json")))
by_kind = collections.defaultdict(list)
for r in old:
    by_kind[r['kind']].append(r)

SIZE = 840
def bx(k): return 30 + 90 * k          # block interior origin

DOWNTOWN = [(i, j) for j in range(3, 6) for i in range(3, 6)]      # 9
TOWN     = [(i, j) for j in range(1, 8) for i in range(1, 8)]      # 49
HOMES    = [c for c in TOWN if c not in DOWNTOWN]                  # 40

out = []
def w(s=""): out.append(s)

# ---------------------------------------------------------------- header
w("# " + "=" * 74)
w("#  NORTHGATE")
w("#")
w("#  RE-LAID AT 840, FROM 1290. The old map was a city footprint carrying a")
w("#  village population: 1,664,100 square metres for a thousand people, so")
w("#  every street was long, every errand was a hike, and the town read as")
w("#  abandoned even with everybody out of doors at once. This is 42% of that")
w("#  area, and the shape is the point rather than the number:")
w("#")
w("#    DOWNTOWN IS THREE BLOCKS SQUARE and holds all of it - the shops, the")
w("#    pubs, the diner, the cinema, the casino, the bank, the hospital, one")
w("#    tower and one small apartment terrace. Nine blocks, walkable corner to")
w("#    corner in four minutes, which is what makes it somewhere to go.")
w("#")
w("#    THE HOMES GROW OUT OF IT. Forty cells of houses wrapped round downtown,")
w("#    nearest first. Nobody lives downtown except the one terrace, and that")
w("#    is deliberate: a town where the shops are somewhere else is a town with")
w("#    a morning worth watching.")
w("#")
w("#    THE EDGE STAYS GREEN. Blocks 0 and 8 are outside the road grid - farms,")
w("#    fields, copses, and the places the story happens in.")
w("#")
w("#  GENERATED, then committed as text like everything else here. Re-running")
w("#  the generator is how the layout changes; editing this file by hand works")
w("#  exactly as well and MapAudit checks either.")
w("# " + "=" * 74)
w()
w("village Northgate")
w(f"size {SIZE} {SIZE}")
w()

# ---------------------------------------------------------------- terrain
w("# ---- ground ---------------------------------------------------------------")
w("# Field under everything, grass over the town, paving over downtown only.")
w(f"terrain field 0,0 {SIZE}x{SIZE}")
w(f"terrain grass {bx(1)-30},{bx(1)-30} {90*7+30}x{90*7+30}")
w(f"terrain path {bx(3)-15},{bx(3)-15} {90*3}x{90*3}")
w()

# ---------------------------------------------------------------- roads
NS = ["westbound", "westway", "market", "first", "second", "eastway", "eastbound", "fourth"]
EW = ["northbound", "northway", "bishop", "northgate", "franklin", "southway", "southbound", "merrow"]
# One arterial each way through the middle of downtown, and the ring that bounds
# the housing. Everything else carries local traffic.
FREEWAY = {"westway", "eastway", "first", "northway", "southway", "northgate"}

w("# ---- the streets ----------------------------------------------------------")
w("# Centred at 15+90k, so each one runs down the near side of the block it names.")
for k, name in enumerate(NS, start=1):
    x = 15 + 90 * k
    w(f"road {name} 30 {x},0 {x},{SIZE-1}")
    w(f"  class {'freeway' if name in FREEWAY else 'mainroad'}")
for k, name in enumerate(EW, start=1):
    y = 15 + 90 * k
    w(f"road {name} 30 0,{y} {SIZE-1},{y}")
    w(f"  class {'freeway' if name in FREEWAY else 'mainroad'}")
w()
w("# ---- the dirt tracks ------------------------------------------------------")
# In the MARGIN, outside both the block interiors and the road grid. Run into
# either and MapAudit is right to complain: a track that ends inside an arterial
# corridor is a junction nothing built, and one laid over a farmyard is a barn
# standing in the road.
# ALONG THE MARGIN, AND ACROSS A ROAD. Two constraints, and missing either one
# breaks something different. Run a track through a block interior and it is laid
# over a farmyard; leave it meeting nothing and it is an ISLAND - lanes with no
# junction at either end, so the cars given to it drive off the tarmac and out of
# the world, which is what NoVehicleEverLeavesTheRoad caught at 9m past the
# asphalt. The margin above y=30 and below y=810 is clear of every block, and
# each track crosses one arterial so it has somewhere to come from.
w(f"road hometrack 10 0,15 150,15")
w("  class track")
w(f"road backtrack 10 690,{SIZE-15} {SIZE-1},{SIZE-15}")
w("  class track")
w()

# ---------------------------------------------------------------- packing
class Block:
    """Shelf packer for one 60x60 block, with a metre of air round every plot."""
    GAP = 2
    def __init__(self, i, j):
        self.x0, self.y0 = bx(i), bx(j)
        self.cy = self.y0 + 1        # top of the current shelf
        self.cx = self.x0 + 1        # next free x on it
        self.shelf = 0               # tallest thing on the current shelf

    def fit(self, pw, ph):
        if self.cx + pw > self.x0 + 59:                 # wrap to a new shelf
            self.cy += self.shelf + self.GAP
            self.cx, self.shelf = self.x0 + 1, 0
        if self.cy + ph > self.y0 + 59:
            return None
        x, y = self.cx, self.cy
        self.cx += pw + self.GAP
        self.shelf = max(self.shelf, ph)
        return x, y

def emit(rec, x, y, note=None):
    w(f'place {rec["kind"]} {x},{y} {rec["w"]}x{rec["h"]} "{rec["name"]}"')
    if rec.get('door'):
        dx, dy = rec['door']
        w(f"  door {min(max(x+dx, x), x+rec['w']-1)},{min(max(y+dy, y), y+rec['h']-1)}")
    if rec.get('units'):
        w(f"  units {rec['units']}")
    if rec.get('human'):
        w(f"  human {rec['human']}")

# ---------------------------------------------------------------- downtown
# Biggest first so the awkward footprints get the clean ground, then the parades
# fill in behind them. One tower, not two: a town of a thousand has one tall thing.
DOWN_KINDS = ["tower", "casino", "hospital", "cinema", "school2", "firestation",
              "precinct", "bank", "villagehall", "pub", "postoffice", "gasstation",
              "shop", "icecream", "diner", "carwash", "restroom", "newsstand", "carpark"]
items = []
for kind in DOWN_KINDS:
    picks = by_kind.get(kind, [])
    if kind == "tower": picks = picks[:1]
    if kind == "carpark": picks = picks[:3]
    items += picks
items.sort(key=lambda r: -(r['w'] * r['h']))

# One small apartment terrace, four units of three, which is the only home downtown.
flats = []
for n in range(4):
    src = by_kind['apartment'][n]
    flats.append(dict(src, units=3, name=["Market Chambers", "Market Chambers, 2",
                                          "Market Chambers, 3", "Market Chambers, 4"][n],
                      human="Over the shops, and the stairs go up from the street." if n == 0 else None))

blocks = [Block(i, j) for (i, j) in DOWNTOWN]
used = set()
w("# " + "=" * 74)
w("#  DOWNTOWN - three blocks square, and everything commercial is in it")
w("# " + "=" * 74)
w()
placed, spill = 0, []
for rec in items + flats:
    for b in blocks:
        at = b.fit(rec['w'], rec['h'])
        if at:
            emit(rec, at[0], at[1]); placed += 1; used.add((b.x0, b.y0)); break
    else:
        spill.append(rec)
w()

# ---- and city fabric in whatever downtown block the shops did not need -------
#
# THE LEFTOVERS ARE NOT EMPTY LOTS. Packing fills a block before it starts the
# next, so authoring fifty plots into nine blocks leaves the last few bare - and
# four dense blocks beside five vacant ones is a retail park, not a downtown.
# A `district` is exactly the thing that fills a block with frontage nobody had
# to author, which is what the rest of a downtown IS: buildings whose only job is
# to be the wall of the street. No `units` on them, because downtown houses
# nobody now - the one terrace above the shops is the whole of it.
w("# ---- the rest of the street wall ------------------------------------------")
fabric = 0
for n, (i, j) in enumerate(DOWNTOWN):
    if (bx(i), bx(j)) in used: continue
    fabric += 1
    w(f'place district {bx(i)},{bx(j)} 60x60 "{["Market Square", "the Exchange", "Old Town",
        "the Arcade", "Cornmarket", "the Shambles", "Bridge Street", "the Quarter",
        "Cathedral Close"][n]}"')
    w("  human Frontage, and behind it the yards nobody has a reason to walk into.")
w()

# ---------------------------------------------------------------- homes
w("# " + "=" * 74)
w("#  THE HOUSING, growing out of downtown")
w("#")
w("#  Forty cells, `units 11` apiece - 440 households, which at the measured 2.25")
w("#  people to a household is the thousand. CitySuburb lays each cell out as")
w("#  detached houses with drives and garages; the cell is the home rather than")
w("#  the house because WorldModel's place array is fixed at load and a generator")
w("#  cannot register a place. Nearest cells to downtown first.")
w("# " + "=" * 74)
w()
NAMES = ["Beechwood Rise", "The Larches", "Coronation Avenue", "Northbound Gardens",
         "Elm Walk", "Hazel Close", "The Chase", "Wicker Rise", "Sycamore Drive",
         "Orchard Way", "Priory Close", "Fairfield", "The Paddocks", "Meadow Rise",
         "Cornmill Lane", "Old Forge Close", "Kingsley Avenue", "Windmill Rise",
         "The Coppice", "Rectory Gardens", "Alder Grove", "Bramble Way",
         "Chestnut Avenue", "Dovecote Close", "Ely Road", "Fernhill", "Garland Way",
         "Hawthorn Rise", "Ivybridge Close", "Juniper Way", "Kestrel Drive",
         "Linden Avenue", "Mulberry Close", "Nightingale Rise", "Oakfield",
         "Pytchley Way", "Quarry Close", "Rowan Avenue", "Saffron Rise", "Tanners Way"]
HUMANS = [r['human'] for r in by_kind['suburb'] if r['human']]

def dist(c):
    i, j = c
    return max(abs(i - 4), abs(j - 4))
for n, (i, j) in enumerate(sorted(HOMES, key=lambda c: (dist(c), c))):
    w(f'place suburb {bx(i)},{bx(j)} 60x60 "{NAMES[n]}"')
    w("  units 11")
    if n < len(HUMANS): w(f"  human {HUMANS[n]}")
w()

# ---------------------------------------------------------------- the edge
w("# " + "=" * 74)
w("#  THE GREEN EDGE - outside the road grid, where the story happens")
w("# " + "=" * 74)
w()
EDGE = [(i, j) for j in range(0, 9) for i in range(0, 9)
        if i in (0, 8) or j in (0, 8)]
country = []
for kind in ["farm", "farmyard", "barn", "silo", "cornfield", "paddock", "orchard",
             "copse", "green", "playground", "allotments", "churchyard",
             "memorial", "standing", "camp"]:
    for r in by_kind.get(kind, []):
        r = dict(r)
        # The old fields were 110x50 and ran out of their own block; the edge here
        # is 60 wide, so they are cut to fit rather than left to overlap.
        r['w'], r['h'] = min(r['w'], 50), min(r['h'], 50)
        country.append(r)

# NUDGED OFF THE GRID, deliberately. Shelf-packing puts everything flush to the
# same two edges, so the countryside came out as a filing cabinet - which is the
# one place on the map that should not read as laid out by anybody. A field is
# where the hedge happened to end. The nudge is bounded by whatever room the
# packer left, so nothing can be pushed into a neighbour or into a road, and it
# is seeded so the same map comes back every time.
jitter = random.Random(20260801)
NUDGE = 6
eblocks = [Block(i, j) for (i, j) in EDGE]
for rec in country:
    for b in eblocks:
        # The slack is RESERVED before packing, not taken afterwards. Nudging a
        # plot after the packer has already advanced past it walks it straight
        # into its neighbour - six overlaps, and MapAudit caught every one.
        at = b.fit(rec['w'] + NUDGE, rec['h'] + NUDGE)
        if at:
            emit(rec, at[0] + jitter.randint(0, NUDGE), at[1] + jitter.randint(0, NUDGE))
            break
    else:
        spill.append(rec)

open(os.path.join(os.path.dirname(__file__), "city.new.txt"), "w",
     encoding="utf-8", newline="\n").write("\n".join(out) + "\n")

print(f"downtown placed {placed} of {len(items)+len(flats)} in {len(used)} blocks, {fabric} blocks of fabric")
print(f"homes {len(HOMES)} cells x 11 units = {len(HOMES)*11} households"
      f" + 12 flats -> ~{round((len(HOMES)*11+12+2)*2.25)} people")
print(f"country {len(country)-len([s for s in spill if s in country])} placed")
if spill:
    print("SPILLED:", collections.Counter(r['kind'] for r in spill))
