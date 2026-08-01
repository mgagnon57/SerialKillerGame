# Terrain Pipeline: Multi-Agent Orchestration Design — CONTINUATION

**Date:** 2026-08-01  
**Project:** Serial Killer Game (Noir detective/investigation game)  
**Current State:** Rossville terrain with USGS NED elevation (30m sampling, just resampled 2026-08-01)

## What's Already Done

✅ **Phase 1 (Geodata):** Real USGS elevation data loaded via `Content/elevation.txt` (71×81 samples, 30m spacing)  
✅ **Phase 2 (Terrain Gen):** Countryside system generates LOD'd terrain meshes; city/farm systems place features  
✅ **Base playability:** 960×960 map, 112+ people, traffic, buildings

## Remaining Work: The Four Question Marks

This workflow tackles the four remaining gaps when you say "do this":

1. **Detail/Refinement** — In-editor sculpt/paint tools to fix specific terrain spots without resampling
2. **Texturing** — Ground materials (grass, dirt, rock, water) that match actual terrain type
3. **Features** — Rivers, landmarks, road accuracy verification
4. **Performance** — Terrain running at 60fps with acceptable draw calls (baseline measurement + optimization)

These run in parallel phases with fast-model iteration + expensive-model verification (same Approach 3 as before).

## Design Approach: Single Pipeline with Review Gates

**Core Strategy:** Fast models iterate and write code for each workstream; expensive models verify correctness before moving on. Parallelism via review gates—expensive model can review Stream N while fast model starts Stream N+1.

### Work Decomposition: Four Workstreams (Parallel)

| Workstream | Fast Agent Role | Task | Expensive Agent Role | Verification Gate |
|-------|-----------------|------|----------------------|-------------------|
| **Stream 1: Sculpt/Paint** | Implementer | In-editor brush tool to paint height delta on terrain without resampling base data; undo/redo; real-time preview in game view; data persistence | Verifier | Tool responsiveness (no frame drops while painting), undo correctness, height-delta isolation from base elevation grid, integration with ElevationGrid queries |
| **Stream 2: Texturing** | Implementer | Ground material system: detect terrain slope/elevation to pick grass/dirt/rock/water; blend textures; apply to Countryside/ground meshes; realistic appearance | Verifier | Visual match to real-world terrain types; performance baseline; seamless blending across LOD transitions; no texture swim or UV stretch |
| **Stream 3: Features** | Implementer | Identify + implement major features (rivers, landmarks, road overlay accuracy); verify against real map; author as overlays or mesh deformations | Verifier | Feature placement correctness vs. real geography; visual accuracy; integration with existing terrain without hard-breaking |
| **Stream 4: Performance** | Implementer | Baseline current performance (FPS, draw calls, memory); profile terrain systems; propose + implement optimization (mesh reduction, occlusion, LOD tweaks) | Verifier | 60fps baseline achieved; draw call reduction measured; no visual regression; memory footprint within target |

### Model Allocation Strategy

**Fast Model** (Haiku or Claude Opus, prioritize speed & cost):
- Writes working, tested C# code
- Prompt shape: "Here's the task. Write complete, tested C# code. Use existing Unity APIs. No TODOs."
- Goal: Ship working code fast; iterate on fixes
- Budget: Unconstrained iteration (speed + cost efficiency primary)

**Expensive Model** (Claude Opus, prioritize correctness & depth):
- Reviews code for correctness, integration, edge cases
- Prompt shape: "Review this implementation. Check for: coordinate transform correctness, edge cases, performance, integration risk, what's missing. Propose fixes."
- Goal: Catch issues before they cascade; design robustness
- Triggered: After each phase's working implementation
- Feedback loop: Finds issues → Fast model fixes → Expensive re-verifies

### Detailed Per-Workstream Workflow

All four workstreams run in parallel. Each follows the same loop:

```
START WORKSTREAM N
│
├─ [1] Fast Model: Implement
│       Prompt: "Build a C# solution for [Workstream N task].
│                Write complete, tested code.
│                Integrate with existing systems (ElevationGrid, Countryside, etc.).
│                No TODOs or placeholders."
│       Output: Code files, unit tests, integration guide
│
├─ [2] You: Test locally
│       Run the code in Unity editor
│       ├─ Works? → Go to [3]
│       └─ Broken? → Report error to Fast Model
│           Fast Model fixes → Loop back to [2]
│
├─ [3] Expensive Model: Code Review
│       Prompt: "Review this [Workstream N] implementation.
│                Check:
│                - Correctness vs. success criteria
│                - Performance implications
│                - Integration with other workstreams
│                - Edge cases & robustness
│                - What's fragile or missing?
│                Suggest fixes."
│       Output: Review with findings + recommendations
│
├─ [4] Fast Model: Address Findings
│       Prompt: "Fix these issues: [list].
│                Keep the working core, harden the edges."
│       Output: Updated code
│
├─ [5] You: Spot-check fixes
│       Quick test that fixes work & nothing broke
│
├─ [6] Expensive Model: Verification Pass
│       Prompt: "Verify fixes are sound. Check no regressions."
│       Output: Sign-off or more findings
│
└─ Workstream approved → Ready for integration
```

