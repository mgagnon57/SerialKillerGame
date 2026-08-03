using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Which of the town's three house layers stands on a given lot.
    ///
    /// THIS IS WHERE THE RESEARCH REACHES THE SCREEN. `Noir.Core.World.ResidentialLots` decides
    /// what is on a lot — built or vacant, how big, how tall, which decade, what is behind it —
    /// from figures measured off the 1913 Sanborn sheets and the county assessor's records. It is
    /// pure data and knows nothing about Unity. This file is the other half: it works out where a
    /// lot sits in the town, asks Core, and turns the answer into a massing grammar.
    ///
    /// WHY POSITION MATTERS. Rossville was platted in 1857 as four blocks at Chicago × Attica and
    /// everything since is an addition bolted onto the edge of what was already there, so how far
    /// out a lot is *is* most of what says when it was built. The old core is farmhouses and
    /// foursquares, a middle ring is 1920s–40s bungalows, and the postwar ranches are on the
    /// fringe. Scatter the vintages evenly and you get 1890s farmhouses on the edge of town and
    /// ranches on the oldest street, which is the loudest way to get a small town wrong.
    ///
    /// THE CENTRE IS DERIVED, NOT TYPED. It is the junction where the road named `chicago` meets
    /// the one named `attica` — the town's own origin, and the one fact the whole map is built
    /// about. CityShot used to carry `const float cx = 750f, cy = 1335f` and had to be re-typed by
    /// hand every time the town was re-laid; this asks the road network instead, so moving the
    /// town moves the age-rings with it.
    ///
    /// A village with no such crossing (Ashcombe) falls back to the centroid of its own dwellings,
    /// so this is safe to leave installed for both towns.
    /// </summary>
    public static class HouseLayers
    {
        private static readonly FarmhouseMassing Farmhouse = new FarmhouseMassing();
        private static readonly FoursquareMassing Foursquare = new FoursquareMassing();
        private static readonly BungalowMassing Bungalow = new BungalowMassing();
        private static readonly RanchMassing Ranch = new RanchMassing();

        /// <summary>The crossing the town grew out from, in map coordinates.</summary>
        public static Vector2 Centre { get; private set; }

        /// <summary>
        /// How far the houses reach from <see cref="Centre"/>. Edgeness is distance over this,
        /// so it is the denominator that turns a position into a decade.
        /// </summary>
        public static float Radius { get; private set; } = 1f;

        public static bool Installed { get; private set; }

        /// <summary>
        /// Work out the town's centre and extent from the world itself. Call once per build,
        /// before anything asks for a grammar.
        /// </summary>
        public static void Install(WorldModel world)
        {
            Installed = false;
            if (world == null) return;

            Centre = CrossingOf(world);

            // The radius is the furthest DWELLING, not the furthest anything: the map runs two
            // kilometres into cornfields, and measuring against those would squash the whole town
            // into the innermost tenth of the range and make every house a farmhouse.
            float furthest = 0f;
            int homes = 0;
            foreach (var place in world.AllPlaces)
            {
                if (place.Kind != PlaceKind.Dwelling) continue;
                homes++;

                float d = Distance(place, Centre);
                if (d > furthest) furthest = d;
            }

            Radius = furthest > 1f ? furthest : 1f;
            Installed = homes > 0;
        }

        /// <summary>
        /// Where the town's two founding roads cross. Falls back to the centroid of the dwellings
        /// when there is no such pair, which is what makes this safe for Ashcombe.
        /// </summary>
        private static Vector2 CrossingOf(WorldModel world)
        {
            var roads = world.Roads;
            if (roads != null)
            {
                foreach (var j in roads.Junctions)
                {
                    if (j.NorthSouth == null || j.EastWest == null) continue;

                    bool pair = (Named(j.NorthSouth, "chicago") && Named(j.EastWest, "attica"))
                             || (Named(j.NorthSouth, "attica") && Named(j.EastWest, "chicago"));

                    if (pair) return new Vector2(j.X, j.Y);
                }
            }

            double sx = 0, sy = 0;
            int n = 0;
            foreach (var place in world.AllPlaces)
            {
                if (place.Kind != PlaceKind.Dwelling) continue;
                sx += place.Bounds.X + place.Bounds.W * 0.5f;
                sy += place.Bounds.Y + place.Bounds.H * 0.5f;
                n++;
            }

            return n == 0 ? new Vector2(world.Width * 0.5f, world.Height * 0.5f)
                          : new Vector2((float)(sx / n), (float)(sy / n));
        }

        private static bool Named(RoadLine line, string name) =>
            line.Name != null && line.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase);

        private static float Distance(Place place, Vector2 from)
        {
            float x = place.Bounds.X + place.Bounds.W * 0.5f - from.x;
            float y = place.Bounds.Y + place.Bounds.H * 0.5f - from.y;
            return Mathf.Sqrt(x * x + y * y);
        }

        /// <summary>How far out this lot is, 0 at the crossing and 1 at the edge of town.</summary>
        public static float EdgenessOf(Place place)
        {
            if (place == null || !Installed) return 0.5f;
            return Mathf.Clamp01(Distance(place, Centre) / Radius);
        }

        /// <summary>
        /// What Core says stands on this lot.
        ///
        /// Keyed on <see cref="Place.Key"/> — the stable 64-bit name that exists precisely so
        /// that content is additive. Seeding off the place's index instead would mean inserting
        /// one house at the top of city.txt re-rolled the era of every house below it.
        /// </summary>
        public static Homestead Of(Place place)
        {
            if (place == null) return default;

            var rng = Xoshiro256ss.Substream(place.Key, "house-layer");
            return ResidentialLots.Occupy(EdgenessOf(place), rng);
        }

        /// <summary>
        /// The grammar for a dwelling: which of the four frame houses it is.
        ///
        /// Vacancy is NOT applied here. Core will call roughly one lot in six empty, which is the
        /// real figure and matters for a generator laying out its own lots — but city.txt's places
        /// are authored, each one a house somebody put there on purpose, and quietly refusing to
        /// build a sixth of them would delete authored content. Whether the town lays vacant lots
        /// is a question for whatever writes city.txt, not for the thing shaping what is on it.
        /// </summary>
        public static IMassingGrammar Grammar(Place place)
        {
            switch (Of(place).Era)
            {
                case HouseEra.Foursquare: return Foursquare;
                case HouseEra.Bungalow:   return Bungalow;
                case HouseEra.Ranch:      return Ranch;
                default:                  return Farmhouse;
            }
        }
    }
}
