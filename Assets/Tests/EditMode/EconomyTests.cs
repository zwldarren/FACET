using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the economy: a <em>complete</em> shape is the currency - a full circle and a
    /// full square bank the same, and anything less is destroyed - it becomes spendable only by being
    /// belted into the Core, every building is paid for, and every removal refunds in full.
    /// The small worlds here use the 20x12 test map, whose Core sits at (8,4)-(11,7).
    /// </summary>
    public class EconomyTests
    {
        /// <summary>A test map with a chosen starting stockpile - the economy's own test rig.</summary>
        private static SimWorld PoorWorld(int startShapes, int width = 20, int height = 12)
            => new SimWorld(new MapDefinition("poor", width, height, startShapes,
                new ShapePatch[0], new Int2[0], new WaveDefinition[0]), new SimConfig());

        [Test]
        public void StartingStockpile_ComesFromTheMap()
        {
            // The opening budget is a map number, not a simulation constant, so the rig's 80 and a
            // hand-made map's 3 both arrive exactly as authored.
            Assert.AreEqual(80, Sim.Rig().Economy.Shapes, "the rig's budget, to the shape");
            Assert.AreEqual(3, PoorWorld(3).Economy.Shapes);
            Assert.AreEqual(0, PoorWorld(0).Economy.Shapes, "and a map that funds nothing opens with nothing");
        }

        [Test]
        public void Placement_Spends_AndRemoval_RefundsInFull()
        {
            SimWorld world = PoorWorld(50);

            Sim.Place(world, BuildKind.Turret, new Int2(2, 2), Dir.East);
            Assert.AreEqual(50 - world.Content.Machine(BuildKind.Turret).Cost, world.Economy.Shapes,
                "the turret is paid for");

            Assert.IsTrue(world.TryRemoveBuilding(new Int2(2, 2)));
            Assert.AreEqual(50, world.Economy.Shapes,
                "and refunded in full: the cost gates expansion speed, not experimentation");
        }

        [Test]
        public void ADryStockpile_StopsPlacement_WithoutStoppingBeingLegal()
        {
            SimWorld world = PoorWorld(3);

            Sim.LayRun(world, new Int2(1, 1), Dir.East, 3);
            Assert.AreEqual(0, world.Economy.Shapes);

            Assert.IsFalse(world.TryPlaceBelt(new Int2(4, 1), Dir.East), "the fourth belt is unaffordable");
            Assert.IsFalse(world.Belts.Has(new Int2(4, 1)), "so it was never laid");

            // The ghost reads both halves of the answer: legal ground, no money.
            Assert.IsTrue(world.CanPlace(BuildKind.Belt, new Int2(4, 1), Dir.East), "the cell itself is fine");
            Assert.IsFalse(world.Economy.CanAfford(BuildKind.Belt), "the stockpile is not");
            Assert.IsFalse(world.Economy.CanAfford(BuildKind.Turret), "let alone a turret");
        }

        [Test]
        public void ADrag_StopsGrowing_WhenTheStockpileRunsDry()
        {
            SimWorld world = PoorWorld(3);

            Sim.Drag(world, new Int2(1, 1), new Int2(6, 1));

            Assert.IsTrue(world.Belts.Has(new Int2(3, 1)), "three belts were affordable");
            Assert.IsFalse(world.Belts.Has(new Int2(4, 1)), "and the run simply stops there");
            Assert.IsFalse(world.Belts.Has(new Int2(6, 1)));
            Assert.AreEqual(0, world.Economy.Shapes);
        }

        [Test]
        public void ClearingAJam_RemovesNothing_AndRefundsNothing()
        {
            SimWorld world = PoorWorld(10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 2);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(1, 1), Sim.Circle));
            Assert.IsTrue(world.Belts.TryJam(new Int2(1, 1)));
            int before = world.Economy.Shapes;

            Assert.IsTrue(world.TryRemoveBuilding(new Int2(1, 1)), "right-click clears the jam");

            Assert.AreEqual(before, world.Economy.Shapes, "nothing was removed, so nothing is refunded");
            Assert.IsTrue(world.Belts.Has(new Int2(1, 1)), "the belt stays");
            Assert.IsFalse(world.Belts.IsJammed(new Int2(1, 1)), "the jam goes");
        }

        [Test]
        public void ACircleDeliveredToTheCore_BanksAsOne()
        {
            SimWorld world = PoorWorld(10);
            // Belt at (7,5) points east into the Core's west edge; the one behind it carries in.
            Sim.LayRun(world, new Int2(6, 5), Dir.East, 2);
            int before = world.Economy.Shapes;

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(6, 5), Sim.Circle));
            Sim.Tick(world, 90);

            Assert.AreEqual(before + 1, world.Economy.Shapes, "one full circle delivered is one shape banked");
            Assert.AreEqual(1, world.Economy.TotalBanked);
            Assert.AreEqual(0, Sim.ItemCount(world), "and the item is consumed, not parked");
        }

        /// <summary>
        /// The rule the currency was re-cut to: the Core banks a complete <em>shape</em>, not a circle, so a
        /// full square pays exactly what a full circle pays. A map whose ore is square is therefore a map
        /// whose money still works, which is the whole reason the rule reads completeness rather than
        /// mineral.
        /// </summary>
        [Test]
        public void ASquareDeliveredToTheCore_BanksTheSameAsACircle()
        {
            SimWorld squares = PoorWorld(10);
            Sim.LayRun(squares, new Int2(6, 5), Dir.East, 2);
            int before = squares.Economy.Shapes;

            Assert.IsTrue(squares.Belts.TrySpawnItem(new Int2(6, 5), Sim.Square));
            Sim.Tick(squares, 90);

            Assert.AreEqual(before + 1, squares.Economy.Shapes, "a full square is one shape banked");
            Assert.AreEqual(1, squares.Economy.TotalBanked);
            Assert.AreEqual(0, Sim.ItemCount(squares), "and it is consumed like a circle");

            // The event carries the coin that actually landed, so a view flashing "the money arrived" draws
            // the shape the player is paid in rather than a circle on a square map.
            Assert.IsTrue(squares.Events.TryLast(SimEventKind.Banked, out SimEvent banked));
            Assert.AreEqual(Sim.Square, banked.Shape);
        }

        [Test]
        public void AHalfCircleDeliveredToTheCore_IsDestroyed_NotBanked()
        {
            SimWorld world = PoorWorld(10);
            Sim.LayRun(world, new Int2(6, 5), Dir.East, 2);
            int before = world.Economy.Shapes;

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(6, 5), Sim.TopHalfCircle));
            Sim.Tick(world, 90);

            Assert.AreEqual(before, world.Economy.Shapes, "an incomplete shape is not money, ammunition or not");
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
            int before = world.Economy.Shapes;

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(7, 5), Sim.Circle));
            Sim.Tick(world, 90);

            Assert.AreEqual(before, world.Economy.Shapes);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(6, 5)),
                "the circle waits at the dead end, still an item, never spendable");
        }

        // ---- Rates. A 30x12 map, Core pinned at (8,4)-(11,7), one 3x2 circle patch at (24,5).
        // The belt is one tile per second and a drill mines one shape per second, matched on purpose -
        // so the numbers below are the economy's contract, not coincidences: a lane is one item per
        // second no matter what feeds it, and its latency is its length.

        /// <summary>A world with one patch and the Core pinned at (8,4), for the rate tests.</summary>
        private static SimWorld RateWorld(int startShapes = 1000)
            => new SimWorld(new MapDefinition("rates", 30, 12, startShapes,
                new[] { new ShapePatch(new Int2(24, 5), 3, 2, Quad.Circle) },
                new Int2[0], new WaveDefinition[0], coreCell: new Int2(8, 4)), new SimConfig());

        /// <summary>
        /// One drill and a fifteen-belt lane home bank one circle per second after the lane's own
        /// latency: the first circle lands in the sixteenth second (fifteen belt tiles plus one hand
        /// into the Core), then the count is t minus fifteen. Distance is paid twice - in circles to
        /// build the lane, and in seconds before the first circle.
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
        /// not the patch. The drills queue on their landing cells and the lane carries one per second
        /// regardless - so a vein's yield is the number of lanes built from it, not the drills on it,
        /// and the extra drill is a backup rather than a bonus.
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
        /// splitter halves the stream, one branch banks at the Core, the other feeds a cutter whose pieces
        /// are ammunition. While both branches drain, the bank's share is the splitter's half; and because
        /// the ammo branch throttles itself (a cutter wired on one front only can hold so many of the piece it
        /// has nowhere to put), the splitter ends up skipping it and the money branch carries everything - the
        /// two halves of "one drill, two destinations", read off the stockpile.
        /// </summary>
        [Test]
        public void AnArmedLane_SplitsOneDrillBetweenMoneyAndAmmo()
        {
            SimWorld world = RateWorld();
            Sim.Place(world, BuildKind.Drill, new Int2(26, 6), Dir.South);
            Sim.LayRun(world, new Int2(26, 5), Dir.West, 6);                 // (26,5) .. (21,5), the trunk
            Sim.Place(world, BuildKind.Splitter, new Int2(20, 5), Dir.West);
            Sim.LayRun(world, new Int2(19, 5), Dir.West, 8);                 // money branch, (19,5) .. (12,5)
            Sim.LayRun(world, new Int2(20, 6), Dir.North, 1);                // ammo branch north, into the cutter
            Sim.Place(world, BuildKind.Cutter, new Int2(20, 7), Dir.West);
            Sim.LayRun(world, new Int2(19, 7), Dir.West, 8);                 // the bottom halves west, to the sink

            // The ammo branch is throttled by its own displaced piece: the cutter flowing west sends the bottom
            // half down this line - its own front - and has nowhere to put the top one, which leaves a lane
            // north, so once that slot holds CutterBehavior.SlotBuffer halves the machine stops cutting. The
            // claim here is that the branch runs at all, so it is observed while it runs rather than at one
            // arbitrary tick.
            bool ammoRan = false;
            for (int i = 0; i < 30 * (int)SimConfig.TickRate; i++)
            {
                world.Tick(InputCommand.None);
                ammoRan |= Sim.ShapeAt(world, new Int2(19, 7)) == Sim.BottomHalfCircle;
            }

            Assert.IsTrue(ammoRan,
                "the ammo branch runs: the cutter pushes the piece on its own front down the line");
            Assert.AreEqual(0, world.Belts.JamCount, "and nothing about it is a wrong-shape delivery");

            // Half the drill, with the lane's own latency paid first: the first circle lands in the
            // sixteenth second, so half of what has crossed the splitter by now is a handful.
            int halfShare = world.Economy.TotalBanked;
            Assert.Greater(halfShare, 3, "the bank took its share of a drill that has been running a while");
            Assert.Less(halfShare, 14, "and its share is about half - one circle every two seconds");

            Sim.Tick(world, 30 * (int)SimConfig.TickRate);

            // Once the ammo branch stops draining, the splitter skips the outlet that cannot take anything
            // and the money branch carries the whole drill - so the bank's second half is worth far more
            // than its first, which is what makes an un-drained branch a real decision rather than a label.
            Assert.Greater(world.Economy.TotalBanked, halfShare * 2,
                "with the ammo branch backed up, the money branch carries the whole drill");
            Assert.Less(world.Economy.TotalBanked, 50, "and never more than the drill produces");
        }
    }
}
