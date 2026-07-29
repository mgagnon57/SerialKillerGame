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
            new Massing(place.Kind == PlaceKind.Surgery ? 3.4f : 3.6f, RoofForm.Hip, 2.0f);

        public void Extras(Place place, MeshChunk into) { }
    }

    /// <summary>
    /// A pub. Gable ends and a heavy stack.
    ///
    /// The gable is doing the work: a public house is usually the oldest and most stubbornly
    /// shaped building on a village street, and an end-on gable to the road is what says so.
    /// </summary>
    public sealed class PubMassing : IMassingGrammar
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
            MassingExtras.BellCote(place, Profile(place), into, Materials3D.ChimneyIndex);
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
        public Massing Profile(Place place) => new Massing(5.5f, RoofForm.Gable, 4.5f);

        public void Extras(Place place, MeshChunk into)
        {
            var tower = MassingExtras.Tower(place, Profile(place), into, Materials3D.ChimneyIndex);
            MassingExtras.Spire(tower, into, Materials3D.ChimneyIndex);
        }
    }

    /// <summary>
    /// A mill. Three storeys, a shallow roof, and a hoist over the loading door.
    ///
    /// The bulk IS the signal - a mill is recognisable because it is half again as tall as
    /// anything near it. The shallow pitch is part of that: a steep roof on a tall building
    /// reads as a church, and Ashcombe already has one of those.
    /// </summary>
    public sealed class MillMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(6.5f, RoofForm.Gable, 1.6f);

        public void Extras(Place place, MeshChunk into) =>
            MassingExtras.Lucam(place, Profile(place), into,
                                Materials3D.RoofingFor(place.Bounds.X, place.Bounds.Y));
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
        public Massing Profile(Place place) => new Massing(3.4f, RoofForm.Flat, 0f);
        public void Extras(Place place, MeshChunk into) { }
    }
}
