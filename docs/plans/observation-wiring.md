# Wiring the observation system

Written 2026-08-03 by the research session.

## Context

The game's central question is *who saw whom*. The machinery for it is **built, tested, and
firewalled** — and it has never run, because **nothing records where the player was.**

`Recollection.WhatTheySaw(world, population, who, day, track, seed)` answers *"everything one
villager could tell you about the player, for one day."* It is complete. It is called by nothing,
because its `PlayerTrack` argument is never populated: `Player.cs` moves a character around Rossville
and writes down nothing at all.

**That is the whole gap.** One missing link at the head of the chain, not a missing system.

---

## First, the thing not to break

**There is a firewall, it is deliberate, and it is enforced three ways.** Read
`tools/Noir.Core.Tests/ObservationFirewallTests.cs` before touching any asmdef.

| | references | meaning |
|---|---|---|
| `Noir.Core.Observation` | **Contracts only** | *cannot name* an `AgentState`, `Citizen` or `WorldModel`. The investigation layer is **provably unable to read ground truth** |
| `Noir.Core.Witness` | Contracts, World, People, Observation | **the producer side.** Allowed to see both, and turn truth into testimony |
| `Noir.Core.Sim` | Contracts, World, People | does not see either, and does not need to |

Enforcement, because an asmdef alone is not enough:

1. The Unity asmdef makes a violation a **build failure**.
2. The parallel netstandard build compiles all of Core into **one** assembly, where asmdefs mean
   nothing — so a **transitive reflection walk** treats the namespace as the boundary.
3. A third test **pins the reference list**, so widening it is *"a deliberate act with a red test
   attached, not a quiet convenience."*

Its own stated reasoning is worth carrying: *"The realistic failure is not `Sighting.Culprit` — it is
a convenience field three types deep, added years from now by someone who never read any of this."*

> **This plan requires no asmdef change whatsoever.** `Witness` already references everything it
> needs. If a step seems to need a new reference, the step is wrong.

---

## What is already built and good

- **`Recollection.WhatTheySaw`** — replays the citizen's day from `DayPlanner`, a pure function of
  (seed, citizen, day). **Nothing is stored and nothing is stepped**, so asking about a day a
  fortnight ago costs the same as asking about yesterday. No log has to accumulate.
- **`Sightlines.HowGoodALook`** — distance and light into a `SightingClarity`.
- **`PlayerTrack`** — `Step(Tile where, Visibly looked)` per minute, stamped on the same clock as
  `Sighting.Minute`.
- **`Degradation` / `Salience`** — what fades and what sticks.
- **`PersonDescription`** — deliberately cannot say who it was. *That is the entire game.*

**A documented limit, deliberately taken:** `Recollection` is **stationary witnesses only**. A
citizen's position is the door of the place their plan has them at; while `TravellingTo` nobody knows
where they are, and interpolating between two doors *"would invent a path and then treat it as
evidence."* Walking witnesses need routing and are later work. **Do not quietly fix this** — the gap
is deliberate and the reasoning is sound.

---

## Step 1 — Record the player's track

**The only thing standing between this system and running.**

`Player.cs` (Unity) owns the walking character. Once a minute of sim time, append a `Step` to a
`PlayerTrack`: the tile they are on, and how visible they were.

Decisions to make, and they are small:

- **Where the track lives.** `VillageHost` is the natural owner — it already holds `Sim`,
  `People` and `World`, and everything else hangs off it.
- **`Visibly`** — the enum exists; read it and fill it honestly rather than always passing the same
  value. This is the player's own contribution to being seen.
- **Only while `Walking`.** `Player.Walking` is already the in-body flag. No track while orbiting.

**Watch for:** the sim clock is 20 Hz and `PlayerTrack` is per *minute*. Sample on the minute
boundary, not per frame, or the track will be 1,200× larger than it needs to be and the minute
stamps will fight `Sighting.Minute`.

---

## Step 2 — Call it, from a diagnostic first

Follow `CountrysideDiagnostic` and `TechnologyDiagnostic`: an **`[Explicit]`** test in
`tools/Noir.Core.Tests` that fabricates a `PlayerTrack`, walks it past a few citizens' days, and
**prints what each of them could tell you.**

This is how the project has twice caught what tests could not — `CommercialRow`'s infill under the
lodge halls, and the technology layer putting a sixth of the town online with nothing to read it on.
An observation system is exactly the kind of thing that will pass every assertion and still produce
nonsense.

**What to read it for:** does the right *sort* of person see you? The retired man at his gate should
see more than the commuter who is in Danville from eight to five (`WHO-SEES-WHOM.md` §1). Somebody
should see nothing at all. If everybody sees everything, the range or light gates are wrong.

Only then wire it to an interaction.

---

## Step 3 — Give `Sightlines` the inputs that now exist

`Sightlines` **predates `Daylight` and `Fields`**, and still uses its own hand-rolled light model:

```csharp
int light = LightAt(minuteOfDay);   // 2 day, 1 dusk, 0 night
```

Two better inputs are built, tested, and currently read by nobody:

**`Daylight`** — real sunrise and sunset for the crossing's own coordinates, on the **pre-2007 DST
rule** that actually applied in this window. `THE-YEAR.md`: sunset is **16:27 on 21 December** and
**20:24 on 21 June**. A three-band constant cannot express that, and the whole point of
`WHO-SEES-WHOM.md` is that *"the same walk home is witnessed in June and unwitnessed in December."*

**`Fields.BlocksSightline`** — built precisely so standing corn blocks sight, and **called by
nothing**. `THE-YEAR.md` argues corn is a **2.5 m barrier over half the map from July to October**,
then gone in three weeks. A sightline model that ignores it is wrong for a quarter of the year.

Both are reachable already: `Daylight` is in `Contracts`, `Fields` is in `World`, and `Witness`
references both.

> **This will break a test on purpose.** `Sightlines` documents a deliberate divergence — *"the
> plan's table says a night sighting past 30 tiles is nothing, but the clamp floors it at
> Glimpsed... That is pinned by a test and left open on purpose."* Replacing `LightAt` with real
> darkness **should** move that. Update the pin deliberately and say why; do not delete it.

---

## Not now, and flagged rather than hidden

- **Walking witnesses.** `Recollection` says plainly this needs routing. It is the single biggest
  expansion of the system and should be scoped on its own.
- **`ObservationLog`.** Given `Recollection` recomputes rather than accumulates, it is worth asking
  what the log is *for* before wiring it. It may be for the investigation's notes rather than the
  world's memory. **Ask before assuming.**

---

## Verification

Core-only for Steps 2 and 3; Step 1 touches one Unity file.

```
dotnet test -c Release tools/Noir.Core.Tests
dotnet test -c Release tools/Noir.Core.Tests --filter "Name=PrintWhatTheySaw" -l "console;verbosity=detailed"
```

**The firewall tests are the ones that matter here.** If `ObservationFirewallTests` or
`WitnessFirewallTests` go red, stop — that is the architecture telling you the step was wrong, not a
test needing an update.

And then the fourth leg, which Core tests cannot do: **press Play, walk the player past somebody's
front gate, and ask them what they saw.**
