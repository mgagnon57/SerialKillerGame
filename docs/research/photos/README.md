# Pictures of the real town

Gathered 2026-08-03. **Reference only — none of this is shipped.** These photographs exist so the
geometry we build can be checked against the thing it is meant to be. Nothing here is traced, used
as a texture, or included in a build; the town gets modelled from scratch and these say whether
the model is right. The same rule the rest of the research follows applies: look at what was
actually photographed, and do not invent what is not in frame.

Sanborn sheets live next door in `../sanborn/` and remain the primary source for *fabric*. These
add the two things a fire-insurance map cannot: **what the buildings look like standing up**, and
**what the town looks like from the air**.

There is also a browsable digest of the whole research set — the sourcing doctrine, the provenance
chain, the measured town, and what is contested or invented — at
<https://claude.ai/code/artifact/78ed559d-f9a3-4208-b609-5cb4b0057ea0>. It summarises the documents
in `..`; it is not a source in its own right.

---

## What is here

| file | what | when |
|---|---|---|
| `aerial-1940-flight4-exp2-88.jpg` | the whole town from the air, 7062 × 7012 | **6 Jul 1940** |
| `aerial-1940-town-crop.jpg` | the same frame cropped to the built-up area | 6 Jul 1940 |
| `downtown-2007-pd.png` | the commercial row, looking at the crossing | **7 Oct 2007** |
| `welcome-sign-pd.png` | the village welcome sign on the approach | c. 2007 |
| `farmstead-2019-cc.jpg` | drone view of a farmstead outside town | 28 Sep 2019 |
| `flickr/` | **25 photographs of the town**, June 2010 | 2010 |

Provenance: the 1940 aerial is USDA survey work distributed by the Illinois State Geological
Survey. `downtown-2007-pd.png` and `welcome-sign-pd.png` are Wikimedia Commons, released public
domain by *Omnedon*; `farmstead-2019-cc.jpg` likewise from Commons. **`flickr/` is Raymond
Cunningham's photography, All Rights Reserved** — held here as visual reference and for no other
purpose. If any of this ever needs to leave the repo, that folder is the one that cannot.

---

## The 1940 aerial — the most useful new document

Not previously found. It matters because it sits **between** the two sources already held: the 1913
Sanborn sheets and the modern parcel data. The town's population was 1,428 in 1940 against 1,217 in
2000, and the median house was built in 1943 — so the 1940 frame is close to the town at its
fullest, and its street-by-street footprint is essentially the footprint of the town the game is
set in.

What it shows that nothing else did:

- **The town is a grove.** The built-up blocks are almost completely under mature canopy — from the
  air the street grid is barely visible through the trees, while the farmland around is bare. A
  prairie town reads as an island of trees. Anything drawing Rossville as open lots with young
  saplings is wrong in a way that shows from any distance. The 2010 street photographs confirm it
  at ground level: every residential view is deep shade under large hardwoods.
- **The edge is hard.** Blocks stop, fields start, almost no transition — the same thing the 1913
  sheet says by printing *"FARM LAND"* at its margin.
- **Industry is strung along the rail at the south-east**, in large bright-roofed sheds set at the
  *rail's* angle rather than the street grid's. Houses stay on the grid. That is
  *"industry followed the rail; houses followed the grid"*, visible from above.
- **The grid and the railroad disagree, plainly.** Blocks cardinal, line NNW–SSE across them.
- **The North Fork corridor** is a wide dark wooded ribbon along the **west** side with old oxbows
  in it — a real landscape feature, not a drainage ditch.
- **Field texture** in many tones, with the drainage pattern legible in the soil as broad wavy
  bands. The tile the Redden works was making by the eight-thousand a day is what put those there.
- Isolated farmsteads, each in its own tight clump of shelterbelt trees — matching
  `farmstead-2019-cc.jpg` exactly, eighty years apart.

### Re-deriving the crop, or finding frames anywhere else

The county index is a shapefile of photo centres in NAD83 lat/lon:

