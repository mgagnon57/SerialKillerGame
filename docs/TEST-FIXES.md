# Test fixes — the work list

**This is a work file. Delete it when the last item lands.** A read-only audit on 2026-08-09 walked
all 490 test methods in both suites. The facts live in `CLAUDE.md`; this is a queue.

**Sibling plans:** `docs/ANIMATION-FIXES.md`, `docs/ROAD-FIXES.md`, `docs/SIM-FIXES.md`.
**W5 of this plan is the same work as `SIM-FIXES.md` W7** — see [Cross-plan](#cross-plan) before
starting it. `docs/TEXTURE-FIXES.md` **is finished and deleted** (2026-08-09).

**Item IDs:** `KEY` the content-key gates · `CROP` the unwired crop model · `DIAG` the PlayMode
diagnostics · `BASE` the road baseline · `AIM` where the behaviour tests point · `COV` the uncovered
Unity layer.

---

## ⚠ Read this before you delete a single test

**"Bad tests" is the wrong model of this suite and acting on it will make things worse.** The audit
looked for the usual rot and did not find it:

> 465 Core test methods. **One** has no assertion, and it is a deliberate reporter.
> **Zero** tautologies — no `Assert.True(true)` or relative anywhere in either suite.
> **Zero** dead references. Every diagnostic is quarantined with its reason in the attribute string.

Almost nothing here needs deleting. **What is wrong is where the tests are pointed**, and two of the
three big items are decisions rather than defects. Specifically:

- **Do not delete the PlayMode diagnostics.** They carry 150 minutes of timeout and four of them
  assert nothing, which reads like pure cost. They are the only instruments the project has for
  frame time, layer isolation and traffic tails, and `CLAUDE.md` records that deleting or shortening
  instruments is how the 2:1 gap got mis-diagnosed for months. W3 makes them *able to fail*; it does
  not remove them.
- **Do not move `PeopleTests` and `WorldTests` onto Rossville as a single commit.** That reshuffles
  the entire town and voids every committed baseline. `SIM-FIXES.md` reserves exactly two reshuffles
  in named waves and this is not one of them. W5 exists to say: this work is already planned
  elsewhere, go and do it there.

**The one genuine defect the audit found is `KEY`, and it is small.** Start there.

---

## The build order

| | Wave | Costs | Unity? |
|---|---|---|---|
| **1** | `KEY` — the content-key gates, and the one live leak | half a day | one look |
| **2** | `CROP` — wire the crop model or retire it | 1–2 days | yes |
| **3** | `DIAG` — the diagnostics learn to fail | half a day | one PlayMode run |
| **4** | `BASE` — the road baseline gets a property, not just a pin | 1 day | no |
| **5** | `AIM` — the behaviour tests move to Rossville | **deferred to `SIM-FIXES` W7** | — |
| **6** | `COV` — the 43 unnamed Unity classes | open-ended | yes |

W1 and W4 move no draw and need no re-baseline. W2 moves the countryside. W3 moves nothing but log
lines. W6 is a backlog, not a sprint.

---

## The waves

### W1 — The content-key gates · half a day · Core only, plus one look

`KEY-0` `KEY-1` `KEY-2` `KEY-3` `KEY-4` `KEY-5` `KEY-6`

**`kinds.txt` declares three key columns and each one has a different policy for a name nothing
answers to. Two of the three are right. The third is silent, and it is leaking now.**

| Column | Unknown value does what | Where |
|---|---|---|
| `grammar` | **Throws at load, with the kind name and line number** | `PlaceKindTable.cs:660` |
| `massing` | **Warns once per bad name, falls back to `cottage`** | `MassingGrammars.cs:44-53` |
| `frontage` | **Nothing. Hangs a generic nameboard and says not one word** | `Frontage.cs:392` |

The two working policies are not accidents — both carry the argument in a comment. `PlaceKindTable`:
*"A grammar nothing answers to would fall back to the house rules, and a hospital built to the house
rules is a 330 m² kitchen with two bedrooms off it. Better the load stops than that it is discovered
by looking at the building."* `MassingGrammars` explains why it warns instead of throwing: it lives
in Unity, Core must not learn about Unity, so the check moved to build time.

**Frontage got neither, and its own doc comment describes this exact bug being fixed once already** —
for `factory`, which said `frontage mill` and got a plain nameboard because the switch was keyed on
`PlaceKind`. The fix was to switch on the frontage *style*. The **guard** one layer up was left
keyed on the enum.

#### The measured gap

```
kinds.txt declares 13 frontage values.  Frontage.cs has a case for 10.
  unanswered:  none (28 kinds)   dwelling (2 kinds)   shopfront (3 kinds)

kinds.txt declares 10 massing values.  MassingGrammars' registry has 14 keys.
  unanswered:  shed (2 kinds)   — warns and renders as a cottage
```

**`KEY-0` — Measure it before fixing it. Half an hour.**
A board is only hung if the place clears `CityBuildings.Handles`, is not `PlaceKind.Dwelling`, and
has a **valid street front**. Most of the 28 `frontage none` kinds are open ground — cemetery, park,
copse, cornfield — and probably have no door, so they never reach the switch. **Confirmed to reach it
today: `bank`, `icecream`, `newsstand`**, all three `frontage shopfront`, none a Dwelling, none
handled by a bought model. Count the real total in the built town before writing the fix — the answer
decides whether `KEY-3` is three lines or thirty.

**`KEY-1` — `TheFrontageColumnNamesSomethingFrontageAnswersTo`. Core test.**
Reads `Content/kinds.txt` and the *text* of `Assets/Noir/Unity/Frontage.cs`, extracts the `case "…"`
labels, and asserts the declared set is a subset of the answered set.

> **This is an existing idiom, not a new one.** `TownPipelineTests` already reads Unity source as
> text from a Core test — `RepoRoot()` walks up from `AppContext.BaseDirectory` looking for
> `Assets/Noir/Core/Observation`, then `File.ReadAllText` and `Strip()`. Copy that helper. It does
> **not** breach the assembly boundary: Core reads a file, it does not reference a type.

**Expect this test to be RED when it lands.** Land it red, in its own commit, with the three
unanswered names in the failure message. That is the point of it.

**`KEY-2` — `TheMassingColumnNamesSomethingTheRegistryAnswersTo`. Core test.**
Same shape, against the `Registry` dictionary keys in `MassingGrammars.cs`. Expect one failure:
`shed`. Note the registry also carries four keys **no content row names** — `farmhouse`,
`foursquare`, `bungalow`, `ranch` — which is deliberate and documented (models being prototyped).
**Assert subset, never equality**, or this test fails on purpose-built spare capacity.

**`KEY-3` — Answer the three frontage names.** Owner decision on two of them:
- `none` → almost certainly `return 0`. Content says the building has no frontage; drawing a
  nameboard on it is the bug.
- `dwelling` → `return 0` as well. Houses do not carry trade signage in 1991 Rossville.
- `shopfront` → **this one is a real design question.** `bank`, `icecream` and `newsstand` are
  Main Street terrace units and are already massed as `MainStreetMassing`. They want a fascia
  sized to a terrace bay, not the 2.4 m × 0.45 m generic board. Ask before inventing one.

**`KEY-4` — Fix the guard at `Frontage.cs:86`.** `if (place.Kind != PlaceKind.Dwelling)` is an enum
test standing in for a content value. `apartment` declares `frontage dwelling` and is not
`PlaceKind.Dwelling`, so it slips past. Once `KEY-3` gives `dwelling` a `return 0`, **delete the
guard entirely** — the switch then carries the whole decision, which is what the file's own comment
argues for.

**`KEY-5` — `massing shed`.** Two kinds ask for it; the registry has no such grammar. Either add a
`ShedMassing` or change the two rows. A shed rendered as a cottage is the smaller of the two wrongs,
so this is not urgent — but it must stop being invisible.

**`KEY-6` — Give `Frontage` the warn-once policy `MassingGrammars` already has.** Copy the
`_warned` HashSet pattern verbatim, including the reason: once per bad name, not once per building,
or one typo in a common kind buries the log. With `KEY-1` green this should never fire — which is
exactly when a warning is worth having.

---

### W2 — The crop model that is tested and unread · 1–2 days · Unity

`CROP-0` `CROP-1` `CROP-2` `CROP-3` `CROP-4`

**Core `Fields` knows which crop stands where, how tall it is, and whether it blocks a sightline,
from position and calendar date. Fifteen tests cover it. The game never asks it a question.**

```
Fields ──▶ StandingCrop ──▶ ISightBlocked ──▶ Recollection(blocked:)
                                                      │
VillageHost.cs:1057   AskInEnglish(World, People, …, Track, Seed)   ◀── no blocked argument
Sightlines.cs:78      return blocked == null || !blocked.Between(…)
```

`StandingCrop` is constructed **once in the whole repository**, at
`tools/Noir.Core.Tests/ObservationDiagnostic.cs:146` — a test, and an `[Explicit]` one.
`SightBlocked.cs:28` already says so: *"Fields.BlocksSightline was built to say so and was read by
nobody."*

**Meanwhile the crops the game actually draws come from somewhere else entirely.** `CityFarm` picks
both the crop and its growth stage from a position hash with no date in it:

```csharp
var crop = Crops[Materials3D.Scatter(lot.X, lot.Y, 1301) % (uint)Crops.Length];   // CityFarm.cs:138
```

A field looks identical on 1 January and 15 August. Its crop list is wheat, sunflowers, potatoes,
beet and pumpkins; `FieldsTests` asserts corn planted ~5 May and soybeans ~25 May, which is what
Vermilion County actually grows.

**`CROP-0` — Owner decision, and nothing in this wave starts before it.** Three honest options:

1. **Wire it.** Witnesses stop seeing through standing corn, and the fields change through the year.
   Costs `CROP-2` through `CROP-4`. This is what the code was written for.
2. **Retire it.** Delete `Fields`, `FieldCondition`, `StandingCrop`, `ISightBlocked`,
   `FieldsTests` and `CountrysideDiagnostic`. Saves ~500 lines and 15 tests that currently protect
   nothing. **`ISightBlocked` is a seam the witness layer may want later** — retiring it is cheap to
   undo from git and expensive to re-argue, so say so in the commit message.
3. **Leave it, and say so in `CLAUDE.md`.** The worst option only if it stays undocumented — an
   unwired-but-tested subsystem that nobody has written down is how a session spends a day
   "fixing" a crop bug in the wrong file.

**`CROP-1` — Whichever way `CROP-0` goes, the audit finding gets a home in `CLAUDE.md`.** One line
under *Traps*. This costs nothing and prevents the wrong-file day.

**`CROP-2` — Pass the blocker from the game path.** `VillageHost.AskWhatTheySaw` gains
`new StandingCrop(World)`. Cheap. Construct it **once and cache it** — it is asked per sightline.

**`CROP-3` — A test that exercises the GAME path, not a double.** `WitnessTests` covers occlusion
with `AWallOfCorn` and `OpenGround`, which are test doubles for an interface the game supplies no
implementation to. That is why the suite stayed green through the whole unwired period. The new test
must assert that **`VillageHost.AskWhatTheySaw` itself** refuses a sightline through August corn —
which puts it in `Noir.PlayTests` beside `WitnessPlayTests`, not in Core.

> **This is the general lesson of the audit and it is worth writing on the wall.** A test double
> satisfying an interface proves the *interface* works. It proves nothing about whether anything
> supplies one.

**`CROP-4` — Give `CityFarm` the date.** It should ask `Fields.At(x, y, year, dayOfYear)` for the
crop and the growth stage rather than hashing position. Two consequences to plan for: the drawn crop
list must reconcile with Core's `Crop` enum (Core knows corn and soybean; `CityFarm` draws
sunflowers, potatoes, beet and pumpkins, which is Iowa-catalogue rather than Vermilion County), and
**every field in every render changes**, so this must not land in the same commit as anything else
visual.

