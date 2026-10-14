using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the second production chain: a square patch, a cutter, half-square
    /// ammunition and a mortar. Most pin that both chains obey the same rules: each converter
    /// refuses the other's mineral, each gun the other's ammunition, and the sorter separates the
    /// two minerals on one line. Worlds use the 20x12 test map, unkillable so a Core breach cannot
    /// end a measurement mid-run.
    /// </summary>
    public class ChainTests
    {
        private static readonly Int2 Cutter = new Int2(7, 9);
        private static readonly Int2 Mortar = new Int2(10, 9);

        /// <summary>
        /// Lays the second chain along the bottom of the test map: a drill on a square patch at (3,9)
        /// mining east, four belt cells into the cutter, then three more into the mortar (rest facing
        /// west; its barrel points at the nearest enemy).
        /// </summary>
        private static SimWorld SecondChain()
        {
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            world.Terrain.SetOre(new Int2(3, 9), ShapeType.Square);

            Sim.Place(world, BuildKind.Drill, new Int2(3, 9), Dir.East);
            Sim.LayRun(world, new Int2(4, 9), Dir.East, 3);       // (4,9) .. (6,9) into the cutter
            Sim.Place(world, BuildKind.Cutter, Cutter, Dir.East);
            Sim.LayRun(world, new Int2(8, 9), Dir.East, 2);       // (8,9) (9,9) into the mortar
            Sim.Place(world, BuildKind.Mortar, Mortar, Dir.West);
            return world;
        }

        [Test]
        public void TheSecondChain_RunsFromAPatch_ToAKill()
        {
            // Mine a square, split it, belt the halves, fire: the whole chain, headless. The mortar was
            // once the cheap way to a Spike (one 6-damage shell against 6 HP); half-square ammo is now
            // resisted by a quarter, so the same shell lands 1.5 and the kill costs four shells.
            SimWorld world = SecondChain();
            Assert.Greater(world.Enemies.Spawn(EnemyKind.Spike, new Vec2(14.5f, 9.5f)), 0);

            for (int i = 0; i < 30 * 20 && world.Enemies.AliveCount > 0; i++) world.Tick(InputCommand.None);

            Assert.AreEqual(0, world.Enemies.AliveCount, "the mortar brought the spike down");
            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.EnemyKilled));
            Assert.AreEqual(4, world.Events.CountOf(SimEventKind.ShotFired),
                "with four resisted shells, not the one it used to take");
        }

        [Test]
        public void TheMortar_IsFedHalfSquares_NotCircles()
        {
            // The diet is the definition's: what arrives is the second recipe's output, not a circle
            // that wandered down the wrong line.
            SimWorld world = SecondChain();

            for (int i = 0; i < 30 * 20; i++)
            {
                world.Tick(InputCommand.None);
                if (world.Machines.TryGet(Mortar, out MachineState mortar) && mortar.Armed) break;
            }

            MachineState armed = Sim.MachineAt(world, Mortar);
            Assert.AreEqual(ShapeType.HalfSquare, world.Content.Turret(BuildKind.Mortar).Ammo);
            Assert.IsTrue(armed.Armed, "the mortar is armed by its own chain");
        }

        [Test]
        public void OneSquare_BecomesTwoHalfSquares()
        {
            // The recipe's shape, not the machine's: one square in, two half-squares out, as a decomposer
            // does to a circle. A single outlet caps the cutter at the belt's rate, so the second half
            // waits in the machine's buffer - the starvation that makes a second outlet worth wiring.
            SimWorld world = SecondChain();

            int halves = 0;
            for (int i = 0; i < 30 * 10; i++)
            {
                world.Tick(InputCommand.None);

                int made = Sim.MachineAt(world, Cutter).OutputCount;
                for (int cell = 4; cell < 10; cell++)
                    if (Sim.ShapeAt(world, new Int2(cell, 9)) == ShapeType.HalfSquare) made++;

                halves = System.Math.Max(halves, made);
                if (halves >= 2) break;
            }

            Assert.GreaterOrEqual(halves, 2, "one square became two half-squares");
        }

        [Test]
        public void ACutter_RefusesCircles_TheWayADecomposerRefusesSquares()
        {
            // The wrong-shape rule is the recipe's, not a list of machines: a cutter fed circles jams the
            // belt that fed it, with the circle still on it. The recipe's input is the whole rule.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            world.Terrain.SetOre(new Int2(3, 9), ShapeType.Circle);

            Sim.Place(world, BuildKind.Drill, new Int2(3, 9), Dir.East);
            Sim.LayRun(world, new Int2(4, 9), Dir.East, 3);
            Sim.Place(world, BuildKind.Cutter, Cutter, Dir.East);
            Sim.LayRun(world, new Int2(8, 9), Dir.East, 2);

            Sim.Tick(world, 30 * 8);

            Assert.IsTrue(world.Belts.IsJammed(new Int2(6, 9)), "the belt that fed the cutter jammed");
            Assert.AreEqual(ShapeType.Circle, Sim.ShapeAt(world, new Int2(6, 9)),
                "with the offending circle left on it");
            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.Jammed));
            Assert.IsTrue(world.Events.TryLast(SimEventKind.Jammed, out SimEvent jam));
            Assert.AreEqual(ShapeType.Circle, jam.Shape, "and the report names the shape that caused it");
            Assert.AreEqual(new Int2(6, 9), jam.Cell);
        }

        [Test]
        public void ADecomposer_RefusesSquares_TheWayACutterRefusesCircles()
        {
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            world.Terrain.SetOre(new Int2(3, 9), ShapeType.Square);

            Sim.Place(world, BuildKind.Drill, new Int2(3, 9), Dir.East);
            Sim.LayRun(world, new Int2(4, 9), Dir.East, 3);
            Sim.Place(world, BuildKind.Decomposer, Cutter, Dir.East);
            Sim.LayRun(world, new Int2(8, 9), Dir.East, 2);

            Sim.Tick(world, 30 * 8);

            Assert.IsTrue(world.Belts.IsJammed(new Int2(6, 9)), "the first chain refuses the second mineral");
            Assert.AreEqual(ShapeType.Square, Sim.ShapeAt(world, new Int2(6, 9)));
        }

        [Test]
        public void EachGun_RefusesTheOtherChainsAmmunition()
        {
            // A mortar fed half-circles jams its feed belt, and a cannon fed half-squares does the same -
            // why two turrets with two diets are a routing problem.
            SimWorld world = Sim.NewEnduringWorld(20, 12);

            Sim.LayRun(world, new Int2(4, 9), Dir.East, 1);
            Sim.Place(world, BuildKind.Mortar, new Int2(5, 9), Dir.West);

            Sim.LayRun(world, new Int2(4, 3), Dir.East, 1);
            Sim.Place(world, BuildKind.Cannon, new Int2(5, 3), Dir.West);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 9), ShapeType.HalfCircle));
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 3), ShapeType.HalfSquare));
            Sim.Tick(world, 30 * 4);

            Assert.IsTrue(world.Belts.IsJammed(new Int2(4, 9)), "the mortar refused a half-circle");
            Assert.IsTrue(world.Belts.IsJammed(new Int2(4, 3)), "and the cannon refused a half-square");
            Assert.AreEqual(2, world.Belts.JamCount);
        }

        [Test]
        public void ASorter_SeparatesTheTwoMinerals_OnOneLine()
        {
            // The chain and the router together: one input line carrying both minerals, the squares
            // leaving by the side the sorter faces, the circles carrying on down the line.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            var hub = new Int2(4, 5);

            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);        // into the hub from the south
            Sim.Place(world, BuildKind.Sorter, hub, Dir.East);      // squares leave east
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 2);         // (5,5) (6,5): squares this way
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 2);        // (4,6) (4,7): everything else

            // The shipped filter is a circle; this is a sorter retuned as a player would, swapping the
            // shapes' names and the routing with them.
            Assert.AreEqual(ShapeType.Circle, world.Content.Machine(BuildKind.Sorter).Filter);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Square));
            Sim.Tick(world, 120);
            Assert.AreEqual(ShapeType.Square, Sim.ShapeAt(world, new Int2(4, 7)),
                "a square is not the filter, so it took the other outlet");

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), ShapeType.Circle));
            Sim.Tick(world, 120);
            Assert.AreEqual(ShapeType.Circle, Sim.ShapeAt(world, new Int2(6, 5)),
                "and the circle - the filter - left by the side it faces");

            Assert.AreEqual(2, Sim.ItemCount(world), "two in, two out, none eaten");
        }
    }
}
