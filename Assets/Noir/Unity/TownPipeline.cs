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
            // FIRST, before anything asks for a file. The survey tables live in
            // Noir.Core.Survey now and read through Noir.Core.Contracts.Content, which has to be
            // told where content comes from; Core cannot open a file for itself and that is the
            // point of the whole arrangement.
            Content.Install(ContentLoader.AsSource);

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

            // SeatOnSurvey is about to note every building it seats, so InteriorsReport.Write can
            // resolve them against the finished world further down - see its own header for why
            // noting and resolving happen on opposite sides of WorldBuilder.Build.
            InteriorsReport.Clear();

            // And the floor-plan census is opened here and CLOSED BELOW, after FillFromSurvey -
            // because two passes attach the owner's plans, not one. See FloorPlans._attached.
            FloorPlans.ResetCensus();

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

            // EVERY PASS THAT CAN ATTACH A FLOOR PLAN HAS NOW RUN, so the census can be closed
            // and the plans nothing asked for can be named. Before 2026-08-20 SeatOnSurvey closed
            // it at the end of its own pass, which is two passes too early: FillFromSurvey raises
            // the 313 buildings the map never had - including 408 Holmes Street, the one building
            // in Rossville the owner has actually drawn - and every one of them attached after
            // the tally had already been printed.
            FloorPlans.Finish();

            // AND THEN NOTHING IS LEFT STANDING IN A ROAD. This runs after every other pass on
            // purpose: FillFromSurvey refuses to RAISE a house in a road, but the fifty that were
            // already in one came out of city.txt, authored against the 37 ruled roads that
            // SurveyRoads replaced with the county's 66. The streets moved and the houses did not.
            // Measured buildings are never moved - see the header of ClearOfRoads.
            int cleared = ClearOfRoads.Apply(layout, out int stuck);
            if (cleared > 0 || stuck > 0)
                Debug.Log($"[survey] {cleared} buildings moved off a road corridor"
                        + (stuck > 0 ? $", {stuck} could not be cleared and are STILL IN A ROAD" : "")
                        + ".");

            // AND THEN THE TOWN TRADES UNDER THE NAMES THE OWNER GAVE IT.
            //
            // LAST OF THE LAYOUT PASSES AND BEFORE Finish, because it can change a place's KIND
            // and everything a kind decides - the interior, the rooms, the counters, the job
            // slots, the opening hours, the households - is generated as the world is built. A
            // kind changed after that would be a tavern with a shop's rooms in it.
            BusinessFromRulings.Apply(layout);

            SurveyReport.Write();

            var built = Finish(layout, seed, mapFile);

            // AFTER Finish, not beside SurveyReport.Write() above - unlike a verdict, an interior
            // does not exist until WorldBuilder has stamped the rooms and the furniture into the
            // world, so this is the earliest point the notes SeatOnSurvey took can be resolved.
            InteriorsReport.Write(built.World);

            // AND THEN PEOPLE HAVE SOMEWHERE TO WALK, AND EVERY YARD BELONGS TO SOMEBODY.
            //
            // After Finish and not inside it, because both write to the GRID - which does not
            // exist until the world is built - and because BuildUnsurveyed must never get either:
            // the parcel ids and the walk rulings are both the real town's.
            //
            // WALKS BEFORE LOTS, and the order is load-bearing the same way the passes above are.
            // A sidewalk is laid Path|Verge and LotsFromSurvey leaves Verge alone, so running it
            // first means the footway is already public when the parcels come to claim ground.
            // The other way round, any lot whose polygon overhangs its own frontage would charge
            // a walker trespass for using the pavement outside it.
            WalksFromSurvey.Apply(built.World);
            LotsFromSurvey.Apply(built.World);

            return built;
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

            // AND THEN EVERY DOOR OPENS ONTO SOMETHING.
            //
            // AFTER THE BUILD AND BEFORE THE VALIDATION, which is the only place it can go. A door
            // is judged on the GRID - can it be stood on, is it joined to the town - and the grid
            // does not exist until the world is built. Judged on the layout instead, a neighbour's
            // LOT reads as its walls and ninety doors look bricked up when nine are.
            //
            // ClearOfRoads shoves buildings off road corridors and carries each door along at its
            // authored offset, which is right - a door is part of a building. What it cannot carry
            // is the ground outside the wall, so on 2026-08-09 nine doors ended up opening into a
            // neighbour's back wall, in mutual pairs, each sealing the other.
            //
            // ONE REBUILD, and only when something moved: the door is carved into the grid as the
            // world is built, so a moved door is not in the grid that found it. The build is about
            // two seconds and this is the only pass that needs a second one.
            int refaced = DoorsThatOpen.Apply(layout, world, out int walledIn);
            if (refaced > 0 || walledIn > 0)
            {
                Debug.Log($"[survey] {refaced} door(s) moved round to a wall with ground outside it"
                        + (walledIn > 0
                            ? $", {walledIn} building(s) are WALLED IN ON EVERY SIDE and cannot be helped"
                            : "")
                        + ".");

                if (refaced > 0) world = WorldBuilder.Build(layout, seed);
            }

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
