# Handoff — the year-gated technology layer

Written 2026-08-03 by the research session, for the session doing the coding.

**Split:** research and planning were done here; **the code is yours.** Step 1 of the plan is
already finished and committed. Steps 2–6 are what you are picking up.

---

## DONE 2026-08-03 — reply from the coding session

**Steps 2, 3, 4 and 6 are built and committed** (`6e28f40`). Suite went 300→316 passing of 318;
the two failures are still `TwoToOneTests`, by design. Unity compiles clean.

- `Core/World/Era.cs` — `Era.Crossing` is `Fields.DayWhen` lifted out, and `Fields` calls it. Its
  fourteen tests stayed green, which was the named regression check. The two roundings differ on
  purpose and both files say why.
- `Content/technology.txt` — every row from `TECHNOLOGY.md` with its measured/inferred marking
  carried across, plus the correction rule.
- `Core/World/TechnologyTable.cs` — `Parse`/`Install`/`Has`/`AdoptsIn`/`LosesIn`/`Adopted`. Inert
  when absent, loud when malformed.
- `tools/Noir.Core.Tests/TechnologyDiagnostic.cs` — `[Explicit]`, as revised.
- Plus the one Unity line in `VillageHost`, caught rather than fatal.

**Three departures, all argued rather than assumed:**

1. **`needs` was added to the row syntax, and it was not in the plan.** The diagnostic printed four
   households by name and one of them had dial-up from 1999 and no computer ever. Independent
   curves put about a sixth of the town online with nothing to read it on — and every test passed,
   because each curve was individually perfect. `dialup … needs computer` ranks the child off the
   parent's queue, so containment is exact rather than probable, and the load refuses a child curve
   that rises above its parent. **This is the single best argument for Step 6 existing.**
2. **The town-scope purity check was written and then removed.** I first refused fractional `town`
   rows on the grounds that a town fact is true for everybody or nobody — which rejected the plan's
   own `payphone 1970:100 1998:60 2005:10`. The plan is right and I was wrong: a fractional town
   curve means a share of the town's *stock*, and past-half reads as "there is still one to use".
   Documented on the enum rather than enforced.
3. **`e911address` is absent, per instruction** — with a test pinning the absence, and a note in the
   content file that absence is *itself* the claim that rural addressing never arrived.

**Step 5 was not done, because neither consumer exists.**

- **There is no LLM dialogue prompt anywhere in the repo.** No file under `Assets/Noir` names an
  LLM, a prompt, Claude or LLMUnity. That is a decision that has been made, not code that has been
  written, so there is nothing to add a technology line to.
- **`DayPlan` has no telephone or reachability concept.** Its only "call" is *calling at* a house
  in person (`DayPlan.cs:602`). Reachability is not a thing being modelled that needed gating; it
  would have to be invented first.

Building either would have meant inventing the consumer to justify the layer — which is the same
mistake as building props before facts, one level up. The layer is queryable, loaded, and tested;
wiring it belongs with whoever builds the dialogue port. **Flagging rather than quietly doing it.**

One thing that bit, worth carrying: `Noir.Core.Contracts.Light` collided with `UnityEngine.Light`
and stopped the editor entering play mode while the whole Core suite stayed green — the netstandard
gate has no UnityEngine, so there was no second `Light` to collide with. Renamed to `LightLevel`
(`9722173`). **A green `dotnet test` is not evidence that Unity compiles.**

---

## NEXT 2026-08-03 — reply from the research session

**The `needs` addition was right, and it is the best argument for Step 6 that anyone could have
made.** Two curves each individually perfect, every test green, and a sixth of the town online with
nothing to read it on — that is precisely the fault a diagnostic catches and a test cannot. It is
the same lesson `CountrysideDiagnostic` records about `CommercialRow`'s infill, found twice
independently now.

Refusing Step 5 was also right. Inventing a consumer to justify the layer is the same mistake as
building props before facts, one level up.

### 1. First, a verification pass — on your own `Light` lesson

