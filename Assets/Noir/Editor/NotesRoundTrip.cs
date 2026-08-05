using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Proves that a household typed into the parcel editor survives the trip to disk and back.
    ///
    /// WHY THIS EXISTS AS A SCRIPT RATHER THAN AS A CLICK: the household editor never wrote a
    /// single person. Write() serialised the derived Adults/Kids counts and not note.People, and
    /// Read() had no `person` branch at all - ReadPerson was written, commented, and called by
    /// nothing. Within one Play session the drafts carry from lot to lot correctly, so the whole
    /// failure is invisible until Play stops, which is exactly why it survived a night of
    /// testing. A round trip that runs without a human is the only honest check.
    ///
    /// Runs on a marker file and a domain reload, the same way PerfProbeAutostart does and for
    /// the same reason - the editor's dynamic-command host does not execute in this project.
    ///
    /// SAFETY: Content/parcel-notes.txt is the sole copy of everything anybody has authored about
    /// this town. The real bytes are read into memory first and written back in a finally, and
    /// the test parcel id is far outside any real one. Nothing here is allowed to cost a name.
    /// </summary>
    [InitializeOnLoad]
    internal static class NotesRoundTrip
    {
        private static readonly string Marker =
            Path.Combine(Path.GetTempPath(), "noir-notes-roundtrip-please.txt");

        private static readonly string ReportPath =
            Path.Combine(Path.GetTempPath(), "noir-notes-roundtrip.txt");

        /// <summary>Well outside any real parcel id, so a mistake cannot land on a real lot.</summary>
        private const int TestId = 900001;

        /// <summary>A second one, for the checks that need two lots - moving house, mainly.</summary>
        private const int SpareId = 900002;

        static NotesRoundTrip()
        {
            // SAYS WHY IT DID NOT RUN. The first attempt left the marker sitting there and the
            // report never appeared, and there are three different reasons that can happen -
            // no marker, the callback thrown away by a second domain reload, or the editor in
            // Play. Guessing between them is what wasted a night last time; the log says which.
            Debug.Log("[notes-roundtrip] loaded. marker=" + File.Exists(Marker));
            if (!File.Exists(Marker)) return;

            EditorApplication.delayCall += () =>
            {
                Debug.Log("[notes-roundtrip] delayCall. marker=" + File.Exists(Marker)
                        + " isPlaying=" + EditorApplication.isPlaying);
                if (!File.Exists(Marker)) return;

                // IN PLAY IS A WAIT, NOT A REFUSAL. Play mode is the normal state of this editor
                // while anybody is testing, and returning here simply lost the request until
                // somebody happened to reload again. Now it queues itself for the moment Play
                // stops, which is when the check can actually run.
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.playModeStateChanged += WhenPlayStops;

                    // AND STOPS IT, rather than waiting for somebody to come and do it. Driving
                    // this from outside meant AppActivate followed by SendKeys("^p"), which has
                    // to land on the right one of three Unity windows to mean anything - twice it
                    // did not, and reported success both times, because AppActivate returning
                    // true says a window was raised and nothing about where the keystroke went.
                    // There is an API for this and it does not care about focus.
                    EditorApplication.isPlaying = false;
                    return;
                }

                File.Delete(Marker);
                Run();
            };
        }

        private static void WhenPlayStops(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= WhenPlayStops;
            if (!File.Exists(Marker)) return;
            File.Delete(Marker);
            Run();
        }

        private static void Run()
        {
            var log = new StringBuilder();
            string notesPath = Path.Combine(ContentLoader.Root, "parcel-notes.txt");
            string original = File.Exists(notesPath) ? File.ReadAllText(notesPath) : null;
            int failures = 0;

            log.AppendLine("PARCEL NOTES ROUND TRIP");
            log.AppendLine("=======================");
            log.AppendLine("file            : " + notesPath);
            log.AppendLine("bytes before    : " + (original == null ? "(no file)" : original.Length.ToString()));
            log.AppendLine();

            try
            {
                // ---- the household that goes in ----
                // The third adult is deliberately left Unrecorded: that is the default state of
                // every person typed into the editor before somebody touches the M/F button, so
                // it is the one that has to survive the trip, not just the two decided ones.
                var wanted = new List<ParcelNotes.Person>
                {
                    Person("Testcase", "Ninepenny", 47, false, ParcelNotes.Sex.Man,
                           "curtain-twitcher", "night owl"),
                    Person("Probe", "Ninepenny", 44, false, ParcelNotes.Sex.Woman,
                           "light sleeper"),
                    Person("Lodger", "Tenthpenny", 31, false, ParcelNotes.Sex.Unrecorded),
                    Person("Smallfry", "Ninepenny", 8, true, ParcelNotes.Sex.Woman),
                };

                var note = new ParcelNotes.Note
                {
                    Adults = 3,
                    Kids = 1,
                    Character = "written by NotesRoundTrip, deleted again on the next line",
                    Zoning = ParcelNotes.Zoning.Residential,
                };
                ParcelNotes.SetResidents(note, wanted);

                ParcelNotes.Save(TestId, note);

                // ---- what actually reached the file ----
                // ONLY THE PEOPLE THIS TEST MADE. Person lines are top-level now rather than
                // sitting under a parcel, so a bare `StartsWith("person ")` counts the whole
                // town's residents and fails a run in which everything worked - which it did,
                // the first time this ran against a file somebody had actually authored into.
                // Scoped by the ids this lot points at.
                string onDisk = File.ReadAllText(notesPath);
                var mine = new HashSet<int>(note.Lives);
                var personLines = new List<string>();
                foreach (var raw in onDisk.Split('\n'))
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("person ")) continue;
                    var bits = line.Split(' ');
                    if (bits.Length >= 2 && int.TryParse(bits[1], out int pid) && mine.Contains(pid))
                        personLines.Add(line);
                }

                // COUNTED FROM THE HOUSEHOLD, not written down again. This said "3" while the
                // household above grew to four, and failed a run in which every single field had
                // round-tripped correctly - a constant sized against another number, rotting the
                // moment that number moved, inside the probe written to catch that.
                log.AppendLine("---- 1. did person lines reach the file? ----");
                log.AppendLine("person lines written : " + personLines.Count
                             + " (want " + wanted.Count + ")");
                foreach (var line in personLines) log.AppendLine("   " + line);
                if (personLines.Count != wanted.Count) { failures++; log.AppendLine("   ** FAIL"); }
                else log.AppendLine("   ok");

                // AND THAT THE LOT POINTS AT THEM. The person lines above prove the people were
                // written; they say nothing about whether anybody lives anywhere. Those are two
                // separate lines in the file now, and either could be written without the other.
                int livesLines = 0;
                foreach (var raw in onDisk.Split('\n'))
                    if (raw.Trim().StartsWith("parcel " + TestId + " lives ")) livesLines++;
                log.AppendLine("lives lines written  : " + livesLines
                             + " (want " + wanted.Count + ")");
                if (livesLines != wanted.Count) { failures++; log.AppendLine("   ** FAIL"); }
                else log.AppendLine("   ok");
                log.AppendLine();

                // ---- force a genuine re-read, not the cache ----
                var byId = typeof(ParcelNotes).GetField("_byId",
                    BindingFlags.NonPublic | BindingFlags.Static);
                byId.SetValue(null, null);

                var back = ParcelNotes.For(TestId);

                log.AppendLine("---- 2. does it come back? ----");
                if (back == null)
                {
                    failures++;
                    log.AppendLine("   ** FAIL - the note did not come back at all");
                }
                else
                {
                    var backPeople = ParcelNotes.Residents(back);
                    log.AppendLine("people read back : " + backPeople.Count
                                 + " (want " + wanted.Count + ")");
                    if (backPeople.Count != wanted.Count) failures++;

                    int n = Mathf.Min(backPeople.Count, wanted.Count);
                    for (int i = 0; i < n; i++)
                    {
                        var a = wanted[i];
                        var b = backPeople[i];
                        bool same = a.First == b.First && a.Last == b.Last
                                 && a.Age == b.Age && a.Child == b.Child
                                 && a.Which == b.Which
                                 && SameTraits(a.Traits, b.Traits);
                        if (!same) failures++;
                        log.AppendLine(string.Format("   {0} {1} {2}, {3}, {4}, age {5}, [{6}]",
                            same ? "ok  " : "FAIL", b.First, b.Last, b.Which,
                            b.Child ? "child" : "adult", b.Age, string.Join("|", b.Traits.ToArray())));
                        if (!same)
                            log.AppendLine(string.Format("        wanted {0} {1}, {2}, {3}, age {4}, [{5}]",
                                a.First, a.Last, a.Which, a.Child ? "child" : "adult", a.Age,
                                string.Join("|", a.Traits.ToArray())));
                    }
                }
                log.AppendLine();

                // ---- 3. a household written in the OLD shape still loads ----
                //
                // People used to be written UNDERNEATH a lot, with no id: `parcel N person adult
                // "..." "..." age "..." m`. The spec said there was nothing to migrate because
                // the file held no person lines - true when it was written that morning, and
                // false a few hours later once somebody had used the editor. A lot carrying only
                // one of those lines, with no `names` blob to fall back on, would have lost its
                // household in silence.
                log.AppendLine("---- 3. a legacy person line, written under a lot ----");
                ParcelNotes.Save(SpareId, new ParcelNotes.Note
                                          { Zoning = ParcelNotes.Zoning.Residential });
                File.WriteAllText(notesPath, File.ReadAllText(notesPath)
                    + "\nparcel " + SpareId + " person adult \"Legacy\" \"Fourteenpenny\" 61 "
                    + "\"keeps chickens\" m\n");
                byId.SetValue(null, null);

                var oldShape = ParcelNotes.Residents(ParcelNotes.For(SpareId));
                bool migrated = oldShape.Count == 1
                             && oldShape[0].Id > 0
                             && oldShape[0].First == "Legacy"
                             && oldShape[0].Age == 61
                             && oldShape[0].Which == ParcelNotes.Sex.Man
                             && oldShape[0].Traits.Count == 1;
                log.AppendLine("picked up, given an id, nothing lost : "
                             + (migrated ? "ok" : "** FAIL"));
                if (!migrated) failures++;
                ParcelNotes.Save(SpareId, new ParcelNotes.Note());
                log.AppendLine();

                // ---- 4. somebody who lives nowhere ----
                //
                // The roster is the entire reason people have ids, and it is the case the old
                // parcel-owned format could not express at all: there was no line you could
                // write for a person with no lot.
                log.AppendLine("---- 4. a person with no lot ----");
                var drifter = new ParcelNotes.Note();
                ParcelNotes.SetResidents(drifter, new List<ParcelNotes.Person>
                {
                    Person("Nomad", "Fifteenpenny", 33, false, ParcelNotes.Sex.Man, "drinks alone"),
                });
                ParcelNotes.Save(SpareId, drifter);
                int drifterId = drifter.Lives[0];
                ParcelNotes.Save(SpareId, new ParcelNotes.Note());   // the lot goes away
                byId.SetValue(null, null);                           // genuine re-read

                bool adrift = false;
                foreach (var p in ParcelNotes.Unhoused()) if (p.Id == drifterId) adrift = true;
                var stillThere = ParcelNotes.PersonById(drifterId);
                bool kept = adrift && stillThere != null
                         && stillThere.First == "Nomad" && stillThere.Traits.Count == 1;
                log.AppendLine("lot emptied, person still here and unhoused : "
                             + (kept ? "ok" : "** FAIL"));
                if (!kept) failures++;
                ParcelNotes.DeletePerson(drifterId);
                log.AppendLine();

                // ---- 5. a father and a son with the same name ----
                //
                // What a value-identified format cannot do. Two people alike in every visible
                // field must come back as two people, not one applied twice - and in a village
                // a Junior and a Senior is an ordinary thing rather than a corner case.
                log.AppendLine("---- 5. same name, different people ----");
                var pairNote = new ParcelNotes.Note { Adults = 2 };
                ParcelNotes.SetResidents(pairNote, new List<ParcelNotes.Person>
                {
                    Person("Junior", "Sixteenpenny", 58, false, ParcelNotes.Sex.Man,
                           "on the village board"),
                    Person("Junior", "Sixteenpenny", 19, false, ParcelNotes.Sex.Man, "night owl"),
                });
                ParcelNotes.Save(SpareId, pairNote);
                byId.SetValue(null, null);

                var readPair = ParcelNotes.Residents(ParcelNotes.For(SpareId));
                bool distinct = readPair.Count == 2
                             && readPair[0].Id != readPair[1].Id
                             && readPair[0].Age != readPair[1].Age
                             && readPair[0].Traits.Count == 1 && readPair[1].Traits.Count == 1
                             && readPair[0].Traits[0] != readPair[1].Traits[0];
                log.AppendLine("two of them stayed apart : " + (distinct ? "ok" : "** FAIL"));
                if (!distinct) failures++;
                foreach (var p in readPair) ParcelNotes.DeletePerson(p.Id);
                ParcelNotes.Save(SpareId, new ParcelNotes.Note());
                log.AppendLine();

                // ---- 6. the highest id stays dead ----
                //
                // Deleting a MIDDLE id proves nothing here: max+1 survives that and fails this.
                // This is the check that would have caught the derived counter that got into the
                // first draft of the spec, and it is why `nextperson` is written to the file.
                log.AppendLine("---- 6. a deleted id is not handed out again ----");
                var counting = new ParcelNotes.Note();
                ParcelNotes.SetResidents(counting, new List<ParcelNotes.Person>
                {
                    Person("Alpha", "Seventeenpenny", 40, false, ParcelNotes.Sex.Woman),
                    Person("Beta", "Seventeenpenny", 41, false, ParcelNotes.Sex.Man),
                });
                ParcelNotes.Save(SpareId, counting);
                int highest = 0;
                foreach (int id in counting.Lives) if (id > highest) highest = id;

                ParcelNotes.Save(SpareId, new ParcelNotes.Note());
                foreach (int id in counting.Lives) ParcelNotes.DeletePerson(id);
                byId.SetValue(null, null);                          // genuine re-read

                var afterDelete = new ParcelNotes.Note();
                ParcelNotes.SetResidents(afterDelete, new List<ParcelNotes.Person>
                {
                    Person("Gamma", "Seventeenpenny", 42, false, ParcelNotes.Sex.Woman),
                });
                int minted = afterDelete.Lives.Count > 0 ? afterDelete.Lives[0] : -1;
                bool fresh = minted > highest;
                log.AppendLine($"deleted up to id {highest}, next minted {minted} : "
                             + (fresh ? "ok" : "** FAIL"));
                if (!fresh) failures++;
                if (minted > 0) ParcelNotes.DeletePerson(minted);
                log.AppendLine();

                // ---- 7. a lives line naming nobody ----
                //
                // Refusing to load a file that is otherwise fine would be worse than losing the
                // one resident. The lot must keep its zoning and everybody else.
                log.AppendLine("---- 7. dangling lives id ----");
                ParcelNotes.Save(SpareId, new ParcelNotes.Note
                                          { Zoning = ParcelNotes.Zoning.Residential });
                File.WriteAllText(notesPath, File.ReadAllText(notesPath)
                    + "\nparcel " + SpareId + " lives 999999\n");
                byId.SetValue(null, null);

                var survivor = ParcelNotes.For(SpareId);
                bool heldUp = survivor != null
                           && survivor.Zoning == ParcelNotes.Zoning.Residential
                           && survivor.Lives.Count == 0;
                log.AppendLine("lot kept its zoning and dropped the ghost : "
                             + (heldUp ? "ok" : "** FAIL"));
                if (!heldUp) failures++;
                ParcelNotes.Save(SpareId, new ParcelNotes.Note());
                log.AppendLine();

                // ---- 8. a move keeps the whole person ----
                //
                // The reason ids exist at all. Traits, age and sex must survive changing houses,
                // because the alternative - retyping them at the other end - is exactly how they
                // get lost, and it is what the old parcel-owned format forced.
                log.AppendLine("---- 8. moving house ----");
                byId.SetValue(null, null);
                var fromLot = new ParcelNotes.Note();
                ParcelNotes.SetResidents(fromLot, new List<ParcelNotes.Person>
                {
                    Person("Mover", "Eighteenpenny", 52, false, ParcelNotes.Sex.Woman,
                           "keeps bees", "won't cross the county line"),
                });
                ParcelNotes.Save(TestId, fromLot);
                int moverId = fromLot.Lives[0];

                ParcelNotes.Save(TestId, new ParcelNotes.Note());      // leaves the first house
                var toLot = new ParcelNotes.Note();
                ParcelNotes.SetResidents(toLot, new List<ParcelNotes.Person>
                                                { ParcelNotes.PersonById(moverId).Copy() });
                ParcelNotes.Save(SpareId, toLot);
                byId.SetValue(null, null);                             // genuine re-read

                var moved = ParcelNotes.Residents(ParcelNotes.For(SpareId));
                bool wholeMove = moved.Count == 1
                              && moved[0].Id == moverId
                              && moved[0].First == "Mover"
                              && moved[0].Age == 52
                              && moved[0].Which == ParcelNotes.Sex.Woman
                              && moved[0].Traits.Count == 2
                              && moved[0].Traits.Contains("keeps bees");
                log.AppendLine("same id, same traits, new lot : " + (wholeMove ? "ok" : "** FAIL"));
                if (!wholeMove) failures++;
                ParcelNotes.Save(SpareId, new ParcelNotes.Note());
                ParcelNotes.DeletePerson(moverId);
                log.AppendLine();

                // ---- 9. a man lives at one address and keeps a shop at another ----
                //
                // The thing a business needed and a household list could not say. A shop has an
                // owner and staff, not residents, and the owner sleeps somewhere else - so ONE
                // person is pointed at by two different lots, in two different ways, and both
                // are true. No format where a person is a line underneath a lot can express it.
                log.AppendLine("---- 9. lives at one lot, runs a shop at another ----");
                byId.SetValue(null, null);

                var house = new ParcelNotes.Note { Adults = 1 };
                ParcelNotes.SetResidents(house, new List<ParcelNotes.Person>
                {
                    Person("Keeper", "Nineteenpenny", 54, false, ParcelNotes.Sex.Man,
                           "runs a business in town"),
                });
                ParcelNotes.Save(TestId, house);
                int keeperId = house.Lives[0];

                var shop = new ParcelNotes.Note { Business = "the hardware" };
                var owner = ParcelNotes.PersonById(keeperId).Copy();
                owner.Proprietor = true;
                var hand = Person("Help", "Twentypenny", 19, false, ParcelNotes.Sex.Man);
                hand.Proprietor = false;
                ParcelNotes.SetWorkers(shop, new List<ParcelNotes.Person> { owner, hand });
                ParcelNotes.Save(SpareId, shop);
                byId.SetValue(null, null);                          // genuine re-read

                var stillHome = ParcelNotes.Residents(ParcelNotes.For(TestId));
                var keeping = ParcelNotes.Workers(ParcelNotes.For(SpareId));
                bool bothTrue = stillHome.Count == 1 && stillHome[0].Id == keeperId
                             && keeping.Count == 2
                             && keeping[0].Id == keeperId && keeping[0].Proprietor
                             && !keeping[1].Proprietor
                             && ParcelNotes.For(SpareId).Lives.Count == 0;
                log.AppendLine("one person, a home and a shop, no residents at the shop : "
                             + (bothTrue ? "ok" : "** FAIL"));
                if (!bothTrue) failures++;

                // And the person record itself must not have picked up the ownership - it is a
                // fact about the link, and he is nobody's proprietor at home.
                var record = ParcelNotes.PersonById(keeperId);
                bool clean = record != null && !record.Proprietor;
                log.AppendLine("the record itself claims no ownership : " + (clean ? "ok" : "** FAIL"));
                if (!clean) failures++;

                ParcelNotes.Save(SpareId, new ParcelNotes.Note());
                foreach (var p in keeping) ParcelNotes.DeletePerson(p.Id);
                log.AppendLine();

                // ---- and put the lot back ----
                ParcelNotes.Save(TestId, new ParcelNotes.Note());
                byId.SetValue(null, null);

                log.AppendLine("---- 10. cleanup ----");
                log.AppendLine("test note removed : " + (ParcelNotes.For(TestId) == null ? "ok" : "** FAIL"));
                if (ParcelNotes.For(TestId) != null) failures++;
            }
            catch (System.Exception e)
            {
                failures++;
                log.AppendLine("** THREW: " + e);
            }
            finally
            {
                // The real file goes back byte for byte whatever happened above.
                if (original != null) File.WriteAllText(notesPath, original);
                typeof(ParcelNotes).GetField("_byId", BindingFlags.NonPublic | BindingFlags.Static)
                    .SetValue(null, null);
            }

            string after = File.Exists(notesPath) ? File.ReadAllText(notesPath) : null;
            log.AppendLine();
            log.AppendLine("---- 11. the real file is untouched ----");
            bool intact = original == after;
            if (!intact) failures++;
            log.AppendLine("bytes after     : " + (after == null ? "(no file)" : after.Length.ToString()));
            log.AppendLine("identical       : " + (intact ? "ok" : "** FAIL - RESTORE DID NOT HOLD"));
            log.AppendLine();
            log.AppendLine(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED");

            File.WriteAllText(ReportPath, log.ToString());
            Debug.Log("[notes-roundtrip] " + (failures == 0 ? "PASSED" : failures + " FAILED")
                    + " - report at " + ReportPath);
        }

        private static ParcelNotes.Person Person(string first, string last, int age, bool child,
                                                 ParcelNotes.Sex which, params string[] traits)
        {
            var p = new ParcelNotes.Person { First = first, Last = last, Age = age, Child = child,
                                             Which = which };
            foreach (var t in traits) p.Traits.Add(t);
            return p;
        }

        private static bool SameTraits(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
