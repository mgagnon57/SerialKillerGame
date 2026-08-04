# What we already own, what is missing, and what to buy

Researched 2026-08-03. **Prices are as found and Unity discounts constantly — re-check before
buying.**

`PACK.md` says *"Check this file before building, generating or buying anything."* This document
does that by **listing the folders rather than trusting the summary**, and the summary turns out to
be wrong in both directions: things we assumed missing are here, and the biggest folder is not what
it looks like.

**The headline: almost nothing needs buying. The pack was audited when this was an English village
called Northgate with no calendar, and a third of it was written off on genre grounds that no longer
apply.**

---

## Part 1 — The re-audit: the genre labels are marketing, not content

`PACK.md` files **1,374 Fantasy, 479 Steampunk, 105 Primeval and 458 seasonal** prefabs under
*"Wrong genre for Northgate."* That judgement was made against a different town and, crucially,
**before the simulation had a calendar**. Both premises have changed.

Opening the folders shows the labels describe which *tab of the store* the content shipped in, not
what the models are.

### Steampunk is largely Victorian, and this town sells Victorian for a living

Rossville is **the Antique Capital of Illinois** — that is its identity and a real part of its
economy at the 1991 opening (`THE-TRAJECTORY.md`). Its shops are full of Victorian and
early-twentieth-century furniture. And:

| folder | n | what is actually in it |
|---|---:|---|
| **`Steampunk/Furniture`** | **120** | `Chest_Of_Drawers_Victorian`, `Chest_Victorian`, `Chandelier_Victorian_Lit/Unlit`, `Desk_Kneehole`, `Cabinet_Specimen`, `Book_Stack`, `Bottle_Ink`. **This is antique-shop stock and older-house furniture.** Barely a gear in sight |
| **`Steampunk/Signboard`** | **21** | `Signboard_Bank`, `_Barber`, `_Bakery`, `_Apothecary`, `_Doctor`, `_Books`, `_Mechanic`, `_Key`, `_Fish`. **These are the documented trades of the 1913 downtown**, as hanging shop signs — and the 2007 photograph shows hanging signs on those very storefronts |
| **`Steampunk/Bathroom`** | **19** | `Bathtub_Victorian`, `Sink_Victorian`, `Sitz_Bath_Victorian`, `Toilet_Holder_Paper_Victorian`. **This is the one bathroom in a pre-1950 house**, exactly as `INSIDE-THE-HOUSES.md` describes it |
| **`Steampunk/Trophies`** | **22** | `Trophy_Deer`, `_Coyote`, `_Fox`, `_Boar`, `_Bison`, `_Fish_Trout`. **Mounted deer heads are deeply rural Illinois** — farmhouse, tavern, Legion hall |
| `Steampunk/Library` | 47 | bookshelves and books — antique shops, parlours |
| `Steampunk/Pipes` | 45 | industrial pipework — grain elevator, boiler room, basement |

### Fantasy holds a farmyard and a main street under a medieval label

| folder | n | what is actually in it |
|---|---:|---|
| **`Fantasy/Wood`** | **21** | `Sawhorse`, `Wood_Logs_Stacked`, `Wood_Pile`, `Axe_Chopping`, `Saw_Frame`, `Wood_Axe_Stump`. **Pure farmyard.** Only the filename suffix is fantasy |
| **`Fantasy/Butcher`** | **25** | `Beef_Chunk_Hanging`, `Chicken_Hanging`, `Cutting_Board_Bacon_Sliced`, `Leg_Pork_Sliced_Hanging`, `Pig_Head`. Farm butchering — and the downtown had a **meat market** |
| **`Fantasy/Smith`** | **30** | `Anvil`, `Anvil_Stump`, `Bellows`, `Forge_Mobile_Wooden`, `Grind_Stone`, `Hammer_Blacksmith`. The 1913 sheet labels a **blacksmith** downtown, and every farm has a shop |
| **`Fantasy/Vegetable`** | **19** | `Basket_Apples`, `Crate_Slanted_Tomato_Carrot`, `Crate_Small_Slanted_Corn_Leek`, display crates. **The produce stand**, or a grocer |
| `Fantasy/Cooking` | 87 | pots, pans, utensils — a generic kitchen |
| `Fantasy/Furniture` | 107 | mixed. Baths and benches usable; battlemaps and figurines not |

**Genuinely unusable and correctly dismissed:** Fantasy `Buildings` (125), `Tents` (102),
`Palisade` (67), `Statues` (53), `Battlefield` (35), `Siege` (20), `Castle` (17), `Portals` (5), most
of `Prison` (26 — though `Door_Bars` and `Handcuffs` survive, and the historical society museum has a
**two-cell jail**), and all of **Primeval** (105).

### The seasonal packs were written off for a reason that has expired

