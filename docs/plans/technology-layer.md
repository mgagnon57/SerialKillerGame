# Year-specific technology, 1991 onward

**Revised 2026-08-03 after re-reading the code**, once `Fields`, `Railroad` and `Daylight` had
landed. Five things changed; the two marked **CHANGED** are the ones that matter.

## Context

The game opens in 1991 and runs to about 2006. `GameClock` knows it — a real civil calendar, epoch
Monday 7 January 1991. `Daylight` and `Fields` now consume it. **Nothing else does.** The only other
year in the simulation's behaviour is a hardcoded `Households.Year = 1991`.

So the town is still era-blind in everything but crops and darkness: a citizen in 2005 has the same
objects and the same reachability as in 1991. The sharpest instance is already written down in
`docs/research/WHO-SEES-WHOM.md` §5:

> In 1991 you cannot reach a person who is not at home, and an unanswered phone means nothing. By
> 2006 a person is reachable, and there is a log. **The same disappearance is a different event at
> the two ends of this game.**

This pass builds a **year-gated fact layer**: Core-only, Unity-free, no art. It answers *"does this
household have a mobile phone in 1999?"* so the day plan, the dialogue prompt and the investigation
can consult it. **Author against the record, not against the art** — asset packs come later, and
`Fields` already proves the point by shipping six states when the pack can render one.

---

## The mechanism already exists in this codebase — do not write a second one

**`Fields` invented the same idea independently**, and its commit message states it better than the
original draft of this plan did:

> *"The percentages are a distribution, and that is the trick. The state tabulates what fraction of
> acreage has reached each stage by each date, and that curve is not an average to apply to every
> field — it IS the spread of dates across fields. So each field gets a fixed rank in [0,1) and its
> own planting date is read off by inverting the curve."*

That is exactly the adoption model this plan needs, one domain over. Two pieces exist:

| piece | where | what it does |
|---|---|---|
| `Fields.RankOf(key)` | `Core/World/Fields.cs:140` | `((key >> 11) & 0xFFFF) / 65536f` — a fixed rank in [0,1) from a key. Note the shift: the low bits are avoided deliberately to stop striping |
| `Fields.DayWhen(days, percent, rank)` | `Core/World/Fields.cs:264` | **private.** Inverts a `% reached by date` curve to give *this* entity's own date |

**CHANGED — Step 2 is now "generalise `DayWhen`", not "write a new inverter."** Lift it into a
shared `Era`, express it in years instead of days, and have `Fields` call the general one. One idea
in the codebase, not two that agree by coincidence — which is the exact argument `ContentText`'s own
header makes about parsers.

---

## What else exists — do not rebuild these

| thing | where | why it matters |
|---|---|---|
| civil calendar | `Core/Contracts/GameClock.cs` | `Year`, `Month`, `DayOfYear`, `Season` |
| shared content syntax | `Core/World/ContentText.cs` | `SplitLines`, `Tokenise`, numbers, times. **`internal` to `Noir.Core.World`** |
| table parse idiom | `Core/World/PlaceKindTable.cs` | `Parse(string)` + `Install(...)`, no file I/O |
| era-specific rule precedent | `Core/Contracts/Daylight.cs` | pre-2007 US DST. Rules are year-specific too |
| looking at it | `tools/Noir.Core.Tests/CountrysideDiagnostic.cs` | `[Explicit]` printing test — see Step 6 |

**CHANGED — `Era` goes in `Core/World`, not `Contracts`.** The original plan put it in Contracts.
That is wrong now: parsing needs `ContentText`, which is `internal` to `Noir.Core.World`, and the
curve inversion it generalises lives in `Fields`, also World. Contracts is for zero-dependency
primitives. `Noir.Core.People` references World, so the day-plan and dialogue consumers still see it.

**`ContentLoader` is still Unity-side** (`Assets/Noir/Unity/ContentLoader.cs`, uses
`Application.dataPath`). Core cannot read files. `ContentText` is a *parser*, not a loader — the
Unity caller still does
`TechnologyTable.Install(TechnologyTable.Parse(ContentLoader.Read("technology.txt")))`.

