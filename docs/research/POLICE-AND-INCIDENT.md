# What actually happens in Rossville

Gathered 2026-08-03 from the *Commercial-News* (Danville) police blotter, Police Scorecard's
compilation of Illinois agency returns, and a search of digitised historic newspapers.

**No person is named anywhere in this file, and none was recorded while gathering it.** Where a
source named someone, the name was dropped at the point of reading, not paraphrased around. The
project's **NO REAL RESIDENTS** rule is the reason, and it holds here exactly as it holds for the
assessor's parcel data.

---

## The number that should shape the whole game

> **Zero homicides in Rossville between 2013 and 2025.**

Twelve years, none. And the village runs its own police department to notice: **four officers for
1,205 residents** on a **$186,000** budget — about $154 a head — making roughly **sixty arrests
across those twelve years**. That is **five arrests a year in the entire village.**

Of those sixty: **67% low-level non-violent**, 10% violent, 10% drug.

For a game whose premise is a killer in this town, that baseline is the premise. A murder in
Rossville is not an escalation of an existing crime rate — **there is no existing crime rate.** It
is the thing that has never happened. A town of 1,200 with four officers and five arrests a year
does not have detectives, a forensics budget, or anywhere to put a suspect; the response to a body
is the county sheriff at Danville, fifteen miles south, and the Illinois State Police.

That also sets the social physics. In a place with this little crime, **the news is the event**.
Everybody knows within the hour, everybody has a theory by evening, and the thing a player is
actually navigating is not a police investigation — it is twelve hundred people talking.

---

## The blotter, as texture

Nine Rossville-datelined items recovered. This is what the police in this town actually deal with:

| when | time | what | where |
|---|---|---|---|
| 28 Jan 2022 | 20:40 | domestic dispute | 300 block N Church St |
| 20 Mar 2022 | — | unlawful use of a weapon | Illinois 1 & 3330 North Rd |
| 26 Dec 2022 | 18:00 | fleeing / eluding police | Illinois 1 |
| 12 Jun 2023 | 21:15 | criminal trespass, theft | East 3550 North Rd |
| 19 Jun 2023 | 16:12 | retail theft | 700 block S Chicago St |
| 18 Aug 2023 | 15:16 | residential burglary | 15000 block Manns Chapel Rd |
| 25 Sep 2023 | 09:32 | personal injury accident | 100 block Stewart St |
| 23 Oct 2023 | 15:02 | burglary | 300 block Bitten St |
| — | — | possession of stolen property | 200 block Thompson St |

Read it as a distribution rather than as nine facts:

- **Nothing is stranger violence.** A domestic dispute, a weapons offence, thefts, two burglaries,
  a traffic injury. No robbery, no assault between strangers, no homicide.
- **Route 1 is where the driving offences are.** Fleeing and eluding, and the weapons call, both on
  Illinois 1. The through road carries the trouble that is not domestic.
- **Half of it is out in the township, not in the village.** East 3550 North Road, the 15000 block
  of Manns Chapel Road — these are county roads among the fields. A "Rossville" incident is often
  a farm-road incident. The 5:1 town/country split in the map is where the calls are too.
- **Afternoon and evening.** 15:02, 15:16, 16:12, 18:00, 20:40, 21:15 — one morning item, and that
  one is a road accident. Almost nothing overnight.

---

## Two things the blotter gave us that the map did not

**1. Rural roads are numbered, not named.** *East 3550 North Road*, *3330 North Road*, *1050 East
Road*, *1648 East Road* — Vermilion County uses the Illinois grid convention, where a country road
is its distance from the county baseline in hundreds of feet. Any road generator naming the
countryside *"Farm Lane"* or *"Country Road 3"* is producing the wrong state. **In the fields, a
road is a number.** In town it is a surname — Church, Stewart, Thompson, Gilbert, Henderson.

**2. Street names we do not have.** `tools/rossville-streets.json` holds 18 east–west and 11
north–south streets. The blotter names **Bitten Street** and **Manns Chapel Road** in Rossville,
and neither is in that file. Manns Chapel Road is certainly real — it runs to the 1855 chapel
`LANDMARKS-1906.md` and the buildings report both describe. *Bitten Street* is unverified and could
be an OCR or transcription slip for something else; treat it as a lead, not a fact.

**Address ranges, observed:** 100 Stewart, 200 Thompson, 300 Bitten, 300 N Church, 700 S Chicago.
Consistent with an ordinary hundred-block-per-block American grid numbered outward from the
crossing, with N/S prefixes on Chicago and Church. That matters directly: the story's one fixed
anchor is a house number — 408 Holmes Ave — and this is the first evidence of how numbering
actually runs here.

---

## The historical record — and what is not in it

**No Vermilion County newspaper is digitised in Chronicling America.** The Illinois titles there are
Chicago, Rock Island, East St. Louis and Cairo. Rossville's own weekly, the **Press-Independent**
(from 1904), is catalogued but not online. The Illinois Digital Newspaper Collections and the
Danville Public Library's microfilm both hold county papers, but IDNC blocks automated access. **A
person with a library card can read what a script cannot** — that is the route to Rossville's local
paper, and it is where a century of village incident actually lives.

What the national wire did carry: seventeen pages across the country pair "Rossville" with
"Vermilion county", and three of them cluster on **15–17 November 1911** around a **cyclone that
struck Wisconsin, Minnesota and Illinois with fourteen dead**. The buildings report records *"no
documented tornado strike on Rossville itself"* and points at a 1942 tornado that hit **Alvin**
instead. This 1911 storm is a lead on that gap — it is not yet evidence that Rossville was hit, and
the OCR was not read closely enough to say.

---

## Caveats that matter

**This is 2022–2025 data and the game is set around 2000.** Nothing here was recoverable for
1995–2006; the blotter archive online does not reach back that far and the local paper is not
digitised. The *mix* of offences in a farm village is likely stable across those decades, and the
argument for zero homicides is if anything stronger in 2000 than now — but this is an inference,
and it should not harden into a fact. The honest statement is: *in the two decades either side of
the setting, this town does not have murders.*

**The officer count disagrees slightly between sources** — three in one, four in another. Either
way it is a department you could fit in a car.

**What was deliberately not pursued.** No attempt was made to find, identify or reconstruct a real
violent crime with real victims in or near Rossville. In a village of twelve hundred, a real case
is identifying to anyone local **even with every name stripped**, and building a murder story on
one would point at people who are still alive and still there. The aggregate rate, the offence mix
and the street texture are what a simulation actually needs, and they carry no such freight.
Everything above is either a published statistic or a location and an offence type.
