# Simulation fixes — the work list

**This is a work file. Delete it when the last item lands.** A read-only audit on 2026-08-09 found
176 verified faults in the simulation — the first audit to ask whether the town *behaves* rather
than how it looks. A planning pass turned them into 128 items across nine waves, with the owner's
ruled build order as the spine. The facts live in `CLAUDE.md`; this is a queue.

**Sibling plans:** `docs/ANIMATION-FIXES.md`, `docs/ROAD-FIXES.md`, `docs/TEST-FIXES.md`. They
move the same two test counts. See [Cross-plan](#cross-plan).

`docs/TEXTURE-FIXES.md` **is finished and deleted** (2026-08-09). What it left behind is one row
in Cross-plan below, and the standing facts it measured are in `CLAUDE.md`.

**Item IDs:** `HAB` habits · `WIT` the witness layer · `S3` the 2:1 rule · `NIGHT` `TRAVEL` `PRECINCT`
`DINNER` `YEAR` `CHURCH` the day's bugs · `WHO` the population · `MOVE` pathfinding · `TEST` the gates.

---

## ⚠ Read this before you touch the seed

**The literal reading of ruling one is a trap, and it was measured.** The ruling is "take `day` out
of the errand seed mix." A planner did exactly that in a replica and re-planned 400 real citizens:

> **Tuesday and Wednesday come out bit-identical for 400 of 400 people.**
> (Today: 10 of 400, and those are people with empty days.)

One stream serves the dinner roll, the church draw, the errand count and every errand draw, so
removing `day` freezes all four together. You get exactly **three distinct days per person for the
rest of time** — weekday, Saturday, Sunday — each replayed to the minute. That is a metronome, not a
habit, and **no existing test catches it**: nothing asserts that Tuesday differs from Wednesday, so
the Core suite stays green and the town ships as clockwork.

**Do not do it.** The ruling's *intent* — habitual errands stick to a person — is delivered by
`HAB-1` and `HAB-2`: a second, day-free source that overrides *part* of a day. `Mix` is left alone.
Record the 400/400 measurement in the commit message so nobody tries the obvious thing twice.

---

## The ruled build order

| | Step | Waves |
|---|---|---|
| **1** | The day repeats — habits stick to a person | W4 |
| **2** | The town watches itself — a Citizen gets a body; Eyewitness runs over citizens; descriptions from the real subject | W5, W6 |
| **3** | Re-point the 2:1 rule at cross-day repetition, re-baseline on Rossville | W7 |
| **4** | The crime — **ruled not yet**, and not in this plan | — |

The ordinary bugs are woven into that order rather than bolted on after: the town's *shape* (ages,
families, shifts, the seven schools) has to be honest before a habit is a property of it.

## Exactly two reshuffles, and they are W3 and W4

Every change to a seed mix, a draw order or a draw count **reshuffles the entire town** and voids
every committed baseline. The spine is built so that happens **twice**, deliberately, in named waves.
Everything in W0, W2, W5, W6 and W7 must move **no hash** — and `WHO-B6`'s two population invariant
hashes are the acceptance criterion that proves it.

---

## The waves

### W0 — Measure the tree · half a day · one editor-closed window

`MOVE-0` `TEST-00` `TEST-01` `S3-0` `WHO-B6` `TEST-26` `HAB-0` `SCOPE-1`

**Not one of the six clusters was planned against a measured tree**, and three of them state a Core
baseline of 428 that four plans are simultaneously moving. Worse:

- **`play3.xml` is already RED** — `TrafficPlayTests.NoCarWaitsForeverAtTheHeadOfAClearQueue` at
  15.3 s — and the run took **719 s** against `CLAUDE.md`'s "13 of 13 PASS, 6m 03s". Two committed
  PlayMode tests have never appeared in any results XML.
- The fixture ratio measurement costs **82.6 s**, not the 45 s `TwoToOneTests.cs:34-40` claims.

Take every baseline **before** anything moves, or step three inherits a confound for the second time.
**First: commit or stash the dirty tree** — `TownGeometryPlayTests.cs` and `CityDriveways.cs` are
modified and uncommitted, and they are `ROAD-FIXES`' files.

