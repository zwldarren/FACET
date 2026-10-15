using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the production half of the chain: the drill that mines a vein and the cutter that
    /// opens the item into the two pieces a briefing can ask for. With the Core sink they are the only
    /// source of both currency and ammunition, so a bug here is a bug in "you cannot buy damage".
    ///
    /// The splitter this file used to test was the decomposer, which ran a recipe chosen by the delivered
    /// shape. Processing is an operator now: the cutter cuts whatever it is handed along the flow its facing
    /// names and stands on two tiles, so the two pieces leave <b>at once, one out of each tile's front</b> -
    /// and the tests below are about the cut, the pair of tiles, and what a blocked front costs.
    /// </summary>
    public class ProductionTests
    {
        [Test]
        public void Drill_MinesOneItemPerSecond_OfItsPatchesShape()
        {
            SimWorld world = Sim.NewWorld(20, 12);           // Core at (8,4)-(11,7)
            world.Terrain.SetOre(new Int2(2, 2), Quad.Circle);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 3);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 2), Dir.East);

            Sim.Tick(world, 29);
            Assert.AreEqual(1, Sim.ItemCount(world), "a drill mines one item per second, not one per tick");

            Sim.Tick(world, 1);
            Assert.AreEqual(1, Sim.ItemCount(world), "tick 30 is still inside the first item's second");

            Sim.Tick(world, 1);
            Assert.AreEqual(2, Sim.ItemCount(world), "and the second item lands 30 ticks after the first");

            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, new Int2(3, 2)),
                "the mined item is the full shape of the patch's mineral, not a generic resource");
            Assert.AreEqual(0f, Sim.ProgressAt(world, new Int2(3, 2)), Sim.ProgressTol,
                "and it enters the belt at the belt's own speed");
        }

        [Test]
        public void Drill_OnlyOnAPatch_AndRemovingItRestoresThePatch()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            var patch = new Int2(2, 9);
            var bare = new Int2(6, 9);
            world.Terrain.SetOre(patch, Quad.Circle);

            Assert.IsFalse(world.TryPlace(BuildKind.Drill, bare, Dir.East), "a drill needs something to mine");
            Assert.IsFalse(world.TryPlace(BuildKind.Cutter, patch, Dir.East), "a patch is not a machine site");
            Assert.IsFalse(world.TryPlace(BuildKind.Turret, patch, Dir.East));

            Assert.IsTrue(world.TryPlace(BuildKind.Drill, patch, Dir.East));
            Assert.AreEqual(TileKind.Drill, world.TileGrid.Get(patch));

            Assert.IsTrue(world.TryRemoveBuilding(patch));
            Assert.IsFalse(world.TileGrid.Get(patch).IsBuilding(), "nothing is standing there again");
            Assert.IsTrue(world.Terrain.HasOre(patch), "the patch is still in the ground");
            Assert.IsTrue(world.TryPlace(BuildKind.Drill, patch, Dir.East), "so it can be drilled again");
        }

        [Test]
        public void Drill_MinesTheInteriorOfAWidePatch_ByThreadingABeltOverTheVein()
        {
            // The reason transport may cross ore, as a test: a drill pushes onto the tile beside it, so inside a
            // 3x3 vein every neighbour of the middle cell is ore - the centre took a drill and then mined nothing,
            // forever, with nothing on screen to say why. A belt laid across the vein is what makes the middle one
            // work, at the cost of the drill that would have stood on the belt's cell. Both halves are asserted:
            // the centre cell mines, and every cell of the vein is still ore.
            SimWorld world = Sim.NewWorld(20, 12);

            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    world.Terrain.SetOre(new Int2(4 + x, 4 + y), Quad.Circle);

            var centre = new Int2(5, 5);
            var overOre = new Int2(5, 6);

            Assert.IsTrue(world.CanPlace(BuildKind.Drill, centre, Dir.East), "the middle of a vein takes a drill");
            Assert.IsTrue(world.CanPlaceBelt(overOre), "and the ore beside it takes a belt");

            Sim.Place(world, BuildKind.Drill, centre, Dir.North);
            Sim.LayRun(world, overOre, Dir.North, 1);            // one belt over the vein, then off it
            Sim.LayRun(world, new Int2(5, 7), Dir.West, 3);      // (5,7) (4,7) (3,7), west and away

            // The trade, one line each way: the ore cell the belt crosses can no longer take the drill that
            // would have mined it, and the drill's own cell can no longer take a belt.
            Assert.IsFalse(world.CanPlace(BuildKind.Drill, overOre, Dir.East), "one cell cannot hold both");
            Assert.IsFalse(world.CanPlaceBelt(centre), "the drill is standing there");

            Sim.Tick(world, 90);

            Assert.Greater(Sim.ItemCount(world), 0, "the middle of the vein is mined after all");

            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    Assert.AreEqual(Quad.Circle, world.Terrain.OreAt(new Int2(4 + x, 4 + y)),
                        "the belt did not eat the ore it crossed");
        }

        [Test]
        public void EveryBuilding_GivesTheGroundBack_WhenRemoved()
        {
            // The invariant the belt-on-ore rule leans on, stated once for the whole table rather than for the
            // two kinds that stand on a patch today. Removing a building must clear the building, not the ground:
            // ore is the map's data, and no player action may erase it. A patch is the only terrain a building can
            // stand on, so also the only terrain a removal can silently destroy - hence looping over BuildCatalog.
            foreach (BuildKind kind in BuildCatalog.All)
            {
                SimWorld world = Sim.NewWorld(20, 12);
                var ore = new Int2(6, 6);
                var bare = new Int2(12, 6);
                world.Terrain.SetOre(ore, Quad.Square);

                // A drill mines ore; every other kind wants bare ground. Belts are the one kind that takes
                // either, which is the point of the rule.
                bool onOre = kind == BuildKind.Drill || kind == BuildKind.Belt;
                Int2 cell = onOre ? ore : bare;

                Assert.IsTrue(world.TryPlace(kind, cell, Dir.East),
                    kind + " could not be placed on " + cell + " at all");
                Assert.AreEqual(onOre, world.Terrain.HasOre(cell),
                    kind + " left the ground under it wrong while it stood there");

                Assert.IsTrue(world.TryRemoveBuilding(cell), kind + " could not be removed");
                Assert.AreEqual(onOre, world.Terrain.HasOre(cell), "removing " + kind + " took the ground with it");
                Assert.AreEqual(TileKind.Empty, world.TileGrid.Get(cell),
                    "and nothing is standing on the " + kind + "'s tile: the ground is not a tile kind");
                Assert.AreEqual(Sim.TestShapes, world.Economy.Shapes, "the " + kind + " was fully refunded");
            }
        }

        [Test]
        public void Drill_StallsWhenItsOutputIsBlocked_InsteadOfLosingItems()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            world.Terrain.SetOre(new Int2(2, 2), Quad.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 2), Dir.East);   // facing bare ground

            Sim.Tick(world, 300);
            Assert.AreEqual(0, Sim.ItemCount(world), "nowhere to put an item means the drill waits");

            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Tick(world, 30);
            Assert.AreEqual(1, Sim.ItemCount(world));

            Assert.IsTrue(world.Belts.TryJam(new Int2(3, 2)));
            Sim.Tick(world, 300);
            Assert.AreEqual(1, Sim.ItemCount(world), "a blocked line must not create or destroy items");
        }

        [Test]
        public void TheCutter_SplitsOneCircleIntoTwoHalves()
        {
            // One line, one cut: the cut runs along the flow, so a machine flowing north cuts left from right,
            // and each piece leaves by the front of its own tile - the left half by the tile the belt fed, the
            // right half a lane over. Both are out within a tick of each other: the machine does not choose one
            // to emit first.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);        // input
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.North);
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 2);       // the blade tile's front: (4,3) (4,4)
            Sim.LayRun(world, new Int2(5, 3), Dir.North, 1);      // and the lane tile's, dead-ended

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));
            Sim.Tick(world, 90);

            // The circle is a second of travel plus a 1.5 s cut: at t=3 the pieces are out, one on each belt.
            Assert.AreEqual(Sim.LeftHalfCircle, Sim.ShapeAt(world, new Int2(4, 3)),
                "a circle comes out as halves, and the piece on the machine's left keeps the lane it fed");

            Sim.Tick(world, 30);
            Assert.AreEqual(Sim.LeftHalfCircle, Sim.ShapeAt(world, new Int2(4, 4)),
                "a second of belt later the first half is a cell along");
            Assert.AreEqual(Sim.RightHalfCircle, Sim.ShapeAt(world, new Int2(5, 3)),
                "and the other half is on its own lane, not queued behind it");

            Assert.AreEqual(2, Sim.ItemCount(world), "one circle became exactly two halves, no more");
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(3, 2)), "the input was eaten, not dropped");
        }

        [Test]
        public void TheCutter_EmitsBothPiecesAtOnce_SoOneFrontThrottlesItToHalf()
        {
            // What the machine's two fronts are worth, as numbers. The rework gave it a second output slot
            // precisely for this: with one front unwired, the piece that cannot leave waits <em>in its own
            // slot</em> rather than being lost or displacing the other - and the next cut waits for both
            // slots, so a single wired front halves the machine. The pieces are never lost either way.
            SimWorld one = FedCutter(secondFront: false);
            SimWorld two = FedCutter(secondFront: true);

            Sim.Tick(one, 90);
            Sim.Tick(two, 90);

            Assert.AreEqual(1, Sim.MachineAt(one, new Int2(4, 2)).OutputCount2,
                "one front takes one half, so the other waits inside its own slot");
            Assert.AreEqual(1, Sim.ItemCount(one), "and only one half is out on a belt");

            Assert.AreEqual(0, Sim.MachineAt(two, new Int2(4, 2)).OutputCount2,
                "a second front takes the other half");
            Assert.AreEqual(2, Sim.ItemCount(two), "the same one circle, both halves out");

            Assert.AreEqual(Sim.LeftHalfCircle, Sim.ShapeAt(two, new Int2(4, 3)), "the tile the belt fed");
            Assert.AreEqual(Sim.RightHalfCircle, Sim.ShapeAt(two, new Int2(5, 3)), "and the lane tile one over");
        }

        /// <summary>
        /// A cutter at (4,2) flowing north, fed one circle from the west and wired out of the blade tile's
        /// front at (4,3) and - optionally - the lane tile's at (5,3) too. Both fronts are one belt long and
        /// dead-ended, so a half that arrives stays where it landed.
        /// </summary>
        private static SimWorld FedCutter(bool secondFront)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);                     // input
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.North);
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 1);                    // always: the blade tile's front
            if (secondFront) Sim.LayRun(world, new Int2(5, 3), Dir.North, 1);   // the lane tile's, a lane over

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));
            return world;
        }

        [Test]
        public void TheCutter_SaturatesItsOwnCycle_AndCirclesQueueBehindIt()
        {
            // The bottleneck puzzle, as numbers. The cut consumes one item per 1.5 s against the line's 1/s,
            // so circles back up behind a fed cutter no matter how long the fronts are - while the two pieces
            // leave at the belts' own 1/s each, which is why one cutter is one pair of lines' worth of pieces
            // and no more. Both fronts are wired and long: with one of them short, the pieces that cannot
            // leave would fill the machine's own slot and stall it (the test above).
            SimWorld world = Sim.NewWorld(40, 12);                            // Core at (18,4)-(21,7)
            var cutter = new Int2(20, 10);
            var input = new Int2(20, 11);

            Sim.LayRun(world, input, Dir.South, 1);                           // into the cutter from the north
            Sim.Place(world, BuildKind.Cutter, cutter, Dir.East);             // flowing east: top from bottom
            Sim.LayRun(world, new Int2(21, 10), Dir.East, 19);                // (21,10) .. (39,10)
            Sim.LayRun(world, new Int2(21, 9), Dir.East, 19);                 // (21,9) .. (39,9), a lane over

            for (int i = 0; i < 21 * 30; i++)
            {
                world.Belts.TrySpawnItem(input, Sim.Circle);
                world.Tick(InputCommand.None);
            }

            int top = Sim.CountOf(world, Sim.TopHalfCircle);
            int bottom = Sim.CountOf(world, Sim.BottomHalfCircle);

            // First pair at t=2.5, one pair per 1.5 s after that: 21 seconds of feeding is thirteen pairs.
            Assert.GreaterOrEqual(top + bottom, 20, "one pair per cut once the machine is warm");
            Assert.LessOrEqual(top + bottom, 30, "and no faster: the cut's own 1.5 s is the ceiling");
            Assert.AreEqual(top, bottom, "every cut makes one of each piece - neither lane is favoured");

            Assert.IsTrue(world.Belts.HasItemAt(input),
                "circles arrive faster than the cut eats them, so the input backs up");
        }

        [Test]
        public void ACutter_WhoseSecondLaneGoesNowhere_ReadsAsStalled()
        {
            // The stoppage the HUD could not see: the machine has eaten its item and the displaced piece has
            // nowhere to go. Nothing upstream of it is ever "blocked" (the cutter would take those items) and
            // nothing was refused, so this used to be a factory standing still in silence.
            SimWorld world = CutterRig(secondLane: false);
            Sim.Tick(world, 600);

            MachineState cutter = Sim.MachineAt(world, new Int2(4, 6));
            Assert.Greater(cutter.OutputCount2, 0,
                "the second lane's slot is holding the piece it cannot pass on");
            Assert.AreEqual(Shape.None, cutter.Carried,
                "and the machine has stopped eating, which is what makes the line behind it queue");
            Assert.GreaterOrEqual(cutter.StallTicks, MachineField.StallTicks,
                "and it has been saying so for longer than the HUD's threshold");

            world.Machines.CountStalled(out int stalled);
            Assert.AreEqual(1, stalled, "one machine, which is the number the card prints");

            var stopped = new System.Collections.Generic.List<Int2>();
            world.Machines.GetStalled(stopped);
            Assert.AreEqual(1, stopped.Count);
            Assert.AreEqual(new Int2(4, 6), stopped[0], "and the locator is pointed at the machine itself");
        }

        [Test]
        public void ACutter_WithBothLanesWired_IsNotStalled()
        {
            // The other half of the claim, and the reason the counter is worth printing: a machine that is
            // merely busy - its input backed up behind a slower cut, which is every healthy cutter - is not
            // stopped, and the HUD must not say it is.
            SimWorld world = CutterRig(secondLane: true);
            Sim.Tick(world, 600);

            MachineState cutter = Sim.MachineAt(world, new Int2(4, 6));
            Assert.Less(cutter.StallTicks, MachineField.StallTicks, "both pieces have somewhere to go");
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(4, 5)),
                "the line behind it is backed up, which is the busy case and not the stopped one");

            world.Machines.CountStalled(out int stalled);
            Assert.AreEqual(0, stalled);
        }

        /// <summary>
        /// A drill, one belt cell, and a cutter flowing north with the lane the belt fed wired out to a sink -
        /// the smallest line whose second lane can be the problem. With <paramref name="secondLane"/> the
        /// displaced piece's lane is wired to a sink of its own as well.
        /// </summary>
        private static SimWorld CutterRig(bool secondLane)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            world.Terrain.SetOre(new Int2(4, 4), Quad.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(4, 4), Dir.North);
            Sim.LayRun(world, new Int2(4, 5), Dir.North, 1);                   // into the cutter's fed tile
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 6), Dir.North);    // tiles (4,6) and (5,6)

            Sim.LayRun(world, new Int2(4, 7), Dir.North, 1);                  // the piece that keeps the lane
            Sim.Place(world, BuildKind.Sink, new Int2(4, 8), Dir.North);

            if (secondLane)
            {
                Sim.LayRun(world, new Int2(5, 7), Dir.North, 1);              // the displaced piece's lane
                Sim.Place(world, BuildKind.Sink, new Int2(5, 8), Dir.North);
            }

            return world;
        }

        [Test]
        public void TheCutter_HoldsBothPieces_WhenNeitherFrontIsWired()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.North);   // no belt on either front

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));
            Sim.Tick(world, 90);

            MachineState machine = Sim.MachineAt(world, new Int2(4, 2));
            Assert.AreEqual(1, machine.OutputCount,
                "the cut finished and the piece keeping the lane waits inside");
            Assert.AreEqual(1, machine.OutputCount2, "with the displaced piece in its own slot beside it");
            Assert.AreEqual(Sim.LeftHalfCircle, machine.OutputShape);
            Assert.AreEqual(Sim.RightHalfCircle, machine.OutputShape2);
            Assert.IsTrue(machine.Carried.IsEmpty, "the circle is consumed by the cut");
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(3, 2)), "which leaves the input line free");

            Sim.LayRun(world, new Int2(4, 3), Dir.North, 1);
            Sim.LayRun(world, new Int2(5, 3), Dir.North, 1);
            Sim.Tick(world, 90);

            Assert.AreEqual(Sim.LeftHalfCircle, Sim.ShapeAt(world, new Int2(4, 3)),
                "and the pieces leave once there is room for them");
            Assert.AreEqual(Sim.RightHalfCircle, Sim.ShapeAt(world, new Int2(5, 3)));
        }

        [Test]
        public void ACutter_NeedsBothItsTilesClear_BecauseTheLaneIsPartOfTheMachine()
        {
            // The second tile is the machine, so it is refused for the same reasons the first is: a belt where
            // the lane would land leaves the ghost red, and so does the edge of the map. What must never happen
            // is a cutter half-built over a belt - a machine whose displaced piece has nowhere to leave and a
            // lane the player cannot see. Turning the ghost a quarter turns the footprint with it, so the same
            // cell is refused flowing east and allowed flowing north.
            SimWorld world = Sim.NewWorld(20, 12);
            var cell = new Int2(6, 6);
            Sim.LayRun(world, new Int2(6, 5), Dir.East, 1);      // where an east-flowing cutter's lane lands

            Assert.IsFalse(world.CanPlace(BuildKind.Cutter, cell, Dir.East), "the lane tile holds a belt");
            Assert.IsFalse(world.TryPlace(BuildKind.Cutter, cell, Dir.East), "so nothing is built there");
            Assert.AreEqual(TileKind.Belt, world.TileGrid.Get(new Int2(6, 5)), "and the belt is still a belt");
            Assert.AreEqual(TileKind.Empty, world.TileGrid.Get(cell), "with no half of a cutter on the press");

            Assert.IsTrue(world.CanPlace(BuildKind.Cutter, cell, Dir.North),
                "the same cell flowing north puts its lane on bare ground, and is allowed");
            Assert.IsTrue(world.TryPlace(BuildKind.Cutter, cell, Dir.North), "so the ghost told the truth");

            Assert.IsFalse(world.CanPlace(BuildKind.Cutter, new Int2(0, 0), Dir.South),
                "a lane off the map is no lane");
            Assert.IsTrue(world.CanPlace(BuildKind.Cutter, new Int2(0, 0), Dir.North),
                "and the same cell has one in bounds");
        }

        [Test]
        public void Machines_ArePlacedByAimingTheDrag_AndByARotatedClick()
        {
            SimWorld world = Sim.NewWorld(24, 12);              // Core at (10,4)-(13,7)
            world.Terrain.SetOre(new Int2(3, 10), Quad.Circle);

            // A drag aims: the machine lands where the press was and faces the way the drag went.
            world.Tick(Gesture.Aim(new Int2(3, 10), BuildKind.Drill));
            world.Tick(Gesture.Aim(new Int2(6, 10), BuildKind.Drill));
            world.Tick(Gesture.Release(new Int2(6, 10), BuildKind.Drill));

            Assert.IsTrue(world.Machines.Has(new Int2(3, 10)), "the drill landed on the press cell");
            Assert.AreEqual(Dir.East, Sim.MachineAt(world, new Int2(3, 10)).Direction, "aimed east by the drag");
            Assert.IsFalse(world.Machines.Has(new Int2(6, 10)), "and nowhere else: a machine is one tile");

            // A click places with the ghost's facing, and Q/E turn the ghost.
            world.Tick(new InputCommand(cursorCell: new Int2(0, 0), selected: BuildKind.Turret, rotateSteps: 1));
            Assert.AreEqual(Dir.South, world.PlacementDirection, "one clockwise step from the default east");

            world.Tick(Gesture.Aim(new Int2(16, 10), BuildKind.Turret));
            world.Tick(Gesture.Release(new Int2(16, 10), BuildKind.Turret));

            Assert.AreEqual(Dir.South, Sim.MachineAt(world, new Int2(16, 10)).Direction);

            // And placement rules still hold through the input path: a turret may not sit on a patch.
            world.Tick(new InputCommand(false, false, new Int2(3, 9), selected: BuildKind.Turret));
            Assert.IsFalse(world.CanPlace(BuildKind.Turret, new Int2(3, 10), Dir.East));
            Assert.IsFalse(world.TryPlace(BuildKind.Turret, new Int2(3, 10), Dir.East));
        }
    }
}