**Core bans transcendentals for replay determinism.** `Daylight` embeds a 365-entry table precisely
because the NOAA solar algorithm needs six of them. Linear interpolation and integer hashing are
fine; do not reach for `Math.Pow` to shape a curve.

---

## Non-goals

- **No prefabs, no art.** The town looks identical in 2005 and 1991; only its reasoning changes.
- **Do not touch the witness layer.** `PersonDescription.CarriedThing` is deliberately vague — *Bag,
  Case, Bundle, LongObject* — because a witness says "something in his hand", not "a Nokia". That
  vagueness is the type's whole design. **This is the one item not negotiable.**
- **Do not fix the ageing bug or rewrite `particulars.txt`.** Both flagged at the bottom.

---

## Step 1 — DONE

`docs/research/TECHNOLOGY.md`, committed `5db18c8`. Curves in `year:percent` notation, each marked
**measured** or **inferred**, with the rural-correction rule at the top. Three rows are deliberately
not final: `mobilephone`, the `answermachine`/`cordless`/`callerid` trio, and `e911address`.

---

## Step 2 — `Core/World/Era.cs`

Generalise `Fields.DayWhen` and express adoption as a year.

```csharp
namespace Noir.Core.World
{
    /// A "percent reached by year" curve. Linear between waypoints, flat outside.
    public readonly struct Adoption
    {
        public bool  IsEmpty { get; }
        public float PercentIn(int year);      // 0..100, for tests and UI
        public int   YearWhen(float rank);     // invert: this rank's own year. Cf. Fields.DayWhen
    }

    public static class Era
    {
        /// Fixed rank in [0,1) for a key within one named thing.
        /// Mirror Fields.RankOf — avoid the low bits.
        public static float RankOf(ulong key, string salt);
    }
}
```

**CHANGED — invert, do not compare.** The original plan computed `PercentIn(year) >= percentile`.
Inverting instead — *this household adopts in 1999* — makes **monotonicity structural rather than
emergent**: a household cannot flicker because there is one crossing year, computed once. It also
matches `Fields.PlantedOn` exactly.

A falling curve needs both ends: `YearWhen` on the rise gives adoption, on the fall gives loss.
`payphone` is the test case.

**Semantics to implement exactly:**

| case | result |
|---|---|
| year before first waypoint | first waypoint's percent |
| year after last waypoint | last waypoint's percent |
| single waypoint | constant |
| empty / unparseable | `IsEmpty`; `PercentIn` → 0 |
| between waypoints | linear |

---

## Step 3 — `Content/technology.txt`

Header in the style of `animations.txt`: **adding a technology is a line here and nothing else.**
Parse with `ContentText.Tokenise` — do not hand-roll.

```
# name         scope      curve
mobilephone    person     1991:0  1996:3  2000:18  2003:40  2006:60
computer       household  1994:20 1998:40 2000:50  2006:65
dialup         household  1995:1  1998:22 2000:39  2004:50  2006:45
payphone       town       1900:100 1998:60 2005:10      # a technology can LEAVE
codis          town       1997:0  1998:100
cctv           town       1991:0                        # never arrives. A negative fact
```

Full set and sourcing in `docs/research/TECHNOLOGY.md`. **Carry the measured/inferred markings into
the comments — do not flatten them.**

**A deliberate divergence, worth knowing:** `Fields` hardcodes its crop curves in C# rather than a
content file. That is defensible — they are fixed research constants. Technology goes in a content
file because adding one should be a line, which is this project's stated doctrine everywhere else.
If that reads as inconsistent, it is a considered inconsistency and not an oversight.

**Scopes:** `town` · `household` · `person` · `farm` · `business`. `town` ignores the key —
`Has` returns `PercentIn(year) >= 50`, and town rows should be 0-or-100 curves.

---

## Step 4 — `Core/World/TechnologyTable.cs`

Mirror `PlaceKindTable`: static, `Parse` + `Install`, no file I/O.

