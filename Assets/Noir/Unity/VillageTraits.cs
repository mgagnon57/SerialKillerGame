namespace Noir.Unity
{
    /// <summary>
    /// The traits a person in this village can be given, grouped so the picker is a list you
    /// scan rather than a wall you read.
    ///
    /// CHOSEN FOR A SMALL TOWN WHERE SOMEBODY IS KILLING PEOPLE, which is a narrower brief than
    /// "personality". Half of these are about ROUTINE and OBSERVATION - who is out at what hour,
    /// who watches the street, who would notice a car that did not belong. That is the half an
    /// investigation runs on, and `docs/research/WHO-SEES-WHOM.md` is the research it answers to.
    /// The rest is character: a village of 1,300 in 1991 is not a village of averages, and the
    /// oddities are what make one house different from the next.
    ///
    /// Written as plain strings rather than an enum on purpose. These are authored notes, the
    /// list will be added to constantly by somebody who remembers a particular man, and an enum
    /// would mean a recompile and a migration every time. Anything not in this list can still be
    /// typed into the behaviour box free-hand.
    /// </summary>
    public static class VillageTraits
    {
        public sealed class Group
        {
            public readonly string Name;
            public readonly string[] Traits;
            public Group(string name, params string[] traits) { Name = name; Traits = traits; }
        }

        public static readonly Group[] All =
        {
            // WHO SEES WHAT. The mechanically useful half.
            new Group("watching",
                "curtain-twitcher", "knows everyone's business", "waves at every car",
                "sits on the porch", "notices strangers", "keeps a police scanner on",
                "walks the dog late", "up before the paper comes", "night owl",
                "never looks up", "keeps to himself", "won't speak to the neighbours"),

            new Group("routine",
                "same route every day", "at the pub most nights", "church every Sunday",
                "never misses a funeral", "coffee at the same table", "drives to Danville for work",
                "works nights at the plant", "farms on the side", "school bus driver",
                "volunteer fireman", "shift work - odd hours", "retired, home all day"),

            // CHARACTER. What makes one house different from the next.
            new Group("temper",
                "generous", "short-tempered", "holds a grudge", "feuding with a neighbour",
                "peacemaker", "sharp-tongued", "gentle", "proud", "bitter about something",
                "everybody's friend", "not spoken to since the divorce"),

            new Group("the house",
                "immaculate yard", "lets it all go", "always a project on the drive",
                "hoards newspapers", "collects something", "burns his own trash",
                "keeps chickens", "dog in the yard", "porch light always on",
                "never locks the door", "shotgun behind the door", "no telephone"),

            new Group("odd",
                "talks to the dog", "eccentric dresser", "writes letters to the paper",
                "sworn off television", "keeps bees", "afraid of the tracks",
                "won't cross the county line", "believes the fire was set",
                "still keeps her husband's chair", "drinks alone", "seen at strange hours"),

            new Group("standing",
                "old family in the township", "moved in recently", "married in",
                "grew up here and came back", "everybody owes him a favour",
                "runs a business in town", "on the village board", "in the historical society",
                "nobody quite knows what he does"),
        };

        /// <summary>Every trait, flattened - for the parser, which does not care about groups.</summary>
        public static System.Collections.Generic.IEnumerable<string> Flat()
        {
            foreach (var g in All)
                foreach (var t in g.Traits)
                    yield return t;
        }
    }
}
