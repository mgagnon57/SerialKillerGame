# One Clock: the ambient town joins the sim clock

**Decision (owner, 2026-08-16):** "One clock for everything." The ambient fleet, the signals
and the elevated train move onto the SIM clock, sliced the way `CityResponse` already slices
its drives. This **reverses the written ruling** in `CityResponse.cs:33-42` ("the ambient
traffic is scenery and is entitled to run off `Time.deltaTime`... THE TWO THEREFORE DIVERGE
ABOVE 1x, BY DESIGN") — that paragraph must be rewritten, not deleted, so the reversal is on
the record.

## The measured problem

The owner plays at the default 10× (`Speeds[5]`; nothing serializes an override). At 10×:

| Mover | Clock today | On screen at 10× |
|---|---|---|
| Townsfolk walking (1.35 m/s adult, `Citizen.cs:307`) | sim | **13.5 m/s ≈ 30 mph** |
| Ambient cars (`Speed = 8f`, `CityTraffic.cs:43`) | real (`Time.deltaTime`, :977) | **8 m/s ≈ 18 mph** |
| Signals (36.0 s cycle, `CitySignals.cs:57`) | real (`Time.time`, :151) | unchanged by the dial |
| Elevated train (11 m/s, `CityTrain.cs:22`) | real (:45) | unchanged by the dial |
| Response rigs (10 m/s, `CityResponse.cs:70`) | sim | 100 m/s |
| Player (2/5.3 on foot, 12 m/s car) | real | unchanged by the dial |

People outrun the traffic at every dial above 1×; at 10× a walking adult beats the player's
own car. Fleet SIZE is already sim-driven (`CarsOutByHour` indexed by `Sim.Clock.MinuteOfDay`),
so the fleet was half-converted from birth. A second, separate effect makes it read worse: the
walk clip pegs at 2× its authored rate (`AgentAnimation.Fastest`), so above ~2× the figures
glide — that is out of scope here and already ruled (the town ambles, 2026-08-08).

## The design

One pattern, already proven by `CityResponse.Update` (`CityResponse.cs:833-838`): each
consumer keeps `_lastTick`, reads `Sim.Clock.Tick`, and advances by
`dtSim = (now - lastTick) / GameClock.TicksPerSecond`. Pause is free (`dtSim == 0`), the skip
drain is free (the clock jumps, the slice cap eats it), and nobody ever reads `SpeedIndex`.

- **CityTraffic**: `_owed` accumulates town seconds instead of `Time.deltaTime`.
  `MostSlices` 12 → 160, sized for the 300× dial at 60 fps (5 town-seconds a frame = 150
  slices) with headroom; beyond that the fleet falls behind rather than teleports, exactly as
  before. All in-file numbers (`Speed`, `TurnSpeed`, `Creep`, `TurnPace`'s 25 s patience,
  `Pace`) keep their values — their unit becomes "per SIM second" together, so every internal
  comparison is untouched.
- **CitySignals**: `State()` stops reading `Time.time`; it reads town seconds pushed by
  `VillageHost.Update`. Cycle stays 36.0 — sim seconds now.
- **CityTrain**: same `_lastTick` pattern; 11 m/s becomes 11 m/sim-s.
- **CityResponse**: the ONE real-clock exception, `Patience = 60` REAL seconds, converts to
  60 SIM seconds — its whole justification ("everything that holds a rig clears on the wall
  clock") inverts once the fleet and the signal cycle are sim-clocked. The held charge uses
  the frame's `dtSim` instead of `Time.unscaledDeltaTime`. The class-header ruling paragraph
  is rewritten to record the 2026-08-16 reversal.
- **The player stays on the real clock**, car and feet. Input-driven motion cannot scale with
  a fast-forward dial and stay drivable. Consequence, accepted: above 1× the scaled town
  outpaces the player; the witness thresholds against the player (1.5 m/s harm, 8 m/s fatal,
  3.2 m/s running) stay real-clock and stay correct because the player is real-clock.

## What must not break

- **A paused town is paused.** SpeedIndex 0 freezes cars, lights and train — today they
  visibly keep running through a pause. This becomes the regression gate
  (`AmbientTrafficFreezesWhenTheTownIsPaused`).
- **TrafficPlayTests' real-second assertions** (waits vs the 36 s cycle, red-light sampling)
  stay valid by PINNING the traffic fixtures to 1× (`SpeedIndex = 3`) in setup and restoring
  in teardown — at 1× sim seconds ARE real seconds and every existing number is literally
  unchanged. Any `Time.timeScale` compression in those tests must be checked: signals no
  longer listen to it.
- **`NOIR_BUILT_TOWN` and fleet behavior at the gate's noon town**: unchanged — Retime and
  CarsOutByHour were already sim-driven.
- Core is untouched: no file under `Assets/Noir/Core` changes.

## Out of scope

- The walk-clip 2× glide above 2× (ruled 2026-08-08; unchanged).
- The response rigs "arriving" far from the scene (IDEAS.md finding, 2026-08-16; the Patience
  unit change touches the same code but the arrival-position defect stays open).
- Any speed VALUE change (people 1.35, cars 8, train 11 all keep their numbers).
