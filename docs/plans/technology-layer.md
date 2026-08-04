# Year-specific technology, 1991 onward

## Context

The game opens in 1991 and runs to about 2006. `GameClock` now knows it — a real civil calendar
with `Year`, `Month`, `DayOfYear` and `Season`, epoch Monday 7 January 1991. **Almost nothing
consumes it.** The only year in the simulation's behaviour is a hardcoded `Households.Year = 1991`.

So the town is era-blind: a citizen in 2005 lives in exactly the 1991 world — same objects, same
reachability, same everything. For a story whose shape is *the town declines around the player*
(`docs/research/THE-TRAJECTORY.md`) and whose mechanic is *who knew what, when*
(`docs/research/WHO-SEES-WHOM.md`), that is the wrong kind of static. The sharpest instance is
already written down:

> In 1991 you cannot reach a person who is not at home, and an unanswered phone means nothing. By
> 2006 a person is reachable, and there is a log. **The same disappearance is a different event at
> the two ends of this game.**

This pass builds a **year-gated fact layer**. Core-only, Unity-free, no art. It answers *"does this
household have a mobile phone in 1999?"* so the day plan, the dialogue prompt and the investigation
can consult it.

**Author against the record, not against the art.** A technology goes in the table because the town
had it, not because a prefab exists. Asset packs come later; the facts should be waiting.

---

## What already exists — do not rebuild these

| thing | where | why it matters here |
|---|---|---|
| civil calendar | `Core/Contracts/GameClock.cs` | `Year`, `Month`, `DayOfYear`, `Season`, `TickOn(y,m,d)` |
| era-specific time rule | `Core/Contracts/Daylight.cs` | already handles **pre-2007 US DST** (first Sunday in April, not March). Precedent: rules themselves are year-specific |
| table parse idiom | `Core/World/PlaceKindTable.cs` | `Parse(string)` + `Install(...)`, static, no file I/O |
| per-key stable choice | `Unity/AgentAnimation.cs` `ClipFor` | hash of a citizen key → stable per-person pick. **Copy this idiom, don't invent one** |
| deterministic hashing | `Core/Contracts/Rng.cs` | already in the base assembly |

**Assembly direction (checked):** `Noir.Core.Contracts` has *zero* references and
`noEngineReferences: true`. `Noir.Core.World` → Contracts. `Noir.Core.People` → Contracts + World.

**`ContentLoader` is Unity-side** (`Assets/Noir/Unity/ContentLoader.cs`, uses `Application.dataPath`).
**Core cannot read files.** Follow the existing pattern — the Unity caller does
`TechnologyTable.Install(TechnologyTable.Parse(ContentLoader.Read("technology.txt")))`.

---

## Non-goals

- **No prefabs, no art, nothing rendered.** The town looks identical in 2005 and 1991; only its
  reasoning changes. Props read from this layer later.
- **Do not touch the witness layer.** `PersonDescription.CarriedThing` is deliberately vague — *Bag,
  Case, Bundle, LongObject* — because a witness says "something in his hand", not "a Nokia".
  Technology must not leak identifiable objects into observation.
- **Do not fix the ageing bug or rewrite `particulars.txt`** — both flagged at the bottom, both
  their own pass.

---

## Step 1 — Research the dates (no code)

**New: `docs/research/TECHNOLOGY.md`**, house style: sourced, confidence marked, inference labelled.

This project does not invent numbers, and most of these dates are unknown. Two are already recorded
as unresearched and **must not be guessed**:

- **When cellular actually reached downstate Illinois** — `WHO-SEES-WHOM.md` §5. Direction sound,
  dates unknown.
- **E911 rural addressing in Vermilion County** — chase this hardest. Rural Illinois addresses like
  *"1050 East Road"* (`POLICE-AND-INCIDENT.md`) were largely assigned *as part of* E911 rollout in
  the 1990s. **If true here, a farm in 1991 may have no street address at all**, which changes how
  anyone is found, directed or reported.

Also: cable vs satellite in a village of 1,200 (rural means satellite; the small dish arrives 1994);
home computer and dial-up; when eBay actually bit the antique trade; DNA/CODIS national (1998); farm
GPS and yield monitors; debit-card acceptance.

**Done when:** every row that will go in the table has a dated curve and a source, or is explicitly
marked inferred.

---

## Step 2 — `Core/Contracts/Era.cs`

General, so `particulars.txt` and `kinds.txt` can use it later without a second mechanism.

```csharp
namespace Noir.Core.Contracts
{
    /// year:percent waypoints. Linear between, flat outside.
    public readonly struct Adoption
    {
        public static Adoption Parse(string waypoints);  // "1996:2 2000:20 2003:55 2006:80"
        public float PercentIn(int year);                // 0..100
        public bool  IsEmpty { get; }
    }

    public static class Era
    {
        /// Stable 0..100 draw for a key within one named thing. Same inputs, same answer, forever.
        public static float Percentile(ulong key, string salt);
    }
}
```

**Semantics to implement exactly:**

| case | result |
|---|---|
| year before first waypoint | first waypoint's percent (flat) |
| year after last waypoint | last waypoint's percent (flat) |
| single waypoint | constant at that percent |
| empty / unparseable | `IsEmpty`, `PercentIn` → 0 |
| between waypoints | linear interpolation |

**Why a percentile and not a coin flip.** Each key draws a *fixed* percentile from
`Era.Percentile(key, name)`. It has the thing when `PercentIn(year) >= percentile`. That gives:

