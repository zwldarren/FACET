using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the second mineral's chain and for the rule that replaced the ammo table: a
    /// round's <em>identity</em> against the group's demand. A square vein, the same cutter that splits a
    /// circle, and the one universal turret - so what this file pins is that the operator set is
    /// mineral-agnostic (one cutter, one sorter, any shape) and that damage is a quarter unless the round
    /// <em>is</em> the item the wave asked for.
    ///
    /// The chain that used to run here ended in a mortar firing half-squares at a Bulwark that only a
    /// half-circle could hurt; both guns and both weaknesses are gone, so those tests are rewritten around
    /// the replacement rule rather than deleted: the same 6-hp archetype now costs two matched rounds and
    /// six resisted ones.
    ///
    /// Worlds use the 20x12 test map, unkillable so a Core breach cannot end a measurement mid-run.
    /// </summary>
    public class ChainTests
    {
        /// <summary>The cutter the second chain runs through, and the gun it feeds.</summary>
        private static readonly Int2 Cutter = new Int2(7, 9);
        private static readonly Int2 Turret = new Int2(7, 11);

        /// <summary>The item this file's chain is asked for: the piece a north-facing cutter pushes out of
        /// its front outlet, which is the only piece the turret is wired to receive.</summary>
        private static readonly Shape Demand = Sim.TopHalfSquare;

        /// <summary>
        /// Lays the second chain up the middle of the test map: a drill on a square vein at (3,9) mining
        /// east, three belt cells into a cutter at (7,9) facing north, one belt north out of it, and the
        /// turret at (7,11) - so the chain's product is the top half-square and nothing else. (The bottom
        /// half leaves by the cutter's other outlet, which this layout deliberately does not wire: half the
        /// product is not the round the briefing asked for, which is the whole point of orientation.)
        /// </summary>
        private static SimWorld SecondChain()
        {
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            world.Terrain.SetOre(new Int2(3, 9), Quad.Square);

            Sim.Place(world, BuildKind.Drill, new Int2(3, 9), Dir.East);
            Sim.LayRun(world, new Int2(4, 9), Dir.East, 3);       // (4,9) .. (6,9) into the cutter
            Sim.Place(world, BuildKind.Cutter, Cutter, Dir.North);
            Sim.LayRun(world, new Int2(7, 10), Dir.North, 1);     // out of the cutter, up into the gun
            Sim.Place(world, BuildKind.Turret, Turret, Dir.West);
            return world;
        }

        [Test]
        public void TheSecondChain_RunsFromAPatch_ToAKill()
        {
            // Mine a square, cut it, belt the piece, fire: the whole chain, headless. Six health against
            // four matched damage is two rounds - where the same round against an enemy demanding something
            // else would be a quarter of that, and the belt's one-per-second ceiling is what makes the
            // difference between holding a wave and watching the Core fall.
            SimWorld world = SecondChain();
            Assert.Greater(world.Enemies.Spawn(EnemyKind.Tank, new Vec2(13.5f, 10.5f), Demand), 0);

            for (int i = 0; i < 30 * 20 && world.Enemies.AliveCount > 0; i++) world.Tick(InputCommand.None);

            Assert.AreEqual(0, world.Enemies.AliveCount, "the chain brought the enemy down");
            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.EnemyKilled));
            Assert.AreEqual(2, world.ShotsFired, "two matched rounds: 6 hp against 4 damage, rounded up");
        }

        [Test]
        public void TheGun_HasNoAmmoTable_SoTheRoundIsWhateverTheLineDelivered()
        {
            // What a mortar's diet used to decide is the line's business now. The turret's row carries
            // any-shape as its diet - the universal chamber - so the interesting claim is that the item on
            // its input belt is the chain's own product, and that the chamber would take anything else too.
            SimWorld world = SecondChain();

            MachineState chamber = default;
            for (int i = 0; i < 30 * 20; i++)
            {
                world.Tick(InputCommand.None);
                chamber = Sim.MachineAt(world, Turret);
                if (chamber.Armed) break;
            }

            Assert.IsTrue(chamber.Armed, "the gun is armed by its own chain");
            Assert.AreEqual(Demand, Sim.ShapeAt(world, new Int2(7, 10)),
                "and the round waiting for it is the piece the cutter's front outlet made");

            Assert.AreEqual(FilterMode.Any, ContentDatabase.Default.Machine(BuildKind.Turret).Diet.Mode,
                "the chamber's diet is 'anything a belt can carry' - there is no ammo row to satisfy");
            Assert.IsTrue(ContentDatabase.Default.Machine(BuildKind.Turret).Diet.Matches(Sim.Circle),
                "so a circle line would feed it too, and the round would simply be worth less");
        }

        [Test]
        public void OneSquare_BecomesTwoHalfSquares()
        {
            // The cut line's own shape, not a machine's: one square in, one top half out the north and one
            // bottom half out the south, at the same moment. Neither outlet can carry two, so the pieces
            // that cannot leave yet wait inside their own slot - which is what makes wiring both sides worth
            // doing rather than a way to lose a piece.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            var input = new Int2(4, 9);

            Sim.LayRun(world, input, Dir.East, 1);                  // into the cutter
            Sim.Place(world, BuildKind.Cutter, new Int2(5, 9), Dir.North);
            Sim.LayRun(world, new Int2(5, 10), Dir.North, 1);       // the front (top) outlet
            Sim.LayRun(world, new Int2(5, 8), Dir.South, 1);        // the back (bottom) outlet

            Assert.IsTrue(world.Belts.TrySpawnItem(input, Sim.Square));
            Sim.Tick(world, 30 * 6);

            Assert.AreEqual(Sim.TopHalfSquare, Sim.ShapeAt(world, new Int2(5, 10)), "the top half went north");
            Assert.AreEqual(Sim.BottomHalfSquare, Sim.ShapeAt(world, new Int2(5, 8)),
                "and the bottom half went south, in the same cycle");
            Assert.AreEqual(2, Sim.ItemCount(world), "one square became exactly two halves, no more");
            Assert.IsFalse(world.Belts.HasItemAt(input), "the input was eaten, not dropped");

            MachineState cutter = Sim.MachineAt(world, new Int2(5, 9));
            Assert.AreEqual(0, cutter.OutputCount, "and neither slot is holding anything back");
            Assert.AreEqual(0, cutter.OutputCount2);
        }

        [Test]
        public void TheOneCutter_SplitsEitherMineral()
        {
            // The merge: there is no per-input recipe any more, because processing is an operator. The same
            // machine, fed off the same belt, cuts a circle and then a square - the delivered shape picks
            // the piece, and neither is a wrong-shape delivery.
            //
            // Both outlets are wired, as they would be on a line: a slot counts pieces of one shape, so a
            // cutter whose other side is queueing holds the next item on its belt rather than rewriting what
            // is already queued behind it - which is a stall, and the layout is what avoids it.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            var input = new Int2(4, 9);
            var output = new Int2(5, 10);
            var other = new Int2(5, 8);

            Sim.LayRun(world, input, Dir.East, 1);
            Sim.Place(world, BuildKind.Cutter, new Int2(5, 9), Dir.North);
            Sim.LayRun(world, output, Dir.North, 1);
            Sim.LayRun(world, other, Dir.South, 1);   // the cut's other piece, out the opposite side

            Assert.IsTrue(world.Belts.TrySpawnItem(input, Sim.Circle));
            Assert.AreEqual(Sim.TopHalfCircle, TickUntilOutput(world, output), "a circle in, a half-circle out");
            Assert.AreEqual(Sim.BottomHalfCircle, Sim.ShapeAt(world, other),
                "with the other piece out of the outlet opposite the one it faces");
            world.Belts.TryTakeItem(output, out _);

            Assert.IsTrue(world.Belts.TrySpawnItem(input, Sim.Square));
            Assert.AreEqual(Sim.TopHalfSquare, TickUntilOutput(world, output),
                "the same machine takes the second mineral, with no table consulted");

            Assert.AreEqual(0, world.Belts.JamCount, "neither mineral is the wrong shape for a cutter");
        }

        [Test]
        public void ASorter_SeparatesTheTwoMinerals_OnOneLine()
        {
            // The chain and the router together: one input line carrying both minerals, the items matching
            // the filter leaving by the side the sorter faces, the rest carrying on down the line. The
            // shipped filter is "anything with a circle in it", which is the money rule - a square-bearing
            // item is not money, so it takes the other outlet.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            var hub = new Int2(4, 5);

            Sim.LayRun(world, new Int2(4, 4), Dir.North, 1);        // into the hub from the south
            Sim.Place(world, BuildKind.Sorter, hub, Dir.East);      // circles leave east
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 2);         // (5,5) (6,5): the filtered way
            Sim.LayRun(world, new Int2(4, 6), Dir.North, 2);        // (4,6) (4,7): everything else

            Assert.AreEqual(ShapeFilter.Has(Quad.Circle), world.Content.Machine(BuildKind.Sorter).Filter,
                "the shipped sorter routes for the money shape");

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Square));
            Sim.Tick(world, 120);
            Assert.AreEqual(Sim.Square, Sim.ShapeAt(world, new Int2(4, 7)),
                "a square carries no circle, so it took the other outlet");

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 4), Sim.Circle));
            Sim.Tick(world, 120);
            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, new Int2(6, 5)),
                "and the circle - which the filter matches - left by the side it faces");

            Assert.AreEqual(2, Sim.ItemCount(world), "two in, two out, none eaten");
        }

        [Test]
        public void AMixedRound_IsOneItem_NotAWildcard()
        {
            // The rule the old composite broke. A shape's structure "containing both minerals" used to make
            // it match every weakness, which made one deep round the answer to the whole roster. Weakness is
            // exact identity now: the composite is worth full damage against an enemy that demands the
            // composite and a quarter against anything else, however much of both minerals it carries.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            int asksForIt = world.Enemies.Spawn(EnemyKind.Runner, new Vec2(10.5f, 8.5f), Sim.Composite);
            int asksForOther = world.Enemies.Spawn(EnemyKind.Runner, new Vec2(10.5f, 9.5f), Sim.TopHalfCircle);

            Assert.IsTrue(Sim.Composite.Contains(Quad.Circle) && Sim.Composite.Contains(Quad.Square),
                "the test's premise: the round is made of both minerals");

            // Neither shot kills, so slots do not reorder and each enemy can be read where it was spawned.
            world.Enemies.ApplyDamage(asksForIt, 3f, Sim.Composite);
            world.Enemies.ApplyDamage(asksForOther, 3f, Sim.Composite);

            float hp = world.Content.Enemy(EnemyKind.Runner).Hp;
            Assert.AreEqual(hp - 3f, world.Enemies.EnemyAt(0).Hp, Sim.Tol,
                "the enemy that demanded the composite takes the full 3");
            Assert.AreEqual(hp - 0.75f, world.Enemies.EnemyAt(1).Hp, Sim.Tol,
                "and the one that demanded a half-circle takes a quarter, both minerals or not");
        }

        [Test]
        public void TheSameSixHealth_CostsTwoMatchedRounds_OrSixResistedOnes()
        {
            // The price rule at one gun, as the number a player feels: 6 hp against the shipped 4 damage is
            // two rounds when the round is what the group asked for, and six when it is not - six seconds of
            // one belt's supply against two, at a fire rate the belt already caps.
            Assert.AreEqual(2, RoundsToKill(Sim.TopHalfCircle, Sim.TopHalfCircle), "a matched round");
            Assert.AreEqual(6, RoundsToKill(Sim.Square, Sim.TopHalfCircle),
                "a resisted round lands a quarter of 4, so the same enemy takes six of them");
        }

        /// <summary>
        /// One turret at (5,9), a belt into it kept stocked with <paramref name="round"/> every tick, and
        /// one 6-hp Runner that demands <paramref name="demand"/>. Returns the shots it took to bring the
        /// enemy down - the gun's real cost of a mismatch, measured rather than asserted from constants.
        /// </summary>
        private static int RoundsToKill(Shape round, Shape demand)
        {
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            var belt = new Int2(4, 9);

            Sim.LayRun(world, belt, Dir.East, 1);
            Sim.Place(world, BuildKind.Turret, new Int2(5, 9), Dir.East);
            Assert.Greater(world.Enemies.Spawn(EnemyKind.Runner, new Vec2(9.5f, 9.5f), demand), 0);

            for (int i = 0; i < 30 * 60 && world.Enemies.AliveCount > 0; i++)
            {
                world.Belts.TrySpawnItem(belt, round);
                world.Tick(InputCommand.None);
            }

            Assert.AreEqual(0, world.Enemies.AliveCount, "the gun never brought the enemy down");
            return world.ShotsFired;
        }

        /// <summary>Tick until an item reaches <paramref name="cell"/> (cap 10 s), returning its shape,
        /// or nothing if it never arrived.</summary>
        private static Shape TickUntilOutput(SimWorld world, Int2 cell)
        {
            for (int i = 0; i < 30 * 10; i++)
            {
                world.Tick(InputCommand.None);
                Shape shape = Sim.ShapeAt(world, cell);
                if (!shape.IsEmpty) return shape;
            }

            return Shape.None;
        }
    }
}
