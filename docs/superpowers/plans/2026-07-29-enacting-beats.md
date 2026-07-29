# Enacting Beats Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the authored particulars reach a watcher — delete the beat that cannot pay, wire the one that can, and tag the clauses that honestly earn one.

**Architecture:** Three changes. `Beat.RoundAbout` is deleted because the manner it would produce (`SomewhereNew`) is saturated at 158/158 and can never yield a distinct proposition. `Beat.Lingers` is wired into `BeginDoorPause` using its **own** `Rolls` purpose, so every non-lingerer's pause stays byte-identical and no RNG stream advances. Then an editorial pass tags clauses in `Content/particulars.txt` that genuinely imply carrying or lingering.

**Tech Stack:** C# 9 / netstandard2.1 (Core), net9.0 (tools + tests), NUnit 4.2.2. Build and test from `C:\SerialKillerGame\tools`.

## Global Constraints

- **Core is netstandard2.1 / C# 9 and may not reference UnityEngine.** `tools/Noir.Core/Noir.Core.csproj` is the gate; if it compiles there, Unity will accept it.
- **Run tests in Release: `dotnet test Noir.sln -c Release`.** This machine's CPU produces impossible faults in Debug — see the head of `docs/STATE.md`. A Debug run aborting the host is not a code failure.
- **Never pipe `dotnet test` into another command.** `dotnet test | tail` reports *tail's* exit status, so a crashed run reads as a pass.
- **No new RNG draws on any existing path.** The `Beat.Lingers` draw must use a new `Rolls.Purpose`, never `DoorPurpose`. Reusing `DoorPurpose` with a wider range changes every villager's pause and re-baselines the village.
- **Do not add or reword clauses in `Content/particulars.txt`.** This work only appends `# tag` markers to existing lines. Adding or removing a line changes the clause count and shifts every draw.
- **Expect the numbers not to move.** This is a correctness change, not a metrics change. If `ratio` moves more than a point or two, be suspicious.

---

### Task 1: Delete `Beat.RoundAbout`

`SomewhereNew` reads 158/158 on both "came out" and "walked past". A saturated manner cannot become a distinct proposition, so this beat could never pay for the routing change it would need. Nothing outside `NameTable.cs` references it.

**Files:**
- Modify: `Assets/Noir/Core/People/NameTable.cs:79-85` (the enum), `:132-135` (the parse arms)
- Create: `tools/Noir.Core.Tests/BeatTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Beat` with exactly two values — `Beat.Carries = 1 << 0`, `Beat.Lingers = 1 << 1`. Task 2 and Task 4 use `Beat.Lingers` and `Beat.Carries`.

- [ ] **Step 1: Write the failing test**

Create `tools/Noir.Core.Tests/BeatTests.cs`:

