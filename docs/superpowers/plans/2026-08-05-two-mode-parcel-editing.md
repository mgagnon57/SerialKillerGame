# Two-Mode Parcel Editing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make an authored person a record in their own right, so somebody can exist between houses, and split the parcel panel into a people tab and a lot tab.

**Architecture:** `ParcelNotes` gains a person store keyed by a stable id, alongside its existing note store. A `Note` stops containing people and instead lists the ids of who lives there. "Unhoused" is not stored — it is computed as the set of people no note points at. `VillageUI`'s single crowded panel becomes two tabs, each getting the full panel width.

**Tech Stack:** Unity 6000.3.20f1, C#, IMGUI (`OnGUI`/`GUILayout`). No test framework for the Unity layer — verification is `Assets/Noir/Editor/NotesRoundTrip.cs`, a marker-file editor probe, plus headless `dotnet build`.

## Global Constraints

- **`Content/parcel-notes.txt` is the sole copy of everything authored about this town.** It is written whole on every save, via write-to-temp then `File.Replace` leaving a `.bak`. Never weaken that. Every probe run backs the file up and restores it byte-for-byte in a `finally`.
- **IMGUI draws the same control count in the Layout pass and the Repaint pass.** Anything conditional must be decided *before* the control that can change it. Capture the governing value into a local first. Getting this wrong produces `ArgumentException: Getting control N's position in a group with only M controls`, and the panel tears rather than erroring usefully.
- **No floating popup inside a scroll view** — it gets clipped by it. `EnumField` and `DrawTraitPicker` are both drawn inline for exactly this reason. The roster picker must be too.
- **Ids are never reused.** The counter is stored in the file as `nextperson <n>` and only ever increases.
- **Headless compile after every task:** `dotnet build Noir.Unity.csproj -c Debug` and `dotnet build Noir.Editor.csproj -c Debug`, from `C:\SerialKillerGame`. Zero `error CS`. One pre-existing warning (`VillageAudio.cs(195,17): CS0162`) is expected and is not yours.
- **Do not `git add -A` or `git add .`** — the working tree carries unrelated dirty files from a parallel session. Add named paths only.
- Spec: `docs/superpowers/specs/2026-08-05-two-mode-parcel-editing-design.md`.

## How to run the probe

There is no `dotnet test` for the Unity layer. `NotesRoundTrip` runs inside the open editor, triggered by a marker file plus a domain reload. This is fiddly and undocumented anywhere else, so:

```powershell
# 1. arm the marker and clear the old report
Set-Content -Path "$env:TEMP\noir-notes-roundtrip-please.txt" -Value "please" -Encoding utf8
Remove-Item "$env:TEMP\noir-notes-roundtrip.txt" -ErrorAction SilentlyContinue

# 2. park focus on a REAL window, then give it to Unity, then force a refresh.
#    Unity only rescans Assets/ on focus GAIN. AppActivate($PID) does NOT park focus -
#    this shell has no window, the call fails silently, and Unity never loses focus.
$sh = New-Object -ComObject WScript.Shell
$sh.AppActivate((Get-Process WindowsTerminal).Id) | Out-Null
Start-Sleep -Milliseconds 700
$sh.AppActivate((Get-Process Unity | Where-Object { $_.MainWindowTitle -ne "" }).Id) | Out-Null
Start-Sleep -Milliseconds 1200
$sh.SendKeys("^r")

# 3. the report appears here when it has run
Get-Content "$env:TEMP\noir-notes-roundtrip.txt"
```

The probe stops Play itself if the editor is playing (`EditorApplication.isPlaying = false`), so it does not matter what state the editor is in. If no report appears, look in `%LOCALAPPDATA%\Unity\Editor\Editor.log` for lines containing `notes-roundtrip` — it logs why it declined. **`mcp__unity-mcp__Unity_GetConsoleLogs` can return empty while logs exist**; `Editor.log` is the reliable channel.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `Assets/Noir/Unity/ParcelNotes.cs` | The authored data: notes, people, the file format, atomic save | Modify — person store, ids, `Lives`, new lines |
| `Assets/Noir/Unity/VillageUI.cs` | The whole IMGUI layer including the parcel editor | Modify — tabs, roster picker, unhouse |
| `Assets/Noir/Editor/NotesRoundTrip.cs` | The round-trip probe | Modify — six new cases |

`VillageUI.cs` is ~1,600 lines and genuinely unwieldy. **Do not split it in this plan.** The change here is contained in `DrawNoteEditor` and its helpers, and a 600-line partial-class move would bury the real diff. Note it and move on.

### Every reader of `Note.People`, which is being removed

Task 1 must update all of these. This list was produced by grep; the last time a field was replaced in this file the *serialiser* was missed and the feature silently saved nothing, so treat the list as load-bearing.

- `ParcelNotes.cs:221` — `Save`'s emptiness test
- `ParcelNotes.cs:380` — `ReadPerson` adding to the note
- `ParcelNotes.cs:426,434` — `MigrateNames`
- `ParcelNotes.cs:485` — `Write`
- `ParcelNotes.cs` — `Note.WithPeople(...)`, which is deleted
- `VillageUI.cs:602,606` — `PeopleDiffer`
- `VillageUI.cs:649` — `SeedDrafts`
- `VillageUI.cs:1379,1382` — the parcel inspector's "who lives here" summary
- `NotesRoundTrip.cs:162–170` — the probe

