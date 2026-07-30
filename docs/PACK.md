# What is in the Poly Universal Pack

**6,484 prefabs**, 764 MB, 6,142 meshes, 297 textures, 101 materials. Publisher polyperfect,
version 4.9, URP and HDRP. One atlas workflow, so almost everything shares a handful of
materials — which is why the chunker gets 13,000 renderers down to 1,700 with 24 materials.

Written because the useful things kept turning up one at a time. `City/Roads City` — a 48-piece
30m road kit with painted lanes and matching junctions — was found on the **second day**, after
the entire city had been built out of the generic 10m `Modular Parts/Roads`. Then 486 farm
prefabs. Then 174 survival props. Then a 13-piece rail kit of which five were in use.

**Check this file before building, generating or buying anything.**

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
| `Farm/Crops Farm` | 166 | *(partly used)* — a dozen crops at growth stages |
| `City/Park City` | 33 | proper park furniture |
| `City/Playground City` + `SkatePark City` | 46 | *(used, via CityStreets)* |
| `Racetrack` | 152 | a modular track kit: 25 road pieces, 91 fences, control gate, overpass, reflectors. Needs land. |
| `Cars/Cars Racing` | 79 | nowhere to race them yet |
| `Cars/Cars Camping` | 25 | caravan, off-roaders, old vans |
| `Cars/Containers` | 12 | freight yards |
| `City/Lamps City` | 16 | *(used)* street lighting |
| `City/Poles City` | 9 | utility poles — a rural road wants these |
| `City/Beach City` | 39 | no coast on this map |
| `Modular Parts/Walls` | **421** | the biggest single folder in the pack |
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

## Wrong genre for Northgate

Real content, but a modern American town is not the place for it: `Fantasy` (1,374 across 28
subfolders), `Steampunk` (479), `Primeval` (105), and the seasonal sets — `Christmas` 120,
`Easter` 108, `Halloween` 80, `4July` 80, `Valentine` 71. **458 seasonal prefabs.**

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
