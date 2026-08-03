# Who lives there

Compiled 2026-08-03. Two corrections to things the project currently believes, one of them
provable from material already in this repo, plus what binds the town together.

**No living person is named here.** The surnames discussed are nineteenth-century figures from
published county histories and the town's own street signs — the same public historical record the
rest of this research draws on.

---

## The correction: the German layer is a Danville phenomenon, not a Rossville one

`Content/names.txt` states that Rossville was *"settled out of Kentucky, Ohio and Indiana with a
heavy later German layer and an Irish minority that came for the railroad."* The first half is well
supported. **The heavy German layer is not, and the evidence against it is already in
`agent-reports/rossville-buildings.md`.**

**Denominations are a demographic fingerprint.** German settlement in the nineteenth-century
Midwest is legible in churches before it is legible in anything else: German Lutheran and German
Catholic congregations appear wherever the population does, often with services in German.

Rossville's churches, from the 1913 Sanborn sheet and the county histories:

| denomination | tradition |
|---|---|
| **Presbyterian** — organised 1850, built 1869 | Scots-Irish |
| **Methodist Episcopal** — built 1869 | the frontier circuit-rider denomination |
| **Christian** (Disciples of Christ) | Stone–Campbell, born on the Kentucky/Ohio frontier |
| **United Brethren** | German in *origin*, but English-speaking and widely spread by this date |

**No Lutheran church. No Catholic parish.** And that absence is not for want of Germans in the
county — **Danville, fifteen miles south, has both**, including a Trinity Evangelical Lutheran
founded by fourteen German Lutheran families and a **German Catholic church holding services in
German**. Vermilion County's five Catholic parishes are at Hoopeston, Danville (two), Westville and
Georgetown. **None is in Rossville.**

So the county's German population is real and it concentrated at the county seat and the larger
towns. Ross Township's own religious profile is Anglo-American upland-South and Ohio-Valley, which
is exactly what the settlement record says: settlers out of Kentucky, Ohio and Indiana. Mann's
Chapel — the township's first church building — was burned and built by a man who *"emigrated
directly from England in 1832."*

> **Recommendation:** keep Kentucky / Ohio / Indiana. Keep the Irish railroad minority, which is
> plausible and unexamined. **Drop "heavy" from the German layer** — a few German families in a
> town this size is fine and likely; a heavy layer would have built a Lutheran church, and there
> isn't one.
>
> *Confidence: strong on the absence of Lutheran and Catholic congregations in Rossville, which is
> attested twice; weaker on how many individual German families that leaves, which nothing here
> measures.*

---

## The drift: the name pool is tuned to 1991, the town is set in 2000

`Content/names.txt` opens *"Names for Rossville, Illinois - 1991"* and builds its cohorts on that:

```
the very old   born 1900-1926
older adults   born 1927-1945
middle-aged    born 1946-1965
young adults   born 1966-1973
the children   born 1974-1991      <- the -child lists
```

But `SOURCING.md` pins the town to **around 2000** — before the February 2004 fire, before the 2006
school closure. The names file predates that decision and was never moved.

**Nine years is nothing for the adults and everything for the children.** The adult pool spans
ninety years deliberately, and sliding it a decade changes almost nothing — the same people are in
the same post-office queue, nine years older. But the child list is bounded at 1991, so **every
child born 1992–2000 is missing**, which is most of the primary school.

That is the same class of error the file itself documents and fixed once already: it notes that
`male-child` and `female-child` were silently ignored, so *"the village had seven-year-olds called
Clarence."* A 7-year-old in 1991 was born 1984 — Jason, Ashley, Dustin. A 7-year-old in **2000** was
born **1993**, which is a different set of names entirely.

**The fix is content, not code:** extend the child cohorts to births 1982–2000 and reset the header
year. The adult list can stay as it is.

*(Also minor: the sizing note reasons from "1,331 people", the 2010 census. The 2000 figure — the
setting's own year — is **1,217**. It does not change the conclusion that about two households per
surname is right.)*

---

## The thing that binds the town, and the thing that ends

**Rossville-Alvin High School — the Bobcats, red and white**, at 350 N Chicago Street, which is on
the main road through town.

In a village of twelve hundred the high school is not one institution among several; it is the
place the whole town assembles, and in downstate Illinois that means **basketball** more than
anything else. A district this size fields a team drawn from a couple of dozen eligible boys, and
the gym holds a meaningful fraction of the population.

**The board voted to close it in 2005 and it shut in 2006**, for money. Students were given a
choice between Bismarck-Henning and Hoopeston Area — *rivals* — and today the co-operative is
Bismarck-Henning-Rossville-Alvin. The elementary school stays open in the village.

That falls **inside the game's window**, two years after the fire. `ROSSVILLE-HISTORY.md` already
says it plainly: *"in a town this size, losing the high school is not an administrative footnote —
it is the thing that happens to a place."* For a story set 1995–2006, the town loses a quarter of
its downtown and then its school within twenty-four months, and the school is the one that takes
the Friday nights with it.

**For the build:** a player in 2000 sees an open high school on Chicago Street with a full car park
on a winter Friday. The Bobcats are red and white. That ends before the story does.

---

## The surname pool is written on the street signs

The names file already found this and it is worth recording as research rather than as a comment in
a content file: **in an American plat town the street names are the founding families.** Rossville's
own streets give a period-correct, locally-true, publicly-documented surname pool:

> Henderson · Gilbert · Stewart · McKibben · Holmes · Benton · Thompson · Harrison · Dale ·
> Greenwood · Watson · Goodwine · Perry · Smith · Green · York · **Stufflebeam**

And the county histories add the rest of the founding generation, all nineteenth-century and all
public record:

> Liggett · Ross · Satterthwait · Livingood · Armstrong · Davis · Comstock · Prillaman · Habel ·
> Redden · Putnam · Merritt · Cronkhite · Austin · Swift · Andrews · Chambers · Bicknell ·
> Purviance · Warner · Tuttle · Laidlow · Lefevre · Duly · McTaggart · Foulke · Mann · Kingsbury ·
> Whitcomb · Upp · Gessie · Sloat · Heaton

`Stufflebeam` is the one to keep hold of — a real and distinctly local name, on a real street, and
the sort of thing no generator would ever invent.

---

## Not pursued

The **November 1911 wire story** linking "Rossville" and "Vermilion county" to a cyclone has been
dropped. It named the county, not the village, and there is no evidence any storm struck Rossville
itself — the nearest documented tornado hit **Alvin** in 1942. If it was anywhere, it was a
neighbouring town.