---

## Task 1: People become records with ids

**Files:**
- Modify: `Assets/Noir/Unity/ParcelNotes.cs`
- Modify: `Assets/Noir/Unity/VillageUI.cs` (readers only — no UI change yet)
- Modify: `Assets/Noir/Editor/NotesRoundTrip.cs`

**Interfaces:**
- Produces:
  - `ParcelNotes.Person.Id` — `int`, 0 means "not yet in the store"
  - `ParcelNotes.Note.Lives` — `readonly List<int>`, replaces `Note.People`
  - `static Person ParcelNotes.PersonById(int id)` — null if unknown
  - `static IReadOnlyDictionary<int, Person> ParcelNotes.AllPeople`
  - `static void ParcelNotes.SetResidents(Note note, IEnumerable<Person> people)`
  - `static List<Person> ParcelNotes.Residents(Note note)`
- Consumes: nothing.

- [ ] **Step 1: Add the id and the store**

In `ParcelNotes.Person`, add above `First`:

```csharp
/// <summary>
/// Stable for the life of this person, and never reused once assigned - see the
/// `nextperson` line in the file. Zero means "not in the store yet": a row typed into the
/// editor has no id until it is saved.
///
/// The reason people have ids at all is that they outlive lots. Somebody between houses
/// cannot be written down by a format that only knows `parcel N person ...`, and moving a
/// family without one means retyping them and losing their traits.
/// </summary>
public int Id;
```

Add `Id = Id` to `Person.Copy()`'s object initialiser.

In `ParcelNotes`, beside `_byId`:

```csharp
/// <summary>Everybody authored, by id, whether or not they live anywhere.</summary>
private static Dictionary<int, Person> _people;

/// <summary>
/// The next id to hand out. STORED rather than derived.
///
/// `max(existing) + 1` walks backwards the moment the highest-numbered person is deleted:
/// delete 19, reload, and the next person minted is 19 again - inheriting any stale
/// `lives 19` line left in a hand edit or a .bak. So it is written to the file and only
/// ever increased.
/// </summary>
private static int _nextPersonId = 1;

public static IReadOnlyDictionary<int, Person> AllPeople { get { Load(); return _people; } }

public static Person PersonById(int id)
{
    Load();
    return _people.TryGetValue(id, out var p) ? p : null;
}
```

- [ ] **Step 2: Replace `Note.People` with `Note.Lives`**

Delete the `People` field and the whole `WithPeople` method. Add:

```csharp
/// <summary>
/// Who lives here, by person id. The people themselves are in ParcelNotes.AllPeople - a
/// lot points AT its residents rather than containing them, which is what lets somebody
/// exist while living nowhere.
/// </summary>
public readonly List<int> Lives = new List<int>();
```

Add these statics to `ParcelNotes`:

```csharp
/// <summary>The people living on this note, resolved. Skips ids with no record.</summary>
public static List<Person> Residents(Note note)
{
    Load();
    var list = new List<Person>();
    if (note == null) return list;
    foreach (int id in note.Lives)
        if (_people.TryGetValue(id, out var p)) list.Add(p);
    return list;
}

/// <summary>
/// Put these people in the store and make them this note's residents.
///
/// COPIES them in, so an abandoned draft cannot mutate the store behind the editor's back -
/// the same reason the old WithPeople copied. Anybody with no id is new and gets one.
/// A row with neither a forename nor a surname is scaffolding, not a person, and is skipped.
/// </summary>
public static void SetResidents(Note note, IEnumerable<Person> people)
{
    Load();
    note.Lives.Clear();
    foreach (var who in people)
    {
        if (string.IsNullOrWhiteSpace(who.First) && string.IsNullOrWhiteSpace(who.Last))
            continue;
        int id = who.Id > 0 ? who.Id : _nextPersonId++;
        var stored = who.Copy();
        stored.Id = id;
        _people[id] = stored;
        note.Lives.Add(id);
    }
}
```

- [ ] **Step 3: Update the four readers inside ParcelNotes.cs**

`Save`'s emptiness test — `note.People.Count == 0` becomes `note.Lives.Count == 0`.

`Load()` — initialise the store beside the notes:

```csharp
if (_byId != null) return;
_byId = new Dictionary<int, Note>();
_people = new Dictionary<int, Person>();
_nextPersonId = 1;
```

`Load()`'s line loop — handle the two new top-level lines **before** the `parcel` guard:

```csharp
var line = raw.Trim();
if (line.Length == 0 || line[0] == '#') continue;

// TOP-LEVEL LINES, which are not about any parcel. Checked before the `parcel` guard
// below, which drops everything that does not start with that word.
if (line.StartsWith("nextperson "))
{
    if (int.TryParse(line.Substring(11).Trim(), out int next))
        _nextPersonId = System.Math.Max(_nextPersonId, next);
    continue;
}
if (line.StartsWith("person "))
{
    ReadPerson(line.Substring(7));
    continue;
}

var parts = line.Split(new[] { ' ' }, 3);
```

`ReadPerson` — takes the id, no longer takes a `Note`, and stores:

