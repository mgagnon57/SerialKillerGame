# Animation fixes — the work list

**This is a work file. Delete it when the last item lands.** It exists because a read-only audit on
2026-08-08 found 86 verified faults in the animation estate and a planning pass turned them into 78
items. Nothing here is a fact about the project — the facts live in `CLAUDE.md`. This is a queue.

**How to use it.** Work in waves. A wave is a batch of edits that ends in ONE verification pass,
because a PlayMode run costs 6–15 minutes and this project's rule is *one render, fix everything,
one render*. Do not verify per item. Do not reorder items inside a wave without reading
[Hot files](#hot-files-where-two-items-collide) — five clusters want to edit the same 27-line method.

**Before wave 1:** answer the nine questions in [Decisions](#decisions-answer-these-first). Three of
them block work. The rest have a recommendation you can let stand by saying nothing.

**Item IDs** are namespaced by area: `DOT` the dotted-clip bug · `ROW` unreachable rows · `GATE` the
test gate · `PB` the player build · `SIM` the sim-to-animation channel · `RIG` rig correctness and
cost · `DOC` documentation drift.

---

## The standing gates

Every wave states which of these it must pass. Commands are in `CLAUDE.md`; do not re-derive them.

| Gate | Command | Today |
|---|---|---|
| Core | `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj` | 428 pass, ~2 min |
| Unity compiles | `dotnet build Noir.Unity.csproj -c Debug` and `Noir.Editor.csproj -c Debug` | clean |
| PlayMode | `-runTests -testPlatform PlayMode -assemblyNames Noir.PlayTests -testCategory "!Diagnostic"` | 13 of 13, ~6 min — **budget ten** |
| Editor-only | `python tools/check-editor-only.py` | exit 0 |
| Animator | `-executeMethod Noir.Editor.AnimatorBuild.Run` | exit 0 |
| Clips | `-executeMethod Noir.Editor.AnimationCheck.Run` | exit 0 |

**The Core baseline moves three times in this plan: 428 → 431 (W1) → 439 (W3) → ~449 (W5).** Each
wave edits `CLAUDE.md`'s table in the same commit as the tests that moved it. A stale number there
is the precise failure the *THIS FILE WINS* section was written about.

**Precondition for every `Unity.exe` command: the editor must be closed.** Check for `Unity.exe`
first. If he left it open deliberately, say so rather than killing his work — W2 needs no Unity and
can be landed while waiting.

---

## Decisions: answer these first

### 1. Should anyone in Rossville ever run? — **blocks W5**

The `Running` clip has never played once and cannot. The simulation's ceiling is about **3.4 mph**
(`Citizen.BaseSpeedIn` × terrain) and the clip is authored for someone covering **8 mph**, so the
row that calls it is dead code with a downloaded asset behind it. Measured, not inferred:
`Running=` appears zero times across all three recorded runs (`play.xml`, `play2.xml`, `play3.xml`)
against census lines reporting up to 317 people on the move.

- **(A) Delete the row and the `hurrying` parameter, root and branch.** Three lines of content, three
  signature changes, no Core change, no risk to the 2:1 ratchet.
- **(B) Let children at play actually run.** A Core change to citizen pace — the one thing in this
  whole plan that can turn the 2:1 ratchet red.
- **(C) Carry the journey's purpose on the agent and price haste separately.** Large, and the
  skeptic broke it: it multiplies the *leader's* walking speed by the *follower's* haste, so a hasty
  child outruns the adult it is paired with — and the school run is exactly what pairs them.

**Recommend (A).** If running belongs in this game it belongs to a story event — somebody fleeing —
not to a commute. Do not buy the expensive option because the FBX is already downloaded. *The plan
proceeds on (A) if no answer comes back.*

### 2. May a row key carry a selector? — **blocks W5**

Today a farmer types and faxes, four in ten lunchtime diner customers play `Drunk Idle`, adults on a
dinner break at the playground cheer like children, and children smoke at home. All of it is one
cause: `Drive` receives only the Activity, so place and person are discarded.

- **(A) `@place` only** — `atthepub@diner`, `atwork@tavern`. Fixes the place faults.
- **(B) `@place` and `:person`** where person is one of `child adult elder male female`. Fixes all of
  them, and buys the elder gait for free as content instead of C#.
- **(C) Widen the `Activity` enum** so the simulation carries the distinction.

**Recommend (B).** The parser needs no change at all — it takes the first whitespace-delimited word
as the key, and `atwork@tavern:elder` is one word — so this is a lookup change plus content rows,
far cheaper than eight faults suggests. The ladder is fixed at five rungs and printed in the file's
own header, place before person, stage before sex. Note the inspector will still *say* "at the pub"
for someone eating lunch at the diner; it prints the place name alongside, so it reads "at the pub,
at Dot's Diner". Fixing the words is a Reports change and should not be bought with enum members.

### 3. May the 2-minute Core gate go red because `Townsfolk.controller` is stale? — **blocks W1**

This is binary, not the three-way it was first written as: the two proposed tests carry the same
coupling in opposite directions, so making one `Explicit` does not remove it.

- **(1) Yes.** Both tests are normal `[Test]`; both failure messages carry the remedy verbatim.
- **(2) No.** Both become `[Test, Explicit, Category("Aspiration")]` beside the 2:1 rule. The third
  test — which reads only the filesystem and cannot go stale — stays in the gate either way.

**Recommend (1).** But the honest price is not "two minutes": it is that any session which edits
`Content/animations.txt` in either direction has to **close your editor** to get back to green, and
your normal working state is editor-open because you are the one who presses Play. That is the whole
cost, and it is why this is your call.

### 4. Non-uniform scale is shearing rigged limbs. Which do you want to lose?

`localScale = (wide, tall, wide)` with X/Z off Y by −6%/+8% above a skinned hierarchy. Bones rotating
under anisotropic scale shear — a short wide person gets arms the wrong thickness for their length,
visible from across a street. The project already knows this failure mode; `AgentFigure` avoids it
deliberately and says so.

- **(A) Uniform scale.** Rigged figures keep full height variation, lose a ±7% silhouette difference
  (roughly a stone). Build variety still comes from 25 cast models and the per-citizen UV shift.
- **(B) Keep both and accept the shear.**
- **(C) Buy separate heavy and slight meshes and cast by build.**

**Recommend (A).** The shear reads as a rendering fault from twenty feet; the missing width reads as
nothing at all at that distance. Not a determinism change — breadth is still hashed and still used by
the primitives, so no seed reproduces differently.

### 5. What should a headless run draw?

Once batch mode stops reading your personal layer preferences (GATE-8):

- **(A) Everything — the compiled default — with the PlayMode suite opting out of Trees, Farm and
  Powerlines in its own file.**
- **(B) Everything with no opt-out** — adds 12,804 trees and 17,405 farm pieces to every run.
- **(C) Keep reading your preferences**, and keep the situation where whatever you last toggled
  silently decides what a test measures.

**Recommend (A).** (C) is the cause of one of the two bugs already documented at length in
`CLAUDE.md`. The suite should say out loud, in its own file, which three things it is not drawing.

### 6. The walk averages 0.54x and structurally cannot reach 1.00x. The test floor is 0.50.

The clip is authored at 1.5 m/s (**3.4 mph**) while Rossville's adults walk 1.19–1.51 m/s before
terrain, so the average villager's honest ceiling is about 0.90x. A floor of 0.50 sits 0.04 from a
measured 0.54 **and will fire on its own without anybody changing anything.**

- **(A) Lower the floor to ~0.35 and accept that this town ambles.**
- **(B) Re-author the clip's m/s figure down so the mean sits near 0.95x.**
- **(C) Leave it and accept a red gate.**

**Recommend (A).** (B) is adjusting the instrument — the authored figure is a fact about the clip,
not a dial. A village where people walk slightly slower than a Mixamo cycle is correct; it is a small
town, not a city.

### 7. Loop Pose is off on all 87 clips. Turn it on?

Every looping clip has a seam at its wrap point and nothing has ever measured how bad it is.

- **(A) Measure first with a small diagnostic, then decide.**
- **(B) Turn it on for all 87 now** — 87 re-imports, and the automatic rule cannot reach them anyway.
- **(C) Leave it.**

**Recommend (A), and do not answer until the measurement exists.** `loopPose` does not merely mark a
clip, it *warps* it — harmless on a true cycle, wrong on the dozen one-shot gestures.

### 8. The player build: people now, or the pattern once for all seven systems?

Seven systems sit behind editor-only guards. A shipped build has capsule people and no bought props.

- **(A) Tier 1 only — make the failure loud.** Hours. Converts every silent degradation into a named
  line, and via PB-2 puts an actual walking body in a shipped exe.
- **(B) Tier 2 for the people** — a cast manifest in Resources, `AgentBody` de-guarded onto it.
  Realistically a week with several Unity round-trips.
- **(C) The manifest pattern across all seven, people as pilot.**

**Recommend (A) now.** People are the only one of the seven whose absence is visible from every
street, and the build's packed-asset dump lands in the same wave — so the size question behind (B)
and (C) becomes a log line rather than an argument before you have to decide it.

### 9. Two small ones

Should `AnimationCheck` enumerate the `Activity` enum so it structurally cannot miss a fifteenth
state (it says "the thirteen" and there are fourteen)? And should `CLAUDE.md` gain a row about the
animation estate? **Recommend yes to both** — the `CLAUDE.md` row is only honest if the two stale
`ASSETS.md` items land in the same commit, which is the "replace something" payment that file's own
law demands, and both are already in W2.

---

## The waves

### W1 — The dotted clip · the first commit — **DONE 2026-08-08**

> **Landed.** Core **428 → 431** as predicted, `Noir.Editor` compiles, `check-editor-only` exit 0,
> `AnimatorBuild` rebuilt 87 states with the new guard silent, `animations-downloaded.md`
> regenerated by the tool rather than by hand.
>
> **The three contract tests were watched failing against the live fault before it was touched** —
> the table's view (`no state named 'Standing Idle Looking Ver. 1'`), the filesystem's (`a period
> in a filename`) and the controller's (`orphan state 'Standing Idle Looking Ver_ 1'`). Three
> angles, one bug.
>
> **DOT-2's guard has NOT been seen firing.** It was written and the fault was fixed before it ever
> ran, so it is a guard rail only ever seen passing - which this wave's own preamble warns about.
> Proving it costs two Unity round-trips to exercise a five-line string comparison, and DOT-3's
> tests cover the identical fault from disk with no Unity at all. Left deliberately, recorded here
> rather than quietly dropped.
>
> Decisions answered by the owner: **3 → (1) yes, normal tests**; **1 → (A) delete the Running
> row**; **2 → (B) `@place` and `:person`**. The last two unblock W5.

**Land and push this today.** No PlayMode, no rendering, no judgement calls. ~10 min machine time.

The order inside the wave is counter-intuitive and deliberate: **build the guards first and watch
them fail against the live fault, then fix it.** A guard rail that has only ever been seen passing
has not been tested.

| | |
|---|---|
| **DOT-2** | `AnimatorBuild` must compare the name it asked for against the name it got back |
| **DOT-3** | A Core test comparing controller state names against the table |
| **DOT-1** | Drop the period from the clip name |
| **DOT-8** | `animations.txt`'s header states something untrue, and that is part of why this survived |

**DOT-2** — `Assets/Noir/Editor/AnimatorBuild.cs:105-119`. `machine.AddState(pair.Key, …)` is not a
setter: `MakeUniqueStateName` owns the name that comes back. Capture the read-back in the existing
loop, collect mismatches, and after `SaveAssets()` at :127 `LogError` + `EditorApplication.Exit(1)`
in batch. Save-then-error is deliberate — the controller is still written, so 86 of 87 clips keep
working while somebody fixes the name. Do **not** try to mirror Unity's sanitisation rule; it is
internal, undocumented, and does more than replace `.`.

**DOT-3** — new `tools/Noir.Core.Tests/AnimatorContractTests.cs`. Copy the `RepoRoot()` helper
(16 files already use it). Read three sets from disk — clip names the table wants, FBX stems, and
`AnimatorState` names scoped out of the controller YAML — and assert three contracts: every wanted
clip has a state named exactly that; no orphan states; no filename carries a period. **Scope the
YAML parse**: a line equal to `AnimatorState:` arms a flag, the next `  m_Name:` while armed is a
state. Unscoped, `Townsfolk` and `Base Layer` come through as states. The csproj is SDK-style with
no explicit `Compile` items, so a new `.cs` is picked up automatically; a file under `tools/` needs
no `.meta`. **431 pass.**

**DOT-1** — the rename, **editor closed**:

1. `git mv` the `.fbx` **and** its `.fbx.meta` in the same step. An `.fbx` Unity sees without its
   meta gets a fresh GUID and the controller's motion reference dies silently.
   `Standing Idle Looking Ver. 1` → `Standing Idle Looking Ver 1`.
2. In the renamed meta, replace all **three** occurrences (lines 35, 533, 539). Line 35 is
   `clipAnimations[0].name` and is load-bearing — Unity will not regenerate it, because
   `AnimationImport` bails when `importSettingsMissing` is false.
3. `Content/animations.txt:105` and `:116`. **Do not reorder the rows** — the clip's index in its row
   is what `ClipFor` hashes, so leaving position alone keeps every citizen on the clip they have
   today. Determinism is untouched; a seed still reproduces the same village.
4. Reopen Unity once to reimport, then re-run `Noir/Build The Townsfolk Animator`.
5. Re-run `Noir/Check The Animations` to regenerate `docs/animations-downloaded.md` — do not
   hand-edit it.

Use `Edit` or Python, never PowerShell `Get-Content`/`Set-Content` — it mojibakes every em dash
silently. Commit with `git commit -F`, not `-m`.

**DOT-8** — amend the header to name the second step: adding a row is not enough, the controller only
carries clips a row asks for, so a row without a rebuild is a clip nobody can play. Same sentence
into `AnimatorBuild`'s class summary.

> **Gates.** `AnimatorBuild` exits **1** before DOT-1 naming the rename, **0** after. Core
> 428 → **431**, and `CLAUDE.md`'s table edited to 431 in the same commit. Both Unity assemblies
> compile. Nothing new lands under `Assets/` in this wave.

---

### W2 — Stale documents and blind tooling — **DONE 2026-08-08**

> **Landed, all thirteen.** Core still **431**, both Unity assemblies compile,
> `check-editor-only.py` exit 0 with **`AgentBody: Folk` now in its inventory** — it was invisible
> before, because the member pattern could not see `public const` inside a guard.
>
> **THIS WAVE'S GATE WAS WRONG AND THE GATE IS NOT MET.** It says "`AnimationCheck` exits 0". It
> exits **1**, and it did before any of this work: five clips carry root motion.
>
> ```
>   Walking Female      travel 1.56 m/s      genuinely travels - downloaded without In Place
>   Walking Male        travel 1.57 m/s      the same
>   Pushing             travel 0.13 m/s      against a StandingStill threshold of 0.10
>   Singing             travel 0.14 m/s      the same
>   Sitting Drinking    travel 0.14 m/s      the same
> ```
>
> **None of it reaches the game**: `AgentBody.cs:202` sets `applyRootMotion = false`, so the
> travel is never applied. The checker has no way to know that. Deliberately NOT resolved here -
> loosening `StandingStill` to make the gate green would be adjusting the instrument, and the two
> Walking clips are a real "re-download with In Place" job. Whoever takes it should decide whether
> the checker ought to know about `applyRootMotion` at all.
>
> **DOC-3 was verified rather than trusted**: `SKM_Woman_Rabbit_Easter`, `animationType: 2`. Note
> that a plain grep reports the pack folder empty - it is gitignored, so it needs `rg --no-ignore`.
>
> **DOC-8 turned out to matter more than its one line suggests.** `rig.ms` in the perf report is
> the ORBIT CAMERA, printed beside `sim.ms` in a town with 1,385 rigged people - so the one column
> a reader takes as "what the skinned figures cost" was the camera, and the animators, which were
> half the frame, appeared nowhere. Renamed `camera.ms`.

### W2 — Stale documents and blind tooling

Nothing here is visible to PlayMode and nothing touches Core behaviour, so it shares one cheap gate
set and costs zero Unity round-trips. **This is the wave to run while the editor is still open.**
~8 min machine.

It goes second because `docs/ASSETS.md` currently teaches per-file manual import — which
`AnimationImport`'s first-import-only guard would then preserve forever — and because `Sighting.cs`
carries a "STAGED, NOT LIVE" header that has already misled one auditor into a wrong finding. Every
later wave reads these documents.

| | |
|---|---|
| **DOC-1** | `ASSETS.md` teaches manual import including un-ticking Loop Time on one-shots |
| **DOC-2** | `ASSETS.md`'s `animations.txt` example (`1.4m/s`) and Activity count are wrong |
| **DOC-3** | The unnamed non-Humanoid character is identified: `SKM_Woman_Rabbit_Easter`, never cast |
| **DOC-4** | `AnimationImport`'s "there are no one-shots on the list" is false — there are a dozen |
| **DOC-5** | `AnimationCheck` says "thirteen Activity states"; there are fourteen — fix the comment **and** the sweep |
| **DOC-6** | `AnimatorBuild`'s header says "Nine states and no arrows"; it builds 87 |
| **DOC-7** | `Sighting.cs` / `PersonDescription.cs` "STAGED, NOT LIVE" headers are false |
| **DOC-8** | `rig.ms` is the camera, not the character rigs; the guidance block is dead code |
| **DOC-9** | Hook `animations-downloaded.md` regeneration to the step that must already re-run |
| **ROW-8** | `ASSETS.md` states a pace figure that does not match the file, and describes Running reaching a state it cannot |
| **GATE-9** | `AnimationCheck`'s batch exit code ignores the clips it says are missing |
| **PB-5** | `check-editor-only.py` cannot see `public const` inside a guard, and mishandles `#elif` |
| **RIG-12** | `PeopleProbe` keeps a private duplicate of the `Folk` path that was made public to kill it |

**DOC-7 is the important one.** The header claims nothing constructs a `Sighting` and that sixteen
values have no code path. Both are false: `Witness/Degradation.cs` produces the values,
`Recollection.cs` constructs the sightings, and the running game reaches them via
`VillageUI` → `VillageHost.AskWhatTheySaw`. Read all of those before writing the replacement. **Keep
every line of the FIREWALL block untouched** — it is still true.

**PB-5, corrected.** Edits 1 and 3 (the `const` alternative in `MEMBER`, and `editor_cond` reading
compound `#if`) are right. **Edit 2 as proposed reproduces the bug it names**: with one boolean per
level, `#if UNITY_EDITOR / #elif FOO / #else` marks the `#else` body — the branch that actually
compiles into a player — as editor-only. Use **two booleans per stack entry** (current region, and
whether any branch at this level was the editor branch). And do not write the proposed comment
claiming the one-boolean handling is correct; enshrining a false claim inside a correctness tool is
worse than the bug.

> **Gates.** `Noir.Editor` and `Noir.Unity` compile. Core still 431 — DOC-7 touches three Core files
> but comments only. `AnimationCheck` exits 0, and now exits non-zero when clips are missing.
> `check-editor-only.py` green, with `AgentBody: Folk` now present in its inventory and its answers
> otherwise identical on every existing guard. **Every replacement via `Edit`** — these files carry
> em dashes.
>
> Order matters in two files: `AnimationCheck` is DOC-5 → DOC-9 → GATE-9; `AnimatorBuild`'s header
> (DOC-6) is rewritten *after* W1 so it describes a guard that now exists.

---

### W3 — The table moves to Core, alone

**GATE-1** goes alone because it changes `AgentAnimation`'s internals that six later items read
through, and because two minutes of `dotnet test` proves it completely. Doing it before W4 means the
hub-file rewrite is written against the final shape rather than against an adapter.

**Feasibility is established from the files, not guessed.** The parser's only dependencies are
`System.Collections.Generic`, `System.Globalization` (Core already uses `InvariantCulture`),
`StringComparer.OrdinalIgnoreCase`, and the `Activity` enum which already lives in Core. Two things
pin it to Unity and both are removable: `ContentLoader.Read` and `Debug.LogWarning`.
`Noir.Core.People.asmdef` has `noEngineReferences: true`, and `Noir.Core.csproj` globs
`Assets\Noir\Core\**\*.cs`, so a new file needs no project edit. `Content/` is already copied beside
the test assembly, so the tests read the real `animations.txt`.

Create `Assets/Noir/Core/People/AnimationTable.cs` — a pure `Parse(string)`, same shape as
`PlaceKindTable.Parse`. **Do not name the type `Animation`**; `UnityEngine.Animation` exists and Core
bans clashing names. Expose `IReadOnlyList<string> Warnings` and let the host log them.

> **The trap nobody's cluster owns.** `AnimatorBuild.cs:83` and `AnimationCheck.cs:93` call
> `AgentAnimation.Reload()` outside any `TownPipeline.Build`, so the moment the table reads through
> `Noir.Core.Contracts.Content` they throw *"No content source installed"*. Both need
> `Content.Install(ContentLoader.AsSource)` first, and neither has a test. **Exercising both editor
> menu items by batch run is the only thing that catches this.**

Also rewrite W1's three DOT-3 tests onto the Core parser here — about ten lines, and the price of
having pushed the bug fix a day earlier.

> **Gates.** Core 431 → **439**, `CLAUDE.md` edited same commit. Both assemblies compile.
> `AnimatorBuild.Run` and `AnimationCheck.Run` each exit 0 by batch. The new `.cs` under `Assets/`
> **has its `.meta` committed with it** — three orphan `.meta` files are already loose in this tree.
> `-c Release` is not optional; Debug is 8–9 min and unstable on this machine.

---

### W4 — Both hub files, rewritten once, one PlayMode run

Twenty items, but only **four files**. Five clusters independently want to edit `Drive` and four want
to edit `Report`. The only way that does not cost four PlayMode runs and a merge fight is to rewrite
each method **once, in one sitting**. ~15 min machine if green, ~25 if it needs a second run.

Land the **instrument** commits first (GATE-2, GATE-4, GATE-6, GATE-3-as-log, RIG-6's counter) and
the **behaviour** commits second (the `Drive` rewrite, RIG-1, RIG-2, SIM-8, SIM-9), so if the single
run comes back red the commit boundary tells you which half.

**`Drive`'s edit order is not optional:** RIG-5 (restructure) → DOT-4 (speed after `HasState`) →
ROW-4 (empty-row stop) → DOT-5 (warn once) → RIG-2 (per-figure stride term). RIG-5 rewrites the exact
line RIG-2 adds a term to; doing RIG-2 first means writing it into a body RIG-5 then replaces.

**`Report`: GATE-2 must go first.** GATE-4's `still = People − Moving − Away` needs GATE-2's `Away`
field. And DOT-7's counter must be read for **every** person with a controller — above the
`if (!moves) continue;`, not below it, or non-moving victims stay invisible by construction.

| | |
|---|---|
| **DOT-4** | Freeze, do not treadmill — move `HasState` above the speed write and answer with `0f` |
| **DOT-5** | Say the missing state name once, gated by a static set cleared by `Reload()` |
| **DOT-7** | Count people whose wanted clip has no state — **counter only in this wave, no Assert** |
| **ROW-4** | Move pace normalisation above the empty-clip return — a sleeper currently holds the last *clamped* speed |
| **ROW-5** | Skip `AwayFromTown` in `Report` as `Refresh` already does — ~200 undrawn people are being counted |
| **RIG-1** | `updateMode = UnscaledTime` — the sim runs unscaled and so must the legs |
| **RIG-2** | Per-figure stride term, linear in height, pinned so adults do not move |
| **RIG-5** | Cache the row key per Activity; kill the double `Resolve` and two allocations per figure per frame |
| **RIG-6** | Skip the 1300-person refresh when the People layer is off (with the `_resync` flag) |
| **RIG-7** | `if (_controller == null)`, not `??=` — the null-coalescing operators cannot see Unity's fake null |
| **RIG-10** | Turn the silent Read/Write early-return into a named, once-only error |
| **RIG-11** | `MeshReadable` walks the 25 cast figures, not all 79 |
| **GATE-2** | Diagnose through `AgentMeshView`; assert the counts instead of logging them |
| **GATE-3** | Prove the **skeleton** moves — **measurement only in this wave** |
| **GATE-4** | An expectation the instrument does not get from the thing it checks (corrected formula) |
| **GATE-6** | Three of the six hourly censuses are dead and print identical lines |
| **GATE-7** | `LayerProof` teardown — snapshot at the top, restore in `[TearDown]` |
| **GATE-8** | Batch mode gets the compiled default, not a person's preferences (part two behind `isBatchMode`) |
| **SIM-8** | The bag reaches a rigged body — a prop on the right hand bone, not a clip |
| **SIM-9** | `CrossFadeInFixedTime`'s fourth argument: a deterministic per-citizen cycle offset |
| **RIG-4** | Uniform scale, *if the owner answered (A) to decision 4* |

**GATE-3 is the strongest single finding in the whole audit.** `play.log` says *"1 of 40 animators
advanced their clip and 39 did not"*, `play2.log` says *"0 … 40"*, `play3.log` says *"40 … 0"* — and
**all three runs passed.** In `play.log` all six sampled figures print the same state hash at
`t=0.00`: the controller's default state, never crossfaded away from. The gate has been red for
months with nobody able to see it. Land it as a **log** here, read the number, ratchet the assert in
W5. Do not relax a threshold to make a wave green.

Corrections the skeptics forced, which you must apply:

- **GATE-3's probe stays entirely inside `PeopleDiagnostics`** — no `BoneProbe` in production code.
  Use `view.GetComponentsInChildren<Animator>()`, which excludes the deactivated away-figures by
  construction; walking `_figures` by index picks them up and ~4 of 24 always count as failures for
  no fault. Take the delta in **local** space (two world positions ~1000 m out carry ~1.2e-4 m of
  error, only 8× under a 1 mm threshold). Force `AlwaysAnimate` or you are measuring the camera —
  and restore `cullingMode` in `try/finally`, not a `[TearDown]` a yield-break can skip.
- **GATE-4 must not reuse the mid-crossfade allowance for the stationary half.** It is written for
  the moving case; reversed, every person leaving a walk is a false positive for the full 0.25 s of
  the fade. Use the split form (`walkNow` / `inTransit`) and require `!inTransit` on the still
  branch. Measure before choosing a budget — the 2% figure is inherited from a different mechanism.
- **GATE-8 part two must open with `if (!Application.isBatchMode) return;`.**
  `Noir.PlayTests.asmdef` carries `defineConstraints ['UNITY_INCLUDE_TESTS']` and no
  `excludePlatforms`, so a `[RuntimeInitializeOnLoadMethod]` fires on **every** entry into Play, not
  only under the test runner. Without that line, he presses Play and permanently loses his trees,
  farm and power lines.
- **DOT-7 lands as a counter without its Assert.** As proposed it turns the six-minute gate red on a
  case `animations.txt` promises is legal. Hold the Assert until the number is measured and decision
  3 is answered.

> **Gates.** PlayMode `!Diagnostic` 13 of 13 — **budget ten minutes**. Core still 439, and
> `TheGapToTheRuleIsReported` must print **0.89 : 1** median and **0.33 : 1** tenth *identically* —
> nothing in this wave touches Core, so if those move, something else in the wave moved them and
> that is the finding. Both assemblies compile.
>
> **Read the `[body]` census line against `play.xml` / `play2.xml` / `play3.xml`**, which are still on
> disk as the baseline: `Sad Idle=` must have **disappeared**, `Running=` must still be absent, and
> an "out of town and not drawn" count near 200 must have appeared at noon.
>
> `try/finally` around anything after a `yield` — RIG-1 sets `timeScale`, GATE-3 sets `cullingMode`
> and `SpeedIndex`, RIG-6 sets the People layer. Layer preferences in `PlayerPrefs` identical before
> and after the run.

---

### W5 — Content rows, the selector channel, and the ratchets

Every one of these is an edit to `Content/animations.txt` or a threshold set from W4's measured
numbers, and they share one PlayMode run and one look-at-it. ~15 min machine plus **5 minutes of the
owner in Play**.

They come after W4 because three thresholds cannot honestly be chosen until W4 has run: GATE-10's
floor moves when RIG-2 lands, and GATE-3's and DOT-7's bars are ratcheted from W4's log.

| | |
|---|---|
| **SIM-2** | The channel: one `Situation` struct, and the selector lookup on top of W3's Core table |
| **SIM-3** | Split the `moving` row by sex — a third of men currently walk like women |
| **SIM-4** | `atthepub@diner`, `atthepub@casino`, `visiting@cinema` |
| **SIM-5** | `atwork@farm/mill/factory/garage/tavern/shop` — and Bartending moves to the staff row |
| **SIM-6** | Gate the playground flag on age (**the C# half is one line and blocked by nothing**) |
| **SIM-7** | `athome:child` without Smoking and Gaming |
| **ROW-2** | Delete the dead `hurrying` row and parameter, root and branch |
| **ROW-6** | Keep the `default` row as a net; make `AnimationCheck` **measure** the fall-through |
| **ROW-7** | Give `Idle` and `Standing Idle 03` a reachable row; leave `Sad Idle` on the net deliberately |
| **GATE-10** | Lower the walk-rate floor and write the derivation into the assert |
| **RIG-3** | `FootPlantDiagnostics` — the measurable that judges RIG-2 without eyes |
| **RIG-8** | Measure loop-seam quality inside `AnimationCheck`'s existing per-clip loop |

`Content/animations.txt` is edited by nine items across three clusters. **One pass. Header block
rewritten last**, because every other edit changes what it should say. Every row stays on **one
line** — the file's own header warns that a wrapped row silently becomes a situation named after its
first clip.

Every clip named in the new rows is already among the 87. Nothing needs downloading.

**Watch for silent typos.** A qualified key that matches nothing falls through silently. The kinds
are `cinema`, `diner`, `casino` — take the names from `Content/kinds.txt`, do not guess them.

> **Gates.** Core 439 → **~449**, `CLAUDE.md` same commit. The single most valuable of the new tests
> is the one asserting **no split locomotion row lost its `m/s` figure** — the parser writes
> `_paces[row] = 0` for every row and a pace of 0 plays at exactly 1.00x, so a forgotten figure
> silently reintroduces sliding feet for half the town.
>
> PlayMode 13 of 13 with GATE-3's and DOT-7's assertions **now live**.
>
> **LOOK AT IT.** No offline render shows rigged people — Snapshot and Crowd draw primitives,
> FilmStrip is aimed at stale coordinates, Tour draws the plan. This is five minutes of the owner in
> Play at **SpeedIndex 3 (1x)**. Tell him plainly that RIG-2 is invisible at the default 10x because
> every walker is already pinned to the 2x clamp — do not oversell the fix. What he is checking:
> feet planted rather than skating, a cohort that set off together no longer marching in step, and
> bags in hands.
>
> Re-run `Noir/Check The Animations`: `animations-downloaded.md` goes from "87 wired, 0 unused" to
> "86 wired, 1 unused" with Running under unused. Stage it deliberately — **never `git add -A`**.
>
> **No animator rebuild** unless a row names a clip that is in no row today. `Townsfolk.controller`
> is a tracked binary asset and W1 already spent the one rebuild this plan is entitled to.

---

### W6 — The player build

Last of the code waves, because it is the only one needing the editor **open** for two steps (PB-2's
prefab variant, PB-6's cast generator) and **closed** for everything else, and because PB-7
restructures the loading path RIG-7 and RIG-10 sit inside. ~25 min machine plus two editor steps.

| | |
|---|---|
| **PB-1** | The crowd already knows it drew zero rigged people — it says it in a whisper |
| **PB-2** | Pressing P in a shipped build does nothing; this one is genuinely fixable today |
| **PB-3** | `BuildPlayer` ships only top-level `Content` — no audio, no surface textures |
| **PB-4** | The packed-asset dump (**part b only**) |
| **PB-6** | *If decision 8 says (B) or (C):* a townsfolk cast manifest in Resources |
| **PB-7** | *If decision 8 says (B) or (C):* de-guard `AgentBody` onto the manifest |

**PB-1 must split on `Application.isEditor`.** On a fresh clone with no polyperfect pack — which the
plan itself calls the project's normal state — the warning would otherwise fire *in* the editor
while printing "outside the editor this is expected". A warning that confidently misattributes is
worse than the `Debug.Log` it replaces.

**PB-2 as proposed throws, and compiles cleanly doing it.** `var body = Object.Instantiate(prefab);`
declares a new local while the `_body` field stays null, so `Player.cs:155` NullReferences on the
first press of P and two PlayMode tests go red. **Assign the field.** An unused local is a warning,
not an error. Also drop the stated reason for not moving StarterAssets — Unity scene references are
by GUID and survive an in-project move; the real reason (a vendored package folder a re-import would
fight) is sufficient on its own.

**PB-3 is the highest-risk item in the plan and the risk is privacy, not technique.** `private` has
sat in `NeverShip` doing nothing because `Directory.GetFiles` returns no directories. Recursion makes
that entry **load-bearing for the first time**. Match `NeverShip` against every path **segment**, not
just the file name.

**PB-7 must open `Build` with `var cast = Cast(); if (cast == null) return null;`.** As written it
throws a NullReference once per citizen from inside the build loop — no figures array, no primitives
fallback, **no people at all, in the editor too**. A missing manifest must give you capsule people,
which is what it gives you today.

**PB-6's determinism trap:** `AgentBody` picks a figure by `Mix(seed) % set.Count`, so **array order
decides who looks like whom**. `Cast()` sorts Ordinal by asset path. The generated manifest must
preserve that ordering exactly, or the same seed draws a different-looking Rossville in a build than
in the editor — silently, with no test able to see it.

> **Gates.** PlayMode 13 of 13 (PB-2 touches `Player.cs`, which three PlayMode tests assert on).
> A Windows64 build exits 0.
>
> **PRIVACY GATE, before anything else touches the artifact:** `Test-Path
> Build\Windows64\Content\private` must be **False**. Do not push or share a build made with a
> half-applied PB-3.
>
> **AUDIO: mute the Windows mixer before the first post-PB-3 player run.** `Content/audio` has never
> shipped, so this is the first build that can blare the village ambience and the church bell into
> his headphones. Put that sentence in the commit message.
>
> `Content/audio` and `Content/textures` now exist beside the exe. The player's own log names a
> nonzero rigged count, or says why in a sentence true on the machine that printed it.

---

### W7 — The documents that are then true, and the long run

| | |
|---|---|
| **PB-10** | `CLAUDE.md`'s editor-only paragraph — the corrected version, landed once |
| **DOC-11** | One row in the load-bearing facts table for the animation estate |
| **RIG-9** | Loop Pose, *only if RIG-8 measured a seam worth 87 re-imports* |
| **GATE-7** | Verification (the code landed in W4) |

`CLAUDE.md`'s editor-only paragraph can only be written once, and only honestly after W6 has changed
what it says. **Use PB-10's version, not DOC-10's** — it is the superset and it was attacked and
corrected. Apply the correction: *"only `CityDriveways` says out loud what is missing"* is **false**;
`PolyPackCottageBuilder.cs:154` also speaks. Shipping a fourth wrong sentence into the paragraph
whose purpose is deleting three wrong sentences is the exact failure the item exists to prevent.

**GATE-7's verification needs a run that spans categories, and `LayerProof` alone carries a 30-minute
timeout.** Run it **once**, targeted: `-testFilter Noir.PlayTests.LayerProof`. Do **not** run the
suite unfiltered — it can take four hours and has never been seen to finish.

> **Gates.** Layer preferences byte-identical before and after the targeted run. `CLAUDE.md`'s
> baselines match what the suites actually print. No other document restates a number that lives in
> `CLAUDE.md`. **Push.**

---

## Hot files: where two items collide

| File | Items | Order that avoids rework |
|---|---|---|
| `AgentAnimation.Drive` :193-220 | DOT-4, DOT-5, ROW-4, ROW-2, RIG-5, RIG-2, SIM-2 | **The hottest hunk in the plan** — five clusters in one 27-line method. GATE-1 first (W3), then one edit pass in W4: RIG-5 → DOT-4 → ROW-4 → DOT-5 → RIG-2. ROW-2's and SIM-2's signature changes land together in W5. |
| `AgentMeshView.Report` :292-376 | GATE-2, GATE-4, DOT-7, ROW-5, SIM-2, SIM-8, SIM-9 | Seven items add fields to one struct. **GATE-2 first** — GATE-4 depends on its `Away` field and wrongly declares no blocker. One rewrite, one sitting, W4. |
| `Content/animations.txt:105` | DOT-1, ROW-7 | Direct line collision. DOT-1 in W1 (it is the bug fix and it is pushable today); ROW-7 in W5 anchored on the **new text**, never the line number. |
| `CLAUDE.md` :227-234 | PB-10, DOC-10 | **Drop DOC-10.** PB-10 is the superset and was corrected. Once, in W7. |
| `CLAUDE.md` test table | DOT-3, GATE-1, SIM-2 | Three waves move the same number: 428 → 431 → 439 → ~449. Each wave edits the table in **its own commit**. |
| `AgentBody.cs` | RIG-1, RIG-4, RIG-7, RIG-10, RIG-11, SIM-8, PB-7 | The five RIG edits are disjoint — one commit in W4. **PB-7 lands last, in W6**, because it deletes the chain RIG-7 and RIG-10 sit beside. |
| `AnimationCheck.cs` | GATE-9, DOC-5, DOC-9, ROW-6 | DOC-5 → DOC-9 → GATE-9 in W2; ROW-6 in W5, because it measures a fall-through W5's row changes alter. |
| `AnimatorBuild.cs` | DOT-2, DOC-6, DOC-9 | DOT-2 in W1 first, so the rewritten header describes a guard that exists. |
| `docs/ASSETS.md` | DOC-1, DOC-2, DOC-3, ROW-8 | All four in W2, one session. Anchor every `Edit` on exact text, never on audit line numbers — each edit shifts the ones below it. |

**Duplicates to delete rather than discover in a merge:** GATE-1 and SIM-2 both move the table into
Core — GATE-1 does the pure move (W3), SIM-2 adds only the lookup on top (W5). RIG-5 and SIM-11 are
the same fix; RIG-5 is fuller. DOT-3 test 1 and GATE-5 are the same assertion in two gates; keep the
2-minute one.

---

## Expect these to go red

Red here is the plan working. Do not "fix" the test.

- **DOT-3's three Core tests are red on today's tree by design**, with exactly one named offender
  each. Write them, watch them fail for the right reason, then apply DOT-1 in the same commit or the
  immediately following one. Do not leave the branch with a red Core gate between commits.
- **DOT-2 makes `AnimatorBuild` exit 1** until DOT-1 lands. That is the guard proving itself.
- **GATE-3 will go red on its first run** — see W4. Two of the three logs on disk already show the
  town frozen and passing.
- **ROW-2 breaks the compile** at two call sites that pass `hurrying:`. Expected, inside one commit.
  Fix the call sites; do not add an overload to keep it compiling.
- **RIG-1 changes every image in `docs/snapshots`** — Tour and FilmStrip run at 3x and 4x, so people
  animate at a visibly different apparent rate. Do not stage them, and do not read it as damage.
- **ROW-5's proof is a disappearance:** `Sad Idle=` appears in every `[body]` line of all three
  recorded runs and must be absent from every line afterwards.
- **The 2:1 ratchet must not move.** Nothing in W4 or W6 touches Core, so `TheGapToTheRuleIsReported`
  must print 0.89 : 1 and 0.33 : 1 identically. If those move in a wave that touches no Core file,
  something else in the wave moved them — and *that* is the finding.

---

## Do not do

- **Drop DOT-6** (`AnimationImport` rejecting a period). DOT-3 test 3 is the identical assertion,
  runs unattended in the 2-minute gate, needs no Unity, and cannot be skipped — and DOT-2 already
  refuses to build a lying controller. DOT-6 buys "you see it thirty seconds sooner if you happen to
  be watching the console", at the price of being a third home for one fact and a `LogError` that
  stops nothing, firing on every Library rebuild until DOT-1 lands.
- **Drop GATE-5.** Not because it is wrong — because DOT-3 test 1 asserts the same set two minutes
  into the Core gate instead of six minutes into PlayMode, and in a batch run the two cannot differ.
- **Drop SIM-11 and DOC-10** — RIG-5 and PB-10 are the same fixes, done better.
- **Do not do PB-4 part (a)**, the hand-maintained string-path table. It prints the same warning on
  every build forever about three known, permanently-true facts; the plan itself concedes it will
  drift; and it structurally cannot detect a *fourth* string-path asset. A warning that fires every
  time is a warning that gets skimmed — the exact failure PB-1 exists to fix.
- **Do not "fix" `AgentMeshView.cs:349`.** It hashes the same dotted name `Drive` does and looks like
  a second instance of the bug. It is not: that comparison only runs for people the simulation says
  are moving, and a moving person is always on a row that names no dotted clip. It comes right for
  free when DOT-1 lands.
- **Honour the leave-its.** Do not teach `AnimationCheck` to open the controller (DOT-9). Do not make
  `Drive` fall back to another clip when a state is missing (DOT-11) — freeze, do not substitute; a
  plausible-looking answer to a broken question is exactly what let this bug live. Do not download a
  sleeping clip (ROW-9) — the row is deliberately empty until furniture reservation exists. Do not
  wire `AnimationCheck` into CI (GATE-11). Do not build an on-screen degradation panel (PB-8). Do not
  give the primitive figure an Animator (PB-9). Do not buy a rigged elder with a bone rotation
  (SIM-10) — **it would silently do nothing**, because `Refresh` runs in `Update` and the Animator
  writes bones after it; buy it with a clip and a row. Do not change the loop-everything import
  behaviour (DOC-12).
- **Process.** Do not run Core in Debug (8–9 min, unstable on this CPU). Do not pass `-nographics`.
  Do not rename the FBX with the editor open. Do not run `Build The Townsfolk Animator` for
  cosmetics. Do not edit `Content/` files with PowerShell `Get-Content`/`Set-Content`. Do not
  `git add -A`. **Do not turn `AgentAnimation.Mix()` into an `IRng` substream** — it is a local hash
  over the citizen key and changing it reshuffles every crowd in the project.
- **Do not re-derive what is on disk.** `play.xml`, `play2.xml` and `play3.xml` are untracked in the
  repo root with the full `[body]` census lines from three past runs. They already prove `hurrying`
  is dead and that the default row is being resolved for undrawn commuters. **Grep them before
  spending ten minutes on a Unity run** — they are also the only baseline to diff against, so do not
  let a cleanup pass delete them.

---

## Done means

- Core is green and its total matches `CLAUDE.md`'s table, whatever that number then is.
  `TheGapToTheRuleIsReported` still prints 0.89 : 1 and 0.33 : 1; both ratchet tests green.
- Both assemblies compile; `check-editor-only.py` exits 0.
- PlayMode `!Diagnostic` is 13 of 13, and the `[body]` census line carries **all** of: no `Sad Idle=`,
  no `Running=`, an out-of-town count near 200 at noon, an unstated-clip count of **0**, and a
  bone-probe line saying at least half the sampled animators moved their hips more than 1 mm over 40
  frames. *That last line is the one that matters most — it is the assertion that would have caught a
  gate silently red for months.*
- `AnimatorBuild.Run` exits 0, `AnimationCheck.Run` exits 0 and now exits non-zero when clips are
  missing, and **no file under `Assets/Noir/Animations` has a period in its name.**
- A Windows64 build succeeds; `Build\Windows64\Content\private` does **not** exist; `Content\audio`
  and `Content\textures` **do** exist beside the exe; the player's log names a nonzero rigged count
  or says why not in a sentence true on the machine that printed it. **Pressing P in that exe moves a
  rigged body, not nothing.**
- A targeted `LayerProof` run completes and layer preferences are identical before and after. No test
  in the suite can change what a later test measures.
- `CLAUDE.md`'s editor-only paragraph, its baselines and its animation facts all describe what is
  then true. Every number that moved moved in the same commit as the change that moved it.
- **The owner has pressed Play once at 1x** and said, in his own words, that the walk looks planted
  rather than skating and that a group setting off together is not marching in step. No test and no
  offline render can substitute for this.
- The branch is pushed.
