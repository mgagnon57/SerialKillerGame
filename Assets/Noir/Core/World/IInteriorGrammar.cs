using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// One way of dividing a building into rooms.
    ///
    /// A grammar is a set of architectural rules, not an algorithm. The BSP that makes houses
    /// was never really "binary subdivision" - it was hall-then-kitchen-then-bedroom, and the
    /// subdivision merely supplied rectangles for those rules to argue over. That is why it
    /// cannot be pointed at a hospital: the rules are about dwellings, and applied to a 330 m2
    /// footprint they produce a kitchen the size of a ward.
    ///
    /// So there is one grammar per TYPE of layout, and the kind table says which one a given
    /// kind of place uses. Each grammar is free to have its own idea of how many rooms a
    /// building needs - one room per eight square metres is a fact about houses.
    ///
    /// <paramref name="programme"/> is the schedule of accommodation: what the rooms are FOR.
    /// Two hospitals differ from two schools not in shape but in what hangs off the corridor,
    /// so the grammar supplies the corridor and the programme supplies the wards. It is the
    /// place kind's own name - "hospital", "school", "shop" - and every grammar has a sane
    /// default for one it does not recognise.
    /// </summary>
    public interface IInteriorGrammar
    {
        /// <summary>The name the kind table asks for this grammar by.</summary>
        string Name { get; }

        Interior Generate(TileRect exterior, Tile frontDoor, string programme, IRng rng);
    }

    /// <summary>
    /// Every grammar there is, by name. This is what the `grammar` column in kinds.txt names.
    ///
    ///   bsp     a house: subdivide, then argue about what the rectangles are for
    ///   spine   a corridor with rooms off it: hospital, school, offices, surgery
    ///   open    one public room with the back of house behind it: shop, pub, cinema
    ///   hall    one dominant volume with the small stuff at the far end: church, village hall
    ///
    /// Names are matched case-insensitively, and the obvious synonyms are accepted, because a
    /// content file that fails to load over "corridor" instead of "spine" is a worse outcome
    /// than a short alias table.
    /// </summary>
    public static class InteriorGrammars
    {
        private static readonly IInteriorGrammar[] All =
        {
            new DomesticBsp(),
            new SpineCorridor(),
            new OpenFloor(),
            new HallGrammar(),
        };

        /// <summary>The house rules, and what anything unrecognised falls back to.</summary>
        public static IInteriorGrammar Domestic => All[0];

        public static System.Collections.Generic.IReadOnlyList<IInteriorGrammar> Known => All;

        /// <summary>Every grammar name, for the kind table to check a row against.</summary>
        public static string[] Names()
        {
            var names = new string[All.Length];
            for (int i = 0; i < All.Length; i++) names[i] = All[i].Name;
            return names;
        }

        public static bool IsKnown(string name) => Find(name) != null;

        /// <summary>The named grammar, or null if there is no such thing.</summary>
        public static IInteriorGrammar Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            string wanted = Canonical(name);
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Name, wanted, System.StringComparison.OrdinalIgnoreCase))
                    return All[i];
            return null;
        }

        private static string Canonical(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "domestic":
                case "house":
                case "dwelling": return "bsp";
                case "corridor": return "spine";
                case "openfloor":
                case "openplan": return "open";
                case "assembly":
                case "volume": return "hall";
                default: return name;
            }
        }
    }
}
