using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// Nails the staircase rule a belt drag lays - the one statement <c>BuildController.LayPath</c> walks
    /// and <c>Sim.Route</c> asserts against. Before this module existed the harness carried a copy of the
    /// walk and the two could drift silently; these tests are the drift alarm, pinning the shape the
    /// player sees: dominant axis first, X wins a tie, the end cell faces the way it was entered.
    /// </summary>
    public class BeltWalkTests
    {
        [Test]
        public void StraightRun_EveryCellPointsDownTheLine_IncludingTheEnd()
        {
            List<BeltWalk.Step> steps = BeltWalk.Predict(new Int2(1, 1), new Int2(4, 1));

            Assert.AreEqual(4, steps.Count);
            Assert.AreEqual(new Int2(1, 1), steps[0].Cell); Assert.AreEqual(Dir.East, steps[0].Direction);
            Assert.AreEqual(new Int2(2, 1), steps[1].Cell); Assert.AreEqual(Dir.East, steps[1].Direction);
            Assert.AreEqual(new Int2(3, 1), steps[2].Cell); Assert.AreEqual(Dir.East, steps[2].Direction);
            Assert.AreEqual(new Int2(4, 1), steps[3].Cell); Assert.AreEqual(Dir.East, steps[3].Direction);
        }

        [Test]
        public void Diagonal_TakesTheDominantAxisFirst()
        {
            // |dy| = 5 > |dx| = 2, so the run leaves the press cell going north and puts its sideways
            // legs at the far end.
            List<BeltWalk.Step> steps = BeltWalk.Predict(new Int2(0, 0), new Int2(2, 5));

            Assert.AreEqual(8, steps.Count, "Manhattan distance + the end cell");
            for (int i = 0; i <= 5; i++)
                Assert.AreEqual(new Int2(0, i), steps[i].Cell, "north leg cell " + i);
            Assert.AreEqual(new Int2(1, 5), steps[6].Cell);
            Assert.AreEqual(new Int2(2, 5), steps[7].Cell);
            Assert.AreEqual(Dir.North, steps[4].Direction);   // (0,4) still stepping up
            Assert.AreEqual(Dir.East, steps[5].Direction);    // (0,5): the corner - the sideways tail starts here
            Assert.AreEqual(Dir.East, steps[6].Direction);    // (1,5)
            Assert.AreEqual(Dir.East, steps[7].Direction);    // (2,5): the end, entered from the west
        }

        [Test]
        public void Diagonal_TieGoesToXFirst()
        {
            // Equal spans: X wins the tie exactly as the machine-aim rule does.
            List<BeltWalk.Step> steps = BeltWalk.Predict(new Int2(3, 3), new Int2(0, 6));

            Assert.AreEqual(7, steps.Count);
            Assert.AreEqual(new Int2(3, 3), steps[0].Cell); Assert.AreEqual(Dir.West, steps[0].Direction);
            Assert.AreEqual(new Int2(1, 3), steps[2].Cell); Assert.AreEqual(Dir.West, steps[2].Direction);
            Assert.AreEqual(new Int2(0, 3), steps[3].Cell); Assert.AreEqual(Dir.North, steps[3].Direction);
            Assert.AreEqual(new Int2(0, 6), steps[6].Cell); Assert.AreEqual(Dir.North, steps[6].Direction);
        }

        [Test]
        public void ZeroLengthDrag_PredictsNoStaircase()
        {
            // A plain click is the release-time placement, not a walk - the drag's own code says so.
            Assert.AreEqual(0, BeltWalk.Predict(new Int2(5, 5), new Int2(5, 5)).Count);
        }

        [Test]
        public void SingleStep_RunIsContiguous_AndEndsWhereTheDragWent()
        {
            // The structural property behind every case above: each step delivers into the next, and the
            // walk ends on the target cell.
            Int2[] corners =
            {
                new Int2(0, 0), new Int2(7, 0), new Int2(0, 7), new Int2(7, 7), new Int2(3, 4),
            };

            foreach (Int2 from in corners)
            {
                foreach (Int2 to in corners)
                {
                    List<BeltWalk.Step> steps = BeltWalk.Predict(from, to);

                    for (int i = 0; i + 1 < steps.Count; i++)
                        Assert.AreEqual(steps[i + 1].Cell, steps[i].Cell + steps[i].Direction.Offset(),
                            "staircase from " + from + " to " + to + " breaks at step " + i);

                    Assert.AreEqual(from == to ? 0 : Manhattan(from, to) + 1, steps.Count,
                        "cell count from " + from + " to " + to);
                    if (steps.Count > 0) Assert.AreEqual(to, steps[steps.Count - 1].Cell);
                }
            }
        }

        private static int Manhattan(Int2 a, Int2 b)
            => System.Math.Abs(b.X - a.X) + System.Math.Abs(b.Y - a.Y);
    }
}
