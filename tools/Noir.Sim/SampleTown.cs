using System;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// A settlement of a stated size, built to be filled with people and nothing else.
    ///
    /// This exists so that `vocab --pop 600` can be answered before the town of six hundred is
    /// authored. Ashcombe holds a hundred and nine and no argument will make it hold more, and
    /// tiling it — which is what `strand --tile` does — only reaches 109, 436, 981: it cannot
    /// stop at a number somebody asked for.
    ///
    /// Dwellings only, and no interiors. That is not a shortcut, it is the whole claim: the
    /// generator draws names and particulars per household and per person, and takes no draw at
    /// all from geometry, jobs, schools or opening hours. So a world with the right number of
    /// front doors puts exactly the same DEMAND on the content tables as a fully authored town
    /// of the same population, and puts it there in a millisecond instead of a minute. It would
    /// be the wrong world to measure anything else with, and this is the only thing it is used
    /// for.
    /// </summary>
    public static class SampleTown
    {
        /// <summary>
        /// People per home, from the generator's own household weights: 28 solitaries, 30
        /// couples, 20 families averaging 3.5, and so on, which come to 2.22. A starting guess
        /// only — the size is then measured and corrected, because the weights are rolled rather
        /// than obeyed.
        /// </summary>
        private const double PeoplePerUnit = 2.22;

        private const int Pitch = 12;
        private const int PlotSize = 8;

        /// <summary>
        /// The world whose generated population lands nearest <paramref name="target"/>.
        ///
        /// Iterated rather than computed: how many people a home holds is a roll, so the only
        /// honest way to hit a number is to build one, count, and correct. Bounded and
        /// deterministic — same seed and target, same town, every run.
        ///
        /// The tables are taken rather than invented because the sizing run has to consume the
        /// generator's stream exactly as the measured run will. Both PickSurname and the
        /// particulars draw retry until they get something they have not already used, so a
        /// table of a different SIZE consumes a different number of draws, which shifts the next
        /// household's shape, which changes the population. Sizing against a stand-in would
        /// produce a town aimed at six hundred that generates five hundred and eighty.
        /// </summary>
        public static WorldModel ForPopulation(int target, ulong seed, NameTable names,
                                               ParticularsTable particulars, out int homes,
                                               out int trials)
        {
            if (target < 1) target = 1;

            int units = (int)Math.Ceiling(target / PeoplePerUnit);
            if (units < 1) units = 1;

            int bestUnits = units, bestMiss = int.MaxValue;
            trials = 0;

            for (int attempt = 0; attempt < 16; attempt++)
            {
                trials++;
                var trial = Build(units, seed);
                int got = PopulationGenerator.Generate(trial, names, particulars, seed).Count;

                int miss = Math.Abs(got - target);
                if (miss < bestMiss) { bestMiss = miss; bestUnits = units; }
                if (miss == 0) break;

                int step = (int)Math.Round((target - got) / PeoplePerUnit);
                if (step == 0) step = target > got ? 1 : -1;
                units += step;
                if (units < 1) units = 1;
            }

            homes = bestUnits;
            return Build(bestUnits, seed);
        }

        /// <summary>A lattice of cottages on open ground. Nothing here is meant to be walked.</summary>
        public static WorldModel Build(int homes, ulong seed)
        {
            if (homes < 1) homes = 1;

            int cols = (int)Math.Ceiling(Math.Sqrt(homes));
            if (cols < 1) cols = 1;
            int rows = (homes + cols - 1) / cols;

            var grid = new TileGrid(cols * Pitch + 4, rows * Pitch + 4);
            var places = new Place[homes];

            for (int i = 0; i < homes; i++)
            {
                int x = 2 + (i % cols) * Pitch;
                int y = 2 + (i / cols) * Pitch;
                var bounds = new TileRect(x, y, PlotSize, PlotSize);

                places[i] = new Place(new PlaceId(i), PlaceKind.Dwelling, "plot " + i, "",
                                      bounds, new Tile(x + PlotSize / 2, y),
                                      Array.Empty<OpenWindow>(), 0, 1);
            }

            return new WorldModel("sample of " + homes + " homes", grid, places);
        }
    }
}
