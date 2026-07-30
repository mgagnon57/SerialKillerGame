# Where we are

## RESUME HERE — 2026-07-30. Northgate rebuilt at city scale, on the road kit we already owned.

**Safe to reboot.** Everything below is committed. Core builds, Unity compiles, `PlayCheck` says
Play should work, and five stills are in `docs/snapshots/`.

### The finding that made the day

`Prefabs/City/Roads City/` — 48 pieces, 30m module, painted lanes, zebra crossings, stop lines,
lay-bys, parking aprons, and matching junction sets. **Never opened.** The whole city had been
built out of `Modular Parts/Roads`, the generic 10m kit whose "main road" is six metres of
unpainted asphalt. Measured, per corridor:

| class | corridor | asphalt | lanes each way |
|---|---|---|---|
| freeway | 30m | 24m | **2**, solid orange centre |
| mainroad | 30m | 12m | 1, dashed centre |
| street | 10m | 6m | 1, unmarked |

The pack has **no centre turn lane** — checked every piece. The freeway's two-lanes-each-way is
the nearest real thing and is better for us anyway.

### Four bugs, all mine, all from not measuring

1. **The asphalt has never once been drawn.** A road tile is two planes: pavement at y=0 and
   asphalt at **y = −0.1**. `CityStreets` lifted its root 0.04 to stop the pavement z-fighting
   and nobody checked the other plane, so the carriageway sat at −0.06, *under* Ashcombe's ground.
   Every "road" in every screenshot to date was the village renderer's own road colour showing
   through the hole. Root is now 0.12.
2. **Every east-west road tile was laid a full cell north of its corridor.** These tiles pivot at
   the corner opposite the origin; a yaw of 90° rotates the footprint clean off the pivot. Tiles
   are now seated by **measured bounds** (`CityStreets.Seat`). This is the rest of the
   cars-on-the-pavement complaint.
3. **`place railway` erased Second Street.** `railway` was `form open, ground path`, and open
   places stamp their ground *after* roads — so the El paved over the street it runs above.
   `kinds.txt` now gives it `ground road`.
4. **`RenderNoon` came back dark** because `SunRig.SetLanternGlow` threw on a lantern the chunker
   had already combined away, aborting `Render()` before any capture. Both glow setters now
   null-check.

Also: `school2` mapped to nothing while `school` mapped Ashcombe's village school to the pack's
American elementary. Fixed.

### What is new

- **`Core/World/RoadNetwork.cs`** — `RoadClass`, `RoadLine`, `Junction`, `RoadNetwork`, reachable
  as `world.Roads`. Roads used to be rasterised into terrain and thrown away, which left every
  later pass inferring corridors by sampling tiles. Centre lines are derived from how
  `WorldBuilder` actually strokes a road, not from the declared number. 9 tests.
- **`class` on a road in the map**, because width alone cannot tell a freeway from a mainroad.
  Mismatched width against class is a parse error, not a silent misdraw.
- **`Unity/CitySignals.cs`** — a post on every approach, NS/EW phases with amber and all-red,
  37s cycle offset per junction. The pack's traffic light **cannot change colour** (lens colours
  are baked into the atlas UVs; proved with `SignalProbe`), so state is shown by an unlit
  emissive lens seated on the measured head, plus a point light.
- **`CityTraffic` rewritten** — lanes from the road class and the measured asphalt, right-hand
  driving, stop lines at every junction, easing to a stop on red. The arbitrary permanent
  right-of-way hack is gone; signals separate the flows so nothing can deadlock.
- **`Content/city.txt` is 240×240** — nine 60m blocks, four junctions, two freeway arterials
  (Northgate, Second) and two mainroads (Franklin, First). 45 places, 27 homes, 57 people.
  First outing for the school, the casino, and two skyscrapers (the Meridian is 111m).

### Verified

- Core: **148 pass / 2 fail** — the same two 2:1 gates by design (`ratio.p10` 0.53, unchanged).
- `PlayCheck`: 240×240, 45 places, 57 people in 27 households, 600 ticks without throwing.
- Stills: `city-street`, `city-junction`, `city-block`, `city-corner`, `city-terrace`, plus
  `road-kit.png` and `signal-probe.png` (the two probes that settled the design).

### Next, in order

1. **Look at it in Play.** Stills cannot show the signals cycling or the cars stopping — that
   needs the editor. Nothing else should be trusted until this is done.
2. **Cars cannot turn.** Each owns one lane end to end. Turning needs junction geometry — which
   lane feeds which — and bus routes and pedestrian crossings all want the same graph. This is
   the next real piece of work.
3. Bus stops and subway entrances are bought and still unplaced; `Mainroad_Stop_*` lay-by tiles
   exist for exactly that.
4. `Skyscraper_C` fits no current lot. The integration spec under `docs/superpowers/specs/`
   still describes the abandoned modular-parts plan.

---

## 2026-07-29, later still. A wiring audit, and two floors nobody was guarding.

Five audits for one bug class: **things built and never wired up.** Read-only, then fixed on
branch `audit-fixes`. **139 pass / 2 fail**, the same two 2:1 gates by design.

### The two you would have seen in Unity

- **The garage had a chimney on its flat roof.** `AddChimneys` ran for every place that passed
  `IsRoofed`, with nothing checking `RoofForm.Flat`. `GarageMassing` is eaves 3.4, pitch 0, so a
  stack was planted flat on the roof at 3.4 m. Fixed by giving `Massing` a `Chimneys` field so the
  *grammar* answers — not a `switch` on `PlaceKind`, which would have reopened the open-kind
  property Stage 4 bought.
- **Nobody was ever shown as talking.** `Activity.Talking` was set at `Simulation.cs:520` and read
  by nothing: 35 `Activity` cases across `VillageUI` and `Reports`, none for it. Worse, the census
  counted two people stopped in the road talking as **walking**, because `Travelling` stays true
  through a conversation. The census now tests `Talking` *before* `Travelling` and prints its own
  column.

### The most valuable fix is invisible: two floors were recorded and enforced by nothing

`Content/watched.floor` carries five thresholds. Three were ratcheted. **`ratio.p10` and
`texture.min` were written down and checked by no test at all** — `Ratio.cs` read them only to
print them.

Both *look* covered, because each has an absolute gate elsewhere. `ratio.p10`'s is
`TheTenthPercentileIsNotALock`, against the required 1.00 — **and that test already fails by
design.** A test that is already red cannot report a regression. The tenth percentile could have
fallen from 0.53 to 0.20 and nothing on the board would have changed colour.

`ProgressDoesNotReverse` now ratchets both, against the recorded floor only (not
`Math.Max(requirement, floor)` as the sight gate does — folding in an aspirational requirement
would just restate the failing gate). Both were falsified by raising the floors and confirming
they fire.

**And that exposed a number worth knowing.** Worst-of-three-seeds:

```
                floor   worst of 3   headroom
ratio.p10        0.52         0.53       0.01
texture.min         8            8       NONE
```

The `texture_min 10` in the entry below is **seed 1979 alone**. Across three seeds it is 8 —
sitting exactly on its recorded floor and exactly on the absolute G3 requirement. G3 passes by
nothing at all. It is now ratcheted, which is the only reason a slip would be noticed.

