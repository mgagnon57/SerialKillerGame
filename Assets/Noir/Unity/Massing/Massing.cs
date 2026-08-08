namespace Noir.Unity
{
    public enum RoofForm
    {
        /// <summary>Slopes on all four sides, meeting at a ridge inset from each end.</summary>
        Hip,

        /// <summary>Two slopes, with a vertical wall closing each end up to the ridge.</summary>
        Gable,

        /// <summary>One slope, from eaves on one side to a higher edge on the other.</summary>
        LeanTo,

        /// <summary>No slope at all.</summary>
        Flat,
    }

    /// <summary>
    /// The shape of a building from outside: how high the walls stand, what the roof does above
    /// them, and which way the ridge runs.
    ///
    /// WHY THIS EXISTS. Every roofed building in the village used to get the identical
    /// AddHipRoof, at the identical 2.2 pitch, on identical 3.0 walls. St Anne's, the mill,
    /// the school and a two-up-two-down were the same box, differing only in footprint and a sign
    /// by the door. Frontage.cs already did good work telling them apart - a hanging board for
    /// the pub, a brass plate for the surgery - but all of it is AT THE FRONT DOOR, and none of
    /// it is in the silhouette. From the overview camera, which is the default view, nothing told
    /// them apart at all. A church reads as a church at two hundred metres because of a tower.
    ///
    /// A plain value with no behaviour, resolved once at build time from the massing grammar.
    /// Nothing here is consulted per frame.
    /// </summary>
    public readonly struct Massing
    {
        /// <summary>Wall height, where the roof starts. Was the global Space3D.WallHeight.</summary>
        public readonly float Eaves;

        public readonly RoofForm Roof;

        /// <summary>
        /// Ridge height above the eaves. Was the global RoofBuilder.Pitch. Zero for a flat roof.
        /// </summary>
        public readonly float Pitch;

        /// <summary>
        /// Run the ridge across the SHORT axis rather than the long one.
        ///
        /// The default follows the long axis, which is what a real roof does and what stops a
        /// row of cottages looking extruded. A building whose entrance is on its long side wants
        /// the other answer, so that the gable faces the street and the door sits under it.
        /// </summary>
        public readonly bool RidgeAcross;

        /// <summary>
        /// Whether this building carries chimney stacks.
        ///
        /// Defaults true, because in 1979 almost everything in a village is heated by something
        /// that needs a flue. The grammar decides rather than a switch on PlaceKind, for the same
        /// reason RoofForm is a column and not an enum test: a kind the enum has never heard of
        /// must be able to answer this from content, and a switch here would quietly close the
        /// open-kind property Stage 4 bought.
        ///
        /// A flat roof with a chimney on it was the defect that made this a field: AddChimneys
        /// ran for every roofed place, so the garage — eaves 3.4, RoofForm.Flat, pitch 0 — got a
        /// stack planted flat on its roof at ridge height, which is 3.4 + 0.
        /// </summary>
        public readonly bool Chimneys;

        public Massing(float eaves, RoofForm roof, float pitch, bool ridgeAcross = false,
                       bool chimneys = true)
        {
            Eaves = eaves;
            Roof = roof;
            Pitch = pitch;
            RidgeAcross = ridgeAcross;
            Chimneys = chimneys;
        }

        /// <summary>
        /// Exactly what every building in the village got before this system existed.
        ///
        /// Houses keep these numbers to the decimal on purpose: adding massing should change the
        /// buildings that were failing to announce themselves, and not move a single cottage.
        /// </summary>
        public static readonly Massing Cottage = new Massing(3.0f, RoofForm.Hip, 2.2f);

        /// <summary>
        /// The same building standing <paramref name="by"/> metres higher.
        ///
        /// Every roof form derives its heights from <see cref="Eaves"/> and <see cref="Pitch"/>
        /// and none of them refers to absolute zero, so raising the eaves raises the whole roof
        /// — ridge, hips, gables and chimneys together — without touching four roof functions.
        /// That is what puts a building on the ground it stands on rather than on the y=0 plane.
        /// </summary>
        public Massing Lifted(float by) =>
            new Massing(Eaves + by, Roof, Pitch, RidgeAcross, Chimneys);
    }
}
