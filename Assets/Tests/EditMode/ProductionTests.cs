using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the production half of the chain: the drill that mines a patch and the decomposer
    /// that splits a circle into the two half-circles a cannon eats. With the Core sink they are the only
    /// source of both currency and ammunition, so a bug here is a bug in "you cannot buy damage".
    /// </summary>
    public class ProductionTests
    {
        [Test]
        public void Drill_MinesOneItemPerSecond_OfItsPatchesShape()
        {
            SimWorld world = Sim.NewWorld(20, 12);           // Core at (8,4)-(11,7)
            world.Terrain.SetOre(new Int2(2, 2), ShapeType.Circle);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 3);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 2), Dir.East);

            Sim.Tick(world, 29);
            Assert.AreEqual(1, Sim.ItemCount(world), "a drill mines one item per second, not one per tick");

            Sim.Tick(world, 1);
            Assert.AreEqual(1, Sim.ItemCount(world), "tick 30 is still inside the first item's second");

            Sim.Tick(world, 1);
            Assert.AreEqual(2, Sim.ItemCount(world), "and the second item lands 30 ticks after the first");

            Assert.AreEqual(ShapeType.Circle, Sim.ShapeAt(world, new Int2(3, 2)),
                "the mined item carries the patch's shape, not a generic resource");
            Assert.AreEqual(0f, Sim.ProgressAt(world, new Int2(3, 2)), Sim.ProgressTol,
                "and it enters the belt at the belt's own speed");
        }

        [Test]
        public void Drill_OnlyOnAPatch_AndRemovingItRestoresThePatch()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            var patch = new Int2(2, 9);
            var bare = new Int2(6, 9);
            world.Terrain.SetOre(patch, ShapeType.Circle);

            Assert.IsFalse(world.TryPlace(BuildKind.Drill, bare, Dir.East), "a drill needs something to mine");
            Assert.IsFalse(world.TryPlace(BuildKind.Decomposer, patch, Dir.East), "a patch is not a machine site");
            Assert.IsFalse(world.TryPlace(BuildKind.Cannon, patch, Dir.East));

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
                    world.Terrain.SetOre(new Int2(4 + x, 4 + y), ShapeType.Circle);

            var centre = new Int2(5, 5);
            var overOre = new Int2(5, 6);

            Assert.IsTrue(world.CanPlace(BuildKind.Drill, centre), "the middle of a vein takes a drill");
            Assert.IsTrue(world.CanPlaceBelt(overOre), "and the ore beside it takes a belt");

            Sim.Place(world, BuildKind.Drill, centre, Dir.North);
            Sim.LayRun(world, overOre, Dir.North, 1);            // one belt over the vein, then off it
            Sim.LayRun(world, new Int2(5, 7), Dir.West, 3);      // (5,7) (4,7) (3,7), west and away

            // The trade, one line each way: the ore cell the belt crosses can no longer take the drill that
            // would have mined it, and the drill's own cell can no longer take a belt.
            Assert.IsFalse(world.CanPlace(BuildKind.Drill, overOre), "one cell cannot hold both");
            Assert.IsFalse(world.CanPlaceBelt(centre), "the drill is standing there");

            Sim.Tick(world, 90);

            Assert.Greater(Sim.ItemCount(world), 0, "the middle of the vein is mined after all");

            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    Assert.AreEqual(ShapeType.Circle, world.Terrain.OreAt(new Int2(4 + x, 4 + y)),
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
                world.Terrain.SetOre(ore, ShapeType.Square);

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
                Assert.AreEqual(Sim.TestCircles, world.Economy.Circles, "the " + kind + " was fully refunded");
            }
        }

        [Test]
        public void Drill_StallsWhenItsOutputIsBlocked_InsteadOfLosingItems()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            world.Terrain.SetOre(new Int2(2, 2), ShapeType.Circle);
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
        public void Decomposer_SplitsOneCircleIntoTwoHalves()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);        // input
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 2);        // output: (5,2) (6,2)

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), ShapeType.Circle));
            Sim.Tick(world, 90);

            // The circle is a second of travel plus a 1.5 s split: at t=3 the first half is on the output
            // belt and the second is still buffered behind it.
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(5, 2)),
                "a circle comes out as halves");

            Sim.Tick(world, 30);
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(6, 2)),
                "a second of belt later the first half is a cell along");
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(5, 2)),
                "and the second half is right behind it");

            Assert.AreEqual(2, Sim.ItemCount(world), "one circle became exactly two halves, no more");
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(3, 2)), "the input was eaten, not dropped");
        }

        [Test]
        public void Decomposer_SaturatesItsOutputBelt_AndCirclesQueueBehindIt()
        {
            // The bottleneck puzzle, as numbers: the split consumes 0.67 circles/s against the input belt's 1/s,
            // so circles back up behind a fed decomposer, while its halves leave at the belt's own 1/s - one
            // decomposer is one cannon's worth of full-rate ammo, no more.
            SimWorld world = Sim.NewWorld(32, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 20);       // a long tail for the halves to fill

            for (int i = 0; i < 21 * 30; i++)
            {
                world.Belts.TrySpawnItem(new Int2(3, 2), ShapeType.Circle);
                world.Tick(InputCommand.None);
            }

            int halves = 0;
            foreach (ItemSnapshot item in Sim.ItemsIn(world))
                if (item.Shape == ShapeType.HalfCircle) halves++;

            // First half at t=2.5, one per second after that: 21 seconds of feeding means 18 or so.
            Assert.GreaterOrEqual(halves, 14, "one half per second once the split is warm");
            Assert.LessOrEqual(halves, 19, "and no faster: the output belt is the ceiling");

            Assert.IsTrue(world.Belts.HasItemAt(new Int2(3, 2)),
                "circles arrive faster than the split eats them, so the input backs up");
        }

        [Test]
        public void Decomposer_HoldsItsHalves_WhenItsOutputIsBlocked()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);   // no belt in front

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), ShapeType.Circle));
            Sim.Tick(world, 90);

            MachineState machine = Sim.MachineAt(world, new Int2(4, 2));
            Assert.AreEqual(2, machine.OutputCount, "the split finished and the halves wait inside");
            Assert.AreEqual(ShapeType.None, machine.Carried, "the circle is consumed by the split");
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(3, 2)), "which leaves the input line free");

            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);
            Sim.Tick(world, 90);

            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(5, 2)),
                "and the halves leave once there is room");
        }

        [Test]
        public void Decomposer_JamsABeltThatDeliversAHalfCircleBack()
        {
            // The twist holds at the splitter's own input: a line that loops a half-circle back into the
            // decomposer is a wrong-shape delivery, and it jams like any other.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), ShapeType.HalfCircle));
            Sim.Tick(world, 60);

            Assert.IsTrue(world.Belts.IsJammed(new Int2(3, 2)), "the feeding belt pays for the loop");
            Assert.AreEqual(1, world.Belts.JamCount);
            Assert.AreEqual(ShapeType.None, Sim.MachineAt(world, new Int2(4, 2)).Carried,
                "and the decomposer never swallowed it");
        }

        [Test]
        public void Decomposer_SecondOutlet_LetsTheSecondHalfLeaveToo()
        {
            // Same machine, same feed, one belt out or two. A single outlet can only carry one half away, so the
            // other waits inside and the decomposer is throttled to the belt's 1/s; with a belt on each of two
            // sides both halves are away within a tick of the split and the machine runs at its own 1.33 halves/s.
            // The outlets are not stored: they are the belts pointing away, read fresh on every push.
            SimWorld one = FedDecomposer(secondOutlet: false);
            SimWorld two = FedDecomposer(secondOutlet: true);

            Sim.Tick(one, 90);
            Sim.Tick(two, 90);

            Assert.AreEqual(1, Sim.MachineAt(one, new Int2(4, 2)).OutputCount,
                "one outlet takes one half, so the other waits inside");
            Assert.AreEqual(1, Sim.ItemCount(one), "and only one half is out on a belt");
            Assert.AreEqual(0, Sim.MachineAt(two, new Int2(4, 2)).OutputCount,
                "a second outlet takes the other half");
            Assert.AreEqual(2, Sim.ItemCount(two), "the same one circle, both halves out");

            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(two, new Int2(5, 2)), "east outlet");
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(two, new Int2(4, 3)), "north outlet");
        }

        /// <summary>
        /// A decomposer at (4,2) fed by one circle, outlet east and - optionally - north too. Both outlets are
        /// one belt long and dead-ended, so a half that arrives stays where it landed.
        /// </summary>
        private static SimWorld FedDecomposer(bool secondOutlet)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);                     // input
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);                     // always: east outlet
            if (secondOutlet) Sim.LayRun(world, new Int2(4, 3), Dir.North, 1);  // belt pointing away north

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), ShapeType.Circle));
            return world;
        }

        [Test]
        public void Machines_ArePlacedByAimingTheDrag_AndByARotatedClick()
        {
            SimWorld world = Sim.NewWorld(24, 12);              // Core at (10,4)-(13,7)
            world.Terrain.SetOre(new Int2(3, 10), ShapeType.Circle);

            // A drag aims: the machine lands where the press was and faces the way the drag went.
            world.Tick(Gesture.Aim(new Int2(3, 10), BuildKind.Drill));
            world.Tick(Gesture.Aim(new Int2(6, 10), BuildKind.Drill));
            world.Tick(Gesture.Release(new Int2(6, 10), BuildKind.Drill));

            Assert.IsTrue(world.Machines.Has(new Int2(3, 10)), "the drill landed on the press cell");
            Assert.AreEqual(Dir.East, Sim.MachineAt(world, new Int2(3, 10)).Direction, "aimed east by the drag");
            Assert.IsFalse(world.Machines.Has(new Int2(6, 10)), "and nowhere else: a machine is one tile");

            // A click places with the ghost's facing, and Q/E turn the ghost.
            world.Tick(new InputCommand(cursorCell: new Int2(0, 0), selected: BuildKind.Cannon, rotateSteps: 1));
            Assert.AreEqual(Dir.South, world.PlacementDirection, "one clockwise step from the default east");

            world.Tick(Gesture.Aim(new Int2(16, 10), BuildKind.Cannon));
            world.Tick(Gesture.Release(new Int2(16, 10), BuildKind.Cannon));

            Assert.AreEqual(Dir.South, Sim.MachineAt(world, new Int2(16, 10)).Direction);

            // And placement rules still hold through the input path: a turret may not sit on a patch.
            world.Tick(new InputCommand(false, false, new Int2(3, 9), selected: BuildKind.Cannon));
            Assert.IsFalse(world.CanPlace(BuildKind.Cannon, new Int2(3, 10)));
            Assert.IsFalse(world.TryPlace(BuildKind.Cannon, new Int2(3, 10), Dir.East));
        }
    }
}
