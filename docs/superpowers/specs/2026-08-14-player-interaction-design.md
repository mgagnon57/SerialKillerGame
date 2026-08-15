# Player interaction: a general action framework, proven on doors

**The ask.** "Adding actions to my character. Like open door, open windows, etc, etc." Confirmed
in brainstorming: this is the first slice of a general, extensible interaction system — not a
door-specific hack — but the concrete deliverable for this pass is doors only, with two verbs,
Open and Close.

## What exists today

`CityDoors` (`Assets/Noir/Unity/CityDoors.cs`) already swings a door's leaf open when anyone —
NPC or player — walks within `Reach` (1.9 m), and holds it open while they stay within `Hold`
(2.9 m), house doors swinging in and shopfronts swinging out. It is entirely proximity-driven:
no key press, no menu, nothing the player decides. It tracks state per door in parallel arrays
(`_hinges`, `_shut`, `_open`, `_angle`) and recomputes each door's target angle every frame from
who is nearby, via a coarse spatial hash it rebuilds every 12 frames.

`Player` (`Assets/Noir/Unity/Player.cs`) stands a body in the street when the owner presses **P**
and exposes `Where` (a `Vector3?`, null unless `Walking`) and the `Camera.main` it drives. Every
control it reads goes through the new Input System (`Keyboard.current`, `Mouse.current`), not the
legacy `Input` class.

All game UI is IMGUI (`VillageUI.cs`'s own comment: "no assets, no canvas, no [scene setup]").
There is no existing verb, prompt, or interaction concept anywhere in the tree.

## Out of scope for this pass

Settled in brainstorming, recorded here so a later session does not have to re-derive it:

- **Windows.** A window today is a flat pane the wall texture draws and a shutter that goes up or
  down — there is no sash, no hinge, nothing with geometry to open. Building that is a separate
  piece of work with its own design questions (does it slide, does it swing, which house grammars
  get one). The framework below is built so a future window verb plugs in the same way a door does,
  but no window geometry is built now.
- **Core-side door state.** `CityDoors`' own comments already flag the correct long-term shape: a
  `Door` in `Noir.Core.World` with open/closed/locked, driven by the sim, so something could later
  ask "was that door open when he walked past." That question does not exist yet — nothing this
  pass needs is deterministic or Core-testable — so state stays exactly where `CityDoors` already
  keeps it. Move it to Core the day a verb actually needs to be queried by name.
- **Any verb beyond Open/Close.** Knock, lock-pick, look-through, examine — all plausible future
  verbs the same framework should carry, none built now.

## The change

### `IInteractable` — the general seam

```csharp
// Assets/Noir/Unity/IInteractable.cs
public interface IInteractable
{
    Vector3 Position { get; }              // where to draw the menu
    IReadOnlyList<string> Verbs { get; }   // e.g. ["Open"] or ["Close"]
    void Perform(string verb);
}
```

Three members, deliberately. Nothing here knows about doors, hinges, or angles — that is the
whole point of the seam. A future window or evidence item implements the same three members and
`PlayerInteraction` (below) never changes.

### `CityDoors` gains a query and a forceable override

`Reach` becomes `internal const` (from `private`) so `PlayerInteraction` uses the exact same
distance `CityDoors` already swings a door open at, rather than a second, independently-chosen
number that can drift out of sync with it.

```csharp
// on CityDoors
public int NearestDoor(Vector3 from, float within);   // hinge index, or -1
public bool IsOpen(int index);                         // _angle[index] != _shut[index]
public void Force(int index, bool open);
```

`Force` is where the real design decision from brainstorming lives: **the player's manual choice
overrides proximity for a while, then hands control back.** A close-only override list,
`_overrideUntil` (parallel to `_hinges`, `float`, `Time.time`-based), is added. `Force(i, false)`
sets `_angle[i]` moving toward `_shut[i]` and sets `_overrideUntil[i] = Time.time + OverrideHold`
(5 s — long enough to step away, short enough that walking back up to a shut door behaves
normally again). While `Time.time < _overrideUntil[i]`, `Update`'s existing proximity computation
is skipped for that door; the target stays shut regardless of who is standing in `Hold` range.
`Force(i, true)` clears the override immediately (`_overrideUntil[i] = 0`) and sets the target
open — the accelerator on what proximity was already about to do, and the way out of a bad Close.

Without this, "Close" would be a button that visibly does nothing: the player is by definition
standing within `Hold` range to have opened the menu, so next frame's proximity check would swing
the door straight back open.

### `DoorInteractable` — the adapter

A small `IInteractable` implementation wrapping one `(CityDoors owner, int index)` pair. `Verbs`
returns `["Close"]` when `owner.IsOpen(index)`, `["Open"]` otherwise — context-sensitive by
construction, not a fixed two-button menu, so the player is never shown a button that does nothing
useful. `Perform` calls `owner.Force(index, verb == "Open")`.

### `PlayerInteraction` — finds the nearest thing, draws its menu

New component, created by `VillageHost` right after `_player = Player.Create(...)` (line 917) —
it needs both `_doors` (created earlier, line 539) and `_player` to already exist, and `Player` is
the later of the two. Each frame, only while `host.Player.Walking`:

1. Ask `CityDoors.NearestDoor(player.Where.Value, CityDoors.Reach)` for the closest door in range
   (`-1` if none). This is the only provider for this pass; a `Nearest`-style registry is not
   built yet — one provider does not need a registry, and adding one the day a second provider
   exists is a smaller change than guessing its shape now.
2. If a door is found, wrap it in a `DoorInteractable` and hold it as "current."
3. In `OnGUI`, if there is a current interactable, project `Camera.main.WorldToScreenPoint` on its
   `Position`; if it's in front of the camera and on-screen, draw one `GUI.Button` per verb,
   stacked, near the projected point. A click calls `Perform` and re-evaluates immediately (so
   Open→Close relabels the same frame, not next frame).

No new key bindings. No new input system code. Clicking a button is the same mouse interaction
`VillageUI`'s own panel already uses.

## Testing

This is entirely Unity-side — no Core change, nothing `dotnet test` can see. Two different
things need two different kinds of proof:

- **`CityDoors.Force`'s override timing is exactly the kind of logic PlayMode can pin down without
  a human watching:** a PlayMode test builds a door, calls `Force(i, false)` while a stand-in
  "person" position is held within `Hold` range, and asserts the door's angle stays at `_shut[i]`
  for the whole override window — proving the override actually beats proximity — then asserts
  proximity resumes control once the window elapses. A second case calls `Force(i, true)` mid-
  override and asserts the door starts opening immediately.
- **The menu itself — screen placement, button legibility, does it feel right to click** — is not
  provable from source. Build it, press P, walk up to a door, look at it. Same rule this whole
  project runs on for anything the eye has to judge.
