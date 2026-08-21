# Angled Frontage (Doors, Signs, Shutters) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Frontage.cs` (doors, signs, shutters) build its geometry at a shaped building's true wall
angle instead of always snapping to the nearest compass direction, so a door frame actually plugs the
angled hole `DrawShapedPerimeters` cuts for it.

**Architecture:** `Frontage.Build` walks every `Place` and, per doorway/sign/shutter, calls `FrontAt` to
get a `Front` — a little local coordinate frame (`Along` the wall, `Out` through it, plus a span) that
every piece-builder (`Piece`, `Fascia`, `Hanging`, `Plate`, `Notice`, `Boarding`, `Gates`) reads off of.
Today `FrontAt` only ever returns one of the four world axes, and every piece is an UNROTATED
axis-aligned box whose "which world axis is long" is baked into `Front.Size()`'s `AlongX` ternary — this
works only because `Along`/`Out` are always exactly `Vector3.right/left/forward/back`. This plan:

1. Generalises `Front`'s math (a `Rotation` quaternion, `Along`/`Out` as genuinely arbitrary
   perpendicular unit vectors, `Lo`/`Hi` computed by projecting the wall's own corners onto `Along`
   instead of reading `b.X`/`b.Y` directly) — proven to reproduce today's behaviour exactly for all four
   cardinal cases before anything shaped-building-specific is touched.
2. Makes every `Piece()` call rotate by `Front.Rotation`, and removes the now-redundant `AlongX`
   special-casing from `Doorway`'s hinge/leaf placement.
3. Teaches `FrontAt` to derive `Along`/`Out` from the nearest edge of `place.OutlinePrecise` (the same
   "closest edge to the door" search `DrawShapedPerimeters` already does) when a precise ring is
   available, instead of always falling back to the nearest compass direction.

**Tech Stack:** C# / Unity 6000.3.20f1. This is `Assets/Noir/Unity` code — no `UnityEngine` reference
exists under `Assets/Noir/Core`, so **none of this is Core-testable**; every verification step in this
plan is a live render taken through `Unity_RunCommand`, matching this file's own existing convention
(see its class header: "Prototype, not shipping code" / the offline-renderer discussion) and the
precise-shaped-corners plan this one follows on from.