458 prefabs across Christmas, Easter, Halloween, 4 July and Valentine were cut as "wrong genre."
**But `GameClock` now has `Year`, `Month` and `DayOfYear`, and `Fields` already drives the crops off
it.** A town that decorates for the holidays is now expressible, and it was not before.

| pack | n | verdict |
|---|---:|---|
| **`4July`** | **80** | **Direct hit.** `Balloon_Small_Flag_American`, `Bench_Garden_Long`, `Bottle_Beer`, `Burger`, **`Corn_Slice_Grilled`**, plastic cups, cupcakes. A small-town American Fourth — and the welcome-sign photograph shows **US flags on short poles downtown** |
| **`Christmas`** | **120** | **Directly attested for this town.** The Rossville Community Organization runs *"Christmas in the Village"* at Christman Park with a **drive-through lighted display** (`rossville-buildings.md`). Also: every candle ships `Lit` and `Unlit`, which the lit-window system can use |
| **`Halloween`** | **80** | Pumpkins and candy, in the month `WHO-SEES-WHOM.md` calls the darkest and most concealing of the year |
| `Easter` | 108 | spring baskets and eggs; modest but real |
| `Valentine` | 71 | weakest — but the champagne, perfume and candle sets are generic props under a pink label |

**And a pattern across all five:** a large share of every seasonal pack is **candles in
`Lit`/`Unlit`/`Used` variants**, plus generic food, bottles, cups, benches and chairs. Those are
neither seasonal nor era-specific.

> **Decorations are era-gated content, and that is the technology plan's mechanism.** Christmas
> lights in 1991 are not the lights of 2005 — incandescent strings give way to LEDs and inflatable
> lawn figures appear in the early 2000s. The same `Era` curve being built for `technology.txt`
> should drive seasonal dressing. **One mechanism, two uses.**

### A trap worth writing down

These prefabs carry `_Fantasy` and `_Steampunk` **in their filenames** — `Wood_Pile_A_Fantasy` is a
woodpile. Any curated selector will look odd in code, and `PACK.md`'s existing warning applies
doubly: `AssetDatabase.FindAssets` recurses, so asking `Steampunk` for furniture also returns
`Alchemy` and `Parts`. **Curate by explicit name list, as `CityGreenery.All` already does.**

---

## Part 2 — Three corrections to what we thought we had

**1. Corn already has four growth stages.** The `Fields` commit says *"the pack ships only ripe field
corn, so five of the six states have no model."* That is wrong:

```
Corn_Sprout_A · Corn_Seedling_A · Corn_Young_A
Corn_Ripe_Fieldcorn_A / _B / _C · Corn_Ripe_Sweetcorn_A
```

**Four of the six `Fields` states have a model already.** Correct the comment before anyone buys corn.

**2. The pack already ships tileable field tiles — for wheat.**

```
Wheat_Sprout_Square_1x1m_A · Wheat_Seedling_Square_1x1m_A · Wheat_Mature_Square_1x1m_A
Wheat_Ripening_Square_1x1m_A · Wheat_Harvested_Square_1x1m_A
```

Five stages as **1 m² tiles**, including a harvested one — the stubble state. That is the technique a
field system wants and it is already owned. **Corn has no tile equivalent; that asymmetry is the real
gap, not "no corn stages."**

**3. `Modular Parts` is not the house kit it looks like.** `PACK.md` lists **421 walls, "the biggest
single folder in the pack."** Counted properly, the modern usable set is **35 walls, 4 City windows,
4 City doors** — the 421 is recursive and dominated by Fantasy, Steampunk and Survival variants.
**Do not plan a procedural house kit around it.** The `Roof_Regular` 1 m² modular roof system *is*
good and should be kept in mind.

---

## Part 3 — What is genuinely missing

| gap | severity | note |
|---|---|---|
| **Soybeans — none at all** | highest, and unbuyable | half the Illinois rotation |
| **An American house for the town** | **highest benefit** | `ShowBuildings` is *switched off* because "the pack has two house families and both are Chicago brownstones" |
| Corn field tiles | high | corn is individual plants; wheat has the tiles |
| American barn & farmhouse | medium | only `Barn_Farm_British`, `Barn_Farm_Scandinavian`, `House_Farm_British/Scandinavian` |
| Maple / ash / hackberry | medium | trees are Oak, Poplar, Willow, Birch, Pine, Spruce + tropicals. `THE-YEAR.md` wants silver maple, hackberry, honey locust, living ash |
| Osage orange hedgerow | low | check `Nature/Hedges` and `Modular Parts/Fences` (105) first |

**Already owned and correct — do not re-buy:** `Bin_Grain_New/Old/Tall`, `Silo_Grain_New/Old`,
`Tower_Water_Farm`, and **`Windmill_Pump`**, which is the American Aermotor-style farm windmill and
exactly right. (`Mill_Netherland` is not.)

