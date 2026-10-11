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
            Assert.AreEqual(Sim.Shipped(0).StartCircles, Sim.NewWorld().Economy.Circles,
                "the real map funds the opening build");
            Assert.AreEqual(3, PoorWorld(3).Economy.Circles);
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
    }
}
