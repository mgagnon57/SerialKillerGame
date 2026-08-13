# Handoff — the people/animation session

Written 2026-08-03 by the terrain/buildings session, for a second session running in parallel.

---

## The one hard rule: Unity is exclusive

**Unity allows one instance per project.** Two Unity processes on the same `Library/` risk
corrupting the asset database, and a `-batchmode` run while the editor is open simply fails.

**You have Unity.** The terrain session has stopped using it and will queue anything needing a
render until you say you are done. If that changes, say so — it needs to be explicit, not assumed.

## Who owns which files

| yours | theirs — do not edit |
|---|---|
| `Assets/Noir/Unity/AgentMeshView.cs` | `Assets/Noir/Unity/Massing/**` |
| `Assets/Noir/Unity/AgentAnimation.cs` | `Assets/Noir/Unity/CityStreets.cs` |
| `Assets/Noir/Unity/AgentBody.cs` | `Assets/Noir/Unity/CityTraffic.cs` |
| `Assets/Noir/Unity/AgentFigure.cs` | `Assets/Noir/Unity/Materials3D.cs` |
| `Assets/Noir/Unity/Player.cs` | `Assets/Noir/Unity/SurfaceTextures.cs` |
| `Assets/Noir/Editor/AnimatorBuild.cs` | `Assets/Noir/Editor/LayerShot.cs`, `HouseProto.cs` |
| `Assets/Noir/Animations/**` | `Content/city.txt` |
| `Content/animations.txt` | `docs/research/**` |

**`Assets/Noir/Unity/VillageHost.cs` is shared, and you own it from now on.** The terrain session
finished its edits there and committed them; it will not touch the file again without saying so.

**Git discipline, already project convention and now load-bearing:** never `git add -A` or
`git add .`. Stage only files you actually edited. The working tree carries unrelated dirty files
from both sessions plus some pre-existing ones.

---

## What landed tonight, before you started

All committed on `main`. Relevant to you:

- **A layer system.** `Layers.cs` / `LayerPanel.cs` — sixteen independent switches for what is
  drawn, `L` opens the panel in Play. **`Layers.Kind.People` already exists and is already wired**
  in `VillageHost.cs`: `if (_agentView != null) Layers.Register(Layers.Kind.People, _agentView.gameObject);`
  So the moment people are drawn, they get a switch for free.
- The town now comes up **whole by default** (was: dark survey plan).
- Chicago Street runs on its **real surveyed curve** with asphalt on it.
- Ground textures are the pack's 2048px PBR.

**Suite state you are inheriting:** Core `dotnet test -c Release tools/Noir.Core.Tests` →
**227 pass / 2 fail**, the two `TwoToOneTests` that fail by design. PlayMode → **11 of 13**.

---

## The two PlayMode failures, and why one of them is yours

```
1. PeopleDiagnostics.WhyAreThePeopleNotAnimating  — "no AgentMeshView - are the people drawn?"
2. TrafficPlayTests.NoVehicleEverLeavesTheRoad    — a car ~9m off the asphalt mid-junction
```

**#1 is yours, and it is a gift.** It fails only because `VillageHost.ShowPeople` is hardcoded
`false` (`VillageHost.cs:106`), so no `AgentMeshView` is ever built. **Flip that flag and the test
becomes a real harness for exactly the work you are doing.** Read it before you start —
`Assets/Noir/PlayTests/PeopleDiagnostics.cs` — it is unusually good:

- counts Animators and separates the five distinct reasons one can silently fail to play:
  no controller, no avatar, avatar not humanoid, disabled, culled
- watches whether `normalizedTime` actually **advances** over a second — "configured correctly"
  and "playing" are different questions and it asks both
- drives the simulation clock to 07:00, 09:00, … 17:00 rather than waiting, because at batch-mode
  frame rates nobody in Northgate ever gets out of bed
- **the gliding test**: of the people the simulation says are *moving*, how many have the wrong
  clip playing. Gated at 2% rather than zero, with the reasoning written down
- **the skating test**: right clip, wrong playback rate, so the feet plant and drag

**#2 is the terrain session's** and is being handled there. Ignore it.

---

## The animation system already exists — you are extending it, not building it

Read `Content/animations.txt`'s header first. Its design is the thing to work with:

> *"ADDING AN ANIMATION IS A LINE HERE AND NOTHING ELSE. Drop the .fbx in
> `Assets/Noir/Animations` — it configures its own import — add its name to a row, and everybody
> in that situation can play it. No C#, no Animator graph, no editor work."*

Current state: **9 clips, all wired** — Dig And Plant Seeds, Digging, Drinking, Looking Around,
Running, Sitting Idle, Standing Idle, Talking, Walking.

Two design decisions in that file worth honouring rather than rediscovering:

- **More than one clip to a row is the point.** Where a row names several, each person is given one
  chosen off *their own citizen key* — so a bar holds people drinking three different ways, and the
  same person drinks the same way every time you look. *"A room where everybody moves identically
  is the same failure as a street where everybody looks identical."*
- **Clip names are Mixamo's own, unchanged**, because the name survives import and becomes the
  Animator state name. Nothing to translate, so nothing to get wrong.

Tools that already exist:

- **`Noir/Check The Animations`** — lists every clip in the folder that no row mentions, so a
  downloaded-and-forgotten clip says so out loud. It writes `docs/animations-downloaded.md`.
  **Run this first**, before and after your import.
- **`Noir/Build The Townsfolk Animator`** (`Assets/Noir/Editor/AnimatorBuild.cs`) — builds the
  controller from the content file.

---

## Suggested order

1. `Noir/Check The Animations` — see what is there now.
2. Import your clips into `Assets/Noir/Animations`. Keep Mixamo names.
3. Add them to rows in `Content/animations.txt`. A situation with no row falls through to
   `default`, so a half-finished set degrades instead of breaking.
4. `Noir/Build The Townsfolk Animator`.
5. `Noir/Check The Animations` again — nothing should be listed as unused.
6. **Set `VillageHost.ShowPeople = true`** (`VillageHost.cs:106`).
7. Run PlayMode and let `WhyAreThePeopleNotAnimating` grade the result:
   ```
   Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode ^
     -assemblyNames Noir.PlayTests -testResults <xml> -logFile <log>
   ```
   **`-assemblyNames Noir.PlayTests` is not optional** — without it the runner also discovers
   `LLMUnityTests.TestLLM`, whose constructor downloads a language model, and the run never
   finishes. **Do not pass `-nographics`** — two tests render and fail spuriously without it.
8. Press Play and look. `L` → the People layer switch is already there.

## Things that will bite you

- **`Time.timeScale` does not speed the sim clock.** The simulation runs on
  `Time.unscaledDeltaTime` deliberately — how fast a day passes is a property of the game, not of
  Unity. Anything asserting on sim time waits in real seconds.
- **The city is built once and shared by every test in a run.** Anything you change on
  `VillageHost` — especially `SpeedIndex` — must be restored in a teardown, or the next test fails
  for reasons that have nothing to do with it.
- **1,300 people is the target and the number is load-bearing.** Traffic and population both scale
  off `WorldModel.Households`, which counts *units*, not buildings. Do not scale anything off the
  building count — a terrace is one building with four front doors.
- **`docs/STATE.md`'s top banner is stale** and describes a superseded 960×960 map. Trust
  `docs/HANDOFF.md`.
