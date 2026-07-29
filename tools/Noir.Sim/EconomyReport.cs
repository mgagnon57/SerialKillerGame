using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// Does the settlement close as a system.
    ///
    /// `check` proves the map is walkable and `strand` proves everybody can get where they are
    /// going. Neither asks whether there is anywhere worth going: a town can be perfectly
    /// connected, never strand a soul, and still have four hundred adults and thirty jobs, a
    /// school with room for a fifth of its catchment, and a shop open only while everybody who
    /// might use it is at work. None of those throw. All of them are content faults, and all of
    /// them get much easier to make at six hundred than at a hundred and nine, because at a
    /// hundred and nine one person can hold the whole settlement in their head.
    ///
    /// Four books that have to balance: work, school, food, and hours against who is free.
    /// Where a bar is somebody's judgement rather than a measurement it is a flag with a
    /// default, and it says so in the output.
    /// </summary>
    public static class EconomyReport
    {
        /// <summary>
        /// A sane walk to the shop, in metres, one way. One tile is one metre — village.txt.
        ///
        /// Eight hundred is ten minutes at an adult's 1.35 m/s, which is about as far as anybody
        /// carries a bag of shopping before the village has failed them. A POLICY: move it with
        /// --walk and the worst-case column will tell you what the move costs.
        /// </summary>
        public const int DefaultWalkMetres = 800;

        /// <summary>
        /// Children one teacher can hold. DERIVED, and the weakest number in this command: a
        /// Place has a footprint and a job count and no capacity field at all, so a school's
        /// roll has to be inferred from its staff. Thirty is a 1979 rural primary class, and
        /// village.txt's own Ashcombe Primary says "forty-one children, two classrooms".
        /// </summary>
        public const int DefaultPupilsPerTeacher = 30;

        /// <summary>Below this share of the working population able to reach a place while it is
        /// open, the hours are a warning. Zero is a failure. A POLICY.</summary>
        public const int DefaultReachablePercent = 33;

        /// <summary>An adult's walking pace before terrain, in tiles a second. Citizen.BaseSpeed
        /// for an adult of average Pace; children and elders are slower and are not what the
        /// walk-to-the-shop figure is about.</summary>
        private const float AdultPace = 1.35f;

        private const int MinuteWords = (1440 + 63) / 64;

        public sealed class Outcome
        {
            public string Text;
            public bool Failed;
        }

        public static Outcome Run(VillageContext ctx, int walkMetres, int pupilsPerTeacher)
        {
            if (walkMetres <= 0) walkMetres = DefaultWalkMetres;
            if (pupilsPerTeacher <= 0) pupilsPerTeacher = DefaultPupilsPerTeacher;

            var world = ctx.World;
            var people = ctx.People;
            var r = new Instrument();
            var verdicts = new List<(bool fail, string text)>();

            r.Line($"economy — {world.Name}, {people.Count} people, {people.HouseholdCount} "
                 + $"households, {world.Width}x{world.Height}, seed {ctx.Seed}");
            r.Line("          Does the settlement close: is there a job for everyone who wants one, a");
            r.Line("          desk for every child, food within a walk, and anybody free to buy it.");
            r.Line();
            r.Line($"          bars, which are judgements and not measurements:");
            r.Line($"            walk to food        {walkMetres} m one way, walked, not crow-flies");
            r.Line($"            school capacity     {pupilsPerTeacher} children per teacher (DERIVED — a Place has");
            r.Line("                                no capacity, only staff and a footprint)");
            r.Line($"            hours reachable     {DefaultReachablePercent}% of the working population, or it is a warning");
            r.Line();
            r.Line($"          COST — {people.Count * 7:#,0} day plans and roughly "
                 + $"{people.HouseholdCount * world.PlacesOfKind(PlaceKind.Shop).Count + people.CountOfStage(LifeStage.Child):#,0} "
                 + "A* queries.");
            r.Line("          No simulation is ticked: every figure below comes from the world, the");
            r.Line("          population and DayPlanner, all of which are pure functions of the seed.");
            r.Line();

            Work(r, ctx, verdicts);
            School(r, ctx, pupilsPerTeacher, verdicts);
            Food(r, ctx, walkMetres, verdicts);
            Hours(r, ctx, verdicts);

            r.Heading("DOES IT CLOSE");
            bool failed = false;
            foreach (var (fail, text) in verdicts)
            {
                if (fail) failed = true;
                r.Line($"    {(fail ? "FAIL" : "ok  ")}  {text}");
            }
            r.Line();
            r.Line(failed
                ? "    The settlement does NOT close. Every FAIL above is a content fault that no"
                : $"    The settlement closes — which is a statement about {people.Count} people and");
            r.Line(failed
                ? "    build, test or strand run would have reported."
                : "    not about six hundred. `--tile 2` and `--tile 3` run the same content at four");
            if (!failed)
                r.Line("    and nine times the size, which is the cheapest preview of the town there is.");
            r.Line();
            r.M("econ_verdict", ("closes", failed ? 0 : 1), ("checks", verdicts.Count));

            return new Outcome { Text = r.ToString(), Failed = failed };
        }

        // ------------------------------------------------------------------
        //  1. jobs against working-age population
        // ------------------------------------------------------------------

        private static void Work(Instrument r, VillageContext ctx, List<(bool, string)> verdicts)
        {
            var world = ctx.World;
            var people = ctx.People;

            var table = new Table("WORKPLACES — every place with a job in it",
                new Col("place", 22, right: false), new Col("kind", 12, right: false),
                new Col("slots", 7), new Col("staffed", 8), new Col("vacant", 7),
                new Col("hours/wk", 9), new Col("", 20, right: false));

            int slots = 0, staffed = 0, vacant = 0, understaffed = 0;

            foreach (var place in world.AllPlaces)
            {
                if (place.JobSlots <= 0) continue;

                int here = people.WorkersAt(place.Id).Count;
                int gap = place.JobSlots - here;
                slots += place.JobSlots;
                staffed += here;
                if (gap > 0) { vacant += gap; understaffed++; }

                table.Row(place.Name, place.Kind, place.JobSlots, here, gap > 0 ? gap : 0,
                          OpenMinutesPerWeek(place) / 60.0,
                          gap > 0 ? "SHORT " + gap : "");
            }

            int adults = people.CountOfStage(LifeStage.Adult);
            int employed = people.WorkingCount;
            int idle = adults - employed;

            table.Note($"{slots} slots, {staffed} staffed, {vacant} vacant. Working-age adults "
                     + $"{adults}, of whom {employed} work.");
            r.Add(table);

            r.Heading("WORK — the two sides of the same shortfall");
            r.Line($"    job slots in the settlement    {slots,6}");
            r.Line($"    working-age adults (21-64)     {adults,6}");
            r.Line($"    adults in work                 {employed,6}");
            r.Line($"    adults with nowhere to be      {idle,6}   "
                 + $"({(adults > 0 ? 100.0 * idle / adults : 0):0.0}% of working age)");
            r.Line($"    slots nobody took              {vacant,6}   in {understaffed} workplace(s)");
            r.Line();
            r.Line("    An idle adult is not automatically a fault — a 1979 village is full of people");
            r.Line("    keeping house or working away, and PopulationGenerator hands out only the jobs");
            r.Line("    the settlement actually has. A VACANCY standing next to an idle adult is a");
            r.Line("    fault, because the generator fills nearest-first until the slots run out: if");
            r.Line("    both numbers are above zero, somebody could not be matched to a job that");
            r.Line("    exists, and the usual reason is a workplace nothing can reach.");
            r.Line();

            r.M("econ_work", ("slots", slots), ("adults", adults), ("employed", employed),
                ("idle", idle), ("vacant", vacant), ("understaffed", understaffed));

            verdicts.Add((slots > adults,
                $"jobs against working age: {slots} slots for {adults} adults"
                + (slots > adults ? $" — {slots - adults} can never be staffed"
                                  : $", {adults - slots} adults spare")));

            verdicts.Add((vacant > 0 && idle > 0,
                $"no vacancy stands beside an idle adult: {vacant} vacant, {idle} idle"));
        }

        // ------------------------------------------------------------------
        //  2. every child in a school with room in it
        // ------------------------------------------------------------------

        private static void School(Instrument r, VillageContext ctx, int pupilsPerTeacher,
                                   List<(bool, string)> verdicts)
        {
            var world = ctx.World;
            var people = ctx.People;
            var schools = world.PlacesOfKind(PlaceKind.School);

            var roll = new Dictionary<int, int>();
            int children = 0, homeless = 0;
            var walks = new List<double>();
            int worstChild = -1;
            double worstWalk = -1, worstWalkMinutes = 0;

            var pathfinder = new Pathfinder(world.Grid);
            var buffer = new List<Tile>();

            for (int i = 0; i < people.Count; i++)
            {
                var c = people.Get(new CitizenId(i));
                if (!c.IsChild) continue;
                children++;

                var household = people.HouseholdOf(c);
                var school = household != null ? household.School : PlaceId.None;
                if (!school.IsValid) { homeless++; continue; }

                roll.TryGetValue(school.Value, out int had);
                roll[school.Value] = had + 1;

                double metres = Walk(world, pathfinder, buffer,
                                     AnchorOf(world.GetPlace(c.Home)),
                                     AnchorOf(world.GetPlace(school)), out double mins);
                if (metres < 0) continue;

                walks.Add(metres);
                if (metres > worstWalk) { worstWalk = metres; worstWalkMinutes = mins; worstChild = i; }
            }

            var table = new Table("SCHOOLS — the roll against the room",
                new Col("school", 22, right: false), new Col("teachers", 9), new Col("roll", 6),
                new Col("capacity", 9), new Col("spare", 7), new Col("m2/child", 9),
                new Col("", 14, right: false));

            bool over = false;
            foreach (var id in schools)
            {
                var place = world.GetPlace(id);
                roll.TryGetValue(id.Value, out int here);
                int capacity = place.JobSlots * pupilsPerTeacher;
                if (here > capacity) over = true;

                table.Row(place.Name, place.JobSlots, here, capacity, capacity - here,
                          here > 0 ? place.Bounds.Area / (double)here : 0,
                          here > capacity ? "OVER by " + (here - capacity) : "");
            }
            table.Note($"{children} children, {homeless} with no school at all. Capacity is DERIVED "
                     + $"from staff x {pupilsPerTeacher}.");
            table.Note("m2/child is the whole footprint including walls, so it is generous; a 1979");
            table.Note("classroom gave a child about 2 m2 and the rest of the building was corridor.");
            r.Add(table);

            if (walks.Count > 0)
            {
                var array = walks.ToArray();
                var spread = new Table("", Spread.Columns);
                Spread.Describe(spread, "walk to school, m", array);
                spread.Note("Walked on the real terrain, not crow-flies: the pathfinder's own route,");
                spread.Note("through the doors people actually use.");
                r.Add(spread);
                Spread.Machine(r, "econ_school_walk", "metres", array);

                if (worstChild >= 0)
                {
                    var who = people.Get(new CitizenId(worstChild));
                    r.Line($"    furthest from a gate   {who.FullName}, of "
                         + $"{Named(world.GetPlace(who.Home))} — {worstWalk:#,0} m, "
                         + $"{worstWalkMinutes:0.0} minutes each way");
                    r.Line();
                }
            }

            r.M("econ_school", ("children", children), ("no_school", homeless),
                ("schools", schools.Count), ("pupils_per_teacher", pupilsPerTeacher),
                ("over_capacity", over ? 1 : 0));

            verdicts.Add((homeless > 0,
                $"every child has a school: {children - homeless} of {children} in a catchment"));
            verdicts.Add((over,
                $"every school has room: {schools.Count} school(s) at {pupilsPerTeacher} per teacher"));
        }

        // ------------------------------------------------------------------
        //  3. food within a walk
        // ------------------------------------------------------------------

        private static void Food(Instrument r, VillageContext ctx, int walkMetres,
                                 List<(bool, string)> verdicts)
        {
            var world = ctx.World;
            var people = ctx.People;
            var shops = world.PlacesOfKind(PlaceKind.Shop);

            var pathfinder = new Pathfinder(world.Grid);
            var buffer = new List<Tile>();

            var metres = new List<double>();
            var minutes = new List<double>();
            int unreachable = 0, beyond = 0;
            int worstHousehold = -1;
            double worst = -1, worstMinutes = 0;
            PlaceId worstShop = PlaceId.None;

            for (int h = 0; h < people.HouseholdCount; h++)
            {
                var household = people.GetHousehold(new HouseholdId(h));
                var door = AnchorOf(world.GetPlace(household.Dwelling));

                double best = double.MaxValue, bestMinutes = 0;
                var bestShop = PlaceId.None;

                foreach (var id in shops)
                {
                    double m = Walk(world, pathfinder, buffer, door,
                                    AnchorOf(world.GetPlace(id)), out double mins);
                    if (m < 0 || m >= best) continue;
                    best = m;
                    bestMinutes = mins;
                    bestShop = id;
                }

                if (bestShop == PlaceId.None) { unreachable++; continue; }

                metres.Add(best);
                minutes.Add(bestMinutes);
                if (best > walkMetres) beyond++;
                if (best > worst)
                {
                    worst = best;
                    worstMinutes = bestMinutes;
                    worstHousehold = h;
                    worstShop = bestShop;
                }
            }

            r.Heading($"FOOD — every household's walk to the nearest of {shops.Count} shop(s)");

            if (metres.Count > 0)
            {
                var array = metres.ToArray();
                var spread = new Table("", Spread.Columns);
                Spread.Describe(spread, "walk to a shop, m", array);
                Spread.Describe(spread, "one way, minutes", minutes.ToArray());
                spread.Note($"Walked, at {AdultPace:0.00} m/s over the terrain the route crosses, "
                          + "ignoring the door pause.");
                spread.Note("Only PlaceKind.Shop counts as food. The Post Office sells stamps.");
                r.Add(spread);
                Spread.Machine(r, "econ_food", "metres", array);
                Spread.Machine(r, "econ_food", "minutes", minutes.ToArray());
            }

            r.Line($"    households with no reachable shop   {unreachable,6}");
            r.Line($"    beyond the {walkMetres} m bar                {beyond,6}");

            if (worstHousehold >= 0)
            {
                var household = people.GetHousehold(new HouseholdId(worstHousehold));
                var home = world.GetPlace(household.Dwelling);
                var names = new List<string>();
                foreach (var id in household.Members) names.Add(people.Get(id).FullName);

                r.Line();
                r.Line($"    worst walk    {worst:#,0} m, {worstMinutes:0.0} minutes each way");
                r.Line($"                  {household} at {Named(home)}, "
                     + $"to {Named(world.GetPlace(worstShop))}");
                r.Line($"                  {string.Join(", ", names)}");
            }
            r.Line();
            r.Line("    Straight-line distance is what PopulationGenerator and DayPlanner choose");
            r.Line("    between places with; this is what the person then walks. The gap between the");
            r.Line("    two is the cost of a decision made by crow-flies, and it grows with the map.");
            r.Line();

            r.M("econ_food_summary", ("shops", shops.Count), ("households", people.HouseholdCount),
                ("unreachable", unreachable), ("beyond_bar", beyond), ("bar_m", walkMetres),
                ("worst_m", worst < 0 ? 0 : worst));

            verdicts.Add((unreachable > 0,
                $"every household can reach a shop: {unreachable} cannot"));
            verdicts.Add((beyond > 0,
                $"every household is within {walkMetres} m of food: {beyond} of "
                + $"{people.HouseholdCount} are not"));
        }

        // ------------------------------------------------------------------
        //  4. opening hours against who is free
        // ------------------------------------------------------------------

        private static void Hours(Instrument r, VillageContext ctx, List<(bool, string)> verdicts)
        {
            var world = ctx.World;
            var people = ctx.People;
            int n = people.Count;

            // A week of everybody's day, as a bitmask of the minutes they are neither asleep,
            // at work, nor at school. Free is the honest word for it: somebody at home at ten in
            // the morning COULD go to the shop, and somebody on the mill floor could not.
            var free = new ulong[n * 7 * MinuteWords];
            var employed = new bool[n];

            for (int i = 0; i < n; i++)
            {
                var c = people.Get(new CitizenId(i));
                employed[i] = c.Works;

                for (int day = 0; day < 7; day++)
                {
                    var plan = DayPlanner.Plan(world, people, c, day, ctx.Seed);
                    int at = (i * 7 + day) * MinuteWords;

                    foreach (var block in plan.Blocks)
                    {
                        if (block.What == Activity.Asleep || block.What == Activity.AtWork
                            || block.What == Activity.AtSchool) continue;
                        SetRange(free, at, block.StartMinute, block.EndMinute);
                    }
                }
            }

            int workers = 0;
            for (int i = 0; i < n; i++) if (employed[i]) workers++;

            var table = new Table("OPENING HOURS — against who is actually free to walk in",
                new Col("place", 22, right: false), new Col("open h/wk", 10),
                new Col("any free", 9), new Col("workers", 8), new Col("never", 7),
                new Col("min/wk med", 11), new Col("", 12, right: false));

            var open = new ulong[7 * MinuteWords];
            int worstPlace = -1;
            double worstShare = 200;
            bool anyDead = false, anyThin = false;

            foreach (var place in world.AllPlaces)
            {
                if (place.AlwaysOpen) continue;

                Array.Clear(open, 0, open.Length);
                for (int day = 0; day < 7; day++)
                foreach (var window in place.Hours)
                {
                    if ((window.DaysMask & (1 << day)) == 0) continue;
                    SetRange(open, day * MinuteWords, window.StartMinute, window.EndMinute);
                }

                int openMinutes = 0;
                for (int w = 0; w < open.Length; w++) openMinutes += PopCount(open[w]);

                int canAny = 0, canWork = 0, neverWork = 0;
                var workerMinutes = new List<double>();

                for (int i = 0; i < n; i++)
                {
                    // Somebody who works here is not a customer, and counting them would let a
                    // shop that nobody can reach be rescued by its own shopkeeper.
                    var c = people.Get(new CitizenId(i));
                    if (c.Work == place.Id) continue;

                    int overlap = 0;
                    for (int day = 0; day < 7; day++)
                    for (int w = 0; w < MinuteWords; w++)
                        overlap += PopCount(free[(i * 7 + day) * MinuteWords + w]
                                          & open[day * MinuteWords + w]);

                    if (overlap > 0) canAny++;
                    if (!employed[i]) continue;

                    workerMinutes.Add(overlap);
                    if (overlap > 0) canWork++; else neverWork++;
                }

                int customers = n - people.WorkersAt(place.Id).Count;
                double shareAny = customers > 0 ? 100.0 * canAny / customers : 0;
                double shareWork = canWork + neverWork > 0
                    ? 100.0 * canWork / (canWork + neverWork) : 0;

                var sortedWorkers = workerMinutes.ToArray();
                Array.Sort(sortedWorkers);
                double median = Spread.Percentile(sortedWorkers, 50);

                string flag = canAny == 0 ? "NOBODY"
                            : shareWork < DefaultReachablePercent ? "thin" : "";
                if (canAny == 0) anyDead = true;
                else if (shareWork < DefaultReachablePercent) anyThin = true;

                if (shareWork < worstShare) { worstShare = shareWork; worstPlace = place.Id.Value; }

                table.Row(place.Name, openMinutes / 60.0, $"{shareAny:0}%", $"{shareWork:0}%",
                          neverWork, median, flag);

                r.M("econ_hours", ("place", place.Name.Replace(' ', '_')),
                    ("kind", place.Kind), ("open_h", openMinutes / 60.0),
                    ("any_pct", shareAny), ("worker_pct", shareWork), ("never_workers", neverWork),
                    ("median_min", median));
            }

            table.Note("'any free' is the share of everyone who is not staff there and has at least one");
            table.Note($"free minute while it is open. 'workers' is the same for the {workers} people who");
            table.Note("hold a job, which is the group hours can actually shut out. 'never' counts the");
            table.Note("workers with no free minute at all while the doors are open — a shop with a big");
            table.Note("number here is a shop the employed half of the settlement cannot use.");
            table.Note("Free means not asleep, not at work, not at school, read off DayPlanner over a");
            table.Note("whole week. Measured, not assumed: this is the schedules the sim actually runs.");
            r.Add(table);

            if (worstPlace >= 0)
                r.Line($"    hardest to reach if you have a job: "
                     + $"{Named(world.GetPlace(new PlaceId(worstPlace)))} at {worstShare:0}% "
                     + "of working adults");
            r.Line();

            verdicts.Add((anyDead, "every place with hours is open when somebody is free"));
            verdicts.Add((false, $"places the employed can rarely reach (under "
                               + $"{DefaultReachablePercent}%): {(anyThin ? "some — see 'thin' above" : "none")}"));
        }

        // ------------------------------------------------------------------
        //  plumbing
        // ------------------------------------------------------------------

        private static void SetRange(ulong[] bits, int at, int from, int to)
        {
            for (int m = from; m < to && m < 1440; m++)
                bits[at + (m >> 6)] |= 1UL << (m & 63);
        }

        private static int PopCount(ulong v)
        {
            int n = 0;
            while (v != 0) { v &= v - 1; n++; }
            return n;
        }

        private static int OpenMinutesPerWeek(Place place)
        {
            if (place.AlwaysOpen) return 1440 * 7;

            int total = 0;
            var day = new ulong[MinuteWords];
            for (int d = 0; d < 7; d++)
            {
                Array.Clear(day, 0, day.Length);
                foreach (var window in place.Hours)
                {
                    if ((window.DaysMask & (1 << d)) == 0) continue;
                    SetRange(day, 0, window.StartMinute, window.EndMinute);
                }
                foreach (ulong w in day) total += PopCount(w);
            }
            return total;
        }

        /// <summary>
        /// The route between two doors, in metres, and how long it takes.
        ///
        /// The pathfinder's own answer, not a straight line: a straight line across the Ash is
        /// not a walk anybody takes, and the whole point of this figure is the walk. Negative
        /// when no route came back at all, which is the same failure `strand` counts.
        /// </summary>
        private static double Walk(WorldModel world, Pathfinder pathfinder, List<Tile> buffer,
                                   Tile from, Tile to, out double minutes)
        {
            minutes = 0;
            if (!from.IsValid || !to.IsValid) return -1;
            if (from == to) return 0;

            buffer.Clear();
            if (pathfinder.FindPath(from, to, buffer) != PathOutcome.Found) return -1;

            double metres = 0, seconds = 0;
            var at = from;
            foreach (var step in buffer)
            {
                double leg = step.X != at.X && step.Y != at.Y ? 1.4142135 : 1.0;
                float cost = world.Grid.InBounds(at) ? world.Grid.MoveCost(at.X, at.Y) : 1.3f;
                if (cost > 100f) cost = 1.3f;

                metres += leg;
                seconds += leg * cost / AdultPace;
                at = step;
            }

            minutes = seconds / 60.0;
            return metres;
        }

        private static Tile AnchorOf(Place place)
        {
            if (place == null) return Tile.None;
            return place.Door.IsValid ? place.Door : place.Bounds.Centre;
        }

        private static string Named(Place place) => place == null ? "nowhere" : "'" + place.Name + "'";
    }
}
