using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The three operators, as machines rather than as algebra: the cutter that splits one item along a
    /// line, the rotator that turns one a quarter, and the stacker that unions two. Each of them is a rule
    /// about <em>time</em> and about where an item leaves on top of the algebra the algebra tests already pin,
    /// and each of those halves is asserted here: how long a cycle takes, what a blocked lane or outlet costs,
    /// and what the machine does with an item it cannot use yet.
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
            // The split is one decision, not two: both pieces leave at once, each by the front of its own tile,
            // so a two-lane line is never half-fed while the machine waits a cycle for the other piece. The
            // piece that keeps the lane the belt fed goes by the blade tile's front and the piece displaced a
            // lane over by the lane tile's.
            SimWorld world = CutWorld(Dir.North, out Int2 input, out Int2 straight, out Int2 displaced);

            Assert.IsTrue(world.Belts.TrySpawnItem(input, Sim.Circle));

            int landed = TickUntil(world, 30 * 5, () =>
                !Sim.ShapeAt(world, straight).IsEmpty || !Sim.ShapeAt(world, displaced).IsEmpty);

            Assert.AreEqual(Sim.LeftHalfCircle, Sim.ShapeAt(world, straight),
                "the piece on the machine's left kept the lane the belt fed");
            Assert.AreEqual(Sim.RightHalfCircle, Sim.ShapeAt(world, displaced),
                "and the one on its right left a lane over, in the same cycle");
            Assert.Greater(landed, 0, "both pieces arrived on the same tick, " + landed + " ticks in");
        }

        [Test]
        public void OppositeFlows_CutTheSameTwoPieces_AndSwapTheLanesTheyLeaveBy()
        {
            // A half turn of the flow does not change the fork: flowing north and flowing south both cut left
            // from right, so the same two pieces come out of the same item and they leave the way the line was
            // already going. What the half turn swaps is which lane each piece owns, and therefore which of
            // the machine's two slots it lands in: the left half keeps the lane flowing north, the right half
            // keeps it flowing south.
            SimWorld north = CutWorld(Dir.North, out Int2 northIn, out Int2 northStraight, out _);
            SimWorld south = CutWorld(Dir.South, out Int2 southIn, out Int2 southStraight, out _);

            Assert.IsTrue(north.Belts.TrySpawnItem(northIn, Sim.Circle));
            Assert.IsTrue(south.Belts.TrySpawnItem(southIn, Sim.Circle));
            TickUntil(north, 30 * 5, () => !Sim.ShapeAt(north, northStraight).IsEmpty);
            TickUntil(south, 30 * 5, () => !Sim.ShapeAt(south, southStraight).IsEmpty);

            Assert.AreNotEqual(northStraight, southStraight,
                "the test's premise: the two machines face opposite ways, so their fronts are not the same cell");

            Assert.AreEqual(Sim.LeftHalfCircle, Sim.ShapeAt(north, northStraight),
                "flowing north, the left half keeps the lane the belt fed");
            Assert.AreEqual(Sim.RightHalfCircle, Sim.ShapeAt(south, southStraight),
                "and flowing south the right half does - the same pair of pieces, the other lane each");

            // What the half turn moved: the piece the lane slot owns. That is the buffer assignment, and it is
            // exactly what turns "which way is the cutter pointing" into a decision about lanes.
            Assert.AreEqual(Sim.LeftHalfCircle, Sim.MachineAt(north, new Int2(4, 2)).OutputShape,
                "flowing north, the lane slot is the left piece's");
            Assert.AreEqual(Sim.RightHalfCircle, Sim.MachineAt(south, new Int2(4, 2)).OutputShape,
                "flowing south, it is the right piece's");
        }

        [Test]
        public void CuttingAQuarter_EmitsTheQuarterAndNothing()
        {
            // "An item can be cut at most twice" is a corollary, not a rule: a quarter already lies on one
            // side of every line that can cross it, so the machine's other lane gets nothing at all - not an
            // empty item, not a stall, just one piece out of the two fronts.
            foreach (int q in new[] { Shape.NW, Shape.NE, Shape.SE, Shape.SW })
            {
                SimWorld world = CutWorld(Dir.North, out Int2 input, out Int2 straight, out Int2 displaced);
                Shape quarter = Shape.Quarter(q, Quad.Circle);

                Assert.IsTrue(world.Belts.TrySpawnItem(input, quarter));
                TickUntil(world, 30 * 5, () =>
                    !Sim.ShapeAt(world, straight).IsEmpty || !Sim.ShapeAt(world, displaced).IsEmpty);

                Assert.AreEqual(1, Sim.ItemCount(world),
                    "a " + Shape.CornerCode(q) + " quarter is one piece, wherever the line falls");
                Assert.IsTrue(Sim.ShapeAt(world, straight) == quarter || Sim.ShapeAt(world, displaced) == quarter,
                    "and the piece is the quarter itself");

                MachineState cutter = Sim.MachineAt(world, new Int2(4, 2));
                Assert.IsTrue((cutter.OutputShape == quarter) ^ (cutter.OutputShape2 == quarter),
                    "exactly one of the two slots ever held a piece, and it was the quarter");
                Assert.IsTrue(cutter.OutputShape.IsEmpty || cutter.OutputShape2.IsEmpty,
                    "and the slot the empty lane owns is empty, not holding a hole");
            }
        }

        [Test]
        public void ABlockedOutlet_BuffersItsOwnPiece_AndLeavesTheOtherSideRunning()
        {
            // The second output slot's whole purpose, as a test: with the lane tile's front left unwired, the
            // cutter keeps cutting *for the other lane* until its own buffer for the stranded piece is full.
            // The pieces are never lost, the two lanes never interfere, and the machine's own ceiling is the
            // only thing that eventually stops it - which is what a player sees as a queue.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);       // input
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.North);
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 6);      // the blade tile's front, long enough to drain

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));
                TickUntil(world, 30 * 6, () => world.Belts.HasItemAt(new Int2(4, 3)));
                TickUntil(world, 30 * 6, () => world.Belts.TryTakeItem(new Int2(4, 3), out _));
            }

            Assert.AreEqual(0, world.Belts.JamCount, "a blocked outlet is backpressure, not a jam");
            Assert.AreEqual(3, Sim.MachineAt(world, new Int2(4, 2)).OutputCount2,
                "all three right halves are buffered in their own slot, none thrown away");

            Sim.LayRun(world, new Int2(5, 3), Dir.North, 1);
            TickUntil(world, 30 * 6, () => !Sim.ShapeAt(world, new Int2(5, 3)).IsEmpty);

            Assert.AreEqual(Sim.RightHalfCircle, Sim.ShapeAt(world, new Int2(5, 3)),
                "and the moment there is an outlet, the oldest piece leaves by it");
        }

        [Test]
        public void ACutterAlreadyQueueingOneShape_WaitsRatherThanRewritingItsQueue()
        {
            // A slot counts pieces of *one* shape, because one count cannot describe a queue of two different
            // items. So a cutter whose lane slot is filling with right half-circles and is then handed a square
            // has to wait: writing the square's piece into that slot would rewrite what is already queued,
            // and three halves the player cut are three halves - not whatever was cut last. The wait is a
            // stall on the machine, which is visible and safe, and never a jam on the belt.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);       // input
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.North);
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 6);      // the blade tile's front, long enough to drain

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));
                TickUntil(world, 30 * 6, () => world.Belts.HasItemAt(new Int2(4, 3)));
                TickUntil(world, 30 * 6, () => world.Belts.TryTakeItem(new Int2(4, 3), out _));
            }

            Assert.AreEqual(3, Sim.MachineAt(world, new Int2(4, 2)).OutputCount2,
                "the test's premise: three right half-circles are queued in the lane's slot");
            Assert.AreEqual(Sim.RightHalfCircle, Sim.MachineAt(world, new Int2(4, 2)).OutputShape2);

            // A square arrives, whose cut would put a right half-*square* in that very slot.
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Square));
            Sim.Tick(world, 30 * 8);

            MachineState after = Sim.MachineAt(world, new Int2(4, 2));
            Assert.AreEqual(Sim.RightHalfCircle, after.OutputShape2,
                "the queued pieces are still right half-circles");
            Assert.AreEqual(3, after.OutputCount2, "and there are still three of them");
            Assert.IsTrue(after.Carried.IsEmpty, "the square was not taken, so nothing is mid-split");
            Assert.AreEqual(Sim.Square, Sim.ShapeAt(world, new Int2(3, 2)),
                "it is still on the belt it arrived on: the machine waits for the slot to drain");
            Assert.AreEqual(0, world.Belts.JamCount, "a machine that waits is backpressure, not a jam");
        }

        [Test]
        public void TheReading_CallsTheTwoLanesTheSameWayTheTickDoes()
        {
            // `CutterBehavior.Pieces` is the one statement of which piece leaves by which lane, and the
            // info card is written through it - so the sentence a player reads mid-split and the belts the
            // pieces actually land on cannot disagree. (The behaviour is internal to Core, so the public
            // seam it is read through is the card; that is the seam a view uses too.)
            SimWorld world = CutWorld(Dir.North, out Int2 input, out Int2 straight, out Int2 displaced);
            Assert.IsTrue(world.Belts.TrySpawnItem(input, Sim.Circle));

            // Mid-split: the intake happens on the thirtieth tick and the cut takes forty-five more.
            Sim.Tick(world, 40);

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(4, 2), out MachineSnapshot snapshot));
            ReadingNote note = MachineReading.LiveNote(world, new Int2(4, 2), snapshot);

            StringAssert.Contains(Sim.LeftHalfCircle.Describe() + " runs straight on", note.Text,
                "the card promises the left half the lane the belt fed: " + note.Text);
            StringAssert.Contains(Sim.RightHalfCircle.Describe() + " leaves a lane to the east", note.Text,
                "and the right half a lane over: " + note.Text);

            TickUntil(world, 30 * 5, () => !Sim.ShapeAt(world, straight).IsEmpty);

            Assert.AreEqual(Sim.LeftHalfCircle, Sim.ShapeAt(world, straight), "and that lane took it");
            Assert.AreEqual(Sim.RightHalfCircle, Sim.ShapeAt(world, displaced), "and the other lane the other");
        }

        [Test]
        public void AFlowOfNorthOrSouth_CutsLeftFromRight_AndAFlowOfEastOrWest_TopFromBottom()
        {
            // The cut runs along the flow, so the four facings are two cuts: a player who wants a left/right
            // split runs the line north or south, and one who wants top from bottom runs it east or west. Read
            // off the machine's own card, and off the ghost's one-line label - the same question asked before
            // the machine exists, because that label is what the rotate buttons sit beside.
            SimWorld world = Sim.NewWorld(20, 12);
            MachineDef def = world.Content.Machine(BuildKind.Cutter);

            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.North);
            Sim.Place(world, BuildKind.Cutter, new Int2(8, 2), Dir.South);
            Sim.Place(world, BuildKind.Cutter, new Int2(12, 2), Dir.East);
            Sim.Place(world, BuildKind.Cutter, new Int2(16, 2), Dir.West);

            foreach (Int2 cell in new[] { new Int2(4, 2), new Int2(8, 2) })
            {
                Assert.IsTrue(world.Machines.TryGetSnapshot(cell, out MachineSnapshot northSouth));
                StringAssert.Contains("cutting left from right", MachineReading.FacingNote(def, northSouth),
                    cell + " runs north or south, so the cut is left from right");
            }

            foreach (Int2 cell in new[] { new Int2(12, 2), new Int2(16, 2) })
            {
                Assert.IsTrue(world.Machines.TryGetSnapshot(cell, out MachineSnapshot eastWest));
                StringAssert.Contains("cutting top from bottom", MachineReading.FacingNote(def, eastWest),
                    cell + " runs east or west, so the cut is top from bottom");
            }

            Assert.AreEqual("cuts left from right", MachineReading.GhostFacingNote(def, Dir.North),
                "and the ghost's label names the cut the click would make");
            Assert.AreEqual("cuts top from bottom", MachineReading.GhostFacingNote(def, Dir.East));
        }

        [Test]
        public void ACutter_IsFedFromTheBackOfEitherOfItsTiles()
        {
            // A machine on two tiles is fed on both: a belt delivering into the back of the lane tile cuts the
            // item exactly as one delivering into the back of the blade tile does, and both pieces leave by
            // the fronts. A cutter that could only be fed on the tile the player placed it on would be a wall
            // in the middle of a run, which is the one thing a machine that sits *in* a line must not be.
            SimWorld blade = FedCutter(new Int2(3, 2), out Int2 straight, out Int2 lane);
            SimWorld laneFed = FedCutter(new Int2(3, 1), out Int2 laneStraight, out Int2 laneOut);

            Assert.AreEqual(straight, laneStraight, "the test's premise: the same two fronts either way");
            Assert.AreEqual(lane, laneOut);

            Assert.IsTrue(blade.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));
            Assert.IsTrue(laneFed.Belts.TrySpawnItem(new Int2(3, 1), Sim.Circle));
            TickUntil(blade, 30 * 6, () => !Sim.ShapeAt(blade, lane).IsEmpty);
            TickUntil(laneFed, 30 * 6, () => !Sim.ShapeAt(laneFed, laneOut).IsEmpty);

            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(laneFed, straight),
                "the top half keeps the lane, whichever of the two tiles the belt fed");
            Assert.AreEqual(Sim.BottomHalfCircle, Sim.ShapeAt(laneFed, laneOut),
                "and the bottom half leaves the lane one over");
            Assert.AreEqual(Sim.ShapeAt(blade, straight), Sim.ShapeAt(laneFed, straight),
                "so the lane tile's own feed comes out as the blade tile's does");
            Assert.AreEqual(Sim.ShapeAt(blade, lane), Sim.ShapeAt(laneFed, laneOut),
                "and the other piece leaves a lane over either way");
        }

        [Test]
        public void TheFourFacings_SendTheLeftPieceStraightOn_AndTheRightOneALaneOver()
        {
            // The mapping the whole machine is, over all four facings, written out as a table of cells: the flow
            // decides which halves the cut makes - north and south cut left from right, east and west top from
            // bottom - and the piece on the machine's left always keeps the tile the belt fed while the piece on
            // its right leaves a lane over. Spelled with the cells, because "a lane over" is a cell on the map,
            // and a table the test worked out from the same helper the machine uses would say nothing.
            AssertCut(Dir.North, new Int2(4, 3), new Int2(5, 3), Sim.LeftHalfCircle, Sim.RightHalfCircle);
            AssertCut(Dir.East, new Int2(5, 2), new Int2(5, 1), Sim.TopHalfCircle, Sim.BottomHalfCircle);
            AssertCut(Dir.South, new Int2(4, 1), new Int2(3, 1), Sim.RightHalfCircle, Sim.LeftHalfCircle);
            AssertCut(Dir.West, new Int2(3, 2), new Int2(3, 3), Sim.BottomHalfCircle, Sim.TopHalfCircle);
        }

        /// <summary>
        /// One circle through a cutter at (4,2) flowing <paramref name="facing"/>: the piece the machine keeps
        /// in the lane the belt fed must land on <paramref name="straight"/> and the displaced piece on
        /// <paramref name="displaced"/>, which are the two fronts the footprint puts a lane to the machine's
        /// right of.
        /// </summary>
        private static void AssertCut(Dir facing, Int2 straight, Int2 displaced,
            Shape straightPiece, Shape displacedPiece)
        {
            SimWorld world = CutWorld(facing, out Int2 input, out Int2 straightOut, out Int2 laneOut);

            Assert.AreEqual(straight, straightOut,
                facing + " keeps the lane out of the front of the tile the belt fed");
            Assert.AreEqual(displaced, laneOut,
                facing + " drops the displaced piece out of the front of the lane tile");

            Assert.IsTrue(world.Belts.TrySpawnItem(input, Sim.Circle));
            TickUntil(world, 30 * 6, () =>
                !Sim.ShapeAt(world, straight).IsEmpty && !Sim.ShapeAt(world, displaced).IsEmpty);

            Assert.AreEqual(straightPiece, Sim.ShapeAt(world, straight),
                facing + " cuts " + straightPiece.Describe() +
                ": the piece on the machine's left keeps the lane");
            Assert.AreEqual(displacedPiece, Sim.ShapeAt(world, displaced),
                facing + " sends " + displacedPiece.Describe() + " a lane over");
        }

        /// <summary>
        /// A cutter at (4,2) flowing <paramref name="facing"/>, fed by one belt on the side opposite its lane
        /// - the side of the machine that is left over once the lane and the front are accounted for - and
        /// wired one dead-ended belt out of each of its two fronts. <paramref name="straight"/> and
        /// <paramref name="displaced"/> are the cells those two belts occupy: the front of the tile the belt
        /// fed and the front of the lane tile one over.
        /// </summary>
        private static SimWorld CutWorld(Dir facing, out Int2 input, out Int2 straight, out Int2 displaced)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Dir lane = MachineFootprint.LaneOf(facing);

            input = new Int2(4, 2) + lane.Opposite().Offset();
            Sim.LayRun(world, input, lane, 1);                    // pointing at the machine
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), facing);

            straight = new Int2(4, 2) + facing.Offset();
            displaced = new Int2(4, 2) + lane.Offset() + facing.Offset();
            Sim.LayRun(world, straight, facing, 1);
            Sim.LayRun(world, displaced, facing, 1);
            return world;
        }

        /// <summary>
        /// A cutter at (4,2) flowing east, with one belt into <paramref name="input"/> from the west and one
        /// dead-ended belt out of each of its two fronts. <paramref name="input"/> is the blade tile's back at
        /// (3,2) or the lane tile's at (3,1), which are the two cells a line can arrive on.
        /// </summary>
        private static SimWorld FedCutter(Int2 input, out Int2 straight, out Int2 lane)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, input, Dir.East, 1);
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.East);

            straight = new Int2(5, 2);                            // the blade tile's front, east
            lane = new Int2(5, 1);                                // and the lane tile's, one over
            Sim.LayRun(world, straight, Dir.East, 1);
            Sim.LayRun(world, lane, Dir.East, 1);
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