```csharp
/// <summary>
/// `<id> adult|child "first" "last" age "trait|trait" m|f|-` - the tail of a person line.
///
/// Hand-tolerant on purpose. This file is meant to be editable in a text editor, so a line
/// missing its age, its traits or its sex still yields a person; only the id, the kind and
/// a name are really needed. A line with no id at all is a pre-id line and is minted one.
/// </summary>
private static void ReadPerson(string rest)
{
    var who = new Person();

    string idField = NextField(ref rest).Trim();
    if (int.TryParse(idField, out int id) && id > 0) who.Id = id;

    who.Child = NextField(ref rest).Trim().Equals("child", System.StringComparison.OrdinalIgnoreCase);
    who.First = Unquote(NextField(ref rest));
    who.Last = Unquote(NextField(ref rest));

    if (int.TryParse(NextField(ref rest).Trim(), out int years)) who.Age = years;

    string traits = Unquote(NextField(ref rest));
    if (!string.IsNullOrWhiteSpace(traits))
        foreach (var t in traits.Split('|'))
            if (!string.IsNullOrWhiteSpace(t)) who.Traits.Add(t.Trim());

    string sex = NextField(ref rest).Trim().ToLowerInvariant();
    if (sex == "m" || sex == "man" || sex == "male") who.Which = Sex.Man;
    else if (sex == "f" || sex == "woman" || sex == "female") who.Which = Sex.Woman;

    if (string.IsNullOrWhiteSpace(who.First) && string.IsNullOrWhiteSpace(who.Last)) return;

    if (who.Id <= 0) who.Id = _nextPersonId++;
    _people[who.Id] = who;
    _nextPersonId = System.Math.Max(_nextPersonId, who.Id + 1);
}
```

`MigrateNames` — mints ids and fills `Lives`. Replace its `note.People.Count > 0` guard with `note.Lives.Count > 0`, and each `note.People.Add(new Person { ... })` with:

```csharp
var who = new Person { First = first, Last = last, Id = _nextPersonId++ };
_people[who.Id] = who;
note.Lives.Add(who.Id);
```

(keep whatever the existing code computes for `first` and `last` — it splits the old blob on the last space).

`Write()` — emit the counter, then the people, then the `lives` lines. The person block replaces the old per-parcel loop entirely:

```csharp
// The id counter goes first and is never allowed to go backwards - see _nextPersonId.
sb.AppendLine("nextperson " + _nextPersonId);
sb.AppendLine();

var personIds = new List<int>(_people.Keys);
personIds.Sort();
foreach (int pid in personIds)
{
    var who = _people[pid];
    sb.AppendLine($"person {who.Id} {(who.Child ? "child" : "adult")} "
                + $"\"{Quote(who.First ?? "")}\" \"{Quote(who.Last ?? "")}\" "
                + $"{who.Age} \"{Quote(string.Join("|", who.Traits))}\" "
                + $"{(who.Which == Sex.Man ? "m" : who.Which == Sex.Woman ? "f" : "-")}");
}
sb.AppendLine();
```

and inside the existing per-parcel loop, where the old `foreach (var who in note.People)` block was:

```csharp
// ONE ID PER LINE, not a list, so a single corrupt entry costs one resident rather
// than a household.
foreach (int personId in note.Lives)
    sb.AppendLine($"parcel {id} lives {personId}");
```

- [ ] **Step 4: Parse `parcel N lives M`**

In `Load()`'s `rest.StartsWith(...)` chain, beside the other parcel keywords:

```csharp
else if (rest.StartsWith("lives "))
{
    if (int.TryParse(rest.Substring(6).Trim(), out int personId))
        note.Lives.Add(personId);
}
```

- [ ] **Step 5: Update the three readers in VillageUI.cs**

`SeedDrafts` (was `foreach (var who in saved.People)`):

```csharp
_draftPeople.Clear();
_traitsOpenFor = -1;
if (saved != null)
    foreach (var who in ParcelNotes.Residents(saved)) _draftPeople.Add(who.Copy());
```

`PeopleDiffer` — compare against the resolved residents, and compare ids too:

```csharp
private bool PeopleDiffer(ParcelNotes.Note saved)
{
    var live = new System.Collections.Generic.List<ParcelNotes.Person>();
    foreach (var who in _draftPeople)
        if (!string.IsNullOrWhiteSpace(who.First) || !string.IsNullOrWhiteSpace(who.Last))
            live.Add(who);

    var onFile = ParcelNotes.Residents(saved);
    if (live.Count != onFile.Count) return true;
    for (int i = 0; i < live.Count; i++)
    {
        var a = live[i];
        var b = onFile[i];
        if (a.Id != b.Id || a.First != b.First || a.Last != b.Last || a.Age != b.Age
            || a.Child != b.Child || a.Which != b.Which)
            return true;
        if (a.Traits.Count != b.Traits.Count) return true;
        for (int t = 0; t < a.Traits.Count; t++)
            if (a.Traits[t] != b.Traits[t]) return true;
    }
    return false;
}
```

`DraftNote` is currently an expression-bodied member ending `}.WithPeople(_draftPeople);`. Convert it to a block:

