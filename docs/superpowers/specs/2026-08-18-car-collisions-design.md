# Car collisions, phase 1 — the town crashes on its own, and the law decides

Owner request 2026-08-17, verbatim intent: *"two cars crash, cops come, check for DUI or
ticket or let go."* Design settled in chat that night, four rulings: the crashes are **the
town's own** (ambient, seeded, deterministic — not player-caused, not emergent physics);
**about one per town-day**, clustered where crashes really happen (the commute peaks and
pub-close); a failed check ends in the **full arc** — cuffed, a ride in the cruiser, days
away, and a tow truck for the car; and **mostly benders, rarely worse** — the occasional bad
one downs a driver and the existing ambulance arc joins the scene. Split ruled 2026-08-18:
**phase 1 is the crash, the case, and the adjudication** (Core-heavy, playable outcome);
phase 2 is the street spectacle (wrecker rig, crumpled wreck models, the cuffed walk).

> **Landed 2026-08-18**, plan `docs/superpowers/plans/2026-08-18-car-collisions-phase1.md`.
> Core gate measured **596 pass, 0 fail, 8 skipped, 2 m 12 s** (+14 over the 582 baseline).
> PlayMode gate measured the same night: **34 of 34 PASS, 1 skipped, 2497 s** —
> `AStagedCollisionRunsToItsVerdict` ran the whole arc live (kerb interviews, arrest, tow,
> clean close), and the ambient daily crash fired deterministically at 17:15 in all seven
> runs it took to re-harden the older response scenario against a town that now crashes on
> its own (see CLAUDE.md's 34-of-34 baseline for the five test assumptions that fell).
> Still open: the live look — stand at the tavern at closing on a crash day.

## What exists already, and is kept

- **The response machine** (`ResponseCases`, Contracts-only, RNG-free, minute-driven) runs
  person-hit cases end to end: discovery, officer, county, canvass, ambulance, teardown.
  The collision case is a second FLAVOR through the same machine, not a second machine.
- **The cordon** (landed 2026-08-18) rises with the officer at any held scene — a collision
  scene gets tape, barricades and the directed lane for free.
- **The rigs** (`CityResponse`): cruiser (with light bar), county car, ambulance — all
  reused. The arrest ride is the existing Board/Alight machinery. The pack carries
  `Car_Towtruck_Modern` for phase 2's wrecker; phase 1 emits the TOW ORDER and closes the
  scene without the visual rig.
- **The event-testimony machinery** (witness voices phase 2): `EventAct` + a forward-only
  event store threaded through `Recollection` is now a proven pattern (`HitEvents`,
  `AskEvents`). Collisions add the third kind the same way.
- **Day plans** know who is where all day — including who spent the evening `AtThePub`.
  That knowledge IS the breathalyzer: no new drunkenness state.
- **The RNG rules**: planning-time variation is hash-of-seed (`Rolls.Int`-style, stateless);
  the response path itself consumes NO randomness.

## The design

1. **The crash planner (Core, new — `CrashPlanner`).** Once per town-day, a pure function of
   (seed, day, world, population) picks at most one crash: a MINUTE drawn from a fixed
   weighting table peaking at the commute hours (07:00/17:00, the IDOT counts' own shape)
   and at pub-close; TWO DRIVERS whose own day plans actually have them travelling by car at
   that minute (the pub-leaver drives home at closing; commuters at rush — a day with no
   qualifying pair is a day with no crash, and that is honest); a SCENE tile on the road
   where their planned routes meet or cross, else where the first driver's route stands at
   that minute; SEVERITY by hash threshold — most crashes are benders, a small fraction
   (roughly one in six) also downs the at-fault driver. Deterministic: same seed, same day,
   same crash. No storage; the plan is recomputed on demand like day plans are.

2. **Witnesses can testify about it.** `EventAct.CarsCollided`, recorded at crash minute and
   tile in a `CrashEvents` store (the `AskEvents` shape: forward-only, minute + tile, no
   identity), threaded through `Recollection.WhatTheySawOfEvents`/`AskInEnglish` as the
   third kind. The sentence: "…I saw two cars come together." Vague as ever — no plates, no
   names. The two car descriptions degrade through the existing `Degradation.CarRegistered`
   with the drivers' household car tones/shapes where known, `Unnoticed` otherwise.

3. **The case flavor (`ResponseCases`).** A collision case is opened AT crash minute (the
   crash is loud — its own discovery evidence; the alarm raises on the first witness sighting
   exactly as a body does, typically fast because two cars in a street are unmissable). The
   arc reuses the existing states with one substitution: for a bender, the door-to-door
   canvass is replaced by ROADSIDE INTERVIEWS — two `InterviewDriver` orders, a few minutes
   each, answered through the same verbatim-file seam the canvass uses. Then the machine
   emits the ADJUDICATION order, decided in Core as a pure function of the drivers' own days:
   - **DUI** — the at-fault driver's plan had them `AtThePub` within the last 3 hours:
     `ArrestDriver` (the cruiser takes them; `AwayFromTown` for days, the hospital pattern —
     the county lockup), then `TowVehicle` for their car.
   - **Ticket** — sober but at fault (fault = a hash-of-seed coin the planner already
     stamped): `TicketDriver`, a long roadside stop; both drive on.
   - **Let go** — neither: `ReleaseDrivers`, everyone leaves.
   Every order writes the file in the canvass's verbatim style ("case N: citizen W blew
   clean", "case N: citizen W is under arrest"). An INJURY crash additionally runs the
   existing ambulance arc for the downed driver before adjudication.

4. **The host (minimal phase 1 staging).** At crash minute the host: downs nothing (bender)
   or downs the at-fault driver (injury) via the existing victim machinery; stands the TWO
   INVOLVED CARS angled nose-to-fender at the scene (plain pack car instances, the parked-car
   idiom — crumple detail is phase 2); interrupts both drivers' days (they stand at the
   scene, `Responding`-style, until released, arrested, or taken away); and executes the
   orders with existing pieces — officer + cordon rise as they already do, the cruiser
   carries the arrested driver away, the cars despawn when the scene clears (`VehiclesLeave`;
   the wrecker VISUAL is phase 2, the tow is real in the record). StreetVoices lines at the
   obvious beats ("he came out of nowhere—"); the case ticker carries the case like any
   other.

5. **Determinism and the clock.** The planner is hash-of-seed; the case machine stays
   RNG-free; everything paces on the sim clock. A replay of the same seed produces the same
   crash, the same interviews, the same verdict, the same tow.

## Testing

- **Core gate:** `CrashPlannerTests` — determinism (same seed twice → identical crash),
  the no-qualifying-pair day produces no crash, minute weighting sanity (a crash minute is
  always one where both drivers are travelling); `CrashEventsTests` (the store, mirroring
  AskEventsTests); adjudication table tests (pub-within-3-hours → DUI; the fault coin is
  stable per seed); `ResponseCasesTests` additions for the collision arc (interview
  substitution, the three verdict orders, tow, injury variant joining the ambulance arc);
  `EventTestimonyTests` addition for the CarsCollided sentence and merge.
- **PlayMode:** a staged collision scenario in the response suite (stage via the planner's
  own output at a forced minute), asserting the case runs to Closed and the involved cars
  appear and clear. At the next editor-closed gate window, as ever.
- **Look at it:** stand at the pub at closing on a crash day and watch the town do the rest.

## Out of scope, named so nobody wonders (phase 2 and later)

- The wrecker rig driving in and hauling the car out (`Car_Towtruck_Modern` waits for it).
- Crumpled/damaged wreck models, glass, skid marks; crash sound.
- The cuffed walk (arrest is a board-and-depart in phase 1).
- Player-caused car-on-car crashes (today the player's car passes through parked cars'
  status quo unchanged; wiring the player into collision cases is its own decision).
- More than one crash per day, multi-car pileups, weather as a cause.
- Any change to what witnesses know or how precisely they say it.
