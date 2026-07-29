# Tile art

Drop PNG files in this folder and the village uses them instead of the procedural drawing.
Delete a file and that material goes back to being generated in code.

**Nothing here is required.** Every material is independent, so you can replace grass today and
roads next month, and the village is never broken in between. No Unity import settings, no
`.meta` files, no Inspector work — the PNGs are read straight off disk at Play time.

---

## What to buy or download

You want an **isometric** tileset — diamond-shaped tiles, not top-down squares.

| | |
|---|---|
| **Shape** | 2:1 diamond. Width exactly twice the height. |
| **Size** | **64×32 is the sweet spot.** 32×16 works but is coarse; 128×64 is lovely and quadruples memory. |
| **Format** | PNG with transparency. The diamond sits in a rectangle; the corners must be transparent. |
| **Licence** | CC0 is simplest (no attribution). CC-BY needs a credit. Check before you commit to a pack. |

Places worth looking, in rough order of licensing simplicity:

- **kenney.nl** — CC0, no attribution required, extremely reliable terms. Isometric packs tend
  to be chunky 3D-rendered blocks: clean and readable, a specific toy-like look.
- **opengameart.org** — large, mixed licences. Filter by licence and read each entry.
- **itch.io** — the widest selection of pixel-art isometric packs, free and paid.

*(I couldn't verify current listings — my web search budget for this session is used up — so
treat these as leads to check rather than confirmed links.)*

---

## Filenames

Ground materials. All optional:

| File | Used for |
|---|---|
| `grass.png` | open ground, gardens, the green |
| `field.png` | farmland, the allotments |
| `wood.png` | the spinney |
| `water.png` | the river |
| `road.png` | Ashcombe Street and the lanes |
| `path.png` | footpaths, verges, yards |
| `floor.png` | building interiors |
| `churchyard.png` | the churchyard |

Structures:

| File | Used for |
|---|---|
| `wall.png` | a whole isometric block. Aligned by its **base**, so any height works. |

### Variants

Add `grass_1.png`, `grass_2.png`, `grass_3.png` and so on (up to 8). One is chosen per tile by
hashing its coordinates — the same tile always gets the same variant, so nothing shimmers, but
a field stops looking obviously tiled. This is the single cheapest way to make imported art
look less repetitive.

---

## Things that will look wrong, and why

**Tiles must all be the same size.** The first ground texture found sets the grid; anything
different is warned about in the Console and will not line up.

**The 2:1 ratio matters.** At any other ratio the diamond edges stair-step and the grid becomes
visible as a herringbone pattern.

**Near-side walls are always drawn short**, procedurally, even when `wall.png` exists. In a true
isometric view the south and east walls stand between the camera and the room, so imported
full-height blocks there would turn the village into a field of sealed boxes. Being able to
watch people indoors matters more than a consistent brick texture on a wall you are looking
over. If you'd rather have the walls whole, that's one line in `IsoWorldTexture.cs`.

**Changing tile size changes memory.** The baked map is `(120 + 90) × tileWidth` pixels across.
At 32px that's ~5.7M pixels; at 64px, ~23M; at 128px, ~92M. Past 64 it is worth talking about
splitting the map into chunks.