### Wired up as it appeared, or honestly labelled

- **`PersonDescription` and `Sighting` are never constructed. Anywhere — tests included.**
  `new Sighting(` and `new PersonDescription(` return zero hits repo-wide, and no file under
  `Assets/Noir/Unity` or `Assets/Noir/Editor` references `Noir.Core.Observation` at all. **The
  running game observes nothing.** Sixteen enum values across `ApparentSex`, `HeightBand`,
  `BuildBand`, `AgeBand`, `ClothingTone` and `CarriedThing` cannot be produced. The live half of
  that assembly — `Observed`, `ObservationLog`, `Salience` — is filled by `Eyewitness` and graded
  by `Ratio`, and is load-bearing. Kept, with headers on both files saying plainly that they do
  not run yet, because the firewall is worth having in place before there is a consumer. Do not
  read those files' confidence as evidence that they execute.
- **`Activity.WaitingForTheBus` is gone.** Nothing ever assigned it, and `Reports.cs` carried a
  display branch for a state no villager could be in. `village.txt` still places the bus stop and
  still says two buses a day go to Marlbury; its `kinds.txt` row is `hours none, jobs 0`, which is
  what scenery looks like. Reinstate the value only alongside a planner that queues somebody
  at the kerb.
- **`Palette` is down from fifteen members to one.** Only `Palette.Selected` was ever read;
  `Materials3D` replaced the rest when the renderer went 3D. A colour table that looks like the
  authority and is consulted by nothing is worse than none — the next person to change a colour
  changes it there and sees nothing happen.
- **`TileFlags.Water`** is produced and read by nothing; every water check asks `Terrain.Water`.
  Kept and commented, because removing it rewrites stored flags for no behavioural gain.
- **`ObservedAct.NothingInParticular`** is a legitimate zero-default so `default(Observed)` is
  inert, but nothing produces it — now documented as unreachable rather than as a default a
  watcher writes.
- **`AgentState.PathIndex` and `TalkCooldown`** have no reader outside `Simulation.cs`; documented
  as internal working state.

### What came back clean

All 18 `Noir.Sim` commands reachable and documented; every advertised flag parsed; `ratio`
correctly refuses `--days` as its usage claims. All three `-executeMethod` targets named in docs
exist. Fifteen-plus enums fully wired both ways. No second instance of the winding-order or
depth-fighting faults — the renderer now self-asserts with `AssertWinding`/`AssertFootprint`.

### Not verified, and why

Snapshots are **not** re-rendered. The garage fix changes geometry, so `elevations/garage.png` and
any snapshot containing a garage will move — that is expected, and it batches with the
re-baseline already owed after the BIOS update. **Nothing here was verified by eye in Unity.**

---

## STOP HERE — 2026-07-29, later. Beats are enacted. The predicted numbers held; one nobody predicted jumped 8x. The machine is still not to be trusted.

**Still the faulty machine below — nothing here changes that.** Every figure in this section was
taken in Release on the same unpatched i9-13900K (microcode `0x10E`, BIOS 0809, 2023-01-05) and is
**PROVISIONAL** until the BIOS update lands. Do not spend these numbers as evidence before then.

### What landed (five tasks, branch `beats`, commit `af5e132`)

1. **`Beat.RoundAbout` deleted.** It could only ever produce `ObservedManner.SomewhereNew`, and the
   act-by-manner table already read 158 of 158 on both "came out" and "walked past" for that manner
   — nobody left to make it a *new* proposition for. It was also the one beat that would have needed
   a routing change, risking the determinism guarantee for a beat that could add nothing.
2. **`Beat.Lingers` wired into `BeginDoorPause`** (`Assets/Noir/Core/Sim/Simulation.cs`), on its own
   `Rolls.Purpose("lingering")` stream so every non-lingerer's door pause stays byte-identical.
   `LingerBase = 400`, `LingerSpread = 400` — a lingerer pauses 406-810 ticks (20-40s) against the
   usual 6-11 (0.3-0.55s), sized against an observer that samples once a simulated minute.
3. **Editorial tagging pass on `Content/particulars.txt`**: 43 `# carries` and 22 `# lingers`
   clauses out of 914 (913 as the parser counts them: a UTF-8 BOM on the file's first byte stops
   `^\s*#` from matching the first comment line, so `grep -vcE '^\s*$|^\s*#'` over-counts by one
   — the file's own header already states 913). A naive `grep -c '# carries'` reads 42 and
   `grep -c '# lingers'` reads 21: each misses one
   combined-tag line — `# works carries` at :766 and `# works lingers` at :786 — where the tag text
   doesn't start immediately after `#`. `BeatIn` substring-matches anywhere after `#`; a literal
   grep on the `# carries`/`# lingers` prefix does not. Same class of blind spot as the "914, not
   1076" clause-count correction in the implementation plan (Task 3 Step 5) — worth writing down
   twice so it isn't hit a third time. Four over-tags caught in review and reverted (pocketed items
   never produced are not visible from across a road).
4. **End-to-end tests added.** The village has 9 lingerers and 16 carriers; all 9 lingerers were
   seen lingering within 2 observed days. A falsification check (pause shortened back to 0-6 ticks)
   dropped that to 1 of 9 — the test genuinely discriminates on the wiring, not on the sampling.
5. **This sweep** — strand check, full suite, `ratio`, and this entry. No code, test, or content
   touched.

### Verification run

- `strand --days 3`: nobody stranded, nobody misplaced, 0 of 158 either way. The 20-40s lingering
  pause does not leave anyone stuck in a doorway.
- `dotnet test -c Release`: **137 pass, 2 fail** — the two headline 2:1 gates
  (`TheMedianVillagerYieldsTwiceAsMuchTextureAsUse`, `TheTenthPercentileIsNotALock`), both by
  design, unchanged from before this feature.

### The `ratio` instrument, before vs. after (seed 1979, same 158-person village) — PROVISIONAL

Before = commit `905dca8`, checked out into a disposable worktree for a clean comparison, never
touching the working tree on `beats`.

```
                        before    after     delta
median ratio            1.2727   1.3182    +0.045
p10                      0.6522   0.6522     0
texture_median             25       26      +1
texture_min                 10       10       0
sight_median              5.34%    5.34%      0
sign: busiest quarter    1.0763   1.0582    -0.018
sign: quietest quarter   1.3895   1.4898    +0.100
sign gap (quiet-busy)     0.313    0.432    +0.119

"came out"  / carry          2        16     +14
"came out"  / linger         6        11      +5
"went in"   / carry         75        79      +4
"went in"   / linger        16        24      +8
```

**Against the predictions, plainly:** the design predicted `texture.median` and `texture.min`
would barely move, because the beats reach only ~25 of 158 villagers and the median and minimum
villager still hold no tagged clause. That held: `texture.min` is exactly unchanged (10 → 10) and
`texture.median` moved by one villager's worth (25 → 26). `p10` and `sight.median` are exactly
unchanged. `median` moved by +0.045 — small, and nowhere near closing the gap to the 2.0 rule.

