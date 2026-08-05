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

                // ---- and put the lot back ----
                ParcelNotes.Save(TestId, new ParcelNotes.Note());
                byId.SetValue(null, null);

                log.AppendLine("---- 4. cleanup ----");
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
            log.AppendLine("---- 5. the real file is untouched ----");
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