```csharp
using NUnit.Framework;
using Noir.Core.People;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The bridge from an authored clause to something a watcher could see.
    ///
    /// These assert on the PARSE and on the enum's shape. Whether a beat reaches anybody is a
    /// different question and lives in BeatsAreEnactedTests.
    /// </summary>
    [TestFixture]
    public class BeatTests
    {
        [Test]
        public void AnUnrecognisedTagIsIgnoredRatherThanRefused()
        {
            // The file already carries `# elder`, `# m` and `# f` for a scoping system that does
            // not exist yet. A parser that threw on those would make writing content a matter of
            // remembering what the code knows about. `roundabout` is now one of those words: the
            // beat is gone, and a line still tagged with it must parse to None rather than throw.
            var table = ParticularsTable.Parse(
                "walks the same lane every evening   # roundabout\n");

            Assert.That(table.Count, Is.EqualTo(1));
            Assert.That(table.BeatAt(0), Is.EqualTo(Beat.None),
                "a tag no beat answers to should leave the clause plain, not throw");
        }

        [Test]
        public void TheTwoSurvivingTagsStillParse()
        {
            var table = ParticularsTable.Parse(
                "carries a stick and does not lean on it   # carries\n"
              + "waits outside for eleven minutes   # lingers\n");

            Assert.That(table.BeatAt(0), Is.EqualTo(Beat.Carries));
            Assert.That(table.BeatAt(1), Is.EqualTo(Beat.Lingers));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Noir.sln -c Release --filter "FullyQualifiedName~BeatTests"`

Expected: `AnUnrecognisedTagIsIgnoredRatherThanRefused` FAILS — `BeatAt(0)` returns `Beat.RoundAbout`, not `Beat.None`, because the parse arm still exists. `TheTwoSurvivingTagsStillParse` passes already.

- [ ] **Step 3: Delete the enum value**

In `Assets/Noir/Core/People/NameTable.cs`, replace the enum body:

```csharp
    [Flags]
    public enum Beat : byte
    {
        None = 0,
        Carries = 1 << 0,      // never goes out empty-handed - a bag, a stick, a dog lead
        Lingers = 1 << 1,      // takes longer over the same journey than anybody else does

        // There was a third, RoundAbout — "does not take the direct way, and never has". It is
        // gone, and the reason is worth keeping. It could only ever have shown up as
        // ObservedManner.SomewhereNew, and the act-by-manner table reads 158 of 158 on both
        // "came out" and "walked past": every villager already earns that manner in the first
        // days of any watch. Texture is a SET of distinct (act, manner) keys, so a manner
        // everybody already has cannot become a new proposition for anybody. It was also the
        // only beat needing a routing change, which would have put the determinism guarantee at
        // risk to buy nothing. Do not reintroduce it without a manner that is actually scarce.
    }
```

- [ ] **Step 4: Delete the parse arm**

In the same file, in `BeatIn`, delete this line:

```csharp
            if (tags.IndexOf("roundabout", StringComparison.OrdinalIgnoreCase) >= 0) beat |= Beat.RoundAbout;
```

leaving the `carries` and `lingers` arms untouched.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Noir.sln -c Release --filter "FullyQualifiedName~BeatTests"`
Expected: PASS, 2 tests.

- [ ] **Step 6: Confirm nothing else referenced it**

Run: `grep -rn "RoundAbout" --include=*.cs Assets tools`
Expected: no output.

- [ ] **Step 7: Commit**

```bash
git add Assets/Noir/Core/People/NameTable.cs tools/Noir.Core.Tests/BeatTests.cs
git commit -m "Delete Beat.RoundAbout: a saturated manner cannot become a proposition"
```

---

### Task 2: Wire `Beat.Lingers` into the door pause

Everyone gets 6–11 ticks at a threshold, 0.3–0.55 s at 20 Hz, while `Eyewitness` samples once per simulated minute — a 0.5–0.9% chance of being caught. That is why only 6 of 158 ever registered as lingering. A lingerer needs to pause for a real fraction of the sampling interval.

**Files:**
- Modify: `Assets/Noir/Core/Sim/Simulation.cs:409` (add constants), `:895-903` (`BeginDoorPause`)
- Modify: `tools/Noir.Core.Tests/BeatTests.cs` (add a fixture)

**Interfaces:**
- Consumes: `Beat.Lingers` from Task 1.
- Produces: `DoorPauseTicks` in `[406, 811]` for a citizen holding `Beat.Lingers`, and unchanged `[6, 11]` for everyone else. Task 4's end-to-end test relies on this range being long enough to be observed.

- [ ] **Step 1: Write the failing test**

Append this fixture to `tools/Noir.Core.Tests/BeatTests.cs`, inside the `Noir.Core.Tests` namespace. It reuses `Queueham.World` (already public) but builds its own population so exactly one citizen lingers:

```csharp
    /// <summary>
    /// The door pause, with and without the beat.
    ///
    /// Queueham is used because everybody in it crosses a threshold twice or more a day by
    /// construction. Its own DoorwayTests already pin the 6-11 range for a village with no beats
    /// in it at all; this adds one lingerer and asserts that ONLY that person changes.
    /// </summary>
    [TestFixture]
    public class LingeringDoorTests
    {
        private const int StartHour = 6;
        private const int Minutes = 240;
        private const int Lingerer = 1;

        // 6 + [0,6) base, plus 400 + [0,400) for the beat.
        private const int PlainShortest = 6, PlainLongest = 11;
        private const int LingerShortest = 406, LingerLongest = 811;

        private static Population OneLingerer(WorldModel world)
        {
            var homes = world.PlacesOfKind(PlaceKind.Dwelling);
            var shop = world.PlacesOfKind(PlaceKind.Shop)[0];

            var citizens = new Citizen[homes.Count];
            var households = new Household[homes.Count];

            for (int i = 0; i < homes.Count; i++)
            {
                var id = new CitizenId(i);
                var house = new HouseholdId(i);
                bool keeper = i == 0;
                Beat beats = i == Lingerer ? Beat.Lingers : Beat.None;

                citizens[i] = new Citizen(id, "Customer", (i + 1).ToString(), 34 + i % 30,
                                          LifeStage.Adult,
                                          keeper ? Occupation.Shopkeeper : Occupation.None,
                                          house, homes[i], keeper ? shop : PlaceId.None, 0, 0,
                                          (byte)(110 + i * 5), (byte)(40 + i * 8), null, beats);

                households[i] = new Household(house, homes[i], "Customer" + (i + 1),
                                              HouseholdShape.Solitary, new[] { id });
            }

            return new Population(citizens, households);
        }

        [Test]
        public void ALingererStandsAtTheDoorLongEnoughToBeSeen()
        {
            var world = Queueham.World;
            var sim = new Simulation(world, OneLingerer(world), Queueham.Seed, StartHour * 60);

            var wasPaused = new int[sim.AgentCount];
            int lingererPauses = 0, plainPauses = 0;

            for (int tick = 0; tick < Minutes * GameClock.TicksPerMinute; tick++)
            {
                sim.Tick();

                for (int i = 0; i < sim.AgentCount; i++)
                {
                    int now = sim.GetAgent(i).DoorPauseTicks;

                    if (wasPaused[i] == 0 && now > 0)
                    {
                        if (i == Lingerer)
                        {
                            lingererPauses++;
                            Assert.That(now, Is.InRange(LingerShortest, LingerLongest),
                                $"the lingerer paused for {now} ticks");
                        }
                        else
                        {
                            plainPauses++;
                            Assert.That(now, Is.InRange(PlainShortest, PlainLongest),
                                $"agent {i} holds no beat but paused for {now} ticks — the "
                              + "base draw has moved, so the whole village has moved");
                        }
                    }

                    wasPaused[i] = now;
                }
            }

            Assert.That(lingererPauses, Is.GreaterThan(0), "the lingerer never crossed a threshold");
            Assert.That(plainPauses, Is.GreaterThan(0), "nobody else crossed one either");
        }
    }
```

Add these usings to the top of `BeatTests.cs`:

```csharp
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Noir.sln -c Release --filter "FullyQualifiedName~LingeringDoorTests"`
Expected: FAIL — the lingerer pauses 6–11 ticks like everybody else, so `Is.InRange(406, 811)` fails.

- [ ] **Step 3: Add the constants**

In `Assets/Noir/Core/Sim/Simulation.cs`, beside `DoorPurpose` at line 409:

```csharp
        private static readonly ulong DoorPurpose = Rolls.Purpose("doorways");

        /// <summary>
        /// The extra time somebody who lingers spends on a threshold, and its OWN purpose.
        ///
        /// A separate purpose rather than a wider range on DoorPurpose, deliberately: Rolls is a
        /// positionless hash, so drawing from a new purpose leaves every existing draw in the
        /// village byte-identical. Widening the door draw would change the pause of all 158
        /// people and regenerate nothing but would move everybody, which is the cost this whole
        /// change was scoped to avoid.
        ///
        /// Sized against the WATCHER, not against taste. Eyewitness samples once per simulated
        /// minute (1,200 ticks), so a 6-11 tick pause is caught under 1% of the time — which is
        /// why lingering read as 6 people of 158 and not as a habit. 400-800 ticks is 20-40 s of
        /// game time and a 33-67% chance per crossing, which is the difference between a
        /// coincidence and something a watcher would write down.
        /// </summary>
        private static readonly ulong LingerPurpose = Rolls.Purpose("lingering");
        private const int LingerBase = 400;
        private const int LingerSpread = 400;
```

- [ ] **Step 4: Wire it into `BeginDoorPause`**

Replace the assignment in `BeginDoorPause` (`Simulation.cs:900-901`):

```csharp
            int ticks = 6 + Rolls.Int(Seed, DoorPurpose, key, _clock.Tick, 0, 6);

            // The sentence in the inspector and the figure still on the step are now one fact.
            if (who != null && (who.Beats & Beat.Lingers) != 0)
                ticks += LingerBase + Rolls.Int(Seed, LingerPurpose, key, _clock.Tick, 0, LingerSpread);

            _agents[index].DoorPauseTicks = ticks;
```

Keep the `who != null` guard: the surrounding code already treats a null citizen as possible when it computes `key`.

No new `using` is needed: `Simulation.cs:664` already reads `citizen.Beats & Beat.Carries`, so
`Beat` resolves in this file today.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test Noir.sln -c Release --filter "FullyQualifiedName~LingeringDoorTests"`
Expected: PASS.

- [ ] **Step 6: Verify the untouched-pause invariant still holds**

Run: `dotnet test Noir.sln -c Release --filter "FullyQualifiedName~DoorwayTests"`
Expected: PASS, unchanged. Queueham's own citizens are built with the 14-argument `Citizen` constructor and take `Beat beats = Beat.None`, so none of them can linger and both existing assertions (`InRange(6, 11)` and `<= Longest`) remain true as written. **If either fails, the base draw has moved — stop and re-read Step 3.**

- [ ] **Step 7: Commit**

```bash
git add Assets/Noir/Core/Sim/Simulation.cs tools/Noir.Core.Tests/BeatTests.cs
git commit -m "A lingerer stands at the door long enough to be seen"
```

---

### Task 3: The editorial pass over `particulars.txt`

Five clauses are tagged today, all of them beginning with the literal word "carries", which is a keyword match rather than an editorial judgement. It reaches about two villagers.

**Files:**
- Modify: `Content/particulars.txt` (append `# carries` / `# lingers` to existing lines only)

**Interfaces:**
- Consumes: `Beat.Carries`, `Beat.Lingers` from Task 1.
- Produces: enough tagged clauses that Task 4's end-to-end test can find at least one lingerer in the real village.

- [ ] **Step 1: Read the rule before tagging anything**

Tag a clause **only if a watcher across the road would see the habit**. Two traps:

1. **Never keyword-match.** Line 524 is `stops anybody opening an umbrella indoors, and means it` — a `stops` match tags it as a lingerer, and then the sentence in the inspector and the behaviour on the street disagree. That disagreement is the exact fault this system exists to prevent.
2. **Indoors is not observable.** `watches the television with the sound down` (329) and `watches the racing standing up` (348) are lingering in a sense no watcher can see. Leave them.

A tag is appended after the clause text, preceded by `#`, in the existing style:

```
carries a stick and does not lean on it   # carries
```

Lines may already carry a tag such as `# elder`, `# m` or `# f`; append to it rather than replacing (`# elder carries`). `BeatIn` substring-matches anywhere after the `#`.

- [ ] **Step 2: Tag the confirmed carriers**

These are read and confirmed. Append `# carries` to each:

```
60    carries a screwdriver in a jacket pocket for no stated reason
135   drinks a flask of tea at eleven, wherever that happens to be
189   brings the same string bag to the shop and has mended the handle twice
213   brings a dog that sleeps under the table and is better known than the owner
220   brings the darts in a tin and will not use the house set
239   takes the jumble in early to have first look at it
243   brings the same cake to every sale and it is always gone first
250   brings flowers from the garden rather than buy them, and is proud of that
290   picks the beans at first light and leaves them in a bag on somebody's step
317   takes a bucket of scraps to a neighbour's pig on a Wednesday
391   carries a starting handle for a Morris Minor that does not need one
475   does the same walk with a flask and a folding stool and sits in the same field
514   keeps a tin of glucose tablets in a coat pocket and offers them to walkers
```

Lines 83, 302, 494, 763 and 846 are already tagged; leave them.

**Rejected, and why — do not tag these:** 416 (`carries a length of rope in the boot`) is in a car boot, not in a hand. 434 (`carries a clean handkerchief and a working one`) is not visible from across a road. 465 (`carves walking sticks`) is a hobby, not a habit of carrying. 132, 158, 178, 316, 407, 442, 482, 505, 625 all contain "tin" but the tin sits on a shelf or in a drawer.

- [ ] **Step 3: Tag the confirmed lingerers**

Append `# lingers` to each:

```
71    arrives eleven minutes early for everything and waits outside
85    stops at the top of Church Lane every time and has never said it is for breath
165   stands outside the phone box while someone else is in it
173   waits for the fish van on a Friday and buys the same fish
376   watches the river level from the bridge and reports it as a number
440   puts a coat on to go to the end of the path and takes it off at the gate
529   will not pass anybody on a staircase and waits at the bottom
831   watches the mill leat for trout and has never fished it
850   watches the mill race for an hour at a time and calls it clearing the head
873   stops at the same lay-by on the way back and looks at the same view
1007  puts a mint in before going into church, every Sunday
```

**Rejected:** 329, 333, 348, 957 are all indoors at a television or a wireless. 386 (`waits for the bus when walking would be quicker`) is a choice of transport, not a pause on a threshold. 524 is the umbrella trap from Step 1.

- [ ] **Step 4: Continue the pass over the rest of the file**

Work the whole file top to bottom applying the Step 1 rule. The lists above are the confirmed seed, not the finished job — expect to reach roughly 60–80 `carries` and 15–30 `lingers` in total. Tag conservatively: an untagged clause that deserved a tag costs nothing, and a wrongly tagged one puts a villager visibly doing something their sentence does not say.

- [ ] **Step 5: Verify the clause count did not change**

Run: `grep -vcE '^\s*$|^\s*#' Content/particulars.txt`
Expected: **914**, exactly as before. (An earlier draft of this plan said 1076; that came from a grep using `//` as the comment marker when this file comments with `#`, so it counted 162 header lines as clauses. The file header states 913 — one less than this grep reports, because a UTF-8 BOM on the file's first byte stops `^\s*#` from matching the file's own first comment line, so the parser's true clause count is 913, not 914. Worth recording once so it isn't chased again like the 1076 mistake was.) If this moved, a line was added, removed or split — the RNG stream has shifted and the whole village has changed. Undo and retag.

- [ ] **Step 6: Run the suite**

Run: `dotnet test Noir.sln -c Release`
Expected: 132 pass, 2 fail — the two 2:1 gates, which fail by design.

- [ ] **Step 7: Commit**

```bash
git add Content/particulars.txt
git commit -m "Tag the particulars that a watcher could actually see"
```

---

### Task 4: Prove the bridge end to end

Asserting `Beats & Lingers != 0` proves a field was set. The claim worth pinning is that a watcher *got* it.

**Files:**
- Modify: `tools/Noir.Core.Tests/BeatTests.cs` (add a fixture)

**Interfaces:**
- Consumes: the wiring from Task 2 and the tags from Task 3.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Write the failing test**

Append to `tools/Noir.Core.Tests/BeatTests.cs`:

```csharp
    /// <summary>
    /// The whole point, asserted at the far end.
    ///
    /// Runs the real village rather than a fixture, because what is being tested is whether the
    /// authored content reaches a watcher — and the content is the thing under test. Two days
    /// rather than the instrument's fourteen: this asks whether the manner appears at all, not
    /// what the ratio is.
    /// </summary>
    [TestFixture]
    public class BeatsAreEnactedTests
    {
        [Test]
        public void SomebodyWhoseParticularsSayTheyLingerIsSeenLingering()
        {
            var ctx = VillageContext.Load();
            var logs = Eyewitness.WatchAll(ctx, 2);

            int lingerers = 0, seenLingering = 0;

            for (int i = 0; i < logs.Length; i++)
            {
                var who = ctx.People.Get(new CitizenId(i));
                if (who == null || (who.Beats & Beat.Lingers) == 0) continue;

                lingerers++;
                foreach (Observed o in logs[i].Entries)
                {
                    if ((o.Manner & ObservedManner.Lingering) == 0) continue;
                    seenLingering++;
                    break;
                }
            }

            Assert.That(lingerers, Is.GreaterThan(0),
                "no villager drew a clause tagged `# lingers` — the editorial pass has not "
              + "reached anybody, so this proves nothing either way");

            Assert.That(seenLingering * 2, Is.GreaterThanOrEqualTo(lingerers),
                $"only {seenLingering} of {lingerers} lingerers were ever seen on a threshold "
              + "in two days — the pause is too short for a watcher who looks once a minute");
        }

        [Test]
        public void TheSentenceAndTheBagAreTheSameFact()
        {
            // A citizen who drew a clause tagged `# carries` must BE a carrier. This is the
            // property that deriving beats from particulars exists to guarantee: the sentence an
            // inspector prints and the thing a watcher sees can never be two facts that merely
            // happen to agree.
            var ctx = VillageContext.Load();
            int carriers = 0;

            for (int i = 0; i < ctx.People.Count; i++)
            {
                var who = ctx.People.Get(new CitizenId(i));
                if (who == null) continue;

                bool clauseSaysSo = false;
                foreach (int p in who.Particulars)
                    if ((ctx.Particulars.BeatAt(p) & Beat.Carries) != 0) clauseSaysSo = true;

                bool beatSaysSo = (who.Beats & Beat.Carries) != 0;
                Assert.That(beatSaysSo, Is.EqualTo(clauseSaysSo),
                    $"citizen {i} holds Beat.Carries={beatSaysSo} but their clauses say "
                  + $"{clauseSaysSo} — the two have come apart");

                if (clauseSaysSo) carriers++;
            }

            Assert.That(carriers, Is.GreaterThan(0),
                "nobody in the village drew a clause tagged `# carries`");
        }
    }