```csharp
public static TechnologyTable Parse(string text);
public static void Install(TechnologyTable table);
public static bool  Has(string name, int year, ulong key = 0);
public static int   AdoptsIn(string name, ulong key);   // the crossing year itself
public static float Adopted(string name, int year);     // 0..100
```

**Inert when absent** — unknown name returns false, does not throw. A missing file means every query
is false, which is exactly the 1991 world and therefore a safe failure.

---

## Step 5 — Wire two consumers, not ten

1. **The LLM dialogue prompt.** A citizen's prompt carries what they have and what is ordinary this
   year. Highest-value consumer — it is what makes a 1991 conversation differ from a 2005 one.
2. **Reachability in the day plan.** Can this person be telephoned away from home? In 1991, no.

---

## Step 6 — CHANGED: an `[Explicit]` diagnostic, not an editor command

The original plan had `Noir/Check The Technology` as a Unity editor command, which **broke the
pass's own Unity-free promise.** `CountrysideDiagnostic.cs` is the better precedent and its rationale
applies directly:

> *"NOT AN ASSERTION — it is a way of looking at the thing, which is the only way some faults ever
> get found here. `CommercialRow`'s infill was laid under the lodge halls with all fourteen of its
> tests green."*

So: **`tools/Noir.Core.Tests/TechnologyDiagnostic.cs`**, `[Explicit]`, printing the town year by
year — how many households have each technology in 1991, 1995, 2000, 2006. Read it and check it
looks like a town rather than a spreadsheet. Runs under `dotnet test`, needs no editor.

---

## Tests — `tools/Noir.Core.Tests`

**Re-measure the baseline before starting.** It was 227/2 earlier today and a great deal has landed
since — `FieldsTests` alone is 327 lines. The two `TwoToOneTests` failures are by design.

- **determinism** — same name, year, key → same answer, repeatedly
- **monotonicity** — sweep 1991→2006; nobody loses a technology while its curve rises
- **loss** — on a falling curve (`payphone`) keys *do* lose it, lowest ranks last
- **boundaries** — year before first waypoint, year of, year after last
- **interpolation** — a midpoint year lands halfway
- **distribution** — over ≥10,000 keys the adopted fraction tracks `PercentIn` within ~2%
- **scopes** — `town` ignores the key; `person`/`household` do not
- **inertness** — unknown name → false, no throw; empty table → all false
- **agreement with `Fields`** — if `DayWhen` is generalised, `Fields`' existing tests must stay green.
  **That is the real regression check for Step 2.**
- **end to end** — no household has a mobile phone in 1991; most do by 2006

---

## Verification

```
dotnet test -c Release tools/Noir.Core.Tests
dotnet test -c Release tools/Noir.Core.Tests --filter "Name=PrintTheTechnologyYears" -l "console;verbosity=detailed"
```

**The whole pass is now genuinely Unity-free** — that was the point of the Step 6 change.

---

## Flagged, deliberately out of scope

**1. Nobody ages.** `Citizen.Age` is a fixed `readonly int` set at generation, and
`Households.Year = 1991` is a second hardcoded year whose comment says ages are worked against it
*"not against whatever year the machine thinks it is"* — true before the clock had a calendar.
**Over a fifteen-year game a seven-year-old stays seven.** Fix: store a birth year, derive from
`GameClock.Year`. Era-adjacent, not technology.

**2. `particulars.txt` is still English, 1979.** 914 clauses, 2.4 per citizen, so *every person in
Rossville* gets the shipping forecast, the pools, **Button B in the phone box**, the immersion, the
*Radio Times*, the **Home Service**, the mobile shop, cricket on the radio, and **Marlbury** —
Rossville's neighbouring town. Its own header still says *"1979, rural England."* `names.txt` was
retuned twice and says the Rossville pool was wrong on both counts; particulars never got that pass.

About nineteen clauses reference technology directly, so it is era-coupled as well as
country-wrong — **which is why `Era` is built general.** When particulars are rewritten for Illinois
the clauses can carry era ranges with no second mechanism. Large pass; scope it on its own.
