# Overnight notes

Open Unity, press ▶.

**Verified, not assumed.** As well as 69 green Core tests, there is now a headless smoke test
that builds the whole village *and its geometry* outside play mode — the thing that catches
what compiling cannot: null references in mesh generation, missing shaders, bad indices.

```
world      170x120, 61 places, 250 rooms, 599 furniture, 894 props
layout     valid, 16138 walkable, 1 region
people     116 in 50 households, 48 in work
sim        ran two hours, clock at Mon d0 08:00
render     built 2452 renderers
--- SMOKE TEST PASSED ---
```

Run it yourself from the **Noir → Smoke Test** menu, or headlessly:

```
"C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" ^
  -batchmode -quit -projectPath C:\SerialKillerGame ^
  -executeMethod Noir.Editor.SmokeTest.Run -logFile smoke.log
```

---

## Controls

| | |
|---|---|
| **Tab** | switch between overview and **street level** |
| Orbit | right-drag, or **Q** / **E** |
| Tilt | **R** up, **Shift+F** down |
| Zoom | mouse wheel |
| Move | **WASD** — pans in overview, walks in street mode (**Shift** to jog) |
| Select | left-click a person |
| Follow | **F** |
| Time | speed buttons top-left; skip-to-hour beside them |

**Do this first:** press **Tab**, then walk down Ashcombe Street with WASD. Then set the clock
to 20:00 and walk it again in the dark.

---

## What's new since you went to bed

### Atmosphere
- **Real sky.** A procedural skybox with a sun disc that tracks the actual sun, and dims
  through the evening rather than staying a bright daylight dome overhead.
- **Distance fog**, tinted to match the sky. This is what puts air between you and the far end
  of the village, so distance reads as distance.
- **Street-level camera.** Tab drops you to eye height (1.7 m) to walk around. A village you
  can only look down at is a diagram — you find out whether it feels alive at eye level,
  walking past somebody's lit kitchen window.

### The village looks like a village
- **Pitched roofs** on every building — hip roofs with a real ridge and eaves overhang.
  They're on from a distance and at street level, and **lift off when you drop in close from
  above**, so you can still watch people indoors. It follows the camera; there's no toggle to
  remember.
- **Chimneys — one per home.** On a terrace that means four stacks on one roof, which is
  exactly what tells you from the street how many families live in the building.
- **Lamp posts.** The street lights previously had no geometry at all — light appeared from
  nothing five metres up. Now there's a column and an emissive lantern, so a lit street has
  the silhouette that makes it read as a street.
- **Windows that light up.** Emissive panes on the outside walls, glowing when somebody is
  home and awake and cold and dark when the house is empty. This is the one to go and look at:
  set the clock to about 21:00, press **Tab**, and walk down Back Lane.
- **906 props**: trees crowding the spinney and thinning across open grass, hedges along the
  road verges, post-and-rail on the field boundaries, benches on the green, headstones in the
  churchyard, a postbox by the Post Office.
- **Tiling ground textures** — seamless 256×256 surfaces for grass, asphalt, ploughed earth,
  water, floorboards, brick and roof tile.

### Houses people could actually live in
The village was rebuilt at **170 × 120** because the old cottages were 6 × 7 metres — after
one-metre walls that's 20 m² of floor, too small for a bedroom *and* a bathroom. Families of
four were living in two rooms. Houses are now 8 × 8 to 12 × 11.

- **250 generated rooms** across the village — hall, kitchen, bedroom, bathroom, front room,
  scullery — no two layouts alike.
- **571 pieces of furniture**, placed against walls, never blocking a door or a walkway.
- **Terraces and flats.** Ash Terrace is one building with four front doors and solid party
  walls; Mill Buildings is four homes for the mill hands. **116 people in 50 households.**

---

## The rules that make generated houses look like houses

Subdivision alone gives you plausible rectangles in an implausible arrangement. What makes it
read as architecture is the rules layered on top:

- the front door opens into a hall or the front room, never a bedroom or bathroom
- the bathroom is the **smallest** room, and hangs off circulation rather than being a
  through-route
- you never walk through one bedroom to reach another
- the kitchen wants an outside wall, for a window and a back door
- nothing is one tile wide — a corridor is not a room
- **priority order is what a house cannot do without**: hall → kitchen → bedroom → bathroom →
  front room → more bedrooms