```csharp
private ParcelNotes.Note DraftNote(ParcelNotes.Note saved)
{
    var note = new ParcelNotes.Note
    {
        // Adults/Kids stay derived so anything still reading them - Households, the
        // inspector summary - keeps working while the people are the real answer.
        Adults = CountPeople(false), Kids = CountPeople(true), Names = "",
        Character = _draftCharacter, Footprint = saved?.Footprint,
        Business = _draftBusiness, Trade = _draftTrade,
        Zoning = _draftZoning, Housing = _draftHousing, Condition = _draftQuality,
        Stories = _draftStories, Basement = _draftBasement,
        Bedrooms = _draftBedrooms, Baths = _draftBaths, HalfBaths = _draftHalfBaths,
        SquareFeet = _draftSquareFeet, YearBuilt = _draftYearBuilt
    };
    ParcelNotes.SetResidents(note, _draftPeople);
    return note;
}
```

The parcel inspector summary at ~line 1379 — `if (note.People.Count > 0)` becomes:

```csharp
var residents = ParcelNotes.Residents(note);
if (residents.Count > 0)
{
    body.Append("\n");
    foreach (var who in residents)
    {
```

**Note:** `residents` must be computed *before* the `if`, not inside it, and the rest of the loop body is unchanged.

While you are here, delete the **duplicated `<summary>` block** above `DraftIsAnything()` (there are two stacked XML doc comments, ~lines 1144–1157; keep the second, longer one).

- [ ] **Step 6: Point the probe at the new API**

In `NotesRoundTrip.Run()`, replace `.WithPeople(wanted)` with a `ParcelNotes.SetResidents(note, wanted);` call after the initialiser, and every `back.People` with a local:

```csharp
var backPeople = ParcelNotes.Residents(back);
```

Update the three uses (`.Count` twice, and `back.People[i]`) to `backPeople`. The grep for `parcel " + TestId + " person "` in check 1 becomes:

```csharp
if (line.StartsWith("person ")) personLines.Add(line);
```

and the count check compares against `wanted.Count` as it already does. Also add, right after it, a check that the lot points at them:

```csharp
int livesLines = 0;
foreach (var raw in onDisk.Split('\n'))
    if (raw.Trim().StartsWith("parcel " + TestId + " lives ")) livesLines++;
log.AppendLine("lives lines written  : " + livesLines + " (want " + wanted.Count + ")");
if (livesLines != wanted.Count) { failures++; log.AppendLine("   ** FAIL"); }
else log.AppendLine("   ok");
```

- [ ] **Step 7: Compile**

```bash
cd /c/SerialKillerGame
dotnet build Noir.Unity.csproj -c Debug -v minimal --nologo
dotnet build Noir.Editor.csproj -c Debug -v minimal --nologo
```

Expected: `Build succeeded`, `0 Error(s)`, one pre-existing `CS0162` warning.

- [ ] **Step 8: Run the probe**

Follow "How to run the probe" above. Expected: `ALL CHECKS PASSED`, with `person` lines now carrying ids and `lives` lines present, e.g.

```
person 1 adult "Testcase" "Ninepenny" 47 "curtain-twitcher|night owl" m
parcel 900001 lives 1
```

and `the real file is untouched: identical  ok`.

- [ ] **Step 9: Commit**

```bash
git add Assets/Noir/Unity/ParcelNotes.cs Assets/Noir/Unity/VillageUI.cs Assets/Noir/Editor/NotesRoundTrip.cs
git commit -m "A person stops being a line underneath a lot"
```

---

## Task 2: The roster, and ids that never come back

**Files:**
- Modify: `Assets/Noir/Unity/ParcelNotes.cs`
- Modify: `Assets/Noir/Editor/NotesRoundTrip.cs`

**Interfaces:**
- Consumes: `Person.Id`, `Note.Lives`, `PersonById`, `SetResidents` from Task 1.
- Produces:
  - `static List<Person> ParcelNotes.Unhoused()`
  - `static void ParcelNotes.DeletePerson(int id)`

- [ ] **Step 1: Add `Unhoused()` and `DeletePerson`**

```csharp
/// <summary>
/// Everybody no lot points at, by id order.
///
/// COMPUTED, NEVER STORED. An "unhoused" flag beside a residents list is two facts that can
/// disagree, and the one that disagrees quietly is the one that loses somebody's evening.
/// At a few hundred people over 794 lots this walk is beneath measurement and it cannot go
/// stale.
/// </summary>
public static List<Person> Unhoused()
{
    Load();
    var housed = new HashSet<int>();
    foreach (var note in _byId.Values)
        foreach (int id in note.Lives) housed.Add(id);

    var list = new List<Person>();
    foreach (var kv in _people)
        if (!housed.Contains(kv.Key)) list.Add(kv.Value);
    list.Sort((a, b) => a.Id.CompareTo(b.Id));
    return list;
}

/// <summary>
/// Remove somebody permanently. The ONLY thing in the project that deletes a person - the
/// editor's x button unhouses instead, so no single click destroys anything.
///
/// Their id is not returned to the pool. _nextPersonId only ever goes up, so a stale
/// `lives 19` in a hand edit or a .bak cannot resurrect a stranger into somebody's house.
/// </summary>
public static void DeletePerson(int id)
{
    Load();
    if (!_people.Remove(id)) return;
    foreach (var note in _byId.Values) note.Lives.Remove(id);
    Write();
    Changed?.Invoke();
}
```

