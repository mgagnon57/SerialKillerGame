using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// Stops a lot line being something you can see from a car.
    ///
    /// THE FAULT THIS EXISTS FOR. Every change of ground surface in Rossville was a straight edge
    /// on a one-metre grid, because every tile asked which parcel its own centre stood in and
    /// painted itself accordingly. A cadastral boundary is a legal fiction - it is a line in the
    /// county's book, not a thing standing in the grass - and drawing one as a hard polygon edge
    /// is most of what made the town read as a zoning diagram rather than as land. The vacant
    /// lots are the loudest case: 118 of them, each a rank-grass rectangle with corners, sitting
    /// in mown lawn.
    ///
    /// WHAT IT DOES INSTEAD, AND WHY THIS SHAPE. Rather than blending two materials - which needs
    /// a shader that reads vertex colour or a splat map, and this project's ground is a baked
    /// mesh sorted into a submesh per look - it moves the QUESTION. A tile asks what the ground
    /// is like a few metres from where it stands, along a smooth wandering offset, so the answer
    /// changes over on a curve of its own rather than on the survey's straight line. The two
    /// surfaces interlock in fingers a few metres deep. No new material, no new draw call, no
    /// shader: the same submeshes, allocated slightly differently.
    ///
    /// It is deliberately NOT white noise per tile. A per-tile coin flip gives a one-metre
    /// stipple, which at eye height reads as television static and at altitude reads as a dithered
    /// line - both worse than the straight edge they replaced. Two octaves of value noise,
    /// bilinearly interpolated, give an edge that wanders like a real boundary between mown grass
    /// and a field left to itself.
    ///
    /// DETERMINISM. Every offset comes from <see cref="Materials3D.Scatter"/>, the same position
    /// hash four other systems key on, so the same land comes out the same every run. Nothing here
    /// touches IRng and nothing here may - see the standing rule about not converting a position
    /// hash into a substream.
    ///
    /// WHAT IT MUST NOT SOFTEN, and does not: a kerb, a floor, the creek, and a poured concrete
    /// apron all have real straight edges, and a wandering one would be a worse lie than the
    /// diagram. The caller decides that; see VillageMesh, where only Grass, Field and Rough are
    /// allowed to swap.
    /// </summary>
    public static class GroundBlend
    {
        /// <summary>Off puts every boundary back on the survey line, for comparing the two.</summary>
        public static bool Enabled = true;

        /// <summary>
        /// The wander, in metres, and the size of the features it wanders in.
        ///
        /// THESE ARE LOOKED-AT NUMBERS, NOT DERIVED ONES, and the constraint that sets them is
        /// the town lot: Rossville's are roughly 20 m wide by 40 m deep. An amplitude near half a
        /// lot width would swallow whole patches and move a vacant lot onto its neighbour's
        /// garden; an amplitude under a metre is invisible at any distance you would look at the
        /// town from. Three metres total - about ten feet - is enough that no eye reads the
        /// boundary as ruled, and small enough that a patch still sits where the county put it.
        ///
        /// The coarse octave does the wandering; the fine one keeps the wander from looking like
        /// a drawn curve.
        /// </summary>
        private const int CoarseCell = 12;
        private const float CoarseAmp = 2.2f;
        private const int FineCell = 4;
        private const float FineAmp = 0.8f;

        /// <summary>
        /// The fray, and it is the octave that makes the difference between a wandering boundary
        /// and a staircase of rectangles.
        ///
        /// MEASURED BY LOOKING AT IT, 2026-08-10. With the two smooth octaves alone the edge does
        /// move off the survey line - but a smooth offset ROUNDED TO WHOLE METRES sits still for
        /// several metres and then jumps, so the boundary came out as blocky right-angled
        /// plateaus: better than a ruled line, and still plainly drawn rather than grown. One
        /// hash per tile at just under a metre breaks every plateau into a one-metre fringe,
        /// which is what the edge of mown grass against rank growth actually looks like.
        ///
        /// This one IS white noise, deliberately, and it is safe to be: it rides on top of two
        /// coherent octaves that have already decided where the boundary goes, so it frays an
        /// edge instead of dissolving one.
        /// </summary>
        private const float FrayAmp = 0.9f;

        private static float[] _coarseX, _coarseY, _fineX, _fineY;
        private static int _coarseW, _coarseH, _fineW, _fineH;
        private static int _forWidth, _forHeight;

        /// <summary>
        /// Build the offset field for a map of this size. Cheap - four grids of a few hundred
        /// thousand floats between them - and rebuilt only when the map size changes, which is
        /// once.
        /// </summary>
        public static void Prepare(int width, int height)
        {
            if (_coarseX != null && _forWidth == width && _forHeight == height) return;

            _forWidth = width;
            _forHeight = height;

            _coarseW = width / CoarseCell + 2;
            _coarseH = height / CoarseCell + 2;
            _fineW = width / FineCell + 2;
            _fineH = height / FineCell + 2;

            _coarseX = Field(_coarseW, _coarseH, 9161);
            _coarseY = Field(_coarseW, _coarseH, 9173);
            _fineX = Field(_fineW, _fineH, 9181);
            _fineY = Field(_fineW, _fineH, 9187);
        }

        /// <summary>One hash, as a number in [-1, 1]. The one place that conversion is written.</summary>
        private static float Unit(int x, int y, int salt) =>
            (Materials3D.Scatter(x, y, salt) & 0xFFFF) / 32767.5f - 1f;

        /// <summary>One grid of hash values in [-1, 1], one per noise cell corner.</summary>
        private static float[] Field(int w, int h, int salt)
        {
            var v = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                v[y * w + x] = Unit(x, y, salt);
            return v;
        }

        /// <summary>Bilinear sample of one octave, in cells rather than metres.</summary>
        private static float Sample(float[] v, int w, int h, int gx, int gy, int cell)
        {
            float fx = (float)gx / cell, fy = (float)gy / cell;
            int x0 = (int)fx, y0 = (int)fy;
            float tx = fx - x0, ty = fy - y0;

            int x1 = Mathf.Min(x0 + 1, w - 1);
            int y1 = Mathf.Min(y0 + 1, h - 1);
            x0 = Mathf.Min(x0, w - 1);
            y0 = Mathf.Min(y0, h - 1);

            float a = Mathf.Lerp(v[y0 * w + x0], v[y0 * w + x1], tx);
            float b = Mathf.Lerp(v[y1 * w + x0], v[y1 * w + x1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        /// <summary>
        /// Where this tile should ASK what the ground is like, which is a few metres from where it
        /// stands. Clamped to the map, so a tile on the edge asks somewhere real.
        ///
        /// Returns the tile's own coordinates untouched when the blend is off, so the caller needs
        /// no second code path and the two can be compared by flipping one bool.
        /// </summary>
        public static void AskAt(int gx, int gy, int width, int height, out int ax, out int ay)
        {
            if (!Enabled || _coarseX == null) { ax = gx; ay = gy; return; }

            float dx = Sample(_coarseX, _coarseW, _coarseH, gx, gy, CoarseCell) * CoarseAmp
                     + Sample(_fineX, _fineW, _fineH, gx, gy, FineCell) * FineAmp
                     + Unit(gx, gy, 9199) * FrayAmp;
            float dy = Sample(_coarseY, _coarseW, _coarseH, gx, gy, CoarseCell) * CoarseAmp
                     + Sample(_fineY, _fineW, _fineH, gx, gy, FineCell) * FineAmp
                     + Unit(gx, gy, 9209) * FrayAmp;

            ax = Mathf.Clamp(gx + Mathf.RoundToInt(dx), 0, width - 1);
            ay = Mathf.Clamp(gy + Mathf.RoundToInt(dy), 0, height - 1);
        }

        /// <summary>
        /// Which looks are allowed to trade places: the ones that are living ground and have no
        /// edge of their own.
        ///
        /// Hard is a poured apron and Bank is a verdict about this tile's OWN slope - the first
        /// has a real straight edge, the second would be a lie about where the ground falls away.
        /// Both keep what they were.
        /// </summary>
        public static bool Soft(GroundLook look) =>
            look == GroundLook.Grass || look == GroundLook.Field || look == GroundLook.Rough;
    }
}
