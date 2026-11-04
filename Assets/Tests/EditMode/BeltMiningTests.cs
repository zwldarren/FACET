using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// A belt laid on ore mines it (<see cref="BeltMining"/>): the drill's job without the drill, so the first
    /// coin is one building and one drag away. And the two kinds it retires - the drill and the wall - are off
    /// the bar but still in the rules.
    /// </summary>
    public class BeltMiningTests
    {
        /// <summary>A 30x12 field, Core at (8,4)-(11,7), a single circle vein at (20,5).</summary>
        private static SimWorld World()
            => new SimWorld(new MapDefinition("mining", 30, 12, 1000,
                new[] { new ShapePatch(new Int2(20, 5), 1, 1, Quad.Circle) },
                new Int2[0], new WaveDefinition[0], coreCell: new Int2(8, 4)), new SimConfig());

        [Test]
        public void ABeltOnOre_MinesAShapeOntoItself_AtTheDrillsRate()
        {
            SimWorld world = World();
            Sim.LayRun(world, new Int2(20, 5), Dir.East, 1);    // on the vein, pointing away from the Core

            Sim.Tick(world, 1);
            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, new Int2(20, 5)), "a circle is mined at once");

            float interval = world.Content.Machine(BuildKind.Drill).Interval;
            Sim.LayRun(world, new Int2(21, 5), Dir.East, 4);
            Sim.Tick(world, (int)(10 * SimConfig.TickRate));
            int items = Sim.ItemsIn(world).Count;
            Assert.LessOrEqual(items, (int)(10f / interval) + 1, "no faster than a drill on that tile");
            Assert.Greater(items, 1, "and it keeps mining");
        }

        [Test]
        public void ABeltDraggedFromTheOreIntoTheCore_BanksCoins_WithNoDrill()
        {
            SimWorld world = World();
            Sim.LayRun(world, new Int2(20, 5), Dir.West, 9);    // (20,5) .. (12,5), into the Core's east side

            Sim.Tick(world, (int)(20 * SimConfig.TickRate));
            Assert.Greater(world.Economy.TotalBanked, 5, "the line is a money line on its own");
            Assert.AreEqual(0, world.Machines.CountOf(BuildKind.Drill));
        }

        [Test]
        public void ABeltOffTheOre_MinesNothing()
        {
            SimWorld world = World();
            Sim.LayRun(world, new Int2(19, 6), Dir.West, 7);

            Sim.Tick(world, (int)(5 * SimConfig.TickRate));
            Assert.AreEqual(0, Sim.ItemsIn(world).Count);
            Assert.AreEqual(0, world.Economy.TotalBanked);
        }

        [Test]
        public void AFullBeltOnOre_WaitsForRoom_RatherThanOverfilling()
        {
            SimWorld world = World();
            Sim.LayRun(world, new Int2(20, 5), Dir.East, 1);    // runs off into nothing: the item stops at the end

            Sim.Tick(world, (int)(10 * SimConfig.TickRate));
            Assert.AreEqual(1, Sim.ItemsIn(world).Count, "one item per tile, whatever the vein wants");
        }

        [Test]
        public void TheDrillAndTheWall_AreOffTheBar_ButStillPlaceable()
        {
            SimWorld world = World();
            Assert.IsFalse(world.IsUnlocked(BuildKind.Drill), "no drill key");
            Assert.IsFalse(world.IsUnlocked(BuildKind.Wall), "no wall key");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Belt));

            Sim.Place(world, BuildKind.Drill, new Int2(20, 5), Dir.West);
            Sim.Place(world, BuildKind.Wall, new Int2(25, 9), Dir.North);
        }

        [Test]
        public void MiningStartsOverOnARestart()
        {
            SimWorld world = World();
            Sim.LayRun(world, new Int2(20, 5), Dir.West, 9);
            Sim.Tick(world, 90);
            world.Tick(new InputCommand(restartPressed: true));

            Assert.AreEqual(0, Sim.ItemsIn(world).Count, "a restart clears the line");
            Sim.LayRun(world, new Int2(20, 5), Dir.West, 9);
            Sim.Tick(world, 1);
            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, new Int2(20, 5)), "and a new belt mines at once");
        }
    }
}