### W1 — The seven schools · one day · no Unity · **the first commit**

`WHO-A1` `WHO-A2` `WHO-A4` `WHO-A3` `WHO-B1` `GUARD-1`

**The highest visible value per line in any of the four plans, and it is a seven-token content diff.**
`city.txt` lines 1524/1526/1528/1574/1596/1600/1602 are `place school … 13x7` house boxes standing
between identical `place house … 13x7` neighbours. They are generator-default filler that a commit
re-*kinded* instead of taking down.

> **75.2% of dwellings currently catchment to "103 E Benton Ave", a 91 m² house.** And because
> `SeatOnSurvey` pairs biggest-first, two of them seat onto **251 m² and 43 m² outbuildings** — the
> built town contains a **43 m² school with four teacher posts**.

It lands first because it reshuffles Rossville and **moves no committed floor** — the only large,
visible change in the spine that costs no re-baseline. `WHO-A1` and `WHO-A2` must be **one commit**:
28 job slots vanish between them and `AwayWorkTests` goes red in the gap.

### W2 — The free gates and every zero-divergence fix · 3–4 days · almost no Unity

37 items. Everything here provably moves no draw, and it must land **before** the reshuffle so that
when the number finally moves there is exactly one commit to point at. Two go first:

- **`watched.floor` fails open.** With the file absent the ratio returns all-zero thresholds, a
  tab-separated line is dropped silently, and five literals switch with no default. **Three of the
  four moving ratchets become tautologies** and one falls to a 4.9× looser bar. *Recording a new
  floor into a file that fails open is recording nothing.*
- **`Eyewitness.cs:100` tests the enum member, not the table.** `kinds.txt` declares
  `kind apartment / home yes`, and apartment is not in `PlaceKind` — so city.txt's 7 apartment places
  (~33 people) would land at the very bottom of the first Rossville measurement the project ever
  records, as a pure instrument artefact. Fix in its own commit, prove byte-identical on the fixture.

> **Gate.** Delete `watched.floor` locally: the Core suite must go **red**. Restore it: green.
> Nothing asserts that today.

### W3 — **The one reshuffle: the town's shape** · 5–7 days · determinism change 1 of 2

`DAY-24` `DET-2` `WHO-B2..B8` `TRAVEL-2` `PRECINCT-1,2` `DINNER-2` `YEAR-1,2` `CHURCH-1` `NIGHT-2,3` `MOVE-7` `DET-1`

Every remaining bug that changes a draw is here and happens **once**: the age pyramid and its sixteen
empty years, children named from the adult pool while 98 cohort-correct child names sit parsed and
unused, impossible families, the 14-minute school run that takes 22, the twelve precinct officers,
the farm working 05:30–19:00 on the shortest day of the year, the 09:00 Sunday service standing
empty, and `DayPlan`'s structural inability to represent a shift crossing midnight.

**These belong before habits, not after** — a habit is a property of a day, and this is what makes
the day honest. It also resolves the sharpest file contest: `DAYBUG` owns `DayPlan.cs` through W3 and
hands it to `HABIT` in W4, instead of two clusters racing.

⚠ **`WHO-B4` is a blocker, not a follow-up.** Add ages 0–4 while every rule keys on `IsChildIn` and a
two-year-old is sent to school, given a solo errand and put to bed at 20:30 — one such person takes
`texture.min` below its floor, and that floor's own rule forbids lowering it.

### W4 — **Step 1: the day repeats** · 4–6 days · determinism change 2 of 2

`HAB-1` `HAB-5` `HAB-3` `HAB-2` `HAB-4` `HAB-7` `DAY-08a/b` `DAY-11` `HAB-6` `TEST-15` `GAME-10` `DET-1`

A habit is **derived, never stored** — a pure function of (seed, world, citizen, year) through
`Rolls`, costing **zero new draws**, following `WorksAwayIn`'s existing precedent. Storing it on
`Citizen` would add draws to `PopulationGenerator` and reshuffle names, ages, jobs, particulars and
Beats — the most expensive determinism change available in this codebase.

