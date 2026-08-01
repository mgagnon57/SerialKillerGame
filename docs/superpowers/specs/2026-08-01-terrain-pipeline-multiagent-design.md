# Terrain Pipeline: Multi-Agent Orchestration Design

**Date:** 2026-08-01  
**Project:** Serial Killer Game (Noir detective/investigation game)  
**Goal:** Build accurate hometown terrain with real geodata, visual accuracy, and in-editor refinement

## Success Criteria

The terrain pipeline is done when:

1. **Real geodata imported** — elevation and map data from actual sources (USGS, OpenStreetMap, etc.) loaded into the game
2. **Visually accurate to hometown** — terrain mesh matches real geography (hills, valleys, rivers, general shape)
3. **Editable in Unity** — sculpt/paint tools allow local refinement (streets, buildings, details)

Performance, texturing, and game mechanics support are lower priority and follow after this foundation is solid.

## Design Approach: Single Pipeline with Review Gates

**Core Strategy:** Fast models iterate and write code for each phase; expensive models verify correctness before moving on. Parallelism via review gates—expensive model can review Phase N while fast model starts Phase N+1.

### Work Decomposition: Three Phases

| Phase | Fast Agent Role | Task | Expensive Agent Role | Verification Gate |
|-------|-----------------|------|----------------------|-------------------|
| **Phase 1: Geodata Ingestion** | Implementer | Download/parse real elevation data for hometown; write C# loaders; handle coordinate transforms (lat/lon → Unity world space); basic error handling | Verifier | Review data format correctness, coordinate math (especially datum mismatches), edge cases (missing tiles, coordinate wrap), loader robustness |
| **Phase 2: Terrain Generation** | Implementer | Convert geodata to Unity terrain meshes; sampling strategy; LOD/chunking if needed; real-time visualization in editor | Verifier | Validate mesh accuracy vs. source data; check visual fidelity matches real geography; baseline performance; integration with Unity terrain system |
| **Phase 3: Editor Tools** | Implementer | Sculpt/paint for local refinement; undo/redo; brush sizes & falloffs; real-time preview; smooth integration with generated terrain | Verifier | Check tool responsiveness (no sculpt lag), data consistency, undo/redo correctness, integration with Phase 2 terrain, edge case handling (terrain boundaries, brush limits) |

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

### Detailed Per-Phase Workflow

```
START PHASE N
│
├─ [1] Fast Model: Implement
│       Prompt: "Build a C# solution for [Phase N task].
│                Write complete, tested code.
│                Use [existing APIs/libraries].
│                No TODOs or placeholders."
│       Output: Code files, unit tests, README
│
├─ [2] You: Test locally
│       Run the code in Unity editor or console
│       ├─ Works? → Go to [3]
│       └─ Broken? → Report error to Fast Model
│           Fast Model fixes → Loop back to [2]
│
├─ [3] Expensive Model: Code Review
│       Prompt: "Review this [Phase N] implementation.
│                Check:
│                - Correctness vs. success criteria
│                - Edge cases & error handling
│                - Integration with prior phases
│                - Performance implications
│                - What's missing or fragile?
│                Suggest fixes."
│       Output: Review with findings + recommendations
│
├─ [4] Fast Model: Address Findings
│       Prompt: "Fix these issues from review: [list].
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
└─ Phase approved → Move to Phase N+1
```

**Parallelism:** While you're testing Phase 1, Expensive can review Phase 1, and Fast can start Phase 2 scoping. No idle time.

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

**Phase 1: Geodata Ingestion**
- ✓ Coordinate math verified correct (spot-check 3+ points vs. real-world map)
- ✓ Data loads without crashes or missing tile errors
- ✓ Edge cases handled gracefully (wrapping, datum mismatches, incomplete tiles)
- ✓ Loaders have basic error messages for debugging

**Phase 2: Terrain Generation**
- ✓ Mesh elevation accuracy within 1-2 meters of source data (or your stated tolerance)
- ✓ Visual fidelity confirmed—terrain visually matches your hometown when viewed in-game
- ✓ Performance baseline met (define: e.g., "60 fps on local hardware," "under 5k draw calls")
- ✓ Integrates cleanly with Phase 1 output; no coordinate mismatches

**Phase 3: Editor Tools**
- ✓ Sculpt/paint tools are responsive (no frame drops while painting)
- ✓ Undo/redo preserves all data correctly (sculpted changes revert, generated base persists)
- ✓ Integrates seamlessly with Phase 2 terrain (brushes respect terrain boundaries)
- ✓ No edge-case crashes (brush at map edge, rapid undo spam, etc.)

If any criterion fails → Fast model fixes → Expensive re-reviews → repeat until sign-off.

## Implementation Timeline

- **Phase 1 (Geodata):** Fast writes loaders (~4-6 hrs of work), Expensive reviews, fixes
- **Phase 2 (Terrain Gen):** Starts while Phase 1 review is happening; Fast writes mesh gen, Expensive reviews
- **Phase 3 (Editor Tools):** Starts while Phase 2 review is happening; Fast writes sculpt/paint, Expensive reviews

**Estimated total:** 2-3 evenings of focused work (your 20X budget resets allow parallel review + implementation)

## Success Checkpoint

When all three phases are approved by the Expensive model and you can:
1. Load real geodata
2. See your hometown rendered in-game
3. Sculpt local details in the editor

...the pipeline is **done**. Texturing, performance optimization, and game-mechanic integration follow after.

## Notes

- **Coordinate systems:** Document your chosen coordinate transform (lat/lon/elevation → Unity world space) early. This is the #1 source of Phase 1→2 integration bugs.
- **Data source:** Decide on your geodata source early (USGS NED for elevation, OSM for map data, etc.). Fast model can help select based on accuracy + ease of parsing.
- **Playability:** Aim to have Phase 1 working by end of first evening so you can see terrain in-game, even if rough.
