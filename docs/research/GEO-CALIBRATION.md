# Geo-calibration — real GPS to map tile, with the receipts

How a real-world latitude/longitude becomes a Rossville map tile, what that mapping was
calibrated against, and which candidate anchors were rejected and why. Written to be
self-contained for any reader — human or AI assistant — auditing the town's spatial accuracy.
The implementation is `Assets/Noir/Unity/GeoAnchors.cs`; the road geometry it leans on is
`Content/roads.txt` (Vermilion County's own street centrelines).

Measured 2026-08-16, by a four-agent pass: survey crossings computed from the county
polylines, Google Geocoding for the real coordinates, residuals gated at 50 m, and an
adversarial re-check for anything that failed the gate.

---

## The frame

One map tile is one metre. The origin is the crossing of Chicago Street (Route 1) and Attica
Street — the point the village numbers its own addresses from:

```
real (40.3793, -87.66897)  ==  map tile (750, 1335)
map +x = east, map +y = SOUTH
lat = 40.3793  - (y - 1335) / 111132
lng = -87.66897 + (x - 750) / 84790        # 111320 * cos(40.3793 deg)
```

**The frame's scale is true and validated.** The control test below geocoded the origin
crossing independently and landed within 2.3 m east, 1.4 m south — geocoder noise, not drift.
Any calibration that rescales this frame is wrong: an exact two-anchor affine through the
origin and the 101 Perry pin demands scales of 0.565 E-W and 1.304 N-S, which the county
block survey flatly contradicts. The Perry residual is mostly Google's pin standing in the
estate's front lawn, ~45 m from the mansion centroid it was paired with — a local error, and
the reason `GeoAnchors` applies anchors as *local residual corrections* (exact at each
anchor, half-strength at 150 m, pure frame beyond a couple of blocks) rather than as a
global fit.

## The anchor table

| anchor | real GPS | map tile | residual vs frame | verdict |
|---|---|---|---|---|
| **Chicago × Attica** (control) | 40.3792443, -87.6689605 | 748.5, 1339.8 (survey crossing) | −2.3 m E, −1.4 m S | **frame validated**; the definitional origin row (750, 1335) stays |
| **Harrison × York** (north end) | 40.3847284, -87.6682433 | 832, 724 | +20.4 m E, −7.7 m S | **adopted** |
| **Abner × Park Place** (west side) | 40.3780110, -87.6714849 | 509, 1483 | −27.7 m E, +4.8 m S | **adopted** |
| **101 Perry St** (estate) | 40.3773085, -87.6680301 | 795, 1623.5 (mansion centroid) | −34.7 m E, +67.2 m S | adopted earlier, with the pin-in-the-front-lawn caveat above |
| Benton × Green (east edge) | — | 1625, 1093.9 | geocoder +198 m off | **rejected**, see below |
| Earl Ct × S Grove St (SE corner) | — | 1338.1, 2226 | geocoder cannot resolve | **rejected**, see below |

Adopted rows live in `GeoAnchors.Anchors`; adding a future verified placement means adding
one row there. Prefer unambiguous points — street crossings, building centroids read off the
satellite — over bare geocoder address pins, which is the lesson the Perry row teaches.

## The rejects, and why they are worth keeping on record

Both rejections are the geocoder going blind at the town's edges, not the survey being wrong
— and both re-checks *confirmed* the county geometry by other means:

- **Benton × Green.** Every query phrasing returns the same synthetic node 627 ft west of
  the survey tee, placed where Google's east-west Green St dead-ends 430 ft short of Benton.
  The county survey carries a genuine tee there via a separate north-south Green St stub
  (OBJECTID 6019) that Google's map lacks entirely — though that stub's "Green" label is
  itself uncorroborated (no address ranges; every addressed Green parcel, 104–409, fronts
  the east-west run). Control queries against the true Grove × Benton junction landed within
  ~35 ft, so the geocoder works nearby; it simply has no data for this crossing. Not forced.
- **Earl Ct × S Grove St.** Google indexes Earl Ct only as address points, not a route, and
  names Grove St at that latitude "Grove St, Alvin, IL 61811". But reverse-geocoding the
  survey's own crossing point finds Grove St 23 m away and 307 Earl Ct 48 m away —
  the county geometry is right; the geocoder just cannot say so as an intersection.

## What an auditor should check

1. Round-trip: `GeoAnchors.TileToLatLng` then `LatLngToTile` returns the input to under a
   thousandth of a tile everywhere on the 2100×2400 map.
2. Every anchor row maps exactly to its stated tile (the residual blend guarantees this by
   construction — a failure means the table and the code disagree).
3. Points far from all anchors reduce to the pure frame formulas above.
4. New real-world data (business coordinates, landmark pins) should be mapped through
   `LatLngToTile` and then sanity-checked against `Content/roads.txt` and `Content/city.txt`
   — a shop that lands in a carriageway is a bad pin, not a map error.

One standing warning for anyone importing *current* real-world data: the game models 1991,
and downtown Rossville burned in February 2004. Present-day listings describe a street that
no longer matches the modelled one. Geometry generalises; businesses do not — the era
sources (`SOURCING.md`, the Sanborn sheets, the owner's memory) outrank anything current.
