# Lessons learned — the night of 2026-08-04

Written the morning after a long session that fixed a freeze nobody could explain, and got
six diagnoses wrong on the way to the right one. Kept because the *mistakes* generalise
better than the fixes do, and every one of them cost real time.

---

## 1. A number is only evidence about the thing it measures

The stutter was chased through six confident wrong answers: shader compilation, CityTraffic,
`runInBackground`, `EditorLoop`, a "5 ms per simulation tick constant", and the tick budget
built on top of it. Each was announced as the cause. Each was wrong.

**The worst was the one that looked most like science.** Every stall showed frame time ÷
ticks = 4.97, 5.00, 5.01, 5.00, 5.00, 5.00 — a physical constant, apparently. It was long
division. The accumulator *defines* `ticks = dt × speed × 20`, which at 10× is `dt × 200`,
and the frame time was `dt × 1000`. The ratio is 1000 ÷ 200 = 5.00 for **any** stall from
**any** cause on **any** machine. A tautology in a lab coat.

> **The test:** before believing a ratio, ask what would make it come out differently. If
> nothing could, it is arithmetic, not a measurement.

Related: `Recorder.Get` sums a marker across every thread that hit it, so
`Semaphore.WaitForSignal` — four idle job workers parked for the frame — reported four to
five times the frame's wall clock and won every stall in three consecutive runs while
meaning nothing. A "biggest value" instrument structurally favours whichever thing appears
on the most threads.

---

## 2. Constants rot when the thing they were sized against moves

**This happened four separate times in one night**, in four unrelated files:

| constant | right when written | broken by | symptom |
|---|---|---|---|
| `Pathfinder` node cap = `_count / 4` | Ashcombe, 5,100 nodes | Rossville is 5,040,000 tiles → **1,260,000** nodes | 415 ms freeze |
| `HeuristicWeight = 1.35` | synthetic grids costing 1.0/tile | real terrain costs 1.3 → effective weight **1.038** | A* ran near-Dijkstra |
| `CityOutlines.Lift = 0.06` | flat ground | elevation kept → hills ate the lines | broken lot lines |
| `SelectionHighlight.Lift = 0.09` | when outlines were 0.06 | outlines went to 0.25 **the same evening** | selection drawn *under* the thing it traces |

None produced an error. All four were silent.

> **The rule:** a constant sized against another number should say so *and* name it. "0.09
> clears CityOutlines' 0.06" is a comment that would have caught the fourth one — it was
> there, and still nobody moved it when 0.06 changed. Better: derive it. `Lift =
> CityOutlines.Lift + 0.11f` cannot rot.

---

## 3. A guard that is checked before the work cannot bound the work

`PathBudgetPerTick` and `PathNodeBudgetPerTick` were both consulted by `CanAffordAPath()`
**before** a search started and never once it was running. So a single A* search spent
twenty-one times the entire tick's node budget on its own, and both budgets reported
themselves satisfied throughout.

Equally: `MaxTicksPerFrame = 24000` could not bound a frame, because the *cost per tick* was
the thing varying. **A count cannot bound a duration.** Only time bounds time.

---

## 4. Verify the side effect, not the call

Three separate times a thing was reported as running when it was not:

- A probe was "started" — but `PerfProbe.Begin()` deletes its report file as its first act,
  and the file was still sitting there untouched. It had never run.
- `Unity_RunCommand` returned "Execution failed" and was assumed to have worked anyway.
- "Marker consumed" was treated as "probe launched". It meant only that a domain reload had
  happened; the callback registered by that reload had been thrown away by the *next* one.

> **The rule:** find the observable the action would have changed, and look at *that*. Not
> the return value, not the log line, not the absence of an error.

---

## 5. The instrument gets the same scrutiny as the subject

- The first every-pair fixture for `WalkableRegions` left every pair reachable, so it proved
  only the easy half. It passed. The guard assertion — "this fixture is supposed to contain
  unreachable pairs" — is what caught it. **Write the guard that refuses a vacuous pass.**