*"A green `dotnet test` is not evidence that Unity compiles"* deserves acting on, not just
recording. And **`ShowPeople` is `true` now**, so PlayMode is heavier than any baseline in this
file — 763 rigged prefabs per city build roughly doubled those runs last time they were measured.

Run all four and post the numbers:

```
1.  dotnet test -c Release tools/Noir.Core.Tests          expect 316 / 318
2.  headless Unity compile, then grep the log for "error CS"
3.  -runTests -testPlatform PlayMode -assemblyNames Noir.PlayTests
        -assemblyNames is NOT optional. Do NOT pass -nographics.
4.  press Play and look at it
```

Core tests are blind to an entire class of failure — the `Light` collision proved it. If all four
are clean, say so and move on.

### 2. Then: one year, not two

You have built a layer whose whole premise is that **the year matters**, and there are now two
"current year" concepts sitting beside each other:

| | |
|---|---|
| `GameClock.Year` | real, advances, and `TechnologyTable` reads it |
| `Households.Year = 1991` | a `const`, frozen, and ages are computed against it |

`Citizen.Age` is a fixed `readonly int` set at generation. **Over a fifteen-year game a seven-year-old
stays seven**, a schoolchild never leaves school, and `names.txt`'s carefully-built 1991 cohorts
slowly become wrong for everybody.

**Fix direction:** store a **birth year** on the citizen and derive `Age` from `GameClock.Year`.
`Households.Year` should **stop existing** rather than be updated — one source of truth. Core-only,
so no editor contention.

**Watch for:** anything assuming `Age` is immutable, and the `AgeBand` / `IsChild` logic. A child
ageing out of school mid-story is correct behaviour and may surprise the day planner.

### 3. Then, if you want the technology layer to stop being inert

`TechnologyTable` is currently consumed by nothing but its own loader line. **`DayPlan` has no
reachability concept** — which you were right not to invent on the spot, but it is worth inventing
now. *"Can this person be telephoned away from home"* is the exact mechanic
`WHO-SEES-WHOM.md` §5 says the game's whole information arc turns on, and `mobilephone`,
`answermachine` and `cordless` are already in the table waiting for it.

### Queued behind those, not now

- **`particulars.txt` is still English, 1979** — 914 clauses, and the biggest content problem in the
  project. Content work rather than code.
- **`ShowBuildings` is still `false`** because there is no Illinois house. See **`docs/ASSET-GAPS.md`**
  — that one needs a purchase decision, not code, and the re-audit there found 400–500 usable
  prefabs already owned that nobody knew about.

---

> **Re-evaluated 2026-08-03, after `Fields`, `Railroad` and `Daylight` landed.** Five things
> changed and two of them matter: **`Fields` already invented this adoption model** (a fixed rank
> in [0,1), curve inverted per entity) so Step 2 generalises `Fields.DayWhen` rather than writing a
> second inverter; and **Step 6 is now an `[Explicit]` Core diagnostic instead of a Unity editor
> command**, which was quietly breaking this pass's own Unity-free promise. `Era` also moves from
> `Contracts` to `World`. Details in the plan.

## The one rule, and this time it is a gift

**This pass is Core-only and Unity-free.** No prefabs, no art, no scene work, no editor. Everything
lands in `Assets/Noir/Core/**`, `Content/`, and `tools/Noir.Core.Tests`, and it all verifies with:

```
dotnet test -c Release tools/Noir.Core.Tests
```

So unlike the people/animation handoff, **there is no Unity exclusivity problem here.** You can work
on this while anything else has the editor. The only Unity file you will touch is the one line that
reads the content file, and even that can wait.

**Establish your own test baseline before you start.** It was 227 pass / 2 fail when this research
session last measured it, and the two failures are `TwoToOneTests`, which fail by design — but code
has landed since and that number has probably moved. **Run it first and write down what you get.**
Trusting a stale baseline cost this project real time earlier today.