---

### W3 — The diagnostics learn to fail · half a day · one PlayMode run

`DIAG-1` `DIAG-2` `DIAG-3`

150 minutes of timeout budget sits behind seven `Category("Diagnostic")` tests. Four of them assert
nothing at all:

```
 30.0 min   Tour                    asserts an image-file count
 30.0 min   PerfCensus              Assert.Pass("a census, not a gate - read the log")
 30.0 min   LayerProof              asserts an image-file count
 15.0 min   TrafficDiagnostics x3   Assert.Pass()
 15.0 min   FilmStrip               asserts an image-file count
```

They are correctly excluded from the gate and **they should stay**. The problem is narrower: a
diagnostic that cannot fail is worth exactly as much as the last time somebody read its log, and
nothing in the repo records when that was.

**`DIAG-1` — Give each one a floor it can trip over.** Not a gate — a floor, asserting only that the
instrument was pointed at something:
- `PerfCensus` — the animating count is non-zero, and the two baseline samples agree within 15%.
  Both numbers are already computed and printed; only the assertion is missing. `CLAUDE.md` warns
  that an 80 m cull once measured 712 fps on a town with `0 of 1385 animating` — **that is precisely
  the failure this floor catches**, and it went unnoticed because the test could not fail.
- `TrafficDiagnostics` ×3 — a non-zero vehicle count, and at least one junction observed.
- `Tour` / `FilmStrip` / `LayerProof` — already assert file counts. Add that the files are non-empty;
  a zero-byte PNG is the shape a render failure takes.

