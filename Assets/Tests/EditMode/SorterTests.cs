using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the sorter: the router. It is a splitter's hub - its ports are nothing but
    /// the belts around it, a belt pointing in is an input, a belt pointing away is an outlet - with
    /// one bit of data a splitter does not have, the shape that leaves by the side it faces.
    ///
    /// The worlds are the 20x12 test map, whose Core sits at (8,4)-(11,7), and the hub sits at (4,5):
    /// every line is short and clear of the Core, because a belt pointing into the Core is a delivery
    /// and the sink would eat the very items the test is counting.
    ///
    /// The shipped filter is a circle, so these tests read as "money goes where it faces, ammo carries
    /// on" - the split map 1 wants.
    /// </summary>
    public class SorterTests
    {
        private static readonly Int2 Hub = new Int2(4, 5);

        /// <summary>
        /// South input, east outlet (the side it faces, so the filter shape's way), north outlet
        /// (everything else's way). One belt in, two belts out, both ending in a dead end so an item
        /// that arrives stays put and can be read off the cell it stopped on.
        /// </summary>
        private static SimWorld Routed(out Int2 filteredEnd, out Int2 otherEnd)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);    // into the hub from the south
            Sim.Place(world, BuildKind.Sorter, Hub, Dir.East);
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 2);     // (5,5) (6,5): where the filter shape goes
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 2);    // (4,6) (4,7): where the rest goes

            filteredEnd = new Int2(6, 5);
            otherEnd = new Int2(4, 7);
            return world;
        }

        [Test]
        public void TheFilteredShape_LeavesByTheSideItFaces()
        {
            SimWorld world = Routed(out Int2 filtered, out Int2 other);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle));
            Sim.Tick(world, 90);

            Assert.AreEqual(ShapeType.Circle, Sim.ShapeAt(world, filtered),
                "a circle is the shipped sorter's filter, so it left by the side it faces");
            Assert.AreEqual(ShapeType.None, Sim.ShapeAt(world, other), "and nothing took the other way");
            Assert.AreEqual(ShapeType.None, Sim.MachineAt(world, Hub).Carried, "with the hub emptied");
        }

        [Test]
        public void EveryOtherShape_LeavesByTheOtherOutlets()
        {
            SimWorld world = Routed(out Int2 filtered, out Int2 other);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.HalfCircle));
            Sim.Tick(world, 90);

            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, other),
                "a half-circle is not the filter, so it left by anywhere but the facing side");
            Assert.AreEqual(ShapeType.None, Sim.ShapeAt(world, filtered), "the filter outlet was left free");
        }

        [Test]
        public void BothShapes_CanShareOneLine()
        {
            // The point of the building: one input line, two destinations, the sorter does the
            // separating - which is what turns "keep money and ammo apart" from a layout puzzle into
            // one building's job.
            SimWorld world = Routed(out Int2 filtered, out Int2 other);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle));
            Sim.Tick(world, 40);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.HalfCircle));
            Sim.Tick(world, 120);

            Assert.AreEqual(ShapeType.Circle, Sim.ShapeAt(world, filtered));
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, other));
            Assert.AreEqual(2, Sim.ItemCount(world), "two in, two out, none eaten");
        }

        [Test]
        public void AnUnroutableShape_IsHeld_SoTheLineBacksUpVisibly()
        {
            // Nothing wired for the non-filter shape: the sorter must not push it out of the filter
            // outlet "because it fits". It holds, the line behind it backs up, and a mis-wiring is a
            // queue the player can see instead of a wrong shape arriving somewhere far downstream.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 2);    // (4,3) (4,4): a two-cell input line
            Sim.Place(world, BuildKind.Sorter, Hub, Dir.East);
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 1);     // the facing outlet - the only one

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 3), ShapeType.HalfCircle));
            Sim.Tick(world, 120);

            Assert.AreEqual(ShapeType.HalfCircle, Sim.MachineAt(world, Hub).Carried, "held, not misrouted");
            Assert.AreEqual(ShapeType.None, Sim.ShapeAt(world, new Int2(5, 5)),
                "and the filter outlet was left alone");

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle), "a circle queues behind it");
            Sim.Tick(world, 120);

            Assert.AreEqual(ShapeType.Circle, Sim.ShapeAt(world, new Int2(4, 4)),
                "the input line backed up behind the held item");
            Assert.AreEqual(ShapeType.HalfCircle, Sim.MachineAt(world, Hub).Carried, "which is still held");
        }

        [Test]
        public void ABlockedFilterOutlet_IsWaitedFor_NotBypassed()
        {
            // The other outlet is free, and the shape that needs the facing side still does not take
            // it: routing is strict, so a full filtered outlet is backpressure and never a misroute.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);
            Sim.Place(world, BuildKind.Sorter, Hub, Dir.East);
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 1);     // the facing outlet, one cell
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 2);    // the other outlet, free

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 5), ShapeType.Circle),
                "the facing outlet is already occupied");
            Sim.Tick(world, 30);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle));
            Sim.Tick(world, 120);

            Assert.AreEqual(ShapeType.Circle, Sim.MachineAt(world, Hub).Carried,
                "a circle waits for its own outlet rather than taking the wrong one");
            Assert.AreEqual(ShapeType.None, Sim.ShapeAt(world, new Int2(4, 7)),
                "the other outlet is not a fallback");
        }

        [Test]
        public void ItsPortsAreTheBeltsAroundIt_AndTheSnapshotSaysSo()
        {
            // The view draws its stubs and its neighbours' docking lanes from these masks, so the
            // masks have to agree with the rule the tick pushes through - the decomposer's and
            // splitter's outlets are read the same way.
            SimWorld world = Routed(out _, out _);

            Assert.IsTrue(world.Machines.TryGetSnapshot(Hub, out MachineSnapshot snapshot));

            Assert.IsTrue(snapshot.InMask.Has(Dir.South), "the input belt feeds it from the south");
            Assert.IsTrue(snapshot.OutMask.Has(Dir.East), "and it pushes out the side it faces");
            Assert.IsTrue(snapshot.OutMask.Has(Dir.North), "and out the other wired side");

            Assert.IsFalse(snapshot.InMask.Has(Dir.East), "an outlet is not also an input");
            Assert.IsFalse(snapshot.OutMask.Has(Dir.South), "and the input is not also an outlet");

            Assert.AreEqual(ShapeType.Circle, snapshot.Shape,
                "the badge it draws is its filter shape - the setting, not what it happens to hold");
        }

        [Test]
        public void APassingLine_IsNotAPort()
        {
            // A belt beside the hub pointing along, neither in nor away, is not part of the hub - so
            // the sorter neither robs it nor routes into it, exactly like a splitter. The line here
            // runs north up the hub's east side, which is the side it would have used as an outlet
            // had it pointed east.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);   // south in
            Sim.Place(world, BuildKind.Sorter, Hub, Dir.North);  // filter side: north
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 2);   // (4,6) (4,7): the filtered way
            Sim.LayRun(world, new Int2(5, 4), Dir.North, 3);   // the passing line: (5,4) (5,5) (5,6)

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle));
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 4), ShapeType.HalfCircle));
            Sim.Tick(world, 150);

            Assert.AreEqual(ShapeType.Circle, Sim.ShapeAt(world, new Int2(4, 7)),
                "the circle went out the side the sorter faces");
            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(5, 6)),
                "and the passing belt kept its own item");
            Assert.AreEqual(2, Sim.ItemCount(world));
        }
    }
}