---

## Part 4 — The constraint that decides any purchase: style

polyperfect is **low-poly stylized on a shared atlas** — 6,142 meshes across 297 textures, which is
why the chunker gets 13,000 renderers down to 24 materials. **Anything bought must match that or it
reads as pasted in, however good it is alone.** That rules out the photoreal route entirely —
Megascans, HDRP vegetation — which would look better in isolation, worse in place, and would wreck
the batching that makes a 2.1 × 2.4 km map affordable.

The risk with any purchase is not quality. It is **palette and shading**.

---

## Part 5 — The market

| pack | price | verdict |
|---|---|---|
| **Stylized Farm Crops — Ultimate Low Poly** (NewLua) | **$7** | 15 crops × 5 stages, **grid-tileable**, **3 plowed soil plots**. **Buy.** Not a decision at $7 — the soil plots alone cover the *tilled* state. No soy |
| **POLYGON Town Pack** (Synty) | **$49.99** | 125 buildings + a **modular** house kit, 412 props. **The highest-benefit purchase available** — see below |
| **POLYGON Farm Pack** (Synty) | **$49.99** | 3 barns, 2 farmhouses, silos, water tower, 173 plants, 11 vehicles. Solves the American barn. Duplicates silos/water tower we own. No soy |
| Synty subscription | **$30/mo** | 130+ packs. Cheaper than two packs *if* you keep what you download — **I could not confirm the retention terms** |
| Poly Universal Pack | now **$300** | what we own, bought at ~$30. **It grows with free updates** — check the changelog before spending |
| CGTrader "soybean" | varies | 71 hits, but scientific/render meshes, not field tiles |

---

## The recommendation

### 1. Mine what we own before buying anything

The re-audit in Part 1 is worth more than any purchase in this document. Roughly **400–500
genuinely usable prefabs** are sitting in folders marked wrong-genre: Victorian furniture for the
antique shops, trade signboards for the documented storefronts, a Victorian bathroom, mounted deer,
a woodpile and sawhorse, hanging meat, an anvil and bellows, produce crates — and 458 seasonal props
that a calendar can now actually drive.

### 2. Buy the $7 crop pack

Five stages, grid-tileable, three plowed soil textures. Worth it as reference alone.

### 3. The highest-benefit purchase is not terrain — it is the town houses

`VillageHost.ShowBuildings` is **off**, and the comment says why:

> *"The pack has two house families and both are Chicago brownstones; a Rossville street built from
> them is not a near miss."*

**The town cannot currently be shown at all** — buildings, streets, greenery, farm, powerlines and
railroad are all behind that flag. That is a far bigger hole than any field.

POLYGON Town Pack, **$49.99**, is the candidate, and its *modular* kit matters because this project
**generates** houses from grammars (`FrameHouseGrammars.cs`) and wants parts, not presets.
**Check the screenshots against the frame-house description first** — white clapboard, L or T plan,
1–1½ storeys, wrap-around porch, 97 m² median footprint. If the kit cannot make that silhouette it
is the wrong $50.

### 4. Do not buy soybeans — and do not model them as prefabs

Nobody sells them. And it is the right answer anyway: the map is 2,100 × 2,400 m at a 5:1
country split, so **~4.2 km² of fields** — tens of millions of plants at any real density. Distant
fields must be ground shader or terrain detail with geometry only near the camera.

A bean field is also the easiest crop to fake: a **low, uniform, close canopy** that goes green →
gold → bare. That is a texture and a colour ramp, not a mesh.

> **The crop problem is ~80% rendering technique and ~20% assets.** No purchase fixes 4 km² of field.

### 5. Trees: probably do not buy

Poplar is defensible — cottonwood is a poplar and a genuine Illinois bottomland tree — and willow
belongs on the North Fork. Trees are the most style-sensitive thing to mix because they are always
in frame. Re-tinting and re-scaling oaks likely beats a species-accurate pack that clashes.

---

## Buying order

1. **$0** — mine the re-audit. Weeks of content, already paid for.
2. **$7** — Stylized Farm Crops.
3. **$49.99** — POLYGON Town, after checking screenshots. The one that could turn the town on.
4. **$49.99** — POLYGON Farm, later. Farms are background; the town is where the game happens.
5. **Nothing** for soybeans or trees.

**Total to unblock the biggest thing: about $57.**

## Before spending

- **Re-check the Poly Universal Pack changelog** — it grows by free update and went ~$30 → $300 by
  accretion. Crops or houses may already be queued.
- **Look at `Nature/Hedges` and `Modular Parts/Fences`** before buying a hedgerow.
- **Put a Synty asset beside a polyperfect one in one frame** before committing $50.
- **Revise `PACK.md`'s "Wrong genre" section.** It is now the most misleading page in the docs.