Order inside the wave is fixed and is **not** the item order. Corrections the skeptic forced:

- `HAB-1`'s signature must take the seed. The specified shape has **no seed reachable from it** and
  every draw it names is unreachable.
- `HAB-2` must fire an appointment only when it falls due **inside the slot being drawn**, or a
  retired woman waking at 06:30 with a 15:15 habit loses her entire morning to one block — measured:
  early errands −25.9%, mean first departure 09:28 → 10:14, and **total out-of-house minutes is blind
  to it**.
- `HAB-5`'s hand-written hour table is a second copy of `kinds.txt` that already disagrees with it.
  Derive the window from the place's own opening hours instead.

> **He watches it:** follow one pensioner two mornings running and see her leave for the same place at
> the same hour; follow her on a Wednesday and see a different day.

### W5 — Step 2a: the body · 4–5 days · no reshuffle

**`WIT-BODY-3` is a two-line change and the highest value per line in the entire plan**, so it lands
*second*, not third: today every witness describes the same 35-year-old, 178 cm man because
`Recollection.cs:109-112` passes constants.

The body already exists in the wrong layer keyed on the wrong thing — `AgentFigure`'s `AgentLook`
hashes height off `who.Id.Value`, the array slot, which `Citizen.cs`'s own comment says must never be
what a roll keys on. Moving the model into Core and keying it on `Key` makes **the described man and
the drawn man the same man**.

⚠ Two skeptic corrections: `WIT-BODY-5`'s specified guard ("no durable bucket under 5") **passes in
0 of 200 Monte-Carlo towns** — assert the person-weighted median bucket instead. And `Degradation`
keys its band shuffle on (witness, minute) with **no subject term**, so one witness asked about four
people at the same minute drops the same band for all four.

### W6 — Step 2b: the town watches itself · 2–3 weeks · no reshuffle

**The architectural finding that makes this affordable:** the town does not need to watch itself
every tick. A plan is a pure function of (seed, citizen, day), so citizen A observing citizen B is a
**replay of two pure functions costing nothing until somebody asks** — 33,888 interval tests for a
whole-town canvass against 11.8 million for the cheapest storing design. **1/347th.** And
`WitnessFirewallTests` ruled on this in writing before anybody asked: a per-tick observer needs
`AgentState` and therefore `Noir.Core.Sim`, which the firewall refuses by name. The replay needs no
new reference.

⚠ `WIT-SEE-2`'s exposure rule as specified **manufactures the fault it exists to prevent**: "the
first and last two minutes of every block are in the open" puts the entire town in its own doorway
from 00:00–00:02 and 23:58–24:00 — the exact failure the 2026-08-05 sleep gate killed, arriving from
the subject side, **at the one hour the game is about**.

### W7 — Step 3: re-point the rule and re-baseline · 1 week · **no Unity at all**

Instrument-only and **cannot change the game**: outside its own file `Salience` is referenced only by
`tools/`, and Unity compiles nothing there. Its entire blast radius is the five numbers in
`watched.floor` — which is exactly why it comes last, after the town has stopped moving.

Both real fixes **can only raise the ratio** (a union of two bin sets is never larger than their sum;
two distinct days is strictly stricter than a second entry). **So the headline will very likely read
above 1.00 for the first time ever, with zero change to Rossville** — and the entry must say so in its
first sentence or the next reader will believe the town got better.

⚠ **The 2.00 and 1.00 targets do not move.** They are the design document's sentence. Lowering a
standard because the instrument got honest is the cheat one level up.

✅ **DONE 2026-08-09, ahead of the rest of this wave.** `CLAUDE.md`'s "the number moves when the town
gains more KINDS of observable moment, and by nothing else" is deleted and replaced. Two entries in
`watched.floor` falsify it independently: 2026-07-28 records the ratio moving 1.04 → 1.21 on a
population change with no new verb, and 2026-08-01 records it moving the *other* way — texture 24 →
29 kinds, ratio 1.21 → 0.89 — under a heading stating the instrument did not move. The replacement
keeps the anti-gaming clause, which was never in doubt, and takes the floor's own conclusion for the
causal one: what moves the number is a kind of moment that is not simply more time in the open.

