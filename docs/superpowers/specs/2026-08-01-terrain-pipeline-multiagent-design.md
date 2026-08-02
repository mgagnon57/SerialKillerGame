# Terrain & Parcel Plotting Pipeline — Multi-Agent Orchestration Design

**Date:** 2026-08-01  
**Project:** Serial Killer Game (Noir detective/investigation game)  
**Current State:** Real Rossville geography — USGS elevation + county parcel data, rotation aligned 2026-08-01

## What's Already Done

✅ **Phase 1 (Geodata):** Real USGS NED elevation loaded via `Content/elevation.txt` (71×81 samples, 30m spacing)  
✅ **Phase 2 (Terrain Gen):** Countryside system generates LOD'd terrain meshes; city/farm systems place features  
✅ **Phase 3 (Parcel Data):** Real 794 cadastral parcels from Vermilion County (ParcelIndex.cs loaded and queryable)  
✅ **Phase 3a (Parcel Alignment):** Just rotated +1.81° (2026-08-01) to align lot lines with street grid  
✅ **Phase 3b (Parcel Annotation System):** ParcelNotes.cs with zoning, housing type, quality, occupants framework  
✅ **Base playability:** 2100×2400 map, 477 places, traffic, buildings *(the "960×960, 112 people" in earlier drafts of this line was stale)*

### Stream 1 delivered, 2026-08-01 (commits 33941d7, 3d4a685, 55d3c0f, 81519aa)
✅ **County record for 776 of 794 parcels** — `Content/parcel-county.txt`: address, PIN, assessor class code and wording, owner-occupancy, over-65 exemption, homesite/dwelling assessed value, acreage. Matched by Hungarian assignment: median 4.4m, p90 6.5m, worst 45.9m.  
✅ **Zoning derived for every matched parcel** from real class codes — residential 524, vacant 118, commercial 58, agricultural 16. Class `0050` ("Commercial >6 Units") identifies the town's apartment buildings and seeds `ApartmentComplex`.  
✅ **1991 households on every built residential lot** — `Households.cs`, deterministic from parcel id, stored nowhere. 522 households, 1,299 people against the real village's 1,331.  
✅ **Editor** — zoning, housing type, condition, bedrooms, baths, half-baths, square feet, year built, all live in a centred two-column panel, with entered area sanity-checked in $/sqft against the county's assessed value.  
✅ **names.txt retuned** for eastern Illinois in 1991, with child-name cohorts finally wired into `NameTable`.

**Remaining parcel work is authoring, not research:** per-lot character notes and house detail on top of the generated households. It does not need an agent stream.

## Remaining Work: Parcel Annotation + Terrain Polish

This workflow tackles the four remaining gaps when you say "do this":

1. **Parcel Annotation** — Fill in parcel-notes.txt with zoning, housing type, condition, occupants, history (uses real public records as reference)
2. **Detail/Refinement** — In-editor sculpt/paint tools to fix specific terrain spots
3. **Texturing** — Ground materials matching terrain and parcel usage (residential grass, industrial dirt, etc.)
4. **Performance** — Terrain + parcels running at 60fps with acceptable draw calls

These run in parallel phases with fast-model iteration + expensive-model verification (same Approach 3 as before).

## Design Approach: Single Pipeline with Review Gates

**Core Strategy:** Fast models iterate and write code for each workstream; expensive models verify correctness before moving on. Parallelism via review gates—expensive model can review Stream N while fast model starts Stream N+1.

### Work Decomposition: Four Workstreams (Parallel)

| Workstream | Fast Agent Role | Task | Expensive Agent Role | Verification Gate |
|-------|-----------------|------|----------------------|-------------------|
| **Stream 1: Parcel Annotation** | Implementer | Research Rossville public records (county assessor, property history, local knowledge); fill parcel-notes.txt with zoning, housing type, condition, occupant names, character notes for each parcel. Use ParcelNotes schema (Zoning enum, HousingType, Quality, Adults/Kids, Names, Character field). Reference real data sources for plausibility. | Verifier | Zoning matches county records; housing types are plausible for era/location; occupant counts align with parcel size; character notes are grounded in community research; no unfounded speculation |
| **Stream 2: Sculpt/Paint** | Implementer | In-editor brush tool to paint height delta on terrain without resampling base data; undo/redo; real-time preview in game view; data persistence | Verifier | Tool responsiveness (no frame drops while painting), undo correctness, height-delta isolation from base elevation grid, integration with ElevationGrid queries |
| **Stream 3: Texturing** | Implementer | Ground material system: detect terrain slope/elevation + parcel zoning to pick grass/dirt/rock/water; blend textures; apply to Countryside/ground meshes; realistic appearance | Verifier | Visual match to real-world terrain types; residential parcels show maintained grass, industrial show dirt, etc.; seamless blending across LOD transitions; no texture swim or UV stretch |
| **Stream 4: Performance** | Implementer | Baseline current performance (FPS, draw calls, memory) with terrain + parcels loaded; profile systems; propose + implement optimization (mesh reduction, occlusion, LOD tweaks) | Verifier | 60fps baseline achieved; draw call reduction measured; no visual regression; memory footprint within target |

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

