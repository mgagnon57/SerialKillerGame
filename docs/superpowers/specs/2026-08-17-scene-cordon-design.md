# The scene cordon — the officer visibly controls the street

Owner request 2026-08-17, after watching his own hit-and-run held for two town-hours by an
officer who just stood there: *"the cop that shows up on scene should put up road blocks,
crime tape, something, maybe direct traffic?"* Design settled in chat the same evening:
**cars really react** (not dressing), and the officer **directs traffic through** one open
lane rather than closing the street — the most 1991 street-scene of the options, chosen over
re-route-around and queue-at-the-tape. Props are **generated simple** (striped sawhorses, a
tape line, flat-color style) so the feature does not wait on modeling; owner models replace
them later with no code change, the cruiser's own precedent.

## What exists already, and is kept

- `ResponseCases` broadcasts every moment the cordon needs: `OfficerArrived` (cordon up),
  the scene-cleared teardown that already emits `VehiclesLeave` (cordon down). **The machine
  is not touched** — the cordon is host-side, keyed off transitions `VillageHost.RunResponse`
  already handles. The response path's rules bind as ever: no RNG, orders never predict travel.
- `CitySignals` proves the town knows how to alternate two flows on a cycle (14+3+1 twice,
  36.0 s). The cordon borrows the *rhythm*, not the system: its alternation is a private
  timer, not a third signalised junction (the PlayMode gate pins `2 signalised (8 heads)`).
- The one-clock ruling: anything that paces the town runs on the SIM clock. The alternation
  timer derives its phase from `Sim.Clock` — deterministic, seed-stable, pausable.
- `Activity.Gawking` (landed with the police response) is the precedent for a new activity:
  enum member + `Content/animations.txt` row in the SAME commit, `EveryActivityHasARowInTheRealFile`
  enforcing the pairing, stand-as semantics in `RespondTests`.
- The junction graph and lane topology are survey artifacts and are **never mutated live**.

## The design

1. **Cordon dressing, generated.** When the officer arrives, the host stands props at the
   scene's street edges: two striped sawhorse barricades (procedural boxes, measured-mean
   colors per `Materials3D` convention) flanking the closed half of the street, and a tape
   line (a thin stretched quad, police-line yellow) ringing the scene tile on the non-street
   sides. Props are plain scenery objects owned by the case's scene root; scene-cleared
   destroys them with everything else. A later `models.txt`-style swap can stand owner
   models in their place without touching the logic.

2. **The pinch.** The closed half is the half of the carriageway containing the body's tile;
   the other half stays open. Two hold lines are computed on the lane paths approaching the
   scene segment from each direction, a car-length short of the barricades.

3. **Alternating release, on the sim clock.** A cordon timer alternates which direction may
   pass: green-for-A / all-hold / green-for-B / all-hold, phased off `Sim.Clock.MinuteOfDay`
   so it consumes no RNG and replays identically. Starting timing borrows the signal cycle's
   proportions (tune by watching, not by spec). Cars whose path enters the scene segment
   check the cordon exactly where they already check car-following: released cars proceed at
   crawl speed on a borrowed-lane offset path around the barricade and rejoin their own lane
   past the scene; held cars wait at the hold line. The PLAYER's car obeys the same hold
   line. **No lane-graph or junction change of any kind.**

4. **The officer directs.** `Activity.DirectingTraffic` (new, the Gawking pattern): the
   officer stands at the pinch — not on the body — facing the open lane, playing a
   directing/hand-raising clip from the existing Townsfolk set via a new `animations.txt`
   row. He is still the scene's officer: canvass and case flow are unchanged; this is where
   he STANDS and what he PLAYS while the machine runs the case. Witness/testimony content is
   untouched.

5. **Teardown.** Scene cleared → props destroyed, hold lines released, timer gone, officer
   leaves as he already does. A `CloseLoudly` teardown (the test-residue path) tears the
   cordon down the same way — nothing may leak a barricade into the next test.

## Testing

- **Core gate:** `Activity.DirectingTraffic` row covered automatically by
  `EveryActivityHasARowInTheRealFile`; stand-as additions in `RespondTests` mirroring
  Gawking's.
- **PlayMode gate:** the existing response scenario grows assertions: cordon props exist
  while the scene is held and are gone after close; during the held scene no car waits at
  the pinch longer than N alternation cycles (mirroring `NoCarWaitsForeverAtTheHeadOfAClearQueue`'s
  discipline: name the failure, run twice before believing a number).
- **Look at it:** the real acceptance test is the street scene — barricades up, tape ringing
  the body, the officer waving one direction through while the other waits. The tests cannot
  see ugly.

## Out of scope, named so nobody wonders

- Pedestrian/gawker control — the officer's step-back ring already handles the crowd.
- Re-routing or road closure — rejected in design; the street stays open through the pinch.
- Owner-modeled cordon props — the generated ones hold the slot.
- A second officer or county deputy working traffic separately — Rossville has one response
  team, and the one officer does both jobs by standing at the pinch.
- Any change to canvass, testimony, or case-file behavior.