⚠ **The rest of W7 is still to do.** This was the one piece that could land without touching
`Salience`, and it was worth taking early because it is a rule in the authority file that a session
will act on. Re-pointing the rule and re-baselining still comes last.

### W8 — The router and the remainder · open-ended · the one optional reshuffle

**`MOVE-9` reverses the audit's implied remedy, and the measurement is decisive.** An *admissible*
heuristic on the shipped cost ladder gets road share 31% → 62% but costs a **median 100,576 nodes
against a ceiling of 100,000** — half the town's journeys come back `GaveUp`, which becomes `Strand`,
and a stranded person is reported as being wherever they were filed at noon. **Lowering the weight
does not put people on the street; it stops them leaving the house.**

The road-restricted two-tier route gets **95.1% road share for 4,504 nodes median**, comfortably
inside the existing budget. Land the seam early (W2, provably a no-op); flip the default here, after
the re-baseline, because today's instrument runs on a 148-person fixture and **cannot see a
road-following habit at all**.

---

## Cross-plan

| File | Owner |
|---|---|
| `DayPlan.cs` | `DAYBUG` through W3, then `HABIT` from W4 |
| `DayPlan.cs:21` — **handed over from `TEXTURE-FIXES`, 2026-08-09, that plan is closed** | **`Activity.OnTheAllotment` is the last English word in the witness vocabulary.** An allotment is a rented council plot; the Illinois word is a garden plot, and `Content/particulars.txt` was moved to it in the same pass that removed 193 other English clauses. This one was left because renaming it touches Core, Unity **and** Editor plus a keyed row in `Content/animations.txt:146` (`ontheallotment`), and CLAUDE.md's rule is that when those two drift the table "does not throw, it falls through to a default" — so it wants its own commit with the animation-table gates watched, not a rushed rename at the end of a long session. |
| `CityStreets.cs` | `ROAD-FIXES` W8 |
| `AgentBody.cs` | `ANIMATION-FIXES` |
| `TownGeometryPlayTests.cs`, `LayerProof.cs` | three-way — add at the end, one commit, rebase |
| The Core count and the PlayMode count | **four plans.** Measure from a run, never predict, write once |

`MOVE-3`'s region assertion rides `ROAD-FIXES`' W2 commit, not this plan's.

---

## Owner decisions

### Answered 2026-08-09 — encode these, do not re-ask

**THE NIGHT.** About **20 people awake at 02:00** on an ordinary weeknight (1.4%), **~30** on Friday
and Saturday, **~28** during harvest. The mix, as ruled:

| | weeknight | notes |
|---|---|---|
| police on duty | 1–2 | from the precinct's real three-shift rota |
| hospital and care staff | 3–4 | |
| new parents | 4–6 | only possible because under-fives now exist |
| night owls / insomnia | 10–14 | weighted to elders, drawn trait |
| teenagers | 0 | **8–12 on Friday and Saturday only** |
| grain elevator + farmers | 0 | **+6–10 during harvest (Sep–Oct) only** |
| walking home from the tavern | — | until about 00:30 |

Every one of those is seasonal or day-of-week aware, **so the night itself has a pattern** — which
is the point: a night that is uniformly empty and a night that is uniformly busy are equally
unreadable.

**THE AGE PYRAMID — babies *and* teenagers together**, not the school-preserving variant:
~97 under five, ~90 aged 16–20, school age ~182. This is the **biggest single gain in legibility in
the plan** (prams, school-leavers, first cars, teenagers out at night) and the **biggest reshuffle**.
It is also what makes two of the night's six categories possible at all.

**MEDIAN AGE 35**, deliberately. Today's 42 is an accident of three flat bands. The research
documents that appear to support 42 are quoting **2020 census figures** — Rossville aged thirty years
after the period being modelled.

**SCHOOL STAFFING ~40, split 22 Grade School / 18 Rossville-Alvin.** ⚠ **See the interaction below
before encoding this.**