```

Add these usings to the top of `BeatTests.cs`:

```csharp
using Noir.Core.Observation;
using Noir.Sim;
```

- [ ] **Step 2: Run the test to verify it passes for the right reason**

Run: `dotnet test Noir.sln -c Release --filter "FullyQualifiedName~BeatsAreEnactedTests"`
Expected: PASS.

To confirm it is not passing vacuously, temporarily set `LingerBase = 0` and `LingerSpread = 6` in `Simulation.cs` and re-run: it must FAIL on the second assertion. **Restore both constants to 400 afterwards** and re-run to confirm PASS.

- [ ] **Step 3: Commit**

```bash
git add tools/Noir.Core.Tests/BeatTests.cs
git commit -m "Pin the far end: a lingerer is seen lingering"
```

---

### Task 5: Sweep, and record what moved

**Files:**
- Modify: `docs/STATE.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Confirm nobody got stuck on a doorstep**

Run: `dotnet run --project Noir.Sim -c Release -- strand --days 3`
Expected: nobody stranded, nobody misfiled. A 20–40 s pause at 6–14 thresholds a day is 2–9 minutes of drift against a ±15 min `Punctuality`, so this should be unchanged — but it is exactly the sort of thing that quietly leaves somebody standing in a doorway all day.

- [ ] **Step 2: Run the full suite**

