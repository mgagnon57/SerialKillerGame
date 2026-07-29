using Noir.Core.People;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>Loads the authored village and fills it with people. One place, so every
    /// harness command sees exactly the same world.</summary>
    public sealed class VillageContext
    {
        public readonly WorldModel World;
        public readonly Population People;
        public readonly ParticularsTable Particulars;
        public readonly ulong Seed;

        private VillageContext(WorldModel world, Population people, ParticularsTable particulars, ulong seed)
        {
            World = world;
            People = people;
            Particulars = particulars;
            Seed = seed;
        }

        public static VillageContext Load(ulong seed = 1979) => Load(1, seed);

        /// <summary>
        /// The authored village, optionally repeated <paramref name="tiles"/> times each way.
        /// One copy is Ashcombe exactly as written; more than one is the same content on a map
        /// several times the size, which is the only way to see a cost that only appears there.
        /// </summary>
        public static VillageContext Load(int tiles, ulong seed = 1979)
        {
            var layout = BigVillage.Repeat(VillageParser.Parse(ContentPath.ReadAll("village.txt")), tiles);
            var world = WorldBuilder.Build(layout);
            var names = NameTable.Parse(ContentPath.ReadAll("names.txt"));
            var particulars = ParticularsTable.Parse(ContentPath.ReadAll("particulars.txt"));
            var people = PopulationGenerator.Generate(world, names, particulars, seed);
            return new VillageContext(world, people, particulars, seed);
        }
    }
}