**Git discipline, project convention and load-bearing:** never `git add -A` or `git add .`. Stage
only what you edited. The tree carries unrelated dirty files, including `docs/snapshots/**` which is
rewritten by every render run.

---

## Read these two, in this order

1. **`docs/plans/technology-layer.md`** — the approved plan, committed here so it survives a fresh
   session. Exact API signatures, a semantics table covering every edge case, worked table rows,
   and nine named tests. It is written to be executed without asking anybody questions.
2. **`docs/research/TECHNOLOGY.md`** — committed `5db18c8`. The dates, in the table's own
   `year:percent` notation, ready to paste across.

---

## What this is for

The game opens in **1991** and runs to about **2006**, and `GameClock` now knows it — you built the
calendar. **Almost nothing consumes it.** A citizen in 2005 currently lives in exactly the 1991
world: same objects, same reachability, same everything.

The sharpest instance is already written down in `docs/research/WHO-SEES-WHOM.md` §5:

> In 1991 you cannot reach a person who is not at home, and an unanswered phone means nothing. By
> 2006 a person is reachable, and there is a log. **The same disappearance is a different event at
> the two ends of this game.**

This layer answers *"does this household have a mobile phone in 1999?"* so the day plan, the
dialogue prompt and the investigation can consult it. **Facts, not props.** Art reads from this
layer later; building props first would mean inventing this layer anyway.

---

## Four things that will cost you an hour each if you find them yourself

**1. `ContentLoader` is Unity-side.** `Assets/Noir/Unity/ContentLoader.cs`, and it uses
`Application.dataPath`. **Core cannot read files.** Follow `PlaceKindTable`: `Parse(string)` +
`Install(...)`, with the Unity caller doing the read —
`TechnologyTable.Install(TechnologyTable.Parse(ContentLoader.Read("technology.txt")))`.

**2. Assembly direction, checked.** `Noir.Core.Contracts` has **zero** references and
`noEngineReferences: true`. `Noir.Core.World` → Contracts. `Noir.Core.People` → Contracts + World.
**Both `Era.cs` and `TechnologyTable.cs` go in `World`** — parsing needs `ContentText`, which is
`internal` to `Noir.Core.World`, and the curve inversion being generalised lives in `Fields`, also
World. Contracts is for zero-dependency primitives.

**3. `Fields` already built this mechanism — generalise it, do not write a second one.**
`Fields.RankOf(key)` gives a fixed rank in [0,1) (note the `>> 11`: low bits are avoided on purpose
to stop striping), and the private `Fields.DayWhen(days, percent, rank)` inverts a
*percent-reached-by-date* curve to give one entity its own date. That is exactly this design, one
domain over. Lift `DayWhen` into `Era`, express it in years, and have `Fields` call the general one.
**`Fields`' existing tests staying green is the regression check for that step.**

**3b. Core bans transcendentals for replay determinism.** `Daylight` embeds a 365-entry table rather
than call six of them. Linear interpolation and integer hashing only — no `Math.Pow` to shape a curve.

**4. DO NOT TOUCH THE WITNESS LAYER.** `PersonDescription.CarriedThing` is deliberately vague —
*Bag, Case, Bundle, LongObject* — because a witness says "something in his hand", not "a Nokia".
That vagueness is the type's entire design. Technology must not leak identifiable objects into
observation. **This is the one thing in the plan I would not bend on.**

---

## About the curves — carry the confidence markings across

`TECHNOLOGY.md` marks every curve **measured** or **inferred**, and the markings are not decoration.
Put them in the table's comments; do not flatten them into bare numbers.

The good ones are rural and era-exact, from NTIA's *Falling Through the Net* — the US government
surveying household technology by rural/urban off Census supplements, in exactly this window.

**Three rows are deliberately not final:**

| row | why |
|---|---|
| `mobilephone` | **the most consequential guess in the file.** Coverage-limited, national data only, and `WHO-SEES-WHOM.md` builds the game's information arc on it |
| `answermachine` / `cordless` / `callerid` | weakest curves in the file — no rural series found for any of them |
| `e911address` | mechanism certain, **Vermilion County date unknown**. Do not ship the placeholder year |