```
https://clearinghouse.isgs.illinois.edu/webdocs/ilhap/county/data/indexes/points/vermilion.zip
```

561 exposures. Parse `vermilion_pts.shp` for points and `vermilion_pts.dbf` for `FILE_NAME` and
`URL`, then take the nearest centre. For the crossing (40.3793, −87.66897) that is **flight 4,
exposure 2-88**, centre 0.89 km away:

```
.../ilhap/county/data/vermilion/flight4/00al02088.jpg
```

The crop assumes a 1:20,000 frame on a 9-inch negative — **4,572 m across 7,062 px, so 0.647 m/px**.
That scale is *taken from the standard USDA specification, not measured off the frame*, so treat
derived distances as approximate until something of known ground length is measured in the image.
`00al02089.jpg` covers the ground to the west.

---

## The downtown

`downtown-2007-pd.png` carries GPS: **40.379847 N, −87.669235 W, heading 150°** — about **65 m
north-north-west of Attica × Chicago, looking straight back at it**. The traffic signal in that
frame is the junction this simulation uses as its origin.

**The decay rule is visible, and it is the rule `COMMERCIAL-ROW.md` read off the 1913 survey.**
Two-storey and ornate at the corner; lower, plainer, narrower away from it. Transcribed from a map,
still standing in a photograph.

- **The corner anchor is a bank** — buff/tan brick on a stone base with banding, heavy projecting
  cornice, tall storefront bays divided by brick piers, flat roof behind a parapet. The most
  deliberate building on the street, which is what a small-town bank is for.
- **The rest is red-brown brick**, two storeys at the crossing, corbelled cornices, segmental-arched
  upper windows, painted timber or pressed-metal upper facades on the older units. Ground floors
  almost entirely glass between piers.
- **Some upper storeys are shingled and gabled** rather than flat-parapeted — a Victorian storefront
  type sitting in the same terrace as the brick ones.
- **Shopfronts are infilled.** Several bays boarded and painted flat colours. That is the *"period
  of decay in our buildings"* the News-Gazette describes. A row of thriving shops is the wrong town.
- **Blank party walls are exposed** in more than one view, with low modern infill or open grass
  beside them. That is the 2004 fire. **For a 2000 setting the terrace is continuous and those
  walls are not exposed** — a useful negative check.

**The approach matters as much as the row.** Looking along Chicago Street toward the signal, the
street is heavily treed and reads residential right up until the commercial block, which arrives
suddenly and is *short*. The roadway is very wide relative to the buildings — a broad asphalt apron
with grass verges and set-back sidewalks. A brick church tower with Gothic louvred openings stands
on the approach and is a genuine landmark from a distance.

**Street furniture:** ornamental lampposts with white globes (dark green in some views, black in
others), mast-arm signals at the crossing, painted crosswalks, concrete kerbs, planters and barrel
tubs, US flags on short poles, awnings. A **green tractor with a front loader parked at the kerb on
the main street** — the single most characteristic thing in any of these photographs. Streets are
wide and nearly empty; two or three vehicles is a busy frame.

Caution: the ornamental lampposts and planters are streetscape work possibly funded by **post-2004
TIF money**. Nothing confirms they stood in 2000.

---

## Houses — and a prediction confirmed

The buildings agent report inferred, without evidence, that Rossville would have *"one or two
'showpiece' Queen Annes... and a much larger quantity of plainer vernacular and foursquare
houses."* It flagged this as inference rather than fact.

**`flickr/home-in-rossville-illinois-2.jpg` is that Queen Anne**, photographed: pale clapboard, a
**round corner tower with a conical shingled roof**, a **porch that wraps the corner** on turned
columns, cross gables with decorative shingle in the gable ends, two storeys plus attic, a brick
foundation with lattice skirting, set well back in a deep lawn under mature trees. The wrapping
porch independently confirms `RESIDENTIAL-1913.md`'s correction that porches *"wrap corners as often
as they run straight across a front."*

