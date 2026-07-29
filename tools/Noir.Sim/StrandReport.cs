using System;
using System.Text;
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// Who could not get where they were going, and for how long.
    ///
    /// A failed path is the one simulation failure with no visible symptom: the renderer draws a
    /// person from their position, so somebody who never set off looks exactly like somebody
    /// standing at home. Everything that reads the place they are IN — the inspector, the
    /// density table, window lights, and eventually the investigation — believes whatever the
    /// sim last wrote there. So it gets counted here rather than looked for by eye.
    /// </summary>
    public static class StrandReport
    {
        public static string Run(VillageContext ctx, int days, int startHour = 0)
        {
            var sim = new Simulation(ctx.World, ctx.People, ctx.Seed, startHour * 60);
            int n = sim.AgentCount;
            int ticks = days * 1440 * GameClock.TicksPerMinute;

            var stranded = new Tally(n);
            var misplaced = new Tally(n);

            for (int t = 0; t < ticks; t++)
            {
                sim.Tick();

                int misplacedNow = 0;
                for (int i = 0; i < n; i++)
                {
                    var a = sim.GetAgent(i);

                    stranded.Sample(i, a.Stranded,
                                    (int)(sim.Clock.Tick - a.StrandedSinceTick) + 1, a.Destination);

                    bool lying = a.At.IsValid && !Inside(ctx, a.At, a.Position);
                    if (lying) misplacedNow++;
                    misplaced.Sample(i, lying, misplaced.Run(i) + 1, a.At);
                }

                stranded.Close(sim.StrandedCount, sim.Clock.Tick);
                misplaced.Close(misplacedNow, sim.Clock.Tick);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"strand — {ctx.World.Name}, {n} people, "
                        + $"{ctx.World.Width}x{ctx.World.Height}, {days} day(s) from {startHour:00}:00");

            // A map in two pieces would produce journeys that are impossible rather than
            // expensive, and the two failures need telling apart or neither number means
            // anything.
            var layout = WorldValidator.Validate(ctx.World);
            sb.AppendLine($"         {layout.RegionCount} walkable region(s), "
                        + $"{100.0 * layout.LargestRegion / layout.WalkableTiles:0.0}% of the map "
                        + "reachable from the largest");
            sb.AppendLine();

            // Standing somewhere unwalkable is a different fault with a different owner: no
            // pathfinder can route out of a wall, so those are the map's or the spawner's and
            // not the search budget's.
            int inAWall = 0;
            for (int i = 0; i < n; i++)
            {
                var a = sim.GetAgent(i);
                if (a.Stranded && !ctx.World.Grid.IsWalkable(a.Position.ToTile())) inAWall++;
            }

            sb.AppendLine("  STRANDED — wanted to be somewhere and no route came back");
            sb.Append(stranded.Describe(ticks, n));
            sb.AppendLine($"    searches given up  {sim.GaveUpTotal,7}");
            sb.AppendLine($"    stood in a wall    {inAWall,7}   (of those stranded at the end)");
            sb.AppendLine();

            sb.AppendLine("  MISPLACED — recorded as being somewhere they were not");
            sb.Append(misplaced.Describe(ticks, n));
            sb.AppendLine();

            sb.Append(Offender(ctx, stranded));
            return sb.ToString();
        }

        /// <summary>Peak, mean and per-person worst run of one kind of failure.</summary>
        private sealed class Tally
        {
            private readonly int[] _run;
            private readonly int[] _worstRun;
            private readonly PlaceId[] _worstWhere;
            private readonly int[] _ticks;

            private long _total;
            private int _peak;
            private long _peakAt;

            public Tally(int agents)
            {
                _run = new int[agents];
                _worstRun = new int[agents];
                _worstWhere = new PlaceId[agents];
                _ticks = new int[agents];
            }

            public int Run(int agent) => _run[agent];

            public void Sample(int agent, bool failing, int runLength, PlaceId where)
            {
                if (!failing) { _run[agent] = 0; return; }

                _ticks[agent]++;
                _run[agent] = runLength;
                if (runLength > _worstRun[agent]) { _worstRun[agent] = runLength; _worstWhere[agent] = where; }
            }

            public void Close(int nowFailing, long tick)
            {
                _total += nowFailing;
                if (nowFailing > _peak) { _peak = nowFailing; _peakAt = tick; }
            }

            public int Worst()
            {
                int worst = 0;
                for (int i = 1; i < _worstRun.Length; i++)
                    if (_worstRun[i] > _worstRun[worst]) worst = i;
                return _worstRun[worst] > 0 ? worst : -1;
            }

            public int WorstMinutes(int agent) => _worstRun[agent] / GameClock.TicksPerMinute;
            public PlaceId WorstWhere(int agent) => _worstWhere[agent];

            public string Describe(int ticks, int agents)
            {
                int affected = 0;
                for (int i = 0; i < _ticks.Length; i++) if (_ticks[i] > 0) affected++;

                int worst = Worst();
                var sb = new StringBuilder();
                sb.AppendLine($"    peak at once  {_peak,12}{(_peak > 0 ? "   at " + At(_peakAt) : "")}");
                sb.AppendLine($"    mean          {_total / (double)ticks,12:0.00}");
                sb.AppendLine($"    longest       {(worst < 0 ? 0 : WorstMinutes(worst)),12} minutes");
                sb.AppendLine($"    people        {affected,12} of {agents}  ({100.0 * affected / agents:0.0}%)");
                return sb.ToString();
            }
        }

        /// <summary>Whoever spent the longest unbroken stretch unable to set off.</summary>
        private static string Offender(VillageContext ctx, Tally stranded)
        {
            int worst = stranded.Worst();
            if (worst < 0) return "  Nobody was ever stranded.\n";

            var who = ctx.People.Get(new CitizenId(worst));
            var sb = new StringBuilder();
            sb.AppendLine("  WORST OFFENDER");
            sb.AppendLine($"    {who.FullName}, of {Named(ctx.World.GetPlace(who.Home))}");
            sb.AppendLine($"    stranded {stranded.WorstMinutes(worst)} minutes trying to reach "
                        + Named(ctx.World.GetPlace(stranded.WorstWhere(worst))));
            return sb.ToString();
        }

        private static bool Inside(VillageContext ctx, PlaceId place, Vec2 position)
        {
            var p = ctx.World.GetPlace(place);
            return p != null && p.Bounds.Contains(position.ToTile());
        }

        private static string Named(Place p) => p == null ? "nowhere" : "'" + p.Name + "'";

        private static string At(long tick)
        {
            int minute = (int)(tick / GameClock.TicksPerMinute);
            return $"day {minute / 1440} {minute % 1440 / 60:00}:{minute % 60:00}";
        }
    }
}
