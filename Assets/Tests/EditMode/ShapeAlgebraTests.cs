using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The shape algebra itself, as properties rather than examples: this is the game's whole language
    /// (§2 of the design), so the rules the operators rest on are asserted over <em>every</em> item the
    /// algebra can hold, not over a handful a hand-written test happens to name.
    ///
    /// Four things are being pinned, and each of them is a rule some machine would quietly break if the
    /// algebra did: rotation is a four-cycle (the rotator's whole job), a cut loses nothing (the cutter's),
    /// stacking is a partial function (the stacker's - and the one that used to be wrong), and the state
    /// space really is the eighty items the generator, the sorter and the briefing all reason about.
    /// </summary>
    public class ShapeAlgebraTests
    {
        /// <summary>Every item, once, as the table the generator draws from.</summary>
        private static readonly IReadOnlyList<Shape> All = ShapePool.All;

        // ---------------------------------------------------------------- rotation

        [Test]
        public void RotatingFourTimes_IsTheIdentity_ForEveryItem()
        {
            // The rotator's machine count rests on this: one direction of turn and a fixed cycle, so
            // counter-clockwise is three machines in series rather than a second machine's worth of
            // content. If four turns were anything but the identity, a line of four rotators would drift.
            foreach (Shape shape in All)
            {
                Shape turned = shape;
                for (int i = 0; i < Shape.QuadrantCount; i++) turned = turned.RotatedCW();

                Assert.AreEqual(shape, turned, shape + " did not come back after four quarter turns");
            }
        }

        [Test]
        public void ATurn_MovesEachQuadrantOneStepClockwise()
        {
            // NW -> NE -> SE -> SW -> NW, with the content carried along. Spelled out for the one item
            // where it is unambiguous, because "clockwise" is exactly the kind of word two people read
            // differently and the algebra cannot.
            Assert.AreEqual(Sim.LeftHalfCircle, Sim.BottomHalfCircle.RotatedCW(),
                "the bottom half turns into the left half");
            Assert.AreEqual(Sim.TopHalfCircle, Sim.LeftHalfCircle.RotatedCW());
            Assert.AreEqual(Sim.RightHalfCircle, Sim.TopHalfCircle.RotatedCW());
            Assert.AreEqual(Sim.BottomHalfCircle, Sim.RightHalfCircle.RotatedCW());
        }

        [Test]
        public void TwoTurns_AreTheComplementPiece_NotTheIdentity()
        {
            // The design's locked decision (§0): a half turn is not the identity. A half turn moves every
            // quadrant to the one opposite it, so two turns of a top half give the bottom half - the
            // complement piece on the same cut line - which is why orientation is a real thing the player
            // has to get right rather than a property that cancels out.
            //
            // The exceptions are the items that happen to be 180-degree symmetric, and they are worth
            // naming rather than hiding: a *full* shape (all four quadrants equal) and a diagonal pair
            // (NW with SE, or NE with SW) read the same either way up. That is exactly why a full round is
            // the one demand orientation cannot get wrong, and why a diagonal round is the one cut piece
            // that cannot be mis-oriented.
            foreach (Shape shape in All)
            {
                Shape twice = shape.RotatedCW().RotatedCW();

                Assert.AreEqual(
                    Shape.Of(shape.At(Shape.SE), shape.At(Shape.SW), shape.At(Shape.NW), shape.At(Shape.NE)),
                    twice, shape + " did not come back with its opposite quadrants swapped");

                bool symmetric = shape.At(Shape.NW) == shape.At(Shape.SE) &&
                                 shape.At(Shape.NE) == shape.At(Shape.SW);
                Assert.AreEqual(symmetric, twice == shape,
                    shape + (symmetric ? " is 180-degree symmetric" : " is not 180-degree symmetric"));
            }

            Assert.AreEqual(Sim.BottomHalfCircle, Sim.TopHalfCircle.RotatedCW().RotatedCW(),
                "and the complement of a top half is the bottom one");
            Assert.AreEqual(Sim.TopHalfCircle, Sim.BottomHalfCircle.RotatedCW().RotatedCW());
            Assert.AreNotEqual(Sim.TopHalfCircle, Sim.TopHalfCircle.RotatedCW().RotatedCW());

            // Of the sixteen full items, exactly the two pure ones are invariant: a mixed full round is
            // made of quadrants that differ, so turning it really does move it - which is why "a full
            // shape" is not by itself a demand orientation can ignore.
            Assert.AreEqual(Sim.Circle, Sim.Circle.RotatedCW(), "a full circle is four-fold symmetric");
            Assert.AreEqual(Sim.Square, Sim.Square.RotatedCW(), "and so is a full square");

            int invariant = 0;
            foreach (Shape shape in All)
            {
                if (shape.FilledCount != Shape.QuadrantCount) continue;

                bool uniform = shape.At(Shape.NW) == shape.At(Shape.NE) &&
                               shape.At(Shape.NE) == shape.At(Shape.SE) &&
                               shape.At(Shape.SE) == shape.At(Shape.SW);

                Assert.AreEqual(uniform, shape.RotatedCW() == shape,
                    shape + " is " + (uniform ? "uniform" : "mixed") + ", so turning it should " +
                    (uniform ? "change nothing" : "move it"));
                if (uniform) invariant++;
            }

            Assert.AreEqual(2, invariant, "the two pure full shapes are the only orientation-free items");
        }

        [Test]
        public void ARotation_KeepsWhatTheItemIsMadeOf()
        {
            // Turned, not converted: the quadrants move and the content does not change.
            foreach (Shape shape in All)
            {
                Shape turned = shape.RotatedCW();
                Assert.AreEqual(shape.FilledCount, turned.FilledCount, shape + " changed how full it is");
                Assert.AreEqual(shape.IsPure(Quad.Circle), turned.IsPure(Quad.Circle), shape + " changed mineral");
                Assert.AreEqual(shape.IsPure(Quad.Square), turned.IsPure(Quad.Square));
            }
        }

        // ---------------------------------------------------------------- cutting

        [Test]
        public void ACut_LosesNothing_ForEveryItemAndBothLines()
        {
            // The cutter's rule, as conservation: the two pieces together are the item that went in, and
            // neither carries anything the item did not. "An item can be cut at most twice" is a corollary
            // of this rather than a rule of its own, which is why it is worth asserting over all eighty.
            foreach (Shape shape in All)
            foreach (bool vertical in new[] { false, true })
            {
                (Shape a, Shape b) = Shape.Cut(shape, vertical);

                Assert.AreEqual(shape, Shape.Stacked(a, b),
                    shape + " (vertical: " + vertical + ") did not survive being put back together");

                for (int q = 0; q < Shape.QuadrantCount; q++)
                {
                    if (a.Has(q)) Assert.AreEqual(shape.At(q), a.At(q), shape + " piece a gained content at " + q);
                    if (b.Has(q)) Assert.AreEqual(shape.At(q), b.At(q), shape + " piece b gained content at " + q);
                }
            }
        }

        [Test]
        public void AHorizontalCut_SplitsTopFromBottom_AndAVerticalOne_LeftFromRight()
        {
            // Which line a facing selects, and which quadrants each side of it owns - the rule the cutter's
            // two outlets are wired to.
            (Shape top, Shape bottom) = Shape.Cut(Sim.Circle, vertical: false);
            Assert.AreEqual(Sim.TopHalfCircle, top, "the first piece is the side the top is on");
            Assert.AreEqual(Sim.BottomHalfCircle, bottom);

            (Shape left, Shape right) = Shape.Cut(Sim.Circle, vertical: true);
            Assert.AreEqual(Sim.LeftHalfCircle, left, "and for a vertical cut, the left");
            Assert.AreEqual(Sim.RightHalfCircle, right);
        }

        [Test]
        public void CuttingAQuarter_YieldsTheQuarterAndNothing()
        {
            // The empty side of a cut is not an item: a quarter already lies on one side of every line that
            // can cross it, so there is nothing to put out of the second outlet. Which of the two outlets
            // gets the quarter is the cut line's business - the first piece is the top for a horizontal cut
            // and the left for a vertical one - so the claim is that exactly one piece carries anything.
            foreach (int q in new[] { Shape.NW, Shape.NE, Shape.SE, Shape.SW })
            {
                Shape quarter = Shape.Quarter(q, Quad.Circle);
                string which = Shape.CornerCode(q);

                foreach (bool vertical in new[] { false, true })
                {
                    (Shape a, Shape b) = Shape.Cut(quarter, vertical);

                    Assert.AreEqual(1, (a.IsEmpty ? 0 : 1) + (b.IsEmpty ? 0 : 1),
                        "exactly one side of a cut through a " + which + " quarter carries anything");
                    Assert.IsTrue(a == quarter || b == quarter,
                        "and what it carries is the quarter itself, unchanged (" + which +
                        ", vertical: " + vertical + ")");
                    Assert.IsTrue(a.IsEmpty || b.IsEmpty, "with nothing at all on the other side");
                }
            }
        }

        // ---------------------------------------------------------------- stacking

        [Test]
        public void StackingTwoDisjointItems_Commutes_AndIsExactlyTheirUnion()
        {
            // The stacker's semantics: a plain OR, because with disjoint inputs the union is unambiguous
            // without any layer concept. Commutativity is what lets the machine take the two pieces in
            // whichever order the belts happen to deliver them.
            var disjoint = new List<(Shape a, Shape b)>();
            for (int i = 0; i < All.Count; i++)
            for (int j = i + 1; j < All.Count; j++)
            {
                if (!All[i].Overlaps(All[j])) disjoint.Add((All[i], All[j]));
            }

            Assert.Greater(disjoint.Count, 0, "the pool must contain pairs that can be unioned at all");

            foreach ((Shape a, Shape b) in disjoint)
            {
                Shape forward = Shape.Stacked(a, b);
                Shape backward = Shape.Stacked(b, a);

                Assert.AreEqual(forward, backward, a + " + " + b + " is not commutative");

                for (int q = 0; q < Shape.QuadrantCount; q++)
                {
                    Quad expected = a.Has(q) ? a.At(q) : b.At(q);
                    Assert.AreEqual(expected, forward.At(q),
                        a + " + " + b + " put the wrong thing in quadrant " + Shape.CornerCode(q));
                }

                Assert.AreEqual(a.FilledCount + b.FilledCount, forward.FilledCount,
                    "a disjoint union's fill is the two pieces' fills added up");
            }
        }

        [Test]
        public void StackingAnOverlappingPair_Throws_AndTheForgivingFormSaysNo()
        {
            // Overlap is illegal: two minerals cannot share a quadrant. This is the test the algebra's one
            // real bug hid behind - the two items below are in the SAME quadrant and are DIFFERENT
            // minerals, which is the case a bitwise AND gets wrong: circle is 01 and square is 10, so
            // `(a & b) != 0` reports them disjoint and mints a 11 - an item that does not exist.
            Shape circleHere = Shape.Quarter(Shape.NW, Quad.Circle);
            Shape squareHere = Shape.Quarter(Shape.NW, Quad.Square);

            Assert.IsTrue(circleHere.Overlaps(squareHere),
                "a circle and a square in one quadrant share that quadrant, whatever the minerals are");
            Assert.IsTrue(squareHere.Overlaps(circleHere), "and the question is symmetric");

            var thrown = Assert.Throws<System.ArgumentException>(() => Shape.Stacked(circleHere, squareHere));
            StringAssert.Contains("quadrant", thrown.Message, "the refusal says why");

            Assert.IsFalse(Shape.TryStack(circleHere, squareHere, out Shape stacked),
                "the tick path's form refuses it rather than throwing");
            Assert.IsTrue(stacked.IsEmpty, "and hands back nothing, so a caller cannot use a half-answer");

            // The same mineral in one quadrant is an overlap too - and this is the case the broken test
            // would have caught, which is why both are asserted.
            Assert.IsTrue(circleHere.Overlaps(Shape.Quarter(Shape.NW, Quad.Circle)));
            Assert.IsFalse(circleHere.Overlaps(Shape.Quarter(Shape.NE, Quad.Circle)),
                "the same mineral in a different quadrant is not an overlap");
            Assert.IsFalse(Shape.None.Overlaps(circleHere), "and nothing overlaps nothing");
        }

        [Test]
        public void NoStackEverMintsAQuadrantThatDoesNotExist()
        {
            // The property behind the bug: whatever pair is offered, every quadrant of the result is one of
            // the three real values. A bitwise overlap test let a circle and a square in one quadrant OR
            // into a 11, so the pool grew past eighty and reached depths no item has.
            foreach (Shape a in All)
            foreach (Shape b in All)
            {
                if (!Shape.TryStack(a, b, out Shape stacked)) continue;

                for (int q = 0; q < Shape.QuadrantCount; q++)
                {
                    Quad at = stacked.At(q);
                    Assert.IsTrue(at == Quad.Empty || at == Quad.Circle || at == Quad.Square,
                        a + " + " + b + " produced quadrant " + at + " at " + Shape.CornerCode(q));
                }
            }
        }

        // ---------------------------------------------------------------- reading an item

        [Test]
        public void ContainsIsMineralWide_AndIsPureIsAllOfOneMineral()
        {
            Assert.IsTrue(Sim.Composite.Contains(Quad.Circle), "the mixed round carries a circle");
            Assert.IsTrue(Sim.Composite.Contains(Quad.Square), "and a square");
            Assert.IsFalse(Sim.Composite.IsPure(Quad.Circle), "but it is not made of one mineral");
            Assert.IsFalse(Sim.Composite.IsPure(Quad.Square));

            Assert.IsTrue(Sim.Circle.IsPure(Quad.Circle), "a full circle is pure");
            Assert.IsFalse(Sim.Circle.IsPure(Quad.Square));
            Assert.IsTrue(Sim.TopHalfSquare.IsPure(Quad.Square), "so is a half-square");
            Assert.IsFalse(Shape.None.IsPure(Quad.Circle), "and nothing is not a shape made of anything");

            Assert.IsTrue(Shape.None.Contains(Quad.Empty), "a hole is a hole");
            Assert.IsFalse(Sim.Circle.Contains(Quad.Square));
        }

        [Test]
        public void FilledCount_CountsTheQuadrantsThatCarrySomething()
        {
            Assert.AreEqual(0, Shape.None.FilledCount);
            Assert.AreEqual(1, Shape.Quarter(Shape.SE, Quad.Square).FilledCount);
            Assert.AreEqual(2, Sim.TopHalfCircle.FilledCount);
            Assert.AreEqual(3, Shape.Of(Quad.Circle, Quad.Circle, Quad.Circle, Quad.Empty).FilledCount);
            Assert.AreEqual(4, Sim.Circle.FilledCount);
        }

        [Test]
        public void AnIndexOffTheItem_ReadsAsAHole_NeverAThrow()
        {
            // `At` is on the tick path - a cut mask, a serialiser, a view's four-quadrant loop - so an
            // index that is wrong must be a hole rather than an exception in the middle of a frame.
            Assert.AreEqual(Quad.Empty, Sim.Circle.At(-1));
            Assert.AreEqual(Quad.Empty, Sim.Circle.At(Shape.QuadrantCount));
            Assert.IsFalse(Sim.Circle.Has(-1));
            Assert.IsFalse(Shape.Of(Quad.Circle, Quad.Empty, Quad.Empty, Quad.Empty).Has(3));
        }

        [Test]
        public void AnItemSaysWhatItIs_InWordsAndInACode()
        {
            // The prose a briefing, a tooltip and a test failure all quote, with orientation included -
            // the price of exact identity, and the reason `Describe` names corners rather than indices.
            Assert.AreEqual("nothing", Shape.None.Describe());
            Assert.AreEqual("full circle", Sim.Circle.Describe());
            Assert.AreEqual("top half-circle", Sim.TopHalfCircle.Describe());
            Assert.AreEqual("bottom half-square", Sim.BottomHalfSquare.Describe());
            Assert.AreEqual("left half-circle", Sim.LeftHalfCircle.Describe());
            Assert.AreEqual("quarter circle (NW)", Shape.Quarter(Shape.NW, Quad.Circle).Describe());
            Assert.AreEqual("three-quarter square", Shape.Of(Quad.Square, Quad.Square, Quad.Square, Quad.Empty).Describe());

            StringAssert.Contains("circle top-left", Sim.Composite.Describe(),
                "a mixed item is spelled quadrant by quadrant");
            StringAssert.Contains("square bottom-right", Sim.Composite.Describe());

            Assert.AreEqual("OOOO", Sim.Circle.ToString(), "and the compact code reads NW NE SE SW");
            Assert.AreEqual("OO--", Sim.TopHalfCircle.ToString());
            Assert.AreEqual("----", Shape.None.ToString());

            Assert.AreEqual("circle", Shape.MineralName(Quad.Circle));
            Assert.AreEqual("square", Shape.MineralName(Quad.Square));
            Assert.AreEqual("nothing", Shape.MineralName(Quad.Empty));
            Assert.AreEqual("top-left", Shape.CornerName(Shape.NW));
            Assert.AreEqual("bottom-right", Shape.CornerName(Shape.SE));
            Assert.AreEqual("SW", Shape.CornerCode(Shape.SW));
        }

        // ---------------------------------------------------------------- the state space

        [Test]
        public void TheStateSpace_IsExactlyTheEightyItems()
        {
            // 3^4 - 1, and the design's own itemisation (§2.2): the generator draws from this table, the
            // sorter names items in it and a wave's demand is one of them, so "how many items are there"
            // is not trivia - a table that drifted would let a wave ask for something unbuildable.
            Assert.AreEqual(80, All.Count, "the design's eighty");

            var codes = new HashSet<uint>();
            foreach (Shape shape in All)
            {
                Assert.IsFalse(shape.IsEmpty, "the empty item is not a shape");
                Assert.IsTrue(codes.Add(shape.Code), "two items share the code " + shape);
                Assert.AreEqual(shape, Shape.Of(shape.At(Shape.NW), shape.At(Shape.NE),
                        shape.At(Shape.SE), shape.At(Shape.SW)),
                    shape + " did not round-trip through its own quadrants");

                for (int q = 0; q < Shape.QuadrantCount; q++)
                {
                    Quad at = shape.At(q);
                    Assert.IsTrue(at == Quad.Empty || at == Quad.Circle || at == Quad.Square,
                        shape + " holds a quadrant value that is not one of the three");
                }
            }

            Assert.AreEqual(All.Count, codes.Count, "the table holds every item once");

            // The classes, and the sum that has to land back on eighty: the tiers the generator's depth
            // bands and the design's "30 pure / 50 mixed" both count.
            int[] byFill = new int[Shape.QuadrantCount + 1];
            int pure = 0;
            foreach (Shape shape in All)
            {
                byFill[shape.FilledCount]++;
                if (shape.IsPure(Quad.Circle) || shape.IsPure(Quad.Square)) pure++;
            }

            Assert.AreEqual(8, byFill[1], "eight quarters: the atoms every chain is built from");
            Assert.AreEqual(24, byFill[2], "twenty-four two-quadrant items: 8 halves + 4 diagonals + 12 mixed");
            Assert.AreEqual(32, byFill[3], "thirty-two three-quadrant items");
            Assert.AreEqual(16, byFill[4], "sixteen full shapes");

            Assert.AreEqual(8 + 24 + 32 + 16, All.Count, "and the classes add up to the whole space");
            Assert.AreEqual(30, pure, "thirty of them are made of one mineral");
            Assert.AreEqual(50, All.Count - pure, "and fifty are mixed");
        }

        // ---------------------------------------------------------------- §2.4, the worked example

        [Test]
        public void TheWorkedExample_ComesOutOfTheAlgebrasOwnOperators()
        {
            // docs/DESIGN.md §2.4, carried out with nothing but Cut, RotatedCW and Stacked: "one pair of
            // cutters, at most two rotations, both mixed rounds complete". This is the algebra proving
            // itself, so it is a test rather than a paragraph.
            (Shape circleTop, Shape circleBottom) = Shape.Cut(Sim.Circle, vertical: false);
            (Shape squareTop, Shape squareBottom) = Shape.Cut(Sim.Square, vertical: false);

            // The circle's bottom half, turned once clockwise, is the left half-circle; the square's top
            // half, turned once, is the right half-square.
            Shape circleLeft = circleBottom.RotatedCW();
            Shape squareRight = squareTop.RotatedCW();
            Assert.AreEqual(Sim.LeftHalfCircle, circleLeft);
            Assert.AreEqual(Sim.RightHalfSquare, squareRight);

            // Stacked, they are circle-left over square-right - and the two pieces do not overlap, so the
            // union is legal (which is the whole reason this pair was chosen).
            Assert.IsFalse(circleLeft.Overlaps(squareRight), "the two turned pieces share no quadrant");
            Shape circleLeftSquareRight = Shape.Stacked(circleLeft, squareRight);
            Assert.AreEqual(Shape.Of(Quad.Circle, Quad.Square, Quad.Square, Quad.Circle), circleLeftSquareRight,
                "circle left, square right");

            // And the leftovers - the top half-circle and the bottom half-square - stack directly, with no
            // rotation at all, into circle over square.
            Assert.IsFalse(circleTop.Overlaps(squareBottom), "and neither do the leftovers");
            Shape circleTopSquareBottom = Shape.Stacked(circleTop, squareBottom);
            Assert.AreEqual(Sim.Composite, circleTopSquareBottom, "circle top, square bottom");

            Assert.AreEqual(4, circleLeftSquareRight.FilledCount, "both rounds are full items");
            Assert.AreEqual(4, circleTopSquareBottom.FilledCount);

            // The pair is why orientation matters: the two mixed rounds are different items, and turning
            // one of them a half turn gives the other's mirror - so a line that skipped its rotators would
            // build the same minerals into the wrong round.
            Assert.AreNotEqual(circleLeftSquareRight, circleTopSquareBottom,
                "the two rounds differ, and only their orientation says how");
            Assert.AreEqual(Shape.Of(Quad.Square, Quad.Circle, Quad.Circle, Quad.Square),
                circleLeftSquareRight.RotatedCW().RotatedCW(),
                "a half turn of the first gives circle-right/square-left - the complement, not the other round");

            // And the two turns are not optional: neither rotated piece can pair with its own twin, because
            // the two twins want the same quadrants. That is the puzzle §2.4 describes - how the line is
            // laid out and which piece rides which belt is the work.
            Assert.IsTrue(circleBottom.Overlaps(squareBottom),
                "the two bottom pieces want the same two quadrants");
            Assert.Throws<System.ArgumentException>(() => Shape.Stacked(circleBottom, squareBottom));
            Assert.IsTrue(circleTop.Overlaps(squareTop), "and so do the two top pieces");
            Assert.Throws<System.ArgumentException>(() => Shape.Stacked(circleTop, squareTop));
        }
    }
}