It is a **minority type** — the record is clear that the bulk of the stock is plainer — but it is
exactly the sort of house a murder story wants, and it exists.

**A brick house is probably a converted institution.** `home-in-rossville-illinois-4.jpg` is red
brick with paired **Gothic pointed-arch windows**, an arched entry hood, bracketed bargeboards and
a steep gable dormer — a chapel or small church now lived in. That is consistent rather than
contradictory: the 1913 census found exactly five brick buildings outside downtown and judged them
institutional. **A brick dwelling in Rossville should be a converted one, not a built one.**

---

## The silhouette

`ROSSVILLE-HISTORY.md` §8 says the water tower, the grain elevators and the 1903 depot are what
read from a distance. All three are photographed in `flickr/`.

- **The depot** is red-brown brick, single storey, under a **wide hipped roof with deep overhanging
  eaves** — the standard railroad form — with a lighter stone base course, white-framed windows and
  a trackside name board reading **ROSSVILLE**. The main line beside it is single track on heavy
  ballast, dead straight to the horizon. A modern metal shed with a roller door stands opposite,
  which is what most trackside industry has become.
- **The grain storage is modern steel, not the 1913 elevator.** Three corrugated galvanised bins of
  different heights with conical vented roofs, the tallest carrying a railed walkway and a side
  ladder, linked by **overhead conveyor gantries on steel trestles**. Gravity wagons and augers lying
  about on a gravel apron. The wooden and concrete elevators of the Sanborn era are gone.
- **A dwelling stands immediately beside the bins.** There is no zoning buffer — industrial and
  residential interleave directly at the town edge.

---

## The welcome sign, which settles nothing and says a lot

`welcome-sign-pd.png` — dark red boards, gold routed lettering, two rough timber posts:

> **Welcome to ROSSVILLE** · antiques · gifts · collectables · **Est. 1859 … on Hubbard Trail**

Two things worth having. The town asserts **1859** in painted wood at its own entrance — the date
`SOURCE-PROVENANCE.md` records as appearing only in modern sources and uncorroborated by either
county history. The village believes it enough to sign it. And it brands itself on **antiques** in
a photograph taken *after* the fire that ended the trade.

Beside it: a **"HISTORIC ROUTE — DH — DIXIE HIGHWAY"** marker, a *"Keep Vermilion County Beautiful"*
sign, and a **"THIS IS A DARE COMMUNITY"** sign — the last period-perfect for the game's window and
the kind of detail no map or census will ever supply.

---

## Anachronisms these photographs would introduce

Everything here is 2007–2019 except the 1940 aerial. Three things are visible that must **not** be
in a town set in 2000:

1. **Wind turbines** along the horizon in `farmstead-2019-cc.jpg`. In 2000 that skyline is empty.
2. **The fire station** — modern metal-clad, maroon standing-seam roof, stone wainscot, three
   overhead doors, north of centre. This is the *"relatively new"* station the buildings report
   mentions and very unlikely to have stood in 2000.
3. **The exposed party walls and cleared lots** downtown. Those are the 2004 fire. In 2000 the row
   is whole.

---

## What could not be got

- **Facebook.** The Rossville (Ill.) Historical and Genealogical Society keeps an active page and
  it is almost certainly the largest collection of photographs of this town anywhere. It is behind
  a login wall and returns nothing to a fetcher. It is also full of **named living people**, which
  the repo's no-real-residents rule keeps us out of regardless. The way to get those photographs is
  to ask the society — 108 W Attica St, (217) 748-4080.
- **Pinterest** (a 200-pin Rossville board) is JS-rendered and returned nothing.
- **CardCow** and other postcard dealers sit behind bot checks. Period postcards of the business
  section around 1909–1910 are known to exist and would show the row **complete and in use**, which
  is the one view none of these photographs give.
- No pre-2004 photograph of the burnt block was found. The News-Gazette keeps a fire archive but it
  yielded no images to a fetcher.
- Five of the thirty photographs in the Flickr album were lazy-loaded and their IDs never appeared
  in the page source; 25 of 30 are here.
