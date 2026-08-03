using System.Collections.Generic;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Which grammar shapes which building, looked up by the `massing` column in kinds.txt.
    /// </summary>
    public static class MassingGrammars
    {
        private static readonly CottageMassing Fallback = new CottageMassing();

        private static readonly Dictionary<string, IMassingGrammar> Registry =
            new Dictionary<string, IMassingGrammar>
            {
                { "cottage",   Fallback },
                { "shopfront", new ShopfrontMassing() },
                { "pub",       new PubMassing() },
                { "hall",      new HallMassing() },
                { "school",    new SchoolMassing() },
                { "church",    new ChurchMassing() },
                { "mill",      new MillMassing() },
                { "garage",    new GarageMassing() },
                { "works",     new WorksMassing() },

                // The Illinois frame house, in the three date-layers the record shows. See
                // FrameHouseGrammars - these are sourced off nine Sanborn sheets and the town's
                // 1943 median build year, not chosen by eye. Registered but not yet named by any
                // row in kinds.txt: the models are being prototyped against the packs first.
                { "farmhouse",  new FarmhouseMassing() },
                { "foursquare", new FoursquareMassing() },
                { "bungalow",   new BungalowMassing() },
                { "ranch",      new RanchMassing() },
            };

        /// <summary>
        /// The grammar for a place, or the cottage if its column names one nothing answers to.
        ///
        /// Falling back rather than throwing is the point, and it is the other half of the trade
        /// recorded on PlaceKindRow.Massing. Core refuses an unknown INTERIOR grammar at load
        /// because InteriorGenerator lives in Core and can be asked; it cannot ask about these,
        /// because they live here and Core must not learn about Unity. So the check happens at
        /// build time instead, and it warns rather than refusing - a village that will not render
        /// because somebody typed "cotage" is worse than one with a plain building in it.
        ///
        /// Warned once per bad name rather than once per building, or a single typo in a common
        /// kind would print forty-four identical lines and bury everything else in the log.
        /// </summary>
        public static IMassingGrammar For(Place place)
        {
            string name = place == null ? null : PlaceKindTable.Current.Row(place.Kind).Massing;
            if (name != null && Registry.TryGetValue(name, out var grammar)) return grammar;

            if (name != null && _warned.Add(name))
                UnityEngine.Debug.LogWarning(
                    $"kinds.txt: no massing grammar answers to '{name}'; using cottage. "
                  + "The building will render, it will just look like a house.");

            return Fallback;
        }

        private static readonly HashSet<string> _warned = new HashSet<string>();

        public static Massing Of(Place place) => For(place).Profile(place);
    }

    /// <summary>
    /// A house.
    ///
    /// Exactly what every building in the village got before massing existed, kept to the decimal
    /// so that introducing this system does not move a single cottage. Dwellings and farmhouses
    /// both use it - a farmhouse genuinely is a house, which is the same reasoning that leaves
    /// the farm on the `domestic` interior grammar.
    /// </summary>
    public sealed class CottageMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => Massing.Cottage;
        public void Extras(Place place, MeshChunk into) { }
    }
}
