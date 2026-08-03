# What the assessor's own records say about Rossville's lots

Computed from `tools/rossville-property-records.json` — **794 parcels**, Vermilion County
Supervisor of Assessments data, already in the repo and never analysed until now.

**Aggregate only.** No individual parcel, address or owner appears here. See the privacy note at
the end.

---

## The headline: how many lots have a building on one

| | count | share |
|---|---|---|
| **improved** (a building is assessed on it) | **575** | **72.4%** |
| **vacant** (no building) | **219** | **27.6%** |

And restricted to the residential classes — the quarter-acre town lots — which is what the suburb
generator actually needs:

| use code | n | improved | median size | reading |
|---|---|---|---|---|
| **0040** | **517** | **100%** | 0.25 ac | improved residential |
| **0030** | **106** | **0%** | 0.25 ac | **vacant residential — same size, no house** |
| 0060 | 58 | 84% | 0.15 ac | smaller lots, likely the commercial core |
| 0090 | 52 | 0% | 0.36 ac | vacant, larger |
| 0021 | 16 | 0% | 8.25 ac | farmland |
| 0080 | 4 | 100% | 5.77 ac | large improved — school/institutional |

**517 improved of 623 residential lots = 83% built, 17% empty.**

That is the number to build to. Not the 62% my single 1913 map crop suggested — that was a sample
of one block, and it was measuring 1913, when the town had a century of infill still to come.

## Lot size — an independent confirmation

| percentile | acres | m² |
|---|---|---|
| p10 | 0.12 | 486 |
| **median** | **0.25** | **1,012** |
| p90 | 0.57 | 2,307 |

`Content/parcels.txt` already records the median lot as **1,011 m²**, derived separately from the
GIS boundary geometry. This figure comes from the **assessor's acreage field**, a completely
different column of a different dataset — and lands within one square metre.

Two independent measurements agreeing to 0.1% is about as good as this kind of confirmation gets.
**A quarter acre is right.**

## Building values

Assessed, not market. Illinois assesses at roughly one third of market value.

| percentile | assessed | implied market |
|---|---|---|
| p10 | $4,361 | ~$13,000 |
| median | $23,575 | ~$71,000 |
| p90 | $54,134 | ~$162,000 |

The implied median tracks the ~$92–95k median home value found independently in
`agent-reports/rossville-economy.md`, allowing for the assessment ratio being approximate and for
land being assessed separately.

**The p10 is the striking figure.** A tenth of the improved buildings in this town are assessed
below $4,400 — around $13,000 of market value. That is not a house anybody is maintaining.

## The characterful number: absentee ownership

**239 of 794 parcels — 30.1% — are flagged absentee-owned.**

Nearly a third of the property in Rossville is owned by somebody who does not live on it. In a
village of 1,200 that is rentals and inherited property, and it is one of the more telling
statistics about a small town that has been slowly thinning for a century.

For a game about who was where and who noticed: **a third of the doors belong to someone who is
not behind them.**

---

## What to change in the build

1. **Leave 17% of residential lots empty.** Not scattered at random — the vacant class `0030` is
   the *same median size* as the improved class, so these are ordinary town lots that simply never
   got built on or were cleared.
2. **The quarter-acre median is confirmed twice over.** Nothing to change; now it is corroborated
   rather than assumed.
3. **Housing quality should vary a great deal.** p10 to p90 spans more than a factor of twelve in
   assessed value. A street of uniform houses is wrong in a way the data can now quantify.
4. **Absentee ownership at ~30%** is available as a real property of a place, derived from the
   real record, with nobody named.

## Privacy

Every field in this dataset was enumerated before analysis:

```
AbsenteeOwner  LegalDesc  OBJECTID  PIN  PropertyAddress
TXACRS  TXBLDA  TXFRMA  TXLNDA  TXOV65  TXOWOC  TXRESA  TXUSEC
```

**There is no owner name field.** The only person-adjacent field is `AbsenteeOwner`, a boolean
with exactly two distinct values across all 794 records. This confirms at source what the earlier
health audit found by inspection — the project's NO REAL RESIDENTS rule holds at the point of data
collection, not merely in derived files.

`PropertyAddress` exists and is populated for some parcels. **It is not read, extracted, or
recorded anywhere in this analysis**, and nothing here is broken down to a level where an
individual parcel could be identified.
