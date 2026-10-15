using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the second production chain: a square patch, the decomposer running the
    /// second recipe, half-square ammunition and a mortar. Most pin that both chains obey the same
    /// rules: the one splitter takes either mineral and jams on anything already split, each gun
    /// refuses the other's ammunition, and the sorter separates the two minerals on one line.
    /// Worlds use the 20x12 test map, unkillable so a Core breach cannot end a measurement mid-run.
    /// </summary>
    public class ChainTests
    {
        private static readonly Int2 Converter = new Int2(7, 9);
        private static readonly Int2 Mortar = new Int2(10, 9);

        /// <summary>
        /// Lays the second chain along the bottom of the test map: a drill on a square patch at (3,9)
        /// mining east, four belt cells into the decomposer, then three more into the mortar (rest facing
        /// west; its barrel points at the nearest enemy).
        /// </summary>
        private static SimWorld SecondChain()
        {
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            world.Terrain.SetOre(new Int2(3, 9), ShapeType.Square);

            Sim.Place(world, BuildKind.Drill, new Int2(3, 9), Dir.East);
            Sim.LayRun(world, new Int2(4, 9), Dir.East, 3);       // (4,9) .. (6,9) into the decomposer
            Sim.Place(world, BuildKind.Decomposer, Converter, Dir.East);
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
            Assert.AreEqual(ShapeType.HalfSquare, world.Content.Turret(BuildKind.Mortar).PrimaryAmmo);
            Assert.IsTrue(armed.Armed, "the mortar is armed by its own chain");
        }

        [Test]
        public void OneSquare_BecomesTwoHalfSquares()
        {
            // The recipe's shape, not the machine's: one square in, two half-squares out, as a circle
            // becomes two half-circles. A single outlet caps the decomposer at the belt's rate, so the
            // second half waits in the machine's buffer - the starvation that makes a second outlet
            // worth wiring.
            SimWorld world = SecondChain();

            int halves = 0;
            for (int i = 0; i < 30 * 10; i++)
            {
                world.Tick(InputCommand.None);

                int made = Sim.MachineAt(world, Converter).OutputCount;
                for (int cell = 4; cell < 10; cell++)
                    if (Sim.ShapeAt(world, new Int2(cell, 9)) == ShapeType.HalfSquare) made++;

                halves = System.Math.Max(halves, made);
                if (halves >= 2) break;
            }

            Assert.GreaterOrEqual(halves, 2, "one square became two half-squares");
        }

        [Test]
        public void TheOneDecomposer_SplitsEitherMineral()
        {
            // The merge: there is no cutter, because there needn't be - the delivered shape picks the
            // recipe. Feed the one machine a circle, then a square, off the same belt.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            var input = new Int2(4, 9);
            var output = new Int2(6, 9);

            Sim.LayRun(world, input, Dir.East, 1);                       // into the decomposer
            Sim.Place(world, BuildKind.Decomposer, new Int2(5, 9), Dir.East);
            Sim.LayRun(world, output, Dir.East, 1);                      // and away again

            Assert.IsTrue(world.Belts.TrySpawnItem(input, ShapeType.Circle));
            Assert.AreEqual(ShapeType.HalfCircle, TickUntilOutput(world, output),
                "a circle in, half-circles out");
            world.Belts.TryTakeItem(output, out _);

            // A split makes two: the second half waits in the buffer and must drain before the machine
            // takes the other mineral - a buffer holds one shape at a time.
            Assert.AreEqual(ShapeType.HalfCircle, TickUntilOutput(world, output), "its twin follows");
            world.Belts.TryTakeItem(output, out _);

            Assert.IsTrue(world.Belts.TrySpawnItem(input, ShapeType.Square));
            Assert.AreEqual(ShapeType.HalfSquare, TickUntilOutput(world, output),
                "the same machine takes the second mineral");

            Assert.AreEqual(0, world.Belts.JamCount, "neither mineral is the wrong shape");
        }

        [Test]
        public void TheOneDecomposer_StillJams_OnAnythingAlreadySplit()
        {
            // The wrong-shape rule outlives the cutter: halves are what the machine makes, not what it
            // eats, so one fed back jams the belt that brought it, with the shape left on it.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            var input = new Int2(4, 9);

            Sim.LayRun(world, input, Dir.East, 1);
            Sim.Place(world, BuildKind.Decomposer, new Int2(5, 9), Dir.East);
            Sim.LayRun(world, new Int2(6, 9), Dir.East, 1);

            Assert.IsTrue(world.Belts.TrySpawnItem(input, ShapeType.HalfSquare));
            Sim.Tick(world, 30 * 4);

            Assert.IsTrue(world.Belts.IsJammed(input), "a half-square is no recipe's input");
            Assert.AreEqual(ShapeType.HalfSquare, Sim.ShapeAt(world, input),
                "with the offending shape left on the belt");
            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.Jammed));
            Assert.IsTrue(world.Events.TryLast(SimEventKind.Jammed, out SimEvent jam));
            Assert.AreEqual(ShapeType.HalfSquare, jam.Shape, "and the report names the shape");
            Assert.AreEqual(input, jam.Cell);
        }

        /// <summary>Tick until an item reaches <paramref name="cell"/> (cap 10 s), returning its shape,
        /// or None if nothing ever arrived.</summary>
        private static ShapeType TickUntilOutput(SimWorld world, Int2 cell)
        {
            for (int i = 0; i < 30 * 10; i++)
            {
                world.Tick(InputCommand.None);
                ShapeType shape = Sim.ShapeAt(world, cell);
                if (shape != ShapeType.None) return shape;
            }

            return ShapeType.None;
        }

        [Test]
        public void AMortarShell_BurstsOverTheWholePack()
        {
            // The mortar's difference that is not a stat: two Bulwarks shoulder to shoulder, one shell,
            // two kills - the pack answer the cannon has no equivalent of.
            SimWorld world = Sim.NewEnduringWorld(20, 12);

            Sim.LayRun(world, new Int2(4, 9), Dir.East, 1);
            Sim.Place(world, BuildKind.Mortar, new Int2(5, 9), Dir.East);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 9), ShapeType.HalfSquare));

            // Side by side across the lane, so the walk keeps them shoulder to shoulder.
            world.Enemies.Spawn(EnemyKind.Bulwark, new Vec2(10.5f, 9.2f));
            world.Enemies.Spawn(EnemyKind.Bulwark, new Vec2(10.5f, 9.8f));

            Sim.Tick(world, 30 * 5);

            Assert.AreEqual(0, world.Enemies.AliveCount, "one bursting shell took the pair");
            Assert.AreEqual(2, world.Events.CountOf(SimEventKind.EnemyKilled));
            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.ShotFired), "one shell, not two");
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

        [Test]
        public void AnAssembler_FusesTwoHalves_IntoAComposite()
        {
            // The ladder's deep rung: one half-circle and one half-square in, one composite out -
            // the two chains meeting at one machine.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            var machine = new Int2(5, 9);
            var output = new Int2(6, 9);

            Sim.LayRun(world, new Int2(4, 9), Dir.East, 1);        // half-circles in from the west
            Sim.Place(world, BuildKind.Assembler, machine, Dir.East);
            Sim.LayRun(world, new Int2(5, 10), Dir.South, 1);      // half-squares in from the north
            Sim.LayRun(world, output, Dir.East, 1);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 9), ShapeType.HalfCircle));
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 10), ShapeType.HalfSquare));

            Assert.AreEqual(ShapeType.Composite, TickUntilOutput(world, output),
                "the two halves fused into a composite");
            Assert.AreEqual(0, world.Belts.JamCount);
        }

        [Test]
        public void AnAssembler_HoldsOneHalf_UntilItsPairArrives()
        {
            // One input alone is not a jam and not a cycle: the machine holds it and waits.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            var machine = new Int2(5, 9);

            Sim.LayRun(world, new Int2(4, 9), Dir.East, 1);
            Sim.Place(world, BuildKind.Assembler, machine, Dir.East);
            Sim.LayRun(world, new Int2(6, 9), Dir.East, 1);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 9), ShapeType.HalfCircle));
            Sim.Tick(world, 30 * 4);

            MachineState state = Sim.MachineAt(world, machine);
            Assert.AreEqual(ShapeType.HalfCircle, state.Carried, "the half-circle is held");
            Assert.AreEqual(0, state.OutputCount, "and nothing is made of it alone");
            Assert.AreEqual(0, world.Belts.JamCount, "a held half is a stall, not a jam");
        }

        [Test]
        public void AnAssembler_CompletesTheRecipeItsFirstHalfCommittedTo()
        {
            // Two fuses may share an input, and then only the row the first half committed to can finish
            // the cycle: a delivery that would complete the *other* row waits on its belt instead of
            // silently steering the machine onto a different recipe - and a different output - mid-cycle.
            var alloy = new RecipeDef(new ContentId("alloy"), ShapeType.HalfCircle, ShapeType.Circle,
                inputCount: 1, outputCount: 2, interval: 1.5f, buffer: 4, input2: ShapeType.Square);

            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig { CoreMaxHp = 1000000f },
                CustomContentTests.Rewrite(extraRecipe: alloy));

            var machine = new Int2(5, 9);
            var west = new Int2(4, 9);          // half-circles: first in, so they commit the row
            var north = new Int2(5, 10);        // squares: the *other* row's second half
            var south = new Int2(5, 8);         // half-squares: the committed row's second half
            var output = new Int2(6, 9);

            Sim.LayRun(world, west, Dir.East, 1);
            Sim.Place(world, BuildKind.Assembler, machine, Dir.East);
            Sim.LayRun(world, north, Dir.South, 1);
            Sim.LayRun(world, south, Dir.North, 1);
            Sim.LayRun(world, output, Dir.East, 1);

            Assert.IsTrue(world.Belts.TrySpawnItem(west, ShapeType.HalfCircle));
            Sim.Tick(world, 45);
            Assert.AreEqual(ShapeType.HalfCircle, Sim.MachineAt(world, machine).Carried,
                "the half-circle is held, in the row the table reaches first");

            Assert.IsTrue(world.Belts.TrySpawnItem(north, ShapeType.Square));
            Sim.Tick(world, 30 * 4);

            Assert.AreEqual(ShapeType.None, Sim.MachineAt(world, machine).Carried2,
                "the square completes the other row, so it is not this cycle's partner");
            Assert.AreEqual(ShapeType.Square, Sim.ShapeAt(world, north), "it waits on its own belt");
            Assert.AreEqual(0, world.Belts.JamCount, "and waiting is not jamming - the row does eat squares");
            Assert.AreEqual(ShapeType.None, Sim.ShapeAt(world, output), "nothing has been fused");

            Assert.IsTrue(world.Belts.TrySpawnItem(south, ShapeType.HalfSquare));
            Assert.AreEqual(ShapeType.Composite, TickUntilOutput(world, output),
                "the committed pair is what completes the cycle");
        }

        [Test]
        public void AnAssembler_Jams_OnAnythingThatIsNotAHalf()
        {
            // Raw minerals are the decomposer's food, not the assembler's: the wrong-shape rule holds.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            var input = new Int2(4, 9);

            Sim.LayRun(world, input, Dir.East, 1);
            Sim.Place(world, BuildKind.Assembler, new Int2(5, 9), Dir.East);
            Sim.LayRun(world, new Int2(6, 9), Dir.East, 1);

            Assert.IsTrue(world.Belts.TrySpawnItem(input, ShapeType.Circle));
            Sim.Tick(world, 30 * 4);

            Assert.IsTrue(world.Belts.IsJammed(input), "a whole circle is no fuse's input");
            Assert.AreEqual(ShapeType.Circle, Sim.ShapeAt(world, input));
        }

        [Test]
        public void AComposite_MatchesEveryWeakness()
        {
            // The deep chain's payoff: the composite's structure contains both minerals, so no enemy
            // resists it - full damage to the Spike's half-circle weakness and the Bulwark's half-square one.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            int spike = world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10.5f, 8.5f));
            int bulwark = world.Enemies.Spawn(EnemyKind.Bulwark, new Vec2(10.5f, 9.5f));

            world.Enemies.ApplyDamage(spike, 3f, ShapeType.Composite);
            world.Enemies.ApplyDamage(bulwark, 3f, ShapeType.Composite);

            Assert.AreEqual(3f, world.Content.Enemy(EnemyKind.Spike).Hp - world.Enemies.EnemyAt(0).Hp, Sim.Tol,
                "full damage against the Spike");
            Assert.AreEqual(3f, world.Content.Enemy(EnemyKind.Bulwark).Hp -
                world.Enemies.EnemyAt(world.Enemies.AliveCount - 1).Hp, Sim.Tol,
                "and against the Bulwark - a plain half would have landed a quarter of it");
        }

        [Test]
        public void ACannon_FedComposite_OneShotsABulwark()
        {
            // The whole ladder end to end at a gun: a cannon eating composite rounds answers the enemy
            // its half-circles barely mark - one shell, where eight resisted shots were needed.
            SimWorld world = Sim.NewEnduringWorld(20, 12);

            Sim.LayRun(world, new Int2(4, 9), Dir.East, 1);
            Sim.Place(world, BuildKind.Cannon, new Int2(5, 9), Dir.East);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 9), ShapeType.Composite));
            world.Enemies.Spawn(EnemyKind.Bulwark, new Vec2(11.5f, 9.5f));

            Sim.Tick(world, 30 * 5);

            Assert.AreEqual(0, world.Enemies.AliveCount, "the Bulwark is down");
            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.ShotFired), "one composite shell did it");
            Assert.AreEqual(0, world.Belts.JamCount, "and composite is in the cannon's diet, so no jam");
        }
    }
}
