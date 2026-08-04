# What the town has, and when it gets it

Compiled 2026-08-03. **Step 1 of the year-gated technology plan** — the dates that
`Content/technology.txt` is authored from. The game opens in 1991 and runs to about 2006, and
nothing in the simulation currently knows the difference.

**Read the curves as `year:percent` waypoints**, which is the notation the table uses: linear
between, flat outside. They are written here so a row can be pasted straight across, and so that
every number in that table can be traced back to something.

---

## The principle that decides where the data runs out

The best source here is the US government measuring exactly this, in exactly this window: NTIA's
**Falling Through the Net** series (1995, 1998, 1999, 2000), which surveyed household technology
**by rural / urban** using Census CPS supplements. It gives rural figures directly for the
telephone, the computer and the internet.

For everything else the data is national, and has to be corrected for a farm village of 1,200. The
correction is not one number, and this is the useful part:

> **Affordability-limited technologies show a SMALL rural lag. Coverage-limited technologies show a
> large one.**

NTIA bears this out. In 1998 rural households were at **39.9%** for a computer against **42.1%**
nationally — barely two points behind — and by 2000 rural was **50.4%**, essentially converged.
Rural internet was **22.2%** in 1998 against 26.2% nationally, and **38.9%** by 2000. A rural
household that could afford a computer bought one at nearly the same rate as anybody else.

But a mobile phone in 1996 is not an affordability question, it is a **tower** question, and a
village fifteen miles from the nearest small city is at the edge of somebody's coverage map.
Likewise cable, which never came to most places this size at all — hence satellite.

**So: apply a small lag (0–3 years) to things you buy, and a large one (3–7 years) to things that
need infrastructure.** Where a curve below is corrected rather than measured, it says so.

---

## Household & personal

### Telephone — already universal, and that is the point

```
telephone      household  1991:94  2006:95
```

Rural telephone penetration was **94.3%** and had been flat for years — *higher* than the national
average. **Confidence: measured (NTIA).**

The interesting thing is not the number, it is what a phone was in 1991: fixed to the house. See
`WHO-SEES-WHOM.md` §5 — to check on somebody you go there, and an unanswered phone means nothing.

### Answering machine, cordless, caller ID

```
answermachine  household  1991:45  1996:65  2006:70
cordless       household  1991:30  1997:60  2006:80
callerid       household  1993:2   1998:20  2006:50
```

**Confidence: inferred.** No rural series was found for any of the three. Answering machines were
common by 1991 and cordless handsets were spreading; caller ID needed the exchange to support it
and was billed as an extra, so it is coverage-limited *and* pay-limited and should lag hard.
**These three are the weakest curves in the file** and are the first thing to correct if a better
source turns up.

### Mobile phone — the fifteen-year arc

National subscriber counts (CTIA) against US population:

| | subscribers | ≈ per capita |
|---|---|---|
| 1991 | 7.6 M | ~3% |
| 1995 | 33.8 M | ~13% |
| 2000 | 109.5 M | ~39% |
| 2005 | 207.9 M | ~70% |

```
mobilephone    person     1991:0  1996:3  2000:18  2003:40  2006:60
```

**Confidence: national measured, rural correction inferred — and the correction is large.**
Subscriptions are not people (some carried two), early adoption was disproportionately urban and
business, and **coverage is the binding constraint here, not price**. The curve above is shifted
markedly later than the national figures and should be treated as the most consequential guess in
this document, because `WHO-SEES-WHOM.md` builds the game's central information arc on it.

**Still unresearched: when cellular coverage physically reached this part of downstate Illinois.**
Flagged in `WHO-SEES-WHOM.md` §5 and still open.

### Computer and internet — measured, rural, era-exact

```
computer       household  1994:20  1998:40  2000:50  2006:65
dialup         household  1995:1   1998:22  2000:39  2004:50  2006:45
```

**Confidence: measured (NTIA), rural figures.** Rural computer ownership **39.9% (1998) → 50.4%
(2000)**; rural internet **22.2% (1998) → 38.9% (2000)**, a 75% rise in two years. The 1994 anchor
is national PC ownership at 24.1% with rural a little under.

The `dialup` curve turns over near the end on purpose — broadband begins displacing it after about
2004, though in a village this size that displacement is slow and mostly satellite.

**This is the curve that matters for the story**: `THE-TRAJECTORY.md` has eBay eroding the antique
trade "from 1995 onward", but **only 22% of rural households were online in 1998.** The trade was
not killed by Rossville getting the internet; it was killed by *everybody else* getting it, and
buyers no longer needing to drive.

### Television, video, satellite

```
vcr            household  1991:72  1999:89  2006:85
dvd            household  1997:0   1999:7   2002:30  2006:81
satellite      household  1994:1   2000:15  2006:30
```

