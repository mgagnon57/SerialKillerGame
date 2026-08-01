# What the town saw: reconstructed witness testimony

**2026-08-01.** The first half of LLM-voiced townsfolk dialogue — and deliberately the half with
no LLM in it.

## Why this before the dialogue

Dialogue was the ask; evidence is the risk. A townsperson worth interrogating is one who knows
something, and nothing in the running game has ever produced a `Sighting`. Building a voice on
top of evidence that turns out to be uniformly *"a figure, unnoticed"* would be a port
delivering nothing, discovered late.

So this spec builds the evidence and measures it. The voice comes after, against real material.

## The shape of the whole thing, for context

The eventual mechanic is the detective interviewing townsfolk while the player — who is the
killer — listens to their own week described back to them. When the LLM arrives it will be
allowed to decide *manner* freely and *fact* not at all: every factual claim in a generated
line must come from a closed list of tokens this layer supplied, and anything else is stripped
before the player sees it. The firewall gets enforced twice — once by the assembly graph before
generation, once by a validator after it.

None of that is in scope here. It is written down because it is why the output of this layer is
a list of narrow typed facts rather than prose.

## 1. `Noir.Core.Witness`, and which way it points

A new assembly referencing `Noir.Core.People`, `Noir.Core.World`, `Noir.Core.Contracts` and
`Noir.Core.Observation`.

That list looks like a firewall breach and is the opposite of one. The direction is everything:

- **`Noir.Core.Observation` is unchanged.** It still references `Contracts` and nothing else, and
  still cannot name a citizen. `ObservationFirewallTests` keeps pinning that list.
- **`Noir.Core.Witness` is the producer.** It knows the day plans, the player's track, who was
  where. Its only exported operation returns `Sighting[]` — a type that structurally cannot carry
  any of it.

Knowledge flows one way, through a bottleneck that cannot hold identity. The narrowing is
enforced by the compiler at the boundary rather than by anybody's discipline.

**The rule this adds, and it goes in the assembly's header comment:** nothing may reference
`Noir.Core.Witness` except the caller that asks a question of it. The moment one scope holds a
`Sighting[]` and a `DayPlan` at once, the firewall is decorative.

## 2. `PlayerTrack` — the only thing stored

Every citizen's past replays deterministically from `DayPlanner.Plan(world, population, who,
day)`, keyed on `Citizen.Key`. The player has no plan and no key, so their movement is the one
piece of genuine history the simulation has to keep.

One entry per minute:

- the tile they were on
- a small flag set for what was *visibly* true — carrying, moving quickly, in company

A fortnight is about 20,000 entries at a handful of bytes each.

It is **not** a record of what the player did. No place entered, no activity, no intent. Only
where a body was and what it looked like from outside, because that is the whole of what the
reconstruction is permitted to consult. A field here that named a place would put the answer to
the investigation one dereference from the witness.

## 3. `Recollection.WhatTheySaw`

```
Sighting[] WhatTheySaw(Citizen who, int day, PlayerTrack track, WorldModel world)
```

Replay that citizen's day. For each minute, ask whether they and the player were close enough,
in line of sight, in enough light, and paying enough attention. Each yes is a candidate, which
then degrades into a `Sighting`.

**Degradation is seeded on `citizen.Key` combined with the minute.** The grocer's memory must be
identical every time it is asked. A witness whose story changes between two identical questions
is not a fallible witness, it is a bug — and once an interrogation can be repeated, an unseeded
memory becomes a slot machine the player pulls until the answer is convenient.

What degradation does:

- **Distance and light set `SightingClarity`**, and clarity gates how many `PersonDescription`
  bands survive at all. At `Glimpsed` most bands stay `Unnoticed`, which is what that type's own
  comment says the common case looks like.
- **`Sociability` and `Beats` set attention.** The man whose particulars have him at his gate
  every evening sees a great deal; the one who keeps his head down sees almost nothing. The
  variation is already authored, in sentences somebody wrote about these people.
- **`ApparentSex` can be wrong at night.** The enum's comment already promises this. `Citizen.Male`
  exists precisely for it and has never had a consumer.
- **Minutes blur.** Witnesses say "about half seven", so the minute is rounded coarsely, and more
  coarsely at low clarity.

## 4. How we find out whether it worked

The measure is not that it compiles. It is a **statement census**: simulate a fortnight,
reconstruct every citizen's testimony, print the distribution of what they hold.

- If ~90% are bare *"a figure"*, the evidence is too thin to interrogate and the tuning is wrong.
  Better learned from a histogram than from a finished dialogue system.
- The target shape is a scatter of witnesses each holding one narrow fragment, where the
  fragments mean something only assembled.

Two tests with teeth:

- **Determinism** — same citizen, same day, same track, twice: byte-identical output.
- **No leakage** — reconstruct a day in which the player did something significant, and assert no
  `Sighting` distinguishes it from an innocent day with identical movements. A difference means
  the reconstruction is reading intent rather than watching a body.

Both run in `tools/Noir.Core.Tests`, next to `ObservationFirewallTests`.

## Out of scope, named so it stays out

`ITownsfolkVoice` and its backends. The claim validator. Memory decay and contamination — a
reconstructed memory has nowhere to keep a change, and giving it one is a later spec, not a
field bolted onto `Sighting`. Any interrogation UI. Any detective.
