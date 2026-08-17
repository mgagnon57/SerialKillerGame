using System;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// Real-world latitude/longitude to map tile, calibrated by anchors.
    ///
    /// THE FRAME IS THE TRUTH AND THE ANCHORS ARE LOCAL CORRECTIONS. The map is
    /// 1 tile = 1 metre, +x east, +y south, origin fixed at the Chicago St x Attica St
    /// crossing: real (40.3793, -87.66897) == tile (750, 1335), the same constants as
    /// tools/relay-rossville.py. That frame's scale is confirmed by the county block
    /// survey, so this class never rescales it.
    ///
    /// WHY THIS IS NOT A TWO-ANCHOR AFFINE, WHICH IS WHAT WAS FIRST ASKED FOR. Solving
    /// an exact per-axis affine through the two anchors below forces scales of 0.565
    /// east-west and 1.304 north-south - a map compressed 44% one way and stretched 30%
    /// the other, which the block survey flatly contradicts. The 101 Perry residual is
    /// mostly not map drift at all: Google's geocode pin for that address stands in the
    /// estate's FRONT LAWN, ~45m north-east of the mansion centroid it was paired with
    /// (measured against the satellite, 2026-08-16). A global fit smears that local
    /// error over the whole town; a business 500m east of Route 1 would land ~218m
    /// short. So instead: convert through the frame at true scale, then add each
    /// anchor's measured residual weighted by 1/distance-squared against a fixed prior
    /// weight for the frame itself. At an anchor the correction is exact; a couple of
    /// CorrectionRadius away it has died back to the pure frame.
    ///
    /// ADDING ANCHORS IS THE WHOLE MAINTENANCE STORY: every placement verified against
    /// the street (the owner-model pipeline does this routinely) can add a row pairing
    /// the real coordinates with the map tile. Prefer anchors where the real-world
    /// point is unambiguous - a street crossing, a building centroid read off the
    /// satellite - over bare geocoder pins, which is the lesson the Perry row teaches.
    /// </summary>
    public static class GeoAnchors
    {
        const double OriginLat = 40.3793;
        const double OriginLng = -87.66897;
        const double OriginX = 750.0;
        const double OriginY = 1335.0;

        const double MetresPerDegLat = 111132.0;
        static readonly double MetresPerDegLng =
            111320.0 * Math.Cos(OriginLat * Math.PI / 180.0);   // ~84,790 at this latitude

        /// <summary>
        /// How far an anchor's influence reaches, in metres: an anchor at exactly this
        /// distance contributes half its residual, at ~1.7x the distance about a tenth.
        /// 150m is under a block - a residual verified at one address vouches for its
        /// own neighbourhood, not the town. Measured with the two anchors below: a
        /// downtown point 125m from the crossing catches under 4m of the Perry
        /// correction, while the estate's own block still gets over 99% of it.
        /// </summary>
        const double CorrectionRadius = 150.0;

        struct Anchor
        {
            public string Name;
            public double Lat, Lng;    // real world
            public double X, Y;        // map tile the real point verifiably occupies
        }

        // Verified pairings. Residuals against the frame are computed, not stated, so a
        // corrected origin constant can never disagree with its own anchor row.
        static readonly Anchor[] Anchors =
        {
            new Anchor { Name = "Chicago x Attica crossing",
                         Lat = 40.3793,    Lng = -87.66897,   X = 750.0, Y = 1335.0 },
            // Geocoder pin for the address, paired with the mansion centroid the town
            // builds at 782..808 x 1617..1630. The pin itself stands in the front lawn
            // (see class header), so treat this row's residual as covering the
            // Perry/Gilbert neighbourhood, not as gospel about the mansion.
            new Anchor { Name = "101 Perry St",
                         Lat = 40.3773085, Lng = -87.6680301, X = 795.0, Y = 1623.5 },
        };

        /// <summary>The frame transform alone - true scale, no anchor correction.</summary>
        static void Frame(double lat, double lng, out double x, out double y)
        {
            x = OriginX + (lng - OriginLng) * MetresPerDegLng;
            y = OriginY + (OriginLat - lat) * MetresPerDegLat;
        }

        /// <summary>Real-world coordinates to fractional map tile.</summary>
        public static void LatLngToTileD(double lat, double lng, out double x, out double y)
        {
            Frame(lat, lng, out x, out y);

            // Inverse-fourth-power blend of anchor residuals against a constant prior
            // weight standing in for the frame itself. d -> 0 makes an anchor's weight
            // explode, so AT an anchor the mapping reproduces its tile exactly; far
            // from every anchor the prior wins and the residual dies off as 1/d^4.
            // Fourth power rather than square so the correction stays LOCAL - with
            // 1/d^2 the Perry residual still reached downtown at quarter strength.
            double r2 = CorrectionRadius * CorrectionRadius;
            double priorW = 1.0 / (r2 * r2);
            double wSum = priorW, rx = 0.0, ry = 0.0;
            for (int i = 0; i < Anchors.Length; i++)
            {
                Frame(Anchors[i].Lat, Anchors[i].Lng, out double ax, out double ay);
                double dx = x - ax, dy = y - ay;
                double d2 = dx * dx + dy * dy;
                double w = 1.0 / (d2 * d2 + 1e-9);
                rx += w * (Anchors[i].X - ax);
                ry += w * (Anchors[i].Y - ay);
                wSum += w;
            }
            x += rx / wSum;
            y += ry / wSum;
        }

        /// <summary>
        /// Real-world coordinates to the nearest map tile. No range check - callers
        /// mapping points outside the 2100x2400 map get out-of-range tiles and should
        /// test with <see cref="OnMap"/>.
        /// </summary>
        public static Vector2Int LatLngToTile(double lat, double lng)
        {
            LatLngToTileD(lat, lng, out double x, out double y);
            return new Vector2Int(
                (int)Math.Round(x, MidpointRounding.AwayFromZero),
                (int)Math.Round(y, MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// Map tile back to real-world coordinates - the inverse of
        /// <see cref="LatLngToTileD"/>, solved by iteration because the anchor blend has
        /// no closed-form inverse. The correction field varies over hundreds of metres,
        /// so a handful of Newton steps lands well under a millimetre.
        /// </summary>
        public static void TileToLatLng(double x, double y, out double lat, out double lng)
        {
            lat = OriginLat - (y - OriginY) / MetresPerDegLat;
            lng = OriginLng + (x - OriginX) / MetresPerDegLng;
            for (int i = 0; i < 4; i++)
            {
                LatLngToTileD(lat, lng, out double px, out double py);
                lat += (py - y) / MetresPerDegLat;
                lng -= (px - x) / MetresPerDegLng;
            }
        }

        public static bool OnMap(Vector2Int tile) =>
            tile.x >= 0 && tile.x < 2100 && tile.y >= 0 && tile.y < 2400;
    }
}