- [ ] **Step 2: Drop dangling `lives` ids after load**

At the end of `Load()`, beside the existing `foreach (var note in _byId.Values) MigrateNames(note);`:

```csharp
// A `lives` line naming somebody with no record. Costs that one resident and says so,
// rather than refusing to load a file that is otherwise fine - the lot keeps its zoning,
// its shape and everybody else.
foreach (var pair in _byId)
    for (int i = pair.Value.Lives.Count - 1; i >= 0; i--)
        if (!_people.ContainsKey(pair.Value.Lives[i]))
        {
            Debug.LogWarning($"[notes] parcel {pair.Key} lists person "
                           + $"{pair.Value.Lives[i]}, who has no record. Dropped.");
            pair.Value.Lives.RemoveAt(i);
        }
```

Order matters: this must run **after** `MigrateNames`, which adds ids of its own.

- [ ] **Step 3: Add probe case — a person with no lot survives**

In `NotesRoundTrip.Run()`, after the existing check 2 and before cleanup:

```csharp
// ---- 5. somebody who lives nowhere ----
//
// The roster is the whole reason people have ids, and it is the case the old
// parcel-owned format could not express at all.
log.AppendLine("---- 5. a person with no lot ----");
var drifter = Person("Nomad", "Elevenpenny", 33, false, ParcelNotes.Sex.Man, "drinks alone");
ParcelNotes.SetResidents(spare, new List<ParcelNotes.Person> { drifter });
ParcelNotes.Save(SpareId, spare);
ParcelNotes.Save(SpareId, new ParcelNotes.Note());   // lot goes away, person should not

byId.SetValue(null, null);
var loose = ParcelNotes.Unhoused();
bool foundDrifter = false;
foreach (var p in loose) if (p.First == "Nomad" && p.Last == "Elevenpenny") foundDrifter = true;
log.AppendLine("unhoused after the lot was emptied : " + (foundDrifter ? "ok" : "** FAIL"));
if (!foundDrifter) failures++;
```

Add `private const int SpareId = 900002;` beside `TestId`, and declare `var spare = new ParcelNotes.Note();` with the other setup.

- [ ] **Step 4: Add probe case — two people with the same name stay distinct**

```csharp
// ---- 6. a father and a son with the same name ----
//
// The thing a value-identified format cannot do. Two Junior Kelches with different traits
// must come back as two people, not one applied twice.
log.AppendLine("---- 6. same name, different people ----");
var senior = Person("Junior", "Kelch", 58, false, ParcelNotes.Sex.Man, "on the village board");
var junior = Person("Junior", "Kelch", 19, false, ParcelNotes.Sex.Man, "night owl");
var pair = new ParcelNotes.Note { Adults = 2 };
ParcelNotes.SetResidents(pair, new List<ParcelNotes.Person> { senior, junior });
ParcelNotes.Save(SpareId, pair);
byId.SetValue(null, null);

var readPair = ParcelNotes.Residents(ParcelNotes.For(SpareId));
bool twoDistinct = readPair.Count == 2
                && readPair[0].Id != readPair[1].Id
                && readPair[0].Age != readPair[1].Age
                && readPair[0].Traits.Count == 1 && readPair[1].Traits.Count == 1
                && readPair[0].Traits[0] != readPair[1].Traits[0];
log.AppendLine("two Junior Kelches stayed apart : " + (twoDistinct ? "ok" : "** FAIL"));
if (!twoDistinct) failures++;
ParcelNotes.Save(SpareId, new ParcelNotes.Note());
```

- [ ] **Step 5: Add probe case — ids are not reused at the top of the range**

```csharp
// ---- 7. the highest id stays dead ----
//
// Deleting a MIDDLE id proves nothing: max+1 survives that and fails this. The counter is
// stored in the file for exactly this case, and this is the check that would have caught
// the derived version that got into the first draft of the spec.
log.AppendLine("---- 7. a deleted id is not handed out again ----");
byId.SetValue(null, null);
var a1 = new ParcelNotes.Note();
ParcelNotes.SetResidents(a1, new List<ParcelNotes.Person>
{
    Person("Alpha", "Twelvepenny", 40, false, ParcelNotes.Sex.Woman),
    Person("Beta", "Twelvepenny", 41, false, ParcelNotes.Sex.Man),
});
ParcelNotes.Save(SpareId, a1);
int highest = 0;
foreach (int id in a1.Lives) if (id > highest) highest = id;

ParcelNotes.Save(SpareId, new ParcelNotes.Note());
ParcelNotes.DeletePerson(highest);
byId.SetValue(null, null);                       // force a genuine re-read

var a2 = new ParcelNotes.Note();
ParcelNotes.SetResidents(a2, new List<ParcelNotes.Person>
{
    Person("Gamma", "Twelvepenny", 42, false, ParcelNotes.Sex.Woman),
});
bool freshId = a2.Lives.Count == 1 && a2.Lives[0] > highest;
log.AppendLine($"deleted id {highest}, next minted {(a2.Lives.Count > 0 ? a2.Lives[0] : -1)} : "
             + (freshId ? "ok" : "** FAIL"));
if (!freshId) failures++;
```

- [ ] **Step 6: Add probe case — a dangling `lives` id is dropped without losing the lot**

