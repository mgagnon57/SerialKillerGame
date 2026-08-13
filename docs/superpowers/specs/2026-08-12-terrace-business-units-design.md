# Multiple businesses per terrace lot

**The ask.** "112 S Chicago was shops in 1991 like on the other side of street... that lot had
multiple business in them." The lot (parcel 237) is already ruled correctly at the parcel level —
`was built`, `kind shop`, `footprint later`, with a note describing a row of antique shops and a
restaurant, burned Feb 2004, replaced by Casey's. What is missing is a way to say *which* trade
sat in *which* storefront. Right now it cannot be said at all: the pass that builds this row
collapses every storefront into one `PlaceSpec`, and the business-ruling system rules whole
Places, not sections of one.

Scope: general, not a 112-S-Chicago-only patch. Any lot ruled `footprint later` goes through the
same pass and gets the same gap closed.

## What exists today and what it's missing

`CommercialRow.Lay(frontage, rng)` (Core) already computes the right thing: an array of
`Storefront` — offset, width, storeys, construction, anchor flag — laid out by the 1913 decay
rule, contiguous, no gaps. `DowntownFromSanborn.Apply` (Unity) calls it, then throws the array
away and creates exactly one `PlaceSpec` for the whole row, named `"the {street} terrace"`.

The business-ruling system (`Content/business-1991.txt`, `BusinessRulings`, `BusinessFromRulings`,
the in-game place panel) is already generic and already correct — it keys purely on a Place's
name (`spec.Name`) and rules that Place's kind/business/trade. It has no notion of "this Place has
several", because until now no Place ever did.

`CommercialLayers.StoreysOf`/construction are also already generic: they read a Place's *distance
from the Attica × Chicago crossing*, not anything specific to how it was created. So any Place
sitting at the right spot gets the right storey count and brick-vs-frame for free.

## The change

`DowntownFromSanborn.Apply` emits **one `PlaceSpec` per `Storefront`** instead of one for the
whole row. Each:

- gets its slice of the frontage as `Bounds` (offset/width from `CommercialRow.Lay`, same depth,
  `DepthMetres`, as today) — contiguous with its neighbours, zero gap, same geometry the single
  merged box used to occupy;
- defaults to `Kind = PlaceKind.Shop`;
- gets a stable handle for its `Name`.

Nothing else in the pipeline changes. `BusinessFromRulings.Apply` iterates `layout.Places` and
looks up a ruling by name exactly as it does today — it does not know or care that a lot now
produces several Places instead of one. `CommercialLayers` derives each new Place's storeys and
construction from its own position, same as it does for the 41 hand-placed units already in
`city.txt`. The in-game panel rules whichever Place you click, same as always.

## Naming

`"{address} #{n}"`, 1-based, left to right from the crossing — e.g. `"112 S Chicago #1"`,
`"112 S Chicago #2"`, `"112 S Chicago #3"`. Falls back to `"parcel {id} unit {n}"` when the lot
has no resolvable street address.

Stable across rebuilds for a fixed seed, on the same reasoning every other generated handle in
this project relies on: same seed, same substream, same layout, same names. Not stable against a
change to the frontage length or the crossing geometry itself — see Risk, below.

## Workflow

Unchanged from how "301 W Benton Ave" and "Rossville unit 27" already got ruled: you cannot
author `business-1991.txt` entries for a terrace ahead of a build, because the count and exact
handles aren't known until `CommercialRow.Lay` has run. Rebuild, walk up to the row in Play mode,
click each storefront's door, rule it through the panel. Nothing new to learn.

The parcel-level ruling (`Content/parcel-1991.txt`, the web tool) is untouched by any of this —
it stays the general "a shop stood here" statement. The per-storefront detail lives entirely in
`business-1991.txt`, same separation of concerns the project already has between "what stood on
the lot" and "what traded in each unit."

## Risk: stale handles after a resurvey

Before this change, a `footprint later` lot had exactly one handle, so there was nothing for a
ruling to go stale against beyond the whole file being untouched. Now there are several, in a
specific order, and if the frontage length or the RNG sequencing upstream of this pass ever
shifts — a road resurvey, a change to how the crossing is measured — an old ruling like
`"112 S Chicago #3"` can silently stop matching anything, or land on a different storefront than
the one it was written for.

`BusinessFromRulings.Apply` already logs `{Count} unit(s) ruled ... {named} named, {retyped}
re-typed`. It gains a fourth number: how many ruled units in `business-1991.txt` matched **no**
Place this build. Same reasoning as every other silent-failure guard in this file's neighbourhood
(`NoContentLoadFailsInSilenceTests`, the `[roads]`/`[walks]`/`[lots]` lines) — a ruling nothing
reads is exactly the failure that stays invisible longest.

## Testing

- Core: a synthetic `footprint later` lot proves `DowntownFromSanborn.Apply` emits one `PlaceSpec`
  per `CommercialRow.Lay` storefront, contiguous (each unit's start equals the previous unit's
  end), uniquely named, `Kind = Shop` by default.
- Core: ruling two adjacent units differently (kind, business, trade) through
  `BusinessFromRulings.Apply` changes only those two Places — no cross-contamination with their
  neighbours or with unrelated units elsewhere in the file.
- Core: a ruling whose handle matches no Place this build is counted and logged, not silently
  dropped.
- Visual: rendered and looked at once built — the open question is whether adjacent storefronts
  read as one continuous building or show a seam/gap. Not provable from source; the owner checks
  it by eye once it's built.