**The rule for filling gaps** is stated at the top of `TECHNOLOGY.md` and is worth internalising:

> Affordability-limited technologies show a **small** rural lag. Coverage-limited technologies show
> a **large** one.

Rural households were two points behind the nation on computers in 1998 and converged by 2000 — if
you could afford one you bought one. But a mobile phone in 1996 is a *tower* question, and cable
never reached a village of 1,200 at all.

---

## Suggested order

1. `Core/World/Era.cs` — generalise `Fields.DayWhen` into a shared curve inverter, plus `RankOf`.
   Do this first and get `Fields` onto it while its tests are there to prove you did not break it.
2. `Content/technology.txt` — the table, with the header in the style of `animations.txt`:
   *adding a technology is a line here and nothing else.* Parse with `ContentText.Tokenise`.
3. `Core/World/TechnologyTable.cs` — `Parse` / `Install` / `Has` / `AdoptsIn` / `Adopted`.
4. The tests. Ten are listed in the plan, including the `Fields`-stays-green one.
5. The two consumers — the LLM dialogue prompt first, it is the highest-value one.
6. `tools/Noir.Core.Tests/TechnologyDiagnostic.cs` — an `[Explicit]` printing test in the style of
   `CountrysideDiagnostic.cs`, showing the town's adoption year by year. **Not** a Unity editor
   command; that was in the first draft and it broke this pass's own Unity-free promise.

---

## Things that will bite you

- **Monotonicity should be structural, not argued.** Invert the curve — *this household adopts in
  1999* — rather than comparing a percentile against each year in turn. One crossing year, computed
  once, and nothing can flicker. This is what `Fields.PlantedOn` does. If you find yourself storing
  state per household per year, the design has gone wrong.
- **A falling curve is not a bug.** `payphone` goes 100 → 60 → 10 because payphones *left*. The
  households that keep it longest are the lowest ranks, which is the right shape — and it means a
  falling curve needs both ends inverted, adoption and loss.
- **`town` scope ignores the key entirely.** CODIS going national in 1998 is true for everybody or
  nobody; there is no adoption curve on it.
- **Inert when absent.** An unknown technology name returns `false` and does not throw — same
  principle as `AgentAnimation`. A missing content file means every query is false, which is exactly
  the 1991 world and therefore a safe failure.

---

## Out of scope, and flagged rather than hidden

**1. Nobody ages.** `Citizen.Age` is a fixed `readonly int` set at generation, and
`Households.Year = 1991` is a second hardcoded year whose comment says ages are worked against it
*"not against whatever year the machine thinks it is"* — which was true before the clock had a
calendar. **Over a fifteen-year game a seven-year-old stays seven.** Fix direction: store a birth
year, derive age from `GameClock.Year`. Era-adjacent, not technology, and its own pass.

**2. `particulars.txt` is still English, 1979.** 914 clauses, drawn 2.4 per citizen, so *every
person in Rossville* is described with the shipping forecast, the pools, **Button B in the phone
box**, the immersion, the *Radio Times*, the **Home Service**, the mobile shop, cricket on the
radio, and **Marlbury** — Ashcombe's neighbouring town. The file's own header still gives its rule
as *"1979, rural England."* `names.txt` was retuned twice and says outright the Ashcombe pool was
wrong on both counts; particulars never got that pass.

About nineteen of those clauses reference technology directly, so it is era-coupled as well as
country-wrong — **which is exactly why `Era` is being built general rather than technology-specific.**
When particulars are rewritten for Illinois, the clauses can carry era ranges with no second
mechanism. **The rewrite is large and should be scoped on its own.**

---

## If you disagree

Push back. The plan was written from a read of the code, not from running it, and you have the
better view of anything that has landed since. The only item I would defend hard is the witness
layer — everything else is negotiable.
