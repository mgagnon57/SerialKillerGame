# Moving and turning a house

**The ask.** "There is no way for me to move a house or rotate it... Some of the house plots are so
close they just need an alignment fix." A drawing tool: select a house, move it, turn it.

## What is being moved

The 822 measured footprints in `Content/parcel-buildings.txt` — polygons from FEMA/ORNL imagery,
seated on the county's lots. Each carries a `skew` (its own angle) and a `block` (its block's
angle), so "square it to the street" is already arithmetic: turn by `block − skew`.

Buildings the game *invents* where a lot has no measured footprint (~315 of them, raised by
`FillFromSurvey`) are **out of scope for now**. They are placed by rule rather than by shape, so
moving one means overriding a rule — a different mechanism, and invisible on the map until Play.

## Decisions

| Question | Answer |
|---|---|
| Move and rotate | Yes, now |
| Resize | Later, with the layout work. The size came off imagery and is the most trustworthy number in the file. |
| Rotation | One button squares it to the block; a handle overrides when the snap is wrong |
| Crossing a lot line | **Flagged in red, never moved for you.** It goes where you drop it. |
| A lot with a house and a garage | Only what you selected moves |
| Alignment aids | All four: snap to the neighbours' front line, straighten a whole block at once, ghost of the measured original with "put it back", live distances in feet |

The crossing rule is worth stating twice because it is the one that could have gone either way:
nothing on this map ever moves a building except the owner. A flag says what is wrong; it does not
correct it.

## Storage

A new authored file, `Content/placement-1991.txt`. One line per adjusted building:

    building <parcelId> <index> move <dx> <dy> turn <degrees>

Metres and degrees, on the same grid as the shapes in `parcel-buildings.txt`. Absent line means
untouched — so the file holds only what the owner has actually changed, and "put it back" is a
deletion rather than a value.

**It is an overlay, never an edit.** `parcel-buildings.txt` stays exactly as measured, so the
survey can be re-run and re-seated without losing a single adjustment, and the ghost of where the
measurement put it is always available to draw.

## The game side

The transform is applied where the ring is handed out, so every consumer sees the adjusted shape
and none of them has to know the overlay exists. `SeatOnSurvey.BoxOf` then squares it to the lot
and `WorldBuilder` builds the outline, both unchanged.

Rotation is about the footprint's own centroid — the pivot a person expects when they grab a
corner handle.

## The gate

`move` and `turn` are **structural**: a moved building changes the overlap test in `SeatOnSurvey`
and the on-a-road test in `FillFromSurvey`, either of which can make a building fail to appear.
`tools/change_gate.py` gains the new file and both verbs, so sending a placement change runs the
smoke test.

## Verification

- The click path itself, with real pointer events at real screen pixels — not by calling the
  handlers directly. That gap is exactly what hid the road-selection lock.
- A moved building survives the round trip: adjust, save, read back, and the game's own smoke test
  reports the town still valid and in one piece.
- `parcel-buildings.txt` byte-for-byte unchanged after any amount of adjusting.
