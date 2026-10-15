using System;
using System.Collections.Generic;
using System.Linq;
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
            Assert.AreEqual(4, pool.MaxDepth, "the deepest round takes four operator steps");

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

        [Test]
        public void EveryDepth_IsTheCheapestDerivation_NotJustTheFirstOneFound()
        {
            // A stack edge costs the deeper of its two sides PLUS ONE, not one step - so the
            // breadth-first walk's first discovery of an item is not necessarily its cheapest
            // derivation: a half and a quarter only pair up when the quarter is dequeued, and by then
            // two earlier-discovered quarters have often stacked and claimed the item at a deeper
            // count. On the shipped ladder that once misfiled fourteen of the eighty (an L-shaped
            // two-plus-one at depth 4 that takes 3; the three-plus-one mixed rounds at 5 that take 4),
            // which pushed the generator's depth band past the tier the player actually builds in.
            // The verifier here is the definition, computed independently: relax every derivation
            // edge to a fixpoint and compare.
            foreach (Operators unlocked in new[]
                     {
                         Operators.Cutter | Operators.Stacker,
                         Operators.Rotator | Operators.Stacker,
                         Operators.All,
                     })
            {
                var pool = new ShapePool(BothMinerals, unlocked);
                var truth = MinimalDepths(pool);
                string at = " (" + unlocked + ")";

                Assert.AreEqual(pool.Shapes.Count, truth.Count, "the verifier saw a different closure" + at);

                foreach (Shape shape in pool.Shapes)
                    Assert.AreEqual(truth[shape.Code], pool.Depth(shape),
                        shape + " is filed at depth " + pool.Depth(shape) + " but builds in " +
                        truth[shape.Code] + operatorSteps(truth[shape.Code]) + at);
            }
        }

        [Test]
        public void TheThreePlusOneMixedRounds_StackTogether_AtFourStepsNotFive()
        {
            // The regression itself, named: the round the campaign's finale demands - three quadrants of
            // one mineral and one of the other - is a mixed half (two stacked quarters) under a half, so
            // it builds in four operator steps. Filing it at five put the generated exam's depth band
            // past every tier but the deepest, so every generated wave briefed the same handful of
            // rounds and the mid-game's "a new decision a wave" collapsed.
            var pool = new ShapePool(BothMinerals, Operators.All);

            Shape threeCircles = Shape.Of(Quad.Circle, Quad.Circle, Quad.Circle, Quad.Square);
            Shape threeSquares = Shape.Of(Quad.Square, Quad.Square, Quad.Circle, Quad.Square);
            Assert.AreEqual(4, pool.Depth(threeCircles), threeCircles.Describe() + " is a mixed half under a half");
            Assert.AreEqual(4, pool.Depth(threeSquares), threeSquares.Describe() + " likewise, other mineral up");

            // And the L-shaped three-quadrant items are a half under a quarter: three steps.
            Shape lShaped = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Square);
            Assert.AreEqual(3, pool.Depth(lShaped), lShaped.Describe() + " is a half plus a quarter");

            Assert.AreEqual(4, pool.MaxDepth, "the ladder's deepest rung is the three-plus-one round");
        }

        /// <summary>The minimal operator count for every producible item, computed independently of
        /// <see cref="ShapePool"/>: a fixpoint relaxation over cut, turn and stack edges, where a cut or
        /// turn costs the source plus one and a stack the deeper side plus one.</summary>
        private static Dictionary<uint, int> MinimalDepths(ShapePool pool)
        {
            List<Shape> shapes = new List<Shape>(pool.Shapes);
            var depth = shapes.ToDictionary(s => s.Code, s => int.MaxValue);
            foreach (Shape mineral in pool.Minerals) depth[mineral.Code] = 0;

            bool changed = true;
            while (changed)
            {
                changed = false;

                foreach (Shape shape in shapes)
                {
                    // An item no edge has reached yet has nothing to relax outwards - and its sentinel
                    // would overflow if it tried.
                    if (depth[shape.Code] == int.MaxValue) continue;

                    int through = depth[shape.Code] + 1;
                    var across = Shape.Cut(shape, false);
                    var along = Shape.Cut(shape, true);
                    var outputs = new List<Shape>
                    {
                        across.a, across.b,
                        along.a, along.b,
                        shape.RotatedCW(),
                    };

                    foreach (Shape output in outputs)
                        changed |= Relax(depth, output, through);

                    foreach (Shape other in shapes)
                    {
                        if (shape.Overlaps(other)) continue;
                        if (depth[other.Code] == int.MaxValue) continue;   // an unreached side has no count to add
                        changed |= Relax(depth, Shape.Stacked(shape, other),
                            Math.Max(depth[shape.Code], depth[other.Code]) + 1);
                    }
                }
            }

            var solved = new Dictionary<uint, int>();
            foreach (var pair in depth)
                if (pair.Value != int.MaxValue) solved[pair.Key] = pair.Value;
            return solved;
        }

        private static bool Relax(Dictionary<uint, int> depth, Shape shape, int candidate)
        {
            if (shape.IsEmpty || !depth.ContainsKey(shape.Code) || candidate >= depth[shape.Code]) return false;

            depth[shape.Code] = candidate;
            return true;
        }

        private static string operatorSteps(int count) => count == 1 ? " step" : " steps";

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