```csharp
// ---- 8. a lives line naming nobody ----
log.AppendLine("---- 8. dangling lives id ----");
ParcelNotes.Save(SpareId, new ParcelNotes.Note { Zoning = ParcelNotes.Zoning.Residential });
string doctored = File.ReadAllText(notesPath)
    + "\nparcel " + SpareId + " lives 999999\n";
File.WriteAllText(notesPath, doctored);
byId.SetValue(null, null);

var survivor = ParcelNotes.For(SpareId);
bool heldUp = survivor != null
           && survivor.Zoning == ParcelNotes.Zoning.Residential
           && survivor.Lives.Count == 0;
log.AppendLine("lot kept its zoning and dropped the ghost : " + (heldUp ? "ok" : "** FAIL"));
if (!heldUp) failures++;
ParcelNotes.Save(SpareId, new ParcelNotes.Note());
```

- [ ] **Step 7: Compile and run the probe**

Both builds clean; probe reports `ALL CHECKS PASSED`. **`the real file is untouched: identical` must still say `ok`** — step 6 deliberately writes to the real file, so confirm the `finally` restore still holds.

- [ ] **Step 8: Commit**

```bash
git add Assets/Noir/Unity/ParcelNotes.cs Assets/Noir/Editor/NotesRoundTrip.cs
git commit -m "The roster is the absence of a lot, and a dead id stays dead"
```

---

## Task 3: The tab strip

**Files:**
- Modify: `Assets/Noir/Unity/VillageUI.cs`

**Interfaces:**
- Consumes: `ParcelNotes.Residents(note)` from Task 1.
- Produces: `_noteTab`, `_tabPinned` — private, consumed by Tasks 4 and 5.

- [ ] **Step 1: Add the state**

Beside `_noteDraftFor`:

```csharp
private enum NoteTab { Occupants, Lot }

private NoteTab _noteTab = NoteTab.Lot;

/// <summary>
/// True once the user has clicked a tab by hand.
///
/// Before that, selecting a lot picks the tab by occupancy - people if anybody lives there,
/// the lot if not. After it, the choice sticks: authoring a street is ten lots on the same
/// tab, and a rule that sometimes overrides you is worse than one that always obeys you
/// after the first click.
/// </summary>
private bool _tabPinned;
```

- [ ] **Step 2: Route on selection**

In `SeedDrafts`, after the `_draftPeople` fill (so occupancy is known):

```csharp
if (!_tabPinned)
    _noteTab = _draftPeople.Count > 0 ? NoteTab.Occupants : NoteTab.Lot;
```

- [ ] **Step 3: Draw the strip**

At the top of `DrawNoteEditor`, immediately after `SeedDrafts(parcelId)` and the `GUILayout.Space`, before `BeginScrollView`:

```csharp
// WHICH TAB IS DRAWN IS DECIDED BEFORE THE TAB BUTTONS RUN.
//
// Clicking a tab changes _noteTab inside the click event pass, and IMGUI has already laid
// that pass out from the value it held during Layout. Reading the fresh value below would
// draw a different set of controls than the layout allowed for - the "Mismatched
// LayoutGroup" tear. So the strip switches on the NEXT frame, which is the same one-frame
// lag the trait picker and the zoning dropdown both take, and is invisible at any frame rate.
var showing = _noteTab;

GUILayout.BeginHorizontal();
if (TabButton("who lives here", showing == NoteTab.Occupants))
{
    _noteTab = NoteTab.Occupants;
    _tabPinned = true;
}
if (TabButton("the lot", showing == NoteTab.Lot))
{
    _noteTab = NoteTab.Lot;
    _tabPinned = true;
}
GUILayout.EndHorizontal();
GUILayout.Space(S(6f));
```

And the helper, beside `EnumField`:

```csharp
/// <summary>A tab in the parcel panel's strip. Lit when it is the one being shown.</summary>
private bool TabButton(string label, bool active)
{
    var was = GUI.backgroundColor;
    if (active) GUI.backgroundColor = new Color(0.36f, 0.52f, 0.38f);
    bool hit = GUILayout.Button(active ? "• " + label : label, _button,
                                GUILayout.Height(S(26f)), GUILayout.Width(S(180f)));
    GUI.backgroundColor = was;
    return hit;
}
```

- [ ] **Step 4: Split the body on the captured value**

`DrawNoteEditor` currently draws two columns inside one `BeginHorizontal`. Replace that structure so each tab owns the full width:

- Delete the `GUILayout.BeginHorizontal();` / `GUILayout.BeginVertical(GUILayout.Width(S(330f)))` that opens the left column, the `GUILayout.EndVertical(); GUILayout.Space(S(24f)); GUILayout.BeginVertical(GUILayout.Width(S(330f)));` that switches columns, and the final `GUILayout.EndVertical(); GUILayout.EndHorizontal();`.
- Wrap the left column's contents in `if (showing == NoteTab.Occupants) { ... }` and the right column's in `if (showing == NoteTab.Lot) { ... }`.
- The save/revert row and anything below the two columns stays **outside** both, drawn on every tab.

Widen the person row now that it has 760px rather than 330: first `S(140f)`, last `S(140f)`, age `S(40f)`, sex `S(28f)`, adult/child `S(60f)`, remove `S(28f)`.