**`DIAG-2` — The timeouts are three to ten times the measured runtime.** `Tour`, `PerfCensus` and
`LayerProof` carry 30 minutes each; `CLAUDE.md` measures `PerfCensus` at ~6 minutes. Take one
measured run, then set each timeout to roughly twice its own p100. A 30-minute timeout on a
6-minute test means a hang costs 24 minutes of nothing.

**`DIAG-3` — Record the date each diagnostic was last read.** One line in the class header, updated
when somebody actually looks. Cheaper than any tooling and it makes an unread instrument visible.

---

### W4 — The road baseline gets a property · 1 day · no Unity

`BASE-1` `BASE-2`

`RoadGeometryBaselineTests` pins segment counts, turn counts and a checksum for the real town. Its
header is an unusually honest changelog of every time the pin moved: the Phase B curve, the side
streets cut to real length (614 segments → 250), fifteen alleys (59 junctions → 113), the alley
corridor at 6 m then 4 m, then five alleys shifted off houses. Nineteen commits touch the file.

**A golden baseline whose answer to every failure is to re-record is a change *detector*, not a
gate.** That is a real and useful thing. But `CLAUDE.md` records the case that proves it is not
enough: nine of 68 roads left one of their own ends backwards and **Summit Street was drawn 39 m
(128 ft) off its own survey line**, and *"nothing could see it: it moves no count and fails no test."*

