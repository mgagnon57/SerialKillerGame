# Which county history is which — and what each one can carry

Written 2026-08-03 after a misattribution survived three documents. **Read this before citing a
"county history."** There are three of them, they are not independent, and the difference decides
how much weight a corroboration is worth.

---

## The three volumes

| # | year | author | archive.org id | read here? |
|---|---|---|---|---|
| 1 | **1879** | **Hiram W. Beckwith** | `historyofvermili00beck` | quoted via research agents |
| 2 | **1911** | **Lottie E. Jones** | (2 vols) | **never opened** |
| 3 | **1930** | **Jack Moore Williams** | `historyofvermili01will` | **read in full text** — this is the one on disk |

The file downloaded to scratch and quoted throughout this research is **#3, Williams 1930**. It
identifies itself in its own preface, signed *"Jack Moore Williams. Danville, Illinois. March 11,
1930."*

> **The trap:** the archive.org identifier `historyofvermili01will` looks like it could be any of
> them, and searching "History of Vermilion County" returns all three interchangeably. An earlier
> draft of `ROSSVILLE-HISTORY.md` credited passages to "the 1911 Lottie E. Jones history" that
> actually came from Williams 1930. The research agents had it right; the master document
> introduced the error while summarising. **Check the signature block, not the search result.**

---

## They are not independent — and this is the part that matters

Williams says outright, in his own preface, what he built from:

> *"The histories of Vermilion County by Hiram W. Beckwith and the later one by Miss Lottie Jones,
> the Centennial Book by Clint Clay Tilton, and the History of Hoopeston, by S. V. Cox … have been
> sources of considerable material, as well as have been the files of the Commercial News, the
> Morning Press, and other old newspaper files."*

And on his method:

> *"former publications of a kindred nature and newspaper files have been freely consulted **in an
> effort to reconcile some of the discrepancies of earlier writers**."*

So the dependency runs **Beckwith 1879 → Jones 1911 → Williams 1930**, each reading the ones
before it.

### What follows from that

1. **"Both county histories agree" is weak evidence, not strong.** When Williams 1930 matches
   Beckwith 1879 in near-identical language — as it does on the 1857 plat and the Lafayette,
   Bloomington & Muncie bypass — that is Williams **copying Beckwith**, not a second witness. One
   source, quoted twice.
2. **Where Williams *differs* from Beckwith, the difference may be deliberate.** He says he set out
   to reconcile discrepancies. A changed date in Williams is worth checking against Beckwith rather
   than averaging with it.
3. **Beckwith 1879 is the closest thing to a primary source for the 1870s.** Published seven years
   after the 1872 incorporation, in the county, by someone who could have asked people who voted.
   For anything before 1879, prefer Beckwith and treat Williams as a transcription of it.
4. **For 1880–1930, Williams is the only one of the three that can speak**, and he had newspaper
   files. The tile works, the schools, the 1893 fire aftermath, the population figures — Williams
   is the source and there is no cross-check inside this set.

### The rule

> **Count sources, not citations.** Three books repeating Beckwith is one source. A Sanborn
> surveyor's annotation and a county assessor's record are two, and they are independent of the
> books and of each other — which is why the quarter-acre lot median, confirmed at 1,011 m² by GIS
> geometry and 1,012 m² by assessed acreage, is the best-evidenced number in this whole research
> set.

---

## What each source is actually good for

| source | trust it for | do not trust it for |
|---|---|---|
| **Beckwith 1879** | settlement, the plat, the roads, the railroads' arrival, the 1872 incorporation | anything after 1879 |
| **Williams 1930** | 1880–1930: industry, schools, paving, population, photographs | independent confirmation of Beckwith |
| **Sanborn maps** (1898/1906/1913) | **fabric** — footprints, materials, storeys, frontages, use | anything not drawn; they stop at the sheet edge |
| **County assessor / GIS** | lot geometry, improvement rates, aggregate value | anything about people — and it is not read for that |
| **Village website / Wikipedia** | the modern town, the antique era, the 2004 fire | the founding date (see `ROSSVILLE-HISTORY.md` §9) |

---

## Standing cautions

- **Search returns Rossville, Georgia / Indiana / Tennessee.** Anything not naming Vermilion County
  or Illinois is a different town.
- **The "Opera House built 1908 by Alexander Bell McRae" claim has no source.** The opera house is
  real — drawn on the 1913 sheet with a Masonic lodge above it — but that date and that name
  appear only in AI-synthesised search summaries. It will resurface. Do not believe it.
- **archive.org needs a User-Agent header** or it returns 403 on both the API and image downloads.
- The **1927 and 1933 Sanborn atlases return zero resources** — never digitised, not a fetch bug.
