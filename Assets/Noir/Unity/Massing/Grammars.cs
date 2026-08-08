using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// A shop, a post office, a surgery.
    ///
    /// Taller in the ground floor than a house, which is the real difference between a shop and
    /// the cottage next door: the window has to be big and the fascia has to go somewhere. The
    /// roof stays hipped, because a commercial building in a village terrace is usually the same
    /// roof as its neighbours with a different thing underneath.
    /// </summary>
    public sealed class ShopfrontMassing : IMassingGrammar
    {
        public Massing Profile(Place place) =>
            new Massing(place.Kind == PlaceKind.Clinic ? 3.4f : 3.6f, RoofForm.Hip, 2.0f);

        public void Extras(Place place, MeshChunk into) { }
    }

    /// <summary>
    /// A pub. Gable ends and a heavy stack.
    ///
    /// The gable is doing the work: a public house is usually the oldest and most stubbornly
    /// shaped building on a village street, and an end-on gable to the road is what says so.
    /// </summary>
    public sealed class TavernMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(3.4f, RoofForm.Gable, 2.4f);
        public void Extras(Place place, MeshChunk into) { }
    }

    /// <summary>
    /// A village hall. One tall room, so the walls go up and the roof goes over in one span.
    /// </summary>
    public sealed class HallMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(4.2f, RoofForm.Gable, 2.6f);
        public void Extras(Place place, MeshChunk into) { }
    }

    /// <summary>
    /// A village school: tall windows, tall rooms, and a bell over the gable.
    /// </summary>
    public sealed class SchoolMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(4.0f, RoofForm.Gable, 2.6f);

        public void Extras(Place place, MeshChunk into) =>
            MassingExtras.BellCote(place, Profile(place), into, Materials3D.WallIndex);
    }

    /// <summary>
    /// A church. Tall nave walls and a steep roof, and a tower at the end away from the door.
    ///
    /// Both numbers do the same job. A church is the one building in a village that was built to
    /// be seen from the next parish, and every one of its proportions is about height - so it is
    /// the building that suffered most from everything sharing one 3 m wall and one 2.2 pitch.
    /// </summary>
    public sealed class ChurchMassing : IMassingGrammar
    {
        /// <summary>
        /// 3.0 rather than the 4.5 this started at. St Anne's is 14x16 - very nearly square - and
        /// a 4.5 pitch across sixteen metres is a roof eight metres tall standing on walls of
        /// five and a half. It read as a marquee. The steepness has to be judged against the
        /// SPAN, and a near-square church is the worst case for it.
        /// </summary>
        public Massing Profile(Place place) => new Massing(5.5f, RoofForm.Gable, 3.0f);

        public void Extras(Place place, MeshChunk into)
        {
            var tower = MassingExtras.Tower(place, Profile(place), into, Materials3D.WallIndex);
            MassingExtras.Spire(tower, into, Materials3D.SpireIndex);
        }
    }

    /// <summary>
    /// A mill. Three storeys, a shallow roof, and a hoist over the loading door.
    ///
    /// The bulk IS the signal - a mill is recognisable because it is half again as tall as
    /// anything near it. The shallow pitch is part of that: a steep roof on a tall building
    /// reads as a church, and Rossville already has one of those.
    /// </summary>
    public sealed class MillMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(6.5f, RoofForm.Gable, 1.6f);

        public void Extras(Place place, MeshChunk into) =>
            MassingExtras.Lucam(place, Profile(place), into,
                                Materials3D.RoofingFor(place.Bounds.X, place.Bounds.Y));
    }

    /// <summary>
    /// A light-industrial works. One long single-pitch roof over a tall shed.
    ///
    /// The lean-to is doing the whole job. It was the one roof form nothing in the village used,
    /// and it is the correct one here: a single slope running the length of a building is what
    /// separates a 1971 factory from the seventeenth-century mill half a mile away, which is
    /// steep, gabled and has a hoist sticking out of it. Two industrial buildings that read as
    /// the same building would be worse than either.
    ///
    /// Note this grammar is the ONLY C# the factory cost. The kind itself is one row in
    /// kinds.txt - name, hours, thirty-four jobs, two trades - and the enum has never heard of
    /// it. That was the whole point of Stage 4 and this is the first time anything has tested it
    /// with a kind that employs people.
    /// </summary>
    public sealed class WorksMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(5.5f, RoofForm.LeanTo, 1.6f);
        public void Extras(Place place, MeshChunk into) { }
    }

    /// <summary>
    /// A garage. A flat top over a wide opening, and no domestic roof at all.
    ///
    /// The absence is the signal. Everything else in the village has a pitched roof; the one
    /// building that does not reads as a shed for machinery before you are near enough to see
    /// the forecourt.
    /// </summary>
    public sealed class GarageMassing : IMassingGrammar
    {
        // No chimney. A lock-up garage has nothing to burn, and a stack sitting flat on a flat
        // roof was the one silhouette in the village that read as a mistake rather than a choice.
        public Massing Profile(Place place) => new Massing(3.4f, RoofForm.Flat, 0f, chimneys: false);
        public void Extras(Place place, MeshChunk into) { }
    }
}
