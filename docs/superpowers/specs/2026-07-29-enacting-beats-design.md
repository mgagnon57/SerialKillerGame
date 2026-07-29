# Enacting beats — making the authored particulars reach a watcher

**Date:** 2026-07-29
**Status:** approved, not yet implemented

## The problem, measured

`Beat` exists. It is derived at generation from the clauses a person drew, folded onto
`Citizen.Beats`, and it is the bridge from authored texture to something a watcher across the
road could see. The bridge is built. Almost nothing crosses it.

```
Content/particulars.txt          1,076 clauses authored
Beat                             3 values — Carries, Lingers, RoundAbout
clauses tagged `carries`         5
clauses tagged `lingers`         0
clauses tagged `roundabout`      0
reads of Citizen.Beats           1 — Simulation.cs:664, testing one flag
```

The five tagged clauses all begin with the literal word "carries", so the tagging was a keyword
match rather than an editorial pass. At 2.4 clauses drawn per citizen over 158 people, five
clauses reach `158 x 2.4 x 5 / 1076` — about **1.8 people**. The instrument agrees: in the
act-by-manner table, "came out" with `carry` reads **2**.

**Two of 158 villagers is the entire extent to which the particulars are enacted.**

## What the act-by-manner table decides

Texture is a set, not a count — `Salience.Weigh` accumulates distinct `(act, manner)` keys in a
`HashSet`, and the report says so in its own footnote: *"a village cannot raise either of them by
doing more of the same."* That single fact settles which beats are worth building.

```
                     people    carry    linger      new    alone
came out                158        2         6      158      158
went in                 155       75        16      131       67
walked past             158      134                158      158
```

- **`SomewhereNew` is saturated** — 158/158 on both "came out" and "walked past". `RoundAbout`
  would produce nothing but `SomewhereNew`, so it cannot yield a distinct key for anybody. It is
  also the only beat that would need a routing change, touching `Pathfinder` and the determinism
  guarantee. **The most expensive beat is the worthless one.**
- **`Lingering` is scarce** — 6 of 158 on "came out", 16 on "went in". There is room here.
- **`Carrying` at "came out" is scarce for a different reason.** `Carrying` is otherwise set from
  the previous activity being Shopping or OnTheAllotment, which cannot be true on the way *out* of
  your own front door. So "came out carrying" is reachable only through `Beat.Carries`.

## Why lingering is rare, and what it costs to fix

`BeginDoorPause` grants everyone `6 + Rolls.Int(..., 0, 6)` ticks — 6 to 11, which at 20 Hz is
0.3 to 0.55 seconds. `Eyewitness` samples once per simulated minute (`sim.Tick(TicksPerMinute)`),
so the chance of catching any given pause is roughly **0.5–0.9% per door transit**. The six people
who registered as lingering were lucky, not habitual.

To be a habit rather than a coincidence, the pause has to be a real fraction of the sampling
interval. `LingerBase = 400`, `LingerSpread = 400` gives 20–40 seconds of game time and a
**33–67%** catch rate per transit. That is also what the enum already promises: *"takes longer over
the same journey than anybody else does."*

## The design

### 1. Delete `Beat.RoundAbout`

Remove the value and its parse arm in `ParticularsTable.BeatIn`, recording the reason at the enum:
a saturated manner cannot become a distinct proposition. Nothing else in the codebase references
it. Leaving a flag that content can tag and no code can honour is the same fault as the authored
`frontage mill` that `Frontage` ignored.

### 2. Wire `Beat.Lingers` into `BeginDoorPause`

Alongside `DoorPurpose` (`Simulation.cs:409`), add:

```csharp
private static readonly ulong LingerPurpose = Rolls.Purpose("lingering");
private const int LingerBase = 400;      // 20s at 20 Hz
private const int LingerSpread = 400;    // ...to 40s
```

and in `BeginDoorPause`, keeping the existing `who != null` guard the surrounding code already
relies on for `who.Key`:

```csharp
int ticks = 6 + Rolls.Int(Seed, DoorPurpose, key, _clock.Tick, 0, 6);
if (who != null && (who.Beats & Beat.Lingers) != 0)
    ticks += LingerBase + Rolls.Int(Seed, LingerPurpose, key, _clock.Tick, 0, LingerSpread);
_agents[index].DoorPauseTicks = ticks;
```