**Spec:** No separate spec document. Diagnosed directly in conversation with the owner on 2026-08-13,
after the precise-shaped-corners plan's wall-direction fix shipped and the owner reported the gap was
still there. Root-caused by elimination: mesh data at the party-wall seam was verified identical
between neighbouring units (corners coincide exactly, front-edge direction identical, and after this
session's separate ground-height fix, the seam's Y-values match exactly too) and turning fog off/on
changed the colour of the gap but not its presence, which pointed at "you are seeing through a hole",
confirmed by raycasting straight through one of the pale patches and hitting a stray, unrelated prop
instead of any part of the terrace's own wall. The hole is the door cut `DrawShapedPerimeters` makes
(a full-storey-height gap, by design — see that method's own `Slab` calls); what should fill it is
`Frontage.Doorway`'s door frame, and `Frontage.FrontAt` (Assets/Noir/Unity/Frontage.cs:265) proves to
only ever return a compass direction, regardless of the wall's true angle.

## Global Constraints

- **Scope is `Frontage.cs` only.** Nothing in `DrawShapedPerimeters`, `DowntownFromSanborn`, or
  `Place`/`PlaceSpec` changes in this plan — the wall geometry and the door-hole cut are already
  correct (precise-shaped-corners plan) and already tested. This plan only fixes what fills the hole.
- **Zero visual change for every ordinary (axis-aligned) building.** This is the load-bearing
  constraint: `Frontage.Build` runs for every door, sign and shutter in the whole town (hundreds of
  them), and only the ~17 terrace units at 112 S Chicago have `OutlinePrecise` set today. Task 1 and
  Task 2 must each end with a live before/after render of at least two ordinary buildings' frontages,
  pixel-diffed, before touching anything shaped-building-specific in Task 3.
- **`Piece()` currently sets `position` and `localScale` and never touches `rotation`** (Assets/Noir/Unity/Frontage.cs:727-742) — confirmed by reading it. Every box it builds is world-axis-aligned today. Adding rotation is not optional for an angled wall: a scaled, unrotated cube cannot represent a box tilted off the grid, this is geometrically necessary, not a math trick to avoid.
- **The rotation formula is pre-verified, not derived on paper.** Run live in the editor via
  `Unity_RunCommand` against the actual four cardinal cases AND the actual terrace angle before this
  plan was written:
  ```csharp
  float yaw = Mathf.Atan2(-along.z, along.x) * Mathf.Rad2Deg;
  var rot = Quaternion.Euler(0, yaw, 0);
  // verified: rot * Vector3.right == along, to within 0.001, for along = Vector3.right,
  // Vector3.forward, and the terrace's actual (-0.30, 0, 0.95) — see Task 1 Step 1.
  ```
- **`Along` does NOT currently flip sign between a wall and its opposite wall.** `FrontAt`
  (Assets/Noir/Unity/Frontage.cs:265-284) sets `along = acrossX ? Vector3.right : Vector3.forward` —
  always one of exactly two values, never negated, even though the "back" wall and the "right" wall
  face the opposite way from the "forward"/"left" walls that share their `along`. `side = alongX ?
  Sign(f.Out.z) : -Sign(f.Out.x)` (Assets/Noir/Unity/Frontage.cs:358) exists specifically to compensate
  for that asymmetry when choosing which way a door swings. Task 1 makes `Along` a fixed rotation of
  `Out` (`Along = new Vector3(Out.z, 0f, -Out.x)`), which DOES flip sign for those same two cases — so
  the swing-side compensation must be re-derived, not left as-is. This is exactly the kind of thing a
  before/after render on ordinary buildings will catch if got wrong.

---

### Task 1: Generalise `Front`'s coordinate math, cardinal-only, byte-for-byte compatible

**Files:**
- Modify: `Assets/Noir/Unity/Frontage.cs` (`Front` struct, `FrontAt`)

**Interfaces:**
- Produces: `Front.Rotation` (new `Quaternion` field). `Front.Along`/`Front.Out` remain `Vector3`, but
  `Along` is now always `Out` rotated so that `Rotation * Vector3.right == Along` and
  `Rotation * Vector3.forward == Out`. `Front.Lo`/`Front.Hi` are now computed by projecting the wall's
  four bounding corners onto `Along` rather than reading `b.X`/`b.Y` by axis.
- Consumed by: Task 2 (`Piece`, `Doorway`'s hinge/leaf code), Task 3 (the new precise-edge branch of
  `FrontAt` reuses the same `Front` constructor).

- [ ] **Step 1: Confirm the rotation formula live, exactly as this plan states it**

Editor must be open (per CLAUDE.md's standing precondition — check for `Unity.exe` first). Run via
`Unity_RunCommand`:

```csharp
using UnityEngine;
using System.Text;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var sb = new StringBuilder();
        Vector3[] alongs = { Vector3.right, Vector3.forward, new Vector3(-0.30f, 0f, 0.95f).normalized };
        foreach (var along in alongs)
        {
            float yaw = Mathf.Atan2(-along.z, along.x) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(0, yaw, 0);
            var got = rot * Vector3.right;
            sb.AppendLine($"along={along} yaw={yaw:F2} rot*right={got} match={Vector3.Distance(got, along) < 0.001f}");
        }
        result.Log(sb.ToString());
    }
}
```

Expected: `match=True` for all three lines. If any is `False`, STOP — the formula in this plan is
wrong and every later step is built on it. Do not proceed past this step without three `True`s.

- [ ] **Step 2: Replace `Front`'s internals**

In `Assets/Noir/Unity/Frontage.cs`, find the `Front` struct (currently lines 222-263):

```csharp
        private readonly struct Front
        {
            public readonly bool Valid;
            public readonly Vector3 Face;
            public readonly Vector3 Out;
            public readonly Vector3 Along;
            public readonly float Lo, Hi;

            public Front(Vector3 face, Vector3 outward, Vector3 along, float lo, float hi)
            {
                Valid = true;
                Face = face;
                Out = outward;
                Along = along;
                Lo = lo;
                Hi = hi;
            }

            private bool AlongX => Along.x > 0.5f;

            public float Span => Hi - Lo;
            public float FaceAlong => AlongX ? Face.x : Face.z;

            /// <summary>A point on this frontage: where along the wall, how high, and how far out.</summary>
            public Vector3 At(float along, float up, float outward) =>
                Face + Along * (along - FaceAlong) + Vector3.up * up + Out * outward;

            /// <summary>Straight over the threshold, at a height and a standoff.</summary>
            public Vector3 On(float up, float outward) => At(FaceAlong, up, outward);

            /// <summary>A box size in the same terms: along the wall, up it, and through it.</summary>
            public Vector3 Size(float along, float up, float through) =>
                new Vector3(AlongX ? along : through, up, AlongX ? through : along);

            /// <summary>Trim a board so it cannot overhang the end of the building it is nailed to.</summary>
            public float Fit(float width) => Mathf.Min(width, Mathf.Max(1.2f, Span - 1.2f));

            /// <summary>Centred over the door, then slid along until it fits on the frontage.</summary>
            public float Centred(float width) => Span <= width
                ? (Lo + Hi) * 0.5f
                : Mathf.Clamp(FaceAlong, Lo + width * 0.5f, Hi - width * 0.5f);
        }
```

Replace with:

```csharp
        private readonly struct Front
        {
            public readonly bool Valid;
            public readonly Vector3 Face;
            public readonly Vector3 Out;
            public readonly Vector3 Along;
            public readonly Quaternion Rotation;
            public readonly float Lo, Hi;

            /// <summary>
            /// <paramref name="outward"/> is the only direction this needs - <see cref="Along"/> is
            /// always <paramref name="outward"/> rotated so a piece's local +X runs along the wall
            /// and local +Z points through it, which is what makes a single <see cref="Rotation"/>
            /// correct for every piece this frontage builds, cardinal or not.
            ///
            /// <paramref name="lo"/>/<paramref name="hi"/> must already be projections onto THIS
            /// Along (i.e. Vector3.Dot(worldPoint, Along) for the wall's two extreme corners) - see
            /// <see cref="FrontAt"/>, which is the only caller.
            /// </summary>
            public Front(Vector3 face, Vector3 outward, float lo, float hi)
            {
                Valid = true;
                Face = face;
                Out = outward;
                Along = new Vector3(outward.z, 0f, -outward.x);
                float yaw = Mathf.Atan2(-Along.z, Along.x) * Mathf.Rad2Deg;
                Rotation = Quaternion.Euler(0f, yaw, 0f);
                Lo = lo;
                Hi = hi;
            }

            public float Span => Hi - Lo;
            public float FaceAlong => Vector3.Dot(Face, Along);

            /// <summary>A point on this frontage: where along the wall, how high, and how far out.</summary>
            public Vector3 At(float along, float up, float outward) =>
                Face + Along * (along - FaceAlong) + Vector3.up * up + Out * outward;

            /// <summary>Straight over the threshold, at a height and a standoff.</summary>
            public Vector3 On(float up, float outward) => At(FaceAlong, up, outward);

            /// <summary>
            /// A box size, still in WORLD axes - <see cref="Piece"/> places every box at identity
            /// rotation, and applying <see cref="Rotation"/> there is Task 2's job, not landed yet.
            /// The swap this used to key off <c>Along.x &gt; 0.5f</c> - Along's sign is exactly what
            /// changed for the {right,back} walls (see the constructor), so that test would now
            /// silently swap the wrong walls. <see cref="Out"/> was never sign-ambiguous - it is
            /// still one of the four cardinal unit vectors here - so the swap is keyed on it instead.
            /// </summary>
            public Vector3 Size(float along, float up, float through)
            {
                bool alongX = Mathf.Abs(Out.z) > 0.5f;
                return new Vector3(alongX ? along : through, up, alongX ? through : along);
            }

            /// <summary>Trim a board so it cannot overhang the end of the building it is nailed to.</summary>
            public float Fit(float width) => Mathf.Min(width, Mathf.Max(1.2f, Span - 1.2f));

            /// <summary>Centred over the door, then slid along until it fits on the frontage.</summary>
            public float Centred(float width) => Span <= width
                ? (Lo + Hi) * 0.5f
                : Mathf.Clamp(FaceAlong, Lo + width * 0.5f, Hi - width * 0.5f);
        }
```

- [ ] **Step 3: Update `FrontAt` to build `Lo`/`Hi` by projection, not by axis**

Find (lines 265-284):

```csharp
        private static Front FrontAt(TileRect b, Tile door)
        {
            if (!door.IsValid) return default;

            // Same order the world builder uses when it decides which way to punch a doorway in,
            // so a door on a corner-ish tile is understood here exactly as it was carved there.
            Vector3 outward;
            if (door.X == b.X) outward = Vector3.left;
            else if (door.X == b.Right) outward = Vector3.right;
            else if (door.Y == b.Y) outward = Vector3.forward;    // grid rows count south, so row 0 faces +Z
            else if (door.Y == b.Bottom) outward = Vector3.back;
            else return default;

            bool acrossX = Mathf.Abs(outward.z) > 0.5f;
            var along = acrossX ? Vector3.right : Vector3.forward;
            float lo = acrossX ? b.X : -(b.Y + b.H);
            float hi = acrossX ? b.X + b.W : -b.Y;

            return new Front(Space3D.ToWorld(door) + outward * 0.5f, outward, along, lo, hi);
        }
```

Replace with:

```csharp
        private static Front FrontAt(TileRect b, Tile door)
        {
            if (!door.IsValid) return default;

            // Same order the world builder uses when it decides which way to punch a doorway in,
            // so a door on a corner-ish tile is understood here exactly as it was carved there.
            Vector3 outward;
            if (door.X == b.X) outward = Vector3.left;
            else if (door.X == b.Right) outward = Vector3.right;
            else if (door.Y == b.Y) outward = Vector3.forward;    // grid rows count south, so row 0 faces +Z
            else if (door.Y == b.Bottom) outward = Vector3.back;
            else return default;

            var f = FrontOf(outward, BoundsCorners(b));
            return new Front(Space3D.ToWorld(door) + outward * 0.5f, outward, f.Lo, f.Hi);
        }

        /// <summary>The four corners of an axis-aligned footprint, in world space.</summary>
        private static Vector3[] BoundsCorners(TileRect b) => new[]
        {
            Space3D.ToWorld(b.X, b.Y), Space3D.ToWorld(b.Right + 1, b.Y),
            Space3D.ToWorld(b.X, b.Bottom + 1), Space3D.ToWorld(b.Right + 1, b.Bottom + 1),
        };

        /// <summary>
        /// A Front for a wall facing <paramref name="outward"/>, spanning whichever of
        /// <paramref name="corners"/> project furthest apart along it. <paramref name="corners"/>
        /// only needs to contain the wall's own two ends for a shaped edge (Task 3) - the bounding
        /// box's four corners is what a cardinal wall uses, and projecting is what makes both work
        /// through the same formula. Face is set by the CALLER (from the door's own position, not
        /// from any of these corners) - this only ever supplies Lo/Hi.
        /// </summary>
        private static Front FrontOf(Vector3 outward, Vector3[] corners)
        {
            var along = new Vector3(outward.z, 0f, -outward.x);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var c in corners)
            {
                float t = Vector3.Dot(c, along);
                if (t < lo) lo = t;
                if (t > hi) hi = t;
            }
            return new Front(Vector3.zero, outward, lo, hi);
        }
```

- [ ] **Step 4: Build and check for compile errors**

Close the Unity editor first if it is open, or verify via `Unity_GetConsoleLogs` if driving it live
(this plan assumes the latter, per CLAUDE.md's "drive Unity yourself" rule). Trigger a recompile
(`AssetDatabase.Refresh()` via `Unity_RunCommand`, then exit/re-enter Play if the editor was mid-Play)
and confirm zero errors.

- [ ] **Step 5: Render two ordinary buildings' frontages before and after, pixel-compare**

Pick two real, currently-built houses (any two with a front door - the game log line
`Frontage: N front doors, ...` confirms the count). Before making Step 2-3's edit (i.e. on the
CURRENT, un-reverted tree), render each door area with a `Camera.Render()` + `ReadPixels` +
`EncodeToPNG` capture (same technique used throughout the precise-shaped-corners and this plan's own
diagnosis - see that plan's Task 4 for the exact pattern), saved to
`docs/snapshots/frontage-baseline-<name>.png`. After the edit and a recompile, render the SAME two
cameras again to `docs/snapshots/frontage-after-<name>.png`, and diff every pixel (not just eyeball
them - `Texture2D.GetPixel` compared per-pixel, same as this session's fog A/B test). Expected: byte
identical, or differing only by render nondeterminism you can show is present in two consecutive
renders of the UNCHANGED tree too (temporal AA, etc - render the baseline twice and confirm it is not
already jittering before blaming this change for any diff found).

If they differ beyond that noise floor: STOP. Do not proceed to Task 2. The formula or the projection
in Step 2-3 is wrong for at least one of the four cardinal cases and needs to be found before anything
else changes.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Unity/Frontage.cs
git commit -m "Front computes a rotation and projects Lo/Hi instead of reading axes, unchanged for cardinal walls"
```

---

### Task 2: Rotate every piece, and simplify the hinge/leaf placement that compensated for not rotating

**Files:**
- Modify: `Assets/Noir/Unity/Frontage.cs` (`Piece`, `Doorway`)

**Interfaces:**
- Consumes: `Front.Rotation`.
- Produces: `Front.Size` returning unrotated local dimensions (flattened here, not in Task 1 - see
  Step 1's own note on why). `Piece(parent, name, position, size, material, rotation)` - every
  existing call site is updated to pass `f.Rotation`. No new public surface; `Doorway`'s hinge/leaf
  code no longer branches on `AlongX`.

> **Plan correction, made during Task 1's review (see the SDD ledger for this plan):** Task 1
> originally flattened `Front.Size()` in its own Step 2. Task 1's task review found that this broke
> box dimensions on `outward∈{left,right}` walls immediately, because nothing applies `Rotation` to
> a box until THIS task's Step 2 lands - the flattening and the rotation are two halves of one
> change and are not independently safe. Task 1 shipped with `Size()` still doing a world-axis swap
> (now keyed on `Out.z`, not the sign-ambiguous `Along`) as a stand-in. **Step 1 below undoes that
> stand-in** - it belongs here, not in Task 1, and must land in the same task as Step 2 (which is
> what actually makes the stand-in unnecessary).

- [ ] **Step 1: Flatten `Front.Size()` now that this task is about to give `Piece()` real rotation**

In `Assets/Noir/Unity/Frontage.cs`'s `Front` struct, find:

```csharp
            /// <summary>
            /// A box size, still in WORLD axes - <see cref="Piece"/> places every box at identity
            /// rotation, and applying <see cref="Rotation"/> there is Task 2's job, not landed yet.
            /// The swap this used to key off <c>Along.x &gt; 0.5f</c> - Along's sign is exactly what
            /// changed for the {right,back} walls (see the constructor), so that test would now
            /// silently swap the wrong walls. <see cref="Out"/> was never sign-ambiguous - it is
            /// still one of the four cardinal unit vectors here - so the swap is keyed on it instead.
            /// </summary>
            public Vector3 Size(float along, float up, float through)
            {
                bool alongX = Mathf.Abs(Out.z) > 0.5f;
                return new Vector3(alongX ? along : through, up, alongX ? through : along);
            }
```

Replace with:

```csharp
            /// <summary>
            /// A box's LOCAL size - along the wall, up it, and through it - unrotated. The piece
            /// this feeds must apply <see cref="Rotation"/> itself (see <see cref="Piece"/>, next
            /// step) - this no longer swaps which world axis is which, which was only ever a
            /// stand-in for real rotation while nothing applied one (see Task 1).
            /// </summary>
            public Vector3 Size(float along, float up, float through) => new Vector3(along, up, through);
```

- [ ] **Step 2: Add rotation to `Piece`**

Find (lines 727-742):

```csharp
        private static void Piece(Transform parent, string name, Vector3 position, Vector3 size,
                                  Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;

            Discard(go.GetComponent<Collider>());

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
```

Replace with:

```csharp
        private static void Piece(Transform parent, string name, Vector3 position, Vector3 size,
                                  Material material, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = rotation;
            go.transform.localScale = size;

            Discard(go.GetComponent<Collider>());

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
```

- [ ] **Step 3: Pass `f.Rotation` at every call site**

Every existing `Piece(...)` call in this file gains a trailing `f.Rotation` argument (every call site
has an `f`/`Front` in scope - that is what `Piece` positions the box relative to). Find and update
each of these nine call sites (`Boarding`'s and `Gates`' calls are handled separately in Step 4):

In `Doorway` (around line 311-317 before this plan's edits; line numbers will have shifted after
Task 1 - search for the text instead):

```csharp
            Piece(parent, "doorhead",
                  f.On((DoorHeight + eaves) * 0.5f, -wall * 0.5f),
                  f.Size(1.0f, eaves - DoorHeight, Mathf.Max(0.08f, wall - 0.04f)),
                  Materials3D.Walls[Materials3D.WallingFor(place)]);

            Piece(parent, "doorcase", f.On(DoorHeight * 0.5f + 0.03f, -0.05f),
                  f.Size(1.0f, DoorHeight + 0.06f, 0.14f), Materials3D.Stone);
```

Replace with:

```csharp
            Piece(parent, "doorhead",
                  f.On((DoorHeight + eaves) * 0.5f, -wall * 0.5f),
                  f.Size(1.0f, eaves - DoorHeight, Mathf.Max(0.08f, wall - 0.04f)),
                  Materials3D.Walls[Materials3D.WallingFor(place)], f.Rotation);

            Piece(parent, "doorcase", f.On(DoorHeight * 0.5f + 0.03f, -0.05f),
                  f.Size(1.0f, DoorHeight + 0.06f, 0.14f), Materials3D.Stone, f.Rotation);
```

Further down in `Doorway`:

```csharp
            Piece(parent, "doorstep", f.On(0.02f, 0.30f),
                  f.Size(1.30f, 0.16f, 0.62f), Materials3D.Stone);
```

Replace with:

```csharp
            Piece(parent, "doorstep", f.On(0.02f, 0.30f),
                  f.Size(1.30f, 0.16f, 0.62f), Materials3D.Stone, f.Rotation);
```

In `Fascia`:

```csharp
            Piece(parent, name, f.At(f.Centred(width), sill + height * 0.5f, Proud + 0.02f + 0.07f),
                  f.Size(width, height, 0.14f), material);
```

Replace with:

```csharp
            Piece(parent, name, f.At(f.Centred(width), sill + height * 0.5f, Proud + 0.02f + 0.07f),
                  f.Size(width, height, 0.14f), material, f.Rotation);
```

In `Hanging`:

```csharp
            Piece(parent, name + ":bracket", f.On(centreY + height * 0.5f + 0.03f, project * 0.5f),
                  f.Size(0.07f, 0.07f, project), Materials3D.Ironwork);

            Piece(parent, name, f.On(centreY, project - length * 0.5f - 0.10f),
                  f.Size(0.09f, height, length), board);
```

Replace with:

```csharp
            Piece(parent, name + ":bracket", f.On(centreY + height * 0.5f + 0.03f, project * 0.5f),
                  f.Size(0.07f, 0.07f, project), Materials3D.Ironwork, f.Rotation);

            Piece(parent, name, f.On(centreY, project - length * 0.5f - 0.10f),
                  f.Size(0.09f, height, length), board, f.Rotation);
```

In `Plate`:

```csharp
            Piece(parent, name, f.At(f.FaceAlong + side * reach, centreY, Proud + 0.03f),
                  f.Size(width, height, 0.06f), material);
```

Replace with:

```csharp
            Piece(parent, name, f.At(f.FaceAlong + side * reach, centreY, Proud + 0.03f),
                  f.Size(width, height, 0.06f), material, f.Rotation);
```

In `Notice`:

```csharp
            Piece(parent, name, f.At(along, 1.55f, Proud + 0.09f), f.Size(width, height, 0.14f), board);
            Piece(parent, name + ":hood", f.At(along, 1.55f + height * 0.5f + 0.06f, Proud + 0.16f),
                  f.Size(width + 0.16f, 0.09f, 0.44f), Materials3D.Bark);
```

Replace with:

```csharp
            Piece(parent, name, f.At(along, 1.55f, Proud + 0.09f), f.Size(width, height, 0.14f), board, f.Rotation);
            Piece(parent, name + ":hood", f.At(along, 1.55f + height * 0.5f + 0.06f, Proud + 0.16f),
                  f.Size(width + 0.16f, 0.09f, 0.44f), Materials3D.Bark, f.Rotation);
```

- [ ] **Step 4: Find the remaining `Piece(` call sites and update them the same way**

`Boarding` and `Gates` (the shutter builders, around what is currently lines 676-720) also call
`Piece` and were not quoted above - read them directly in the file (they follow the exact same
pattern: a `Front f` parameter already in scope) and add `, f.Rotation` to every `Piece(...)` call
inside both. Grep to confirm none were missed:

```bash
grep -n "Piece(" Assets/Noir/Unity/Frontage.cs
```

Expected: every match's line ends with `f.Rotation);` (or is the `Piece` method definition itself).
If any call site is missing the trailing argument, add it before continuing - a compile error here is
the safe failure mode (Step 6 will catch it), a silently-unrotated leftover box is not.

- [ ] **Step 5: Simplify the hinge/leaf placement in `Doorway`**

Find:

```csharp
            float hingeAlong = f.FaceAlong - DoorWidth * 0.5f;
            var hinge = new GameObject("hinge");
            hinge.transform.SetParent(parent, false);
            hinge.transform.position = f.At(hingeAlong, 0f, Proud - 0.06f);

            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = "door";
            leaf.transform.SetParent(hinge.transform, false);
            Discard(leaf.GetComponent<Collider>());
            var leafRenderer = leaf.GetComponent<MeshRenderer>();
            leafRenderer.sharedMaterial = DoorPaint(place, door);
            leafRenderer.shadowCastingMode = ShadowCastingMode.On;
            leafRenderer.receiveShadows = true;

            // Local, because the hinge is what turns: half a leaf along the wall from the post,
            // and half a leaf up from the threshold.
            bool alongX = Mathf.Abs(f.Along.x) > 0.5f;
            float half = DoorWidth * 0.5f;
            leaf.transform.localPosition = new Vector3(alongX ? half : 0f,
                                                       (DoorHeight - 0.06f) * 0.5f,
                                                       alongX ? 0f : half);
            leaf.transform.localScale = f.Size(DoorWidth, DoorHeight - 0.06f, 0.12f);

            // Which way is "in"? `Out` points away from the building, so a shop turns the leaf
            // towards Out and a house away from it. The sign of the yaw follows the wall's own
            // heading so both walls of a corner shop still open outward.
            float shutYaw = hinge.transform.localEulerAngles.y;
            float side = alongX ? Mathf.Sign(f.Out.z) : -Mathf.Sign(f.Out.x);
            float swing = Commercial(place) ? 85f : -85f;
            Doors?.Add(hinge.transform, shutYaw, shutYaw + swing * side);
```

Replace with:

```csharp
            float hingeAlong = f.FaceAlong - DoorWidth * 0.5f;
            var hinge = new GameObject("hinge");
            hinge.transform.SetParent(parent, false);
            hinge.transform.position = f.At(hingeAlong, 0f, Proud - 0.06f);
            hinge.transform.rotation = f.Rotation;

            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = "door";
            leaf.transform.SetParent(hinge.transform, false);
            Discard(leaf.GetComponent<Collider>());
            var leafRenderer = leaf.GetComponent<MeshRenderer>();
            leafRenderer.sharedMaterial = DoorPaint(place, door);
            leafRenderer.shadowCastingMode = ShadowCastingMode.On;
            leafRenderer.receiveShadows = true;

            // Local to the hinge, which now CARRIES the wall's rotation - so "half a leaf along
            // the wall from the post" is always local +X, the same for every wall, cardinal or
            // not. This is what f.Rotation on the hinge buys: the AlongX branch that used to pick
            // world X or world Z is gone because there is no more world-axis case to pick between.
            float half = DoorWidth * 0.5f;
            leaf.transform.localPosition = new Vector3(half, (DoorHeight - 0.06f) * 0.5f, 0f);
            leaf.transform.localScale = f.Size(DoorWidth, DoorHeight - 0.06f, 0.12f);

            // Which way is "in"? Out points away from the building; with the hinge's own rotation
            // now carrying the wall's true heading, "outward" in the hinge's LOCAL frame is
            // always local +Z (see Front's constructor: Rotation*forward == Out) - so the shop/
            // house sign no longer needs to read Out's world components at all.
            float shutYaw = hinge.transform.eulerAngles.y;
            float swing = Commercial(place) ? 85f : -85f;
            Doors?.Add(hinge.transform, shutYaw, shutYaw + swing);
```

- [ ] **Step 6: Build and check for compile errors**

Same as Task 1 Step 4.

- [ ] **Step 7: Render the SAME two ordinary buildings again, pixel-compare against Task 1's "after" shot**

This is the load-bearing check for Task 2, because it is the step that touches hinge/swing sign - the
one thing Task 1 deliberately left untested. Render both doors again (Play mode, same camera poses as
Task 1 Step 5), for BOTH the shut state and, if `Doors` is wired (it is, once `VillageHost`/`SunRig`
sets `Frontage.Doors` - confirm via `Unity_GetConsoleLogs` for the `[doors] N of M leaves hung on a
hinge` line this file already logs) an open state - stand a capsule/the player within `Reach` (1.9 m)
of each door and confirm in a screenshot that it swings the SAME direction it did before this task
(inward for the house, outward for the shop - `CommercialRow`/`Commercial(place)` tells you which is
which). Save to `docs/snapshots/frontage-task2-<name>-{shut,open}.png`.

Expected: shut-state pixel-identical to Task 1's "after" shot (proving the geometry didn't move even
though it is now nominally rotated by an identity-equivalent quaternion for a cardinal wall); open
state swings the same direction as it did on the pre-Task-1 tree. If a swing direction flipped: STOP.
The `shutYaw`/no-`side` simplification in Step 5 is wrong for at least one of house-vs-shop or one of
the four wall directions, and needs to be found before Task 3.

- [ ] **Step 8: Commit**

```bash
git add Assets/Noir/Unity/Frontage.cs
git commit -m "Every frontage piece rotates with its wall instead of assuming the four compass points"
```

---

### Task 3: Teach `FrontAt` to read a shaped building's true wall angle

**Files:**
- Modify: `Assets/Noir/Unity/Frontage.cs` (`Openings`, the call site in `Build` that invokes `FrontAt`)

**Interfaces:**
- Consumes: `Place.OutlinePrecise` (existing, from the precise-shaped-corners plan), `Front.Rotation`/
  the `FrontOf` helper (Task 1).
- Produces: doors, signs and shutters on a shaped building now sit flush against its true wall instead
  of the nearest compass direction. Every other building - anything with `OutlinePrecise == null` -
  is completely unaffected; the fallback path is the exact cardinal logic Task 1/2 already proved
  unchanged.

- [ ] **Step 1: Add a precise-edge lookup, mirroring `DrawShapedPerimeters`'s door-edge search**

`Assets/Noir/Unity/VillageMesh.cs`'s `DrawShapedPerimeters` (around line 1747-1772) already finds
"which edge of this place's ring the door actually opens onto" - the same search this file needs, so
the two must agree about which edge a given door is on. Add a new private method to `Frontage.cs`,
near `FrontAt`:

```csharp
        /// <summary>
        /// Which edge of a shaped place's own precise ring a doorway sits on, and where along it -
        /// the same "closest point on the closest edge" search DrawShapedPerimeters
        /// (Assets/Noir/Unity/VillageMesh.cs) runs for the SAME reason: the two must agree about
        /// which wall a door belongs to, or the hole and the frame that fills it disagree about
        /// which direction is outward.
        ///
        /// Returns false (and leaves outward/edgeStart/edgeEnd untouched) when the place has no
        /// usable precise ring, or the ring is malformed - the caller falls back to the cardinal
        /// FrontAt in that case, exactly as it always has.
        /// </summary>
        private static bool PreciseEdgeAt(Place place, Tile door,
                                          out Vector3 outward, out Vector3 edgeStart, out Vector3 edgeEnd)
        {
            outward = default; edgeStart = default; edgeEnd = default;
            var precise = place.OutlinePrecise;
            var outline = place.Outline;
            if (precise == null || outline == null || precise.Length != outline.Length || precise.Length < 3)
                return false;

            int n = precise.Length;
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++) pts[i] = new Vector2(precise[i].X, precise[i].Y);

            float signedArea = 0f;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % n];
                signedArea += a.x * b.y - b.x * a.y;
            }
            var ring = pts;
            if (signedArea < 0f)
            {
                ring = new Vector2[n];
                for (int i = 0; i < n; i++) ring[i] = pts[n - 1 - i];
            }

            var doorPoint = new Vector2(door.X + 0.5f, door.Y + 0.5f);
            int bestEdge = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var p0 = ring[i];
                var p1 = ring[(i + 1) % n];
                float len = Vector2.Distance(p0, p1);
                if (len < 0.01f) continue;
                var dir = (p1 - p0) / len;
                float t = Mathf.Clamp(Vector2.Dot(doorPoint - p0, dir), 0f, len);
                float dist = Vector2.Distance(doorPoint, p0 + dir * t);
                if (dist < bestDist) { bestDist = dist; bestEdge = i; }
            }
            if (bestEdge < 0) return false;

            var e0 = ring[bestEdge];
            var e1 = ring[(bestEdge + 1) % n];
            var edgeDir = (e1 - e0).normalized;
            // +90 degrees from the direction of travel points into a CCW ring's own interior (see
            // DrawShapedPerimeters's own comment on this exact formula) - negate it for OUTWARD.
            var normal2 = new Vector2(edgeDir.y, -edgeDir.x);

            outward = new Vector3(normal2.x, 0f, normal2.y);
            edgeStart = new Vector3(e0.x, 0f, -e0.y);
            edgeEnd = new Vector3(e1.x, 0f, -e1.y);
            return true;
        }
```

- [ ] **Step 2: Give `FrontAt` a `Place` overload that tries the precise edge first**

Find the `FrontAt` you finished in Task 1 (`private static Front FrontAt(TileRect b, Tile door)`).
Rename the existing method to `FrontAtBounds` and add a new `FrontAt(Place, Tile)` that tries the
precise path first:

```csharp
        private static Front FrontAt(Place place, Tile door)
        {
            if (!door.IsValid) return default;

            if (PreciseEdgeAt(place, door, out var outward, out var edgeStart, out var edgeEnd))
            {
                var f = FrontOf(outward, new[] { edgeStart, edgeEnd });
                return new Front(Space3D.ToWorld(door) + outward * 0.5f, outward, f.Lo, f.Hi);
            }

            return FrontAtBounds(place.Bounds, door);
        }

        private static Front FrontAtBounds(TileRect b, Tile door)
        {
            if (!door.IsValid) return default;

            Vector3 outward;
            if (door.X == b.X) outward = Vector3.left;
            else if (door.X == b.Right) outward = Vector3.right;
            else if (door.Y == b.Y) outward = Vector3.forward;    // grid rows count south, so row 0 faces +Z
            else if (door.Y == b.Bottom) outward = Vector3.back;
            else return default;

            var f = FrontOf(outward, BoundsCorners(b));
            return new Front(Space3D.ToWorld(door) + outward * 0.5f, outward, f.Lo, f.Hi);
        }
```

- [ ] **Step 3: Update `Build`'s call sites to pass `place` instead of `place.Bounds`**

In `Frontage.Build` (around lines 84-96), find:

```csharp
                foreach (var tile in openings)
                {
                    var opening = FrontAt(place.Bounds, tile);
                    if (!opening.Valid) continue;
                    pieces += Doorway(doorsRoot, place, tile, opening);
                    doors++;
                }

                // Signs and shutters hang off the authored door, which is the one on the street
                // even in a building that turned out to have several.
                var front = FrontAt(place.Bounds, place.Door.IsValid ? place.Door
                                  : openings.Count > 0 ? openings[0] : Tile.None);
```

Replace with:

```csharp
                foreach (var tile in openings)
                {
                    var opening = FrontAt(place, tile);
                    if (!opening.Valid) continue;
                    pieces += Doorway(doorsRoot, place, tile, opening);
                    doors++;
                }

                // Signs and shutters hang off the authored door, which is the one on the street
                // even in a building that turned out to have several.
                var front = FrontAt(place, place.Door.IsValid ? place.Door
                                  : openings.Count > 0 ? openings[0] : Tile.None);
```

- [ ] **Step 4: Build and check for compile errors**

Same as Task 1 Step 4.

- [ ] **Step 5: Re-run Task 1/2's ordinary-building pixel comparison one more time**

Every non-terrace `Place` has `OutlinePrecise == null`, so `PreciseEdgeAt` returns `false` for all of
them and `FrontAt` falls straight through to `FrontAtBounds` - the exact path Task 1/2 already proved
unchanged. Re-render the same two ordinary buildings' doors (shut state is enough here) and confirm
still pixel-identical to Task 2 Step 7's shot. This is a cheap, fast confirmation that Step 2's
`FrontAt`/`FrontAtBounds` split didn't change the fallback path while renaming it.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Unity/Frontage.cs
git commit -m "FrontAt reads a shaped building's own wall angle off OutlinePrecise before falling back to the compass"
```

---

### Task 4: Verification — the terrace, and a report back

No new code - confirms Tasks 1-3 actually closed the gap the owner is looking at.

- [ ] **Step 1: Render 112 S Chicago from the same grazing angle used throughout this session's diagnosis**

Camera pose `(789, 17, -1374)`, `eulerAngles (18, 261, 0)` - the same one used in the
precise-shaped-corners plan's Task 4 and this session's diagnosis. Compare against
`docs/snapshots/AFTER-GROUNDHEIGHT-FIX.png` (this session's last "still broken" shot): the pale
vertical bands at each doorway should be gone, replaced by an actual door frame sitting flush in the
angled hole.

- [ ] **Step 2: Raycast through where a gap used to be**

Re-run this session's diagnostic raycast (`Physics.Raycast` from the same camera through the same
screen pixels that used to read as a gap - `(890,550)` and neighbours in the 1920x1080 grazing-angle
render) and confirm the hit is now on the terrace's own geometry (door frame or wall, normal close to
the wall's own `(0.95, 0, 0.30)`-family direction) rather than sailing through to a stray prop 50+
metres away.

- [ ] **Step 3: Confirm a door on the terrace still opens**

Stand within `Reach` (1.9 m) of one of the 17 doors (in Play, with `Frontage.Doors` wired) and confirm
in a screenshot it swings - and swings OUTWARD (`Commercial(place)` is true for `Shop`, per
`Frontage.Commercial`'s switch), consistent with every other shop on the street.

- [ ] **Step 4: Run the standing PlayMode gate**

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode -assemblyNames Noir.PlayTests -testCategory "!Diagnostic" -testResults <xml> -logFile <log>
```

Expected: unchanged from the baseline in `CLAUDE.md` at the time this plan is executed - this plan
touches no Core code and no gameplay logic, only frontage cosmetics, so no PlayMode test should move.

- [ ] **Step 5: Report to the owner**

Tell him it's ready to look at, with the before/after grazing-angle shots. Remind him this was two
separate bugs that happened to look alike: the wall-direction kink (precise-shaped-corners plan,
already shipped) and the door/sign frames not knowing about a shaped wall's angle (this plan). Both
are now fixed for 112 S Chicago; every other door, sign and shutter in the town was proved unchanged
at three separate checkpoints (end of Task 1, Task 2, and Task 3) because they all still take the
cardinal fallback path.