- **determinism** — same seed, same town, same answer forever
- **monotonicity on a rising curve** — nobody flickers in and out, because the percentile never moves
- **correct loss on a falling curve** — the last to give a thing up are the lowest percentiles
- **no stored state** — one hash per query

---

## Step 3 — `Content/technology.txt`

Header in the style of `animations.txt` / `kinds.txt`: **adding a technology is a line here and
nothing else.**

```
# name         scope      curve
mobilephone    person     1996:2  2000:20  2003:55  2006:80
answermachine  household  1985:15 1991:45  2000:70
cordless       household  1988:10 1995:55  2004:80
callerid       household  1993:2  1998:25  2005:55
vcr            household  1985:40 1991:95  2006:90
dvd            household  1998:1  2002:25  2006:65
satellite      household  1994:3  2000:22  2006:35
computer       household  1991:8  1997:30  2003:55
dialup         household  1995:1  1999:22  2003:45  2006:38
payphone       town       1900:100 1998:60 2005:10      # a technology can LEAVE
codis          town       1998:100
cctv           town       1991:0                        # never arrives. A negative fact.
gpsguidance    farm       1998:1  2002:12  2006:35
cardreader     business   1991:10 1997:45  2004:85
```

**Scopes:** `town` · `household` · `person` · `farm` · `business`.
`town` ignores the key entirely — `Has` returns `PercentIn(year) >= 50`. Town rows should be
0-or-100 curves.

**All four domains go in** (as chosen): household & personal, investigative & forensic, farm &
agricultural, commercial & retail. The numbers above are **placeholders pending Step 1** — do not
ship them unsourced.

---

## Step 4 — `Core/World/TechnologyTable.cs`

Mirror `PlaceKindTable`: static, `Parse` + `Install`, no file I/O.

```csharp
public static TechnologyTable Parse(string text);
public static void Install(TechnologyTable table);
public static bool Has(string name, int year, ulong key = 0);
public static float Adopted(string name, int year);   // 0..100, for UI and tests
```

**Inert when absent**, matching `AgentAnimation`'s stated principle: an unknown name returns
`false` rather than throwing, so a half-authored table is safe to run. A missing or unreadable file
means every query is false — which is exactly the 1991 world, and therefore a safe failure.

---

## Step 5 — Wire two consumers, not ten

1. **The LLM dialogue prompt** (the port in `Noir.Core`). A citizen's prompt carries what they have
   and what is ordinary this year. **Highest-value consumer** — it is what makes a 1991 conversation
   differ from a 2005 one.
2. **Reachability in the day plan** — can this person be telephoned away from home? In 1991, no,
   and an unanswered call means nothing.

---

## Step 6 — `Noir/Check The Technology`

Editor command in the established style of `Noir/Check The Animations` (`Editor/AnimationCheck.cs`):
list technologies nothing consumes, and names referenced in code but absent from the table. Writes a
short report. This is how the project keeps content tables honest.

---

## Tests — `tools/Noir.Core.Tests`

Baseline is **227 pass / 2 fail** (the two `TwoToOneTests` fail by design). Add:

- **determinism** — same name, year and key give the same answer, repeatedly
- **monotonicity** — sweep 1991→2006; no key loses a technology while its curve rises
- **loss** — on a falling curve (`payphone`), keys *do* lose it, lowest percentiles last
- **boundaries** — the year before the first waypoint, the year of, the year after the last
- **interpolation** — a midpoint year lands halfway between waypoints
- **distribution** — over ≥10,000 keys the adopted fraction tracks `PercentIn` within ~2%
- **scopes** — `town` ignores the key; `person`/`household` do not
- **inertness** — unknown name → false, no throw; empty table → all false
- **end to end** — *no household has a mobile phone in 1991; most do by 2006*

---

## Verification

**The whole pass is Core-only and Unity-free**, so it does not contend for the editor while the
other session has it.

```
dotnet test -c Release tools/Noir.Core.Tests
```

Expect **227 + new tests passing, 2 failing by design**. Then `Noir/Check The Technology` once the
editor is free.

---

## Flagged, deliberately out of scope

**1. Nobody ages.** `Citizen.Age` is a fixed `readonly int` set at generation, and
`Households.Year = 1991` is a second hardcoded year whose comment says ages are worked against it
*"not against whatever year the machine thinks it is"* — true before the clock had a calendar.
**Over a fifteen-year game a seven-year-old stays seven.** Fix direction: store a birth year, derive
age from `GameClock.Year`. Era-adjacent, not technology.

**2. `particulars.txt` is still English, 1979 — the biggest content problem in the project.**
914 clauses, drawn 2.4 per citizen, so *every person in Rossville* is described with British
details: the shipping forecast, the pools, **Button B in the phone box**, the immersion, the *Radio
Times*, the **Home Service** (renamed 1967), the mobile shop, the mobile library, cricket on the
radio, and **Marlbury** — Ashcombe's neighbouring town. The file's own header still states its rule
as *"1979, rural England."*

`names.txt` was retuned twice and says outright the Ashcombe pool was wrong on both counts;
`particulars.txt` never got that pass. About nineteen clauses reference technology directly, so it
is era-coupled as well as country-wrong — **which is precisely why Step 2 builds `Era` general**.
When particulars are rewritten for Illinois the clauses can carry era ranges with no second
mechanism. The rewrite is a large pass and should be scoped on its own.
