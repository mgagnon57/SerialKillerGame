# What is in the Poly Universal Pack

**6,484 prefabs**, 764 MB, 6,142 meshes, 297 textures, 101 materials. Publisher polyperfect,
version 4.9, URP and HDRP. One atlas workflow, so almost everything shares a handful of
materials — which is why the chunker gets 13,000 renderers down to 1,700 with 24 materials.

Written because the useful things kept turning up one at a time. `City/Roads City` — a 48-piece
30m road kit with painted lanes and matching junctions — was found on the **second day**, after
the entire city had been built out of the generic 10m `Modular Parts/Roads`. Then 486 farm
prefabs. Then 174 survival props. Then a 13-piece rail kit of which five were in use.

**Check this file before building, generating or buying anything** — and read **`ASSET-GAPS.md`**
beside it, which re-audits the folders this one used to dismiss and says what is actually worth
buying. Between them the answer is usually *"we already own that."*

The Asset Store page is no help: its content list is loaded by script and comes back empty. The
filesystem is the authority, and this was generated from it.

---

## Used by the city today

| Folder | n | What it gives us |
|---|---:|---|
| `City/Roads City` | 48 | the 30m road kit — freeway/mainroad/street, junctions, crossings, lay-bys |
| `City/Buildings City` | 30 | hospital, precinct, school, cinema, bank, casino, diner, 3 skyscrapers, subway |
| `City/Buildings Modular City` | 79 | the townhouse modules the terraces are stacked from |
| `City/Props City` | 181 | kerbside furniture; only **Neon** and **Roof** subfolders placed directly |
| `City/TrafficLights City` | 15 | the signal posts |
| `City/Rails City` | 13 | the elevated railway — **currently switched off in `city.txt`** |
| `Cars/Cars City` | 47 | everyday traffic + police, ambulance, fire engine, school bus |
| `Cars/Cars Trucks` | 69 | the freight that only arterials carry |
| `Farm/Buildings Farm` | 27 | farmhouse, barns, silos, grain bins, water tower, windmill, greenhouse |
| `Farm/Vehicles Farm` | 18 | tractors, combine, baler, plough, seeder |
| `Farm/Roads Farm` | 4 | the dirt track |
| `Modular Parts/Fences` | 105 | paddock fencing, gates, posts |
| `Nature/Trees` + `Trees City` | 225 | every tree and the street species |
| `Nature/Rocks`, `Bushes`, `Flowers`, `Grass`, `Hedges` | 161 | ground cover and topiary |

## Bought and never once placed

Ordered by what I think is worth most to this game.

| Folder | n | Why it matters |
|---|---:|---|
| **`Survival`** | **174** | `Tree_Stand` (a hunting platform), `Bear_Trap`, `Cross_Wood` for a roadside memorial, `Road_Flare`, 3 signposts, abandoned suitcases, bedrolls, storm lanterns, a radio transceiver, tents, a generator. **The best-matched folder in the pack for this game.** |
| `City/Signs City` | 171 | 171 signs and the city uses a handful |
| `Farm/Crops Farm` | 166 | *(partly used)* — a dozen crops at growth stages. **Corn has four stages** (`Sprout`, `Seedling`, `Young`, three `Ripe_Fieldcorn`), and **wheat ships as 1 m² tileable squares in five stages including `Harvested`** — that is the field-tile technique, already owned. **No soybeans anywhere**, and corn has no tile equivalent |
| `City/Park City` | 33 | proper park furniture |
| `City/Playground City` + `SkatePark City` | 46 | *(used, via CityStreets)* |
| `Racetrack` | 152 | a modular track kit: 25 road pieces, 91 fences, control gate, overpass, reflectors. Needs land. |
| `Cars/Cars Racing` | 79 | nowhere to race them yet |
| `Cars/Cars Camping` | 25 | caravan, off-roaders, old vans |
| `Cars/Containers` | 12 | freight yards |
| `City/Lamps City` | 16 | *(used)* street lighting |
| `City/Poles City` | 9 | utility poles — a rural road wants these |
| `City/Beach City` | 39 | no coast on this map |
| `Modular Parts/Walls` | 421 | **not the house kit this number suggests.** Recursive, and dominated by Fantasy/Steampunk/Survival variants — the usable modern set is **35 walls, 4 City windows, 4 City doors**. Do not plan a procedural house kit around it. The `Roof_Regular` 1 m² roof system *is* good |
| `Modular Parts/Roofs` | 340 | |
| `Modular Parts/Towers` | 73 | |
| `Modular Parts/Ramparts` | 50 | |
| `Modular Parts/Beams` | 46 | |
| `Modular Parts/Doors` `Windows` `Stairs` `Floors` `Ceilings` `Gates` `Railings` `Decors` `Misc` `Chimneys` | 199 | a whole building-construction kit |
| `Modular Parts/Rails` | 6 | **tram track** — ground level, 1/3/5/10m + turns |
| `Nature/Vines` | 19 | for derelict walls |
| `Nature/Freshwater` `Seawater` `Corals` `Icebergs` `Icicles` | 249 | water; none on this map |
| `Nature/Mushrooms` | 10 | woodland floor |
| `People` | 79 | 7 sets; only Farm People are clothed for work |
| `Guns` | 51 | + ammo, attachments, throwables, a shooting range |
| `Tools` | 30 | |
| `Food` / `Drinks` | 27 | |
| `Sports` | 14 | |
| `Terrains` | 24 | prefab ground planes, hills, slopes, rivers, lakes |
| `Movie Set` | 91 | greenscreens, tripods, lighting rigs |