> ### The staffing collision, resolved 2026-08-09 — **40 are TEACHERS, and the roll is wrong**
> Put back to him with the arithmetic, he ruled that **40 teaching posts stand and the school roll is
> the thing that is too small**: Rossville's schools drew from outside the village.
>
> **He is right, and the map already says so.** The high school is *Rossville-Alvin* — a
> **consolidated district**, named for the consolidation. Its roll was never the village's children
> alone. 40 teachers against a proper consolidated roll is ordinary; against 182 village children it
> only looked absurd because the pupils from outside are missing from the simulation.
>
> **This is now a modelling item, not a number to tune.** Three consequences the plan must carry:
> 1. The high school needs pupils the town does not contain. Either non-citizen pupils who exist only
>    as a roll, or the Alvin expansion (already designed and parked, per the project's own notes).
> 2. **School buses.** A consolidated rural district runs them, and a bus arriving at 07:30 and
>    leaving at 15:30 is one of the most legible daily rhythms a farm town has — and one of the few
>    that a whole neighbourhood sees at once. This is habit material, not just plumbing.
> 3. `WHO-A3`'s size guard must compare a school against its **roll**, not against the village's
>    child count, or it will fire on a correctly-staffed consolidated school.

### Also answered 2026-08-09

**THE CHURCH ON MAPLE — take the county's building.** Replace the 91 m² house box with the county's
9,608 sq ft footprint and address, and name it from `names.txt`, which was already retuned against
the county's own church records. **The size guard in W1 therefore covers churches as well as
schools**, rather than being scoped to schools only.

**CHILDREN GET FULL HABITS, same rule as adults** — against the recommendation, for the simpler code.
⚠ The named risk must be handled, not assumed away: a child's weekday habit can collide with the
school bell. The habit resolver must treat the school block as immovable and place a colliding
weekday habit **after 15:30 or not at all**, and a test must prove no child is ever drawn out of
school by a habit.

**THE PLAYER IS BOTH — killer by night, detective by day.**

> ### ⚠ This changes the shape of W6, and it must be known before it starts
> The plan's step two was scoped as *citizens observe each other*, with the player's own track left
> as the separate thing it is today. **"Both" makes that the wrong shape.** It needs **one symmetric
> system — everyone a subject and everyone a witness, the player included** — because a player who
> acts at night and investigates by day is on both sides of the same question, sometimes about the
> same night.
>
> Building one direction and turning it round later means building it twice. **W6's design must be
> symmetric from the first commit**, even though the first thing it delivers is still citizens seeing
> citizens. Concretely: `Eyewitness` takes a subject and a witness that are the same type, and the
> player is a subject with a track rather than a special case.
>
> It also settles a question the audit could not: the observation firewall's job is to keep the layer
> **clean**, not to keep it one-directional — so W6's replay design (33,888 interval tests, no new
> assembly reference) still holds, and the player simply becomes another subject inside it.

### Also answered 2026-08-09, second round

**THE PRECINCT — four officers, TWO watches, and one on call from home overnight.** Not three
watches. Nobody is *at* the precinct at 3 a.m.; somebody can be **woken**.

> ⚠ **This revises the night mix recorded above.** Police no longer contribute 1–2 awake bodies
> overnight — they contribute **zero at the precinct and one asleep-but-wakeable officer at home**.
> Weeknight awake at 02:00 drops from ~20 to **~18**.
>
> **And it is better than what it replaces.** An on-call officer is not a witness *until something
> wakes him* — so a light going on in a policeman's house at 3 a.m. is itself an event with a cause,
> and it is the first thing in this simulation that reacts to another thing happening. Build the
> wakeable state now even though nothing calls him yet; step four will.

**COMMUTERS' HABITS — accept the tavern.** No shop opening hours are invented and no weekend
special-case is written. A man who works away and stops at the same bar on the way home two nights a
week, same night, same hour, is a real and highly legible pattern — arguably the most legible one a
commuter has. **Zero code, zero content, and it was already true.**

**BUILD FAULTS — gate the smoke test at zero door errors.** `TownPipeline` keeps returning a town (a
pipeline that throws makes the editor unusable), but `SmokeTest` **fails while a named error stands**.
A gate that passes with nine named errors on screen is how six broken doors survived to be found by
an audit.

