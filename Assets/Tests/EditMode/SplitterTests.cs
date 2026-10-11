using System;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the splitter: the one-tile balancer whose ports are nothing but the belts
    /// around it - a belt pointing in is an input, a belt pointing away is an output - so merging,
    /// splitting and balancing are the same building on different streets.
    ///
    /// The worlds are the 20x12 test map, whose Core sits at (8,4)-(11,7), and the hub sits at (4,5):
    /// every line here is short and clear of the Core, because a belt pointing into the Core is a
    /// delivery and the sink would eat the very items the test is counting.
    /// </summary>
    public class SplitterTests
    {
        private static readonly Int2 Hub = new Int2(4, 5);

        /// <summary>How many items sit in a region - the count that says where a hub dealt them.</summary>
        private static int CountWhere(SimWorld world, Predicate<Vec2> inRegion)
        {
            int count = 0;
            foreach (ItemSnapshot item in Sim.ItemsIn(world))
                if (inRegion(item.Position)) count++;
            return count;
        }

        /// <summary>East of the hub at (4,5).</summary>
        private static int East(SimWorld world) => CountWhere(world, p => p.X > 4.5f);

        /// <summary>West of the hub.</summary>
        private static int West(SimWorld world) => CountWhere(world, p => p.X < 3.5f);

        /// <summary>The column north of the hub, above it.</summary>
        private static int North(SimWorld world) => CountWhere(world, p => p.X >= 4f && p.X < 5f && p.Y > 5.5f);

        [Test]
        public void OneIn_TwoOut_Alternates()
        {
            // South input, east and west outputs: four circles in, and the hub deals them alternately
            // like cards - the round-robin is what makes one decomposer share two cannons evenly.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);
            Sim.Place(world, BuildKind.Splitter, Hub, Dir.North);
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 2);    // east line
            Sim.LayRun(world, new Int2(3, 5), Dir.West, 3);    // west line

            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle));
                Sim.Tick(world, 45);
            }
            Sim.Tick(world, 120);

            Assert.AreEqual(2, East(world), "two went east");
            Assert.AreEqual(2, West(world), "two went west");
            Assert.AreEqual(4, Sim.ItemCount(world), "a splitter moves items, it never eats them");
            Assert.AreEqual(ShapeType.None, Sim.MachineAt(world, Hub).Carried, "and the hub empties");
        }

        [Test]
        public void ThreeIn_OneOut_Merges()
        {
            // The same building as a merger: three lines pointing in, one pointing away. All three
            // items leave north - which one goes first is the fixed port order, not chance.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);   // south in
            Sim.LayRun(world, new Int2(3, 5), Dir.East, 1);    // west in
            Sim.LayRun(world, new Int2(5, 5), Dir.West, 1);    // east in
            Sim.Place(world, BuildKind.Splitter, Hub, Dir.North);
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 3);   // north out

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle));
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 5), ShapeType.HalfCircle));
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 5), ShapeType.HalfCircle));
            Sim.Tick(world, 180);

            Assert.AreEqual(3, North(world), "all three left by the one output");
            Assert.AreEqual(3, Sim.ItemCount(world));
        }

        [Test]
        public void TwoIn_TwoOut_Balances()
        {
            // The balancer proper: two feeds, two destinations, and the hub deals 2 and 2 out of 4,
            // not 4 and 0 - the round-robin is the whole point of putting one in.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);   // south in
            Sim.LayRun(world, new Int2(3, 5), Dir.East, 1);    // west in
            Sim.Place(world, BuildKind.Splitter, Hub, Dir.North);
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 2);    // east out
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 2);   // north out

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle));
            Sim.Tick(world, 45);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 5), ShapeType.Circle));
            Sim.Tick(world, 45);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle));
            Sim.Tick(world, 45);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 5), ShapeType.Circle));
            Sim.Tick(world, 180);

            Assert.AreEqual(2, East(world), "two went east");
            Assert.AreEqual(2, North(world), "two went north");
            Assert.AreEqual(4, Sim.ItemCount(world));
        }

        [Test]
        public void ABlockedOutput_IsSkipped_NotStuckBehind()
        {
            // One output jammed solid: the hub deals around it. An overflow, not a deadlock.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);
            Sim.Place(world, BuildKind.Splitter, Hub, Dir.North);
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 2);
            Sim.LayRun(world, new Int2(3, 5), Dir.West, 2);

            Assert.IsTrue(world.Belts.TryJam(new Int2(5, 5)), "the east output is jammed");

            for (int i = 0; i < 2; i++)
            {
                Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle));
                Sim.Tick(world, 60);
            }

            Assert.AreEqual(2, West(world), "both went west instead");
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(5, 5)), "and none touched the jammed belt");
        }

        [Test]
        public void AFullOutput_Backpressures()
        {
            // Nowhere to go at all: the hub holds its one item and stops taking, so the input line
            // backs up behind it - the same honest wait a drill shows, not item loss.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 2);   // south in: (4,3) (4,4)
            Sim.Place(world, BuildKind.Splitter, Hub, Dir.North);
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 1);    // the only output, one cell, full

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 5), ShapeType.HalfCircle));   // parks there
            Sim.Tick(world, 60);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 3), ShapeType.Circle));
            Sim.Tick(world, 90);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 3), ShapeType.Circle), "a second follows it");
            Sim.Tick(world, 120);

            Assert.AreEqual(ShapeType.Circle, Sim.MachineAt(world, Hub).Carried,
                "the hub holds the one it took");
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(5, 5)),
                "the full output is untouched");
            Assert.AreEqual(2, Sim.ItemCount(world),
                "two items on belts - the one parked on the line and the blocker - plus the one the " +
                "hub holds: three in the world, none lost");
        }

        [Test]
        public void ABeltRunningPast_IsNotAPort()
        {
            // A belt beside the hub pointing along, neither in nor away, is not part of the hub: the
            // port roles come from belt directions, so a passing line is ignored, not robbed.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);   // south in
            Sim.Place(world, BuildKind.Splitter, Hub, Dir.North);
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 2);   // north out
            Sim.LayRun(world, new Int2(5, 4), Dir.North, 3);   // the passing line: (5,4)..(5,6)

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle));
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 4), ShapeType.HalfCircle));
            Sim.Tick(world, 150);

            Assert.AreEqual(ShapeType.Circle, Sim.ShapeAt(world, new Int2(4, 7)), "the circle went north");
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(5, 6)),
                "and the passing belt kept its own item");
            Assert.AreEqual(2, Sim.ItemCount(world));
        }
    }
}
