using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Core.Contracts;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// THE TOWN AS THE GAME ACTUALLY BUILDS IT.
    ///
    /// Everything else that ever checked "is a house standing in the road" checked a FILE. The
    /// Core tests read Content/city.txt and Content/roads.txt and reason about them, which is
    /// worth doing and is not the same claim: the town on screen is those files put through five
    /// survey passes - SurveyRoads, RuledAway, SeatOnSurvey, DowntownFromSanborn, FillFromSurvey -
    /// and then ClearOfRoads. Any one of them can move a building, and only this assembly can see
    /// the result.
    ///
    /// That gap is exactly how the owner's complaint survived for weeks. He said houses were
    /// standing in the road; the file-level tests said otherwise and were right about the files.
    ///
    /// No Category attribute, deliberately - these must run inside the `!Diagnostic` gate, which
    /// is the suite anybody actually runs.
    /// </summary>
    public class TownGeometryPlayTests
    {
        /// <summary>
        /// How far into a carriageway a building may reach. Same number SeatOnSurvey and
        /// ClearOfRoads use: the footprints are traced from imagery and the centrelines come from
        /// the county, so the two disagree by tens of centimetres everywhere without either being
        /// wrong.
        /// </summary>
        private const float Tolerance = 0.5f;

        [UnityTest]
        public IEnumerator NoBuildingStandsInAStreet()
        {
            yield return CityUnderTest.WaitUntilBuilt();

            var world = CityUnderTest.World;
            Assert.That(world, Is.Not.Null, "the town did not build");

            var table = PlaceKindTable.Current;
            var offenders = new List<string>();
            float worst = 0f;

            foreach (var place in world.AllPlaces)
            {
                if (!table.Row(place.Kind).IsBuilding) continue;

                float pen = place.Outline != null && place.Outline.Length >= 3
                    ? RoadCorridor.WorstPenetration(world.Roads, place.Outline, RoadCorridor.StreetWidth)
                    : RoadCorridor.WorstPenetration(world.Roads, place.Bounds, RoadCorridor.StreetWidth);

                if (pen <= Tolerance) continue;
                if (pen > worst) worst = pen;
                var road = RoadCorridor.RoadUnder(world.Roads, place.Bounds, RoadCorridor.StreetWidth);
                offenders.Add($"{place.Name} {place.Bounds} — {pen:0.0} m into "
                            + (road.Length > 0 ? road : "<an outline-only hit>"));
            }

            Debug.Log($"[geometry] {offenders.Count} buildings standing in a street, worst {worst:0.0} m");
            foreach (var o in offenders) Debug.Log("[geometry]   " + o);

            // A RATCHET AT THE MEASURED VALUE, AND AN OPEN QUESTION - said plainly rather than
            // tuned into a green tick.
            //
            // The layout-level test (ClearOfRoadsTests.NoAuthoredBuildingIsLeftStandingInARoad)
            // measures city.txt against roads.txt and reports ZERO. This measures the same town
            // after the pipeline and reports 28. Both cannot be describing the same thing, and
            // the difference is NOT the passes - ClearOfRoads runs last and leaves 8 stuck, not
            // 28. It is the ROAD: a layout carries RoadRun, a list of points with a declared
            // width; the world carries RoadLine wrapping a RoadPath that has been smoothed and
            // resampled, and RoadPath.Project is being asked for a lateral distance against that
            // smoothed curve. Until somebody establishes which of the two corridors the tarmac is
            // actually drawn to, the honest number is "28, and it must not grow".
            //
            // Ratcheting rather than asserting zero because zero here would mean deleting the
            // test, and this is the only assertion in the project that can see the built town at
            // all. That blindness is why "there are houses in the road" survived for weeks.
            // RE-RECORDED AT 28, 2026-08-10 - this is ROAD-FIXES GATE-5, and the plan asks for it
            // to be taken "from THAT run" because the old figure was unstable: it "reads 9 in two
            // logs and 31 in a third".
            //
            // It reads 28 in FOUR independent PlayMode runs on today's tree, worst penetration
            // 4.9 m every time. A ratchet at 134 is nearly five times the real number: 106 more
            // buildings could drift into a carriageway before it said a word, which is a ratchet
            // that has stopped ratcheting.
            //
            // The open question above is unchanged and still open - the layout-level test reports
            // zero against this number, and until somebody establishes which corridor the tarmac
            // is drawn to, that gap stays unexplained.
            //
            // RE-RECORDED AT 40, 2026-08-12 - not a new building drifting into a road. 112 S
            // Chicago (parcel 237) was already one of the 28: one Place, "the Chicago Street
            // terrace", already measured at ~4.9 m into Smith Street. The terrace-business-units
            // feature splits that single Place into 13 independently-named storefronts sharing
            // the same footprint, so the one known violation now prints 13 times. Proof it is
            // nothing else: 40 offenders - 13 Chicago storefronts = 27, and 27 + 1 (the old single
            // terrace entry) = 28, the exact prior baseline - every other offender on the list is
            // unchanged. Worst penetration is still ~5.0 m, in the same range as the old 4.9 m,
            // because it is the same intrusion. If this number moves again, suspect a real building
            // before assuming another Place-count reshuffle.
            Assert.That(offenders.Count, Is.LessThanOrEqualTo(40),
                "More buildings stand in a street in the BUILT town than before. Either a pass "
                + "moved something into a road, or the corridor the world draws has changed "
                + "shape. Worst offender: " + (offenders.Count > 0 ? offenders[0] : "none"));
        }

        /// <summary>
        /// Every road the town drives on has at least two points and a width its class allows.
        /// A one-point road is not a road; a zero-width one is drawn as nothing and is invisible
        /// in every render, which is the quietest possible way to lose a street.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryRoadIsDrawable()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var world = CityUnderTest.World;

            foreach (var line in world.Roads.Lines)
            {
                Assert.That(line.Path, Is.Not.Null, $"{line.Name} has no path");
                Assert.That(line.Path.Length, Is.GreaterThan(0f),
                            $"{line.Name} has zero length - it is drawn as nothing, which is the "
                          + "quietest possible way to lose a street");
                Assert.That(line.Width, Is.GreaterThan(0), $"{line.Name} is zero wide");
            }

            Debug.Log($"[geometry] {world.Roads.Lines.Count} roads, all drawable");
        }

        /// <summary>
        /// 112 S Chicago (parcel 237) is ruled `footprint later` with a note naming several
        /// narrow shops and a restaurant. Before this feature it built as exactly one Place named
        /// "the Chicago Street terrace" - one name, one business, no matter how many trades the
        /// note said stood there. This asserts the row is now several independently-named Places.
        ///
        /// Reads through BusinessFromRulings.HandleOf rather than Place.Name directly: once the
        /// owner rules a storefront through the panel its Name becomes the business name, and a
        /// test matching on the handle text would start under-counting the moment that happens -
        /// which is the normal, intended outcome of ruling it, not a regression.
        /// </summary>
        [UnityTest]
        public IEnumerator ATerraceLotProducesMoreThanOneIndependentlyNamedStorefront()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var world = CityUnderTest.World;
            Assert.That(world, Is.Not.Null, "the town did not build");

            var units = new List<Place>();
            foreach (var place in world.AllPlaces)
            {
                var handle = BusinessFromRulings.HandleOf(place.Name);
                if (handle.StartsWith("112 S Chicago #")) units.Add(place);
            }

            Debug.Log($"[geometry] 112 S Chicago: {units.Count} independently-named storefront(s)");

            Assert.That(units.Count, Is.GreaterThan(1),
                "112 S Chicago is ruled footprint later with several narrow shops named in its "
              + "own note - it should not still be one Place wearing one name.");

            foreach (var u in units)
            {
                Assert.That(u.Bounds.W, Is.GreaterThan(0), $"{u.Name} has zero width");
                Assert.That(u.Bounds.H, Is.GreaterThan(0), $"{u.Name} has zero depth");
            }
        }

        /// <summary>
        /// The whole town is walkable as one piece.
        ///
        /// Two regions means somebody's front door opens onto ground nothing else can reach, and
        /// the people behind it will stand still all day for reasons no log explains. This has
        /// happened before, from a door left outside its own wall.
        /// </summary>
        /// <summary>
        /// THE DRIVEWAYS EMPTY WHEN THE TOWN GOES TO WORK, AND THAT IS THE WHOLE POINT.
        ///
        /// A parked car is content in this game because it can be found to have MOVED. Drawing
        /// 611 of them and never taking one away would be scenery.
        ///
        /// This asserts against the clock the city already has rather than moving it. VillageHost
        /// starts the simulation at **noon** and `DayPlan` sends the out-of-town workers off at
        /// 06:20 and brings them back at 17:10 - so at the moment every test in this run sees, the
        /// commuters ARE away and their cars are gone. Winding the clock forward to prove the
        /// evening would work once and then break everything after it: the city is built once and
        /// shared by every test in the run, and a clock cannot be wound back in a teardown.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCarsOfPeopleAtWorkAreNotOnTheirDrives()
        {
            yield return CityUnderTest.WaitUntilBuilt();

            var host = CityUnderTest.Host;
            Assert.That(host, Is.Not.Null);

            var drives = host.Driveways;
            Assert.That(drives, Is.Not.Null, "no driveways were built at all");
            Assert.That(drives.Count, Is.GreaterThan(0), "no cars were planned");

            // How many people are out of town right now, counted the same way CityDriveways
            // counts them - one absent resident takes one car off that house's drive.
            var away = new Dictionary<int, int>();
            var people = host.Sim.People.Citizens;
            for (int i = 0; i < people.Count; i++)
            {
                var who = people[i];
                if (!who.Home.IsValid) continue;
                if (host.Sim.CurrentBlock(new CitizenId(i)).What != Noir.Core.People.Activity.AwayFromTown)
                    continue;
                away[who.Home.Value] = (away.TryGetValue(who.Home.Value, out int n) ? n : 0) + 1;
            }

            int gone = drives.Count - drives.Standing;

            Debug.Log($"[driveways] {drives.Standing} of {drives.Count} cars on their drives at "
                    + $"{host.Sim.Clock.MinuteOfDay / 60:00}:{host.Sim.Clock.MinuteOfDay % 60:00}; "
                    + $"{gone} away, over {away.Count} houses with somebody out of town.");

            Assert.That(away.Count, Is.GreaterThan(0),
                        "nobody in this town works out of it at midday, which cannot be right - "
                      + "DayPlan sends the commuters out at 06:20");

            Assert.That(gone, Is.GreaterThan(0),
                        $"{away.Count} households have somebody out of town and not one car left "
                      + "the drive - Refresh is not running, or it is not reaching the cars");

            Assert.That(drives.Standing, Is.GreaterThan(0),
                        "every single car left, which means the whole town commutes and it does not");
        }

        /// <summary>
        /// WHAT THE PARKED CARS COST, WHICH IS THE FIRST THING NOBODY MEASURED.
        ///
        /// A polyperfect car is **11 MeshRenderers, 11 MeshFilters and 11 MeshColliders** across
        /// 12 GameObjects. Six hundred of them, created after `CityChunker.Bake` so they can be
        /// taken away when their owner drives off, is about **6,700 unbatched renderers and 6,700
        /// mesh colliders** - in a project that once fought the ground alone from 2,626 draw calls
        /// down to 319.
        ///
        /// The absence has to stay - it is the whole point of drawing them - so the fix is not to
        /// bake them back into the town but to make ONE car cost one renderer. This is the budget
        /// that says so.
        /// </summary>
        [UnityTest]
        public IEnumerator AParkedCarCostsOneRendererAndNoMeshCollider()
        {
            yield return CityUnderTest.WaitUntilBuilt();

            var drives = CityUnderTest.Host != null ? CityUnderTest.Host.Driveways : null;
            Assert.That(drives, Is.Not.Null, "no driveways were built");
            Assert.That(drives.Count, Is.GreaterThan(0));

            var root = drives.gameObject;
            int renderers = root.GetComponentsInChildren<Renderer>(true).Length;
            int colliders = root.GetComponentsInChildren<Collider>(true).Length;
            int meshCol = root.GetComponentsInChildren<MeshCollider>(true).Length;
            int objects = root.GetComponentsInChildren<Transform>(true).Length;

            float perCar = renderers / (float)drives.Count;

            Debug.Log($"[driveways] cost: {drives.Count} cars, {renderers} renderers "
                    + $"({perCar:0.00}/car), {colliders} colliders ({meshCol} of them MeshColliders), "
                    + $"{objects} transforms.");

            Assert.That(perCar, Is.LessThanOrEqualTo(2f),
                        $"each parked car is costing {perCar:0.0} renderers. These are built after "
                      + "the chunk bake so they can be taken away, which means nothing batches them "
                      + "- a car has to arrive as one mesh, not eleven.");

            Assert.That(meshCol, Is.EqualTo(0),
                        $"{meshCol} MeshColliders on parked cars. PhysX bakes every one of them and "
                      + "the player queries against them; a car needs at most one primitive box.");
        }

        [UnityTest]
        public IEnumerator TheTownIsOnePiece()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var world = CityUnderTest.World;

            var report = WorldValidator.Validate(world);
            var split = new List<string>();
            foreach (var e in report.Errors)
                if (e.Contains("separate from the main")) split.Add(e);

            Debug.Log($"[geometry] validator: {report.Errors.Count} error(s), "
                    + $"{report.Warnings.Count} warning(s)");
            foreach (var e in report.Errors) Debug.Log("[geometry]   ERROR " + e);

            // A RATCHET, NOT A ZERO, and the honesty matters more than the green tick. Rossville
            // has ONE walkable area cut off from the rest and it predates this test - it is not
            // known yet whether that is a door outside its own wall, a yard fully enclosed by
            // buildings, or ground the survey legitimately walled off. Asserting zero today would
            // mean deleting the test tomorrow; asserting "no worse" catches the thing that
            // actually matters, which is a change that cuts the town further.
            Assert.That(split.Count, Is.LessThanOrEqualTo(1),
                "The town has been cut into more pieces than it was. Somebody's front door now "
                + "opens onto ground nothing can reach, and the people behind it will stand still "
                + "all day with nothing in the log to say why.");
        }

        /// <summary>
        /// EVERY DOOR IN ROSSVILLE OPENS ONTO SOMETHING, AT ZERO.
        ///
        /// A gate rather than a ratchet, at the owner's instruction on 2026-08-09. This number was
        /// printed and never asserted, and across one night of road work it went 6 to 7 to 9 while
        /// every gate stayed green - which is what an unasserted number does.
        ///
        /// THIS CAN ONLY LIVE IN PLAYMODE. The fault needs the survey passes to exist at all:
        /// `city.txt` on its own has no unreachable door, and the nine appeared because
        /// `ClearOfRoads` shoved 175 buildings off road corridors and each door went with its
        /// building into a neighbour's back wall. `DoorsThatOpen` re-faces them, and this is what
        /// says it worked on the town the game actually builds.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryDoorOpensOntoSomething()
        {
            yield return CityUnderTest.WaitUntilBuilt();

            var report = WorldValidator.Validate(CityUnderTest.World);

            var doors = new List<string>();
            foreach (var e in report.Errors)
                if (e.Contains("door")) doors.Add(e);

            Debug.Log($"[geometry] {doors.Count} door(s) nobody can walk through");
            foreach (var d in doors) Debug.Log("[geometry]   DOOR " + d);

            Assert.That(doors, Is.Empty,
                "These doors cannot be walked through:\n  " + string.Join("\n  ", doors) + "\n\n" +
                "Each one is somebody who cannot leave his own house. DoorsThatOpen runs in "
                + "TownPipeline.Finish and moves a sealed door round to a wall with reachable "
                + "ground outside it, preferring the wall nearest a road - so a door still failing "
                + "here is either a building walled in on every side, which that pass counts and "
                + "reports, or a door the pass could not judge because it was moved after the "
                + "world was built.");
        }

        /// <summary>
        /// EVERY ROOF COVERING IS WIRED TO SOMETHING. A wiring assertion, deliberately, and not a
        /// threshold on a colour.
        ///
        /// The fault it guards is the one a shipped player had for months: `Roofing` handed
        /// `Make` `Color.white`, and white only shows when NO texture binds - which is exactly a
        /// build's situation, because `SurfaceTextures.ApplyPack` is entirely `#if UNITY_EDITOR`.
        /// So the editor looked correct and the product had pure white roofs, and no test could
        /// tell the difference because in the editor there was none.
        ///
        /// A material passes if it has a texture OR a colour that is not white. That is the whole
        /// claim: something other than "untouched" happened to it. Asserting a particular colour
        /// instead would pin the palette in a second place and contradict the covering table the
        /// moment somebody re-tints one - the number would have to be kept in step by hand, which
        /// is how the four road-geometry constants in this project came to disagree with the
        /// suite for months.
        ///
        /// It also catches the subtler regression: binding a pack set but leaving the base colour
        /// white on a covering whose whole shade IS the tint, which would take charcoal back to
        /// mid grey and brown-black back to brown.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryRoofCoveringIsWiredToATextureOrAColour()
        {
            yield return CityUnderTest.WaitUntilBuilt();

            var roofs = Materials3D.Roofs;
            Assert.That(roofs, Is.Not.Null.And.Length.GreaterThan(4),
                        "the roof palette should carry four coverings, the chimney brick and the wall");

            var bare = new List<string>();
            for (int i = 0; i < roofs.Length; i++)
            {
                var m = roofs[i];
                if (m == null) { bare.Add($"[{i}] is null"); continue; }

                bool hasMap = m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null;
                bool tinted = m.HasProperty("_BaseColor") && m.GetColor("_BaseColor") != Color.white;

                Debug.Log($"[roofs] {m.name}: map={(hasMap ? "yes" : "NO")} "
                        + $"colour={(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "none")}");

                if (!hasMap && !tinted) bare.Add($"[{i}] {m.name}");
            }

            Assert.That(bare, Is.Empty,
                "These roof materials have neither a texture nor a colour - they are white:\n  "
              + string.Join("\n  ", bare) + "\n\n"
              + "A white roof is what a shipped player got for months while the editor looked "
              + "right, because ApplyPack is editor-only and Roofing passed Color.white as the "
              + "fallback. Either the pack set stopped binding or somebody put the white back.");
        }

        /// <summary>
        /// NO SURFACE IN THE TOWN IS TEXTURED THROUGH A PINHOLE. The gate for the whole UVX
        /// series, and it can only exist now that the walls carry a real texture: a flat colour
        /// hides a degenerate UV completely.
        ///
        /// A gable end is a triangle at constant x. Mapped from above with `(x, -z)` — the roof's
        /// own projection, which it inherited — every point on it has the same U and its height
        /// never enters the mapping at all, so the whole wall is textured from a single line of
        /// the image and smears from eaves to ridge. `RoofBuilder.WallsInTheirOwnPlane` fixed
        /// that; this is what says so, and what will say so again the next time somebody adds a
        /// surface to the roof mesh and forgets.
        ///
        /// MEASURED AS TEXEL DENSITY, not as a UV area, because a UV area means nothing on its
        /// own — a small triangle SHOULD have a small one. What cannot happen is a triangle that
        /// covers square metres of wall out of a sliver of image, and that ratio is exactly what
        /// a person sees as a smear.
        ///
        /// IT WALKS THE BAKED NODES, AND THAT IS NOT INCIDENTAL. `CityChunker.Bake` combines the
        /// town into a handful of meshes and DESTROYS the renderers it consumed, so the obvious
        /// version of this test — walk the MeshFilters under the city root — finds an empty
        /// subtree and passes without looking at anything. A test that cannot fail is worse than
        /// no test, because it is believed. Both counts below are asserted for that reason.
        /// </summary>
        [UnityTest]
        public IEnumerator NoTriangleInTheTownIsTexturedThroughAPinhole()
        {
            yield return CityUnderTest.WaitUntilBuilt();

            var filters = new List<MeshFilter>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                for (var t = mf.transform; t != null; t = t.parent)
                    if (t.name == "Baked") { filters.Add(mf); break; }

            Debug.Log($"[uv] {filters.Count} baked meshes to walk");
            Assert.That(filters, Is.Not.Empty,
                "no baked meshes were found, so this looked at nothing. CityChunker.Bake parents "
              + "what it builds under a node called \"Baked\"; if that name changed, this test "
              + "has been passing vacuously ever since.");

            var worst = new List<string>();
            int triangles = 0;

            int bought = 0;

            foreach (var mf in filters)
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;

                // THE MESHES THIS PROJECT BUILDS, NOT THE ONES IT BOUGHT.
                //
                // Every material the pack ships is named `M_...`; nothing generated here is -
                // Grass, Road, WallBrick, RoofShingleGrey. That is the whole test: a degenerate
                // UV on a wall THIS code mapped is a bug with a fix, and a degenerate UV on a
                // bought road tile is the vendor's art, unfixable from here and not evidence of
                // anything.
                //
                // IT MATTERED THE FIRST TIME THE GATE RAN AGAINST THE BUILT TOWN. Until then
                // CityStreets never built headlessly, so this walked only generated meshes and
                // found two. With NOIR_BUILT_TOWN=1 the pack's own road kit arrives and brings
                // dozens of zero-area UVs on `M_Universal_A` and `M_Universal_Glass` - and the
                // honest reading of that is not "the town got worse", it is "the test met a
                // population it had never seen". Loosening the threshold to swallow them would
                // have blinded it to the thing it exists for.
                var renderer = mf.GetComponent<MeshRenderer>();
                var material = renderer != null ? renderer.sharedMaterial : null;
                if (material != null && material.name.StartsWith("M_", System.StringComparison.Ordinal))
                { bought++; continue; }

                var verts = mesh.vertices;
                var uvs = mesh.uv;
                if (uvs == null || uvs.Length != verts.Length) continue;

                var tris = mesh.triangles;
                float worstRatio = 0f;
                int worstAt = -1;

                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];

                    float world = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).magnitude * 0.5f;
                    if (world < 1f) continue;              // a square metre, or it is trim

                    var u1 = uvs[b] - uvs[a];
                    var u2 = uvs[c] - uvs[a];
                    float uv = Mathf.Abs(u1.x * u2.y - u1.y * u2.x) * 0.5f;

                    triangles++;

                    float ratio = uv < 1e-7f ? float.MaxValue : world / uv;
                    if (ratio > worstRatio) { worstRatio = ratio; worstAt = i / 3; }
                }

                // 400 m2 of surface per unit of UV area is twenty metres of wall across one
                // repeat - already stretched, and nowhere near the degenerate case this hunts.
                if (worstRatio > 400f)
                {
                    // THE MATERIAL, because "Chunk_5_-2_0 triangle 1568" names nothing anybody
                    // can go and look at. A chunk is whatever the bake grouped by material, so
                    // the material is the only handle back to the code that built it.
                    var mr = mf.GetComponent<MeshRenderer>();
                    string what = mr != null && mr.sharedMaterial != null
                        ? mr.sharedMaterial.name : "no material";

                    worst.Add($"{mf.name} [{what}] triangle {worstAt}: "
                            + (worstRatio == float.MaxValue
                                ? "UV area is ZERO - textured from a single line of the image"
                                : $"{worstRatio:0} m2 of surface per unit of UV area"));
                }
            }

            Debug.Log($"[uv] {triangles} triangles of a square metre or more checked, "
                    + $"{worst.Count} stretched past the limit; {bought} bought mesh(es) skipped "
                    + "as the pack's own art");

            Assert.That(triangles, Is.GreaterThan(1000),
                "hardly any triangles were checked - the test proved nothing");

            // A RATCHET AT ZERO, AND IT CAME DOWN FROM TWO BY ANSWERING THE QUESTION.
            //
            // This test earned its keep on its first run: of 138,780 triangles a square metre or
            // larger, exactly two came back at 32,281 m2 per unit of UV area against a limit of
            // 400. They were left standing and NAMED rather than tuned away, with a note saying
            // two out of 138,780 is not the gable smear this was written for and wants a fresh
            // session rather than a loosened threshold.
            //
            // THE ANSWER IS THAT THEY WERE BOUGHT ART. Once the walk learned to skip the pack's
            // own materials - see the `M_` test above, which the built town forced - the count
            // went to ZERO across 138,790 triangles with six bought meshes skipped. Nothing this
            // project generates is mapped through a pinhole. The ratchet comes DOWN, which is the
            // only direction it is ever allowed to move.
            Assert.That(worst.Count, Is.EqualTo(0),
                "These surfaces are textured through a pinhole:\n  " + string.Join("\n  ", worst)
              + "\n\nA triangle covering square metres out of a sliver of image is what a person "
              + "sees as a smear. The classic case is a vertical wall carrying the roof's own "
              + "top-down (x, -z) mapping, where height never enters the UV - see "
              + "RoofBuilder.WallsInTheirOwnPlane.");
        }

        /// <summary>
        /// THE ROOFS OF THE TOWN THAT IS RUNNING, not the roofs the source file describes.
        ///
        /// The Core suite can read Materials3D.cs as text and prove what it SAYS. Nothing proved
        /// what the built town HAS: that the shader resolved rather than falling back to magenta,
        /// that a texture actually bound, and that the roll over the real six hundred buildings
        /// produces the mix the owner ruled. All three are free here - Materials3D.Roofs is a
        /// public static array and this assembly already references Noir.Unity.
        ///
        /// It PRINTS the census and ASSERTS only the rulings. A palette the owner changes his mind
        /// about should cost one number in Materials3D, not a rewritten test; a roof called thatch
        /// in Vermilion County in 1991 should cost a red.
        /// </summary>
        [UnityTest]
        public IEnumerator TheRoofsOfRossvilleAreShingle()
        {
            yield return CityUnderTest.WaitUntilBuilt();

            var roofs = Materials3D.Roofs;
            foreach (var m in roofs)
            {
                var map = m.GetTexture("_BaseMap");
                Debug.Log($"[surfaces] {m.name}  shader={m.shader.name}  "
                        + $"map={(map == null ? "(none)" : map.name)}  "
                        + $"base=#{ColorUtility.ToHtmlStringRGB(m.GetColor("_BaseColor"))}");
            }

            // THE CENSUS OVER THE REAL TOWN. RoofingFor is integer hashing on the building's own
            // key - no IRng, no reshuffle - so reading it here cannot move a single roof.
            var tally = new int[roofs.Length];
            int roofed = 0;
            foreach (var place in CityUnderTest.World.AllPlaces)
            {
                if (!PlaceKindTable.Current.Row(place.Kind).Roofed) continue;
                tally[Materials3D.RoofingFor(place)]++;
                roofed++;
            }
            for (int i = 0; i < tally.Length; i++)
                if (tally[i] > 0)
                    Debug.Log($"[surfaces] {roofs[i].name,-22} {tally[i],4}  "
                            + $"{(roofed == 0 ? 0f : 100f * tally[i] / roofed):F1}% of {roofed}");

            foreach (var m in roofs)
            {
                Assert.That(m.name.ToLowerInvariant(), Does.Not.Contain("thatch"),
                    "1991, Vermilion County, Illinois. The coverings are asphalt shingle and "
                  + "built-up tar and gravel. " + m.name + " is Ashcombe coming back.");
                Assert.That(m.name.ToLowerInvariant(), Does.Not.Contain("terracotta"),
                    m.name + " is a clay tile roof in east-central Illinois.");
                Assert.That(m.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"),
                    m.name + " is on " + m.shader.name + " - it fell back to the error shader or "
                  + "to Standard, which is the whole town's covering silently wrong.");
            }
        }

        /// <summary>
        /// THE PLAYER'S BODY IS REACHABLE THE WAY A SHIPPED BUILD REACHES IT.
        ///
        /// `Player.Spawn` used to be `#if UNITY_EDITOR` end to end, because it loads the armature
        /// through `AssetDatabase` - so PRESSING P IN A SHIPPED ROSSVILLE DID NOTHING AT ALL. No
        /// body, no error, no log, from the day it was written.
        ///
        /// It loads from `Resources` first now, in the editor as well as in a build, so the path a
        /// build takes is the path this suite walks every run rather than one nobody exercises.
        /// The asset is a prefab VARIANT of Starter Assets' armature, made by
        /// `Noir.Editor.PlayerArmatureResource` - **which has to be re-run after Starter Assets is
        /// re-imported**, and this test is how you find that out in ten minutes instead of after
        /// shipping.
        ///
        /// Asserted here rather than by pressing P, deliberately: a synthetic keypress into a
        /// built window proves nothing when it fails - it may be the key, the focus or the input
        /// system - whereas a missing asset is unambiguous.
        /// </summary>
        [UnityTest]
        public IEnumerator ThePlayersBodyIsWhereAShippedBuildWouldLookForIt()
        {
            yield return CityUnderTest.WaitUntilBuilt();

            var armature = Resources.Load<GameObject>("PlayerArmature");

            Assert.That(armature, Is.Not.Null,
                "Resources/PlayerArmature is missing, so a shipped build cannot stand a body up "
              + "and pressing P does nothing at all - silently, which is how it went unnoticed "
              + "from the day Player.Spawn was written. Run Noir > Make The Player Shippable. If "
              + "Starter Assets was just re-imported, that is why.");

            // A body, not an empty GameObject: the whole point is a rig with an animator on it.
            Assert.That(armature.GetComponentInChildren<Animator>(), Is.Not.Null,
                "Resources/PlayerArmature has no Animator anywhere under it, so it is not the "
              + "armature - somebody saved the wrong object into the variant.");

            Debug.Log($"[player] the shipped body is at Resources/PlayerArmature: {armature.name}");
        }

        /// <summary>
        /// THE DRAWN GROUND FOLLOWS THE MEASURED SURFACE. Everything in Rossville is placed on
        /// ElevationGrid.HeightAt - the bilinear blend of the USGS samples, which is a SADDLE
        /// inside every 30 m cell - while the ground mesher merges same-look tiles into runs
        /// drawn as two flat triangles. A flat triangle across a curved cell bows off the
        /// saddle. Measured 2026-08-18, the night the owner said "things sink into the ground,
        /// almost like there is a layer that sits on top": 917 cells bowed over 10 cm, 325 over
        /// 20 cm, 1.98 m in the creek ravine - and correctly-placed cars stood buried to their
        /// axles under ground that was drawn, not measured.
        ///
        /// The metric is immune to the deliberate flat lifts (water's channel, a road's wear):
        /// a planar triangle's centroid height is the mean of its vertex heights, so
        /// mean(HeightAt at the vertices) minus HeightAt at the centroid is exactly the bow,
        /// whatever constant the run's terrain adds. Risers are vertical and are skipped by the
        /// normal test; the countryside skirt is not the town and is skipped by bounds.
        /// </summary>
        [UnityTest]
        public IEnumerator TheDrawnGroundFollowsTheMeasuredSurface()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var host = CityUnderTest.Host;
            Assert.That(host, Is.Not.Null);

            float worst = 0f;
            Vector3 at = default;
            long sampled = 0;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (!mf.gameObject.name.StartsWith("Ground")) continue;
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var verts = mesh.vertices;
                var tris = mesh.triangles;
                var l2w = mf.transform.localToWorldMatrix;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = l2w.MultiplyPoint3x4(verts[tris[i]]);
                    Vector3 b = l2w.MultiplyPoint3x4(verts[tris[i + 1]]);
                    Vector3 c = l2w.MultiplyPoint3x4(verts[tris[i + 2]]);

                    Vector3 n = Vector3.Cross(b - a, c - a);
                    if (n.sqrMagnitude < 1e-10f) continue;
                    if (n.normalized.y < 0.7f) continue;   // a riser, not ground

                    Vector3 g = (a + b + c) / 3f;
                    if (g.x < 1f || g.x > host.World.Width - 1f
                        || -g.z < 1f || -g.z > host.World.Height - 1f) continue;   // the skirt

                    float bow = (ElevationGrid.HeightAt(a.x, -a.z)
                               + ElevationGrid.HeightAt(b.x, -b.z)
                               + ElevationGrid.HeightAt(c.x, -c.z)) / 3f
                              - ElevationGrid.HeightAt(g.x, -g.z);
                    sampled++;
                    if (Mathf.Abs(bow) > Mathf.Abs(worst)) { worst = bow; at = g; }
                }
            }
            Debug.Log($"[ground] {sampled:N0} horizontal ground triangles in the town; "
                    + $"worst bow off the measured surface {worst:0.000} m at ({at.x:0},{-at.z:0})");

            Assert.That(sampled, Is.GreaterThan(10000),
                "the ground scan found almost nothing - the chunk naming moved out from under it");
            Assert.That(Mathf.Abs(worst), Is.LessThan(0.05f),
                "the drawn ground bows off the surface everything is placed on - things standing "
              + "there sink into it or float above it");
        }

        /// <summary>The owner's hinges exist and none lost its leaf to the bake - the exact
        /// fault the town-wide Leafless gate was built for, scoped to 408's four doors
        /// (front, rear, garage service, garage overhead panel).</summary>
        [UnityTest]
        public IEnumerator TheOwnersDoorsSurviveTheBake()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var host = CityUnderTest.Host;
            // The standing gate builds the SURVEY PLAN, which stands no owner models -
            // "anything this run does not build, it has not tested," the gate's own words.
            // This gate measures the dressed town: the live editor, or NOIR_BUILT_TOWN=1.
            if (Object.FindFirstObjectByType<OwnerModel>() == null)
                Assert.Ignore("no owner models in this town - the plan gate cannot see 408's doors");
            var doors = Object.FindFirstObjectByType<CityDoors>();
            Assert.That(doors, Is.Not.Null);

            Place home = null;
            foreach (var p in host.World.AllPlaces)
                if (p != null && p.Name == "408 Holmes Street") home = p;
            Assert.That(home, Is.Not.Null, "the survey lost 408 Holmes Street");

            var centre = Space3D.ToWorld(home.Door);
            int nearby = 0;
            for (int i = 0; i < doors.Count; i++)
            {
                var d = doors.PositionOf(i) - centre;
                if (d.x * d.x + d.z * d.z < 40f * 40f) nearby++;
            }
            Assert.That(nearby, Is.GreaterThanOrEqualTo(4),
                "408 should hinge front, rear, garage service and the overhead panel");
            Assert.That(doors.Leafless(), Is.EqualTo(0),
                "a hinge with no renderer is a door the bake ate");
        }

        /// <summary>408's rooms come from the owner's authored plan, not the generated
        /// interior grammar - and unlike the owner-door gates above, this needs no
        /// Assert.Ignore in the plan town: FloorPlans.For/SeatOnSurvey apply the authored
        /// plan to the GENERATED 408 there too, so the plan gate can see it just as the
        /// dressed town can. 673-0.json authors 11 rooms (3 bedrooms, 1 kitchen, plus
        /// hall/bath/living/dining/family/second bath); conversion may drop slivers and
        /// the door-blind guard may repair a wall, so this is a floor on count and kind,
        /// not an equality on either.</summary>
        [UnityTest]
        public IEnumerator TheOwnersFloorPlanIsTheHousesRealRooms()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var world = CityUnderTest.World;
            Assert.That(world, Is.Not.Null, "the town did not build");

            var place = world.AllPlaces.FirstOrDefault(p => p.Name == "408 Holmes Street");
            if (place == null) { Assert.Ignore("no 408 Holmes Street in this town"); yield break; }

            var rooms = world.AllRooms.Where(r => r.Building.Equals(place.Id)).ToList();

            Debug.Log($"[floorplans] 408 Holmes Street: {rooms.Count} room(s), "
                    + $"{rooms.Count(r => r.Kind == RoomKind.Bedroom)} bedroom(s), "
                    + $"{rooms.Count(r => r.Kind == RoomKind.Kitchen)} kitchen(s)");

            Assert.That(rooms.Count, Is.GreaterThanOrEqualTo(9),
                "408's rooms are not the authored plan's");
            Assert.That(rooms.Count(r => r.Kind == RoomKind.Bedroom), Is.GreaterThanOrEqualTo(3),
                "the authored plan names three bedrooms");
            Assert.That(rooms.Any(r => r.Kind == RoomKind.Kitchen),
                "the authored plan names a kitchen");
        }

        /// <summary>The front doorway is a hole a body fits through, and the floor inside is
        /// real: a capsule cast crosses the threshold untouched, and a ray straight down
        /// inside the hall lands on a collider.</summary>
        [UnityTest]
        public IEnumerator TheFrontDoorOfHolmesAdmitsThePlayer()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var host = CityUnderTest.Host;
            // Plan gate town stands no owner models; without 408's model this test would
            // drive the controller through the NEIGHBOUR's generated door and call it
            // green - measured doing exactly that, 2026-08-19. Skip rather than lie.
            if (Object.FindFirstObjectByType<OwnerModel>() == null)
                Assert.Ignore("no owner models in this town - the plan gate cannot walk 408's door");
            Place home = null;
            foreach (var p in host.World.AllPlaces)
                if (p != null && p.Name == "408 Holmes Street") home = p;
            Assert.That(home, Is.Not.Null);

            // THE REAL CONTROLLER WALKS THE DOOR, both ways - not a ray, not a crouched
            // capsule. The first version of this gate probed a knee-to-chest band and
            // PASSED while the full 1.8 m controller wedged under the door header (the
            // owner spent a night stuck on his own doorsill, 2026-08-19); a gate that
            // approves a duck-height door is worse than none. The front door is the
            // southernmost hinge on the lot (Holmes is south) - the survey's own door
            // tile sits on the rear, and trusting it walked the first probe into the
            // back wall.
            var doors = Object.FindFirstObjectByType<CityDoors>();
            Assert.That(doors, Is.Not.Null);
            var lotCentre = Space3D.ToWorld(new Tile(
                home.Bounds.X + home.Bounds.W / 2, home.Bounds.Y + home.Bounds.H / 2));
            int front = -1; float southmost = float.MaxValue;
            for (int i = 0; i < doors.Count; i++)
            {
                var d = doors.PositionOf(i) - lotCentre;
                if (d.x * d.x + d.z * d.z > 40f * 40f) continue;
                if (doors.PositionOf(i).z < southmost) { southmost = doors.PositionOf(i).z; front = i; }
            }
            Assert.That(front, Is.GreaterThanOrEqualTo(0), "no hinge on the lot at all");
            var hp = doors.PositionOf(front);

            var player = Object.FindFirstObjectByType<Player>();
            Assert.That(player, Is.Not.Null);
            bool wasWalking = player.Walking;
            if (!wasWalking) player.Toggle();
            for (int f = 0; f < 5; f++) yield return null;
            var body = GameObject.Find("PlayerArmature");
            Assert.That(body, Is.Not.Null, "the body never stood up");
            var cc = body.GetComponent<CharacterController>();
            try
            {
                cc.enabled = false;
                body.transform.position = new Vector3(hp.x + 0.35f, hp.y + 1.2f, hp.z + 1.5f);
                cc.enabled = true;
                for (int i = 0; i < 240; i++) cc.Move(new Vector3(0f, -0.03f, -0.025f));
                Assert.That(body.transform.position.z, Is.LessThan(hp.z - 1.4f),
                    "the controller stalled walking OUT at z=" + body.transform.position.z.ToString("F2"));
                for (int i = 0; i < 240; i++) cc.Move(new Vector3(0f, -0.03f, +0.025f));
                Assert.That(body.transform.position.z, Is.GreaterThan(hp.z + 1.0f),
                    "the controller stalled walking back IN at z=" + body.transform.position.z.ToString("F2"));
            }
            finally
            {
                if (player.Walking && !wasWalking) player.Toggle();
            }
        }

        /// <summary>P stands you at 408's front walk when the address exists - within a few
        /// strides of the door, not out on Route 1.</summary>
        [UnityTest]
        public IEnumerator ThePlayerSpawnsAtHolmesFrontDoor()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var host = CityUnderTest.Host;
            var player = Object.FindFirstObjectByType<Player>();
            Assert.That(player, Is.Not.Null);

            Place home = null;
            foreach (var p in host.World.AllPlaces)
                if (p != null && p.Name == "408 Holmes Street") home = p;
            Assert.That(home, Is.Not.Null);

            // A FRESH SPAWN, UNCONDITIONALLY: the body keeps its position across toggles,
            // so any earlier test's parking spot becomes this test's measurement - 25.9m
            // of response-scene parking on the first run, 531m of Route 1 on the second,
            // because the destroy was guarded on not-Walking and a suite can leave the
            // player either way. Step out if needed, destroy whatever body exists, spawn
            // fresh: Player.Spawn only runs Standing() when there is no body at all.
            bool wasWalking = player.Walking;
            if (player.Walking) { player.Toggle(); yield return null; }
            var stale = GameObject.Find("PlayerArmature");
            if (stale != null) Object.DestroyImmediate(stale);
            player.Toggle();
            for (int f = 0; f < 5; f++) yield return null;
            try
            {
                var at = player.Where;
                Assert.That(at.HasValue, "the body never stood up");
                // The front walk: the lot's south edge, centred - Player.Standing's own
                // arithmetic, because the survey's door tile is on the rear side.
                var b = home.Bounds;
                var walk = Space3D.ToWorld(new Tile(b.X + b.W / 2, b.Y + b.H - 1));
                var d = at.Value - new Vector3(walk.x, walk.y, walk.z - 3f);
                Assert.That(new Vector2(d.x, d.z).magnitude, Is.LessThan(6f),
                    "P put the player " + d.magnitude.ToString("0.0") + "m from his own front walk");
            }
            finally
            {
                if (player.Walking && !wasWalking) player.Toggle();
            }
        }
    }
}
