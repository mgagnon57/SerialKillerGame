using System.Collections.Generic;
using UnityEngine;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// The brush's own math: which delta cells a stroke touches, and which mesh vertices have to
    /// move because of it. No SceneView, no EditorWindow, no mouse - SculptTerrainWindow is the
    /// only caller, and everything here works from plain world coordinates so it can be checked
    /// headlessly against a hand-built mesh (see SculptCheck.CheckBrushPaint).
    /// </summary>
    public static class SculptBrush
    {
        /// <summary>Which cached Ground chunks a brush centred at (cx, cy) could possibly touch -
        /// including the one-cell margin DeltaStep beyond the brush radius, since a cell's
        /// bilinear neighbours can move a vertex that stands outside the radius itself.</summary>
        public static IEnumerable<MeshFilter> OverlappingChunks(float cx, float cy, float radius,
            IReadOnlyDictionary<(int col, int row), MeshFilter> chunkCache)
        {
            float reach = Reach(radius);
            int colFrom = Mathf.FloorToInt((cx - reach) / MeshChunks.Size);
            int colTo = Mathf.FloorToInt((cx + reach) / MeshChunks.Size);
            int rowFrom = Mathf.FloorToInt((cy - reach) / MeshChunks.Size);
            int rowTo = Mathf.FloorToInt((cy + reach) / MeshChunks.Size);

            for (int row = rowFrom; row <= rowTo; row++)
            for (int col = colFrom; col <= colTo; col++)
                if (chunkCache.TryGetValue((col, row), out var mf) && mf != null)
                    yield return mf;
        }

        /// <summary>
        /// One brush application: nudges every delta cell within `radius` of (cx, cy) by
        /// `strength`, weighted by a smooth radial falloff, then patches every vertex of every
        /// given chunk whose (x, y) could have moved - live, without rebuilding anything.
        ///
        /// Costed in vertex fetches, not vertex count: Mesh.vertices copies the whole array on
        /// every call, so each chunk is read once (for the "before" heights) and written once
        /// (after mutating the grid), never per vertex.
        /// </summary>
        public static void Apply(float cx, float cy, float radius, float strength, bool invert,
            IEnumerable<MeshFilter> overlappingChunks)
        {
            float signedStrength = strength * (invert ? -1f : 1f);
            float reach = Reach(radius);

            var verts = new Dictionary<MeshFilter, Vector3[]>();
            var before = new Dictionary<MeshFilter, float[]>();

            foreach (var mf in overlappingChunks)
            {
                var v = mf.sharedMesh.vertices;
                var b = new float[v.Length];
                for (int i = 0; i < v.Length; i++)
                    if (InReach(v[i], cx, cy, reach)) b[i] = ElevationGrid.HeightAt(v[i].x, -v[i].z);
                verts[mf] = v;
                before[mf] = b;
            }

            PaintCells(cx, cy, radius, signedStrength);

            foreach (var entry in verts)
            {
                var mf = entry.Key;
                var v = entry.Value;
                var b = before[mf];
                bool touched = false;

                for (int i = 0; i < v.Length; i++)
                {
                    if (!InReach(v[i], cx, cy, reach)) continue;
                    float after = ElevationGrid.HeightAt(v[i].x, -v[i].z);
                    v[i].y += after - b[i];
                    touched = true;
                }

                if (!touched) continue;
                mf.sharedMesh.vertices = v;
                mf.sharedMesh.RecalculateNormals();
                mf.sharedMesh.RecalculateBounds();
            }
        }

        private static bool InReach(Vector3 vertex, float cx, float cy, float reach)
        {
            float dx = vertex.x - cx, dy = -vertex.z - cy;
            return dx * dx + dy * dy <= reach * reach;
        }

        /// <summary>
        /// How far a change can reach beyond the brush radius itself: a painted cell's bilinear
        /// influence covers a DeltaStep-wide SQUARE around it (it moves any vertex whose grid
        /// cell has that cell as one of its four corners), so the worst case - a vertex sitting
        /// in that square's own diagonal corner - is DeltaStep * sqrt(2) away, not DeltaStep.
        /// Using the plain 1x margin here left a thin sliver of vertices at the brush's diagonal
        /// edge unpatched until the next full rebuild (caught in Task 3's review).
        /// </summary>
        private static float Reach(float radius) => radius + ElevationGrid.DeltaStep * 1.41421356f;

        /// <summary>Nudges every delta cell within `radius` of (cx, cy). A smooth falloff -
        /// full strength at the centre, none at the rim - so a wide brush blends into its
        /// neighbours instead of stepping at the edge.</summary>
        private static void PaintCells(float cx, float cy, float radius, float signedStrength)
        {
            int step = ElevationGrid.DeltaStep;
            int colFrom = Mathf.Max(0, Mathf.FloorToInt((cx - radius) / step));
            int colTo = Mathf.Min(ElevationGrid.DeltaCols - 1, Mathf.CeilToInt((cx + radius) / step));
            int rowFrom = Mathf.Max(0, Mathf.FloorToInt((cy - radius) / step));
            int rowTo = Mathf.Min(ElevationGrid.DeltaRows - 1, Mathf.CeilToInt((cy + radius) / step));

            for (int row = rowFrom; row <= rowTo; row++)
            for (int col = colFrom; col <= colTo; col++)
            {
                float dx = col * step - cx, dy = row * step - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > radius) continue;

                float t = 1f - Mathf.SmoothStep(0f, 1f, dist / radius);
                ElevationGrid.SetDeltaCell(col, row, ElevationGrid.GetDeltaCell(col, row) + signedStrength * t);
            }
        }
    }
}