The design also predicted the inverted sign might slightly widen, because beats land independently
of how busy a villager already is. It did widen, from a 0.313 gap to 0.432 — a small move in this
instrument's own units, not a point-or-two swing, and not something to treat as suspicious given the
prediction named it explicitly.

**`linger` moved as intended** — "came out" went from 6 of 158 to 11 of 158 (task-5's asked-for
number). **`carry` moved further than the brief called out**: "came out"/carry went from 2 of 158
to 16 of 158, an 8x jump, because task 3's editorial pass added 42 `# carries` clauses on top of
carry-tagging that was already routed before this batch of work. Not a bug — the carry vocabulary
genuinely grew — but worth flagging since only `linger` was named as expected to move.

**Of the four metrics the design made a prediction about — median, texture_median, texture_min,
and the sign gap — none moved by more than a fraction of a point.** That scoping does not extend
to the count columns: `carry` on "came out" moved 8x, as detailed above. No number is treated as a
green light; all of it is provisional pending the BIOS fix.

### Snapshots — deliberately NOT re-rendered

Per this task's brief, the Unity snapshot render is deferred until after the BIOS update. Positions
will differ for the 9 lingerers and whoever is near them when it is finally run — any *other*
difference at that point is a real defect, not an artifact of this change.

Full instrument output, both before and after, is in
`.superpowers/sdd/2026-07-29-enacting-beats/task-5-report.md`.

---

## STOP HERE — 2026-07-29. THE MACHINE IS FAULTY. Read this before believing any red test.

`dotnet test` in **Debug** crashes the test host on this machine, and the crashes are not
code. They are faults that cannot occur in correct execution:

- `_blocks[i]` with `i=8` on an array whose `Length` is `9` threw `IndexOutOfRangeException`
  — instrumented and printed; the index was in range and the access faulted anyway.
- `AccessViolationException` in `Rolls.Bits` (four lines of integer arithmetic), in
  `Pathfinder.HeapPush`, in `Simulation.ServeCounters` — a different site every run, on the
  same binary and the same seed.
- `ArgumentOutOfRangeException` raised *inside* the BCL's own `ValueStringBuilder.Grow`.

Ruled out by checking rather than assuming: no `unsafe`/`stackalloc`/`fixed`/P-Invoke
anywhere in the repo; no threading in Core or in `tools/`; NUnit is single-threaded (no
`[Parallelizable]`, no `.runsettings`); reproduces identically on .NET 9 **and** .NET 10, so
not a JIT bug; a standalone integrity check verified **47.9 billion reads with zero faults**,
so it is not RAM.

**The CPU is an Intel Core i9-13900K on microcode `0x10E`, BIOS 0809 dated 2023-01-05.**
That is Raptor Lake with the documented Vmin Shift Instability defect, and Intel's
mitigations — microcode `0x125`, `0x12B`, `0x12F` — are all later than what is installed.
**Fix: update the BIOS to one carrying `0x12B` or later.** If it persists after that the part
is degraded; Intel extended the warranty on these to five years, so it is an RMA.

**In Release the suite is clean: 134 tests, 132 pass, 2 fail — the two headline 2:1 gates,
by design.** Use `dotnet test -c Release` until the microcode is updated, and do not read a
Debug crash as a defect until it reproduces on healthy hardware.

One trap worth knowing: `dotnet test | tail` reports *tail's* exit status, so a crashed run
reads as a pass. Run it bare, or capture the exit code directly.

---

## STOP HERE — 2026-07-29, overnight. Low-hanging fruit, and one thing PARKED.

**132 tests pass, 2 fail.** The two are the headline 2:1 gates. Everything below is on branch
`massing`, still NOT merged.

### Landed

| | |
|---|---|
| **`Assets/Noir/Unity` no longer switches on `PlaceKind` at all** | `IsRoofed` and `Frontage.Sign`/`BoardPaint` read the authored `roof` and `frontage` columns. Both were live bugs, not tidying — see below. |
| **Trades print names, not numbers** | The village's largest employer printed as `14  33`. That is 33 machinists. |
| **Gable ends and towers in wall stone** | A gable end is masonry carried to the ridge, not roofing. It was clay tile, then chimney brick, now the wall's own material. |
| **Spires in slate** | A spire is lead or slate, never brick. |
| **The lucam moved to the eaves** | It sat near the ridge and read as a second chimney. A lucam overhangs the wall so the hoist rope drops clear to a cart. |
| **Snapshots re-baselined** | 12 of 12 byte-identical across two Unity processes. |

### The two column reads were live bugs

`IsRoofed` listed open-ground kinds by hand with `default: return true`. It had already drifted out
of step with `VillageLayout.IsBuilding` once and left a pyramid with a chimney hanging above bare
grass. Worse: **any content-authored kind got a roof whether or not its row said `roof no`** — so
Stage 4's open-kind property was quietly broken for anything that was not a building.

`Frontage` had a live instance of the same fault. **The factory says `frontage mill` and got a
plain nameboard**, because the enum has never heard of `PlaceKind.Factory`. The column was
authored, correct, and ignored. Both now switch on the *style*, which is a closed set the file
defines — the same distinction as `RoofForm` against `PlaceKind`.

### PARKED on branch `particulars-deck`: dealing particulars

STATE.md task #36, complete and tested — worst sharer 4 → 2, at 383 draws over 913 clauses. It is
**not on `massing`**, because it costs three floors and the exception clause does not cover it:

```
                floor   with the deck
ratio.median     1.21   1.19   FAIL
ratio.p10        0.52   0.48   FAIL
texture.min         8      7   FAIL — G3 goes red again
texture.median     24     24   unchanged, so no trade is available
```

**The honest difficulty:** dealing shifts the RNG stream, so all three seeds now generate
*different villages* (1981 went 142 → 153 people). I cannot separate "the village got worse" from
"these are different villages" — per-seed it moved both ways, 1979 and 1981 up, 1980 down. And
`texture.min` at 7 is exactly the flicker the instrument's own author predicted for G3.

That is a judgement about what the ratchet is for, and it is yours. **Adopting it is one merge.**

### Deliberately not done

- **The lockstep dinner hour.** Everyone breaks within fifteen minutes. Cheap to stagger, but it
  is a taste call about whether a village should have a dinner rush.
- **Task #37, children drawing adult lives.** The content is written and staged inert, but the fix
  changes who draws what and would shift the RNG stream exactly as the deck did. It would very
  likely need parking too. Worth doing deliberately, with the floor question settled first.
- **Merging to `main`.** Not mine to do unattended.

### Still the work

Median 1.21 against a rule of 2.00. **Enact the particulars** — 900 clauses read by two inspectors
and no line of simulation code. Every instrument has said so for two days.

---


## STOP HERE — 2026-07-28. A factory, and G3 goes green for the first time.

**`dotnet test` is now 132 pass, 2 fail.** The two are the headline 2:1 gates, which fail by
design. It has been three red all day; **G3 — "distinct texture, minimum >= 8" — now PASSES on all
three seeds**, and it has failed since the instrument was built.

