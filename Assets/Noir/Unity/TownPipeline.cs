using System;
using System.IO;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// THE ONE WAY TO BUILD THE TOWN. Every render, audit, probe and Play session comes through
    /// here, and <c>TownPipelineTests.OnlyTheOnePipelineBuildsTheWorld</c> fails the suite if any
    /// other file under Assets/Noir/Unity or Assets/Noir/Editor calls <c>WorldBuilder.Build</c>.
    ///
    /// WHY THIS EXISTS. The pipeline used to live as a run of statements inside
    /// <c>VillageHost.Awake</c>, callable from nowhere, so every tool that wanted a town rebuilt
    /// the sequence by hand. On 2026-08-07 there were twenty call sites across eighteen files and
    /// only ONE of them - Awake itself - ran all of it. Fourteen ran no survey passes at all,
    /// which meant the menu command literally named "Render The Built Town" drew city.txt's 37
    /// ruled roads while the game built the 66 surveyed ones. MapAudit had already been bitten by
    /// this and said so in its own header - "an audit that measures a different town than the one
    /// on screen is worse than no audit, because it is believed" - and the fix was applied to
    /// MapAudit alone. One line out of five, and the other four kept lying for another two weeks.
    ///
    /// THE ORDER IS LOAD-BEARING and is the reason this is a function rather than a convention.
    /// RuledAway before SeatOnSurvey, so nothing is carefully seated and then taken down again.
    /// DowntownFromSanborn after SeatOnSurvey, because that pass deliberately SKIPS lots ruled
    /// `footprint later` and leaves them for this one. FillFromSurvey last, so it can see
    /// everything already standing and put nothing up on top of it. Written as prose in a comment,
    /// that ordering survived exactly as long as the next person who copied four lines of it.
    /// </summary>
    public static class TownPipeline
    {
        /// <summary>What a build produced. The layout is kept because the passes annotate it and
        /// the tools read those annotations back.</summary>
        public readonly struct Result
        {
            public readonly VillageLayout Layout;
            public readonly WorldModel World;
            public readonly ValidationReport Validation;

            public Result(VillageLayout layout, WorldModel world, ValidationReport validation)
            {
                Layout = layout; World = world; Validation = validation;
            }
        }

        /// <summary>The town, built the way the game builds it. This is what a tool wants.</summary>
        public static Result Build() => Build(VillageHost.MapFile, VillageHost.Seed);

        /// <summary>
        /// The town from a named map file, with the full survey layer applied.
        ///
        /// The map file is a parameter and the survey passes are not, because the passes are keyed
        /// to the real town's parcel ids. Handing this a fixture would not fail - it would quietly
        /// do nothing, which is worse.
        /// </summary>
        public static Result Build(string mapFile, ulong seed)
        {
            if (!ContentLoader.Exists)
                throw new Exception($"Content not found at {ContentLoader.Root}");

            // Before anything reads a kind. Core cannot open a file, so without this it falls back
            // to the copy compiled into PlaceKindTable - which covers the enum but not the content,
            // so a barber authored in city.txt fails as an unknown kind while working in the tools.
            PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));

            // What the town has, and when it gets it. CAUGHT, NOT FATAL, and the asymmetry is the
            // design: a technology with no row is a technology the town does not have, so a missing
            // file means the opening world rather than a village that will not load. The kind table
            // one line up is the opposite and throws, because a kind with no row is a building with
            // no rooms. A MALFORMED file still stops here - an authoring mistake is not an absence.
            //
            // Note this ran in Awake and NOWHERE ELSE before the pipeline was extracted, so every
            // offline render had the town frozen at its opening technology regardless of the date.
            try
            {
                TechnologyTable.Install(TechnologyTable.Parse(ContentLoader.Read("technology.txt")));
            }
            catch (FileNotFoundException)
            {
                Debug.LogWarning("[era] no Content/technology.txt - the town stays in 1991.");
            }

            var layout = VillageParser.Parse(ContentLoader.Read(mapFile));

            // The passes are about to say what they did with each of the owner's rulings, and the
            // answer is written back where the browser map can read it - see SurveyReport.
            SurveyReport.Clear();

            // The roads in city.txt are DERIVED, not authored - SOURCES-OF-TRUTH.md is explicit
            // that when they disagree with the parcels the parcels win and the road gets moved.
            // Content/roads.txt is that move, done from survey rather than by sliding a ruled line
            // sideways. No-ops if the file is not there.
            SurveyRoads.Apply(layout);

            // The buildings on lots the owner ruled had none in 1991. Decided on the layout, once,
            // so everything that walks AllPlaces afterwards sees one town.
            RuledAway.Apply(layout);

            // And what is left stands where it was measured standing, at the size it was measured
            // being - rather than on the generator's 13x7 box.
            SeatOnSurvey.Apply(layout);

            // The downtown lots SeatOnSurvey just skipped, because their measured shape is what
            // REPLACED 1991 rather than a late photograph of it. Laid from the 1913 Sanborn
            // frontages instead. Its own substream, so adding the pass does not shift the numbers
            // every other system draws.
            DowntownFromSanborn.Apply(layout, Xoshiro256ss.Substream(seed, "downtown"));

            // Last, so it can see everything already standing - including the terrace above.
            FillFromSurvey.Apply(layout);
            SurveyReport.Write();

            return Finish(layout, seed, mapFile);
        }

        /// <summary>
        /// A world from a layout that is NOT the real town - a fixture, or a map built in memory.
        /// NO SURVEY PASSES RUN.
        ///
        /// Named for what it gives up rather than what it does, because the whole failure this
        /// class exists to stop was tools quietly building an unsurveyed town and believing it.
        /// If you are reaching for this and your layout came from city.txt, you want
        /// <see cref="Build()"/> instead.
        /// </summary>
        public static Result BuildUnsurveyed(VillageLayout layout, ulong seed, string named = "<in-memory map>")
        {
            return Finish(layout, seed, named);
        }

        private static Result Finish(VillageLayout layout, ulong seed, string named)
        {
            var world = WorldBuilder.Build(layout, seed);
            var validation = WorldValidator.Validate(world);

            // NAMED FROM THE MAP, not written out. These said "village.txt:" while the game had
            // loaded city.txt for months, and an error that names the wrong file sends whoever
            // reads it to go and look at the wrong one - which it did.
            foreach (var problem in validation.Errors) Debug.LogError(named + ": " + problem);
            foreach (var warning in validation.Warnings) Debug.LogWarning(named + ": " + warning);

            return new Result(layout, world, validation);
        }
    }
}
