using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;
using Noir.Core.Witness;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// A fortnight of the player walking the village, and everything anybody could say about it.
    ///
    /// THIS IS THE SUCCESS TEST FOR THE WHOLE WITNESS LAYER, and it is a histogram rather than an
    /// assertion because the property it measures is "is this interesting", which no assert can
    /// hold. If nearly every statement is a bare figure, the tuning in Sightlines and Degradation
    /// is wrong and no amount of dialogue on top will save it. What we want is a scatter of
    /// witnesses each holding one narrow fragment, where the fragments only mean something
    /// assembled.
    /// </summary>
    public static class TestimonyReport
    {
        public static int Run(int days)
        {
            VillageContext v = VillageContext.Load();

            // The player walks the village: a lap of the road network, one tile a minute, from
            // early morning to late at night. Crude on purpose — this measures what the town
            // notices, so the movement only has to be movement.
            var track = new PlayerTrack();
            var roadTiles = new List<Tile>();
            foreach (Place p in v.World.AllPlaces) roadTiles.Add(p.Door);

            for (int day = 0; day < days; day++)
            for (int m = 0; m < Sighting.MinutesPerDay; m++)
            {
                int minute = day * Sighting.MinutesPerDay + m;
                Tile at = roadTiles[(minute / 3) % roadTiles.Count];
                track.Record(minute, at, (minute % 7 == 0) ? Visibly.Carrying : Visibly.Nothing);
            }

            int statements = 0, blank = 0;
            var byClarity = new int[3];
            var byBandCount = new int[7];
            var witnesses = new HashSet<int>();
            var samples = new List<string>();

            for (int day = 0; day < days; day++)
            foreach (Citizen who in v.People.Citizens)
            {
                Sighting[] said = Recollection.WhatTheySaw(v.World, v.People, who, day, track, v.Seed);
                if (said.Length > 0) witnesses.Add(who.Id.Value);

                foreach (Sighting s in said)
                {
                    statements++;
                    byClarity[(int)s.Clarity]++;
                    byBandCount[s.Description.NoticedCount]++;
                    if (s.Description.IsBlank) blank++;
                    else if (samples.Count < 25 && statements % 17 == 0)
                        samples.Add("  d" + s.Day + " " + s.MinuteOfDay / 60 + ":" +
                                    (s.MinuteOfDay % 60).ToString("00") + "  " +
                                    who.FullName + ": \"" + s.Description + "\"");
                }
            }

            Console.WriteLine("TESTIMONY over " + days + " days, " + v.People.Count + " people");
            Console.WriteLine();
            Console.WriteLine("  statements      " + statements);
            Console.WriteLine("  witnesses       " + witnesses.Count + " of " + v.People.Count);
            Console.WriteLine("  blank ('a figure') " + blank + "  (" +
                              (statements == 0 ? 0 : blank * 100 / statements) + "%)");
            Console.WriteLine();
            Console.WriteLine("  by clarity      glimpsed " + byClarity[0] +
                              "   partial " + byClarity[1] + "   clear " + byClarity[2]);
            Console.Write("  bands noticed   ");
            for (int i = 0; i < byBandCount.Length; i++) Console.Write(i + ":" + byBandCount[i] + "  ");
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("A sample of what the town would say:");
            foreach (string line in samples) Console.WriteLine(line);

            Console.WriteLine();
            Console.WriteLine(blank * 2 > statements
                ? "MOSTLY BLANK. Over half of all testimony is 'a figure'. The evidence is too thin\n" +
                  "to interrogate — retune Sightlines (range/light bands) or Degradation (surviving\n" +
                  "band counts) before building anything on top of it."
                : "Usable. Most statements carry at least one band worth asking about.");

            return 0;
        }
    }
}