### What landed, in order

1. **The east quarter.** The 170x120 map was FULL — four terraces were tried inside it and the
   layout checker refused every one, because the only gaps left were the green, the playground and
   the allotments. So the map went **170 -> 210 wide** and five terraces went up east of Mill Lane.
   **112 -> 158 people.**
2. **Calloway & Sons**, a factory employing **34 on two shifts**. The estate had arrived with no
   work attached: 59% of working-age adults had nowhere to be. That is now **23.4%**.

### The floors, all five raised

```
                worst of 3 seeds
ratio.median     1.04 -> 1.21
ratio.p10        0.37 -> 0.52
texture.median     23 ->   24
texture.min         6 ->    8      <- G3 passes
sight.median    0.052 -> 0.053
```

**Why texture.min moved is the interesting part, and it was not the obvious cause.** More people
does not by itself give any individual a more various life. What did it was the **work**: a second
large employer on split shifts means a neighbour who leaves at seven and one who leaves at three,
lights on in the same terrace at different hours, somebody walking home past you when the street
used to be empty. **Employment is texture.**

### The factory cost one content row

`kind factory` in `kinds.txt` — name, hours, 34 jobs, two trades — and `PlaceKind` has never heard
of it. That is what Stage 4 was for, and this is the first time it has been tested with a kind that
*employs people*. The only C# was one massing grammar (`works`, a single-pitch shed) so it does not
read as a second watermill — and that was a choice about looks, not a requirement.

`RoofForm.LeanTo` was the one roof form nothing used. It does now.

### Two tests were pinned to the old village and are now proportional

- `HasEnoughWorkForAboutHalfThePopulation` asserted `InRange(35, 70)` jobs. It now measures jobs
  against **homes**, so it tracks the village instead of firing whenever it grows.
- `AKindTheEnumHasNeverHeardOfGetsAValueOfItsOwn` asserted a content-only kind takes exactly the
  next enum value. `factory` is now a content-only kind in the authored table, so the test's own
  `shed` takes the one after. Changed to `>=`, which is the invariant the message always stated.

### Still true, still the work

Median 1.21 against a rule of 2.00, and a tenth percentile of 0.52 against 1.00. The instrument's
verdict has not changed all day: **enact the particulars.** Nine hundred clauses read by two
inspectors and by no line of simulation code.

Also open: the **dinner hour is lockstep** — everyone breaks within fifteen minutes, so the green
floods at noon and empties by one. Left deliberately for a human to judge.

---


## STOP HERE — 2026-07-28. Buildings have shapes now. On branch `massing`, NOT merged.

Every roofed building used to get the identical `AddHipRoof`, at the identical 2.2 pitch, on
identical 3.0 walls. St Anne's, Ashcombe Mill, the school and a two-up-two-down were the same box
differing only in footprint and a sign by the door. `Frontage.cs` was doing good work and all of
it was AT THE FRONT DOOR; none of it was in the silhouette, which is the only signal that carries
to the overview camera — the default view.

**`Massing` replaces the two globals.** One grammar per kind behind `IMassingGrammar`, selected by
a new optional `massing` column in `kinds.txt` defaulting to `cottage`. Never a `switch` on
`PlaceKind` — that would have closed the open-kind property Stage 4 bought, and a new amenity
would cost C# again.

| kind | eaves | roof | extra |
|---|---|---|---|
| dwelling, farm | 3.0 | hip | — *(unchanged to the decimal, on purpose)* |
| shop, postoffice | 3.6 | hip | |
| surgery | 3.4 | hip | |
| pub | 3.4 | gable | |
| school | 4.0 | gable | bell-cote |
| villagehall | 4.2 | gable | |
| church | 5.5 | gable, 3.0 | **west tower + spire** |
| mill | 6.5 | gable, 1.6 | **lucam** |
| garage | 3.4 | **flat** | the only unpitched roof in the village |

### Verified

```
134 tests, 131 pass, 3 fail   the same three 2:1 gates, by design
smoke                          55 buildings shaped, PASSED
determinism    12 of 12 snapshots byte-identical across two Unity processes
renderers      1104, unchanged;  roofs 3,186 -> 3,294 verts
```

Determinism is the one that mattered. `BuildWalls` merged wall tiles into runs purely
geometrically and never asked which `Place` they belonged to — fine while everything was 3 m
tall. It now paints an owner per tile and **breaks ties to the lowest place id**, deliberately,
because letting iteration order decide a wall's height would not have failed as a wrong-looking
wall. It would have failed as twelve byte-identical snapshots quietly ceasing to mean anything.

### `elevations` — a new instrument

`dotnet`-free; run `Noir.Editor.Elevations.Render`. One straight-on render per building kind into
`docs/elevations/`, with **no sign, no frontage, no props and no label**. You name each one; what
you cannot name has failed to read. The signs come off precisely because `Frontage` is good at its
job — a picture with a pub sign in it tests whether you can read, not whether the pub reads as a
pub. It grades the village rather than proving the code runs, so it sits with `ratio` and `street`
and never fails a build.

### Four defects it caught that nothing else could

1. **Flat, lean-to and box faces were wound inside-out** and backface-culled. The garage had no
   roof at all — you saw its interior from above — and every tower and bell-cote was inverted.
   The same fault the ground had once. The hip roof is the reference: `cross(v1-v0, v2-v0)` out.
2. **The flat roof sat level with the wall caps**, so the depth buffer picked per pixel and the
   wall tops flickered through. Lifted 0.2, about what a parapet stands anyway.
3. **The church was a cathedral.** A tower at 0.55 of the short side is 7.7 m square on St Anne's
   carrying a 17 m spire, and a 4.5 pitch across a near-square 16 m span put a marquee on it.
   **A village tower is about four metres square whatever the church is — it is the HEIGHT that
   varies, not the footprint.** Capped at 5 m, spire 1.8x, pitch 3.0.
4. **Gable ends were tiled.** A hip end is roof; a gable end is the building's masonry carried up
   to the ridge, and roofing it puts roof tiles on a vertical wall.

Only the first was a bug. Two and three were taste being wrong, which is the argument for looking.

### Known warts, not fixed

- **The gable end is chimney brick, not wall stone.** Masonry was already in the roof material
  array so it cost no new submesh, but it is the wrong masonry — brown brick against pale walls.
  Doing it properly means giving the roof mesh the wall material.
- **The church tower reads as detached.** It is placed at the bounds edge furthest from the door
  and clamped inside, but against a 14x16 nave it stands clear of the roof and looks like a
  separate campanile rather than a west tower.
- **The tower claims no interior floor space.** Deliberate, recorded in the spec: the interior
  grammar has already laid rooms across the whole footprint.
- **`Noir.Bench` does not measure this.** It grades the simulation — build times, memory, paths.
  Massing is geometry, so its cost shows up in the smoke test's renderer and vertex counts
  instead, and those are flat.

Spec: `docs/superpowers/specs/2026-07-28-building-massing-design.md`.
Plan: `docs/superpowers/plans/2026-07-28-building-massing.md`.

---

## Earlier the same day — the dinner hour is in.