## The "wrong genre" folders, re-audited 2026-08-03 — and most of them are not

**This section used to read "Wrong genre for Rossville" and write off 2,416 prefabs in three
sentences. It was wrong, and it was the most misleading page in these docs.**

It was written against an English village called Rossville, in a simulation that had no calendar.
The town is now Rossville, Illinois, and `GameClock` has `Year`, `Month` and `DayOfYear`. **Both
premises expired.** Opening the folders shows the genre labels describe *which tab of the store the
content shipped in*, not what the models are.

Full working in **`ASSET-GAPS.md`**. The short version:

### Steampunk is largely Victorian — and this town sells Victorian for a living

Rossville is the **Antique Capital of Illinois**; that is its identity and part of its economy at
the 1991 opening.

| folder | n | what is actually in it |
|---|---:|---|
| **`Steampunk/Furniture`** | **120** | `Chest_Of_Drawers_Victorian`, `Chandelier_Victorian_Lit/Unlit`, `Desk_Kneehole`, `Cabinet_Specimen`, `Book_Stack`. **Antique-shop stock and older-house furniture.** Barely a gear in sight |
| **`Steampunk/Signboard`** | **21** | `Signboard_Bank`, `_Barber`, `_Bakery`, `_Apothecary`, `_Doctor`, `_Books`, `_Mechanic`. **The documented trades of the 1913 downtown**, as hanging shop signs |
| **`Steampunk/Bathroom`** | **19** | `Bathtub_Victorian`, `Sink_Victorian`, `Sitz_Bath_Victorian`. **The one bathroom in a pre-1950 house** |
| **`Steampunk/Trophies`** | **22** | mounted `Deer`, `Coyote`, `Fox`, `Trout` — farmhouse, tavern, Legion hall |
| `Steampunk/Library` | 47 | bookshelves and books |
| `Steampunk/Pipes` | 45 | grain elevator, boiler room, basement |

### Fantasy holds a farmyard and a main street under a medieval label

| folder | n | what is actually in it |
|---|---:|---|
| **`Fantasy/Wood`** | **21** | `Sawhorse`, `Wood_Logs_Stacked`, `Wood_Pile`, `Axe_Chopping`. Pure farmyard |
| **`Fantasy/Smith`** | **30** | `Anvil`, `Bellows`, `Forge`, `Grind_Stone` — the 1913 sheet labels a blacksmith |
| **`Fantasy/Butcher`** | **25** | hanging beef, pork and chicken — farm butchering, and there was a meat market |
| **`Fantasy/Vegetable`** | **19** | produce crates and baskets — the produce stand |
| `Fantasy/Cooking` | 87 | a generic kitchen |
| `Fantasy/Furniture` | 107 | mixed — benches and baths yes, battlemaps no |

**Still genuinely unusable:** Fantasy `Buildings` (125), `Tents` (102), `Palisade` (67),
`Statues` (53), `Battlefield` (35), `Siege` (20), `Castle` (17), `Portals` (5), most of `Prison`
(26 — but `Door_Bars` and `Handcuffs` survive, and the historical society museum has a two-cell
jail), and all of **`Primeval`** (105).

### The 458 seasonal prefabs were cut for a reason that has expired

They were dismissed when the simulation had no calendar. It has one now, and `Fields` already drives
the crops off it.

| pack | n | verdict |
|---|---:|---|
| **`4July`** | **80** | **Direct hit.** `Balloon_Small_Flag_American`, `Bench_Garden_Long`, `Bottle_Beer`, `Burger`, **`Corn_Slice_Grilled`**, plastic cups. The welcome-sign photograph shows US flags downtown |
| **`Christmas`** | **120** | **Directly attested.** The Rossville Community Organization runs *"Christmas in the Village"* at Christman Park with a drive-through lighted display. Every candle ships `Lit` and `Unlit` |
| **`Halloween`** | **80** | pumpkins and candy, in the month `WHO-SEES-WHOM.md` calls the darkest of the year |
| `Easter` | 108 | spring baskets and eggs |
| `Valentine` | 71 | weakest — but the candles, bottles and perfume are generic props under a pink label |

A large share of **every** seasonal pack is candles in `Lit`/`Unlit`/`Used` variants plus generic
food, bottles, cups, benches and chairs. Neither seasonal nor era-specific.

> **Decorations are era-gated content.** Christmas lights in 1991 are not the lights of 2005. The
> same `Era` curve being built for `technology.txt` should drive seasonal dressing — one mechanism,
> two uses.

### The lesson for this file

**Roughly 400–500 usable prefabs were written off on the strength of a folder name.** Before
dismissing anything here again, open it. The counts in the tables above are recursive and the labels
are the vendor's, not ours.

## Two traps, both of which have already cost time

**`AssetDatabase.FindAssets` searches folders recursively.** Asking `Nature/Rocks` for a rock also
returns `Nature/Rocks Winter`, which is how a summer afternoon came to be strewn with snow-capped
boulders. 200 prefabs across Nature are seasonal variants.

**The pack ships the whole world in one drawer.** `Nature/Trees` holds baobab, mangrove, acacia,
jungle and six palms alongside oak, beech and birch. Asking it for "a tree" puts a savanna species
on a northern high street. Curate by name — see `CityGreenery.All`.

## Where the code reaches into the pack

`CityStreets` roads + kerbside · `CityBuildings` landmarks + townhouses · `CityFarm` crops, pens,
yards · `CityGreenery` trees and bushes · `CityRail` the El · `CitySignals` signal posts ·
`CityTraffic` vehicle fleets by road class · `SunRig` street lamps.
