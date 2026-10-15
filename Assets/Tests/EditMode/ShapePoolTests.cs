using System;
using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The producible set: the closure of a map's minerals under the operators it has unlocked. This is
    /// the guardrail the whole unlock ladder hangs on (§5.3, §7) - the wave generator draws its demands
    /// from nothing else - so what is asserted here is that the closure is <em>right</em>: it is exactly
    /// what the operators can make, its size is bounded by the algebra, and its depth is the number of
    /// operator steps a player actually has to spend.
    ///
    /// The pools below are built from hand-written mineral lists rather than read from a shipped map, for
    /// the same reason every other test rig here is: a shipped map is designer data, and asserting its
    /// contents would test today's file instead of the closure.
    /// </summary>
    public class ShapePoolTests
    {
        private static readonly Quad[] CircleOnly = { Quad.Circle };
        private static readonly Quad[] BothMinerals = { Quad.Circle, Quad.Square };

        [Test]
        public void CuttersAndCircleOre_ProduceTheNineCircleItems_AndNothingElse()
        {
            // The first rung of the ladder (§7): map 1 mines circles and hands over the cutter. One full
            // item, four halves (two cut lines) and four quarters (a half cut again) - nine, and there is
            // no way past nine, because a cut only ever empties quadrants and never fills them.
            var pool = new ShapePool(CircleOnly, Operators.Cutter);

            Assert.AreEqual(9, pool.Shapes.Count, "the full circle, four halves and four quarters");
            Assert.AreEqual(2, pool.MaxDepth, "a full item is mined; a half is one cut; a quarter is two");

            Assert.IsTrue(pool.CanProduce(Sim.Circle));
            Assert.IsTrue(pool.CanProduce(Sim.TopHalfCircle));
            Assert.IsTrue(pool.CanProduce(Sim.BottomHalfCircle));
            Assert.IsTrue(pool.CanProduce(Sim.LeftHalfCircle));
            Assert.IsTrue(pool.CanProduce(Sim.RightHalfCircle));
            foreach (int q in new[] { Shape.NW, Shape.NE, Shape.SE, Shape.SW })
                Assert.IsTrue(pool.CanProduce(Shape.Quarter(q, Quad.Circle)),
                    "the " + Shape.CornerCode(q) + " quarter comes out of a half");

            // Nothing square, because there is no square ore: the guardrail's other half.
            foreach (Shape shape in ShapePool.All)
            {
                if (!shape.Contains(Quad.Square)) continue;
                Assert.IsFalse(pool.CanProduce(shape), shape + " carries a mineral this map does not have");
            }

            // And nothing deeper than a quarter: a cutter cannot put two quadrants of different origin
            // together, so the three-quarter and diagonal items are out of reach without a stacker.
            Assert.IsFalse(pool.CanProduce(Shape.Of(Quad.Circle, Quad.Circle, Quad.Circle, Quad.Empty)),
                "a three-quarter item needs a stacker to build");
            Assert.IsFalse(pool.CanProduce(Shape.Of(Quad.Circle, Quad.Empty, Quad.Circle, Quad.Empty)),
                "and so does a diagonal pair, even of the one mineral");
        }

        [Test]
        public void WithoutAStacker_EveryProducibleItemIsPure()
        {
            // The stacker is what makes mixed items - and the cutter and the rotator cannot fake it: every
            // piece a cut produces is one mineral throughout, and turning one keeps it that way. With both
            // minerals and no stacker the pool is therefore eighteen *pure* items and no mixed ones, which
            // is the rule the generator leans on when it refuses to brief a mixed round on map 2 (§7).
            var pool = new ShapePool(BothMinerals, Operators.Cutter | Operators.Rotator);

            Assert.AreEqual(18, pool.Shapes.Count, "two minerals' worth of fulls, halves and quarters");
            Assert.AreEqual(2, pool.MaxDepth);

            foreach (Shape shape in pool.Shapes)
            {
                Assert.IsTrue(shape.IsPure(Quad.Circle) || shape.IsPure(Quad.Square),
                    shape + " is mixed, and a map without a stacker cannot build it");
            }

            Assert.IsFalse(pool.CanProduce(Sim.Composite), "the mixed round needs a stacker");
            Assert.IsFalse(pool.CanProduce(Shape.Of(Quad.Circle, Quad.Empty, Quad.Square, Quad.Empty)),
                "and so does the simplest mixed item there is - two quarters, two minerals");
        }

        [Test]
        public void BothMineralsAndEveryOperator_ProduceExactlyTheEighty()
        {
            // The far end of the ladder: map 3 and beyond unlock everything, so the pool is the whole state
            // space - not "about" eighty. A bug in the closure shows up here as 79 or 255, which is exactly
            // how the algebra's overlap test first gave itself away.
            var pool = new ShapePool(BothMinerals, Operators.All);

            Assert.AreEqual(80, pool.Shapes.Count, "the closure of two minerals under all three operators");
            Assert.AreEqual(ShapePool.All.Count, pool.Shapes.Count, "which is the whole item space");
            Assert.AreEqual(5, pool.MaxDepth, "the deepest round takes five operator steps");

            foreach (Shape shape in ShapePool.All)
                Assert.IsTrue(pool.CanProduce(shape), shape + " is in the state space but not in the closure");
        }

        [Test]
        public void EveryPool_StaysInsideTheStateSpace_AtEverySize()
        {
            // The invariant the overlap bug broke, asserted for every pool this file builds rather than
            // only the biggest: an item outside the eighty, or a quadrant that is not one of the three, is
            // a shape no drill can mine, no sorter can name and no wave can legally demand.
            Quad[][] mineralLists = { CircleOnly, BothMinerals, new[] { Quad.Square }, new Quad[0] };
            Operators[] ladders =
            {
                Operators.None, Operators.Cutter, Operators.Rotator, Operators.Stacker,
                Operators.Cutter | Operators.Rotator, Operators.Cutter | Operators.Stacker,
                Operators.Rotator | Operators.Stacker, Operators.All,
            };

            var seen = AllCodes();
            foreach (Quad[] minerals in mineralLists)
            foreach (Operators unlocked in ladders)
            {
                var pool = new ShapePool(minerals, unlocked);
                string at = " (minerals " + minerals.Length + ", " + unlocked + ")";

                Assert.LessOrEqual(pool.Shapes.Count, 80, "more items than exist" + at);

                var codes = new HashSet<uint>();
                foreach (Shape shape in pool.Shapes)
                {
                    Assert.IsTrue(seen.Contains(shape.Code), shape + " is not one of the eighty" + at);
                    Assert.IsTrue(codes.Add(shape.Code), shape + " appears twice in the pool" + at);
                    Assert.IsFalse(shape.IsEmpty, "the empty item is not producible" + at);

                    for (int q = 0; q < Shape.QuadrantCount; q++)
                    {
                        Quad content = shape.At(q);
                        Assert.IsTrue(content == Quad.Empty || content == Quad.Circle || content == Quad.Square,
                            shape + " holds a quadrant that does not exist" + at);
                    }
                }
            }
        }

        [Test]
        public void DepthIsTheOperatorStepCount_AndEveryProducibleItemHasOne()
        {
            // Depth is the progression currency (§4.2): "the deeper the demanded shape, the tougher and
            // slower that group". It has to be a property of the algebra - the number of steps that make
            // the item - and not a tier table somebody keeps by hand, so it is asserted as one: a mined
            // item is zero, each operator edge costs one, and every item the pool holds is at the depth the
            // pool says and in that tier.
            var pool = new ShapePool(BothMinerals, Operators.All);

            Assert.AreEqual(0, pool.Depth(Sim.Circle), "a full item is what a drill puts on the belt");
            Assert.AreEqual(0, pool.Depth(Sim.Square));
            Assert.AreEqual(1, pool.Depth(Sim.TopHalfCircle), "one cut");
            Assert.AreEqual(1, pool.Depth(Sim.LeftHalfSquare), "one cut, the other line");
            Assert.AreEqual(2, pool.Depth(Shape.Quarter(Shape.NW, Quad.Circle)), "a half cut again");

            var tierMembers = new HashSet<uint>();
            for (int depth = 0; depth <= pool.MaxDepth; depth++)
            {
                IReadOnlyList<Shape> tier = pool.AtDepth(depth);
                Assert.IsNotEmpty(tier, "depth " + depth + " is within the pool's reach, so it has members");

                foreach (Shape shape in tier)
                {
                    Assert.AreEqual(depth, pool.Depth(shape), shape + " is filed under the wrong depth");
                    Assert.IsTrue(tierMembers.Add(shape.Code), shape + " appears in two tiers");
                }
            }

            Assert.AreEqual(pool.Shapes.Count, tierMembers.Count,
                "every producible item is in exactly one tier, and the tiers are the pool");

            Assert.IsEmpty(pool.AtDepth(-1), "there is no tier below the minerals");
            Assert.IsEmpty(pool.AtDepth(pool.MaxDepth + 1), "nor past the deepest item");
            Assert.IsEmpty(pool.AtDepth(99), "and asking absurdly far out is empty, not a throw");
        }

        [Test]
        public void AnItemTheMapCannotMake_HasNoDepth()
        {
            var pool = new ShapePool(CircleOnly, Operators.Cutter);

            Assert.AreEqual(-1, pool.Depth(Sim.Square), "this map has no square ore at all");
            Assert.AreEqual(-1, pool.Depth(Sim.Composite), "and no stacker to make a mixed round");
            Assert.AreEqual(-1, pool.Depth(Shape.None), "and nothing is not an item");
            Assert.IsFalse(pool.CanProduce(Shape.None), "nothing is nobody's demand");
        }

        [Test]
        public void TheRotator_AddsNoItemsByItself_ItOnlySaysWhatTheCutterAlreadyMade()
        {
            // Worth pinning because it is easy to assume the opposite: turning a full item does nothing
            // (it is four-fold symmetric), and turning a piece a cut made only produces pieces the cut
            // already produced. The rotator's value is *repair* - getting a piece the right way up - and
            // logistics, not new items; what makes new items is the stacker.
            var cut = new ShapePool(CircleOnly, Operators.Cutter);
            var cutAndTurn = new ShapePool(CircleOnly, Operators.Cutter | Operators.Rotator);
            var turnOnly = new ShapePool(CircleOnly, Operators.Rotator);

            Assert.AreEqual(cut.Shapes.Count, cutAndTurn.Shapes.Count,
                "with a cutter in the map, the rotator reaches nothing new");
            Assert.AreEqual(cut.MaxDepth, cutAndTurn.MaxDepth);

            Assert.AreEqual(1, turnOnly.Shapes.Count, "and on its own it can only hold what was mined");
            Assert.AreEqual(0, turnOnly.MaxDepth, "because a full item is the same whichever way up it is");

            // Every turned piece is itself in the pool, which is what "the rotator is a repair" means
            // concretely: the two halves of one cut line are both reachable, one directly and one by turning.
            foreach (Shape shape in cutAndTurn.Shapes)
                Assert.IsTrue(cutAndTurn.CanProduce(shape.RotatedCW()),
                    "turning " + shape + " produced something the map cannot make");
        }

        [Test]
        public void TheMinerals_AreTheRoots_Deduplicated_AndHolesDropped()
        {
            // A map that paints one vein twice has one mineral, and a patch with no mineral is a map error
            // named by the validator rather than a source of items.
            var pool = new ShapePool(new[] { Quad.Circle, Quad.Circle, Quad.Empty, Quad.Square },
                Operators.Cutter);

            Assert.AreEqual(2, pool.Minerals.Count, "two distinct minerals");
            Assert.AreEqual(Sim.Circle, pool.Minerals[0], "in the order the map lists them");
            Assert.AreEqual(Sim.Square, pool.Minerals[1]);
            Assert.AreEqual(Operators.Cutter, pool.Unlocked);

            var empty = new ShapePool(new Quad[0], Operators.All);
            Assert.IsEmpty(empty.Shapes, "a map with no ore can make nothing");
            Assert.IsEmpty(empty.Minerals);
            Assert.AreEqual(0, empty.MaxDepth);
            Assert.IsFalse(empty.CanProduce(Sim.Circle));

            Assert.AreEqual(Operators.All, new ShapePool(BothMinerals, (Operators)0xFF).Unlocked,
                "an unknown operator bit is not remembered");
        }

        [Test]
        public void AnOperatorResult_IsAtMostOneStepDeeperThanWhatItCameFrom()
        {
            // The structural property the generator's bands rest on. Depth is the *minimum* number of
            // operator steps, so applying one operator can never cost less than the minimum and never more
            // than one step beyond it - every edge costs one. Without this, "the deeper the demanded shape,
            // the tougher that group" would be a sentence about a number that means nothing.
            var pool = new ShapePool(BothMinerals, Operators.All);

            foreach (Shape shape in pool.Shapes)
            {
                int depth = pool.Depth(shape);

                foreach (bool vertical in new[] { false, true })
                {
                    (Shape a, Shape b) = Shape.Cut(shape, vertical);
                    if (!a.IsEmpty && a != shape) AssertReach(pool, a, depth + 1, shape, "a cut of");
                    if (!b.IsEmpty && b != shape) AssertReach(pool, b, depth + 1, shape, "a cut of");
                }

                Shape turned = shape.RotatedCW();
                if (turned != shape) AssertReach(pool, turned, depth + 1, shape, "a turn of");
            }

            // And the floor of the scale is the mine: nothing is shallower than a mineral, because a drill
            // is the only thing that makes an item without an operator.
            foreach (Shape shape in pool.Shapes)
            {
                bool mined = shape.FilledCount == Shape.QuadrantCount && shape.IsPure(shape.At(Shape.NW));
                if (!mined) continue;

                Assert.AreEqual(0, pool.Depth(shape), shape + " is a full pure item - a mineral");
            }

            foreach (Shape shape in pool.AtDepth(0))
                Assert.AreEqual(4, shape.FilledCount, shape + " is not something a drill could have mined");
        }

        /// <summary>An item one operator step from <paramref name="from"/> must be no deeper than that
        /// step could make it - and must be producible at all, or the closure is missing an edge.</summary>
        private static void AssertReach(ShapePool pool, Shape shape, int ceiling, Shape from, string via)
        {
            int depth = pool.Depth(shape);
            Assert.GreaterOrEqual(depth, 0, via + " " + from + " produced " + shape + ", which the pool lacks");
            Assert.LessOrEqual(depth, ceiling, via + " " + from + " put " + shape + " deeper than one step");
        }

        /// <summary>The codes of every item in the state space, so a test can ask "is this one of the
        /// eighty" with one hash lookup instead of a scan per item.</summary>
        private static HashSet<uint> AllCodes()
        {
            var codes = new HashSet<uint>();
            foreach (Shape shape in ShapePool.All) codes.Add(shape.Code);
            return codes;
        }
    }
}