**Stream 1: Parcel Annotation**
- ✓ Zoning values match county assessor records (spot-check 20+ parcels against public GIS)
- ✓ Housing types are historically plausible (no Victorian mansions on industrial lots, etc.)
- ✓ Condition/Quality matches what would be visible in streetview or property histories
- ✓ Adult/Kid counts are proportional to parcel size (small lot ≠ large family)
- ✓ Character notes grounded in research (not invented; references exist)
- ✓ parcel-notes.txt is well-formed, parseable, no schema violations

**Stream 2: Sculpt/Paint Tool**
- ✓ Brush is responsive (no frame drops, paints in real-time)
- ✓ Painted deltas persist on save and reload
- ✓ Undo/redo works correctly (sculpt changes revert; base elevation grid unchanged)
- ✓ Integrates cleanly with ElevationGrid (queries return base + delta)
- ✓ No crashes at terrain boundaries or with rapid undo spam

**Stream 3: Texturing**
- ✓ Ground materials applied based on slope/elevation + parcel zoning (residential=grass, industrial=dirt, low=water)
- ✓ Visual match to real Rossville terrain appearance
- ✓ Textures blend smoothly across LOD transitions (no visible seams at Countryside edges)
- ✓ Parcel boundaries visible from texture/material transitions (zoning is readable)
- ✓ No texture swim or UV stretch under camera movement
- ✓ Performance baseline met (no draw call spike from texturing system)

**Stream 4: Performance**
- ✓ Current baseline established (FPS, draw calls, memory on target hardware with full terrain + 794 parcels)
- ✓ 60fps achieved with full terrain/city/parcels loaded
- ✓ Draw calls reduced by at least 10% from baseline (or target met if already optimal)
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

### Critical — Do NOT Redo This Work
- **Parcel data:** 794 cadastral parcels from county records, already loaded, queryable, and rotation-aligned (2026-08-01). Do NOT regenerate, resample, or re-rotate.
- **Elevation data:** USGS NED, 30m sampling (just moved from 60m). Stable and accurate. Do NOT re-sample without clear reason.
- **Parcel Index/Notes systems:** ParcelIndex.cs and ParcelNotes.cs already built and working. Do NOT refactor or rewrite.

### Coordinate Systems (Locked)
- **Origin:** Chicago St × Attica St (750, 1335) — all systems reference this
- **Parcels:** Rotated +1.81° to align lot lines with street grid (2026-08-01)
- **Elevation:** Bilinear-sampled 30m grid, baseline relative to crossing
- **All three must stay in sync** — any parcel query, elevation query, or texture lookup uses the same origin and rotation

### Stream 1 Research Foundation — SUPERSEDED, see "No real residents" below
- **Vermilion County Assessor:** gis.cityofdanville.org/arcgis/rest/services/Property/Property (source of parcel data, and now of class codes, occupancy, over-65 and assessed values — see `Content/parcel-county.txt`)
- **What the county does NOT publish:** no square footage, bedroom, bathroom or year-built data anywhere online. Its tax card (devnetwedge) carries billing and assessment only, and its earliest year is 2007. Do not go looking again; this was checked on 2026-08-01.
- **1991 records:** not obtainable. The portal starts at 2007; older rolls are paper at the courthouse.

### NO REAL RESIDENTS — standing constraint, applies to every stream
The assessor publishes the current owner of every parcel by name, and flags which owners are
over 65 and which are owner-occupied. **No agent, stream or task may put a real person's name —
or a recognisable variant of one — on a real address in this project.**

Rossville is a real town of about 1,300 real people and this is a game about somebody murdering
its residents. Real addresses are geography and are fine. Real names at those addresses would
make identifiable living people into the victims of a murder simulation without their knowledge,
and a deliberately near-miss pseudonym ("Judy Rayford" for "Judith Rayfield") is worse rather
than better: it keeps the person identifiable while removing their ability to say it is not them.

An earlier draft of this document asked Stream 1 to research "occupant names" from public records
and gated it on being "grounded in community research". That instruction is withdrawn. Households
are GENERATED — see `Households.cs` — with their shape taken from the county's real record
(owner-occupied vs absentee, over-65, assessed dwelling value) and their people drawn from
`Content/names.txt`. The town's demography is genuinely Rossville's; nobody in it is a real person.

Regional recognisability is achieved instead through the surname pool, which carries the town's
own street-name families (Henderson, Stufflebeam, McKibben, Holmes, Benton, Gilbert, Stewart,
Watson, Greenwood), assigned to lots by seed rather than by record.

### Integration Points
- Texturing system reads parcel zoning (via ParcelNotes) to pick materials
- Countryside LOD boundaries at 120m, 190m, 380m — textures must be seamless across them
- Parcel boundaries should be visually readable (texture/material change at lot lines)

### Launch order — REVISED 2026-08-01, 10PM
- **Stream 1 (Parcel Annotation) is COMPLETE and is not to be launched.** See "Stream 1 delivered" above. Zoning is derived for all 776 matched parcels from the county's own class codes, and households are generated for every built residential lot. Relaunching it would redo committed work and, on the original brief, would have gone looking for real residents' names.
- **Stream 2 (Sculpt/Paint Tool)** starts first. It is the only remaining blocker on interactive terrain refinement.
- **Streams 3 (Texturing) and 4 (Performance)** run in parallel after Stream 2, and read parcel zoning through `ParcelNotes` / `CountyRecord` — both already populated, so neither waits on annotation.