**Parallelism:** While you're testing Stream 1, Expensive can review Stream 1, Fast can implement Stream 2, and Expensive can review Stream 2. All four streams advance independently.

### Data Flow & Integration Points

**Phase 1 → Phase 2:**
- Geodata Loaders produce `TerrainData` class (elevation grid, lat/lon coordinates, metadata)
- Phase 2 consumes `TerrainData` to generate Unity `Terrain` meshes
- **Integration risk:** Coordinate transform (lat/lon → world space) must be identical in both phases; document the transform formula

**Phase 2 → Phase 3:**
- Terrain Generation produces Unity `Terrain` component + mesh data
- Phase 3 reads the terrain and adds sculpt/paint capability
- **Integration risk:** Undo/redo must preserve both generated (immutable) and sculpted (mutable) data layers

### Review Gate Sign-Off Criteria

**Stream 1: Sculpt/Paint Tool**
- ✓ Brush is responsive (no frame drops, paints in real-time)
- ✓ Painted deltas persist on save and reload
- ✓ Undo/redo works correctly (sculpt changes revert; base elevation grid unchanged)
- ✓ Integrates cleanly with ElevationGrid (queries return base + delta)
- ✓ No crashes at terrain boundaries or with rapid undo spam

**Stream 2: Texturing**
- ✓ Ground materials applied based on slope/elevation (grass on gentle, rock on steep, water in low spots)
- ✓ Visual match to real Rossville terrain appearance
- ✓ Textures blend smoothly across LOD transitions (no visible seams at Countryside edges)
- ✓ No texture swim or UV stretch under camera movement
- ✓ Performance baseline met (no draw call spike from texturing system)

**Stream 3: Features**
- ✓ Major features (rivers, landmarks) identified and implemented
- ✓ Feature placement matches real geography (spot-check vs. satellite imagery)
- ✓ Integration with terrain is clean (no hard breaks in mesh or painting)
- ✓ Features visible at gameplay scales (game camera perspective, not top-down only)

**Stream 4: Performance**
- ✓ Current baseline established (FPS, draw calls, memory on target hardware)
- ✓ 60fps achieved with full terrain/city loaded
- ✓ Draw calls reduced by at least 10% (or target met if already optimal)
- ✓ No visual regression from optimization (shadows, LOD, occlusion working correctly)

If any criterion fails → Fast model fixes → Expensive re-reviews → repeat until sign-off.

## Implementation Timeline

All four workstreams run in parallel:

- **Stream 1 (Sculpt/Paint):** Fast writes brush tool (~6-8 hrs), Expensive reviews, fixes in parallel
- **Stream 2 (Texturing):** Fast writes material system (~4-6 hrs), happens while Stream 1 is testing
- **Stream 3 (Features):** Fast implements rivers/landmarks (~4-6 hrs), happens while Streams 1-2 review
- **Stream 4 (Performance):** Fast profiles + optimizes (~3-4 hrs), happens in parallel

**Estimated total:** 1-2 evenings of focused 20X budget work (four agents running in parallel means you don't hit sequential bottlenecks)

## Success Checkpoint

When all four workstreams are approved by the Expensive model and you can:
1. ✓ Paint terrain refinements in the editor
2. ✓ See realistic ground texturing (grass/dirt/rock matching actual terrain)
3. ✓ See rivers and landmarks in the right places
4. ✓ Run at 60fps with optimal draw calls

...the terrain pipeline is **done**. You can then say "the terrain is accurate" and move on to gameplay/investigation layer.

## Data Flow & Integration

**ElevationGrid ← Sculpt Tool ← Texturing:**
- Base elevation from `Content/elevation.txt` (USGS NED, 71×81 at 30m)
- Sculpt tool adds delta layer (separate data, persisted independently)
- Height queries: `ElevationGrid.HeightAt(x, y)` returns base + delta
- Texturing system reads combined height for material selection

**Countryside + City + Farm ← Texturing:**
- Existing mesh systems respect texture overlay (don't replace, blend)
- Material assignment by elevation slope (existing terrain analysis can be reused)

**All systems ← Feature overlays:**
- Rivers/landmarks implemented as separate systems (not replacing base terrain)
- Can be toggled/debugged independently

## Notes & Constraints

- **Existing coordinate system:** Origin locked at Chicago St × Attica St (750, 1335). Do NOT change.
- **Resampling:** Elevation just went 60m → 30m (2026-08-01). Matches real USGS 10m source well enough; do not resample again without reason.
- **Integration risk:** Texturing system must respect LOD transitions in Countryside. Test visual seams at distance LOD boundaries (120m, 190m, 380m).
- **Playability:** Aim to have Stream 1 (sculpt tool) working first, so you can refine terrain interactively while testing Streams 2-4.
