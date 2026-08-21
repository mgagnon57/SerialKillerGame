# Player Interaction (doors) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the player a deliberate Open/Close action on doors, as the first proven slice of a
general, extensible interaction framework.

**Architecture:** A three-member `IInteractable` interface (position, available verbs, perform)
is the seam every future action plugs into. `CityDoors` gains a nearest-door query and a timed
Force override on top of its existing automatic proximity swing. A `DoorInteractable` adapter
wraps one door as an `IInteractable`. A new `PlayerInteraction` component finds the nearest
interactable each frame the player is walking and draws its verb menu with IMGUI, matching the
rest of the game's UI.

**Tech Stack:** Unity 6000.3.20f1, C#, `UnityEngine.InputSystem` for the player's existing input
(unchanged by this plan — the menu is IMGUI's own mouse handling, not a new input binding).

**Spec:** `docs/superpowers/specs/2026-08-14-player-interaction-design.md`

## Global Constraints

- No Core changes. This is entirely `Assets/Noir/Unity` and `Assets/Noir/PlayTests` — nothing here
  is `dotnet test`-testable, per the spec's own Testing section.
- Windows, verbs beyond Open/Close, and Core-side door state are explicitly out of scope — do not
  add them.
- `CityDoors.Reach` is `public const float Reach = 1.9f;` after Task 1 — `public`, not `internal`,
  because the PlayMode tests that reference it live in the separate `Noir.PlayTests` assembly and
  this project has no `InternalsVisibleTo` anywhere (checked). `PlayerInteraction` reads this
  constant directly rather than choosing its own interaction range, so the menu is never offered
  at a distance the door itself is not already reacting to.
- The override window is 5 seconds (`OverrideHold = 5f` in `CityDoors`) — long enough to step away
  from a door, short enough that walking back up to a shut door behaves normally again.

---

### Task 1: `CityDoors` gains a nearest-door query and a forceable override

**Files:**
- Modify: `Assets/Noir/Unity/CityDoors.cs`
- Test: `Assets/Noir/PlayTests/PlayerInteractionPlayTests.cs` (new file)

**Interfaces:**
- Produces: `CityDoors.Reach` (now `public`), `CityDoors.PositionOf(int index) : Vector3`,
  `CityDoors.NearestDoor(Vector3 from, float within) : int` (hinge index, or -1),
  `CityDoors.IsOpen(int index) : bool`, `CityDoors.Force(int index, bool open) : void`.
  Task 2's `DoorInteractable` consumes all five.

- [ ] **Step 1: Change `Reach` from `private` to `public`**

In `Assets/Noir/Unity/CityDoors.cs`, find:

```csharp
        /// <summary>How near a person has to be for the door to open.</summary>
        private const float Reach = 1.9f;
```

Replace with:

```csharp
        /// <summary>How near a person has to be for the door to open. Public so
        /// PlayerInteraction can offer its menu at exactly this distance, rather than choosing a
        /// second, independently-picked number that can drift out of sync with it - and public
        /// rather than internal because the PlayMode tests that reference it live in the separate
        /// Noir.PlayTests assembly, which this project grants no InternalsVisibleTo access to.</summary>
        public const float Reach = 1.9f;
```

- [ ] **Step 2: Add the override-hold constant and the per-door override list**

Find:

```csharp
        private const int RehashFrames = 12;
        private const float CellSize = 8f;

        private readonly List<Transform> _hinges = new List<Transform>();
        private readonly List<float> _shut = new List<float>();
        private readonly List<float> _open = new List<float>();
        private readonly List<float> _angle = new List<float>();
        private readonly List<Vector3> _at = new List<Vector3>();
```

Replace with:

```csharp
        private const int RehashFrames = 12;
        private const float CellSize = 8f;

        /// <summary>How long a manual Close beats automatic proximity, in seconds. Long enough to
        /// step away from the door; short enough that walking back up to a shut door behaves
        /// normally again rather than staying artificially locked.</summary>
        private const float OverrideHold = 5f;

        private readonly List<Transform> _hinges = new List<Transform>();
        private readonly List<float> _shut = new List<float>();
        private readonly List<float> _open = new List<float>();
        private readonly List<float> _angle = new List<float>();
        private readonly List<Vector3> _at = new List<Vector3>();
        private readonly List<float> _overrideUntil = new List<float>();
```

- [ ] **Step 3: Track the override list alongside the others in `Add`**

Find:

```csharp
        public void Add(Transform hinge, float shutYaw, float openYaw)
        {
            if (hinge == null) return;
            _hinges.Add(hinge);
            _shut.Add(shutYaw);
            _open.Add(openYaw);
            _angle.Add(shutYaw);
            _at.Add(hinge.position);
        }
```

Replace with:

```csharp
        public void Add(Transform hinge, float shutYaw, float openYaw)
        {
            if (hinge == null) return;
            _hinges.Add(hinge);
            _shut.Add(shutYaw);
            _open.Add(openYaw);
            _angle.Add(shutYaw);
            _at.Add(hinge.position);
            _overrideUntil.Add(0f);
        }
```

- [ ] **Step 4: Make the override win over proximity in `Update`**

Find:

```csharp
                bool open = _angle[i] != _shut[i];                     // already ajar?
                want = SomebodyWithin(_at[i], open ? Hold : Reach) ? _open[i] : _shut[i];
```

Replace with:

```csharp
                bool open = _angle[i] != _shut[i];                     // already ajar?
                bool overridden = Time.time < _overrideUntil[i];
                want = overridden ? _shut[i]
                     : SomebodyWithin(_at[i], open ? Hold : Reach) ? _open[i] : _shut[i];
```

- [ ] **Step 5: Add `PositionOf`, `NearestDoor`, `IsOpen`, `Force`**

Find the end of the `Add` method (now ending `_overrideUntil.Add(0f);\n        }`) and insert
immediately after it, before `private static long CellOf(Vector3 p)`:

```csharp
        /// <summary>The world position Update measures this door's own distance checks from.</summary>
        public Vector3 PositionOf(int index) => _at[index];

        /// <summary>Whether hinge <paramref name="index"/> is currently ajar rather than shut.</summary>
        public bool IsOpen(int index) => _angle[index] != _shut[index];

        /// <summary>
        /// The hinge index of the nearest door to <paramref name="from"/> within
        /// <paramref name="within"/> metres, or -1 if none.
        ///
        /// Ignores the LiveRange gate Update uses to limit its own per-frame cost - a menu asking
        /// "what is nearest" is a one-off query on approach, not a per-frame walk of every door in
        /// town, so it costs nothing to check all of them.
        /// </summary>
        public int NearestDoor(Vector3 from, float within)
        {
            int best = -1;
            float bestD2 = within * within;
            for (int i = 0; i < _hinges.Count; i++)
            {
                if (_hinges[i] == null) continue;
                var d = _at[i] - from;
                float d2 = d.x * d.x + d.z * d.z;
                if (d2 > bestD2) continue;
                bestD2 = d2;
                best = i;
            }
            return best;
        }

        /// <summary>
        /// The player's own choice, which beats proximity for a while rather than forever.
        ///
        /// Opening clears any active override and lets Update's own easing carry the swing the
        /// rest of the way - the way out of a bad Close. Closing sets the override: without it a
        /// door shut here would swing straight back open next frame, because the player is by
        /// definition standing within Hold range to have reached this door's menu at all.
        /// </summary>
        public void Force(int index, bool open)
        {
            _overrideUntil[index] = open ? 0f : Time.time + OverrideHold;
        }

```

- [ ] **Step 6: Write the PlayMode tests**

Create `Assets/Noir/PlayTests/PlayerInteractionPlayTests.cs`:

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// CityDoors' player-facing API: finding the nearest door, and the timed override that lets
    /// a deliberate Close beat automatic proximity for a while.
    /// </summary>
    public class PlayerInteractionPlayTests
    {
        [UnitySetUp]
        public IEnumerator Ready()
        {
            Time.timeScale = 1f;
            yield return CityUnderTest.WaitUntilBuilt();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator NearestDoorFindsTheClosestOneInRange()
        {
            var doors = Object.FindFirstObjectByType<CityDoors>();
            Assert.That(doors, Is.Not.Null, "no CityDoors was created by the host");
            Assert.That(doors.Count, Is.GreaterThan(0), "no doors in this town");

            var at = doors.PositionOf(0);

            int nearest = doors.NearestDoor(at, CityDoors.Reach);
            Assert.That(nearest, Is.EqualTo(0), "the door's own position did not find itself");

            int tooFar = doors.NearestDoor(at + new Vector3(10_000f, 0f, 0f), CityDoors.Reach);
            Assert.That(tooFar, Is.EqualTo(-1), "a point 10km away found a door within range");

            yield break;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator ForceCloseBeatsProximityUntilItExpires()
        {
            var doors = Object.FindFirstObjectByType<CityDoors>();
            var at = doors.PositionOf(0);

            var player = Object.FindFirstObjectByType<Player>();
            player.Toggle();
            for (int frame = 0; frame < 5; frame++) yield return null;
            var body = GameObject.Find("PlayerArmature");
            body.transform.position = at;

            for (int frame = 0; frame < 60; frame++) yield return null;   // let it swing open
            Assert.That(doors.IsOpen(0), Is.True, "the door never opened for a standing player");

            doors.Force(0, false);
            float forcedAt = Time.time;
            for (int frame = 0; frame < 30; frame++) yield return null;   // let it swing shut
            Assert.That(doors.IsOpen(0), Is.False, "Force(false) did not shut the door");

            // Still well inside the override window - proximity alone would reopen it.
            while (Time.time < forcedAt + 2f) yield return null;
            Assert.That(doors.IsOpen(0), Is.False,
                        "the door reopened while the override should still be active");

            // Past the window - proximity should take control back, and the player is still
            // standing right there, so it should swing open again on its own.
            while (Time.time < forcedAt + 5.5f) yield return null;
            for (int frame = 0; frame < 30; frame++) yield return null;   // let it swing back open
            Assert.That(doors.IsOpen(0), Is.True,
                        "proximity never took control back after the override expired");

            player.Toggle();
        }
    }
}
```

- [ ] **Step 7: Run the PlayMode gate**

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode ^
  -assemblyNames Noir.PlayTests -testFilter "Noir.PlayTests.PlayerInteractionPlayTests" ^
  -testResults <xml> -logFile <log>
```

Expected: 2 of 2 pass. The editor must be closed first (batch mode takes an exclusive lock).

- [ ] **Step 8: Commit**

```bash
git add Assets/Noir/Unity/CityDoors.cs Assets/Noir/PlayTests/PlayerInteractionPlayTests.cs
git commit -m "CityDoors: a nearest-door query and a timed Force override over proximity"
```

---

### Task 2: `IInteractable`, `DoorInteractable`, `PlayerInteraction`, and wiring it in

**Files:**
- Create: `Assets/Noir/Unity/IInteractable.cs`
- Create: `Assets/Noir/Unity/DoorInteractable.cs`
- Create: `Assets/Noir/Unity/PlayerInteraction.cs`
- Modify: `Assets/Noir/Unity/VillageHost.cs`
- Test: `Assets/Noir/PlayTests/PlayerInteractionPlayTests.cs` (append)

**Interfaces:**
- Consumes: `CityDoors.Reach`, `CityDoors.PositionOf`, `CityDoors.NearestDoor`, `CityDoors.IsOpen`,
  `CityDoors.Force` — all from Task 1.
- Produces: `IInteractable` (interface), `DoorInteractable` (class), `PlayerInteraction.Current`
  (`IInteractable`, null when nothing is in range) — nothing later in this plan consumes these,
  but they are the seam a future action plugs into.

- [ ] **Step 1: Write `IInteractable`**

Create `Assets/Noir/Unity/IInteractable.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// The seam a player action plugs into: where to draw its menu, what it can be asked to do,
    /// and how to do it. Doors are the first thing behind this interface - see DoorInteractable
    /// for the adapter. Nothing here knows about hinges, angles, or CityDoors.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Where to draw this interactable's menu, in world space.</summary>
        Vector3 Position { get; }

        /// <summary>The verbs available right now - may change between calls (a door offers
        /// "Open" when shut, "Close" when open).</summary>
        IReadOnlyList<string> Verbs { get; }

        /// <summary>Carry out one of the verbs from <see cref="Verbs"/>.</summary>
        void Perform(string verb);
    }
}
```

- [ ] **Step 2: Write `DoorInteractable`**

Create `Assets/Noir/Unity/DoorInteractable.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// One door, offered through the general IInteractable seam. Wraps a (CityDoors, hinge index)
    /// pair rather than owning any state of its own - CityDoors already knows everything about
    /// this door; this only translates its questions (position, verbs) and its one command
    /// (perform) into the generic shape PlayerInteraction expects.
    /// </summary>
    public sealed class DoorInteractable : IInteractable
    {
        private static readonly string[] OpenVerb = { "Open" };
        private static readonly string[] CloseVerb = { "Close" };

        private readonly CityDoors _doors;
        private readonly int _index;

        public DoorInteractable(CityDoors doors, int index)
        {
            _doors = doors;
            _index = index;
        }

        public Vector3 Position => _doors.PositionOf(_index);

        public IReadOnlyList<string> Verbs => _doors.IsOpen(_index) ? CloseVerb : OpenVerb;

        public void Perform(string verb) => _doors.Force(_index, verb == "Open");
    }
}
```

- [ ] **Step 3: Write `PlayerInteraction`**

Create `Assets/Noir/Unity/PlayerInteraction.cs`:

```csharp
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// Finds the nearest thing the player can act on and offers its menu.
    ///
    /// ONE PROVIDER FOR NOW, ASKED DIRECTLY. CityDoors is the only source of interactables this
    /// pass builds, so this asks it directly rather than through a registry - a registry earns
    /// its keep the day a second provider exists, and guessing its shape before that day arrives
    /// is more likely to be wrong than useful.
    ///
    /// ONLY LIVE WHILE THE PLAYER IS IN THE STREET. Interaction is a first-person mechanic; there
    /// is nothing to act on from the overview camera, and Player.Where is null there anyway.
    /// </summary>
    public sealed class PlayerInteraction : MonoBehaviour
    {
        /// <summary>How close the player must stand for a door to offer its menu - the exact
        /// distance CityDoors itself swings a door open at, so the menu is never offered at a
        /// range where the door is not already reacting to the player being there.</summary>
        private const float Range = CityDoors.Reach;

        private VillageHost _host;
        private CityDoors _doors;
        private GUIStyle _button;

        /// <summary>The interactable currently offering its menu, or null.</summary>
        public IInteractable Current { get; private set; }

        public static PlayerInteraction Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("PlayerInteraction");
            go.transform.SetParent(parent, false);
            var it = go.AddComponent<PlayerInteraction>();
            it._host = host;
            return it;
        }

        private void Update()
        {
            Current = null;

            var player = _host.Player;
            if (player == null || !player.Walking) return;
            var where = player.Where;
            if (!where.HasValue) return;

            if (_doors == null) _doors = Object.FindFirstObjectByType<CityDoors>();
            if (_doors == null) return;

            int nearest = _doors.NearestDoor(where.Value, Range);
            if (nearest < 0) return;

            Current = new DoorInteractable(_doors, nearest);
        }

        private void OnGUI()
        {
            if (Current == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            var screen = cam.WorldToScreenPoint(Current.Position);
            if (screen.z <= 0f) return;                    // behind the camera
            float x = screen.x, y = Screen.height - screen.y;
            if (x < 0f || x > Screen.width || y < 0f || y > Screen.height) return;

            if (_button == null) _button = new GUIStyle(GUI.skin.button) { fontSize = 16 };

            var verbs = Current.Verbs;
            const float w = 90f, h = 32f, gap = 4f;
            for (int i = 0; i < verbs.Count; i++)
            {
                var rect = new Rect(x - w * 0.5f, y - (verbs.Count - i) * (h + gap), w, h);
                if (GUI.Button(rect, verbs[i], _button))
                    Current.Perform(verbs[i]);
            }
        }
    }
}
```

- [ ] **Step 4: Wire it into `VillageHost`**

In `Assets/Noir/Unity/VillageHost.cs`, find the `_player` field declaration:

```csharp
        private Player _player;
```

Replace with:

```csharp
        private Player _player;
        private PlayerInteraction _interaction;
```

Then find:

```csharp
            _player = Player.Create(this, transform);
            profile.Done("Player");
```

Replace with:

```csharp
            _player = Player.Create(this, transform);
            profile.Done("Player");
            _interaction = PlayerInteraction.Create(this, transform);
            profile.Done("PlayerInteraction");
```

- [ ] **Step 5: Add the detection test**

In `Assets/Noir/PlayTests/PlayerInteractionPlayTests.cs`, add this test inside the
`PlayerInteractionPlayTests` class, after `ForceCloseBeatsProximityUntilItExpires`:

```csharp
        [UnityTest, Timeout(900000)]
        public IEnumerator OffersTheNearestDoorsMenuAndSwitchesVerbOnState()
        {
            var doors = Object.FindFirstObjectByType<CityDoors>();
            var interaction = Object.FindFirstObjectByType<PlayerInteraction>();
            Assert.That(interaction, Is.Not.Null, "no PlayerInteraction was created by the host");

            var at = doors.PositionOf(0);
            var player = Object.FindFirstObjectByType<Player>();
            player.Toggle();
            for (int frame = 0; frame < 5; frame++) yield return null;

            var body = GameObject.Find("PlayerArmature");
            body.transform.position = at + new Vector3(1000f, 0f, 0f);   // far from every door
            yield return null;
            Assert.That(interaction.Current, Is.Null, "offered a menu with nobody near a door");

            body.transform.position = at;
            yield return null;
            Assert.That(interaction.Current, Is.Not.Null, "no menu offered standing at a door");
            Assert.That(interaction.Current.Verbs, Does.Contain("Open").Or.Contain("Close"));

            player.Toggle();
        }
```

- [ ] **Step 6: Run the PlayMode gate**

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode ^
  -assemblyNames Noir.PlayTests -testFilter "Noir.PlayTests.PlayerInteractionPlayTests" ^
  -testResults <xml> -logFile <log>
```

Expected: 3 of 3 pass. The editor must be closed first.

- [ ] **Step 7: Look at it**

This is the one part of the spec that no test can see (its own Testing section says so). Build,
press **P** to enter the street, walk up to a house door, confirm an "Open"/"Close" button appears
and that clicking it actually does something. Do not assert it relabels "the same frame" — a real
door takes the whole swing (~0.57s) to finish moving, and `IsOpen` (which the label reads) is
derived from that same in-progress angle, so a label that flips instantly would mean the door
teleported rather than swung. Per this project's standing rule, do not report this task done
without having actually looked — and note that IMGUI's `Event.current.mousePosition` does not
track real mouse movement while `Cursor.lockState == Locked` (which `Player.Enter()` sets), so
"looked at it" here means confirming the click actually registers, not just that the button is
visible.

- [ ] **Step 8: Commit**

```bash
git add Assets/Noir/Unity/IInteractable.cs Assets/Noir/Unity/DoorInteractable.cs \
        Assets/Noir/Unity/PlayerInteraction.cs Assets/Noir/Unity/VillageHost.cs \
        Assets/Noir/PlayTests/PlayerInteractionPlayTests.cs
git commit -m "Player interaction: a general verb-menu framework, proven on door Open/Close"
```