**The extra draw takes its own purpose.** Reusing `DoorPurpose` with a wider range would change
the base value for everybody; a separate purpose leaves every non-lingerer's pause byte-identical
to today. `Rolls` is stateless by construction, so no stream advances, no citizen is regenerated,
and no day plan changes.

A paused agent does not block anyone — `Simulation.cs:372` skips their advance and nothing treats
the tile as occupied — so there is no doorway deadlock to design around.

### 3. The editorial pass

Hand-tag clauses that honestly imply the habit. **Not** by keyword: line 524 is
`stops anybody opening an umbrella indoors, and means it`, which a `stops` match would tag as a
lingerer. A clause whose sentence and whose behaviour disagree is the exact failure this system
exists to prevent — the point of deriving beats from particulars is that the two *can never*
disagree.

Expect roughly 60–80 `carries` and 15–30 `lingers`, reaching perhaps 25–35 of 158 villagers. The
file's own rule is the test: *observable, or nearly so*.

## What this does not do

**It will not move `texture.median` or `texture.min`, and it is not meant to.** At 0.35 holders
per tagged clause, ~70 clauses reaches ~25 people; the median villager still holds no tagged
clause, and the minimum villager — which is what G3 grades — almost certainly does not either.
Ninety-percent coverage by chance would need ~667 of 1,076 clauses tagged, which would mean
tagging clauses that do not honestly imply the habit.

**It will not fix the inverted sign, and may widen it slightly.** Beats are distributed
independently of how busy someone is, so tagging lifts every quarter by about the same `k` keys —
and the same `+k` over the busy quarter's larger useful denominator is a smaller proportional gain
than over the quiet quarter's smaller one. The sign wants *company*, which is a separate design:
`fell in with somebody` is 15 of 158 and `called on somebody` is 8, while `alone` is 158/158.
Company is the one texture a solitary villager cannot earn, and cutting solitude cuts the useful
column that busy people saturate. **That work re-baselines the village and should be batched with
guaranteed beat coverage, which re-baselines it too.**

If the ratio numbers move more than a point or two, that is a reason for suspicion, not
satisfaction.

## Tests

The load-bearing test is end to end. Asserting `Beats & Lingers != 0` proves only that a field was
set; it would pass while the feature reached no watcher at all.

1. **A lingerer is seen lingering.** Drive `Eyewitness` over a tagged citizen and assert
   `ObservedManner.Lingering` appears in the log, with an untagged control where it stays rare.
2. **Non-lingerers are untouched.** Door pauses for citizens without the beat remain in 6–11.
   This pins the separate-purpose claim, which is the entire basis for "the same village".
3. **A tagged clause and its holder agree.** A citizen who draws a `carries` clause has
   `Beat.Carries` and is observed `Carrying`.
4. **A lingerer still gets everywhere.** They reach every planned place and are not stranded.
   Budget: 6–14 door transits a day at 20–40s is 2–9 minutes of drift, inside the existing ±15 min
   `Punctuality` — but asserted, not assumed.

**The two existing door assertions stay exactly as they are.** `QueueAndDoorTests.cs:368`
(`InRange(6, 11)`) and `:448` (`<= Longest`) run against Queueham, whose citizens are built with
the 14-argument `Citizen` constructor and so take `Beat beats = Beat.None` by default. No Queueham
citizen can hold a beat, so neither assertion changes — and together they already *are* test 2
above, pinning the untouched-pause invariant for free. Test 2 therefore adds a lingerer
deliberately rather than modifying these.

Regression sweep: `dotnet run --project Noir.Sim -- strand --days 3`.

## Verification, and a hardware caveat

This machine's i9-13900K is on microcode `0x10E` with a BIOS from January 2023 and produces
impossible faults under sustained compute — see the head of `docs/STATE.md`. Until the BIOS is
updated:

- run the suite with `dotnet test -c Release`; Debug aborts the host
- treat no `ratio` figure from this machine as evidence of anything
- re-render snapshots only afterwards. Positions will move for lingerers and whoever is near
  them; the population is unchanged, so any *other* difference is a real defect.
