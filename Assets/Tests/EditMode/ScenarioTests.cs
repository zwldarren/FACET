using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The end-to-end balance tests: a whole map-1 run, played by the real production chain, on the
    /// real map, inside the real budget.
    ///
    /// Nothing here cheats past the economy: the defence is bought with the starting stockpile plus
    /// what the economy line physically belts home, which is the claim the design makes - that the
    /// map is winnable by mining, not by fiat. The second test cuts the decomposer out of the same
    /// layout: raw circles reach the cannon, the line jams, and the run is lost. The twist, as a test.
    /// </summary>
    public class ScenarioTests
    {
        [Test]
        public void Maps_AreWellFormed()
        {
            // The structural half of "is this map sane" is MapValidation's, so that the maps written in
            // code, the maps painted in the Editor window and the maps a test invents are all held to one
            // list of rules rather than three. What is asserted here is what the validator deliberately
            // does not own: the pacing the design asks for, and the table's own arithmetic.
            foreach (MapDefinition map in Sim.Maps)
            {
                Assert.IsEmpty(MapValidation.Validate(map), map.Name + " is not playable data");

                for (int w = 0; w < map.Waves.Length; w++)
                {
                    WaveDefinition wave = map.Waves[w];
                    Assert.Greater(wave.Total, 0, map.Name + ": an empty wave is a stuck wave");

                    // The first wave is never counted down - the player starts it by hand, however long
                    // they have spent building - so the "time to rebuild in" number is only owed by
                    // every wave after it. That is pacing, not structure: a five-second intermission is
                    // a valid map and a bad one, and no data error is what makes it bad.
                    if (w > 0)
                        Assert.GreaterOrEqual(wave.Intermission, 20f,
                            map.Name + ": there has to be time to rebuild in");

                    int summed = 0;
                    foreach (SpawnGroup group in wave.Groups) summed += group.Count;
                    Assert.AreEqual(wave.Total, summed, map.Name + ": the preview must add up to the wave");
                }

                foreach (ShapePatch patch in map.Patches)
                    foreach (Int2 corner in new[] { patch.Origin,
                             new Int2(patch.Origin.X + patch.Width - 1, patch.Origin.Y + patch.Height - 1) })
                        Assert.IsTrue(corner.X >= 0 && corner.Y >= 0 && corner.X < map.Width && corner.Y < map.Height,
                            map.Name + ": a patch hangs off the map");
            }
        }

        [Test]
        public void MapOne_HoldsItsWave_OnARealEconomy()
        {
            SimWorld world = Sim.NewWorld();
            int startedWith = world.Economy.Circles;

            // First the money: drill plus twenty belts home. Thirty circles, forty left - not enough
            // for the defence, so the run waits on what the line banks.
            Sim.BuildMapOneEconomyLine(world);
            Assert.AreEqual(startedWith - 30, world.Economy.Circles, "the economy line is paid for");

            Sim.TickUntilFunded(world, world.Content.Machine(BuildKind.Drill).Cost
                + world.Content.Machine(BuildKind.Decomposer).Cost
                + world.Content.Machine(BuildKind.Pipe).Cost
                + world.Content.Machine(BuildKind.Cannon).Cost + 26);
            Assert.Greater(world.Economy.TotalBanked, 0, "circles physically belted into the Core");

            Sim.BuildMapOneDefence(world, out _, out _);
            Assert.AreEqual(0, world.Economy.Circles,
                "the whole budget is on the map now: " + startedWith + " to start plus what was banked");

            // Let the ammo line fill (27 tiles of belt latency), then call the wave and fight it out.
            Sim.Tick(world, (int)(35 * SimConfig.TickRate));
            world.Waves.StartNextWave();
            for (int i = 0; i < 180 * SimConfig.TickRate && world.Status == GameStatus.Playing; i++)
                world.Tick(InputCommand.None);

            Assert.AreEqual(GameStatus.Won, world.Status,
                "lost with " + world.Core.Hp + " hp left and " + world.ShotsFired + " shots fired");
            Assert.AreEqual(world.Config.CoreMaxHp, world.Core.Hp, Sim.Tol, "the Core untouched");
            Assert.Greater(world.ShotsFired, 0, "won by shooting");
            Assert.AreEqual(0, world.Belts.JamCount, "every line fed the shape its machine eats");
        }

        [Test]
        public void FeedingTheCannonRawCircles_JamsIt_AndLosesTheRun()
        {
            // The same layout with one thing changed: no decomposer and no pipe, so the belts carry
            // raw circles straight into the cannon's mouth - a re-routing mistake a player makes in
            // one drag. The delivery jams the last segment, the cannon never fires, and the wave
            // walks in. The wave starts only after the jam has landed: the claim is that the jam,
            // not the schedule, loses the run.
            SimWorld world = Sim.NewWorld();

            Sim.Place(world, BuildKind.Drill, new Int2(62, 23), Dir.West);
            Sim.LayRun(world, new Int2(61, 23), Dir.West, 18);  // (61,23) .. (44,23): straight through
            Sim.LayRun(world, new Int2(43, 23), Dir.South, 1);
            Sim.LayRun(world, new Int2(43, 22), Dir.South, 4);
            Sim.LayRun(world, new Int2(43, 18), Dir.West, 1);
            Sim.LayRun(world, new Int2(42, 18), Dir.West, 4);
            Sim.Place(world, BuildKind.Cannon, new Int2(38, 18), Dir.West);

            for (int i = 0; i < 90 * SimConfig.TickRate && world.Belts.JamCount == 0; i++)
                world.Tick(InputCommand.None);

            Assert.Greater(world.Belts.JamCount, 0, "the wrong-shape delivery jams the line it lands on");
            Assert.AreEqual(0, world.ShotsFired, "the cannon never fired: circles are not ammunition");

            world.Waves.StartNextWave();
            for (int i = 0; i < 150 * SimConfig.TickRate && world.Status == GameStatus.Playing; i++)
                world.Tick(InputCommand.None);

            Assert.AreEqual(GameStatus.Lost, world.Status,
                "a cannon fed raw mineral is no defence, and the run has to end that way");
            Assert.Greater(world.Belts.JamCount, 0, "and the jam is still there to be found");
        }
    }
}
