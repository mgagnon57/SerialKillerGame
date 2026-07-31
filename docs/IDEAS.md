# Ideas

Things to look at later. Nothing here is a commitment and nothing here has been started.

Captured with `/idea <thing>` — optionally prefixed with a category, e.g.
`/idea road: the freeway should have on-ramps rather than crossings`. Tick a box when it is
done or delete the line when it turns out to be a bad idea.

## Env

## Roads

## Traffic

- [ ] Vehicle look-ahead is a constant 8m, tuned when the longest thing on the road was a hatchback. It should come from the vehicle's own measured length, so an articulated lorry keeps a lorry's distance. — *2026-07-30*
- [ ] No colliders on any vehicle: `CityTraffic` avoids by RULES (signals, give-way, look-ahead box), never by intersection test, so where a rule has no case cars pass through each other. Probably right for AI-vs-AI; needs revisiting the moment the player can drive. — *2026-07-30*
- [ ] Jams appeared after the fleet went 97 -> 243. Suspect the give-way gap check starves a minor-road car forever once traffic is dense, blocking everyone behind it. — *2026-07-30*

## City

- [ ] Downtown block interiors are flat paving. Real blocks have rear yards, parking and low buildings in the middle. — *2026-07-30*

## People

## Story

- [ ] Deduction as recipes: a corkboard where pinning evidence in a *shape* produces a lead. The Crafting System's `TableRecipe` is already position-aware rather than only contents-aware, and `ISatisfier` is the "do these inputs match this pattern" abstraction. Build it in Core against `particulars.txt`. — *2026-07-30*

## Tech

- [ ] Lift the Crafting System's UGUI inventory UI — drag-drop slots, transfer, tabs — rather than writing one. Tedious to build, and presentation belongs in Unity anyway. — *2026-07-30*
- [ ] Evidence catalogue as `Content/items.txt` in the shape of `kinds.txt`, read by Core. NOT the Crafting System's ScriptableObjects: content authored in an editor window is content `MapAudit` and the PlayMode tests cannot see. — *2026-07-30*

## Ad hoc
