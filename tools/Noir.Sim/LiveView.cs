using System;
using System.Collections.Generic;
using System.Text;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// The village, moving, in a terminal. Crude, but it answers the only question that matters
    /// at this stage — do a hundred people going about their day look like a place, or like a
    /// hundred dots on rails?
    /// </summary>
    public static class LiveView
    {
        public static string Frame(Simulation sim, int scale)
        {
            var grid = sim.World.Grid;
            int w = (grid.Width + scale - 1) / scale;
            int h = (grid.Height + scale - 1) / scale;

            // Terrain underneath.
            var cells = new char[h, w];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cells[y, x] = AsciiMap.Glyph(grid.TerrainAt(x * scale, y * scale));

            // People on top. Crowds become digits, which is how a village reads at a glance:
            // you do not count eleven men in the pub, you notice that the pub is full.
            var counts = new int[h, w];
            for (int i = 0; i < sim.AgentCount; i++)
            {
                var a = sim.GetAgent(i);
                var t = a.Position.ToTile();
                int cx = t.X / scale, cy = t.Y / scale;
                if (cx < 0 || cy < 0 || cx >= w || cy >= h) continue;
                counts[cy, cx]++;
            }

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int n = counts[y, x];
                if (n == 0) continue;
                cells[y, x] = n == 1 ? 'o' : n < 10 ? (char)('0' + n) : '*';
            }

            var sb = new StringBuilder();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) sb.Append(cells[y, x]);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string StatusLine(Simulation sim)
        {
            int asleep = 0, travelling = 0, working = 0, atSchool = 0, pub = 0, out_ = 0;
            for (int i = 0; i < sim.AgentCount; i++)
            {
                var a = sim.GetAgent(i);
                if (a.Travelling) { travelling++; continue; }
                switch (a.Doing)
                {
                    case Activity.Asleep: asleep++; break;
                    case Activity.AtWork: working++; break;
                    case Activity.AtSchool: atSchool++; break;
                    case Activity.AtThePub: pub++; break;
                    case Activity.AtHome: break;
                    default: out_++; break;
                }
            }

            return $"{sim.Clock}   asleep {asleep,3}   walking {travelling,3}   "
                 + $"work {working,3}   school {atSchool,3}   pub {pub,2}   out {out_,3}";
        }

        /// <summary>Animate the village for a while.</summary>
        public static void Watch(VillageContext ctx, int startHour, int gameHours,
                                 int minutesPerFrame, int scale, int frameDelayMs)
        {
            var sim = new Simulation(ctx.World, ctx.People, ctx.Seed, startHour * 60);
            int ticksPerFrame = minutesPerFrame * GameClock.TicksPerMinute;
            int frames = gameHours * 60 / Math.Max(1, minutesPerFrame);

            // When output is piped to a file there is no cursor to move, so fall back to
            // simply printing each frame in sequence.
            bool animate = !Console.IsOutputRedirected;
            if (animate) Console.Clear();

            for (int f = 0; f < frames; f++)
            {
                sim.Tick(ticksPerFrame);

                var sb = new StringBuilder();
                sb.Append(Frame(sim, scale));
                sb.Append('\n');
                sb.Append(StatusLine(sim));
                sb.Append("        o one person · digits a crowd\n");

                if (animate)
                {
                    Console.SetCursorPosition(0, 0);
                    Console.Write(sb.ToString());
                    if (frameDelayMs > 0) System.Threading.Thread.Sleep(frameDelayMs);
                }
                else
                {
                    Console.WriteLine(sb.ToString());
                }
            }
        }

        /// <summary>
        /// Follow one person through a day, reporting only when something changes.
        /// This is the strongest evidence the simulation is doing what the schedules say.
        /// </summary>
        public static string Trace(VillageContext ctx, int citizenIndex, int hours, int startHour)
        {
            var citizen = ctx.People.Get(new CitizenId(citizenIndex));
            if (citizen == null) return $"no citizen {citizenIndex}";

            var sim = new Simulation(ctx.World, ctx.People, ctx.Seed, startHour * 60);
            var sb = new StringBuilder();

            sb.AppendLine($"{citizen.FullName}, {citizen.AgeIn(sim.Clock.Year)} — "
                        + $"{(citizen.Works ? ctx.World.GetPlace(citizen.Work).Name : citizen.IsChildIn(sim.Clock.Year) ? "at school" : "not in work")}");
            sb.AppendLine($"home: {ctx.World.GetPlace(citizen.Home).Name}");
            sb.AppendLine();

            var lastDoing = (Activity)255;
            var lastPlace = PlaceId.None;
            int totalTicks = hours * 60 * GameClock.TicksPerMinute;

            for (int t = 0; t < totalTicks; t++)
            {
                sim.Tick();
                var a = sim.GetAgent(citizen.Id);

                bool changed = a.Doing != lastDoing ||
                               (!a.Travelling && a.At != lastPlace);
                if (!changed) continue;

                var place = ctx.World.GetPlace(a.Travelling ? sim.CurrentBlock(citizen.Id).Where : a.At);
                string verb = a.Travelling ? "walking to" : Describe(a.Doing);
                sb.AppendLine($"  {sim.Clock}  {verb,-16} {place?.Name}");

                lastDoing = a.Doing;
                lastPlace = a.Travelling ? PlaceId.None : a.At;
            }

            return sb.ToString();
        }

        private static string Describe(Activity a)
        {
            switch (a)
            {
                case Activity.Asleep: return "asleep at";
                case Activity.AtHome: return "at home in";
                case Activity.AtWork: return "working at";
                case Activity.AtSchool: return "at school";
                case Activity.Shopping: return "shopping at";
                case Activity.AtThePub: return "in";
                case Activity.AtChurch: return "at";
                case Activity.Visiting: return "visiting";
                case Activity.Walking: return "walking on";
                case Activity.AtThePlayground: return "playing at";
                case Activity.OnTheAllotment: return "digging at";
                default: return a.ToString();
            }
        }
    }
}
