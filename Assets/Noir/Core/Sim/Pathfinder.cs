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
        /// 20,000 that no Rossville journey could reach is a quarter of the searches on a map
        /// four times the size, and every one of those is somebody who does not set off. Tying
        /// it to the grid keeps the guard doing the job it was written for at any size.
        ///
        /// The old constant survives as the FLOOR, not the ceiling. A quarter of a small map is
        /// a smaller allowance than the village has always had — 5,100 on Rossville — and
        /// tightening the guard while fixing what it broke would have been a poor trade.
        /// </summary>
        public readonly int MaxNodesExamined;

        /// <summary>What the game shipped with, and what nothing on a village-sized map has ever
        /// needed more than.</summary>
        public const int MinimumNodeCap = 20_000;

        /// <summary>
        /// The most any ONE search may examine, whatever the map's size.
        ///
        /// Scaling the guard with the map fixed a real bug and introduced a worse one, because
        /// the cap was only ever reasoned about in tiles. On Rossville a quarter of the grid was
        /// 5,100 nodes; Rossville's grid is 5,040,000 tiles, so the same rule produced 1,260,000
        /// - and a node costs 329 nanoseconds, measured. That is 415 MILLISECONDS inside a
        /// single tick, from a single search, which is what the town's stutter turned out to be.
        ///
        /// Neither PathBudgetPerTick nor PathNodeBudgetPerTick could ever have caught it: both
        /// are consulted BEFORE a search starts and never once it is running, so one search is
        /// free to spend twenty-one times the whole tick's node budget on its own.
        ///
        /// 40,000 was the first attempt and it was far too mean: 73% of every search in the town
        /// abandoned, forty-four people unable to reach the burial ground because it sits at the
        /// west edge and nothing had measured how far a real journey reaches. A ceiling below
        /// what honest journeys need does not bound a cost, it deletes the journeys - which is
        /// the fault the old map-sized cap was written to avoid, arrived at from the other side.
        ///
        /// It can afford to be generous now. The region map takes the impossible journeys first
        /// and answers them in O(1), so nothing reaches this ceiling by exhausting a walled-off
        /// map any more; what reaches it is a long walk that is genuinely findable. Sized from
        /// Simulation.WorstNodesFound - the most any search that SUCCEEDED had to look at - with
        /// room over the top, rather than from what sounded affordable.
        ///
        /// Measured at the weight this actually ships with: the worst journey Rossville has ever
        /// SUCCESSFULLY found took 56,015 nodes, and no search in a twenty thousand frame run
        /// reached this ceiling at all. Not much more than a factor of two over the observed
        /// worst, which is deliberate - it is about 36 ms for a search that is going to fail
        /// anyway, and a ceiling that can never be hit is not a guard.
        ///
        /// The number is weight-dependent and must be re-measured with it. At 1.35 the worst
        /// successful journey was 19,418 nodes; at 1.15 it was 99,267 and a fifth of the town
        /// could not travel. Quoting a headroom figure from one weight while shipping another is
        /// how the 40,000 above came to be wrong.
        ///
        /// ---- RAISED 100,000 -> 300,000 ON 2026-08-11, AND THE GUARD WAS COSTING SIXTEEN TIMES
        ///      WHAT IT SAVED ----
        ///
        /// Everything above was measured on 2026-08-04, when `Content/roads.txt` DID NOT EXIST.
        /// That is the pre-survey town of city.txt's 37 ruled roads; 44 commits then rebuilt the
        /// walkable grid under it - the surveyed 68-road network, the alleys reaching town, every
        /// building reseated on its measured footprint, the doors recut. Nothing re-measured this
        /// number against the town that came out, and the header's own rule says it must be.
        ///
        /// What the stale ceiling was doing, measured over two sim hours from 06:00:
        ///
        ///                        searches asked   found   gave up   sim cost
        ///     ceiling 100,000              781     389       171    13,970 ms
        ///     no ceiling at all            616     395         0     8,355 ms
        ///
        /// ONLY SEVEN JOURNEYS IN THE TOWN GENUINELY NEED MORE THAN 100,000 NODES, and the worst
        /// of them needs 184,986. But a refused journey is retried on a backoff, and a capped
        /// search spends the WHOLE allowance before it can report failure - so seven honest walks
        /// became 171 refusals at 100,000 nodes each. Seventeen million nodes burnt to avoid
        /// paying about one million. Lifting the guard made the simulation 40% FASTER, which is
        /// the exact opposite of what a cost guard is for.
        ///
        /// Every one of the refused journeys was long - nearest 750 tiles, mean 1,194, and NOT
        /// ONE under 200. The search was never unhealthy; short journeys all succeeded. What
        /// they were mostly trying to reach is the old burial ground at 69,609, hard against the
        /// west edge, which is the SAME place named four paragraphs above as the thing a mean
        /// ceiling strands people from. It is half a mile from the town centre.
        ///
        /// 300,000 is 1.62x the observed worst of 184,986, near the 1.79x this number carried
        /// when it was 100,000 against 56,015. Re-measure it the next time the walkable grid
        /// moves - `[smoke] paths` prints the whole table now, and `NoJourneyIsRefusedThatCanBe
        /// Walked` in SmokeTest fails the run if this drifts again.
        /// </summary>
        public const int HardNodeCeiling = 300_000;

        /// <summary>
        /// Deliberate overweighting of the heuristic, which trades route quality for search cost.
        ///
        /// At 1.0 A* is admissible and returns the true shortest path; above it, the search is
        /// pulled towards the goal and expands far fewer nodes for a route that may be a few
        /// per cent longer. Measured on the synthetic grids, 1.35 takes nodes examined per tile
        /// of path from about 16 to about 7. Nobody watching a village can tell that a man took
        /// the next lane over, and everybody would notice him never leaving the house.
        ///
        /// THE WEIGHT ONLY MEANS THAT AGAINST THE COST OF THE GROUND. Those synthetic grids cost
        /// 1.0 a tile; the real map is mostly grass at 1.3, so a heuristic of octile x 1.35 was
        /// working out at an effective 1.038 out in the country - unweighted A* in all but name,
        /// flooding outwards precisely where the journeys are longest. One walk to the burial
        /// ground examined 350,278 nodes. Hence the TypicalMoveCost factor in Heuristic: the
        /// weight is applied to what a step really costs, not to what it cost on a test fixture.
        ///
        /// 1.30 rather than 1.35, chosen against the watched ratio rather than by taste. Once
        /// the scaling was corrected, 1.35 was greedy enough to cost route quality: the tenth
        /// percentile of the watched ratio fell to 0.39 against a recorded floor of 0.40, which
        /// is the ratchet in TwoToOneTests doing exactly what it was built for. 1.15 bought that
        /// back and far too much else with it - 99,267 nodes on the worst journey against
        /// 19,418, and 22% of every search in the town refused. 1.30 clears the floor and keeps
        /// the speed. The floor itself was not touched; a number moved instead.
        /// </summary>
        public const float DefaultHeuristicWeight = 1.30f;

        public readonly float HeuristicWeight;

        public int LastNodesExamined { get; private set; }

        private WalkableRegions _regions;

        /// <summary>
        /// The map of which tiles can reach which, built on demand and rebuilt when the grid's
        /// walkable shape changes underneath it.
        ///
        /// Lazy rather than built in the constructor because the builder makes a Pathfinder and
        /// then keeps carving doorways, and a region map from before the doors were cut would
        /// refuse every journey through one. Keyed on the grid's own revision so it cannot go
        /// quietly out of date - the failure mode of a stale map is a town where nobody can
        /// travel and nothing says why.
        /// </summary>
        public WalkableRegions Regions
        {
            get
            {
                if (_regions == null || _regions.BuiltAtRevision != _grid.WalkabilityRevision)
                    _regions = new WalkableRegions(_grid);
                return _regions;
            }
        }

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
                : Math.Min(Math.Max(_count / 4, MinimumNodeCap), HardNodeCeiling);
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
        /// What a step across a lot you have no business on is multiplied by. See the use site.
        ///
        /// 4.0 rather than a bar, and it is a DETOUR RATIO rather than a feeling: at 4x a walker
        /// accepts the short-cut only when the lawful way round is more than four times the
        /// distance. It stays a cost so that the pathfinder can never fail to find a route
        /// because of it, which matters more here than the number does — a person who cannot
        /// path stands still for ever and nothing in the log says why.
        ///
        /// MAKING THIS SMALLER MAKES THE SEARCH SLOWER, WHICH IS THE OPPOSITE OF THE GUESS.
        /// Measured 2026-08-11 by TrespassSearchCostDiagnostic, 200 journeys over a gridded
        /// synthetic town, against the same journeys with no lots stamped at all:
        ///
        ///     Trespass 2.0   3.26x nodes   1.86x time
        ///     Trespass 4.0   2.35x nodes   1.39x time
        ///     Trespass 8.0   2.24x nodes   1.36x time
        ///
        /// At 2.0 a yard is still cheap enough to be worth considering, so A* expands the yards
        /// AND the roads before it settles; at 4.0 it writes the yards off on sight and follows
        /// the road corridor, which is a far narrower search. The curve is flat past 4.0, so this
        /// is the cheap end of it and lowering it to be "gentler" would cost speed and buy
        /// nothing. Do not tune this number downwards without re-running that diagnostic.
        /// </summary>
        private const float Trespass = 4.0f;

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

            // Two array lookups instead of exhausting the map. Somebody whose destination is
            // walled off used to cost a full 415 ms search on EVERY retry, for ever, because a
            // search can only prove a route absent by trying everything. Now it costs nothing,
            // and it says NoRouteExists - which the caller is entitled to treat as final.
            if (!Regions.Connected(from, to)) return PathOutcome.NoRouteExists;

            NextStamp();
            _heapCount = 0;

            int startIdx = _grid.Index(from);
            int goalIdx = _grid.Index(to);

            // THE TWO LOTS THIS JOURNEY IS ENTITLED TO CROSS: the one it starts on and the one it
            // ends on. Everything else somebody owns costs Trespass.
            //
            // Read off the ENDPOINTS rather than off the walker, and that is the whole reason
            // this needs no new argument, no per-person plumbing and no change to Simulation. A
            // person leaving home starts on their own lot; a person calling at a house ends on
            // that one; a caller at the door of a house they do not live in is entitled to walk
            // up the path, and is entitled to exactly that lot and no other. The rule falls out
            // of the geometry instead of having to be maintained against it.
            int freeLotA = _grid.LotAt(from);
            int freeLotB = _grid.LotAt(to);

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

                    // SOMEBODY ELSE'S YARD. Priced, not barred: a short-cut across a corner is a
                    // thing people really do, and a hard block would strand anyone whose lot is
                    // enclosed and send the rest on absurd detours. At Trespass a walker goes
                    // round unless going round is more than about four times further, which is
                    // rare on a gridded town and common in the one place it should be — the gap
                    // between a back door and the alley behind it.
                    int lot = _grid.LotAt(nx, ny);
                    if (lot != TileGrid.NoLot && lot != freeLotA && lot != freeLotB)
                        step *= Trespass;

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
        /// <summary>
        /// Route the way a boy on a bike does: pick up the back lane, run it, and come off it
        /// near the far end. Falls back to the ordinary path when there is no alley worth the
        /// detour, so a caller can always ask and never has to check first.
        ///
        /// WHY THIS IS A WAYPOINT AND NOT A CHEAPER TILE. The obvious way to express a
        /// preference is to make an alley step cost less and let A* find it, and it does not
        /// work here - measurably. This search runs on a heuristic scaled to
        /// <see cref="TileGrid.TypicalMoveCost"/>, which already overestimates a road step, so
        /// it is deliberately greedy: probed over a 127-tile journey it examined exactly 127
        /// nodes, which is to say it walked straight at the goal and never considered an
        /// alternative at all. A cost multiplier changed nothing until the alley was practically
        /// underfoot. Making the heuristic admissible instead would have made every existing
        /// route in the game a different route, to fix something that is not a routing failure.
        ///
        /// And the waypoint is the truer model anyway. A cheaper tile says the boy went down the
        /// alley because it saved him something. He did not. He went because it is the alley -
        /// he decides, and then he goes. So: choose one near the middle of the journey, path to
        /// it, path on from it.
        ///
        /// <paramref name="maxDetour"/> is how much longer than the direct route he will accept.
        /// Past that the lane is not on his way in any useful sense and he stays on the street.
        /// </summary>
        public PathOutcome FindPathViaAlley(Tile from, Tile to, List<Tile> result,
                                            int searchRadius = 45, float maxDetour = 1.8f)
        {
            result.Clear();
            if (!_grid.InBounds(from) || !_grid.InBounds(to)) return PathOutcome.NoRouteExists;

            var direct = new List<Tile>();
            var straight = FindPath(from, to, direct);
            if (straight != PathOutcome.Found)
            {
                result.AddRange(direct);
                return straight;
            }

            var midpoint = new Tile((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            if (!TryFindAlleyNear(midpoint, searchRadius, out var lane) ||
                _grid.IsAlley(from) || _grid.IsAlley(to))
            {
                result.AddRange(direct);
                return PathOutcome.Found;
            }

            var first = new List<Tile>();
            var second = new List<Tile>();
            if (FindPath(from, lane, first) != PathOutcome.Found ||
                FindPath(lane, to, second) != PathOutcome.Found ||
                first.Count + second.Count > direct.Count * maxDetour)
            {
                result.AddRange(direct);
                return PathOutcome.Found;
            }

            result.AddRange(first);
            result.AddRange(second);
            return PathOutcome.Found;
        }

        /// <summary>
        /// The nearest walkable alley tile to <paramref name="near"/>, within
        /// <paramref name="radius"/>. Searched in growing rings so the first hit is the nearest,
        /// and bounded by the radius so a town with no alley near this journey costs a box scan
        /// rather than a map sweep.
        /// </summary>
        public bool TryFindAlleyNear(Tile near, int radius, out Tile found)
        {
            found = near;
            if (_grid.IsAlley(near) && _grid.IsWalkable(near)) return true;

            for (int r = 1; r <= radius; r++)
            {
                for (int d = -r; d <= r; d++)
                {
                    if (Hit(near.X + d, near.Y - r, ref found)) return true;
                    if (Hit(near.X + d, near.Y + r, ref found)) return true;
                    if (Hit(near.X - r, near.Y + d, ref found)) return true;
                    if (Hit(near.X + r, near.Y + d, ref found)) return true;
                }
            }
            return false;
        }

        private bool Hit(int x, int y, ref Tile found)
        {
            if (!_grid.IsAlley(x, y) || !_grid.IsWalkable(x, y)) return false;
            found = new Tile(x, y);
            return true;
        }

        private float Heuristic(Tile a, Tile b)
        {
            int dx = a.X - b.X; if (dx < 0) dx = -dx;
            int dy = a.Y - b.Y; if (dy < 0) dy = -dy;
            int min = dx < dy ? dx : dy;
            int max = dx < dy ? dy : dx;
            return ((max - min) * Straight + min * Diagonal)
                   * TileGrid.TypicalMoveCost * HeuristicWeight;
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
