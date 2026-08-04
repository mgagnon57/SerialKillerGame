using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// A ROAD RUNS ON PUBLIC LAND. THIS IS THE TEST THAT MAKES THAT A RULE.
    ///
    /// `docs/SOURCES-OF-TRUTH.md` says the county parcels are the authority for where public land
    /// is. That ruling already existed, scattered, in `docs/research/SOURCING.md` - which even
    /// recorded the correct method:
    ///
    ///     "Route 1 alignment ... Verified against county parcels - 0 of 112 sample points fall
    ///      inside a lot."
    ///
    /// It was run against ONE road. Chicago Street is, to this day, the one mainroad that sits on
    /// its own right of way. The other forty never got the check, and 17 of 22 streets and 13 of 15
    /// alleys are laid off theirs by three to twenty-five metres. Two days went into rediscovering
    /// a rule that was already written down, because prose does not fail a build.
    ///
    /// THE KNOWN-BAD SETS BELOW ARE THE CURRENT STATE, NOT THE TARGET. They are asserted exactly, so
    /// that a REGRESSION and a FIX both fail this test. A fix failing is the point: it forces
    /// SOURCES-OF-TRUTH.md to be updated in the same commit as the thing it describes.
    ///
    /// **They are not allowed to grow.**
    /// </summary>
    [TestFixture]
    public class RoadsSitOnPublicLandTests
    {
        // ---- the roads that are currently laid OFF their own right of way -------------------
        //
        // Measured 2026-08-04. Each of these has a parcel-free strip of the right width nearby -
        // 20 m for a street, 4-6 m for an alley - and sits somewhere else. See
        // docs/research/road-parcel-strips.txt for the offset of each.
        private static readonly string[] KnownOffTheirRightOfWay =
        {
            "attica", "harrison", "church", "summit", "grove", "goodwine",
            "green", "benton", "holmes", "gilbert", "earlcourt", "railroad",
            "alley1", "alley2", "alley3", "alley4", "alley5", "alley6", "alley7", "alley8",
            "alley9", "alley10", "alley12", "alley13", "alley14", "alley15",
        };

        /// <summary>
        /// Roads whose SURFACE covers a building footprint. The unambiguous check - a house is a
        /// house, where a tax parcel is a tax boundary.
        ///
        /// `chicago` is here because its corridor is 30 m, which is 98 ft of surface for a road
        /// that is two lanes and a shoulder. Narrowing it to 14 m takes it from 415 offending
        /// samples to 126, and fails nineteen road tests that bake the 30 m width into their
        /// assertions rather than just their fixtures. That is a scoped job - see docs/IDEAS.md.
        /// </summary>
        private static readonly string[] KnownRunningThroughBuildings = { "chicago", "railroad" };

        [Test]
        public void EveryRoadSitsOnPublicLandExceptTheOnesWeKnowAbout()
        {
            var parcels = Parcels();
            var world = City();

            var off = new List<string>();
            foreach (var line in world.Roads.Lines)
            {
                // TRACKS ARE EXCLUDED, AND NOT AS A FUDGE. The section roads and crossroads run
                // through open country, and the county parcels tile farmland - a point in the
                // middle of a cornfield is inside a lot because every acre belongs to somebody.
                // That is the trap recorded in osm-tiger-data-is-not-a-survey. This assertion is
                // about the PLATTED TOWN, where a right of way is a real reserved strip.
                if (line.Class == RoadClass.Track) continue;

                int inside = 0, total = 0;
                for (float d = line.Path.Length * 0.15f; d < line.Path.Length * 0.9f;
                     d += Math.Max(4f, line.Path.Length * 0.05f))
                {
                    var c = line.Path.PointAt(d);
                    total++;
                    if (InAnyParcel(parcels, c.X, c.Y)) inside++;
                }
                // A third of the run inside private lots is not a rounding error or a corner cut
                // at a junction - it is a road in the wrong place.
                if (total > 0 && inside / (float)total > 0.33f) off.Add(line.Name);
            }

            off.Sort(StringComparer.Ordinal);
            var expected = KnownOffTheirRightOfWay.OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Assert.That(off, Is.EqualTo(expected),
                "The set of roads laid off their own right of way has CHANGED.\n\n"
              + "If it grew, a road was moved onto private land - fix it.\n"
              + "If it shrank, a road was fixed - update this list AND docs/SOURCES-OF-TRUTH.md in\n"
              + "the same commit, which is the whole reason this test asserts an exact set.\n\n"
              + "now: " + string.Join(", ", off) + "\n"
              + "was: " + string.Join(", ", expected));
        }

        [Test]
        public void NoRoadRunsThroughABuildingExceptTheOnesWeKnowAbout()
        {
            var world = City();
            var kinds = PlaceKindTable.Current;
            var buildings = world.AllPlaces.Where(p => kinds.Row(p.Kind).IsBuilding)
                                           .Select(p => p.Bounds).ToList();
            Assert.That(buildings, Is.Not.Empty, "the map has buildings on it");

            var offenders = new List<string>();
            foreach (var line in world.Roads.Lines)
            {
                float half = RoadClasses.CorridorWidth(line.Class) * 0.5f;
                for (float d = 0f; d < line.Path.Length; d += 2f)
                {
                    var c = line.Path.PointAt(d);
                    bool hit = buildings.Any(r => c.X >= r.X - half && c.X <= r.X + r.W + half
                                              && c.Y >= r.Y - half && c.Y <= r.Y + r.H + half);
                    if (hit) { offenders.Add(line.Name); break; }
                }
            }

            offenders.Sort(StringComparer.Ordinal);
            var expected = KnownRunningThroughBuildings.OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Assert.That(offenders, Is.EqualTo(expected),
                "Roads whose carriageway covers a building have CHANGED.\n"
              + "now: " + string.Join(", ", offenders) + "\n"
              + "was: " + string.Join(", ", expected));
        }

        [Test]
        public void NoAlleyCrossesALotWithAHouseOnIt()
        {
            // The owner's standing fact, made assertable: "I have never seen a town where the alley
            // runs right through their back yard." SOURCES-OF-TRUTH.md section 3, fact 2.
            //
            // This one has NO known-bad list. Five alleys were moved off houses on 2026-08-03 and
            // the count went 162 samples to 0. It stays at 0.
            var world = City();
            var kinds = PlaceKindTable.Current;
            var buildings = world.AllPlaces.Where(p => kinds.Row(p.Kind).IsBuilding).ToList();

            float half = RoadClasses.CorridorWidth(RoadClass.Alley) * 0.5f;
            var through = new List<string>();

            foreach (var line in world.Roads.Lines.Where(l => l.Class == RoadClass.Alley))
                for (float d = 0f; d < line.Path.Length; d += 2f)
                {
                    var c = line.Path.PointAt(d);
                    var hit = buildings.FirstOrDefault(
                        p => c.X >= p.Bounds.X - half && c.X <= p.Bounds.X + p.Bounds.W + half
                          && c.Y >= p.Bounds.Y - half && c.Y <= p.Bounds.Y + p.Bounds.H + half);
                    if (hit != null) { through.Add($"{line.Name} through '{hit.Name}'"); break; }
                }

            Assert.That(through, Is.Empty,
                "An alley is laid over a building. An alley runs along the BACK LOT LINE, behind the\n"
              + "houses - it does not cross a lot and it never crosses a house.\n  "
              + string.Join("\n  ", through));
        }

        [Test]
        public void ChicagoStreetIsStillOnItsRightOfWayAndStillCurves()
        {
            // The one road that was ever checked, and the one that is right. Two claims, because
            // the road has been wrongly straightened once already from OSM tags.
            var parcels = Parcels();
            var world = City();
            var chicago = world.Roads.Lines.Single(l => l.Name == "chicago");

            int inside = 0, total = 0;
            for (float d = 0f; d < chicago.Path.Length; d += 8f)
            {
                var c = chicago.Path.PointAt(d);
                total++;
                if (InAnyParcel(parcels, c.X, c.Y)) inside++;
            }
            Assert.That(inside / (float)total, Is.LessThan(0.25f),
                        $"Chicago Street left its right of way: {inside} of {total} samples inside a lot");

            // And it bends. A straight Route 1 erases the reason the town is where it is.
            var a = chicago.Path.PointAt(0f);
            var z = chicago.Path.PointAt(chicago.Path.Length);
            var mid = chicago.Path.PointAt(chicago.Path.Length * 0.5f);
            float t = (mid.Y - a.Y) / (z.Y - a.Y);
            float straightX = a.X + t * (z.X - a.X);
            Assert.That(Math.Abs(mid.X - straightX), Is.GreaterThan(15f),
                        "Chicago Street has been straightened. It is the 1829 Hubbard Trail and the "
                      + "town was platted square around a path that was already there.");
        }

        // ---- reading the two files -----------------------------------------------------------

        private static WorldModel City()
        {
            if (!PlaceKindTable.IsInstalled)
                PlaceKindTable.Install(PlaceKindTable.Parse(TestContent.ReadRaw("kinds.txt")));
            return WorldBuilder.Build(VillageParser.Parse(TestContent.ReadRaw("city.txt")));
        }

        /// <summary>One closed ring of x,y points per line. See the header of parcels.txt.</summary>
        private static List<float[]> Parcels()
        {
            var rings = new List<float[]>();
            foreach (string raw in TestContent.ReadRaw("parcels.txt").Split('\n'))
            {
                string s = raw.Trim();
                if (s.Length == 0 || s[0] == '#') continue;

                var pts = new List<float>();
                foreach (string pair in s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int comma = pair.IndexOf(',');
                    if (comma <= 0) continue;
                    if (float.TryParse(pair.Substring(0, comma), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float x)
                     && float.TryParse(pair.Substring(comma + 1), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float y))
                    { pts.Add(x); pts.Add(y); }
                }
                if (pts.Count >= 8) rings.Add(pts.ToArray());
            }
            Assert.That(rings.Count, Is.GreaterThan(700), "parcels.txt should hold 794 lots");
            return rings;
        }

        private static bool InAnyParcel(List<float[]> rings, float px, float py)
        {
            foreach (var r in rings)
            {
                // Cheap reject on the ring's own box before the crossing count.
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < r.Length; i += 2)
                {
                    if (r[i] < minX) minX = r[i];
                    if (r[i] > maxX) maxX = r[i];
                    if (r[i + 1] < minY) minY = r[i + 1];
                    if (r[i + 1] > maxY) maxY = r[i + 1];
                }
                if (px < minX || px > maxX || py < minY || py > maxY) continue;

                bool inside = false;
                for (int i = 0, j = r.Length - 2; i < r.Length; j = i, i += 2)
                {
                    float xi = r[i], yi = r[i + 1], xj = r[j], yj = r[j + 1];
                    if (yi > py != yj > py &&
                        px < (xj - xi) * (py - yi) / (yj - yi) + xi) inside = !inside;
                }
                if (inside) return true;
            }
            return false;
        }
    }
}