`DayPlanner` now cuts a **45-minute dinner break** out of any shift or school day that straddles
midday, and it is spent OUT — the green, the churchyard, the playground, the pub. Measured by
`street`:

```
            before   after
12:00          0.3    36.0     <-- the hole is gone
mean, day     3.19    4.56     +43%
mins/person     41    58.6
peak              35      51   at 12:14, of 112
```

**Fifty-one of a hundred and twelve people outdoors at once at quarter past twelve.** The pub's
11:00-14:30 opening was authored in `village.txt` long ago and had never had a single customer;
that window is what a dinner hour was always for.

### It moved the hole rather than removing it — 13:00 now reads 0.17

Everyone breaks within the same fifteen minutes, because the only stagger is `Punctuality`
(±15 min). So the village floods at 12:00 and is empty again by 13:00, sharper than before.

Two honest readings and **this is a judgement call, not a defect to be fixed on autopilot**:

- A dinner rush *is* what a village does. Fifty people on the green at ten past twelve and none
  at one o'clock is legible, true, and good to look at.
- Or it is mechanical, and dinner should stagger by trade — the mill, the school and the shops
  cannot all stop at the same minute.

The fix for the second is cheap and needs **no new RNG draw**: derive the offset from a stable
per-person trait so a dinner time is a *habit* rather than a dice roll, spreading the break across
12:00-13:15. Not done, deliberately — see which you prefer by looking at it.

### The 2:1 ratchet caught this and the exception clause was invoked

`Content/watched.floor` failed on `ProgressDoesNotReverse`: worst-of-three median fell 1.06 ->
1.045. That is the one trade the file permits — **legibility for texture** — and it was taken,
with the reasoning written into the file:

```
ratio.median    1.06 -> 1.04    down
texture.median    20 ->   23    up
texture.min        2 ->    6    tripled
ratio.p10       0.37 -> 0.375   up
sight.median   0.048 -> 0.052   up
```

The worst-served villager went from **two kinds of moment in a fortnight to six**. On seed 1979
alone it reads as a straight win — median up to 1.21, texture min 4 -> 9, which clears the G3
gate outright. 1980 and 1981 pay for it. The file had warned the previous author about exactly
that single-seed misreading, and it caught me with it.

**What did not improve:** the sign inversion reopened, 1.01 -> 0.94. A fuller life scores worse
again, because the dinner hour puts people outdoors ALONE and time outdoors alone still accrues
tactical facts faster than texture. Same finding as before, same fix, still untried — **enact the
particulars.**

### Tests

**131 tests, 128 pass, 3 fail — the same three 2:1 gates that fail by design.** Two new ones:
`AShiftAcrossTheDinnerHourIsBrokenByABreakOutOfTheWorkplace` and
`SchoolchildrenGetADinnerBreakOutOfTheSchoolBuilding`. Both assert on WHERE, not merely that the
block splits, because a break spent in the workplace would satisfy any weaker test and change
nothing anyone can see. The first one caught a real defect immediately: the publican's dinner
break was sending her to the pub.

**Snapshots will all have moved** — this shifts every plan and every RNG stream after it. Take a
fresh baseline before reading any snapshot diff as a rendering change.

---

## Earlier the same day — the streets are empty and now we know by how much.

**"I didn't see a whole lot of folks walking around."** That was an eyeball observation with no
instrument behind it, so there is one now: `dotnet run --project Noir.Sim -c Release -- street`.

It counts what a camera counts — bodies standing on an unroofed tile — rather than what the day
plan *says*. `density` has always answered "where is everyone recorded", which is a different
question and a much kinder one.

```
mean visible, whole day       3.19 of 112  (2.8%)
peak visible at once            35   at 08:30
ever seen outdoors             112 of 112
minutes outdoors per person per day  41.0
```

Everybody does go out. **The village is not still — it is EMPTY AT THE HOURS YOU LOOK.**

```
07:00   8.4      16:00   7.1
08:00  13.6      17:00   6.7
09:00  10.4      18:00   1.9
10:00   5.7      19:00   5.7
11:00   2.7      20:00   5.5
12:00   0.3  <-- 21:00   2.9
13:00   0.2  <-- 22:00   1.4
14:00   1.1      23:00   0.1
```

**Midday is a hole: 0.2 of 112 people visible on a 170x120 map.** The default clock is noon and
`noon-overview` and `street-noon` are two of the twelve snapshots, so noon is very likely exactly
when it got looked at. Four causes, in order of size:

1. **Nobody breaks for lunch.** A shift is one unbroken block, 09:00 to 17:00 — `DayPlan` has no
   midday leg at all. 37 people are sealed in buildings for eight hours and 20 children in the
   school for six. That is half the village removed from the street by a missing feature rather
   than by a choice.
2. **Every destination is indoors.** The errand list is shop, post office, pub, village hall,
   church, a neighbour's front room. An errand *takes somebody off the street* for 25-90 minutes.
   The only unroofed destinations are green, playground, allotments and churchyard, and their
   weights are 20/30/22/20 against the shop's 45.
3. **The walks are short, and that was deliberate.** `AssignJobs` got its distance term and
   `Locality.ChooseNearby` shortlists the six nearest, which fixed a real defect — 57% of traffic
   was cross-map by construction. It also cut the only thing that put a body in front of you.
   Nothing was measuring that side of the trade, which is precisely why `street` now exists.
4. **112 people on a map drawn for 600.** At the same 2.8% a 600-person town shows ~17 at once
   instead of 3.2. This one needs no new design — it is Stage 5, already the plan.

Cause 1 is the cheap one and it is worth doing before anything else on this list: a midday break
moves ~57 people through the street twice a day and fills the emptiest hours on the chart. Causes
2 and 3 are the same argument the 2:1 gate is already making — **more kinds of moment** — and
`street` now grades whether an answer to it is visible or merely authored.

### `ratio` has moved since the section below was written

`ChooseErrand` **now has dwellings in it** — the "nobody calls on anybody" item below is done, and
the code carries the reasoning. Measured effect, honestly small:

```
                    was     now    needs
median ratio       1.10    1.20     2.00   FAIL
tenth percentile   0.37    0.44     1.00   FAIL
distinct texture      3       4        8   FAIL
```

**Only 9 of 112 called on anybody in a fortnight**, and `FellInWithSomebody` is 8. The hole is
plugged; it is not yet a social life. The sign is still inverted (busiest 1.01, quietest 1.50).

### Verified, was blocking

`house` on all three: **St Anne's has a 126 m2 nave**, The Wheatsheaf has a 60 m2 bar with
lavatories and a kitchen off it, Ashcombe Mill has a 154 m2 workshop, office and store. The
grammars are doing what Stage 4 claimed. That clears the "NOT YET VERIFIED, do this first" below.

Note: `house <name>` matches on substring, so `house Church` returns *1 Church Row* and `house
Mill` returns *1 Mill View*. Use `St Anne`, `Wheatsheaf`, `Ashcombe Mill`.

### One wart in the new instrument, already fixed, worth knowing

