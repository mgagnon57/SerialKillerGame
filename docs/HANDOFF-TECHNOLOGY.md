# Handoff — the year-gated technology layer

Written 2026-08-03 by the research session, for the session doing the coding.

**Split:** research and planning were done here; **the code is yours.** Step 1 of the plan is
already finished and committed. Steps 2–6 are what you are picking up.

---

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
So `Era.cs` goes in **Contracts** and `TechnologyTable.cs` in **World**.

**3. Copy the per-key idiom from `AgentAnimation.ClipFor`** — a hash of the citizen key giving a
stable per-person pick. It is the same shape as the adoption percentile and the project already has
it. Do not invent a second one.

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

1. `Core/Contracts/Era.cs` — the notation and the percentile. Test it alone first; it is pure
   arithmetic and hashing and every later step depends on it being right.
2. `Content/technology.txt` — the table, with the header in the style of `animations.txt`:
   *adding a technology is a line here and nothing else.*
3. `Core/World/TechnologyTable.cs` — `Parse` / `Install` / `Has` / `Adopted`.
4. The tests. All nine are listed in the plan.
5. The two consumers — the LLM dialogue prompt first, it is the highest-value one.
6. `Noir/Check The Technology`, in the style of `Editor/AnimationCheck.cs`. Needs the editor, so it
   can come last and wait for a free moment.

---

## Things that will bite you

- **Monotonicity is the point.** A household must not flicker in and out of owning something as the
  years advance. That is why the percentile is fixed per key and only the curve moves. If you find
  yourself storing state per household per year, the design has gone wrong.
- **A falling curve is not a bug.** `payphone` goes 100 → 60 → 10 because payphones *left*. The
  households that keep it longest are the lowest percentiles, which is the right shape.
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