Run: `dotnet test Noir.sln -c Release`
Expected: 132 pass, 2 fail — `TheMedianVillagerYieldsTwiceAsMuchTextureAsUse` and `TheTenthPercentileIsNotALock`, both by design.

- [ ] **Step 3: Read the instrument, expecting almost nothing**

Run: `dotnet run --project Noir.Sim -c Release -- ratio`

Record `texture_median`, `texture_min`, `median` and the `carry`/`linger` columns of the act-by-manner table. The design predicts `texture_median` and `texture_min` barely move, because the median and minimum villager still hold no tagged clause. **What should move is the `linger` column** — 6 of 158 on "came out" was the pre-change reading.

**Treat none of these numbers as evidence until the BIOS is updated.** Record them as provisional.

- [ ] **Step 4: Update `docs/STATE.md`**

Add a section at the top recording: what landed, the `linger` and `carry` columns before and after, that `Beat.RoundAbout` is gone and why, and that snapshots have not been re-rendered. State plainly that the ratio numbers were taken on unpatched hardware and are provisional.

- [ ] **Step 5: Commit**

```bash
git add docs/STATE.md
git commit -m "STATE: the particulars reach a watcher"
```

- [ ] **Step 6: After the BIOS update — re-render and diff**

Not part of this plan's verification, and must not be done before the microcode is fixed:

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.Snapshot.Render -logFile <log> -quit
```

Positions will differ for lingerers and whoever is near them; the population is unchanged, so **any other difference is a real defect**.
