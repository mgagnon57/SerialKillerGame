using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Sim
{
    /// <summary>
    /// Which tiles can reach which, worked out once instead of discovered the hard way.
    ///
    /// A* answers "is there a route" by looking for one, and when there is none it can only
    /// find that out by exhausting everything reachable. On Rossville that is 1.26 million
    /// nodes and 415 milliseconds, paid EVERY time somebody whose door is walled off tries
    /// again - which is what the town's stutter turned out to be.
    ///
    /// Flood filling the walkable space once turns that question into two array lookups. A
    /// journey between two regions is refused in O(1) and correctly reported as
    /// <see cref="PathOutcome.NoRouteExists"/> rather than <see cref="PathOutcome.GaveUp"/>,
    /// which are different facts that the search alone cannot tell apart: one means never ask
    /// again, the other means the map has outgrown the budget.
    ///
    /// The connectivity here MUST match the search's exactly, corner rule included. A fill that
    /// is more generous than the search would let a path be attempted that cannot be walked -
    /// harmless, just slow. A fill that is more mean would refuse a journey that is perfectly
    /// possible, and the person who could not set off would have no way to tell you why.
    /// </summary>
    public sealed class WalkableRegions
    {
        /// <summary>A tile nobody can stand on belongs to no region.</summary>
        public const int None = -1;

        private static readonly int[] Dx = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] Dy = { -1, 0, 1, 0, -1, 1, 1, -1 };

        private readonly int[] _region;
        private readonly List<int> _sizes = new List<int>();
        private readonly int _width, _height;

        /// <summary>The grid revision this was built from. Stale above that.</summary>
        public int BuiltAtRevision { get; }

        /// <summary>How many separate walkable areas the map has. One is a healthy town.</summary>
        public int RegionCount => _sizes.Count;

        public WalkableRegions(TileGrid grid)
        {
            _width = grid.Width;
            _height = grid.Height;
            BuiltAtRevision = grid.WalkabilityRevision;

            int count = grid.Count;
            _region = new int[count];
            for (int i = 0; i < count; i++) _region[i] = None;

            // An explicit stack rather than recursion. A region can be most of the map, and a
            // recursive fill over a million tiles overflows the stack on the first big town.
            var stack = new int[count];

            for (int seed = 0; seed < count; seed++)
            {
                if (_region[seed] != None) continue;
                if (!grid.IsWalkable(seed % _width, seed / _width)) continue;

                int id = _sizes.Count;
                int size = 0;
                int top = 0;
                stack[top++] = seed;
                _region[seed] = id;

                while (top > 0)
                {
                    int current = stack[--top];
                    size++;

                    int cx = current % _width;
                    int cy = current / _width;

                    for (int d = 0; d < 8; d++)
                    {
                        int nx = cx + Dx[d];
                        int ny = cy + Dy[d];
                        if (nx < 0 || ny < 0 || nx >= _width || ny >= _height) continue;
                        if (!grid.IsWalkable(nx, ny)) continue;

                        // The search will not cut a corner between two walls, so neither does
                        // this. Both orthogonal neighbours have to be open for the diagonal to
                        // count, exactly as in Pathfinder.FindPath.
                        if (d >= 4 && (!grid.IsWalkable(cx + Dx[d], cy)
                                       || !grid.IsWalkable(cx, cy + Dy[d]))) continue;

                        int n = ny * _width + nx;
                        if (_region[n] != None) continue;
                        _region[n] = id;
                        stack[top++] = n;
                    }
                }

                _sizes.Add(size);
            }
        }

        /// <summary>Which region a tile belongs to, or <see cref="None"/> if it is not walkable.</summary>
        public int RegionAt(int x, int y) =>
            x < 0 || y < 0 || x >= _width || y >= _height ? None : _region[y * _width + x];

        /// <summary>Which region a tile belongs to, or <see cref="None"/> if it is not walkable.</summary>
        public int RegionAt(Tile t) => RegionAt(t.X, t.Y);

        /// <summary>How many tiles are in a region.</summary>
        public int SizeOf(int region) =>
            region < 0 || region >= _sizes.Count ? 0 : _sizes[region];

        /// <summary>
        /// Whether a walk between these two could exist at all.
        ///
        /// False for an unwalkable endpoint as well as for two genuinely separated ones, because
        /// both answer the caller's real question the same way: do not go looking.
        /// </summary>
        public bool Connected(Tile a, Tile b)
        {
            int ra = RegionAt(a);
            return ra != None && ra == RegionAt(b);
        }

        /// <summary>The biggest region, which on a healthy map is the town and everything in it.</summary>
        public int Largest()
        {
            int best = None, bestSize = 0;
            for (int i = 0; i < _sizes.Count; i++)
                if (_sizes[i] > bestSize) { bestSize = _sizes[i]; best = i; }
            return best;
        }
    }
}