The first version of `street` read 3.9 minutes outdoors per person per day — 17x too low — because
it treated every `Place` as a building. The green and the churchyard are Places with bounds and
`roof no`. It disagreed with `ratio`'s "in sight" figure and that disagreement is what caught it.
`street` now asks `PlaceKindTable`, which is a use for the `roof` column Core previously never
read.

---

## Stages 1-4 are done. Read this next.

**The village now measures itself, and it fails its own gate.**

```
dotnet run --project Noir.Sim -c Release -- ratio

median ratio            1.10   needs 2.00   FAIL
tenth percentile        0.37   needs 1.00   FAIL
distinct texture, min      3   needs    8   FAIL
```

The 2:1 rule — *watching someone must yield more useless information than useful, or they are a
lock rather than a person* — was the one quantitative gate this project defined for itself and
never built. It is built now, and the diagnosis is precise:

1. **The 913 particulars are never performed.** `Citizen.Particulars` is read by two inspectors
   and by NO line of simulation code. 611 clauses were written today and not one is enacted.
   This is plan item #10 — "beats and particulars" — where the particulars landed and the beats
   never did.
2. **Nobody calls on anybody.** Zero visits in 1,568 household-days. `DayPlanner.ChooseErrand`'s
   weighting list contains no dwelling. Same root cause as "the barber is staffed, open, and
   nobody ever goes for a haircut".
3. **The observable vocabulary is nine verbs.** Came out, went in, walked past, stood about,
   stopped to speak, fell in with somebody, stood in a line, a light on, a light off.
4. **The sign is inverted** — busiest quarter 0.86, quietest 1.40. A fuller life currently scores
   WORSE, because going out generates tactical facts and there is nothing else on offer. Printed,
   deliberately not yet a gate.

### THE NEXT PIECE OF WORK, and it is now measurable

**Enact the particulars as beats, and put dwellings in `ChooseErrand`.** The fix is MORE KINDS OF
MOMENT, never fewer tactical propositions — do not try to pass this gate by suppressing the
denominator. `ratio` will grade it directly.

`ChooseErrand` is the blocker for both, and the kind-table work established it cannot be moved
into content: it is an ordered weighting list with bespoke logic per entry, read in different
orders by children and adults, drawing one `rng.NextFloat()` per entry considered. Tabling it
would put RNG draw order under content control. **It wants its own design.**

### What the other three instruments say

- `encounters` — the graph is REAL. One island, nobody isolated, 30% acquaintance overlap,
  mixing ratio **0.99** (encounters independent of where people live). But thin: median meets 24
  of 111 in a week, and 20 of 112 never had a conversation at all.
- `economy` — closes on all eight checks, but **Ashcombe Mill is 22 of 48 jobs, 46% of all
  employment in one building**, and `encounters` says the 0.99 mixing ratio is being PAID FOR by
  that single mill. Two instruments pointing at the same decision from opposite ends: author the
  town with Ashcombe's ratios and it gets one employer for half its adults, which is exactly the
  condition that fragments the acquaintance graph by street.
- `economy` also — the Post Office is open 23 h/week and **16 of 48 employed adults have no free
  minute while it is open**. A property of the schedules, not the population, so it survives
  scaling unchanged.
- `vocab` — at 600, particulars need **+2,284** to hold a bar of 4, and the whole cost is the
  tail (the average is already inside the bar). The ladder is the argument to have first: k=6
  needs nothing, k=3 needs +6,485. And the number no table can absorb: **266 more distinct
  building names and 75 more `human` lines** — `WorldBuilder` refuses duplicate place names, so
  that is 266 separate acts of authorship.

### Costs and warts, honestly

- **`dotnet test` is now 5 minutes**, up from ~30 s. The 2:1 gates run 72 M ticks in Debug.
- G3 (`distinct texture, minimum >= 8`) is a minimum over a stochastic population and will be the
  first gate to flicker as the village improves. Implemented as specified, disagreement recorded.
- `ratio` exits 0 even when red, deliberately — a permanently non-zero exit gets wrapped in
  `|| true` within a week. `TwoToOneTests` is the gate that says no.
- **126 tests, 3 failing** — and the 3 are the 2:1 gates, failing by design.

### Verify everything

```
cd C:\SerialKillerGame\tools
dotnet build Noir.sln
dotnet test Noir.sln                                  # 126, 3 red BY DESIGN
dotnet run --project Noir.Sim -- check
dotnet run --project Noir.Sim -c Release -- ratio     # 16 s
dotnet run --project Noir.Sim -- encounters           # 61 s, --days 1 for a seventh
dotnet run --project Noir.Sim -- vocab --pop 600      # exits 1: it is a gate and 600 fails
dotnet run --project Noir.Sim -- economy
dotnet run --project Noir.Bench -c Release            # the performance scoreboard, ~4 min
```

**NOT YET VERIFIED, do this first:** `Noir.Sim` could not be run when the grammars were wired,
so nobody has confirmed a church actually has a nave. Run `house` on the church, the pub and the
mill and look at the plans.

---

# Earlier — 2026-07-27

## The decision that shapes everything after this

**Build a 600-person market town, hand-authored, then build the game layer — not the city.**

Reached by 36 agents across two workflows plus two standalone studies, all measuring rather
than reasoning. Three independent streams converged on it. The argument that settled it:

> The investigation layer is the thing that decides what the world model must record, and it
> does not exist. Every measurement was taken on a simulation with no observation layer in it.
> Scaling that to twenty thousand people is optimising a system whose requirements are not yet
> known.

Supporting numbers, all measured today:

- The simulation's wall is **2,022 citizens** (bracketed by benchmark runs, not extrapolated).
  A town of 600 has 4.9x headroom.
- Every ceiling found was the same failure wearing different costumes: **the world was treated
  as one flat global thing** — people walked across it at random, the renderer submitted all of
  it every frame, the whole town re-planned on one tick in 1200.
- The generator is **not a one-way door**. `VillageLayout` is already a plain public mutable
  class and `WorldBuilder.Build(VillageLayout, ulong)` is already public, so a generator is a new
  producer feeding an existing seam. It can be deferred at zero architectural cost.
- Honest cost to a finished, watchable 600-person town: **26-36 days**.

## Landed today

| | |
|---|---|
| **Truth** | Pathfinder failure used to set `At` to the destination without moving anybody. Measured 23.2% of a doubled village and 29.0% of a tripled one recorded somewhere they weren't. Now 0. |
| **Locality** | `AssignJobs` had zero distance term (~57% of traffic cross-map by construction). Home-to-work mean 165.9 -> 80.4 tiles on a doubled map. Deliberate 20% long-commute minority kept, so the town still has traffic. |
| **Investigation firewall** | `Noir.Core.Observation` — fifth asmdef, references Contracts and nothing else. One `Sighting` type whose descriptor cannot name anyone. Two reflection tests. Makes "the investigation cannot read ground truth" a compiler error. |
| **Light pool** | 32 real lights total, population-independent. URP silently drops past 256; we had 84 at 109 people and would have crossed at ~330. |
| **Benchmark harness** | `tools/Noir.Bench`, in the solution. Repeatable, diffable, with a machine anchor so runs on different days can be compared honestly. |
| **Chunked rendering** | 5,487 renderers -> 1,835. Ground/walls/roofs/props/countryside split on a 64 m grid (128 m outside the map). A single map-spanning mesh cannot be frustum-culled. |
| **Small defects** | Click-to-select now asks the simulation, not the renderer. Stride amplitude no longer depends on frame rate. Hanging signs meet their brackets. Dead `CutawayDistance` removed. |
| **Snapshot determinism** | **12/12 PNGs byte-identical across two separate Unity processes.** The snapshot set is now a regression check, not just pictures. |

