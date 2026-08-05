# Two-mode parcel editing — design

**Date:** 2026-08-05
**Status:** approved, not yet planned or built

## The problem

The parcel editor draws one panel with two unrelated jobs in it: who lives on the lot (left
column, 330px) and what the lot is (right column, 330px). Both are cramped, both scroll, and
the person row has just gained a sixth control. Worse, a person exists **only** as a line
underneath a lot, so there is no way to express somebody who is between houses — and no way to
move a family from one lot to another except by retyping them.

## What we are building

1. A tab strip splitting the panel into **who lives here** and **the lot**, each getting the
   panel's full width.
2. People become **records in their own right** with stable ids. A lot points at the people
   living on it rather than containing them.
3. A **roster** of unhoused people — everyone no lot points at — which is where somebody goes
   when you remove them from a house, and where you take them from to put them in another.

## What we are NOT building

- **No connection to the simulation.** `Noir.Core.People.Citizen` — the 148 who actually walk
  around, each with a `Home`, a `Household` and a `Job` — is untouched. Authored people are
  authoring data. Wiring the two together is a separate, larger piece of work, and the format
  below is chosen so it can happen later without another migration.
- **No per-person behaviour text.** `Note.Character` stays on the lot. It answers "what is this
  family like", and splitting it per person is a different question.
- **No repair tool for the 13 stranded citizens.** That is a simulation bug, tracked separately,
  and putting it in this list would mean making authored people and sim citizens the same kind
  of thing — which is exactly what the first bullet defers.

---

## 1. Data model

### 1.1 Person becomes a record

`ParcelNotes.Person` gains:

```csharp
/// Stable for the life of the person. Assigned once, never reused - see PeopleStore.NextId.
public int Id;
```

Everything else it already has stays: `First`, `Last`, `Age`, `Child`, `Which` (the
Man/Woman/Unrecorded sex), `Traits`.

### 1.2 People live in their own store

A new `ParcelNotes.People` dictionary, `int -> Person`, sits beside the existing `_byId`
dictionary of notes. It is loaded, saved and rewritten by the same `Load()`/`Write()` pair, so
there is still exactly one file and one atomic write.

### 1.3 A Note points at people rather than owning them

```csharp
// WAS: public readonly List<Person> People
public readonly List<int> Lives = new List<int>();
```

`Note.People` is removed. Every caller moves to resolving ids through the store. The known
callers are `VillageUI` (the editor and the hover tip), `ParcelNotes.Save`'s emptiness test, and
`NotesRoundTrip`.

### 1.4 Unhoused is not stored

A person is unhoused when **no note's `Lives` contains their id**. There is no flag and no second
bucket. This is the reason for the whole shape: a stored "unhoused" flag and a `Lives` list are
two facts that can disagree, and the one that disagrees silently is the one that loses somebody's
work.

`ParcelNotes.Unhoused()` computes it by walking the notes once and returning every person id not
seen. At 794 lots and a few hundred people this is beneath measurement, and it cannot go stale.

### 1.5 Id allocation

**Ids are never reused.** If person 17 is deleted, 17 stays dead — otherwise a `lives 17` line
left behind anywhere, in a hand edit or a stale backup, resurrects a stranger into somebody's
house.

That means the counter has to be **stored**, not derived. Deriving it as `max(existing id) + 1`
walks backwards the moment the highest-numbered person is deleted: delete 19, reload, and the
next person minted is 19 again. So the file carries it:

```
nextperson 20
```

Written on every save, read on load, and only ever increased. If the line is missing — an older
file, or a hand edit that dropped it — it is taken as `max(existing id) + 1`, which is the best
guess available and no worse than not having it.

---

## 2. File format

```
nextperson 20

person 17 adult "Dorothy" "Vance" 44 "light sleeper" f
person 18 adult "Russell" "Vance" 47 "curtain-twitcher|drinks alone" m
person 19 adult "Junior"  "Kelch" 19 "" m

parcel 412 lives 17
parcel 412 lives 18
parcel 412 zoning residential
```

Person 19 appears in no `lives` line, so he is in the roster.

- The `person` line is the one already written and parsed today, with the id inserted after the
  keyword. It stays hand-tolerant: a line missing its age, traits or sex still yields a person.
- `lives` takes one id per line rather than a list, so a single corrupt entry costs one resident
  instead of a household.
- Order does not matter. `lives` lines are resolved after the whole file is read, the same way
  `MigrateNames` already runs at the end of `Load()`.

### 2.1 Error handling

| Case | Behaviour |
|---|---|
| `lives` names an id with no `person` record | Drop that entry, `Debug.LogWarning` once per load naming the parcel and id. The lot keeps its other residents. |
| Two `person` records share an id | Last one wins, warn. The file is rewritten whole on the next save, which resolves it. |
| `person` line with no id | Treated as a pre-id line: minted a fresh id on load. |
| `nextperson` missing, or lower than the highest id present | Taken as `max(existing id) + 1`. Never allowed to go backwards. |
| Same person id in two lots' `lives` | Allowed on load and warned about; the editor cannot create it. Somebody hand-editing a move and forgetting to delete the old line is the likely cause, and refusing to load is worse than telling them. |

### 2.2 Migration

There is nothing to migrate. The file today holds 61 `parcel` lines and **zero** `person` lines —
the writer never emitted one until 2026-08-05, and the owner has confirmed the current contents
are disposable. This is why the format change is happening now rather than later.

