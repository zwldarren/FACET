using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The three operators, as machines rather than as algebra: the cutter that splits one item along a
    /// line, the rotator that turns one a quarter, and the stacker that unions two. Each of them is a rule
    /// about <em>time</em> and about <em>outlets</em> on top of the algebra the algebra tests already pin,
    /// and each of those halves is asserted here: how long a cycle takes, what a blocked outlet costs, and
    /// what the machine does with an item it cannot use yet.
    ///
    /// The worlds are the 20x12 test map, clear of the Core's footprint (8,4)-(11,7) wherever a test cares
    /// about an item surviving to be read.
    /// </summary>
    public class OperatorTests
    {
        // ---------------------------------------------------------------- the cutter

        [Test]
        public void TheCutter_EmitsBothPiecesInTheSameTick()
        {
            // The split is one decision, not two: the piece on the side the machine faces leaves by that
            // outlet and the other by the outlet opposite it, and they go together. A machine that emitted
            // one and waited a cycle for the other would halve a two-sided line for no reason the player
            // could see.
            SimWorld world = CutWorld(Dir.North, out Int2 front, out Int2 back);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));

            int landed = TickUntil(world, 30 * 5, () =>
                !Sim.ShapeAt(world, front).IsEmpty || !Sim.ShapeAt(world, back).IsEmpty);

            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(world, front), "the front outlet took the top half");
            Assert.AreEqual(Sim.BottomHalfCircle, Sim.ShapeAt(world, back), "and the back outlet the bottom one");
            Assert.Greater(landed, 0, "both pieces arrived on the same tick, " + landed + " ticks in");
        }

        [Test]
        public void TheTwoFacingsOfOneCutLine_ProduceTheSamePairOnTheSameSides()
        {
            // The cut line's own 180-degree symmetry, which is why the machine stores a facing rather than
            // a line: turning a knife half a turn does not change the cut. What swaps is which piece the
            // machine calls the *front* one - and therefore which of its two buffers the piece lands in -
            // not which side of the item leaves by which belt.
            SimWorld north = CutWorld(Dir.North, out Int2 northFront, out Int2 northBack);
            SimWorld south = CutWorld(Dir.South, out Int2 southFront, out Int2 southBack);

            Assert.IsTrue(north.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));
            Assert.IsTrue(south.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));
            TickUntil(north, 30 * 5, () => !Sim.ShapeAt(north, northFront).IsEmpty);
            TickUntil(south, 30 * 5, () => !Sim.ShapeAt(south, southFront).IsEmpty);

            Assert.AreEqual(new Int2(4, 3), northFront, "the test's premise: the two machines face opposite ways");
            Assert.AreEqual(new Int2(4, 1), southFront);

            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(north, new Int2(4, 3)),
                "facing north, the top half leaves by the north belt");
            Assert.AreEqual(Sim.BottomHalfCircle, Sim.ShapeAt(north, new Int2(4, 1)),
                "and the bottom half by the south one");
            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(south, new Int2(4, 3)),
                "facing south, the same piece still leaves north");
            Assert.AreEqual(Sim.BottomHalfCircle, Sim.ShapeAt(south, new Int2(4, 1)),
                "and the same piece still leaves south - the cut did not change");

            // What did change: the piece the facing side owns. That is the buffer assignment, and it is
            // exactly what turns "which way is the cutter pointing" into a decision about outlets.
            Assert.AreEqual(Sim.TopHalfCircle, Sim.MachineAt(north, new Int2(4, 2)).OutputShape,
                "facing north, the front slot is the top piece's");
            Assert.AreEqual(Sim.BottomHalfCircle, Sim.MachineAt(south, new Int2(4, 2)).OutputShape,
                "facing south, the front slot is the bottom piece's");
        }

        [Test]
        public void CuttingAQuarter_EmitsTheQuarterAndNothing()
        {
            // "An item can be cut at most twice" is a corollary, not a rule: a quarter already lies on one
            // side of every line that can cross it, so the other outlet gets nothing at all - not an empty
            // item, not a stall, just one piece out of two outlets.
            foreach (int q in new[] { Shape.NW, Shape.NE, Shape.SE, Shape.SW })
            {
                SimWorld world = CutWorld(Dir.North, out Int2 front, out Int2 back);
                Shape quarter = Shape.Quarter(q, Quad.Circle);

                Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), quarter));
                TickUntil(world, 30 * 5, () => !Sim.ShapeAt(world, front).IsEmpty || !Sim.ShapeAt(world, back).IsEmpty);

                Assert.AreEqual(1, Sim.ItemCount(world),
                    "a " + Shape.CornerCode(q) + " quarter is one piece, wherever the line falls");
                Assert.IsTrue(Sim.ShapeAt(world, front) == quarter || Sim.ShapeAt(world, back) == quarter,
                    "and the piece is the quarter itself");

                MachineState cutter = Sim.MachineAt(world, new Int2(4, 2));
                Assert.IsTrue((cutter.OutputShape == quarter) ^ (cutter.OutputShape2 == quarter),
                    "exactly one of the two slots ever held a piece, and it was the quarter");
                Assert.IsTrue(cutter.OutputShape.IsEmpty || cutter.OutputShape2.IsEmpty,
                    "and the slot the empty side owns is empty, not holding a hole");
            }
        }

        [Test]
        public void ABlockedOutlet_BuffersItsOwnPiece_AndLeavesTheOtherSideRunning()
        {
            // The second output slot's whole purpose, as a test: with nowhere to put one piece, the cutter
            // keeps cutting *for the other side* until its own buffer for the stranded piece is full. The
            // pieces are never lost, the two sides never interfere, and the machine's own ceiling is the
            // only thing that eventually stops it - which is what a player sees as a queue.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);       // input
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.North);
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 6);      // the front outlet, long enough to drain

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));
                TickUntil(world, 30 * 6, () => world.Belts.HasItemAt(new Int2(4, 3)));
                TickUntil(world, 30 * 6, () => world.Belts.TryTakeItem(new Int2(4, 3), out _));
            }

            Assert.AreEqual(0, world.Belts.JamCount, "a blocked outlet is backpressure, not a jam");
            Assert.AreEqual(3, Sim.MachineAt(world, new Int2(4, 2)).OutputCount2,
                "all three bottom halves are buffered in their own slot, none thrown away");

            Sim.LayRun(world, new Int2(4, 1), Dir.South, 1);
            TickUntil(world, 30 * 6, () => !Sim.ShapeAt(world, new Int2(4, 1)).IsEmpty);

            Assert.AreEqual(Sim.BottomHalfCircle, Sim.ShapeAt(world, new Int2(4, 1)),
                "and the moment there is an outlet, the oldest piece leaves by it");
        }

        [Test]
        public void ACutterAlreadyQueueingOneShape_WaitsRatherThanRewritingItsQueue()
        {
            // A slot counts pieces of *one* shape, because one count cannot describe a queue of two different
            // items. So a cutter whose slot is filling with bottom half-circles and is then handed a square
            // has to wait: writing the square's piece into that slot would rewrite what is already queued,
            // and three halves the player cut are three halves - not whatever was cut last. The wait is a
            // stall on the machine, which is visible and safe, and never a jam on the belt.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);       // input
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.North);
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 6);      // the front outlet, long enough to drain

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));
                TickUntil(world, 30 * 6, () => world.Belts.HasItemAt(new Int2(4, 3)));
                TickUntil(world, 30 * 6, () => world.Belts.TryTakeItem(new Int2(4, 3), out _));
            }

            Assert.AreEqual(3, Sim.MachineAt(world, new Int2(4, 2)).OutputCount2,
                "the test's premise: three bottom half-circles are queued in the back slot");
            Assert.AreEqual(Sim.BottomHalfCircle, Sim.MachineAt(world, new Int2(4, 2)).OutputShape2);

            // A square arrives, whose cut would put a bottom half-*square* in that very slot.
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Square));
            Sim.Tick(world, 30 * 8);

            MachineState after = Sim.MachineAt(world, new Int2(4, 2));
            Assert.AreEqual(Sim.BottomHalfCircle, after.OutputShape2,
                "the queued pieces are still bottom half-circles");
            Assert.AreEqual(3, after.OutputCount2, "and there are still three of them");
            Assert.IsTrue(after.Carried.IsEmpty, "the square was not taken, so nothing is mid-split");
            Assert.AreEqual(Sim.Square, Sim.ShapeAt(world, new Int2(3, 2)),
                "it is still on the belt it arrived on: the machine waits for the slot to drain");
            Assert.AreEqual(0, world.Belts.JamCount, "a machine that waits is backpressure, not a jam");
        }

        [Test]
        public void TheReading_CallsTheTwoOutletsTheSameWayTheTickDoes()
        {
            // `CutterBehavior.Pieces` is the one statement of which piece leaves by which side, and the
            // info card is written through it - so the sentence a player reads mid-split and the belts the
            // pieces actually land on cannot disagree. (The behaviour is internal to Core, so the public
            // seam it is read through is the card; that is the seam a view uses too.)
            SimWorld world = CutWorld(Dir.North, out Int2 front, out Int2 back);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));

            // Mid-split: the intake happens on the thirtieth tick and the cut takes forty-five more.
            Sim.Tick(world, 40);

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(4, 2), out MachineSnapshot snapshot));
            ReadingNote note = MachineReading.LiveNote(world, new Int2(4, 2), snapshot);

            StringAssert.Contains(Sim.TopHalfCircle.Describe() + " leaves north", note.Text,
                "the card promises the top half to the north outlet: " + note.Text);
            StringAssert.Contains(Sim.BottomHalfCircle.Describe() + " leaves south", note.Text,
                "and the bottom half to the south one: " + note.Text);

            TickUntil(world, 30 * 5, () => !Sim.ShapeAt(world, front).IsEmpty);

            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(world, front), "and the north outlet took it");
            Assert.AreEqual(Sim.BottomHalfCircle, Sim.ShapeAt(world, back), "and the south outlet the other");
        }

        [Test]
        public void AHorizontalCut_IsSelectedByNorthOrSouth_AndAVerticalOneByEastOrWest()
        {
            // Which line the facing selects, read off the machine's own card: the four facings are two
            // lines, and a player who wants a left/right split must point the machine east or west.
            SimWorld world = Sim.NewWorld(20, 12);
            MachineDef def = world.Content.Machine(BuildKind.Cutter);

            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.North);
            Sim.Place(world, BuildKind.Cutter, new Int2(8, 2), Dir.South);
            Sim.Place(world, BuildKind.Cutter, new Int2(12, 2), Dir.East);
            Sim.Place(world, BuildKind.Cutter, new Int2(16, 2), Dir.West);

            foreach (Int2 cell in new[] { new Int2(4, 2), new Int2(8, 2) })
            {
                Assert.IsTrue(world.Machines.TryGetSnapshot(cell, out MachineSnapshot northSouth));
                StringAssert.Contains("cuts top from bottom", MachineReading.FacingNote(def, northSouth),
                    cell + " selects the horizontal cut");
            }

            foreach (Int2 cell in new[] { new Int2(12, 2), new Int2(16, 2) })
            {
                Assert.IsTrue(world.Machines.TryGetSnapshot(cell, out MachineSnapshot eastWest));
                StringAssert.Contains("cuts left from right", MachineReading.FacingNote(def, eastWest),
                    cell + " selects the vertical cut");
            }
        }

        /// <summary>
        /// A cutter at (4,2) facing <paramref name="facing"/>, fed from the west by one belt and wired on
        /// both sides of its cut line with one dead-ended belt each. <paramref name="front"/> and
        /// <paramref name="back"/> are the cells those two belts occupy, which is the side the machine
        /// faces and the side opposite it - the same two cells whichever way it faces.
        /// </summary>
        private static SimWorld CutWorld(Dir facing, out Int2 front, out Int2 back)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), facing);
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 1);
            Sim.LayRun(world, new Int2(4, 1), Dir.South, 1);

            front = facing == Dir.South ? new Int2(4, 1) : new Int2(4, 3);
            back = facing == Dir.South ? new Int2(4, 3) : new Int2(4, 1);
            return world;
        }

        // ---------------------------------------------------------------- the rotator

        [Test]
        public void TheRotator_TurnsAQuarter_InThreeQuartersOfASecond()
        {
            // The interval matters and is short on purpose: well under the belt's one item per second, so
            // the cost of orientation repair is the machine and its footprint rather than its cycle time.
            // Pinned to the tick, because "0.75 s" and "1 s" are one number apart in the table and a whole
            // second apart in a player's line.
            SimWorld world = RotatorWorld(Dir.East, out Int2 outlet);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.BottomHalfCircle));

            Sim.Tick(world, 30 + 22);   // the intake at t=30, and 22 ticks is a shade under three quarters
            Assert.AreEqual(Sim.BottomHalfCircle, Sim.MachineAt(world, new Int2(4, 2)).Carried,
                "22 ticks in, the turn is not finished");
            Assert.AreEqual(Shape.None, Sim.ShapeAt(world, outlet), "so nothing has left yet");

            Sim.Tick(world, 1);         // tick 53: 0.75 s after the intake, rounded up
            Assert.AreEqual(Sim.LeftHalfCircle, Sim.ShapeAt(world, outlet),
                "and one tick later it is out, turned a quarter clockwise");
        }

        [Test]
        public void TheRotator_IsTheOrientationRepair()
        {
            // The design's own story (§2.3): the briefing wants a top half-circle and the line is running
            // bottom halves - one turn fixes it, and the machine costs three quarters of a second.
            SimWorld world = RotatorWorld(Dir.East, out Int2 outlet);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.BottomHalfCircle));
            TickUntil(world, 30 * 5, () => !Sim.ShapeAt(world, outlet).IsEmpty);
            Assert.AreEqual(Sim.LeftHalfCircle, Sim.ShapeAt(world, outlet), "a bottom half in, a left half out");

            world.Belts.TryTakeItem(outlet, out _);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.LeftHalfCircle));
            TickUntil(world, 30 * 5, () => !Sim.ShapeAt(world, outlet).IsEmpty);
            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(world, outlet),
                "and one more turn gives the top half the briefing asked for");
        }

        [Test]
        public void FourRotatorsInSeries_AreTheIdentity_AndTwoAreTheComplement()
        {
            // The algebra's four-cycle, as a line rather than a loop: each machine costs its own three
            // quarters of a second, and the item that comes out of four of them is the item that went into
            // the first. Counter-clockwise is three of these, which is the logistics puzzle the design
            // deliberately leaves as the player's problem.
            Assert.AreEqual(Sim.BottomHalfCircle, TurnInSeries(4, Sim.BottomHalfCircle),
                "four turns bring an item back to itself");
            Assert.AreEqual(Sim.TopHalfCircle, TurnInSeries(2, Sim.BottomHalfCircle),
                "and two give the complement piece, which is not the identity");
            Assert.AreEqual(Sim.LeftHalfCircle, TurnInSeries(1, Sim.BottomHalfCircle));
            Assert.AreEqual(Sim.RightHalfCircle, TurnInSeries(3, Sim.BottomHalfCircle),
                "so three is the counter-clockwise turn, on purpose");
        }

        [Test]
        public void ARotatorWithNowhereToPush_KeepsTheItemInsteadOfLosingIt()
        {
            // A rotator with no belt on the side it faces holds what it took - a visible mis-wiring rather
            // than a lost shape - and the turn still happens one interval after the intake. What must never
            // happen is the item disappearing: the piece is the player's, and a missing outlet is a mistake
            // they can fix.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Rotator, new Int2(4, 2), Dir.North);   // nothing to the north

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.BottomHalfCircle));

            Sim.Tick(world, 30 + 23);   // the intake at t=30, and one interval of three quarters of a second
            Assert.AreEqual(Sim.LeftHalfCircle, Sim.MachineAt(world, new Int2(4, 2)).Carried,
                "the turn happened, and the item is still the machine's problem");
            Assert.AreEqual(0, Sim.ItemCount(world), "nothing anywhere on a belt");
            Assert.AreEqual(0, world.Belts.JamCount, "and a mis-wiring is not a jam");

            Sim.Tick(world, 30 * 20);
            Assert.IsFalse(Sim.MachineAt(world, new Int2(4, 2)).Carried.IsEmpty,
                "twenty seconds later the item is still held, not lost");
            Assert.AreEqual(0, Sim.ItemCount(world), "and it never went anywhere");
        }

        /// <summary>A rotator at (4,2) facing <paramref name="facing"/>, fed from the west and wired one
        /// belt out the side it faces.</summary>
        private static SimWorld RotatorWorld(Dir facing, out Int2 outlet)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Rotator, new Int2(4, 2), facing);

            outlet = new Int2(4, 2) + facing.Offset();
            Sim.LayRun(world, outlet, facing, 1);
            return world;
        }

        /// <summary>Feed <paramref name="shape"/> through <paramref name="count"/> rotators in a row and
        /// return what comes off the far end. Each machine's facing is east, so the line runs west to east,
        /// and each machine's outlet is the next machine's input belt.</summary>
        private static Shape TurnInSeries(int count, Shape shape)
        {
            SimWorld world = Sim.NewWorld(24, 12);
            var input = new Int2(2, 2);
            Sim.LayRun(world, input, Dir.East, 1);

            Int2 cell = new Int2(3, 2);
            for (int i = 0; i < count; i++)
            {
                Sim.Place(world, BuildKind.Rotator, cell, Dir.East);
                Sim.LayRun(world, cell + Dir.East.Offset(), Dir.East, 1);
                cell = cell + Dir.East.Offset() * 2;
            }

            Int2 outlet = cell - Dir.East.Offset();

            Assert.IsTrue(world.Belts.TrySpawnItem(input, shape));
            TickUntil(world, 30 * 40, () => !Sim.ShapeAt(world, outlet).IsEmpty);
            return Sim.ShapeAt(world, outlet);
        }

        // ---------------------------------------------------------------- the stacker

        [Test]
        public void TheStacker_UnionsTwoDisjointPieces_InTwoSeconds()
        {
            // The combination operator, and the design's own mixed round: circle-left meets square-right
            // and the union is one item. The fuse is the premium on a mixed round - two seconds, the
            // longest cycle in the game - so it is pinned to the tick the machine actually takes it.
            SimWorld world = StackerWorld(out Int2 west, out Int2 north, out Int2 south, out Int2 outlet);

            Assert.IsTrue(world.Belts.TrySpawnItem(west, Sim.LeftHalfCircle));
            Assert.IsTrue(world.Belts.TrySpawnItem(north, Sim.RightHalfSquare));

            int paired = TickUntil(world, 30 * 5, () =>
            {
                MachineState m = Sim.MachineAt(world, new Int2(5, 9));
                return !m.Carried.IsEmpty && !m.Carried2.IsEmpty;
            });

            // Which hand holds which piece is the port scan's business - it reaches the north belt before
            // the west one - so the claim is that both are held, not which is which.
            MachineState stacker = Sim.MachineAt(world, new Int2(5, 9));
            Assert.IsTrue(
                (stacker.Carried == Sim.LeftHalfCircle && stacker.Carried2 == Sim.RightHalfSquare) ||
                (stacker.Carried == Sim.RightHalfSquare && stacker.Carried2 == Sim.LeftHalfCircle),
                "both pieces are held: " + stacker.Carried + " and " + stacker.Carried2);
            Assert.AreEqual(0, world.Belts.JamCount, "and holding two pieces is not a jam");
            Assert.Greater(paired, 0, "they were paired " + paired + " ticks in");

            // Forty-five ticks is a second and a half: past a one-second fuse and short of a two-second
            // one, so this is the tick that tells the two apart. The union then has to be out shortly after
            // the fuse, because the machine pushes it on the next tick.
            Sim.Tick(world, 45);
            Assert.AreEqual(0, Sim.MachineAt(world, new Int2(5, 9)).OutputCount,
                "a second and a half in, the fuse has not run out");
            Assert.IsTrue(Sim.MachineAt(world, new Int2(5, 9)).Carried2 == Sim.RightHalfSquare ||
                          Sim.MachineAt(world, new Int2(5, 9)).Carried == Sim.RightHalfSquare,
                "and both pieces are still the machine's");

            Sim.Tick(world, 20);
            Shape union = Shape.Of(Quad.Circle, Quad.Square, Quad.Square, Quad.Circle);
            Assert.AreEqual(union, Sim.ShapeAt(world, outlet),
                "and two seconds after the pair was complete, the union has left");

            MachineState after = Sim.MachineAt(world, new Int2(5, 9));
            Assert.IsTrue(after.Carried.IsEmpty && after.Carried2.IsEmpty, "with both hands free again");
        }

        [Test]
        public void AnOverlappingSecondPiece_WaitsOnItsOwnBelt_AndNothingJams()
        {
            // The stacker's one refusal, and it is a stall rather than a jam on purpose: a jam would
            // destroy the piece the player just spent a chain building, and the honest answer to "these two
            // do not fit" is a queue that says so. The machine skips the piece it cannot use and keeps
            // looking, so a later port holding the fitting piece is still taken.
            SimWorld world = StackerWorld(out Int2 west, out Int2 north, out Int2 south, out Int2 outlet);

            Assert.IsTrue(world.Belts.TrySpawnItem(west, Sim.LeftHalfCircle));
            TickUntil(world, 30 * 5, () => !Sim.MachineAt(world, new Int2(5, 9)).Carried.IsEmpty);

            // LeftHalfSquare wants the same two quadrants as the piece already held.
            Assert.IsTrue(Sim.LeftHalfSquare.Overlaps(Sim.LeftHalfCircle), "the test's premise");
            Assert.IsTrue(world.Belts.TrySpawnItem(north, Sim.LeftHalfSquare));
            Sim.Tick(world, 30 * 4);

            MachineState held = Sim.MachineAt(world, new Int2(5, 9));
            Assert.AreEqual(Sim.LeftHalfCircle, held.Carried, "the first piece is still the one held");
            Assert.IsTrue(held.Carried2.IsEmpty, "the piece that does not fit was not taken");
            Assert.AreEqual(Sim.LeftHalfSquare, Sim.ShapeAt(world, north),
                "it is still standing on the belt that brought it - a visible queue");
            Assert.AreEqual(0, world.Belts.JamCount, "and nothing jammed, so nothing was destroyed");
            Assert.AreEqual(0, held.OutputCount, "nothing was made of one piece alone");

            // The piece that does fit arrives by another port, and the machine takes it past the one that
            // does not - which is what "a later port may hold the piece that fits" means.
            Assert.IsTrue(world.Belts.TrySpawnItem(south, Sim.RightHalfSquare));
            int fused = TickUntil(world, 30 * 8, () => !Sim.ShapeAt(world, outlet).IsEmpty);

            Assert.Greater(fused, 0, "the fitting piece was taken and the union left");
            Assert.AreEqual(Shape.Of(Quad.Circle, Quad.Square, Quad.Square, Quad.Circle),
                Sim.ShapeAt(world, outlet));
            Assert.AreEqual(0, world.Belts.JamCount, "and the queued piece is still waiting on its belt");
        }

        [Test]
        public void TheOrderThePiecesArriveIn_DoesNotChangeTheUnion()
        {
            // Two belts can deliver in either order - which one the scan reaches first is a fact about the
            // map, not about the pieces - so the machine's answer cannot depend on it. This is the algebra's
            // commutativity, at a machine that has to choose which hand holds what.
            Assert.AreEqual(UnionInOrder(Sim.LeftHalfCircle, Sim.RightHalfSquare),
                UnionInOrder(Sim.RightHalfSquare, Sim.LeftHalfCircle),
                "the same two pieces, whichever way round they arrive");
        }

        [Test]
        public void AStackerWithNoOutlet_HoldsTheUnionUntilThereIsOne()
        {
            // One union at a time, and an outlet that will not take it stops the machine rather than
            // piling unions up invisibly. The piece is not lost: wiring an outlet lets it go.
            SimWorld world = Sim.NewWorld(20, 12);
            var stacker = new Int2(5, 9);
            Sim.LayRun(world, new Int2(4, 9), Dir.East, 1);
            Sim.LayRun(world, new Int2(5, 10), Dir.South, 1);
            Sim.Place(world, BuildKind.Stacker, stacker, Dir.East);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 9), Sim.LeftHalfCircle));
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 10), Sim.RightHalfSquare));
            Sim.Tick(world, 30 * 8);

            Assert.AreEqual(1, Sim.MachineAt(world, stacker).OutputCount, "the union is made and waits inside");
            Assert.AreEqual(Shape.Of(Quad.Circle, Quad.Square, Quad.Square, Quad.Circle),
                Sim.MachineAt(world, stacker).OutputShape);

            Sim.LayRun(world, new Int2(6, 9), Dir.East, 1);
            TickUntil(world, 30 * 5, () => !Sim.ShapeAt(world, new Int2(6, 9)).IsEmpty);

            Assert.AreEqual(Shape.Of(Quad.Circle, Quad.Square, Quad.Square, Quad.Circle),
                Sim.ShapeAt(world, new Int2(6, 9)), "and it leaves the moment an outlet exists");
            Assert.AreEqual(0, Sim.MachineAt(world, stacker).OutputCount);
        }

        /// <summary>
        /// A stacker at (5,9) with three belts around it: one feeding from the west, one from the north,
        /// one from the south, and one belt away to the east. The two northern belts are the "other" ports
        /// the intake scan reaches before the west one, which is what lets a test offer a piece that fits
        /// and a piece that does not and see which the machine takes.
        /// </summary>
        private static SimWorld StackerWorld(out Int2 west, out Int2 north, out Int2 south, out Int2 outlet)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            west = new Int2(4, 9);
            north = new Int2(5, 10);
            south = new Int2(5, 8);
            outlet = new Int2(6, 9);

            Sim.LayRun(world, west, Dir.East, 1);
            Sim.LayRun(world, north, Dir.South, 1);
            Sim.LayRun(world, south, Dir.North, 1);
            Sim.Place(world, BuildKind.Stacker, new Int2(5, 9), Dir.East);
            Sim.LayRun(world, outlet, Dir.East, 1);
            return world;
        }

        /// <summary>One stacker, fed two pieces in the order given, returning what it finally puts out.</summary>
        private static Shape UnionInOrder(Shape first, Shape second)
        {
            SimWorld world = StackerWorld(out Int2 west, out Int2 north, out _, out Int2 outlet);

            Assert.IsTrue(world.Belts.TrySpawnItem(west, first));
            TickUntil(world, 30 * 5, () => !Sim.MachineAt(world, new Int2(5, 9)).Carried.IsEmpty);
            Assert.IsTrue(world.Belts.TrySpawnItem(north, second));

            TickUntil(world, 30 * 12, () => !Sim.ShapeAt(world, outlet).IsEmpty);
            return Sim.ShapeAt(world, outlet);
        }

        // ---------------------------------------------------------------- shared

        /// <summary>Tick until <paramref name="until"/> holds, up to a cap. Returns the tick the condition
        /// first held on (counted from the call), or -1 if it never did - so a test can assert both that
        /// something happened and roughly when.</summary>
        private static int TickUntil(SimWorld world, int cap, System.Func<bool> until)
        {
            for (int i = 0; i < cap; i++)
            {
                if (until()) return i;

                world.Tick(InputCommand.None);
            }

            return until() ? cap : -1;
        }
    }
}
