using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the sorter, the router: a splitter's hub - its ports are just the belts around
    /// it - with one bit of data a splitter lacks, the rule that decides what leaves by the side it faces.
    ///
    /// The rework turned that rule from "one shape" into a <see cref="ShapeFilter"/> with two modes, which
    /// is the difference between the two jobs a player wants from one building: <b>contains a mineral</b>
    /// ("anything with square in it" - one rule for fifty mixed items) and <b>exactly this item</b>
    /// (orientation included). The filter is the <em>machine's own</em>, seeded from its row and editable
    /// while it stands, so re-routing a line is not a rebuild.
    ///
    /// Worlds use the 20x12 test map with the hub at (4,5), lines kept clear of the Core so the sink cannot
    /// eat the items under test. The shipped filter is "anything with a circle in it", so the shipped tests
    /// read as "circle-bearing items go where it faces, everything else carries on" - a mineral predicate and
    /// not the currency rule, which is completeness and out of this filter's reach.
    /// </summary>
    public class SorterTests
    {
        private static readonly Int2 Hub = new Int2(4, 5);

        /// <summary>
        /// South input, east outlet (the side the filter faces), north outlet (everything else's). Both
        /// outlets dead-end so an arriving item stops and can be read off its cell.
        /// </summary>
        private static SimWorld Routed(out Int2 filteredEnd, out Int2 otherEnd)
            => Routed(ContentDatabase.Default, out filteredEnd, out otherEnd);

        /// <summary>The same layout over any content table, for the tests that retune the filter.</summary>
        private static SimWorld Routed(ContentDatabase content, out Int2 filteredEnd, out Int2 otherEnd)
        {
            SimWorld world = content == ContentDatabase.Default
                ? Sim.NewWorld(20, 12)
                : new SimWorld(Sim.TestMap(20, 12), new SimConfig(), content);

            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);    // into the hub from the south
            Sim.Place(world, BuildKind.Sorter, Hub, Dir.East);
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 2);     // (5,5) (6,5): where a match goes
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 2);    // (4,6) (4,7): where the rest goes

            filteredEnd = new Int2(6, 5);
            otherEnd = new Int2(4, 7);
            return world;
        }

        /// <summary>The shipped table with the sorter's own filter swapped - a row, not a class.</summary>
        private static ContentDatabase Filtering(ShapeFilter filter)
            => CustomContentTests.Rewrite(machine: CustomContentTests.SorterRouting(filter));

        [Test]
        public void TheFilteredShape_LeavesByTheSideItFaces()
        {
            SimWorld world = Routed(out Int2 filtered, out Int2 other);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Circle));
            Sim.Tick(world, 90);

            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, filtered),
                "a circle matches the shipped 'anything with a circle in it', so it left by the side it faces");
            Assert.AreEqual(Shape.None, Sim.ShapeAt(world, other), "and nothing took the other way");
            Assert.AreEqual(Shape.None, Sim.MachineAt(world, Hub).Carried, "with the hub emptied");
        }

        [Test]
        public void AShapeThatMerelyContainsTheMineral_TakesTheFilteredWayToo()
        {
            // The difference between a mineral predicate and a list of exact shapes, as one item: a
            // circle-top/square-bottom mixed round is not a circle, but it *bears* one - so the predicate
            // catches it without the player having to name all fifty mixed items by hand.
            SimWorld world = Routed(out Int2 filtered, out Int2 other);

            Assert.IsTrue(Sim.Composite.Contains(Quad.Circle), "the test's premise: it carries a circle");
            Assert.AreNotEqual(Sim.Circle, Sim.Composite, "and it is not the circle itself");

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Composite));
            Sim.Tick(world, 90);

            Assert.AreEqual(Sim.Composite, Sim.ShapeAt(world, filtered),
                "a mineral rule matches an item that contains the mineral, whatever else is in it");
            Assert.AreEqual(Shape.None, Sim.ShapeAt(world, other), "so it did not take the other outlet");
        }

        [Test]
        public void EveryOtherShape_LeavesByTheOtherOutlets()
        {
            SimWorld world = Routed(out Int2 filtered, out Int2 other);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Square));
            Sim.Tick(world, 90);

            Assert.AreEqual(Sim.Square, Sim.ShapeAt(world, other),
                "a square bears no circle, so it left by anywhere but the facing side");
            Assert.AreEqual(Shape.None, Sim.ShapeAt(world, filtered), "the filter outlet was left free");
        }

        [Test]
        public void AnExactFilter_SendsThatOneItemAndNothingElse()
        {
            // The other mode, and the one orientation needs: "exactly the top half-circle" must not catch
            // the bottom half - the two are different items now, and a rule that confused them would make
            // the rotator pointless.
            SimWorld world = Routed(Filtering(ShapeFilter.Is(Sim.TopHalfCircle)),
                out Int2 filtered, out Int2 other);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.TopHalfCircle));
            Sim.Tick(world, 90);
            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(world, filtered), "the named item went to the face");
            Assert.AreEqual(Shape.None, Sim.ShapeAt(world, other));

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.BottomHalfCircle));
            Sim.Tick(world, 90);

            Assert.AreEqual(Sim.BottomHalfCircle, Sim.ShapeAt(world, other),
                "its mirror image is a different item and did not match - which is the whole point");
            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(world, filtered),
                "and the outlet the first item took is untouched");
        }

        [Test]
        public void BothShapes_CanShareOneLine()
        {
            // The point of the building: one input line, two destinations, the sorter separating them -
            // what turns "keep two minerals apart" from a layout puzzle into one building's job.
            SimWorld world = Routed(out Int2 filtered, out Int2 other);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Circle));
            Sim.Tick(world, 40);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Square));
            Sim.Tick(world, 120);

            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, filtered));
            Assert.AreEqual(Sim.Square, Sim.ShapeAt(world, other));
            Assert.AreEqual(2, Sim.ItemCount(world), "two in, two out, none eaten");
        }

        [Test]
        public void AnUnroutableShape_IsHeld_SoTheLineBacksUpVisibly()
        {
            // Nothing is wired for the non-matching shape: the sorter must not push it out of the filter
            // outlet "because it fits". It holds, the line behind it backs up, and a mis-wiring is a
            // visible queue rather than a wrong shape arriving far downstream.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 2);    // (4,3) (4,4): a two-cell input line
            Sim.Place(world, BuildKind.Sorter, Hub, Dir.East);
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 1);     // the facing outlet - the only one

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 3), Sim.Square));
            Sim.Tick(world, 120);

            Assert.AreEqual(Sim.Square, Sim.MachineAt(world, Hub).Carried, "held, not misrouted");
            Assert.AreEqual(Shape.None, Sim.ShapeAt(world, new Int2(5, 5)),
                "and the filter outlet was left alone");

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Circle), "a circle queues behind it");
            Sim.Tick(world, 120);

            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, new Int2(4, 4)),
                "the input line backed up behind the held item");
            Assert.AreEqual(Sim.Square, Sim.MachineAt(world, Hub).Carried, "which is still held");
        }

        [Test]
        public void ABlockedFilterOutlet_IsWaitedFor_NotBypassed()
        {
            // The other outlet is free, and the shape that needs the facing side still does not take
            // it: routing is strict, so a full filtered outlet is backpressure, never a misroute.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);
            Sim.Place(world, BuildKind.Sorter, Hub, Dir.East);
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 1);     // the facing outlet, one cell
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 2);    // the other outlet, free

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 5), Sim.Circle),
                "the facing outlet is already occupied");
            Sim.Tick(world, 30);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Circle));
            Sim.Tick(world, 120);

            Assert.AreEqual(Sim.Circle, Sim.MachineAt(world, Hub).Carried,
                "a circle waits for its own outlet rather than taking the wrong one");
            Assert.AreEqual(Shape.None, Sim.ShapeAt(world, new Int2(4, 7)),
                "the other outlet is not a fallback");
        }

        [Test]
        public void ReRoutingAStandingSorter_ChangesTheRouting_WithNoRebuild()
        {
            // The picker's verb, and the reason the filter lives on the machine rather than in its row: a
            // player who built a line the wrong way round turns the sorter instead of demolishing it. Same
            // machine, same belts, same tiles - one setting changed, and the traffic follows it.
            SimWorld world = Routed(out Int2 filtered, out Int2 other);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Circle));
            Sim.Tick(world, 120);
            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, filtered), "the shipped rule sends circles east");

            // Empty both dead ends, so what the assertions below read is the traffic after the change and
            // not what the old rule left parked there.
            world.Belts.TryTakeItem(new Int2(5, 5), out _);
            world.Belts.TryTakeItem(filtered, out _);
            world.Belts.TryTakeItem(new Int2(4, 6), out _);
            world.Belts.TryTakeItem(other, out _);

            int machines = world.Machines.Occupied.Count;

            // From "anything with a circle" to "exactly a square": a square now takes the facing side and a
            // circle takes the other way.
            Assert.IsTrue(world.SetFilter(Hub, ShapeFilter.Is(Sim.Square)),
                "the picker re-routes the machine that is standing there");
            Assert.AreEqual(machines, world.Machines.Occupied.Count, "and rebuilds nothing");
            Assert.AreEqual(ShapeFilter.Is(Sim.Square), Sim.MachineAt(world, Hub).Filter,
                "the machine's own setting is what changed");

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Square));
            Sim.Tick(world, 120);
            Assert.AreEqual(Sim.Square, Sim.ShapeAt(world, filtered),
                "the newly named item leaves by the side the sorter faces");

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Circle));
            Sim.Tick(world, 120);
            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, other),
                "and the shape that used to go that way now takes the other outlet");

            Assert.IsTrue(world.Machines.TryGetSnapshot(Hub, out MachineSnapshot snapshot));
            Assert.AreEqual(ShapeFilter.Is(Sim.Square), snapshot.Filter,
                "and the view reads the machine as it stands, not as its row was authored");
        }

        [Test]
        public void ThePicker_RefusesAFilterThatWouldRouteNothing()
        {
            // ShapeFilter.None is a legitimate rule ("matches nothing") but not a legitimate setting: the
            // machine would take items it can never send anywhere, which stalls a line with no explanation.
            SimWorld world = Routed(out _, out _);
            ShapeFilter shipped = Sim.MachineAt(world, Hub).Filter;

            Assert.IsFalse(world.SetFilter(Hub, ShapeFilter.None), "a machine that routes nothing is refused");
            Assert.AreEqual(shipped, Sim.MachineAt(world, Hub).Filter, "and the old rule is left standing");

            Assert.IsFalse(world.SetFilter(new Int2(9, 9), ShapeFilter.Is(Sim.Circle)),
                "a cell with no sorter on it has no filter to set");
        }

        [Test]
        public void ItsPortsAreTheBeltsAroundIt_AndTheSnapshotSaysSo()
        {
            // The view draws its stubs and its neighbours' docking lanes from these masks, so the masks
            // have to agree with the rule the tick pushes through.
            SimWorld world = Routed(out _, out _);

            Assert.IsTrue(world.Machines.TryGetSnapshot(Hub, out MachineSnapshot snapshot));

            Assert.IsTrue(snapshot.InMask.Has(Dir.South), "the input belt feeds it from the south");
            Assert.IsTrue(snapshot.OutMask.Has(Dir.East), "and it pushes out the side it faces");
            Assert.IsTrue(snapshot.OutMask.Has(Dir.North), "and out the other wired side");

            Assert.IsFalse(snapshot.InMask.Has(Dir.East), "an outlet is not also an input");
            Assert.IsFalse(snapshot.OutMask.Has(Dir.South), "and the input is not also an outlet");

            // A badge is what the machine draws on itself, and for a sorter that is its configuration - so the
            // badge is resolved through the filter's own mode, in Core, once: a mineral rule has no exact item
            // and wears the whole mineral it asks for, an exact rule wears the item it names. The view draws
            // what it is handed and resolves nothing, which is what keeps the two from disagreeing.
            Assert.AreEqual(ShapeFilter.Has(Quad.Circle), snapshot.Filter,
                "the snapshot carries the machine's own filter, not a copy of its row");
            Assert.AreEqual(Shape.Full(Quad.Circle), snapshot.Shape,
                "a 'contains a mineral' rule is badged with the whole mineral it routes for");

            // And re-routing it to one exact item swaps the badge for that item.
            Assert.IsTrue(world.SetFilter(Hub, ShapeFilter.Is(Sim.TopHalfCircle)));
            Assert.IsTrue(world.Machines.TryGetSnapshot(Hub, out MachineSnapshot rerouted));
            Assert.AreEqual(Sim.TopHalfCircle, rerouted.Shape,
                "while an exact rule is badged with the item itself, orientation included");
        }

        [Test]
        public void APassingLine_IsNotAPort()
        {
            // A belt beside the hub pointing along, neither in nor away, is not part of the hub - so
            // the sorter neither robs it nor routes into it, exactly like a splitter. The passing line
            // runs north up the hub's east side.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);   // south in
            Sim.Place(world, BuildKind.Sorter, Hub, Dir.North);  // filter side: north
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 2);   // (4,6) (4,7): the filtered way
            Sim.LayRun(world, new Int2(5, 4), Dir.North, 3);   // the passing line: (5,4) (5,5) (5,6)

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Circle));
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 4), Sim.Square));
            Sim.Tick(world, 150);

            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, new Int2(4, 7)),
                "the circle went out the side the sorter faces");
            Assert.AreEqual(Sim.Square, Sim.ShapeAt(world, new Int2(5, 6)),
                "and the passing belt kept its own item");
            Assert.AreEqual(2, Sim.ItemCount(world));
        }
    }
}
