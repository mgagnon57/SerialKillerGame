using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// How tall a downtown building is, and what it is made of, from where it stands.
    ///
    /// The commercial counterpart to <see cref="HouseLayers"/>, and it consumes the same research
    /// in the same way: <see cref="CommercialRow"/> in Core holds the rule measured off the 1913
    /// Sanborn sheets — forty units transcribed across all four faces of Attica × Chicago, each
    /// with the storey count and frontage the surveyor wrote under it — and this asks it where a
    /// particular building sits.
    ///
    /// THE RULE IS ONE LINE: height and width decay with distance from the crossing. The corner
    /// carries two-storey units of 22–26 ft with halls, offices, a dentist and a law office
    /// upstairs; walk away in any direction and the buildings drop to one storey, narrow to
    /// 14–18 ft, the trades get heavier and dirtier, and at the far end the brick stops and the
    /// row finishes in frame. That gradient is land value written in brick.
    ///
    /// WIDTH IS NOT APPLIED HERE. `CommercialRow.Lay` can divide a frontage into units of the
    /// surveyed sizes, but the buildings in city.txt are authored with their own footprints, and
    /// re-cutting somebody's authored lot from underneath them is not this file's business. What
    /// it does is decide **height and material**, which is what reads from the street and what
    /// was most wrong: a 1,200-person Illinois town had four-storey city blocks on it.
    /// </summary>
    public static class CommercialLayers
    {
        /// <summary>
        /// A storey, floor to floor, on Main Street. Taller than a house's: a shopfront needs
        /// head-room for its glass and the floor over it is offices or a lodge hall.
        /// </summary>
        public const float Storey = 3.4f;

        /// <summary>
        /// The false front. A one-storey Main Street building carries its facade above its own
        /// roof to square off the elevation, and it is the single most recognisable thing about
        /// an American commercial row — without it a one-storey shop reads as a shed.
        /// </summary>
        public const float Parapet = 1.1f;

        /// <summary>
        /// How far along the row this building stands, measured the way the surveyor measured.
        ///
        /// NOT centre-of-crossroads to centre-of-building. The Sanborn frontages are distances
        /// ALONG THE ROW from the building line at the corner, and a straight centre-to-centre
        /// reading adds two things the survey never counted: half the width of the crossing
        /// itself (Chicago and Attica are both 30 m, so 15 m of carriageway) and half the
        /// building's own depth.
        ///
        /// That mattered. Measured centre to centre, the nearest building downtown came out at
        /// 41.6 m against a two-storey reach of 35, so not one building in Rossville qualified as
        /// two storeys and the row had no anchor at its corner at all - which is the one thing
        /// every face of the real row has.
        /// </summary>
        public static float DistanceOf(Place place)
        {
            if (place == null || !HouseLayers.Installed) return 0f;

            var b = place.Bounds;
            float x = b.X + b.W * 0.5f - HouseLayers.Centre.x;
            float y = b.Y + b.H * 0.5f - HouseLayers.Centre.y;
            float centres = Mathf.Sqrt(x * x + y * y);

            // Back off the carriageway and the building's own half-depth, so what is left is the
            // frontage distance the sheet records.
            float own = Mathf.Min(b.W, b.H) * 0.5f;
            return Mathf.Max(0f, centres - HouseLayers.CrossingReach - own);
        }

        /// <summary>
        /// One or two. Two within <see cref="CommercialRow.TwoStoreyReach"/> of the crossing —
        /// which is the middle of four measurements off the sheet, 28/34/35/43 m — and one
        /// beyond it. Nothing downtown was ever three.
        /// </summary>
        public static int StoreysOf(Place place) =>
            DistanceOf(place) < CommercialRow.TwoStoreyReach ? 2 : 1;

        /// <summary>
        /// Brick until the brick gives out. On the west side of Chicago the terrace runs 179 ft
        /// of brick and then finishes in a frame barber's shop, and that is the one face whose
        /// end falls inside the sheet.
        /// </summary>
        public static Construction ConstructionOf(Place place) =>
            DistanceOf(place) < CommercialRow.BrickReach ? Construction.Brick : Construction.Frame;

        /// <summary>Wall height including the parapet, which is what the massing wants.</summary>
        public static float EavesOf(Place place) => StoreysOf(place) * Storey + Parapet;
    }

    /// <summary>
    /// A Main Street commercial block: flat roof, parapet front, party walls.
    ///
    /// NOT A HIP ROOF. The grammar this replaces gave every shop a 3.6 m hip, which is an English
    /// village shop and reads completely wrong on an Illinois main street. What the Sanborn sheet
    /// draws is a continuous terrace of flat-roofed brick blocks whose front walls run up past
    /// the roof — party walls, no gaps, no variation in setback, the variation is all in width
    /// and height.
    ///
    /// No chimneys: a commercial block is heated from a basement boiler with one flue at the
    /// back, not a stack per bay, and a row of them along a parapet reads as housing.
    /// </summary>
    public sealed class MainStreetMassing : IMassingGrammar
    {
        public Massing Profile(Place place) =>
            new Massing(CommercialLayers.EavesOf(place), RoofForm.Flat, 0f, false, chimneys: false);

        public void Extras(Place place, MeshChunk into) { }
    }
}