- The perf probe printed the *largest* marker, which structurally favoured the multi-thread
  one. Printing every marker with its value turned three wasted runs into one answer.
- `ms` came from `unscaledDeltaTime` (the frame *before*) and was printed beside costs from
  the frame *after*, putting cause one row from effect. It made the simulation look innocent
  — 840 ms of work printed against a 426 ms frame — when it was the culprit one row down.

---

## 6. Fix the fault, then check you have not moved it

Every one of these was a fix that created the next problem:

- Node ceiling 1,260,000 → **40,000**: stalls gone, and **73.5% of all journeys refused**.
  Bounding a cost by deleting the work is not bounding a cost.
- Keyboard guard added so typing would not fly the camera: correct, and it had no indicator
  and one undiscoverable exit. Reported as *"I did not know, it never told me, and then I
  could not click it any more like it locked it."* **A mode with no sign saying it is a mode
  is a bug however well it works.**
- The structured household replaced `Adults`/`Kids`/`Names` — and `DraftIsAnything()` still
  checked the old counters, which are now derived and always zero. A household typed on a
  fresh lot read as "no changes" and was silently binned on the next click: **the exact bug
  that had been fixed for the old fields, reintroduced by the thing that replaced them.**

> **The rule:** when replacing a field, grep every reader of the old one. The dirty check,
> the emptiness test and the serialiser are three separate readers and they all matter.

---

## 7. Caches that claim things they cannot know

`HoverHighlight` rebuilt only when the lot id changed. That asserts *the drawn state matches
the id* — which nothing in the class could guarantee, and it skipped every other lot the
cursor crossed. The cache guarded four hundred vertices.

> **The rule:** a cache keyed on "the input has not changed" is asserting the *output* is
> still correct. If anything outside can touch the output, that assertion is false. Price
> the work before defending it — four hundred vertices a frame is beneath measurement.

---

## 8. Data hygiene has to be a mechanism, not a promise

`Content/parcel-notes.txt` is written by the game's own editor, had just gained name fields,
was **tracked**, and the remote is **public GitHub**. Nothing had leaked yet — the committed
copies were checked and hold only zoning and quality — but one push would have done it.

> **The rule:** when a rule is about where data may go, enforce it in `.gitignore`, not in a
> comment and not in a memory. Also: check `git ls-files` before assuming a Content file is
> local, and check `git log -- <path>` before assuming nothing has already gone out.

---

## 9. Working practice, specific to this project

- **Unity only rescans scripts on focus GAIN.** Already-focused plus an edit means nothing
  happens, forever. Park focus elsewhere, then return. `WScript.Shell.AppActivate` works
  where `SetForegroundWindow` is refused.
- **Unity will not compile while in Play.** Ctrl+R is a no-op there and Ctrl+P is not — a
  silent editor is often just a running one.
- **`Unity_RunCommand` is broken in this project**, not wedged: it fails identically on a
  fresh instance. `PerfProbeAutostart` (marker file + domain reload) is the way in.
- **Do not script C# edits through Python heredocs.** Escape sequences were mangled three
  times tonight — `\n` became real newlines inside string literals, and `'\\'` came out as
  `'\'`. Use the editing tool for anything containing a backslash.
- **Two by-design failures are the baseline**: `TwoToOneTests.TheMedianVillagerYields…` and
  `…TheTenthPercentileIsNotALock`. 341 passed / 2 failed is green. Anything else is a
  regression.

---

## 10. What the owner was right about, first time

Worth recording separately, because in each case the code said otherwise and the code was
wrong:

- *"tracks is all fucked up looking around the elevator"* — centring each vertex
  independently had produced a 17° kink. Measured position; never checked shape.
- *"it only highlights every other one it passes"* — a cache bug that reading the code twice
  had not found.
- *"When clicking I should make more clear"* — the selection mark was drawing underneath the
  lot lines.
- *"maybe research is wrong?"* — no; the wording was. The town has a bank, a hardware, a
  grocer and a barber. They are named as **1913 trades** — a millinery, a confectionery, a
  general merchandise — which is a different problem entirely, and the real one.
