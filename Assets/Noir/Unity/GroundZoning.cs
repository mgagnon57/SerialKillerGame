using UnityEngine;
using Noir.Core.World;
using Terrain = Noir.Core.World.Terrain;
using Noir.Core.Survey;

namespace Noir.Unity
{
    /// <summary>What a Grass or Field tile actually looks like, once the real parcel standing on
    /// it - and the real ground under it - are both asked.</summary>
    ///
    /// <remarks>
    /// city.txt only ever says GRASS or FIELD - the fictional layer that puts down where the
    /// town is and where the farmland is. Underneath that, Content/parcels.txt is the real
    /// 776-lot Vermilion County survey and Content/elevation.txt is the real USGS ground, and
    /// both disagree with the fiction in exactly the places that make a town look like a town: a
    /// vacant lot two doors from an occupied one, a feed store's gravel yard, a bank too steep
    /// for turf. Road, Path, Water, Floor, Wood and Churchyard are deliberate placements and are
    /// never touched here - only the two tiles that are the fiction's default guess.
    /// </remarks>
    public enum GroundLook { Grass, Field, Hard, Rough, Bank }

    public static class GroundZoning
    {
        /// <summary>
        /// Grade above which a tile reads as bare ground whatever it is zoned. Eastern Illinois
        /// farmland is close to flat - measured against Content/elevation.txt on 2026-08-02, the
        /// real ground across the whole 2,100x2,400 map has a median grade of 1.6%, and only its
        /// steepest tenth of a percent clears 16%. Set low enough to catch the genuine creek
        /// banks and ditch sides the USGS data actually has, and high enough that ordinary
        /// rolling ground - the 90th percentile is under 4% - never trips it. A flat farm town
        /// with a slope map painted over it would be exactly the "debug colour" look this stream
        /// exists to avoid.
        /// </summary>
        private const float BankGrade = 0.12f;

        private static short[] _parcelAt;
        private static int _gridW, _gridH;

        /// <summary>
        /// Which real parcel (if any) a tile's centre falls inside, or -1. Built once per world
        /// size by rasterising each of the 776 parcels over its OWN bounding box, rather than
        /// testing every tile against every parcel - the naive order is 5,040,000 tiles times
        /// 776 parcels, which is billions of point-in-polygon tests on the full map and was the
        /// first thing tried, in a spare script, before this was written. Walking each parcel's
        /// own footprint instead bounds the work by the parcels' total area, which against a
        /// 2,100x2,400 map is a rounding error.
        /// </summary>
        private static void EnsureGrid(WorldModel world)
        {
            if (_parcelAt != null && _gridW == world.Width && _gridH == world.Height) return;

            _gridW = world.Width;
            _gridH = world.Height;
            _parcelAt = new short[_gridW * _gridH];
            for (int i = 0; i < _parcelAt.Length; i++) _parcelAt[i] = -1;

            foreach (var parcel in ParcelIndex.In1991)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(parcel.Bounds.xMin));
                int x1 = Mathf.Min(_gridW - 1, Mathf.CeilToInt(parcel.Bounds.xMax));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(parcel.Bounds.yMin));
                int y1 = Mathf.Min(_gridH - 1, Mathf.CeilToInt(parcel.Bounds.yMax));

