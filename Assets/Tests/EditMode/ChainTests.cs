using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the second production chain: a square patch, a cutter, half-square
    /// ammunition and a mortar.
    ///
    /// The point of these is not that a second chain exists - it is what adding one cost. The cutter is
    /// the decomposer's behaviour on another recipe; the mortar is the cannon's behaviour on another
    /// diet. So the tests that matter most here are the ones that show both chains obeying the *same*
    /// rules: each converter refuses the other's mineral, each gun refuses the other's ammunition, and
    /// the sorter can separate the two minerals on one line.
    ///
    /// The worlds are the 20x12 test map, whose Core sits at (8,4)-(11,7) - so the chain is laid along
    /// the bottom of the map, clear of the Core, and the worlds are unkillable ones: an enemy reaching
    /// the Core would otherwise end the run in the middle of a measurement.
    /// </summary>
    public class ChainTests
    {
        private static readonly Int2 Cutter = new Int2(7, 9);
        private static readonly Int2 Mortar = new Int2(10, 9);

        /// <summary>
        /// The second chain, laid along the bottom of the test map: a drill on a square patch at
        /// (3,9) mining east, four belt cells into the cutter, then three more into the mortar. The
        /// mortar's rest facing is west; its barrel points wherever the nearest enemy is.
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
            // Mine a square, split it, belt the halves, fire one: the whole chain, headless. The mortar
            // was the cheap way to a Spike before weaknesses counted - one 6-damage shell against its 6
            // HP - and it is the expensive way now: half-square ammo is resisted by a quarter, so the
            // same shell lands 1.5 and the kill costs four of them instead of one. The second chain is
            // still worth running, it is just no longer the shortcut past the first one.
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
            // The diet is the definition's, so what arrives at the mortar is the second recipe's output
            // - not a circle that wandered down the wrong line.
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
            // The recipe's shape, not the machine's: one square in, two half-squares out, which is
            // exactly what the decomposer does to a circle. A single outlet therefore caps the cutter
            // at the belt's rate, so the second half waits in the machine's buffer - the same
            // starvation-behind-the-stage behaviour that makes a second outlet worth wiring.
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
            // The wrong-shape rule is the recipe's, not a list of machines: a cutter is fed circles and
            // jams the belt that fed it, with the circle still on it. Nothing here knows what a cutter
            // is - the recipe's input is the whole rule.
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
            // A mortar fed half-circles jams the feeding belt, and a cannon fed half-squares does the
            // same. Two turrets with two diets is a routing problem for the player, and this is why.
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
            // The chain and the router together, which is the split the second mineral makes the sorter
            // worth building for: one input line carrying both minerals, the squares leaving by the side
            // the sorter faces, the circles carrying on down the line.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            var hub = new Int2(4, 5);

            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);        // into the hub from the south
            Sim.Place(world, BuildKind.Sorter, hub, Dir.East);      // squares leave east
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 2);         // (5,5) (6,5): squares this way
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 2);        // (4,6) (4,7): everything else

            // The shipped filter is a circle, so this is a sorter retuned the way a player would for a
            // map that mines two minerals: swap the two shapes' names and the routing swaps with them.
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