**THE STANDING GATE'S RED — a dated exception, added in W0 before the first red arrives**, naming
exactly which tests go red and in which wave, and **removed by W4's re-record commit**. So a session
never meets an unexplained red suite, and a third, real red stays visible throughout.

### Still open — four, none blocking, all with a standing answer

**Every blocking decision in this plan has been answered.** These four remain, and unless he says
otherwise a session should **proceed on the recommendation** rather than stopping to ask:

| | Question | Take this |
|---|---|---|
| **1** | Where does the Rossville 2:1 measurement live? | Keep the **gate** on the fixture — a fixture is the right tool for a *rule* — and carry the Rossville number in `CLAUDE.md` as a **dated recorded figure** with its instrument, which is the right tool for a *standard*. Also correct `TwoToOneTests`' claim of 45 s; measured, the three-seed fixture watch is **82.6 s** |
| **2** | When does the road router's default flip, putting people on the street instead of on lawns? | **After step three (W8).** Land the seam in W2 (provably a no-op) and flip last — today's instrument runs on a 148-person fixture and **cannot see a road-following habit at all**, so flipping early spends the risk before any of the payoff can be measured |
| **3** | Port the five survey passes into Core, so Core tests can see the town the game builds? | **Defer to W8**, and only if `ROAD-FIXES` W4's `SurveyedTown` helper proves insufficient. Until then every Core test that says "Rossville" must say **in its printed header** that it measured the pre-survey town and is a lower bound |
| **4** | Bundle the seed subject change (`Citizen` Id → Key) into the one reshuffle, or pay a second re-baseline later? | **Bundle into W3.** The spine reshuffles the town exactly twice; a third reshuffle for one line is pure waste, and `Citizen.cs`'s own doc comment already says the array slot must never be what a roll keys on |

**Answered elsewhere and recorded here so nobody re-opens them:** the standing gate's red (dated
exception in W0, removed by W4), the church on Maple (take the county's building), and whether the
player is the killer or the detective (**both** — see the W6 note above, which is the consequence
that actually changes code).

---

## Do not do

- **Do not take `day` out of the seed mix** (see the box at the top). Thirty entries in the full
  do-not-do list; these are the ones that would cost most:
- **Do not lower the heuristic weight to put people on the street.** Measured: it strands half the
  town instead.
- **Do not store habits on `Citizen`.** It reshuffles names, ages, jobs, particulars and Beats.
- **Do not move the 2.00 and 1.00 targets** when the instrument gets honest.
- **Do not key the tools guard on `WorldBuilder.Build(`** — it has 43 call sites in 23 files. Key it
  on the literal `city.txt` with a short allow-list.
- **Do not adopt the presence rule in `Eyewitness` before W7** — it would move the ratio in a wave
  that is supposed to move nothing.
- **Do not let `WIT-SEE-6` build anything.** Five lines of comment naming the intended
  generalisation and why 1,390 tracks and a Sim reference are both wrong.
- Ten planner proposals came back **FLAWED** and three **OVERBUILT** under attack; each is corrected
  in place above rather than deleted, so nobody re-proposes them.

---

## Done means

- The Core suite and the PlayMode gate are green at **numbers a run printed**, each written in
  `CLAUDE.md` exactly once, with the already-red traffic test either fixed or knowingly recorded.
- Deleting `Content/watched.floor` turns the Core suite **red**.
- `WHO-B6`'s two population hashes moved **exactly twice** in the whole spine, in W3 and W4, each in
  one commit with the delta explained.
- Rossville has **two schools**, not nine, and no institution the whole town has a catchment for is
  the size of a house.
- **A named neighbour, asked about a named person on a named day, gives a description — and the
  person is not the player.**
- **At 02:00 there is a lit window and somebody behind it who can see something.**
- **He follows one pensioner two mornings running and she leaves for the same place at the same
  hour** — then follows her on a Wednesday and sees a different day.
- The 2:1 entry that records the new floors says in its first sentence that the number rose because
  the instrument got honest, not because the town got better.