                for (int gy = y0; gy <= y1; gy++)
                for (int gx = x0; gx <= x1; gx++)
                {
                    if (!Inside(parcel.Points, gx + 0.5f, gy + 0.5f)) continue;
                    _parcelAt[gy * _gridW + gx] = (short)parcel.Id;
                }
            }
        }

        /// <summary>The same ray-casting point-in-polygon test ParcelIndex uses internally, kept
        /// as its own copy rather than exposed from there - ParcelIndex is locked for this
        /// stream, see the terrain pipeline spec, and this is ten lines against touching it.</summary>
        private static bool Inside(Vector2[] poly, float px, float py)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > py) != (b.y > py) &&
                    px < (b.x - a.x) * (py - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>The real zoning under a tile: the author's note where one exists, the
        /// county's own class code otherwise, Unset where the tile stands on no parcel at all -
        /// a street, an alley, the gap between lots the survey never drew. Mirrors the
        /// "author overrules the county" rule CountyRecord itself documents.</summary>
        private static ParcelNotes.Zoning ZoningAt(WorldModel world, int gx, int gy)
        {
            EnsureGrid(world);
            short pid = _parcelAt[gy * _gridW + gx];
            if (pid < 0) return ParcelNotes.Zoning.Unset;

            // One property, one surface - the third of the three places that make this decision,
            // with VillageMesh.ZoningMask and ZoningPatch.ZoningOf. This one picks the GROUND
            // TEXTURE, so without the redirect the grade school would stand on concrete across
            // two of its lots and grass across the third.
            int id = Rulings.SpokesmanFor(pid);

            var note = ParcelNotes.For(id);
            if (note != null && note.Zoning != ParcelNotes.Zoning.Unset) return note.Zoning;

            return CountyRecord.For(id)?.Zoning ?? ParcelNotes.Zoning.Unset;
        }

        /// <summary>
        /// WHICH PROPERTY A TILE STANDS ON, or -1 for public ground. Exposed for LotsFromSurvey,
        /// which stamps it into the grid so the pathfinder can tell a yard from a field.
        ///
        /// The PROPERTY and not the parcel, through the same <c>Rulings.SpokesmanFor</c> redirect
        /// ZoningAt uses one screen up: the grade school stands on three lots, and a walker who
        /// paid to cross the school field would pay again at every internal lot line. One
        /// property, one piece of ground somebody is or is not entitled to be on.
        ///
        /// Reuses the index EnsureGrid already builds rather than rasterising the parcels a
        /// second time - the naive order here is 5,040,000 tiles against 776 polygons, which is
        /// the billions of tests that comment was written about.
        /// </summary>
        public static int PropertyAt(WorldModel world, int gx, int gy)
        {
            EnsureGrid(world);
            if (gx < 0 || gy < 0 || gx >= _gridW || gy >= _gridH) return -1;
            short pid = _parcelAt[gy * _gridW + gx];
            return pid < 0 ? -1 : Rulings.SpokesmanFor(pid);
        }

        /// <summary>
        /// Is this property the county's farmland? Asked by LotsFromSurvey, which exempts it from
        /// trespass - see that file's header for the measurement behind the decision.
        ///
        /// Takes a property id already resolved through <c>Rulings.SpokesmanFor</c>, which is what
        /// <see cref="PropertyAt"/> hands back, and applies the same author-overrules-the-county
        /// rule as ZoningAt above.
        /// </summary>
        public static bool IsFarmland(int propertyId)
        {
            var note = ParcelNotes.For(propertyId);
            if (note != null && note.Zoning != ParcelNotes.Zoning.Unset)
                return note.Zoning == ParcelNotes.Zoning.Agricultural;

            return CountyRecord.For(propertyId)?.Zoning == ParcelNotes.Zoning.Agricultural;
        }

        /// <summary>Zoning only, no slope test - what a riser's higher side should match, since
        /// SubmeshAt never has that tile's own corner heights to hand. See LookAt for the full
        /// version the flat ground itself uses.</summary>
        public static GroundLook ZoningLookAt(WorldModel world, int gx, int gy, Terrain fallback)
        {
            switch (ZoningAt(world, gx, gy))
            {
                case ParcelNotes.Zoning.Commercial:
                case ParcelNotes.Zoning.Industrial:
                    return GroundLook.Hard;
                case ParcelNotes.Zoning.Vacant:
                    return GroundLook.Rough;
                case ParcelNotes.Zoning.Agricultural:
                    return GroundLook.Field;
                default:   // Residential, Civic, Unset - the fiction's own answer stands
                    return fallback == Terrain.Field ? GroundLook.Field : GroundLook.Grass;
            }
        }

        /// <summary>
        /// What a Grass or Field tile actually looks like. h00..h11 are the SAME four corner
        /// heights BuildGround already sampled through ElevationGrid.HeightAt for the quad's own
        /// vertices, passed in rather than resampled - this costs nothing beyond the lookups
        /// BuildGround was already doing.
        ///
        /// Slope wins over zoning on purpose: a creek bank does not become someone's lawn
        /// because it happens to sit on a residential lot.
        /// </summary>
        public static GroundLook LookAt(WorldModel world, int gx, int gy, Terrain fallback,
                                        float h00, float h10, float h01, float h11)
        {
            // Two edges each way, averaged, over the tile's own one-metre width - cheaper than a
            // second round of ElevationGrid sampling and accurate to within what a 1m quad can
            // curve anyway.
            float dx = (h10 - h00 + h11 - h01) * 0.5f;
            float dy = (h01 - h00 + h11 - h10) * 0.5f;
            if (Mathf.Sqrt(dx * dx + dy * dy) >= BankGrade) return GroundLook.Bank;

            return ZoningLookAt(world, gx, gy, fallback);
        }
    }
}