That last one matters. An earlier version assigned by size and produced a four-room house,
occupied by a family of three, with nowhere to sleep. A cottage with a kitchen and one bedroom
and no front room is a real house; one with a front room and no bed is not. A three-room
cottage ends up with the privy outside, which for a 1970s village is a fact rather than an
oversight.

---

## Buying textures — what will actually fit

You asked to see something before committing. Here's the spec, so you can judge a pack in
about a minute.

**Everything uses standard URP/Lit materials**, deliberately, so a bought pack drops in without
adapters.

### Ground and surfaces — `Content/textures/`
Loose PNGs read straight off disk. No import settings, no `.meta` files, no Inspector work.
Drop a file in, press Play, see it. Delete it and that surface goes back to flat colour.

| | |
|---|---|
| Files | `grass` `field` `wood` `water` `road` `path` `floor` `churchyard` `wall` `roof` |
| Format | PNG, **seamlessly tiling**, square |
| Size | 256² is fine, 512² or 1024² better. Must tile without a visible seam |
| Tiling | applied at 4 metres per repeat (3 m walls, 2.5 m roofs) |
| Colour | the material **tints** the texture, so greyscale or muted sets work best — a heavily coloured pack will fight the palette |

**What to search for:** "seamless PBR ground textures", "tileable material pack". You only need
base colour; normal and roughness maps aren't wired up yet but are easy to add.

### Furniture and buildings — models
Every piece of furniture already has an exact **footprint and height** in metres. The box *is*
the specification — models drop onto what's there:

| | | |
|---|---|---|
| Bed | 2 × 1 m | 0.55 m |
| Wardrobe | 1 × 1 | 1.90 |
| Dresser | 1 × 1 | 1.20 |
| Sofa | 2 × 1 | 0.80 |
| Table | 1–2 × 1 | 0.75 |
| Chair | 1 × 1 | 0.90 |
| Cooker | 1 × 1 | 0.90 |
| Sink / Counter | 1–2 × 1 | 0.90 |
| Bath | 2 × 1 | 0.60 |
| Basin | 1 × 1 | 0.80 |
| Hearth | 1 × 1 | 1.10 |

**What to look for:** a **modular interior kit**, 1970s domestic or generic residential, in
metres, Y-up, origin at the base. Avoid anything stylised-fantasy or sci-fi — the register here
is ordinary and slightly worn.

**My honest advice: don't buy anything yet.** The boxes tell you whether the *layouts* work,
and that's the thing still moving. Furnishing rooms that are about to change shape is wasted
money. When the layouts settle, the table above is your shopping list.

---

## Known rough edges

- **Capsules, not people.** No walk cycle, no facing — a capsule is radially symmetric so
  rotation would be invisible anyway. Occlusion *does* work now: they're real geometry with
  real depth, so a wall genuinely hides whoever is behind it. (That was the one thing 2D
  could not do without a rewrite; in 3D it comes free.)
- **No interior doors as objects.** Doorways are gaps in walls; there's no door leaf to open.
- **Bedroom count comes from floor area, not from who lives there.** Arguably realistic —
  people live in houses that fit — but a family of five can end up in a two-bedroom cottage.
- **No audio at all.**
- **The Old Rectory and a few outliers** sit oddly far from the road. Cosmetic.

---

## Where things live

```
Assets/Noir/Core/     the simulation. zero UnityEngine references. 69 tests.
  World/              tiles, places, rooms, furniture, props, generation
  People/             citizens, households, jobs, day plans
  Sim/                pathfinding, 20 Hz tick
Assets/Noir/Unity/    the shell. reads state, draws it, decides nothing.
Assets/Noir/Editor/   automated editor work (the URP 3D renderer swap)
Content/              village.txt, names, particulars, tiles/, textures/
tools/                tests + headless harness
```

**Useful commands** (from `tools/`):

```
dotnet test Noir.sln                          69 tests, ~3 s
dotnet run --project Noir.Sim -- check        validate the village layout
dotnet run --project Noir.Sim -- house 7      one floor plan, as text
dotnet run --project Noir.Sim -- who          population summary
dotnet run --project Noir.Sim -- day 3        one person's whole day
dotnet run --project Noir.Sim -- density      where everyone is, hour by hour
dotnet run --project Noir.Sim -- tiles        regenerate all textures
```

`village.txt` is the map. Edit it and run `check` — it will tell you about overlaps,
unreachable doors and cut-off cottages before you ever open Unity.