Earlier in the session: the ground was found to have never rendered (every quad wound
face-down — correct normals, correct bounds, backface-culled from every camera above ground);
chimneys wound inside-out; roofs smooth-shaded so the ridge vanished; the sun set at 18:00 while
the light curve still called for sun at 20:00; URP post-processing skipped entirely because
`postProcessData` was null; colour grading in LDR so every lit window clipped to flat white.

## Stage 4 landed — kinds are content now — 2026-07-28

**`Content/kinds.txt`**, 17 rows, 16 columns, each derived from a switch it replaces. Plus
`PlaceKindTable.cs` with asserts that refuse an incomplete row at load. **`PlaceKind` is now
open**: a row the enum has never heard of gets the next value along, so a new amenity costs one
content row and no C# at all. Demonstrated with a barber — one row, four lines of village.txt,
zero code — staffed by Nancy Grimshaw, 43, open 09:00-17:30, shut Mondays, none of which was
written in village.txt.

**Four interior grammars** behind `IInteriorGrammar`, with the distinction that made it work:

> Grammar is how the building is arranged. Programme is what its rooms are for. A hospital and a
> school are the same corridor with different things off it.

`bsp` (houses, moved verbatim, byte-identical), `spine` (hospital/school/surgery/offices),
`open` (shop/pub/cinema/barber/garage/mill), `hall` (church/village hall). Thirteen programmes.
Verified by a stress harness of **27,104 interiors** — every grammar x programme x footprints
3x3 to 24x24 x all four door walls: no room escapes its building, none overlap, every room
reachable from the front door. Zero problems.

### The integration I did afterwards

- **Fixed six wrong grammar columns.** The table shipped saying `domestic` for the pub, shop,
  post office, school, surgery and village hall. Now `open`, `open`, `open`, `spine`, `spine`,
  `hall`. The farm stays `domestic`, because a farmhouse genuinely is a house.
- **Gave three buildings interiors at all.** Church, garage and mill were `rooms none` — hollow
  boxes, the exact thing this stage exists to fix. Now `auto` with `hall`, `open`, `open`.
- **Passed the programme.** `WorldBuilder.StampInterior` was calling the grammar with a null
  programme, so everything got its grammar's default. Now passes the kind's name.
- **Installed the table in Unity** — one line in `VillageHost.Awake`, which was the whole
  precondition for retiring the bootstrap.
- **Deleted the bootstrap.** Core carried a compiled-in copy of the table because it cannot read
  files and Unity did not yet hand it one. `PlaceKindTable.Current` now throws with an
  explanation instead of falling back. Failing loudly beats a second source of truth that
  silently disagrees with `Content/kinds.txt` the moment anybody edits it. The drift test that
  compared the two went with it.

**115 tests green.** `Noir.Sim` could not be run to check a church actually has a nave — the
ratio agent is mid-write on `Ratio.cs`. **Do that first: `dotnet run --project Noir.Sim -- house`
on the church, the pub and the mill.**

### Known gaps, deliberate

- **`DayPlanner.ChooseErrand` cannot be tabled** and this is the real finding of the stage. It
  looks like a switch but is an *ordered weighting list* with bespoke logic per entry, read in
  different orders by children and adults, drawing one `rng.NextFloat()` per entry considered.
  Tabling it would put RNG draw order under content control, so a content edit would reshape
  every villager's day. **That is why the barber is staffed and open and nobody walks to him for
  a haircut.** It wants its own design, not a column.
- Unity still switches on `PlaceKind` in `RoofBuilder.IsRoofed`, `Frontage.Sign`,
  `Frontage.DoorPaint`, `Frontage.Boarded`, `VillageUI.Pretty`, `Snapshot.cs:323`. Columns for
  the first two already exist and are authored; the rest have no column yet.
- Occupations print as numbers (`13`) rather than names — `Occupations.NameOf` exists, the
  display sites still `ToString()` the enum.

## Rendering: chunked and LOD'd — 2026-07-28

- **Chunking verified clean.** 5,487 renderers -> 1,835. Deterministic run to run, and I looked
  at `morning-long` for seams at chunk boundaries: none. Ground unbroken, raking shadows
  continuous, roofs shading correctly.
- **Canopy LOD.** Geodesic spheres sized so the worst outline error stays inside half a pixel at
  the closest distance a camera can legally reach. Countryside mesh **17.0 -> 8.7 MB**, vertices
  440k -> 223k. Verified by eye on `first-light`: no polygonal silhouettes, treeline still
  recedes into fog.
- The agent predicted **which 7 of 12 snapshots would change**. All 7 correct. Of the 5 it said
  must not change, `noon-overview` and `close-terrace` held. The three misses — `school-run`,
  `mill-gate`, `the-crowd` — are exactly the three people-shots, and the population changed
  109 -> 112 underneath when the surname pool grew. Not a bad prediction; a confounded test.

**Baseline for the next diff is `scratchpad/chunk-1/` — superseded. Take a fresh one.**

## Content expanded for 600 — 2026-07-28

**This heading has misled once already, so: this made the POOLS big enough for 600 people. It did
not create 600 people.** The village is still 112. Population is not a setting anywhere — it is
derived entirely from `Content/village.txt`: one household per dwelling *unit*, 44 dwellings ->
50 units -> 112 people, at ~2.24 people per unit. The only way the number goes up is authoring
more housing, which is Stage 5 and has not been started. `vocab --pop 600` grades these pools
against that future town; it is a forecast, not a description.

