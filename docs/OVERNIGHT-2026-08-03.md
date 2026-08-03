# Overnight, 2026-08-02 into 03

You went to bed asking for four things: the health of the app, the Rossville terrain looking
right with no trees and no homes, layers you can strip in the GUI, and elevation / roads /
railroad visible so the town can start being a real place.

## Look at these first

| file | what it is |
|---|---|
| `docs/snapshots/layers-town-ground.png` | **the one you asked for** — the town from the air with nothing planted or built on it |
| `docs/snapshots/layers-country-ground.png` | the same, low oblique from the south-west |
| `docs/snapshots/layers-town-all.png` | everything switched on, for comparison |
| `docs/snapshots/layers-town-notrees.png` | trees and houses off, civic buildings still standing |

Then press Play and hit **L**.

## What is new

### Sixteen layer switches, and the town comes up whole now

`L` opens a panel. Every row is a click. Three presets at the top: **All**, **None**, and
**Ground + roads** — the last being the view above, one button.

Streets · Parking · Road signs · Traffic lights · Railroad · Elevated rail · Power lines ·
Farm · Civic buildings · Downtown blocks · Houses · Story props · Trees & hedges · Street
lighting · Traffic · People

The default changed. Play used to open on the dark survey plan, which is why twice you pressed
Play and thought the town was missing. It now comes up whole, and anything in the way comes off
with a click.

**Nothing here changes the simulation.** A hidden car still drives its lane and is still counted
by the jam instrument; a hidden villager still goes to work. This decides what is *drawn*.

**Why it did not exist before.** `CityChunker` combines every renderer under a node into a
handful of meshes and destroys the originals — so the single bake over the city had already
turned the trees and the walls into the same mesh before anything could switch them off. Each
layer is now baked separately under its own root, and the root is what toggles.

I got that wrong the first time and the renders caught it: with the trees switched off, the
picture came back **byte-identical** to the one with them on, 2,193,825 bytes both. I had baked
each layer and then baked their parent, which undid it. Fixed in `534e6fa`, and removing the
extra bake made it faster as well.

### The ground is the pack's own 2048px PBR now

`Content/textures/` held ten 256×256 PNGs whose own generator calls them "honest placeholders" —
flat albedo, no normal map, so the ground took light like paper. The Poly Universal Pack you
already own ships full PBR ground sets at 2048 with normal and occlusion, and nothing had ever
read them.

Grass and churchyard take `Nature/Grass_A`; field takes `Farm/Ground_Dirt_Stubble`, which is
what a harvested Illinois field actually looks like; path takes `Ground_Dirt_Flat`; road takes
`City/Asphalt_A`; floor takes `City/Concrete_A`.

Field and path deliberately do **not** share a texture, though both are dirt — giving them one
made a farm track vanish into the field it crossed.

### The elevation is real, and now provably so

24.5m of relief over 2100×2400 metres is a **one percent grade**. Too gentle to see — so "the
ground looks flat" is what correct elevation looks like here, and also what a bug that dropped it
entirely would look like. Indistinguishable by eye, so `Noir/Probe The Elevation` reads the
numbers instead:

```
grid relief   24.02m
mesh relief   24.85m   across 600,780 vertices
```

The mesh carries it. And the section along Attica is the calibration proving itself — the ground
reads exactly **0.0m at x=750**, the Chicago Street crossing every other measurement in this
project is taken from. West edge −4.9m, rising to +8.5m around x=1200, back to +2.5m at the east
edge: the town sits on a low rise east of Route 1, which is why the river is out west and down.

### Chicago Street bends

`Content/city.txt` now declares Illinois Route 1 on its **real surveyed alignment** — the
14-point polyline from OpenStreetMap way 22037977, rotated into the parcels' frame — instead of
a straight line at x=750.

Sampled every 12m: **0 of 112 points** fall inside a county lot along the real centreline;
**95 of 112** did along the straight one. We had been drawing the state highway through the
middle of the town's back gardens.

## The health of the app

Full audit in `.superpowers/overnight/health-report.md` (341 lines). The short version: **good,
with a short list of specific, fixable gaps** — and the reason it is good is that almost
everything unfinished is *already written down by the project itself*, usually more precisely
than an audit could put it.

- **229 headless tests + 13 PlayMode.** The one permanently-red test (`TwoToOneTests`) is a
  design target kept honest, not a hidden bug. Architecture is enforced by reflection tests
  rather than convention.
- **Privacy is verifiably clean.** The auditor checked the raw `tools/rossville-property-records.json`
  itself rather than trusting the project's claims: **794 records, no owner names in any field.**
  The NO REAL RESIDENTS rule holds at the point of data collection, not just in derived files.
- **The dialogue LLM does not exist in code.** `Assets/LLMUnity` is vendored, but repo-wide search
  finds no `ILLM` port, no Anthropic client, no dialogue system. It is scoped as future work in
  your own design doc — designed, not built. Worth knowing before you plan around it.
- **The witness/evidence layer is built and tested but deliberately unreachable** from the
  playable game — proven by a passing test literally called `NothingInTheGameReferencesWitnessYet`.
  Sequenced work, not neglect. (Two file headers claiming "nothing constructs a Sighting" are now
  stale — `Recollection.cs` does.)

**Top things worth fixing, in the auditor's order:**

1. **Three `Content/` files have no committed regeneration script** — `parcels.txt`,
   `parcel-county.txt`, `elevation.txt`. Already blocking: the curved-roads Phase C work needs to
   rebuild `parcels.txt` with a corrected rotation, and cannot.
2. **The curved-road `Way`-label inversion**, before traffic work goes further. *Not live tonight*
   — I checked: Chicago is declared in increasing y order, and the bug only bites a curve declared
   the other way. Dormant, but it is sitting under live traffic now.
3. **`3` of the "13/13" PlayMode tests can never fail** (unconditional `Assert.Pass()`), so that
   number overstates the gate. 8 of 13 are real checks.
4. **`docs/STATE.md`'s "RESUME HERE" banner is stale** — still describes the superseded 960×960
   map. `HANDOFF.md` has replaced it as the entry point but nothing says so.

## What is NOT done, and why

- **Hedges survive the Trees switch.** `VillageMesh` draws hedges as runs *into the terrain
  mesh* rather than placing them like `CityGreenery` does, so there is no object to switch off.
  Visible in `layers-country-ground.png` as green lines still standing between the fields.
  Fixing it means giving hedges their own node, which is real work rather than a tweak.

- **The field/grass boundary is a hard edge.** Where the platted town stops and farmland starts
  there is an abrupt colour change rather than a transition. Real, and it looks synthetic from
  the air.

- **No foreclosure or property-distress records were used.** You said "even if it is foreclosure,
  etc, we are not using real names — YET". I stopped short deliberately. That data is inherently
  about identifiable people in financial trouble, your repo already carries a written
  NO REAL RESIDENTS rule (`docs/IDEAS.md`, "Decisions needed"), and it is a decision to make in
  daylight rather than at 2am while you are asleep. The assessor's own valuation data — already
  in `tools/rossville-property-records.json` — gives housing quality without naming anybody, and
  that stays fair game.

  Everything used tonight is geographic: USGS elevation, OpenStreetMap roads/rail/water, county
  parcel boundaries.