**`BASE-1` — Add the invariant that would have caught it.** Not a number to re-record — a property
that holds for every road at every town size: **each drawn polyline stays within a stated distance
of the survey centreline it was built from.** Sample the drawn path, measure to the declared
segment, assert the maximum. Set the bound from a measured run plus headroom, and put the reason in
the message.

> **Do not set this bound tight enough to fight the smoothing curve.** The owner ruled on
> 2026-08-09 that corners stay rounded — a real street corner has a turning radius. This test must
> catch a road leaving its own end backwards, **not** a corner with a radius on it. If it cannot
> separate those two, it is the wrong test and should not land.

**`BASE-2` — Make re-recording deliberate.** The failure message should name the ritual: re-record
only with a one-line reason appended to the header, in the same commit as the change that moved it.
The header already works this way by custom; the test should say so out loud.

---

### W5 — Where the behaviour tests point · **deferred**

`AIM-1`

**`PeopleTests` (48 methods) and `WorldTests` (42) — the behavioural heart of the simulation — load
`fixture-village.txt`**, which is the retired Ashcombe village at 170×120 tiles and ~148 people.
Rossville is 2100×2400 and ~1,300, and `CLAUDE.md` records that the 1,300 is load-bearing because
population scales off `WorldModel.Households`.

```
Core test methods by the town they load
  synthetic / in-memory     303   65.2%   36 files
  Rossville (city.txt)       86   18.5%   11 files   ← every one is roads, lanes, driveways, doors
  Ashcombe fixture           76   16.3%    2 files   ← PeopleTests, WorldTests
```

