using System;
using System.Text;
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// How many people you can actually SEE, hour by hour.
    ///
    /// Every other instrument counts where people are RECORDED — `density` reads the day plan and
    /// tells you 55 are at home and 37 at work. None of them answers the question you ask by
    /// standing in the street, which is how many bodies are visible from it. Almost every
    /// destination in this village is indoors, so the visible population is very nearly the
    /// TRANSIT population, and nothing measured that.
    ///
    /// Outdoors here means the literal thing the camera sees: the tile under the agent is inside
    /// no building's footprint. That deliberately counts somebody standing on the green or in the
    /// churchyard as visible, because they are.
    /// </summary>
    public static class StreetReport
    {
        public static string Run(VillageContext ctx, int days)
        {
            var sim = new Simulation(ctx.World, ctx.People, ctx.Seed, 0);
            int n = sim.AgentCount;
            int ticks = days * 1440 * GameClock.TicksPerMinute;

            // One lookup rather than a scan of 61 places per agent per tick.
            //
            // Indoors means ROOFED, not "inside a Place". The green, the playground, the
            // allotments and the churchyard are all Places with bounds and `roof no` — somebody
            // standing on the green is as visible as somebody walking past it, and the first
            // version of this report counted them as hidden. That understated the visible
            // population by a factor of seventeen and disagreed with `ratio`, which is how it
            // was caught.
            var indoors = new bool[ctx.World.Width * ctx.World.Height];
            foreach (var place in ctx.World.AllPlaces)
            {
                if (!PlaceKindTable.Current.Row(place.Kind).Roofed) continue;
                var b = place.Bounds;
                for (int y = b.Y; y < b.Y + b.H; y++)
                for (int x = b.X; x < b.X + b.W; x++)
                    if (x >= 0 && y >= 0 && x < ctx.World.Width && y < ctx.World.Height)
                        indoors[y * ctx.World.Width + x] = true;
            }

            var perHour = new long[24];
            var peakHour = new int[24];
            long total = 0;
            int peak = 0;
            long peakAt = 0;
            var everOut = new bool[n];

            for (int t = 0; t < ticks; t++)
            {
                sim.Tick();

                int minute = (int)(sim.Clock.Tick / GameClock.TicksPerMinute) % 1440;
                int hour = minute / 60;

                int outNow = 0;
                for (int i = 0; i < n; i++)
                {
                    var tile = sim.GetAgent(i).Position.ToTile();
                    if (!ctx.World.Grid.InBounds(tile)) continue;
                    if (indoors[tile.Y * ctx.World.Width + tile.X]) continue;
                    outNow++;
                    everOut[i] = true;
                }

                perHour[hour] += outNow;
                if (outNow > peakHour[hour]) peakHour[hour] = outNow;
                total += outNow;
                if (outNow > peak) { peak = outNow; peakAt = sim.Clock.Tick; }
            }

            int ticksPerHourSlot = ticks / 24;
            var sb = new StringBuilder();
            sb.AppendLine($"street — {ctx.World.Name}, {n} people, "
                        + $"{ctx.World.Width}x{ctx.World.Height}, {days} day(s)");
            sb.AppendLine("         how many bodies are visible from outside, not where plans say they are");
            sb.AppendLine();
            sb.AppendLine("        mean   peak  | visible outdoors");
            sb.AppendLine(new string('-', 60));

            for (int h = 0; h < 24; h++)
            {
                double mean = ticksPerHourSlot == 0 ? 0 : perHour[h] / (double)ticksPerHourSlot;
                sb.AppendLine($"{h:00}:00 {mean,8:0.00} {peakHour[h],6}  | "
                            + new string('#', Math.Min((int)Math.Round(mean * 2), 50)));
            }

            int seen = 0;
            for (int i = 0; i < n; i++) if (everOut[i]) seen++;

            double dayMean = total / (double)ticks;
            sb.AppendLine();
            sb.AppendLine($"  mean visible, whole day   {dayMean,8:0.00} of {n}  ({100.0 * dayMean / n:0.0}%)");
            sb.AppendLine($"  peak visible at once      {peak,8}   at {At(peakAt)}");
            sb.AppendLine($"  ever seen outdoors        {seen,8} of {n}");
            sb.AppendLine();
            sb.AppendLine($"  minutes outdoors per person per day  {1440.0 * dayMean / n:0.0}");
            return sb.ToString();
        }

        private static string At(long tick)
        {
            int minute = (int)(tick / GameClock.TicksPerMinute);
            return $"day {minute / 1440} {minute % 1440 / 60:00}:{minute % 60:00}";
        }
    }
}
