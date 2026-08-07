# 1991 is the date. Everything else is a fallback.

Rossville as this project builds it is Rossville in **1991**. That is not a setting or a theme; it
is the specification. If a thing was not there in 1991 it does not belong in the game.

Every measured source this project owns postdates that by fifteen years or more. They had to be
used — there was no other way to get a town on the ground — but now that the town exists, their
job changes. **They are what to draw until somebody who was there says otherwise, and they are
never evidence about 1991.**

## The order of authority

| | Source | Date | Standing |
|---|---|---|---|
| 1 | `Content/parcel-1991.txt` · `roads-1991.txt` · `placement-1991.txt` | 1991 | **Truth.** The owner was there. Overrides everything below without argument. |
| 2 | Sanborn fire insurance sheets | 1913 | Historical. Wrong era, but wrong in a way that can be reasoned about — it shows a town that predates 1991 rather than one that replaced it. |
| 3 | Vermilion County tax roll | 2007+ | Fallback. |
| 4 | FEMA / ORNL building footprints | 2016 | Fallback. |
| 5 | OSM / TIGER road geometry | current | Fallback, and the least trustworthy of them — see the note on `tiger:reviewed`. |

Nothing in rows 3 to 5 may be cited as a reason to believe something about 1991. They answer
"what is there now", and the difference between that and "what was there then" is the whole
project.

## Why this has to be visible rather than remembered

The failure mode is silent. A footprint nobody has ruled on draws exactly like one that has been
confirmed, so a building put up decades later sits in the town looking as though it belongs — and
nothing anywhere says otherwise. That is not hypothetical: the Casey's on South Chicago Street sat
downtown reading as 1991 until the owner recognised it by name. Its geometry had been flagged and
misread as a survey error, when what it actually was is a forecourt: a layout that dates the
building, if you know to ask what year you are looking at.

So the distinction is drawn and counted rather than trusted to memory:

- **The map** draws an unconfirmed footprint as a pale wash with a dashed edge. Confirmed
  buildings are solid. The layer panel carries the count.
- **The game** prints it on every smoke run: how many buildings are confirmed for 1991 and how
  many are still standing on post-2000 sources.

At the time of writing that number is **95 of 824 confirmed — 710 unchecked, 86%**. Watching it
fall is the only real measure of how close this town is to the year it claims to be.

## What "confirmed" means

A lot is confirmed once it carries a `was` ruling, whatever that ruling says:

- `built` — a building stood here in 1991
- `vacant` — the lot was there and nothing stood on it
- `absent` — there was no such lot in 1991
- `unsure` — looked at, and not settled

`unsure` counts as confirmed for this purpose, and should. It means somebody has been to the lot
and failed to settle it, which is a different and more honest state than never having looked.
