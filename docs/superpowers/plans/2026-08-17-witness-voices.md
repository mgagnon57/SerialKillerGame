# Witness Voices Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The street visibly reacts to a case (floating one-liners over the people the
response path already knows about), and the T-key ask becomes aimed and role-aware.

**Architecture:** One new display-only MonoBehaviour (StreetVoices); one-line hooks beside
existing log lines in VillageHost; VillageUI gains the badge toggle, aimed targeting and
civilian thinning. No Core changes, no RNG, no new input surfaces beyond the B key.

**Tech Stack:** Unity 6000.3.20f1, IMGUI (display only), Keyboard.current.

**Spec:** `docs/superpowers/specs/2026-08-17-witness-voices-design.md`

## Global Constraints

- The response path consumes NO RNG — bubble variants key on `CitizenId.Value % n`.
- IMGUI is display-only; input is `Keyboard.current` (PlayerInteraction.cs header).
- The witness layer's vagueness is untouched — thinning only removes lines.
- Verify with `dotnet build Noir.Unity.csproj -c Debug`; the PlayMode gate is additive-safe
  and runs at the next scheduled gate, not per-task.

### Task 1: StreetVoices component

**Files:** Create `Assets/Noir/Unity/StreetVoices.cs`

- [x] `Create(VillageHost, Transform)` factory; ring buffer of (CitizenId, line, until);
  `Say()` replaces an existing bubble for the same citizen; OnGUI draws ≤8 labels,
  ≤80 m from `Camera.main`, above the head at `Space3D.ToWorld(agent.Position, 2.1f)`,
  alpha fade in the final second; batch-mode and no-camera guards.
- [x] Build clean.

### Task 2: host hooks

**Files:** Modify `Assets/Noir/Unity/VillageHost.cs` (BodySeen site, gawker drift site,
OfficerArrived site, CanvassNext arm; create the component in Build)

- [x] One `Say` beside each existing narration point, fatal-aware where the discoverer is
  concerned. Build clean.

### Task 3: badge toggle + aimed, thinned ask

**Files:** Modify `Assets/Noir/Unity/VillageUI.cs`, `docs/CONTROLS.md`

- [x] `VillageHost.Badge` bool; B key beside the T handling; role shown in the top bar and
  on H. CONTROLS.md row.
- [x] `Ask()` targets by facing among candidates ≤6 m when walking (fallbacks per spec);
  header names the role; badge-off shows last 2 lines + the hedge. Build clean.

### Task 4: land it

- [x] `dotnet build` all three Unity csproj; commit; push. PlayMode gate at next editor-closed
  window; live look at the next case (the real acceptance test is the street talking).
