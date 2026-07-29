using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Sim
{
    /// <summary>
    /// Grid A*, 8-directional, weighted by terrain.
    ///
    /// Unity still has no first-party 2D navmesh, and at village scale a plain grid A* is both
    /// simpler and faster than any of the alternatives. One instance owns its scratch buffers
    /// and reuses them, so a query allocates nothing — which matters in a game, where a garbage
    /// collection pause is a visible stutter rather than a background cost.
    ///
    /// The "visit stamp" trick avoids clearing arrays between queries: instead of resetting
    /// 14,400 entries every time, each entry records which query last touched it.
    /// </summary>
    public sealed class Pathfinder
    {
        private readonly TileGrid _grid;
        private readonly int _width, _height, _count;

        private readonly float[] _gScore;
        private readonly int[] _cameFrom;
        private readonly int[] _stamp;
        private readonly bool[] _closed;
        private int _currentStamp;

        private int[] _heap;      // indices into the grid
        private float[] _heapF;   // f-score parallel to _heap
        private int _heapCount;

        /// <summary>
        /// Guard against pathological searches, as a fraction of the map rather than a constant.
        ///
        /// A fixed number is a cap on a village and a corruption switch on a town: the same
        /// 20,000 that no Ashcombe journey could reach is a quarter of the searches on a map
        /// four times the size, and every one of those is somebody who does not set off. Tying
        /// it to the grid keeps the guard doing the job it was written for at any size.
        ///
        /// The old constant survives as the FLOOR, not the ceiling. A quarter of a small map is
        /// a smaller allowance than the village has always had — 5,100 on Ashcombe — and
        /// tightening the guard while fixing what it broke would have been a poor trade.
        /// </summary>
        public readonly int MaxNodesExamined;

        /// <summary>What the game shipped with, and what nothing on a village-sized map has ever
        /// needed more than.</summary>
        public const int MinimumNodeCap = 20_000;

        /// <summary>
        /// Deliberate overweighting of the heuristic, which trades route quality for search cost.
        ///
        /// At 1.0 A* is admissible and returns the true shortest path; above it, the search is
        /// pulled towards the goal and expands far fewer nodes for a route that may be a few
        /// per cent longer. Measured on the synthetic grids, 1.35 takes nodes examined per tile
        /// of path from about 16 to about 7. Nobody watching a village can tell that a man took
        /// the next lane over, and everybody would notice him never leaving the house.
        /// </summary>
        public const float DefaultHeuristicWeight = 1.35f;

        public readonly float HeuristicWeight;

        public int LastNodesExamined { get; private set; }

        /// <param name="maxNodesExamined">Zero, the normal case, scales the cap with the map.</param>
        public Pathfinder(TileGrid grid, int maxNodesExamined = 0,
                          float heuristicWeight = DefaultHeuristicWeight)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _width = grid.Width;
            _height = grid.Height;
            _count = grid.Count;

            MaxNodesExamined = maxNodesExamined > 0
                ? maxNodesExamined
                : Math.Max(_count / 4, MinimumNodeCap);
            HeuristicWeight = heuristicWeight;

            _gScore = new float[_count];
            _cameFrom = new int[_count];
            _stamp = new int[_count];
            _closed = new bool[_count];
            _heap = new int[_count + 1];
            _heapF = new float[_count + 1];
        }

        private static readonly int[] Dx = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] Dy = { -1, 0, 1, 0, -1, 1, 1, -1 };
        private const float Straight = 1.0f;
        private const float Diagonal = 1.41421356f;

        /// <summary>
        /// Find a walkable path. On success `result` holds the tiles from just after `from`
        /// through `to` inclusive.
        /// </summary>
        public PathOutcome FindPath(Tile from, Tile to, List<Tile> result)
        {
            result.Clear();
            LastNodesExamined = 0;

            if (!_grid.InBounds(from) || !_grid.InBounds(to)) return PathOutcome.NoRouteExists;
            if (!_grid.IsWalkable(from) || !_grid.IsWalkable(to)) return PathOutcome.NoRouteExists;
            if (from == to) return PathOutcome.Found;

            NextStamp();
            _heapCount = 0;

            int startIdx = _grid.Index(from);
            int goalIdx = _grid.Index(to);

            Touch(startIdx);
            _gScore[startIdx] = 0f;
            _cameFrom[startIdx] = -1;
            HeapPush(startIdx, Heuristic(from, to));

            while (_heapCount > 0)
            {
                int current = HeapPop();
                if (current == goalIdx) { Reconstruct(current, result); return PathOutcome.Found; }

                if (_closed[current]) continue;
                _closed[current] = true;

                if (++LastNodesExamined > MaxNodesExamined) return PathOutcome.GaveUp;

                int cx = current % _width;
                int cy = current / _width;

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Dx[d];
                    int ny = cy + Dy[d];
                    if (nx < 0 || ny < 0 || nx >= _width || ny >= _height) continue;
                    if (!_grid.IsWalkable(nx, ny)) continue;

                    bool diagonal = d >= 4;
                    // Don't let anyone cut the corner between two walls — it looks wrong and
                    // lets people slip through doorframes diagonally.
                    if (diagonal && (!_grid.IsWalkable(cx + Dx[d], cy) || !_grid.IsWalkable(cx, cy + Dy[d])))
                        continue;

                    int nIdx = ny * _width + nx;
                    if (_closed[nIdx] && _stamp[nIdx] == _currentStamp) continue;

                    float step = (diagonal ? Diagonal : Straight) * _grid.MoveCost(nx, ny);
                    float tentative = _gScore[current] + step;

                    if (_stamp[nIdx] != _currentStamp)
                    {
                        Touch(nIdx);
                    }
                    else if (tentative >= _gScore[nIdx])
                    {
                        continue;
                    }

                    _gScore[nIdx] = tentative;
                    _cameFrom[nIdx] = current;
                    HeapPush(nIdx, tentative + Heuristic(new Tile(nx, ny), to));
                }
            }

            return PathOutcome.NoRouteExists;
        }

        /// <summary>Find a walkable path, ignoring why it failed. Kept for callers that only
        /// need to know whether anything came back.</summary>
        public bool TryFindPath(Tile from, Tile to, List<Tile> result) =>
            FindPath(from, to, result) == PathOutcome.Found;

        /// <summary>
        /// Begin a new query.
        ///
        /// The stamp stands in for clearing three arrays per query, so it must never repeat: on
        /// wrap, every stale entry in the grid would suddenly look like it belonged to this
        /// search, and the search would read g-scores left over from whenever that tile was
        /// last crossed. Two thousand million sounds unreachable and is not — at the
        /// fast-forward budget of six thousand ticks a second it is about eight hours of
        /// cruising, which is an afternoon of somebody skipping through a month.
        /// </summary>
        private void NextStamp()
        {
            if (_currentStamp == int.MaxValue)
            {
                Array.Clear(_stamp, 0, _stamp.Length);
                _currentStamp = 0;
            }
            _currentStamp++;
        }

        private void Touch(int idx)
        {
            _stamp[idx] = _currentStamp;
            _gScore[idx] = float.MaxValue;
            _cameFrom[idx] = -1;
            _closed[idx] = false;
        }

        /// <summary>Octile distance — the exact cost of moving on an 8-way grid with no
        /// obstacles — scaled by <see cref="HeuristicWeight"/>.</summary>
        private float Heuristic(Tile a, Tile b)
        {
            int dx = a.X - b.X; if (dx < 0) dx = -dx;
            int dy = a.Y - b.Y; if (dy < 0) dy = -dy;
            int min = dx < dy ? dx : dy;
            int max = dx < dy ? dy : dx;
            return ((max - min) * Straight + min * Diagonal) * HeuristicWeight;
        }

        private void Reconstruct(int goal, List<Tile> result)
        {
            int node = goal;
            while (node >= 0 && _cameFrom[node] >= 0)
            {
                result.Add(new Tile(node % _width, node / _width));
                node = _cameFrom[node];
            }
            result.Reverse();
        }

        // ---- binary min-heap (netstandard2.1 has no PriorityQueue) ----

        private void HeapPush(int index, float f)
        {
            // A tile is pushed again every time a cheaper way into it turns up, so the heap can
            // legitimately hold more entries than the grid has tiles. It only ever grows once,
            // on the worst query the map has, and every query after that reuses the buffer.
            if (_heapCount + 1 >= _heap.Length)
            {
                Array.Resize(ref _heap, _heap.Length * 2);
                Array.Resize(ref _heapF, _heapF.Length * 2);
            }

            int i = ++_heapCount;
            _heap[i] = index;
            _heapF[i] = f;
            while (i > 1)
            {
                int parent = i >> 1;
                if (_heapF[parent] <= _heapF[i]) break;
                Swap(parent, i);
                i = parent;
            }
        }

        private int HeapPop()
        {
            int top = _heap[1];
            _heap[1] = _heap[_heapCount];
            _heapF[1] = _heapF[_heapCount];
            _heapCount--;

            int i = 1;
            while (true)
            {
                int left = i << 1, right = left + 1, smallest = i;
                if (left <= _heapCount && _heapF[left] < _heapF[smallest]) smallest = left;
                if (right <= _heapCount && _heapF[right] < _heapF[smallest]) smallest = right;
                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }
            return top;
        }

        private void Swap(int a, int b)
        {
            (_heap[a], _heap[b]) = (_heap[b], _heap[a]);
            (_heapF[a], _heapF[b]) = (_heapF[b], _heapF[a]);
        }
    }
}
