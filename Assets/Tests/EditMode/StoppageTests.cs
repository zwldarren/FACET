using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The line-stoppage analytics metric: how often a production line stops, split by the game's three
    /// causes - a jammed belt, a blocked belt, a stalled machine. Read off the same lists the status card
    /// reads, cut into one row per wave by <see cref="StoppageTracker"/>, and sent from the WebGL build by
    /// the analytics reporter.
    /// </summary>
    public class StoppageTests
    {
        [Test]
        public void ADeadEnd_IsCountedOnce_WhileItStands_AndANewOneIsCountedAgain()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(2, 5), Dir.East, 2);           // (2,5) (3,5), bare ground at (4,5)
            var tracker = new StoppageTracker();

            world.Belts.TrySpawnItem(new Int2(3, 5), Sim.Circle);
            TickAndPoll(world, tracker, 60);

            Assert.IsTrue(world.Belts.IsBlocked(new Int2(3, 5)), "the line ends in the air");
            Assert.AreEqual(1, tracker.Counted.Blocked, "one dead end, one stoppage");

            TickAndPoll(world, tracker, 60);
            Assert.AreEqual(1, tracker.Counted.Blocked, "standing for longer is still the same stoppage");

            Sim.Place(world, BuildKind.Sink, new Int2(4, 5), Dir.East); // the cure: the line now ends in a sink
            TickAndPoll(world, tracker, 30);
            Assert.IsFalse(world.Belts.IsBlocked(new Int2(3, 5)), "a finished line is not a stoppage");
            Assert.AreEqual(1, tracker.Counted.Blocked, "and curing one does not take it back off the count");

            Sim.LayRun(world, new Int2(6, 2), Dir.South, 2);          // (6,2) (6,1), the map's edge at (6,0)
            world.Belts.TrySpawnItem(new Int2(6, 1), Sim.Circle);
            TickAndPoll(world, tracker, 60);

            Assert.IsTrue(world.Belts.IsBlocked(new Int2(6, 1)), "a second line that ends in the air");
            Assert.AreEqual(2, tracker.Counted.Blocked, "a new dead end is a new stoppage");
            Assert.AreEqual(0, tracker.Counted.Jammed);
            Assert.AreEqual(0, tracker.Counted.Stalled, "nothing here is a machine");
        }

        [Test]
        public void ACutterWithNowhereForItsSecondPiece_IsCountedAsStalled()
        {
            // The stoppage the GDD's hypothesis is about: the displaced piece's lane has no belt, so the cutter
            // holds it and stops - a stall, not a dead end, because no belt is parked anywhere.
            SimWorld world = Sim.NewWorld(20, 12);
            world.Terrain.SetOre(new Int2(4, 4), Quad.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(4, 4), Dir.North);
            Sim.LayRun(world, new Int2(4, 5), Dir.North, 1);          // into the cutter's fed tile
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 6), Dir.North);
            Sim.LayRun(world, new Int2(4, 7), Dir.North, 1);          // the lane that keeps its piece
            Sim.Place(world, BuildKind.Sink, new Int2(4, 8), Dir.North);
            var tracker = new StoppageTracker();

            TickAndPoll(world, tracker, 600);

            Assert.AreEqual(1, tracker.Counted.Stalled, "one cutter, stopped once, for as long as it stands");
            Assert.AreEqual(0, tracker.Counted.Blocked, "the line behind it is queued, not dead-ended");
            Assert.AreEqual(0, tracker.Counted.Jammed);
        }

        [Test]
        public void TheRig_ReportsOneRowPerWave_LinedUpWithTheMatchedRoundRows()
        {
            // The two metrics are read side by side in the sheet, so they have to cut their rows at the same
            // moments: same waves, same outcomes.
            SimWorld world = Sim.Rig();
            Sim.BuildRig(world, out _);
            var stoppages = new StoppageTracker();
            var matches = new MatchRateTracker();
            var stopRows = new List<WaveStoppageRow>();
            var matchRows = new List<WaveMatchRow>();

            for (int t = 0; t < 6000 && world.Status == GameStatus.Playing; t++)
            {
                world.Tick(new InputCommand(startWavePressed: world.Waves.HasWaveToStart));
                if (stoppages.Poll(world, out WaveStoppageRow s)) stopRows.Add(s);
                if (matches.Poll(world, out WaveMatchRow m)) matchRows.Add(m);
            }

            if (stoppages.Poll(world, out WaveStoppageRow lastStop)) stopRows.Add(lastStop);
            if (matches.Poll(world, out WaveMatchRow lastMatch)) matchRows.Add(lastMatch);

            Assert.AreNotEqual(GameStatus.Playing, world.Status, "the run reached an end inside the budget");
            Assert.AreEqual(matchRows.Count, stopRows.Count, "one stoppage row for every matched-round row");

            int blocked = 0;
            for (int i = 0; i < stopRows.Count; i++)
            {
                Assert.AreEqual(matchRows[i].Wave, stopRows[i].Wave, "row " + (i + 1) + " is the same wave");
                Assert.AreEqual(matchRows[i].Outcome, stopRows[i].Outcome, "and closes the same way");
                Assert.AreEqual(world.Map.Name, stopRows[i].Map);
                blocked += stopRows[i].Blocked;
            }

            Assert.GreaterOrEqual(blocked, 1, "the rig's bottom half runs onto a dead end, and that is reported");
        }

        [Test]
        public void ARestartMidWave_ClosesWhatWasCountedAsAbandoned()
        {
            SimWorld world = Sim.Rig();
            Sim.BuildRig(world, out _);
            var tracker = new StoppageTracker();

            world.Tick(new InputCommand(startWavePressed: true));
            tracker.Poll(world, out _);
            Assert.AreEqual(1, tracker.OpenWave, "the first wave is being fought");

            TickAndPoll(world, tracker, 150);
            int counted = tracker.Pending;

            world.Restart();

            Assert.IsTrue(tracker.Poll(world, out WaveStoppageRow row), "the restart closes the open stretch");
            Assert.AreEqual(1, row.Wave);
            Assert.AreEqual("abandoned", row.Outcome);
            Assert.AreEqual(counted, row.Total, "with everything counted before it");
            Assert.AreEqual(0, tracker.OpenWave);
            Assert.AreEqual(0, tracker.Pending, "and the next run starts from nothing");
        }

        [Test]
        public void Polling_DoesNotChangeTheRun()
        {
            SimWorld watched = Sim.Rig();
            SimWorld alone = Sim.Rig();
            Sim.BuildRig(watched, out _);
            Sim.BuildRig(alone, out _);
            var tracker = new StoppageTracker();

            for (int t = 0; t < 3000; t++)
            {
                watched.Tick(new InputCommand(startWavePressed: watched.Waves.HasWaveToStart));
                alone.Tick(new InputCommand(startWavePressed: alone.Waves.HasWaveToStart));
                tracker.Poll(watched, out _);
            }

            Assert.AreEqual(alone.TickCount, watched.TickCount);
            Assert.AreEqual(alone.Status, watched.Status);
            Assert.AreEqual(alone.Core.Hp, watched.Core.Hp, 0f);
            Assert.AreEqual(alone.Economy.Shapes, watched.Economy.Shapes);
        }

        /// <summary>Tick and poll in step, the way the reporter polls once after each frame's ticks.</summary>
        private static void TickAndPoll(SimWorld world, StoppageTracker tracker, int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                world.Tick(InputCommand.None);
                tracker.Poll(world, out _);
            }
        }
    }
}