- [ ] **Step 5: Compile and look at it**

Both builds clean. Then run the probe (it exercises no UI but proves nothing regressed), and **press Play and look at the panel** — this step cannot be verified any other way. Check: both tabs appear; clicking one switches on the next frame with no console exception; an occupied lot opens on people and an empty one on the lot; after clicking a tab, selecting other lots leaves it alone.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Unity/VillageUI.cs
git commit -m "Two tabs, routed by whether anybody lives there"
```

---

## Task 4: The occupants tab

**Files:**
- Modify: `Assets/Noir/Unity/VillageUI.cs`

**Interfaces:**
- Consumes: `ParcelNotes.Unhoused()`, `ParcelNotes.DeletePerson(int)` from Task 2; `_noteTab` from Task 3.
- Produces: nothing for later tasks.

- [ ] **Step 1: The x button unhouses**

The removal is already deferred out of the draw loop into `removeAt`. Only the comment and the semantics change — the person is dropped from `_draftPeople`, and because `SetResidents` writes the store from the draft, they simply stop being listed here while their record survives:

```csharp
// REMOVED FROM THE HOUSE, NOT FROM THE WORLD. Dropping them from the drafts means the next
// save writes a `lives` list without them - and since nothing deletes their person record,
// they turn up in the roster, which is also how you move a family: remove here, place there.
// The only thing that deletes anybody is the roster's own delete button.
if (removeAt >= 0)
{
    _draftPeople.RemoveAt(removeAt);
    _traitsOpenFor = -1;
}
```

- [ ] **Step 2: The roster picker state**

Beside `_traitsOpenFor`:

```csharp
/// <summary>Whether the unhoused list is showing. Inline, not a popup - see DrawRoster.</summary>
private bool _rosterOpen;
```

Set `_rosterOpen = false;` in `SeedDrafts` beside `_traitsOpenFor = -1;`.

- [ ] **Step 3: The button and the list**

After the `+ adult` / `+ child` row, inside the same `BeginHorizontal`:

```csharp
// CAPTURED BEFORE THE BUTTON THAT CHANGES IT, for the same reason as the tabs.
bool rosterShowing = _rosterOpen;
if (GUILayout.Button(rosterShowing ? "from roster ▲" : "from roster ▾", _button,
                     GUILayout.Height(S(22f))))
    _rosterOpen = !rosterShowing;
```

Then after `GUILayout.EndHorizontal()`:

```csharp
if (rosterShowing) DrawRoster();
```

And the method:

```csharp
/// <summary>
/// The people who live nowhere, with a way to put one in this house.
///
/// Drawn INLINE rather than as a floating window, like the trait picker and the enum
/// dropdowns, because a popup inside a scroll view is clipped by the scroll view and the
/// bottom of the list becomes unreachable.
/// </summary>
private void DrawRoster()
{
    var loose = ParcelNotes.Unhoused();

    // Anybody already drafted into THIS house is not available to add again. They are still
    // unhoused on disk until the save lands, so without this they appear in their own
    // roster while sitting in the rows above it.
    var drafted = new System.Collections.Generic.HashSet<int>();
    foreach (var who in _draftPeople) if (who.Id > 0) drafted.Add(who.Id);

    int shown = 0;
    int placeId = -1, deleteId = -1;

    foreach (var who in loose)
    {
        if (drafted.Contains(who.Id)) continue;
        shown++;

        GUILayout.BeginHorizontal();
        GUILayout.Label($"<color=#d8cfa8>   {who.FullName}</color>"
                      + (who.Age > 0 ? $"<color=#75736e>, {who.Age}</color>" : ""),
                        _small, GUILayout.Width(S(280f)));
        if (GUILayout.Button("place", _button, GUILayout.Width(S(60f)))) placeId = who.Id;
        if (GUILayout.Button("delete", _button, GUILayout.Width(S(60f)))) deleteId = who.Id;
        GUILayout.EndHorizontal();
    }

    if (shown == 0)
        GUILayout.Label("<color=#75736e>   nobody is between houses</color>", _small);

    // BOTH DEFERRED OUT OF THE LOOP. Adding to _draftPeople or deleting a person mid-draw
    // changes how many controls IMGUI lays out between its Layout and Repaint passes, and
    // the reward for that is a torn panel rather than a placed lodger.
    if (placeId > 0)
    {
        var who = ParcelNotes.PersonById(placeId);
        if (who != null) _draftPeople.Add(who.Copy());
    }
    if (deleteId > 0) ParcelNotes.DeletePerson(deleteId);
}
```

- [ ] **Step 4: Compile and look at it**

Both builds clean. Press Play and check, on a lot with residents: `×` removes a row; save; the person appears under `from roster` on another lot; `place` puts them in it; save; they are gone from the roster and listed on the new lot with their traits and age intact. Then `delete` on somebody in the roster removes them for good.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Unity/VillageUI.cs
git commit -m "Remove somebody from a house and they wait in the roster"
```

---

## Task 5: The lot tab, and proving a move keeps everything

