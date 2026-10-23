using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The matched-round analytics metric: the share of turret hits whose round was the shape the target
    /// was weak to. Counted on impact by <see cref="EnemyField"/>, cut into one row per wave by
    /// <see cref="MatchRateTracker"/>, and sent from the WebGL build by the analytics reporter.
    /// </summary>
    public class MatchRateTests
    {
        [Test]
        public void AHit_IsCountedAsMatchedOrResisted_ByTheRoundItLandsWith()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            int id = world.Enemies.Spawn(EnemyKind.Tank, new Vec2(5.5f, 5.5f), Sim.TopHalfCircle);

            world.Enemies.ApplyDamage(id, 1f, Sim.TopHalfCircle);
            Assert.AreEqual(1, world.Enemies.MatchedHits, "the round the enemy is weak to is a matched hit");
            Assert.AreEqual(0, world.Enemies.ResistedHits);

            world.Enemies.ApplyDamage(id, 1f, Sim.Circle);
            Assert.AreEqual(1, world.Enemies.MatchedHits);
            Assert.AreEqual(1, world.Enemies.ResistedHits, "any other round is a resisted hit");

            world.Enemies.Clear();
            Assert.AreEqual(0, world.Enemies.MatchedHits, "a new run starts its counts from nothing");
            Assert.AreEqual(0, world.Enemies.ResistedHits);
        }

        [Test]
        public void TheRig_ReportsOneRowPerWave_WithItsOwnHits()
        {
            // Whether the rig's line holds its second wave is a balance question this test does not ask; what
            // it asks is that every wave the run reaches is reported once, closed the way it actually ended.
            SimWorld world = Sim.Rig();
            Sim.BuildRig(world, out _);
            var tracker = new MatchRateTracker();

            List<WaveMatchRow> rows = Play(world, tracker, 6000);

            Assert.AreNotEqual(GameStatus.Playing, world.Status, "the run reached an end inside the budget");
            Assert.GreaterOrEqual(rows.Count, 1, "a run that saw a wave reports it");

            for (int i = 0; i < rows.Count; i++)
            {
                Assert.AreEqual(i + 1, rows[i].Wave, "one row per wave, in order");
                Assert.AreEqual(world.Map.Name, rows[i].Map);

                bool last = i == rows.Count - 1;
                string expected = !last ? "cleared" : world.Status == GameStatus.Won ? "won" : "lost";
                Assert.AreEqual(expected, rows[i].Outcome, "row " + (i + 1) + " closes the way the wave ended");
            }

            Assert.AreEqual("cleared", rows[0].Outcome, "the rig's line holds its first wave");
            Assert.Greater(rows[0].Matched, 0,
                "the rig's cutter makes the top half its waves ask for, so its hits match");

            int reported = 0;
            foreach (WaveMatchRow row in rows) reported += row.Hits;
            Assert.AreEqual(world.Enemies.MatchedHits + world.Enemies.ResistedHits, reported,
                "every hit of the run lands in exactly one row");
        }

        [Test]
        public void TheWaveThatWinsTheMap_IsReportedAsWon()
        {
            // The sheet tells a finished attempt from one the player walked away from by its last row: a run
            // that ends in a win closes its final wave as "won", not "cleared".
            SimWorld world = Sim.OneWaveWorld();
            var tracker = new MatchRateTracker();
            var rows = new List<WaveMatchRow>();

            for (int t = 0; t < 900 && world.Status == GameStatus.Playing; t++)
            {
                Sim.FeedOneWaveGun(world);
                world.Tick(InputCommand.None);
                if (tracker.Poll(world, out WaveMatchRow row)) rows.Add(row);
            }

            Assert.AreEqual(GameStatus.Won, world.Status, "the one-wave rig wins");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("won", rows[0].Outcome);
            Assert.Greater(rows[0].Matched, 0, "fed the round its wave is weak to");
            Assert.AreEqual(0, rows[0].Resisted);
        }

        [Test]
        public void ARestartMidWave_ClosesThatWaveAsAbandoned()
        {
            SimWorld world = Sim.Rig();
            Sim.BuildRig(world, out _);
            var tracker = new MatchRateTracker();

            world.Tick(new InputCommand(startWavePressed: true));
            tracker.Poll(world, out _);
            Assert.AreEqual(1, tracker.OpenWave, "the first wave is being counted");

            world.Restart();

            Assert.IsTrue(tracker.Poll(world, out WaveMatchRow row), "the restart closes the open wave");
            Assert.AreEqual(1, row.Wave);
            Assert.AreEqual("abandoned", row.Outcome);
            Assert.AreEqual(0, tracker.OpenWave);
        }

        [Test]
        public void Polling_DoesNotChangeTheRun()
        {
            SimWorld watched = Sim.Rig();
            SimWorld alone = Sim.Rig();
            Sim.BuildRig(watched, out _);
            Sim.BuildRig(alone, out _);

            Play(watched, new MatchRateTracker(), 3000);
            Play(alone, null, 3000);

            Assert.AreEqual(alone.TickCount, watched.TickCount);
            Assert.AreEqual(alone.Status, watched.Status);
            Assert.AreEqual(alone.Core.Hp, watched.Core.Hp, 0f);
            Assert.AreEqual(alone.Economy.Shapes, watched.Economy.Shapes);
        }

        /// <summary>Press "start wave" every intermission and tick until the run ends or the budget runs out,
        /// polling after each tick the way the reporter polls after each frame.</summary>
        private static List<WaveMatchRow> Play(SimWorld world, MatchRateTracker tracker, int ticks)
        {
            var rows = new List<WaveMatchRow>();

            for (int t = 0; t < ticks && world.Status == GameStatus.Playing; t++)
            {
                bool between = world.Waves.CurrentWave == 0;
                world.Tick(new InputCommand(startWavePressed: between));

                if (tracker != null && tracker.Poll(world, out WaveMatchRow row)) rows.Add(row);
            }

            if (tracker != null && tracker.Poll(world, out WaveMatchRow last)) rows.Add(last);
            return rows;
        }
    }
}
