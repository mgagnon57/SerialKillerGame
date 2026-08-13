using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// WHERE PEOPLE WALK, and the three faults that had them walking everywhere else.
    ///
    /// Reported by the owner on 2026-08-11, watching the town from the street: "people are
    /// walking through other people's yards and just wandering out in the road." All three
    /// causes were in the cost table and none of them was visible to any existing test, because
    /// every one of these routes is a legal, successful path - the pathfinder was doing exactly
    /// what it was asked and nothing could see that the asking was wrong.
    ///
    ///   1. A road cost 1.00 and a footpath 1.05, so the asphalt was the cheapest ground in the
    ///      village and the whole town walked down the middle of it.
    ///   2. The five metres of public ground each side of the carriageway was indistinguishable
    ///      from open country, so there was nowhere better to put them.
    ///   3. No tile knew whose lot it was - a yard was anonymous grass at 1.30 against a road's
    ///      1.00 - so cutting the corner won whenever going round was a third further.
    ///
    /// These are gates on the RANKING, not on the numbers. Move a cost and they still pass as
    /// long as a walk still beats a verge, a verge still beats asphalt, and somebody else's lawn
    /// still loses to going round.
    /// </summary>
    public class NobodyWalksThroughYourYardTests
    {
        /// <summary>
        /// '.' open grass   'R' carriageway   'V' verge   'P' laid walk   '#' wall
        /// '1'..'9' open grass that is part of that numbered lot
        /// </summary>
        private static TileGrid Draw(params string[] rows)
        {
            var grid = new TileGrid(rows[0].Length, rows.Length);
            for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                char c = rows[y][x];
                switch (c)
                {
                    case '#': grid.Set(x, y, Terrain.Wall, TileFlags.BlocksSight); break;
                    case 'R': grid.Set(x, y, Terrain.Road, TileGrid.FlagsFor(Terrain.Road)); break;
                    case 'P': grid.Set(x, y, Terrain.Path, TileGrid.FlagsFor(Terrain.Path)); break;
                    case 'V': grid.Set(x, y, Terrain.Grass, TileFlags.Walkable | TileFlags.Verge); break;
                    default:
                        grid.Set(x, y, Terrain.Grass, TileFlags.Walkable);
                        if (c >= '1' && c <= '9') grid.SetLot(x, y, c - '0');
                        break;
                }
            }
            return grid;
        }

        private static List<Tile> Walk(TileGrid grid, Tile from, Tile to)
        {
            var path = new List<Tile>();
            var outcome = new Pathfinder(grid).FindPath(from, to, path);
            Assert.That(outcome, Is.EqualTo(PathOutcome.Found),
                        "the route has to exist at all before anything else here means anything");
            return path;
        }

        // ---- 1. the ranking itself ----

        [Test]
        public void AMadeWalkBeatsTheVergeBeatsTheAsphaltBeatsTheGrass()
        {
            var grid = Draw("PVR.");

            float walk  = grid.MoveCost(0, 0);
            float verge = grid.MoveCost(1, 0);
            float road  = grid.MoveCost(2, 0);
            float grass = grid.MoveCost(3, 0);

            // Written as a chain rather than four constants so that re-pricing the table is a
            // one-line change and reversing it is still caught. Until 2026-08-11 the second of
            // these was the wrong way round and that is the whole bug.
            Assert.That(walk,  Is.LessThan(verge), "a laid sidewalk must beat the verge");
            Assert.That(verge, Is.LessThan(road),  "the verge must beat the carriageway");
            Assert.That(road,  Is.LessThan(grass), "the carriageway must still beat open country");
        }

        // ---- 2. the verge ----

        [Test]
        public void SomebodyWalkingAlongAStreetStaysOffTheAsphalt()
        {
            // A street as the survey lays one: a 3 m carriageway inside a wider easement, so
            // there is public ground either side that is not road.
            var grid = Draw(
                "VVVVVVVVV",
                "RRRRRRRRR",
                "VVVVVVVVV");

            var path = Walk(grid, new Tile(0, 0), new Tile(8, 0));

            foreach (var t in path)
                Assert.That(grid.TerrainAt(t), Is.Not.EqualTo(Terrain.Road),
                            $"walked out onto the carriageway at {t.X},{t.Y}");
        }

        [Test]
        public void TheRoadIsStillThereToBeCrossed()
        {
            // Priced, never barred. Somebody on the far side of the street must still be able to
            // reach the near side, and by the short way.
            var grid = Draw(
                "VVV",
                "RRR",
                "VVV");

            var path = Walk(grid, new Tile(1, 0), new Tile(1, 2));

            Assert.That(path.Count, Is.EqualTo(2), "crossing a one-tile street should take two steps");
        }

        // ---- 3. somebody else's yard ----

        [Test]
        public void GoesRoundAYardRatherThanCuttingTheCorner()
        {
            var grid = Draw(
                ".........",
                "...111...",
                "...111...",
                "...111...",
                ".........");

            var path = Walk(grid, new Tile(0, 2), new Tile(8, 2));

            foreach (var t in path)
                Assert.That(grid.LotAt(t), Is.EqualTo(TileGrid.NoLot),
                            $"cut through lot 1 at {t.X},{t.Y} — going round was open the whole way");
        }

        [Test]
        public void YouMayLeaveYourOwnYardWithoutGoingRoundIt()
        {
            var grid = Draw(
                ".........",
                "...111...",
                "...111...",
                "...111...",
                ".........");

            // Starting INSIDE the lot. The lot a journey begins on is the walker's own by
            // construction, so it costs nothing and the way out is the direct one.
            var path = Walk(grid, new Tile(4, 2), new Tile(8, 2));

            Assert.That(path.Count, Is.EqualTo(4),
                        "walked out of your own front yard the long way round");
        }

        [Test]
        public void AVisitorMayWalkUpToTheDoor()
        {
            var grid = Draw(
                ".........",
                "...111...",
                "...111...",
                "...111...",
                ".........");

            // Ending inside the lot: calling at the house. Entitled to that lot and no other.
            var path = Walk(grid, new Tile(0, 2), new Tile(4, 2));

            Assert.That(path.Count, Is.EqualTo(4), "would not walk up to a door it was sent to");
        }

        [Test]
        public void TheEntitlementIsToTHATLotAndNotToLotsInGeneral()
        {
            // Leaving lot 1 for the public ground on the right, with lot 2 in the way. Being
            // entitled to your own yard must not become being entitled to the neighbour's.
            var grid = Draw(
                ".........",
                "..11.22..",
                "..11.22..",
                "..11.22..",
                ".........");

            var path = Walk(grid, new Tile(2, 2), new Tile(8, 2));

            foreach (var t in path)
                Assert.That(grid.LotAt(t), Is.Not.EqualTo(2),
                            $"crossed the neighbour's lot at {t.X},{t.Y}");
        }

        [Test]
        public void ALongJourneyOverManyLotsStillFindsItsWayHome()
        {
            // THE COST OF MAKING GROUND EXPENSIVE, and the reason farmland is exempt from
            // trespass out in the survey layer.
            //
            // The heuristic is scaled to TileGrid.TypicalMoveCost, 1.3. Every step that actually
            // costs more than that is a step the heuristic underestimates, and an A* whose
            // heuristic is too small stops being A* and becomes Dijkstra - it stops aiming at the
            // goal and starts expanding everything nearby. Far enough down that road and the
            // search hits MaxNodesExamined and returns GaveUp, which is a person who never
            // arrives and never says why.
            //
            // A hundred metres of nothing but other people's back gardens, which is worse than
            // anything the real town can present, and it must still come back Found.
            var grid = new TileGrid(120, 21);
            for (int y = 0; y < 21; y++)
            for (int x = 0; x < 120; x++)
            {
                grid.Set(x, y, Terrain.Grass, TileFlags.Walkable);
                if (y > 0 && y < 20) grid.SetLot(x, y, 1 + x / 12);   // ten lots in a row
            }

            var path = new List<Tile>();
            var finder = new Pathfinder(grid);
            var outcome = finder.FindPath(new Tile(0, 10), new Tile(119, 10), path);

            Assert.That(outcome, Is.EqualTo(PathOutcome.Found),
                        "gave up crossing ten lots — the search is degenerating under trespass");
            Assert.That(path[path.Count - 1], Is.EqualTo(new Tile(119, 10)));
        }

        [Test]
        public void AnEnclosedLotCanStillBeReached()
        {
            // THE REASON TRESPASS IS A COST AND NOT A WALL. Lot 2 is landlocked inside lot 1.
            // A hard block would strand whoever lives there for ever, and a person who cannot
            // path stands still and logs nothing — a far worse fault than a tidy-looking town.
            var grid = Draw(
                ".........",
                ".1111111.",
                ".1122211.",
                ".1111111.",
                ".........");

            var path = Walk(grid, new Tile(0, 0), new Tile(4, 2));

            Assert.That(path, Is.Not.Empty);
            Assert.That(path[path.Count - 1], Is.EqualTo(new Tile(4, 2)));
        }
    }
}
