using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the lines a belt carries: a run downstream of a merge runs as many times the base
    /// speed as there are lines feeding it, so the merge keeps up with both of its feeds instead of backing
    /// them up. Worlds use the 20x12 test map, whose Core stands at (8,4)-(11,7); every layout keeps clear of
    /// it so the sink cannot eat the items under test.
    /// </summary>
    public class BeltFlowTests
    {
        /// <summary>Two feeds into a splitter at (4,9) - one from the south, one from the west - and one outlet
        /// running east along row 9 to the map's edge.</summary>
        private static SimWorld TwoIntoOne()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 8), Dir.North, 1);   // south in
            Sim.LayRun(world, new Int2(3, 9), Dir.East, 1);    // west in
            Sim.Place(world, BuildKind.Splitter, new Int2(4, 9), Dir.North);
            Sim.LayRun(world, new Int2(5, 9), Dir.East, 15);   // the outlet, (5,9)..(19,9)
            return world;
        }

        /// <summary>How many items of <paramref name="shape"/> are on the outlet row east of the hub.</summary>
        private static int OnOutlet(SimWorld world, Shape shape)
        {
            int count = 0;
            foreach (ItemSnapshot item in Sim.ItemsIn(world))
                if (item.Shape == shape && item.Position.X > 5f && item.Position.Y > 9f && item.Position.Y < 10f)
                    count++;
            return count;
        }

        [Test]
        public void TwoIntoOne_TheOutletCarriesTwoLines()
        {
            SimWorld world = TwoIntoOne();
            Sim.Tick(world, 1);

            Assert.AreEqual(1, world.Belts.LinesAt(new Int2(4, 8)), "each feed is an ordinary run");
            Assert.AreEqual(1, world.Belts.LinesAt(new Int2(3, 9)));
            Assert.AreEqual(2, world.Belts.LinesAt(new Int2(5, 9)), "the outlet carries both");
            Assert.AreEqual(2, world.Belts.LinesAt(new Int2(19, 9)), "and so does the whole run after it");
        }

        [Test]
        public void TwoIntoOne_KeepsUpWithBothFeeds()
        {
            // Both feeds kept full for six seconds: a circle line and a square line, one a second each. At one
            // speed the outlet could take only one a second between them and both feeds backed up; at two it
            // takes everything they bring, and neither starves the other.
            SimWorld world = TwoIntoOne();
            for (int t = 0; t < 180; t++)
            {
                world.Belts.TrySpawnItem(new Int2(4, 8), Sim.Circle);
                world.Belts.TrySpawnItem(new Int2(3, 9), Sim.Square);
                Sim.Tick(world, 1);
            }

            int circles = OnOutlet(world, Sim.Circle);
            int squares = OnOutlet(world, Sim.Square);
            Assert.GreaterOrEqual(circles + squares, 8,
                "about two a second for the five seconds after the first item arrives - one a second would be 5");
            Assert.GreaterOrEqual(circles, 4, "the south feed gets its share");
            Assert.GreaterOrEqual(squares, 4, "and so does the west");
        }

        [Test]
        public void OneIntoTwo_TheOutletsStayOrdinary()
        {
            // Splitting is the other direction: one line shared across two outlets is half a line each, and a
            // belt never runs slower than the base speed.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 8), Dir.North, 1);   // south in
            Sim.Place(world, BuildKind.Splitter, new Int2(4, 9), Dir.North);
            Sim.LayRun(world, new Int2(5, 9), Dir.East, 2);    // east out
            Sim.LayRun(world, new Int2(3, 9), Dir.West, 2);    // west out
            Sim.Tick(world, 1);

            Assert.AreEqual(1, world.Belts.LinesAt(new Int2(5, 9)));
            Assert.AreEqual(1, world.Belts.LinesAt(new Int2(3, 9)));
        }

        [Test]
        public void TwoBeltsJoining_CarryTwoLines()
        {
            // A belt running into the side of another is a merge too, with the same bottleneck - so it gets the
            // same answer. Upstream of the join the main run is still one line.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(1, 9), Dir.East, 6);    // the main run, (1,9)..(6,9)
            Sim.LayRun(world, new Int2(3, 11), Dir.South, 2);  // the side run, (3,11) (3,10), into (3,9)
            Sim.Tick(world, 1);

            Assert.AreEqual(1, world.Belts.LinesAt(new Int2(2, 9)), "before the join");
            Assert.AreEqual(1, world.Belts.LinesAt(new Int2(3, 10)), "the side run itself");
            Assert.AreEqual(2, world.Belts.LinesAt(new Int2(3, 9)), "the join");
            Assert.AreEqual(2, world.Belts.LinesAt(new Int2(6, 9)), "and after it");
        }

        [Test]
        public void AMergedRun_StaysFastAcrossAPipeAndRoundATurn()
        {
            // A pipe hands on what came into it, and a turn is still the same run: neither should be where a
            // merged line drops back to one.
            SimWorld world = TwoIntoOneShort(out Int2 outletEnd);
            Sim.Place(world, BuildKind.Pipe, outletEnd + Dir.East.Offset(), Dir.East);   // (7,9), lands on (9,9)
            Sim.LayRun(world, new Int2(9, 9), Dir.East, 1);
            Sim.LayRun(world, new Int2(10, 9), Dir.North, 3);   // turns north: (10,9)..(10,11)
            Sim.Tick(world, 1);

            Assert.AreEqual(2, world.Belts.LinesAt(new Int2(9, 9)), "where the pipe lands");
            Assert.AreEqual(2, world.Belts.LinesAt(new Int2(10, 9)), "round the turn");
            Assert.AreEqual(2, world.Belts.LinesAt(new Int2(10, 11)), "to the end of the run");
        }

        [Test]
        public void RemovingAFeed_SlowsTheOutletBackDown()
        {
            SimWorld world = TwoIntoOne();
            Sim.Tick(world, 1);
            Assert.AreEqual(2, world.Belts.LinesAt(new Int2(5, 9)));

            Assert.IsTrue(world.Belts.TryRemove(new Int2(3, 9)));
            Sim.Tick(world, 1);
            Assert.AreEqual(1, world.Belts.LinesAt(new Int2(5, 9)), "one feed left, one line");
        }

        [Test]
        public void ChangingPace_IsARedraw()
        {
            // The chevrons slide at the new pace, so the bed has to be drawn again - the same contract every
            // other belt mutation keeps with the renderer.
            SimWorld world = TwoIntoOne();
            Sim.Tick(world, 1);

            Assert.IsTrue(world.Belts.TryRemove(new Int2(3, 9)));
            int revision = world.Belts.Revision;
            Sim.Tick(world, 1);
            Assert.Greater(world.Belts.Revision, revision, "the outlet's pace changed");
        }

        [Test]
        public void Merges_StackUpToFourLines()
        {
            // Three single belts into one cell carry three; join a two-line run onto that and the sum is five,
            // which is past the cap of four - a chain of merges cannot send a run faster than that.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 9), Dir.East, 1);    // from the west, into (5,9)
            Sim.LayRun(world, new Int2(5, 10), Dir.South, 1);  // from the north
            Sim.LayRun(world, new Int2(5, 8), Dir.North, 1);   // from the south
            Sim.LayRun(world, new Int2(5, 9), Dir.East, 3);    // (5,9)..(7,9)
            Sim.LayRun(world, new Int2(6, 10), Dir.East, 1);   // two lines into (7,10)...
            Sim.LayRun(world, new Int2(7, 11), Dir.South, 2);  // ...(7,11) and (7,10), which runs into (7,9)
            Sim.Tick(world, 1);

            Assert.AreEqual(3, world.Belts.LinesAt(new Int2(6, 9)), "three single feeds");
            Assert.AreEqual(2, world.Belts.LinesAt(new Int2(7, 10)), "the second merge");
            Assert.AreEqual(4, world.Belts.LinesAt(new Int2(7, 9)), "three and two is five, capped at four");
        }

        [Test]
        public void ALoop_FeedsItselfNothing()
        {
            // A ring of belts is its own feeder all the way round: the walk upstream has to stop where it came
            // in, and the ring is one line.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 1);
            Sim.LayRun(world, new Int2(2, 1), Dir.North, 1);
            Sim.LayRun(world, new Int2(2, 2), Dir.West, 1);
            Sim.LayRun(world, new Int2(1, 2), Dir.South, 1);
            Sim.Tick(world, 1);

            Assert.AreEqual(1, world.Belts.LinesAt(new Int2(1, 1)));
            Assert.AreEqual(1, world.Belts.LinesAt(new Int2(2, 2)));
        }

        /// <summary><see cref="TwoIntoOne"/> with a two-cell outlet, (5,9) and (6,9), for a test that carries
        /// the run on past it.</summary>
        private static SimWorld TwoIntoOneShort(out Int2 outletEnd)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(4, 8), Dir.North, 1);
            Sim.LayRun(world, new Int2(3, 9), Dir.East, 1);
            Sim.Place(world, BuildKind.Splitter, new Int2(4, 9), Dir.North);
            Sim.LayRun(world, new Int2(5, 9), Dir.East, 2);
            outletEnd = new Int2(6, 9);
            return world;
        }
    }
}