**No test measures how people behave in Rossville.**

**`AIM-1` — Do this inside `SIM-FIXES.md` W7, not here.** That wave already re-points the 2:1 rule
and re-baselines on the real town, already reserves the reshuffle, and already carries the
acceptance criterion (`WHO-B6`'s two population invariant hashes). Doing it twice, in two plans, is
how two baselines drift apart. **This item exists only to point at that one.**

The fixture keeps genuine value afterwards and should **not** be deleted: it is small, fast,
hand-checkable, and a deterministic 148-person town is the right instrument for a unit test of a
rule. The fault is that it is the *only* instrument, not that it exists.

---

### W6 — The 43 classes no test names · open-ended

`COV-1` …

13,347 of the 30,360 lines in `Assets/Noir/Unity` sit in files that no test in either suite so much
as mentions.

```
1464  VillageMesh        552  PerfProbeRunner    421  CityDistrict
 869  ParcelNotes        478  OrbitCamera        417  CityFarm
 649  CityOutlines       476  MeshChunks         411  AgentFigure
 637  Frontage           422  CitySigns          368  CityParking
```

**Do not try to cover this by line count.** Take it in the order the audit's own findings suggest,
because each one is a class of defect already proven to occur:

1. **`Frontage`** — covered by W1. Content keys silently unanswered.
2. **`CityFarm`** — covered by W2. A model duplicated in two places that disagree.
3. **`MeshChunks` / `CityChunker`** — `CLAUDE.md` records `CityChunker` has no editor guard at all
   and combines an empty scene in a player. That is Core-testable in part.
4. **`OrbitCamera`** — `CLAUDE.md` records it opens at 330 m, which silently made an animator-cull
   measurement meaningless. A test that pins the opening distance is four lines.

The rest are renderers, and renderers are the honest limit of automated cover — `CLAUDE.md` is right
that the tests cannot see ugly. **Look at it** stays the gate for those.

---

## Cross-plan

| Collision | With | Resolution |
|---|---|---|
| `AIM-1` moving the behaviour tests to Rossville | `SIM-FIXES.md` W7 | **W7 owns it.** This plan only points. |
| `CROP-4` re-drawing every field | — | **The texture pass is done and its plan is deleted** (2026-08-09), so the collision is gone: land `CROP-4` alone so one render answers one question, but nothing is waiting on it now. |
| `BASE-1` road geometry invariant | `ROAD-FIXES.md` | `ROAD-FIXES` may move road counts; land `BASE-1` **after** it, or re-derive the bound twice. |
| `DIAG-2` cutting timeouts | `ANIMATION-FIXES.md` | `WhyAreThePeopleNotAnimating` is 292.9 s of the gate and is that plan's instrument. Do not touch its timeout. |

---

## The gate

Take the baselines from `CLAUDE.md` and nowhere else — it wins by its own rule, and six documents
once held six different numbers for the Core suite.

| Wave | Core | Unity compile | PlayMode | Look at it |
|---|---|---|---|---|
| W1 | **+2, and one lands RED on purpose** | yes | no | yes — the three shopfront kinds |
| W2 | −15 if retired, +0 if wired | yes | **+1** (`CROP-3`) | yes — every field changes |
| W3 | no | yes | one run, no count change | read the logs |
| W4 | **+1** | no | no | no |
| W5 | see `SIM-FIXES` W7 | — | — | — |
| W6 | +N | yes | maybe | yes |

**`dotnet build Noir.PlayTests.csproj -c Debug` before every PlayMode run** — `CLAUDE.md` records two
eighteen-minute runs lost to compile errors in the test assembly itself.
