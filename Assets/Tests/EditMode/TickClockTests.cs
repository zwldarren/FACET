using Facet.Core;
using Facet.Game;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>The speed gear (issue #4): the gear changes how many ticks a frame runs, never what a tick
    /// does, so a run is the same tick sequence at 1x and 3x.</summary>
    public class TickClockTests
    {
        private const float Frame60 = 1f / 60f;

        [Test]
        public void Gears_StartAt1x_StepUpAndDown_AndClampAtTheEnds()
        {
            var clock = new TickClock();
            Assert.AreEqual(1, clock.Gear);
            Assert.AreEqual("1x", clock.Label);

            clock.Down();
            Assert.AreEqual(1, clock.Gear, "1x is the floor");

            clock.Up();
            clock.Up();
            Assert.AreEqual(3, clock.Gear);
            clock.Up();
            Assert.AreEqual(3, clock.Gear, "3x is the ceiling");

            clock.Down();
            Assert.AreEqual(2, clock.Gear);
        }

        [Test]
        public void Cycle_WrapsFromTheTopGearBackTo1x()
        {
            var clock = new TickClock();
            clock.Cycle();
            clock.Cycle();
            Assert.AreEqual(3, clock.Gear);
            clock.Cycle();
            Assert.AreEqual(1, clock.Gear);
        }

        [Test]
        public void OneSecondOfFrames_RunsThreeTimesTheTicks_At3x()
        {
            int slow = RunFrames(new TickClock(), 60);

            var fast = new TickClock();
            fast.Up();
            fast.Up();
            int quick = RunFrames(fast, 60);

            Assert.AreEqual(30, slow, 1, "1x is 30 ticks a second");
            Assert.AreEqual(90, quick, 1, "3x is 90 ticks a second");
        }

        [Test]
        public void ALongHitch_IsDropped_AtEveryGear_NotRepaid()
        {
            var clock = new TickClock();
            clock.Up();
            clock.Up();

            int ticks = clock.Advance(10f, SimConfig.TickDt);

            Assert.AreEqual(TickClock.BaseMaxTicksPerFrame * 3, ticks, "the cap scales with the gear");
            Assert.AreEqual(0f, clock.Accumulator, "the rest of the backlog is dropped");
        }

        [Test]
        public void ASlowFrameRate_IsReportedAsFallingBehind()
        {
            var clock = new TickClock();
            clock.Up();
            clock.Up();

            // 2 frames a second: each wants 45 ticks at 3x, the cap allows 15.
            for (int i = 0; i < 4; i++) clock.Advance(0.5f, SimConfig.TickDt);

            Assert.That(clock.EffectiveSpeed, Is.LessThan(3f));
            Assert.IsTrue(clock.FallingBehind);
        }

        [Test]
        public void ResetBacklog_KeepsTheGear()
        {
            var clock = new TickClock();
            clock.Up();
            clock.Advance(0.02f, SimConfig.TickDt);

            clock.ResetBacklog();

            Assert.AreEqual(0f, clock.Accumulator);
            Assert.AreEqual(2, clock.Gear, "a map change or restart keeps the player's gear");
        }

        [Test]
        public void TheSameRun_At1xAnd3x_EndsInTheSameState()
        {
            SimWorld slow = PlayRig(gearUps: 0);
            SimWorld fast = PlayRig(gearUps: 2);

            Assert.AreEqual(slow.TickCount, fast.TickCount);
            Assert.AreEqual(slow.Status, fast.Status);
            Assert.AreEqual(slow.Core.Hp, fast.Core.Hp, 0f);
            Assert.AreEqual(slow.Economy.Shapes, fast.Economy.Shapes);
            Assert.AreEqual(slow.Economy.TotalBanked, fast.Economy.TotalBanked);
            Assert.AreEqual(slow.Enemies.AliveCount, fast.Enemies.AliveCount);
            Assert.AreEqual(slow.Waves.CurrentWave, fast.Waves.CurrentWave);
        }

        private static int RunFrames(TickClock clock, int frames)
        {
            int total = 0;
            for (int i = 0; i < frames; i++) total += clock.Advance(Frame60, SimConfig.TickDt);
            return total;
        }

        /// <summary>The rig, built and then played through both of its waves the way the driver plays it:
        /// a clock decides how many ticks each 60 fps frame runs. A start-wave press lands on a fixed tick,
        /// not a fixed frame, so both gears see the same command stream.</summary>
        private static SimWorld PlayRig(int gearUps)
        {
            SimWorld world = Sim.Rig();
            Sim.BuildRig(world, out _);

            var clock = new TickClock();
            for (int i = 0; i < gearUps; i++) clock.Up();

            int startTick = world.TickCount;
            const int RunTicks = 4000;
            int pressAt = startTick + 300;

            while (world.TickCount < startTick + RunTicks && world.Status == GameStatus.Playing)
            {
                int ticks = clock.Advance(Frame60, SimConfig.TickDt);
                for (int t = 0; t < ticks && world.TickCount < startTick + RunTicks; t++)
                {
                    InputCommand cmd = world.TickCount == pressAt ? new InputCommand(startWavePressed: true) : InputCommand.None;
                    world.Tick(cmd);
                }
            }
            return world;
        }
    }
}