**Files:**
- Modify: `Assets/Noir/Unity/VillageUI.cs`
- Modify: `Assets/Noir/Editor/NotesRoundTrip.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–4.
- Produces: nothing.

- [ ] **Step 1: The empty-lot affordance**

At the top of the `showing == NoteTab.Lot` block:

```csharp
// AN EMPTY LOT SAYS SO, and offers the way in. This is the "add occupants" route for a lot
// nobody lives on: the tab strip sent you here because it is empty, so the one thing it must
// not do is leave you hunting for where the people went.
if (_draftPeople.Count == 0)
{
    GUILayout.Label("<color=#8a8a86>nobody lives here yet</color>", _small);
    if (GUILayout.Button("add occupants", _button,
                         GUILayout.Height(S(22f)), GUILayout.Width(S(160f))))
    {
        _noteTab = NoteTab.Occupants;
        _tabPinned = true;
    }
    GUILayout.Space(S(8f));
}
```

This is safe to make conditional on `_draftPeople.Count` because nothing in this pass changes that count — the rows that do live on the other tab.

- [ ] **Step 2: Probe case — moving somebody keeps everything**

```csharp
// ---- 9. a move keeps the whole person ----
//
// The reason ids exist. Traits, age and sex must survive changing houses, because the
// alternative - retyping them - is how they get lost.
log.AppendLine("---- 9. moving house ----");
byId.SetValue(null, null);
var fromLot = new ParcelNotes.Note();
var mover = Person("Mover", "Thirteenpenny", 52, false, ParcelNotes.Sex.Woman,
                   "keeps bees", "won't cross the county line");
ParcelNotes.SetResidents(fromLot, new List<ParcelNotes.Person> { mover });
ParcelNotes.Save(TestId, fromLot);
int moverId = fromLot.Lives[0];

ParcelNotes.Save(TestId, new ParcelNotes.Note());              // leaves the first house
var toLot = new ParcelNotes.Note();
ParcelNotes.SetResidents(toLot, new List<ParcelNotes.Person>
                                { ParcelNotes.PersonById(moverId).Copy() });
ParcelNotes.Save(SpareId, toLot);
byId.SetValue(null, null);                                     // genuine re-read

var moved = ParcelNotes.Residents(ParcelNotes.For(SpareId));
bool intactMove = moved.Count == 1
               && moved[0].Id == moverId
               && moved[0].First == "Mover" && moved[0].Age == 52
               && moved[0].Which == ParcelNotes.Sex.Woman
               && moved[0].Traits.Count == 2
               && moved[0].Traits.Contains("keeps bees");
log.AppendLine("same id, same traits, new lot : " + (intactMove ? "ok" : "** FAIL"));
if (!intactMove) failures++;
ParcelNotes.Save(SpareId, new ParcelNotes.Note());
ParcelNotes.DeletePerson(moverId);
```

- [ ] **Step 3: Compile and run the probe**

Both builds clean, probe reports `ALL CHECKS PASSED`, and `the real file is untouched: identical  ok`.

- [ ] **Step 4: Look at the whole thing in Play**

Press Play. Walk the full path once: click an empty lot → opens on **the lot** → `add occupants` → tab switches → `+ adult` → type a name → `+ child` → surname defaults to the mother's → save. Click away and back: everything is there. Stop Play, restart, click the same lot: still there.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Unity/VillageUI.cs Assets/Noir/Editor/NotesRoundTrip.cs
git commit -m "An empty lot says so, and a move keeps the whole person"
```

---

## Self-review

**Spec coverage.** §1.1 Person.Id → Task 1 Step 1. §1.2 store → Task 1 Step 1. §1.3 `Lives` and every caller → Task 1 Steps 2, 3, 5. §1.4 computed unhoused → Task 2 Step 1. §1.5 stored counter → Task 1 Step 1 and Task 2 Step 5's test. §2 format → Task 1 Steps 3, 4. §2.1 error table: dangling `lives` → Task 2 Step 2; id-less `person` line → Task 1 Step 3's `ReadPerson`; duplicate id → last-wins falls out of `_people[who.Id] = who`; missing `nextperson` → `Math.Max` in Step 3. §2.2 migration → Task 1 Step 3 `MigrateNames`. §3.1–3.2 tabs and routing → Task 3. §3.3 occupants → Task 4. §3.4 lot tab → Task 5 Step 1. §4 drafts → Task 1 Step 5. §5 all six tests → Tasks 1–5. §7 the four defects were fixed before this plan and are not tasks here.

**Gap found and closed:** the spec's §2.1 says a duplicate person id warns. Last-wins happens for free but nothing warns. It is not worth a task of its own — fold it into Task 1 Step 3 by making the store write `if (_people.ContainsKey(who.Id)) Debug.LogWarning($"[notes] two person records share id {who.Id}; the later one wins.");` immediately before `_people[who.Id] = who;`.

**Type consistency.** `Residents(Note)` returns `List<Person>` and is used that way in Tasks 1, 2, 5. `SetResidents(Note, IEnumerable<Person>)` is called with `List<Person>` throughout. `Unhoused()` returns `List<Person>`, iterated in Task 4. `PersonById(int)` returns `Person` or null and is null-checked at both call sites. `DeletePerson(int)` returns void. `_noteTab`/`_tabPinned`/`_rosterOpen` are named identically in Tasks 3, 4 and 5.

**Placeholder scan.** No TBD/TODO. Every code step carries the actual code.
