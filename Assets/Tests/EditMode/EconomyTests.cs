using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the economy: circles are the only currency, they become spendable only by
    /// being belted into the Core, every building is paid for, and every removal refunds in full.
    /// The small worlds here use the 20x12 test map, whose Core sits at (8,4)-(11,7).
    /// </summary>
    public class EconomyTests
    {
        /// <summary>A test map with a chosen starting stockpile - the economy's own test rig.</summary>
        private static SimWorld PoorWorld(int startCircles, int width = 20, int height = 12)
            => new SimWorld(new MapDefinition("poor", width, height, startCircles,
                new ShapePatch[0], new Int2[0], new WaveDefinition[0]), new SimConfig());

        [Test]
        public void StartingStockpile_ComesFromTheMap()
        {
            // The opening budget is a number the map carries rather than a constant in the simulation,
            // so the rig's own 62 and a hand-made map's 3 both arrive exactly as authored.
            Assert.AreEqual(62, Sim.Rig().Economy.Circles, "the rig's budget, to the circle");
            Assert.AreEqual(3, PoorWorld(3).Economy.Circles);
            Assert.AreEqual(0, PoorWorld(0).Economy.Circles, "and a map that funds nothing opens with nothing");
        }

        [Test]
        public void Placement_Spends_AndRemoval_RefundsInFull()
        {
            SimWorld world = PoorWorld(50);

            Sim.Place(world, BuildKind.Cannon, new Int2(2, 2), Dir.East);
            Assert.AreEqual(50 - world.Content.Machine(BuildKind.Cannon).Cost, world.Economy.Circles,
                "the cannon is paid for");

            Assert.IsTrue(world.TryRemoveBuilding(new Int2(2, 2)));
            Assert.AreEqual(50, world.Economy.Circles,
                "and refunded in full: the cost gates expansion speed, not experimentation");
        }

        [Test]
        public void ADryStockpile_StopsPlacement_WithoutStoppingBeingLegal()
        {
            SimWorld world = PoorWorld(3);

            Sim.LayRun(world, new Int2(1, 1), Dir.East, 3);
            Assert.AreEqual(0, world.Economy.Circles);

            Assert.IsFalse(world.TryPlaceBelt(new Int2(4, 1), Dir.East), "the fourth belt is unaffordable");
            Assert.IsFalse(world.Belts.Has(new Int2(4, 1)), "so it was never laid");

            // The ghost reads both halves of the answer: legal ground, no money.
            Assert.IsTrue(world.CanPlace(BuildKind.Belt, new Int2(4, 1)), "the cell itself is fine");
            Assert.IsFalse(world.Economy.CanAfford(BuildKind.Belt), "the stockpile is not");
            Assert.IsFalse(world.Economy.CanAfford(BuildKind.Cannon), "let alone a cannon");
        }

        [Test]
        public void ADrag_StopsGrowing_WhenTheStockpileRunsDry()
        {
            SimWorld world = PoorWorld(3);

            Sim.Drag(world, new Int2(1, 1), new Int2(6, 1));

            Assert.IsTrue(world.Belts.Has(new Int2(3, 1)), "three belts were affordable");
            Assert.IsFalse(world.Belts.Has(new Int2(4, 1)), "and the run simply stops there");
            Assert.IsFalse(world.Belts.Has(new Int2(6, 1)));
            Assert.AreEqual(0, world.Economy.Circles);
        }

        [Test]
        public void ClearingAJam_RemovesNothing_AndRefundsNothing()
        {
            SimWorld world = PoorWorld(10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 2);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(1, 1), ShapeType.Circle));
            Assert.IsTrue(world.Belts.TryJam(new Int2(1, 1)));
            int before = world.Economy.Circles;

            Assert.IsTrue(world.TryRemoveBuilding(new Int2(1, 1)), "right-click clears the jam");

            Assert.AreEqual(before, world.Economy.Circles, "nothing was removed, so nothing is refunded");
            Assert.IsTrue(world.Belts.Has(new Int2(1, 1)), "the belt stays");
            Assert.IsFalse(world.Belts.IsJammed(new Int2(1, 1)), "the jam goes");
        }

        [Test]
        public void ACircleDeliveredToTheCore_BanksAsOne()
        {
            SimWorld world = PoorWorld(10);
            // Belt at (7,5) points east into the Core's west edge; the one behind it carries in.
            Sim.LayRun(world, new Int2(6, 5), Dir.East, 2);
            int before = world.Economy.Circles;

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(6, 5), ShapeType.Circle));
            Sim.Tick(world, 90);

            Assert.AreEqual(before + 1, world.Economy.Circles, "one circle delivered is one circle banked");
            Assert.AreEqual(1, world.Economy.TotalBanked);
            Assert.AreEqual(0, Sim.ItemCount(world), "and the item is consumed, not parked");
        }

        [Test]
        public void AHalfCircleDeliveredToTheCore_IsDestroyed_NotBanked()
        {
            SimWorld world = PoorWorld(10);
            Sim.LayRun(world, new Int2(6, 5), Dir.East, 2);
            int before = world.Economy.Circles;

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(6, 5), ShapeType.HalfCircle));
            Sim.Tick(world, 90);

            Assert.AreEqual(before, world.Economy.Circles, "ammunition is not money");
            Assert.AreEqual(0, world.Economy.TotalBanked);
            Assert.AreEqual(0, Sim.ItemCount(world), "the sink is the overflow drain as well as the mint");
        }

        [Test]
        public void ABeltNotPointingIn_BanksNothing()
        {
            SimWorld world = PoorWorld(10);
            // Same cell as the banking test, facing away: the delivery rule is the belt's direction.
            Sim.LayRun(world, new Int2(6, 5), Dir.West, 1);
            Sim.LayRun(world, new Int2(7, 5), Dir.West, 1);
            int before = world.Economy.Circles;

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(7, 5), ShapeType.Circle));
            Sim.Tick(world, 90);

            Assert.AreEqual(before, world.Economy.Circles);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(6, 5)),
                "the circle waits at the dead end, still an item, never spendable");
        }

        // ---- Rates. A 30x12 map, Core pinned at (8,4)-(11,7), one 3x2 circle patch at (24,5).
        // The belt is one tile per second, a drill mines one shape per second, and the two are
        // matched on purpose - so the numbers below are the economy's contract, not coincidences:
        // a lane is one item per second no matter what feeds it, and its latency is its length.

        /// <summary>A world with one patch and the Core pinned at (8,4), for the rate tests.</summary>
        private static SimWorld RateWorld(int startCircles = 1000)
            => new SimWorld(new MapDefinition("rates", 30, 12, startCircles,
                new[] { new ShapePatch(new Int2(24, 5), 3, 2, ShapeType.Circle) },
                new Int2[0], new WaveDefinition[0], coreCell: new Int2(8, 4)), new SimConfig());

        /// <summary>
        /// One drill and a fifteen-belt lane home bank one circle per second, starting after the
        /// lane's own latency: the first circle lands during the sixteenth second (fifteen tiles of
        /// belt, one for the hand into the Core), and the count is then exactly t minus fifteen.
        /// This is the whole wealth rule of the game in one line - distance is paid three times,
        /// in circles to build the lane, in seconds before the first circle, and in nothing after.
        /// </summary>
        [Test]
        public void AMoneyLine_BanksOneCirclePerSecond_AfterItsOwnLatency()
        {
            SimWorld world = RateWorld();
            Sim.Place(world, BuildKind.Drill, new Int2(26, 6), Dir.South);   // pushes south onto the lane
            Sim.LayRun(world, new Int2(26, 5), Dir.West, 15);                // (26,5) .. (12,5), into the Core

            Sim.Tick(world, 16 * (int)SimConfig.TickRate);
            Assert.AreEqual(1, world.Economy.TotalBanked, "the first circle lands after exactly the lane's length");

            Sim.Tick(world, 29 * (int)SimConfig.TickRate);
            Assert.AreEqual(30, world.Economy.TotalBanked, "and the rate is one per second, t minus the latency");
        }

        /// <summary>
        /// Three drills cannot push three circles a second down one lane: the lane is the bottleneck,
        /// not the patch. The drills queue on their landing cells and the lane carries its one per
        /// second regardless - which is why a vein's yield is the number of lanes built from it, not
        /// the number of drills crammed onto it, and why the extra drill is a backup rather than a
        /// bonus.
        /// </summary>
        [Test]
        public void ThreeDrillsOnOneLane_StillBankOnlyOneCirclePerSecond()
        {
            SimWorld world = RateWorld();
            Sim.LayRun(world, new Int2(26, 5), Dir.West, 15);
            Sim.Place(world, BuildKind.Drill, new Int2(26, 6), Dir.South);
            Sim.Place(world, BuildKind.Drill, new Int2(25, 6), Dir.South);
            Sim.Place(world, BuildKind.Drill, new Int2(24, 6), Dir.South);

            Sim.Tick(world, 30 * (int)SimConfig.TickRate);
            int banked = world.Economy.TotalBanked;

            Sim.Tick(world, 30 * (int)SimConfig.TickRate);
            Assert.GreaterOrEqual(world.Economy.TotalBanked - banked, 29);
            Assert.LessOrEqual(world.Economy.TotalBanked - banked, 30,
                "three miners, one lane, one circle per second - not three");
            Assert.Less(world.Economy.TotalBanked, 60,
                "and the lane's cap is absolute: three drills do not even double it");
        }

        /// <summary>
        /// The armed lane - the build that makes one drill pay for a gun and an income at once: a
        /// splitter halves the stream, one branch banks at the Core, the other feeds a decomposer
        /// whose half-circles are the cannon's ammunition. The bank's share is the splitter's half,
        /// about one circle every two seconds; the ammunition branch demonstrably runs (a half-circle
        /// sits on the converter's outlet belt) while everything it makes is destroyed at the sink,
        /// because ammunition is not money.
        /// </summary>
        [Test]
        public void AnArmedLane_SplitsOneDrillBetweenMoneyAndAmmo()
        {
            SimWorld world = RateWorld();
            Sim.Place(world, BuildKind.Drill, new Int2(26, 6), Dir.South);
            Sim.LayRun(world, new Int2(26, 5), Dir.West, 6);                 // (26,5) .. (21,5), the trunk
            Sim.Place(world, BuildKind.Splitter, new Int2(20, 5), Dir.West);
            Sim.LayRun(world, new Int2(19, 5), Dir.West, 8);                 // money branch, (19,5) .. (12,5)
            Sim.LayRun(world, new Int2(20, 6), Dir.North, 1);                // ammo branch north: the splitter's outlet into the decomposer
            Sim.Place(world, BuildKind.Decomposer, new Int2(20, 7), Dir.West);
            Sim.LayRun(world, new Int2(19, 7), Dir.West, 8);                 // halves west, into the sink

            Sim.Tick(world, 30 * (int)SimConfig.TickRate);
            Assert.IsTrue(world.Belts.TryGet(new Int2(19, 7), out BeltState outlet) && outlet.Item == ShapeType.HalfCircle,
                "the ammo branch runs: the converter pushes its halves down the line");

            Sim.Tick(world, 30 * (int)SimConfig.TickRate);
            Assert.Greater(world.Economy.TotalBanked, 18);
            Assert.Less(world.Economy.TotalBanked, 24,
                "the bank's share is the splitter's half - about one circle every two seconds");
        }
    }
}
