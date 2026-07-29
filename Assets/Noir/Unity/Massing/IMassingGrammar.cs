using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// One kind of building's outside shape.
    ///
    /// Deliberately an interface with a registry rather than a switch on PlaceKind. Stage 4 made
    /// PlaceKind OPEN - a row the enum has never heard of gets the next value along, so a new
    /// amenity costs one content row and no C# at all - and a switch in the mesh builder would
    /// quietly close it again. An unregistered kind falls through to the cottage and still looks
    /// like a building.
    ///
    /// The same distinction the interior grammars are built on, one layer out:
    ///
    ///     Interior grammar is how the building is ARRANGED.
    ///     Massing grammar is how the building is SHAPED.
    ///     Kind is what it is FOR.
    /// </summary>
    public interface IMassingGrammar
    {
        /// <summary>Wall height, roof form, pitch. Called once per building at build time.</summary>
        Massing Profile(Place place);

        /// <summary>
        /// Bespoke geometry this kind and no other gets: a tower, a bell-cote, a hoist.
        ///
        /// Emitted into the same chunked mesh as the roofs, so it inherits the existing chunking
        /// and frustum culling and adds no renderers of its own. Most grammars add nothing and
        /// should leave this empty.
        /// </summary>
        void Extras(Place place, MeshChunk into);
    }
}