Two older forms still load, both through the existing `MigrateNames`, which now mints ids:

- `parcel N names "Bob Fuller|Jane Fuller"` — the pre-structured-editor blob.
- `parcel N household 2 1` — the derived adult/kid counts. These keep being **written** as well,
  because `Households`, the inspector summary and the county cross-check still read them.

---

## 3. Panel structure

### 3.1 The tab strip

```
+--------------------------------------------------------------+
| 408 Holmes Ave                                               |
| [ WHO LIVES HERE ]  [ the lot ]                              |
+--------------------------------------------------------------+
```

Each tab gets the panel's full 760px rather than a 330px column.

### 3.2 Which tab opens

- Selecting a lot with residents opens **who lives here**.
- Selecting a lot with none opens **the lot**.
- **Once the user clicks a tab, that choice pins** and selecting further lots no longer moves it.
  Clicking the other tab re-pins to that one.

A `_tabPinned` bool, set true by a tab click, consulted when the selection changes. Deliberately
not cleverer than that: authoring a street means ten lots on the same tab, and a rule that
sometimes overrides the user is worse than one that always obeys them after the first click.

### 3.3 Who lives here

Rows as they are now — first, last, age, M/F, adult/child — with three changes:

- `×` **unhouses**: removes the id from this lot's `Lives`. The person survives, in the roster.
  Nothing in this panel deletes a person.
- `+ adult` / `+ child` mint a new person, add them to the store, and append the id here.
  `+ child` still defaults the surname to `MothersSurname()`.
- `from roster` toggles an **inline** list of unhoused people, drawn in place rather than as a
  floating popup — a popup inside a scroll view is clipped by it, which is why `EnumField` and
  the trait picker are both inline. Each entry offers `place` (add here) and `delete` (remove
  from the store for good, the only place that does).

The trait picker and the behaviour text box are unchanged.

### 3.4 The lot

Everything in today's right column, unchanged: business, trade, zoning, housing type, condition,
stories, basement, bedrooms, baths, half-baths, square feet, year built, footprint drawing.

When the lot has no residents it also shows one line — `nobody lives here yet` — and a button
that switches to the occupants tab, which is the "way to add occupants" for an empty lot.

---

## 4. Drafts and saving

The existing draft model is kept: `SeedDrafts` loads a lot's values into `_draft*` fields, and
moving to another lot carries unsaved edits to disk first. Two adjustments:

- `_draftPeople` becomes the working copy of the **people this lot points at**, still a
  `List<Person>` of copies so an abandoned edit cannot mutate the store.
- Saving writes each drafted person back into the store by id, and writes the lot's `Lives` from
  the drafted list. A person minted during an edit that is then abandoned is written anyway —
  they simply arrive in the roster, which is recoverable, where discarding them is not.

`DraftsDifferFromDisk` and `PeopleDiffer` gain the id and compare `Lives`.

---

## 5. Testing

`Assets/Noir/Editor/NotesRoundTrip.cs` — the marker-file editor probe — grows these cases. It
already backs up and restores the real file, so all of them are safe to run on a live project.

1. **A person with no lot survives a save and a reload.** The roster is the whole point and it is
   the case a parcel-owned format could not express at all.
2. **Two people with the same name stay distinct.** A father and son both called Junior Kelch get
   separate ids and separate traits, and neither overwrites the other.
3. **`lives` round-trips**, including a lot with three residents and a lot with none.
4. **A dangling `lives` id is dropped without losing the lot.** Hand-write `parcel N lives 9999`,
   load, and confirm the lot keeps its other residents and its zoning.
5. **Ids are not reused, specifically at the top of the range.** Create three people, delete the
   **highest-numbered** one, force a reload from disk, then create another — and confirm it does
   not take the dead id. Deleting the middle one proves nothing here: `max + 1` survives that and
   fails this, which is how the derived counter got into the first draft of this spec.
6. **Moving somebody keeps everything.** Unhouse a person with traits and a sex from one lot,
   place them on another, and confirm every field survived the move.

The existing checks — that the real file comes back byte-identical, and that every field of every
person round-trips — stay.

---

## 6. Out of scope, recorded so it is not lost

- Wiring authored people to `Noir.Core.People.Citizen`.
- Housing the 13 stranded citizens.
- Per-person behaviour text.
- A roster view reachable without going through a lot. Worth doing if the roster gets large;
  not worth doing before anybody has used it once.

## 7. The four defects

Fixed **before** this work and independently of it, since none of them are caused by the design
above and all of them bite today:

1. `OrbitCamera.HandleZoom` never checks `VillageUI.PointerOverUI`, so the wheel zooms the map
   while the pointer is over the panel. It is the only input handler missing that guard.
2. `PointerOverUI` is assigned at the **end** of `OnGUI`, after `DrawHoverTip` has already drawn
   from it — so the hover address flashes behind the behaviour box. Computing it before any
   drawing fixes this and the next one.
3. `HandleSelection` reads that same stale guard and, on a click the panel should have swallowed,
   reaches `PlacePicker` and sets `SelectedParcel = null` — the panel closing on a dropdown pick.
4. The zoning dropdown's first click was the layout tear fixed on 2026-08-05 at 12:31; needs a
   retest on the current build before anything else is done to it.
