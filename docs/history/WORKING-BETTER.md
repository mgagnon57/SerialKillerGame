# How to work better on this project

Companion to `POSTMORTEM-2026-08-03-ROADS.md`. That one says what went wrong; this one says what to
do instead. **Both halves — the assistant's and the owner's.**

---

## For the assistant

### 1. Verify in the running scene, not in the diff

The rule that would have prevented the worst of it:

> **Never say a change landed until it has been measured in the live scene.**

Not "the code looks right". Not "the file changed". Not "the edit-mode probe agrees". The Unity MCP
connection exists for exactly this and was installed for exactly this reason. It was not used, and
the owner pressed Play twice to look at a town that had not changed.

The sequence that works:

```
edit  →  exit play  →  RequestScriptCompilation  →  wait for the compile
      →  enter play →  wait for the build
      →  MEASURE the thing that was supposed to change
      →  only now say something
```

A measurement means a number that would be different if the change had not worked. "1111 alley
tiles, median 4.0 m, was 7.1" is a measurement. "It should be narrower now" is not.

### 2. Look at the picture before reasoning about the numbers

The straight-down screenshot with the parcel lines on settled in one image what a day of
measurement had failed to settle. `look-at-it-before-saying-it-works` is already a standing rule in
this project and it was applied to the wrong artefact — the code was read carefully and the town was
not looked at.

For anything spatial, render the view **first**, then measure to confirm what the eye already found.

### 3. Sample windows are part of the hypothesis, not a detail

The two-day error was a cross-section taken ±14 m around a road whose answer lay 17 m away. The
sample looked rigorous and was framed to exclude the truth.

Before generalising from a scan: **widen the window until the answer changes, or until it is clear
the window is not the limit.** If a measurement says "there is nothing here", the first question is
whether the instrument could have seen it.

### 3a. Measure the property that was wrong, AND the one you might break

Added 2026-08-04, after the railroad. The track was off its right of way, so the measurement was
*position* — and the fix scored perfectly on it: median 0.5 m off centre, 15 m of clearance a side.
It also snaked visibly, because moving each vertex onto the corridor centre by a different amount
put a 17° kink between two vertices 21 m apart, and the drawing spline amplified it.

**Position and shape are two different measurements, and a geometry fix trades one against the
other.** The numbers were all true. Nobody measured the second thing, so the owner found it by
looking at the screen — again.

For a line, shape means the **signed** heading change per vertex. Unsigned hides the fault: a
steady 5° per vertex is a curve, alternating ±5° is a snake. Count direction flips, not degrees.

Generally: before reporting a fix, ask what property the change could have degraded, and measure
that one too. A fix that is right on its own metric and wrong on the neighbouring one is the
easiest kind to ship by accident.

### 4. When every candidate scores the same, the test is broken

Already written in `osm-tiger-data-is-not-a-survey` for the case where everything *agrees*. It was
hit again with the sign flipped — 40 of 41 roads "overrunning lots" — and not recognised, because
the memory records the shape and not the symmetry.

**Either extreme is the same fault.** All-pass and all-fail are both instrument failures until
proven otherwise.

### 5. Find every call site before claiming a fix

`CityStreets` had two tile-placement paths. One was patched, the fix was reported, and the path
every straight road uses was untouched. A `grep` for the function's siblings would have taken
fifteen seconds.

**Before reporting: search for other callers, other paths, other overloads of whatever was changed.**

### 6. Do not ship an editor-behaviour change that cannot be demonstrated

Three attempts at a play-mode freshness guard, all broken, one of which blocked Play entirely. The
removal was correct. The two hours were not, and the right call was to stop after the first failed
design and write down the procedure instead.

**Tooling that changes how the editor behaves needs a demonstration, not an argument.** If it cannot
be demonstrated in one attempt, the manual procedure wins.

### 7. Name things so they cannot collide

A Core enum called `Light` blocked play mode because `UnityEngine.Light` exists. `Terrain` bit the
same way an hour later. The netstandard gate cannot see either collision because it has no
UnityEngine.

**Avoid Core type names UnityEngine also uses:** `Light`, `Terrain`, `Object`, `Random`, `Debug`,
`Material`, `Space`, `Bounds`, `Color`, `Camera`, `Input`.

### 8. Finish one thing

The alley width was chased three times across a fragmented afternoon. Some of that fragmentation was
external (§ below) but the response to it was mine: each interruption was taken immediately rather
than queued.

**Answer the interrupt in one line, put it on a list, finish the diagnosis.**

---

## For the owner

Requested explicitly, and offered in the same spirit.

### 1. One source ruling, and let it stand until the work is done

Across this problem the guidance was *"OSM should not be trusted, it has been wrong on almost
everything"*, then *"if you can confirm OSM is right it can be used"*, then the parcels were treated
as decisive, then dismissed, then decisive again. Every individual call was defensible. The set of
them meant there was never a fixed thing to measure against.

**A ruling like "the county parcels are the truth for where public land is; OSM is a hint" would
have ended this on day one.** Give one, and let it stand until the job closes — corrections are
cheap, mid-flight reversals are not.

### 2. The screenshot is the highest-value thing you send

*"Look at my clipboard"* and *"just look at the white boundaries"* each moved the work further than
any amount of measurement. A picture of the wrong thing, with the overlay on, is worth an hour of
description.

**Send it early.** It is not a last resort.

### 3. Interruptions are expensive mid-diagnosis

Six requests arrived while the alley diagnosis was open — vehicles, the inspector pop-out, trees,
the camera, the parcel overlay, the roads. Every one was reasonable and four were quick.
Collectively they are what let a half-applied fix be reported as complete.

**If a diagnosis is running, batching the small stuff until it closes will get the big thing fixed
faster.** Or say "drop that, do this instead" — an explicit switch is much cheaper than an implicit
one.

### 4. "It has not changed" is the most useful sentence available

It was said several times and it was right every time. It is worth saying immediately and bluntly,
because it is the only signal that reliably distinguishes "the code is wrong" from "the code never
ran".

### 5. Local knowledge beats the data, but say which part

*"Route 1 is not straight"* was correct against the OSM tags and settled that argument. The lesson
drawn from it — trust the owner over the data — then contributed to the parcels being dismissed.

**When correcting from local knowledge, saying which specific claim is wrong** — "the road's shape
is wrong, the lot lines are fine" — keeps the correction from being generalised into "the data is
unreliable".

---

## The standing agreement, short enough to actually follow

1. **Nothing is "fixed" until it is measured in the running scene.** A number, not a claim.
2. **Render the view before reasoning about it**, for anything spatial.
3. **One agreed source of truth per problem**, held until the problem closes.
4. **Interrupts get one line and a list entry**, not an immediate context switch.
5. **All-pass and all-fail both mean the instrument is broken.**
6. **Screenshots early**, from the owner, with the relevant overlay on.

---

## The immediate next job, so it is not lost

From the postmortem, § 6: the block layout has to be derived from the parcel geometry — fit road
centrelines to the parcel-free strips, then re-seat the lot rows on the parcels, then re-measure
building overlaps. Steps in that order, all four together.

Step 1 is already done: `scratchpad/strips.txt` has the strip position and width for all 41 roads.

**And do not open it by adjusting a width.** Width was never the problem.
