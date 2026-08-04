using System;
using System.Collections.Generic;
using System.Text;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>Terminal views onto the village. This is how the simulation gets looked at
    /// before there is anything to look at.</summary>
    public static class Reports
    {
        /// <summary>
        /// The year this describes. These are reports about the village AS GENERATED, and a
        /// VillageContext has no clock in it - so the epoch, which is the year the population was
        /// drawn for. Anything wanting the town in 2003 needs a simulation, not a context.
        /// </summary>
        private const int Year = GameClock.EpochYear;

        public static string Households(VillageContext ctx)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{ctx.People.Count} people in {ctx.People.HouseholdCount} households");
            sb.AppendLine();

            foreach (var h in ctx.People.Households)
            {
                var dwelling = ctx.World.GetPlace(h.Dwelling);
                sb.AppendLine($"{dwelling.Name}  —  {h.Shape}");
                foreach (var id in h.Members)
                {
                    var c = ctx.People.Get(id);
                    string job = c.Job == Occupation.None
                        ? (c.IsChildIn(Year) ? "at school" : c.StageIn(Year) == LifeStage.Elder ? "retired" : "—")
                        : Occupations.NameOf(c.Job);
                    string work = c.Work.IsValid ? ctx.World.GetPlace(c.Work).Name : "";
                    sb.AppendLine($"    {c.FullName,-26} {c.AgeIn(Year),3}  {job,-12} {work}");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// One building's floor plan, as text. This is how house generation gets judged:
        /// you can tell at a glance whether a layout reads as a house or as random boxes.
        /// </summary>
        public static string House(VillageContext ctx, int index)
        {
            var dwellings = ctx.World.PlacesOfKind(PlaceKind.Dwelling);
            if (dwellings.Count == 0) return "no dwellings";
            if (index < 0 || index >= dwellings.Count) index = 0;
            return Plan(ctx, ctx.World.GetPlace(dwellings[index]));
        }

        /// <summary>
        /// The plan of any building at all, found by name.
        ///
        /// This command only ever knew about dwellings, which was fine while a house was the only
        /// thing with an interior. Now that a church has a nave and a cinema has an auditorium,
        /// the buildings worth looking at are exactly the ones it could not show.
        /// </summary>
        public static string Named(VillageContext ctx, string name)
        {
            Place best = null;
            foreach (var place in ctx.World.AllPlaces)
            {
                if (place.Name == null) continue;
                if (place.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                // Prefer the shortest match, so "mill" finds Ashcombe Mill and not Mill Buildings.
                if (best == null || place.Name.Length < best.Name.Length) best = place;
            }

            if (best == null) return $"no place whose name contains '{name}'";
            if (ctx.World.RoomsIn(best.Id).Count == 0)
                return $"{best.Name} has no interior - kinds.txt gives {best.Kind} 'rooms none'.";
            return Plan(ctx, best);
        }

        private static string Plan(VillageContext ctx, Place place)
        {
            var b = place.Bounds;
            var sb = new StringBuilder();

            sb.AppendLine($"{place.Name}   {b.W}x{b.H}   door at {place.Door}");

            var household = ctx.People.GetHousehold(ctx.People.HouseholdAt(place.Id));
            if (household != null)
            {
                sb.Append($"  {household.Shape}, {household.Size}: ");
                var names = new List<string>();
                foreach (var id in household.Members) names.Add(ctx.People.Get(id).Forename);
                sb.AppendLine(string.Join(", ", names.ToArray()));
            }
            sb.AppendLine();

            for (int y = b.Y; y <= b.Bottom; y++)
            {
                sb.Append("   ");
                for (int x = b.X; x <= b.Right; x++)
                {
                    var terrain = ctx.World.Grid.TerrainAt(x, y);
                    if (terrain == Terrain.Wall) { sb.Append('#'); continue; }

                    var roomId = ctx.World.Grid.RoomAt(x, y);
                    if (!roomId.IsValid) { sb.Append('+'); continue; }   // doorway or threshold

                    sb.Append(Letter(ctx.World.GetRoom(roomId).Kind));
                }
                sb.AppendLine();
            }

            sb.AppendLine();
            foreach (var id in ctx.World.RoomsIn(place.Id))
            {
                var room = ctx.World.GetRoom(id);
                sb.AppendLine($"   {Letter(room.Kind)}  {room.Describe(),-14} "
                            + $"{room.Bounds.W}x{room.Bounds.H} ({room.Area} m2)");
            }
            return sb.ToString();
        }

        /// <summary>Every dwelling's plan side by side, to check they are not all the same.</summary>
        public static string Houses(VillageContext ctx, int count)
        {
            var sb = new StringBuilder();
            var dwellings = ctx.World.PlacesOfKind(PlaceKind.Dwelling);
            int n = Math.Min(count, dwellings.Count);
            for (int i = 0; i < n; i++)
            {
                sb.AppendLine(House(ctx, i));
                sb.AppendLine(new string('-', 46));
            }
            sb.AppendLine("   # wall   + doorway   H hall   L front room   K kitchen");
            sb.AppendLine("   B bedroom   W bathroom   S scullery   O back room");
            return sb.ToString();
        }

        /// <summary>
        /// A letter per room kind, for the plan.
        ///
        /// The house kinds keep the letters they have always had, because they collide with each
        /// other on first initial - Bathroom and Bedroom, Kitchen and no other K - and those
        /// letters are in every plan anybody has ever read.
        ///
        /// Everything else falls through to its own first letter rather than to a question mark.
        /// Four grammars and seventeen room kinds arrived at once, and a hand-maintained switch
        /// with a silent default is exactly the thing this project spent a day removing: the
        /// church printed a perfectly good nave as a field of '?' because nothing here had heard
        /// of it. A letter that occasionally repeats is a far smaller lie than a room with no
        /// letter at all, and the legend under the plan says which is which.
        /// </summary>
        private static char Letter(RoomKind kind)
        {
            switch (kind)
            {
                case RoomKind.Hall: return 'H';
                case RoomKind.Living: return 'L';
                case RoomKind.Kitchen: return 'K';
                case RoomKind.Bedroom: return 'B';
                case RoomKind.Bathroom: return 'W';
                case RoomKind.Scullery: return 'S';
                case RoomKind.Workroom: return 'O';
                default:
                    string name = kind.ToString();
                    return name.Length > 0 ? name[0] : '?';
            }
        }

        public static string Summary(VillageContext ctx)
        {
            var p = ctx.People;
            var sb = new StringBuilder();
            sb.AppendLine($"{ctx.World.Name} — seed {ctx.Seed}");
            sb.AppendLine(new string('-', 46));
            sb.AppendLine($"people          {p.Count}");
            sb.AppendLine($"households      {p.HouseholdCount}");
            sb.AppendLine($"  children      {p.CountOfStage(LifeStage.Child, Year)}");
            sb.AppendLine($"  adults        {p.CountOfStage(LifeStage.Adult, Year)}");
            sb.AppendLine($"  elders        {p.CountOfStage(LifeStage.Elder, Year)}");
            sb.AppendLine($"in work         {p.WorkingCount} of {ctx.World.TotalJobSlots} jobs");
            sb.AppendLine();
            sb.AppendLine($"map             {ctx.World.Width} x {ctx.World.Height}");
            sb.AppendLine($"places          {ctx.World.PlaceCount}");
            sb.AppendLine($"rooms           {ctx.World.RoomCount}");
            sb.AppendLine($"furniture       {ctx.World.FurnitureCount}");
            sb.AppendLine($"props           {ctx.World.PropCount}");
            sb.AppendLine();

            var byJob = new Dictionary<Occupation, int>();
            foreach (var c in p.Citizens)
            {
                byJob.TryGetValue(c.Job, out int n);
                byJob[c.Job] = n + 1;
            }
            sb.AppendLine("occupations");
            foreach (var kv in byJob)
                if (kv.Key != Occupation.None)
                    sb.AppendLine($"  {Occupations.NameOf(kv.Key),-14} {kv.Value}");

            var shapes = new Dictionary<HouseholdShape, int>();
            foreach (var h in p.Households)
            {
                shapes.TryGetValue(h.Shape, out int n);
                shapes[h.Shape] = n + 1;
            }
            sb.AppendLine();
            sb.AppendLine("households by shape");
            foreach (var kv in shapes) sb.AppendLine($"  {kv.Key,-14} {kv.Value}");

            return sb.ToString();
        }

        /// <summary>One person's whole day, block by block. The answer to "why is he there".</summary>
        public static string Day(VillageContext ctx, int citizenIndex, int day)
        {
            var c = ctx.People.Get(new CitizenId(citizenIndex));
            if (c == null) return $"no citizen {citizenIndex}";

            var plan = DayPlanner.Plan(ctx.World, ctx.People, c, day, ctx.Seed);
            var household = ctx.People.HouseholdOf(c);

            var sb = new StringBuilder();
            sb.AppendLine($"{c.FullName}, {c.AgeIn(Year)}");
            sb.AppendLine($"  {household}, at {ctx.World.GetPlace(c.Home).Name}");
            if (c.Works)
                sb.AppendLine($"  {Occupations.NameOf(c.Job)} at {ctx.World.GetPlace(c.Work).Name}"
                            + (c.Shift > 0 ? "  (late shift)" : ""));
            else if (c.IsChildIn(Year)) sb.AppendLine("  at school");
            else if (c.StageIn(Year) == LifeStage.Elder) sb.AppendLine("  retired");

            sb.AppendLine();
            foreach (int p in c.Particulars)
                sb.AppendLine("  " + ctx.Particulars.Sentence(c.Forename, p));

            sb.AppendLine();
            sb.AppendLine($"  {GameClock.DayNames[day % 7]}, day {day}");
            foreach (var b in plan.Blocks)
            {
                var place = ctx.World.GetPlace(b.Where);
                sb.AppendLine($"    {b.StartMinute / 60:00}:{b.StartMinute % 60:00}–"
                            + $"{b.EndMinute / 60:00}:{b.EndMinute % 60:00}  "
                            + $"{Pretty(b.What),-18} {place?.Name}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Where everybody is, hour by hour. The density curve the plan says must be VERIFIED
        /// rather than assumed — if the village does not empty at nine and fill at six, the
        /// schedules are wrong and this is where it shows.
        /// </summary>
        public static string DensityByHour(VillageContext ctx, int day)
        {
            var plans = new DayPlan[ctx.People.Count];
            for (int i = 0; i < ctx.People.Count; i++)
                plans[i] = DayPlanner.Plan(ctx.World, ctx.People, ctx.People.Get(new CitizenId(i)), day, ctx.Seed);

            var sb = new StringBuilder();
            sb.AppendLine($"where everyone is — {GameClock.DayNames[day % 7]}, day {day}");
            sb.AppendLine();
            sb.AppendLine("      asleep  home   work  school   pub   shop   out    | out & about");
            sb.AppendLine(new string('-', 78));

            for (int hour = 0; hour < 24; hour++)
            {
                int minute = hour * 60 + 30;
                int asleep = 0, home = 0, work = 0, school = 0, pub = 0, shop = 0, other = 0;

                foreach (var plan in plans)
                {
                    switch (plan.At(minute).What)
                    {
                        case Activity.Asleep: asleep++; break;
                        case Activity.AtHome: home++; break;
                        case Activity.AtWork: work++; break;
                        case Activity.AtSchool: school++; break;
                        case Activity.AtThePub: pub++; break;
                        case Activity.Shopping: shop++; break;
                        default: other++; break;
                    }
                }

                int outAndAbout = work + school + pub + shop + other;
                sb.AppendLine($"{hour:00}:30 {asleep,6} {home,6} {work,6} {school,7} {pub,5} {shop,6} {other,5}"
                            + $"    | {new string('#', Math.Min(outAndAbout, 60))}");
            }
            return sb.ToString();
        }

        private static string Pretty(Activity a)
        {
            switch (a)
            {
                case Activity.Asleep: return "asleep";
                case Activity.AtHome: return "at home";
                case Activity.AtWork: return "at work";
                case Activity.AtSchool: return "at school";
                case Activity.Shopping: return "shopping";
                case Activity.AtThePub: return "at the pub";
                case Activity.AtChurch: return "at church";
                case Activity.Visiting: return "visiting";
                case Activity.Walking: return "out walking";
                case Activity.AtThePlayground: return "playing out";
                case Activity.OnTheAllotment: return "on the allotment";
                case Activity.Talking: return "stopped to talk";
                default: return a.ToString();
            }
        }
    }
}
