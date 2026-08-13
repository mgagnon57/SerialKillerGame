# AI impressions of Rossville, 1991 — NOT SURVEY

Generated 2026-08-06 with Gemini 3.0 Pro (Nano Banana Pro) via the Unity asset generation
bridge. Six images, one prompt each, no reference photographs used.

## What these are

**Mood, not measurement.** They are what the research *reads like* rendered as a period
photograph — a target to build toward and to argue with. Nothing in them is derived from
`Content/parcels.txt`, `parcel-buildings.txt` or `roads.txt`, and no street layout, building
count, frontage or address in these pictures corresponds to the real town. The model invented
every one of them.

Put plainly, against `docs/SOURCES-OF-TRUTH.md`: these sit *below* authored. `parcel-1991.txt`
is authored by somebody who was there and outranks the sources. These are authored by a model
that has never been there and outrank nothing.

**Do not measure anything off these. Do not cite them. Do not let them settle a question.**

## What fed the prompts

- `docs/research/THE-ERA.md` — build to 1991: downtown block whole (the Feb 2004 fire has not
  happened), high school open, antique district mid-run
- `docs/research/DOWNTOWN-1991.md` — the "Antique Capital of Illinois"; Market Place Shoppes is
  the one business confirmed trading in 1991, so it is the one shop name on a sign
- `docs/research/THE-YEAR.md` — early September, chosen because it is the year's most striking
  moment: gold soybeans against still-standing green corn, before anything is cut. Also the
  canopy: silver maple, hackberry, honey locust and *healthy ash* — elms forty years gone, the
  emerald ash borer not yet arrived
- `Content/parcel-1991.txt` — 172 parcels ruled; the mix of 45 dwellings, 28 shops, 4 schools,
  4 apartments and the railway set what appears in each frame
- `docs/SOURCES-OF-TRUTH.md` §3 facts 4 and 5 — alleys are not for cars, and a lot ran to the
  alley. That is what shot 04 is about.

No personal names were sent to the model. Business and trade names were, which `THE-ERA.md`
explicitly permits: a shop sign is public.

## The six

| file | view |
|---|---|
| `01-chicago-street-south.png` | main street looking south, unbroken brick terrace both sides |
| `02-chicago-attica-corner.png` | the downtown corner, both rows meeting |
| `03-maple-street-residential.png` | residential street under closed canopy |
| `04-alley-behind-lots.png` | service alley behind the back lots |
| `05-edge-of-town.png` | the hard town/country edge, elevator and railroad |
| `06-aerial-establishing.png` | low oblique aerial, whole town in its fields |

### Second set, generated after feedback that set one "looked way before that"

The first six skewed to about 1978. Cause: the prompts said "period vehicles" without naming
model years, and asked for faded, weathered, peeling, sun-bleached — which ages everything.
Set two names exact 1988–91 vehicles, asks for a crisp one-hour-lab print instead of a
sun-bleached one, and loads in objects that can only be 1991.

| file | view |
|---|---|
| `07-schools-letting-out.png` | both schools facing each other, buses, 1991 clothing |
| `08-filling-station.png` | filling station, period pumps and window signage |
| `09-main-street-dusk.png` | main street at dusk, video rental and bank lit |
| `10-back-yard-and-drive.png` | back yard: C-band dish, above-ground pool, minivan |
| `11-harvest-at-the-elevator.png` | October, grain trucks on the scale, combine, dust |
| `12-residential-street-at-night.png` | one sodium streetlight, porch lights, a lit window |

`12` is the one that matters for the sightline work — it is what a residential street actually
gives a witness after dark, which per `THE-YEAR.md` is half past four in late December.

## Known wrong

- **02 street signs read MAIN ST and BROADWAY.** The real crossroads is Chicago and Attica.
- **Sign lettering garbles** in 01 and 02 ("AIITIQUES S COLLLECTIBLES"), as image models do.
- **06 carries a `SEP 12 '91` date stamp** the model added on its own. Consistent with the
  season chosen, but invented — it is not a real print from a real day. `10` and `11` did the
  same, `SEP 14 '91` and `OCT 91`.
- The downtown in 06 reads smaller than 28 shop parcels would give you.
- **Set one reads about 1978**, not 1991 — the cars are square-body 1970s. Kept for the
  architecture and the light, not for the era.
- **06 and 11 disagree about the elevator**: concrete in the aerial, metal-clad at harvest.
  Each is individually plausible; they are not the same building.
- **08 is in late autumn or winter** — bare trees, harvested stubble — while the rest of the
  set is September. A different day, not a wrong one.
- **09's awning reads FARMEES STATE BANK.** The projecting sign above it gets FARMERS right.
- **10's wheeled green bins lean later than 1991** for rural Illinois; metal and moulded cans
  would be likelier.

## Where they live, and why they are not in the repository

Moved out of `Assets/Noir/Reference/` on 2026-08-09. Inside `Assets/`, Unity imported each one
as a Sprite asset with mipmaps — build time, `Library/` space and a GUID apiece — for pictures
this file forbids settling anything. They are 24 MB and they are ignored by `.gitignore`
(`docs/reference/**/*.png`); this README and `prompts/` are committed, because 52 KB of sidecar
JSON is the only unrecoverable thing here.

**`customSeed` is -1 on every one of the twelve**, so none of them is reproducible from its
prompt. The prompts are a record of what was asked for, not a recipe.

There was a second copy. `GeneratedAssets/`, the Unity AI Generators output cache, held all
twelve again — byte-identical, confirmed by hash — plus the sidecars. It was 24 MB of untracked
duplicate sitting one `git add -A` away from a public remote, and it is deleted and ignored now.
Deleting the pictures through Unity would not have reclaimed it: the package's deletion processor
renames the folder to `<guid>_deleted` and returns `DidNotDelete`.