**Confidence: DVD and VCR measured nationally (Nielsen — DVD 6.7% in 1999, 30% by 2002, 81.2% by
Q3 2006; VCR 88.6% in 1999 and declining by 2006). Satellite inferred.**

DBS — the small dish — launched in **1994**. Satellite matters disproportionately here because
**cable almost certainly never served a village of 1,200**; if a Rossville house has many channels
in 1998, it has a dish. That is a visible roof-line fact when props eventually arrive.

---

## Investigative & forensic

```
codis          town       1997:0   1998:100
cctv           town       1991:0
e911address    town       1991:0   1996:100     # DATE UNVERIFIED - see below
```

**CODIS** — the FBI's national DNA index went live in **1998**. Before that there is no national
database to search, only a sample to compare against a suspect you already have.
**Confidence: well established.**

**CCTV is a negative technology and belongs in the table as one.** A village of 1,200 with four
officers and five arrests a year has essentially no camera coverage in this era — no municipal
system, no ATM cameras outside the bank, and Casey's does not exist until after 2004. A search that
returns nothing is a finding: model it as zero, explicitly, so nothing downstream assumes footage.

**E911 rural addressing — chase this, do not ship the date above.** Illinois counties assigned
rural addresses on a grid: distance from a county reference axis, ~1,000 addresses per mile, even
numbers one side and odd the other. Coles, McLean, LaSalle and Champaign counties all document the
scheme. That is exactly the form of *"East 3550 North Road"* in `POLICE-AND-INCIDENT.md`.

**But no date was found for Vermilion County.** A Vermilion County Emergency Telephone System Board
exists at Danville; when it formed and when rural addressing was assigned is not established.
**If it happened mid-window, then a farm in 1991 has no street address at all** — which changes how
anybody is found, directed, or reported missing. **Confidence: mechanism certain, date unknown.**
The route to an answer is the county ETSB or county board minutes, not another web search.

---

## Farm & agricultural

```
yieldmonitor   farm       1992:0  1996:3   2000:15  2006:35
gpsguidance    farm       1997:0  2000:3   2003:12  2006:30
```

**Confidence: inferred, and weakly.** Yield monitors appeared on new combines in the mid-1990s and
GPS guidance followed at the end of the decade at a price only larger operations could carry. No
Illinois or Vermilion County adoption series was found. USDA's ARMS surveys track precision
agriculture and would settle this properly.

Worth remembering that this is a **farm-scope** row and there are far fewer farms than households,
so a low percentage is a very small number of actual machines.

The social one is not a machine: **the mobile phone in the tractor cab** changes how isolated a
farmer is, and it arrives on the `mobilephone` curve, not this one.

---

## Commercial & retail

```
cardreader     business   1991:15  1997:45  2004:80
atm            town       1991:100
caseys         town       2004:0   2006:100
```

**Confidence: inferred.** Debit cards spread through the 1990s; a small-town shop is a late adopter.

The point for the game is not the machine, it is the record:

> **A card transaction says where somebody was. In 1991, cash leaves nothing.**

`caseys` is in the table as a *place gate* rather than a technology — Casey's is built on the corner
cleared by the **February 2004** fire (`THE-TRAJECTORY.md`), so it must not exist before then. This
is the row that demonstrates why `kinds.txt` should eventually share the era mechanism.

---

## Still unresearched — do not invent these

1. **When cellular coverage reached downstate Illinois.** The single most consequential gap, because
   the game's information arc is built on it.
2. **Vermilion County E911 addressing date.** Determines whether rural properties have addresses at
   the opening.
3. **Cable vs satellite in Rossville specifically.** Assumed no cable; unverified.
4. **Farm precision-agriculture adoption for east-central Illinois.** USDA ARMS would settle it.
5. **Local telephone exchange capability** — when the Rossville exchange supported caller ID, touch
   tone billing, and so on. This is a local telco question and is probably unanswerable from here.

Items 1, 2 and 5 are the kind of thing the historical society, the county ETSB, or a local telephone
co-operative could answer in a phone call, and no further searching from here will.

---

## Sources

- **NTIA, *Falling Through the Net*** — 1995 (1994 data), 1998 (1997 data), 1999 (1998 data), 2000.
  Household technology by rural/urban, from Census CPS supplements. The rural computer, internet and
  telephone figures above are read directly from these.
- **CTIA semi-annual wireless industry survey** — US subscriber counts by year.
- **Nielsen**, via trade press — DVD and VCR household penetration.
- **FBI** — CODIS national index, 1998.
- Illinois county E911 addressing schemes — Coles, McLean, LaSalle, Champaign county published
  descriptions, for the mechanism.

**A standing caution.** Most non-NTIA figures are **national**, and this town is not national. Every
curve above that is not marked *measured* carries a rural correction that is judgement, not
evidence. The correction rule is at the top of this file; where a curve is load-bearing, the honest
move is to widen the uncertainty rather than sharpen the number.