302 -> 913 particulars, surnames 74 -> 401, forenames 124 -> 348. Zero exact duplicates, zero
repeated 4-grams, zero pronouns. Seed 1979 now yields **112 people, not 109** (a bigger surname
pool changes how many attempts `PickSurname`'s prefer-unused loop consumes).

Two live bugs found and NOT yet fixed — both need `PopulationGenerator.cs`, which was owned by
another agent at the time. Tasks #36 and #37:

1. **Particulars are sampled, not dealt.** `rng.NextInt(particulars.Count)` per person means the
   worst-case repeat barely improves however much is authored — from ~7 holders to ~5 between
   913 clauses and 1,655. Dealing from a shuffled deck puts every clause on exactly 1-2 people.
   ~10 lines, worth more than doubling the content again.
2. **Children draw an adult's life.** A nine-year-old who watched the Coronation and buys one
   chop at a time, four days a week. ~48 broken renders at 109 people, ~264 at 600. Forenames
   too — a seven-year-old named Mavis. 55 child clauses and 51 cohort forenames are already
   written and staged INERT in the content files, waiting on the parser change.

## Stage 2 finished and verified — 2026-07-28

The two failing tests were the dead agent's own half-built fixture, not its real work.

- `Farsyde` — a three-kilometre village meant to force a mid-walk strand. Its shop was
  hardcoded at x=3500 on a 1900-wide map, so it never parsed; fixed, and it still produced no
  strand. **Removed, with the reason recorded in `PeopleTests.cs`:** the node budget gates when
  a search may START, not how deep it may go, so a long walk strands nobody. The general
  invariant it was backing up — `AnybodyWhoDidNotMoveIsNotPointingAnywhere` — already passes on
  the real village and covers every stop path.
- `ScratchDiag.cs` — leftover diagnostic, deleted.

**104 tests pass, 0 fail. `check` clean.**

The `Heading` fix landed better than asked: rather than one line in `Strand`, every stop routes
through `StandStill`, because `Heading` was doing double duty as a direction and an is-walking
flag and the two disagreed in exactly one place.

### Measured result — the journey jitter worked

Worst tick of the day, before -> after:

| people | before | after | |
|---|---|---|---|
| 109 | 1,396 us (890x median) | **163 us (151x)** | 8.6x better, and now AT budget rather than 8.4x over |
| 611 | 22,026 us | **5,919 us** | 3.7x |
| 2,511 | 27,556 us | **7,858 us** | 3.5x |

Measured ceiling moved **2,022 -> 2,525 citizens**.

Stranding on a tripled Ashcombe: **misplaced 0 of 1,011 (was 29.0%)**. Stranded 2 of 1,011,
none standing in a wall. One oddity left unchased: Victor Fenwick stranded 1,410 minutes trying
to reach the house he already lives in — 0.2%, on a synthetic stress map, probably an artefact
of how `BigVillage` tiles duplicate place names across seams.

## Superseded — kept for the record

**Stage 2 is PARTIALLY APPLIED and its agent died** (session token limit, not a code fault).
It got a long way in: **13 Core files changed**, tests grew 87 -> 106.

State of the tree as left:

```
dotnet test    104 passed, 2 FAILED, 106 total
check          OK - no problems found, 1 region, 100% connected
build          clean
```

Files it touched: `Ids.cs`, `Rng.cs`, `Citizen.cs`, `Household.cs`, `Population.cs`,
`PopulationGenerator.cs`, `Simulation.cs`, `Place.cs`, `Prop.cs`, `TileGrid.cs`,
`VillageLayout.cs`, `VillageParser.cs`, `WorldBuilder.cs`.

Its last words, which are the resume point:

> The forced-stranding approach was wrong (the node budget gates search *starts*, not search
> depth). Let me build a fixture that actually produces a mid-walk strand.

**First job on resume:** identify the 2 failing tests, finish or revert Stage 2 as a whole. A
half-applied seed-breaking change set is the worst state to leave this in — the seed is meant to
break exactly once and then freeze.

## Chunking: verified deterministic, NOT yet verified clean

```
run 1 vs run 2       12 identical, 0 differing     <- chunking IS deterministic
vs pre-chunk baseline 0 unchanged, 12 CHANGED      <- everything moved
```

The first line is the result that matters: chunking produces the same picture every run, so the
regression check still works.

The second line is **unattributed and must not be read as a pass or a fail.** Stage 2's Core
changes landed in between, and they move where every person stands, so all twelve shots would
change for that reason alone. Whether chunking *also* introduced a seam is unknown.

To settle it: finish Stage 2, re-render, and compare against `scratchpad/chunk-1/` — then any
remaining difference is Stage 2's alone. Or eyeball `morning-long` and `first-light` for
chunk-boundary seams, which is where the chunking agent said to look first (long raking shadows
are the case that would expose a cascade or normals problem).

Baselines kept: `scratchpad/det-a/` (pre-chunking), `scratchpad/chunk-1/` (post-chunking).

## Next, in order

1. **Verify chunking** — the background render above. Two runs must match each other. Diffs
   against `det-a` need attributing: geometry change = a chunking seam (bad); people in
   different places = Stage 2's seed break (expected).
2. **Stage 3 — the instruments.** The 2:1 aliveness test the plan claims to have **does not
   exist**; `Assets/Noir/Tests/EditMode` is empty. Also `encounters` (with repeat-rate and
   distinct-people-met), `vocab` (the content-ceiling gate — 302 particulars is 4.8 sharers per
   clause at 600 people), and `economy`.
3. **Stage 4 — the kind table.** `PlaceKind` is switched on in 13 files, every switch with a
   silent `default:`. A hospital currently generates a 330 m2 kitchen and two bedrooms, and is
   staffed by nobody. Replace with `Content/kinds.txt` plus four interior grammars.
4. **Stage 5 — author the town.** ~670 lines, Ashcombe pinned as the old parish at its heart.
5. **Then the game layer.** Not the city.

## Open decisions — yours

- **The town's name.** Agents independently invented *Marlbury* and *Marchford*. Both placeholders.
- **The game's name.** Still banked: PATTERN OF LIFE · SIGNATURE · CREATURE OF HABIT ·
  HE KEPT TO HIMSELF · SMALL HOURS. Working title NIGHT WORK.

## The thing only you can do

The milestone has one checkpoint that is not a number and has **never been run**:

> **Does it feel alive** — you, following one villager through a whole day.

Click someone, press **F**, drop to **1/4x**, watch for ten minutes. If that is interesting,
the foundation works and the town is worth authoring. If it isn't, better to know before
600 people get written.

Note: 1/4x was broken until today — the walking test read distance per *rendered frame*, and at
1/4x only one frame in twelve advances a tick, so eleven in twelve reported the whole village as
standing still and their legs settled straight while they walked.

## Known and unfixed

- `PlaceKind` enum with 13 silent-default switches (Stage 4 fixes it).
- Content is not additive: adding one building reshuffles other buildings' interiors
  (Stage 2 fixes it — verify with the add-a-building test).
- No save/replay system exists. Nothing in Core or tools serialises anything.
- Pathfinder scratch is 21 bytes per tile — 3.5x the world it searches — allocated in the
  constructor whether or not anyone asks for a path.
- The spinney is one 392k-vertex chunk; every other chunk is under 46k. Deliberate, commented,
  wants a profiler.
- 28 of 4,628 renderers unexplained by the chunking agent's reconciliation (0.6%).

## How to check anything

```
cd C:\SerialKillerGame\tools
dotnet test Noir.sln                              # 87 tests at session end
dotnet run --project Noir.Sim -- check            # layout: overlaps, connectivity
dotnet run --project Noir.Sim -- strand --days 3  # nobody recorded where they aren't
dotnet run --project Noir.Sim -- strand --days 3 --tile 3   # same, on a 3x map
dotnet run --project Noir.Bench -c Release        # the scoreboard, ~4 min
```

Unity, headless — **close the editor first**, it takes an exclusive lock:

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SmokeTest.Run -logFile <log> -quit
Unity.exe -batchmode -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.Snapshot.Render -logFile <log> -quit
```

Snapshots land in `docs/snapshots/` — twelve views, byte-identical run to run. Diff them.
