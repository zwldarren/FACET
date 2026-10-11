using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the wave clock and the end of a run: the first wave waiting for the player
    /// instead of counting down, the countdown every later wave still runs, spawning, what counts as a
    /// cleared wave, whether a jam really is enough to lose, and what a restart puts back. Map 1 has
    /// exactly one wave, so clearing it is the win - the intermission after a clear needs a map with a
    /// second wave, so that one is built inside the test that pins it.
    /// </summary>
    public class WaveTests
    {
        [Test]
        public void FirstWave_NeverStartsItself_AndWaitsForThePlayer()
        {
            // The run opens in the player's hands. The first wave has no countdown at all, so no
            // amount of waiting sends it: only N, or the HUD's button, does. This is the whole of the
            // rule, and the reason the map's first intermission is 0.
            SimWorld world = Sim.NewWorld();

            Assert.AreEqual(0, world.Waves.CurrentWave, "nothing has been sent yet");
            Assert.AreEqual(1, world.Waves.NextWave);
            Assert.IsTrue(world.Waves.FirstWaveHeld, "the first wave is the player's to call");
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase, "and the clock says so in one word");
            Assert.IsTrue(world.Waves.HasWaveToStart, "the player can answer it");
            Assert.AreEqual(0f, world.Waves.IntermissionRemaining, Sim.Tol, "there is no countdown to run out");
            Assert.AreEqual(0f, world.Waves.IntermissionTotal, Sim.Tol, "not even a stopped one");

            // Five minutes, which is longer than any intermission the table could hold.
            Sim.Tick(world, (int)(5 * 60 * SimConfig.TickRate));

            Assert.AreEqual(0, world.Waves.CurrentWave, "a wave nobody started never starts");
            Assert.AreEqual(0, world.Enemies.AliveCount);
            Assert.IsTrue(world.Waves.FirstWaveHeld);
            Assert.IsFalse(world.Waves.Finished, "and waiting is not winning it either");
            Assert.AreEqual(GameStatus.Playing, world.Status, "nor losing it");

            world.Waves.StartNextWave();
            Assert.AreEqual(1, world.Waves.CurrentWave);
            Assert.AreEqual(WavePhase.Running, world.Waves.Phase, "the clock is on the wave now");
            Assert.IsFalse(world.Waves.HasWaveToStart, "and there is nothing left to start early");
            Assert.AreEqual(0f, world.Waves.IntermissionRemaining, Sim.Tol);

            // The group's own delay, generously rounded: the schedule is a float countdown, so it can
            // land a tick either side of the number in the table.
            int delay = (int)(world.Map.Waves[0].Groups[0].FirstDelay * SimConfig.TickRate);
            Sim.Tick(world, delay + 5);

            Assert.AreEqual(1, world.Enemies.AliveCount, "and then the first enemy walks in");
            Assert.Greater(world.Waves.PendingInWave, 0, "with the rest of the wave still to come");
        }

        [Test]
        public void EveryWaveAfterTheFirst_StillCountsDown_AndStartsItself()
        {
            // The line the rule draws: only a map's first wave waits for the player. The map is built
            // here because map 1 has one wave - and the first of these two holds no enemies at all, so
            // it clears on its first tick and the intermission under test is the one before wave 2.
            var map = new MapDefinition("two waves", 20, 12, 0,
                new ShapePatch[0],
                new[] { new Int2(1, 1) },
                new[]
                {
                    new WaveDefinition(0f),
                    new WaveDefinition(3f, new SpawnGroup(EnemyKind.Spike, 1, 0, 0f, 1f)),
                });

            var world = new SimWorld(map, new SimConfig());

            world.Waves.StartNextWave();
            world.Tick(InputCommand.None);

            Assert.AreEqual(0, world.Waves.CurrentWave, "the empty first wave is cleared by its own tick");
            Assert.AreEqual(2, world.Waves.NextWave);
            Assert.AreEqual(WavePhase.Intermission, world.Waves.Phase, "and the clock is counting down");
            Assert.IsFalse(world.Waves.FirstWaveHeld, "the countdown belongs to every wave but the first");
            Assert.AreEqual(3f, world.Waves.IntermissionRemaining, Sim.Tol, "and the second wave gets one");

            Sim.Tick(world, (int)(2 * SimConfig.TickRate));
            Assert.AreEqual(0, world.Waves.CurrentWave, "a countdown that has not run out has not started it");

            Sim.Tick(world, (int)(SimConfig.TickRate + 5));
            Assert.AreEqual(2, world.Waves.CurrentWave, "and then it starts itself, with nobody pressing anything");
            Assert.AreEqual(WavePhase.Running, world.Waves.Phase);
        }

        [Test]
        public void N_StartsTheNextWaveEarly_AndOnlyOnce()
        {
            // Through InputCommand, not around it: pressing N is the input source latching a one-shot
            // into the command, and the tick that consumes it is the only thing that can turn that into
            // a wave. Calling StartNextWave() directly tests the director and leaves the wiring
            // untested - and on map 1 this press is the only way the first wave ever arrives.
            SimWorld world = Sim.NewWorld();

            world.Tick(new InputCommand(startWavePressed: true));
            Assert.AreEqual(1, world.Waves.CurrentWave, "the press reaches the wave clock through the tick");
            Assert.AreEqual(0f, world.Waves.IntermissionRemaining, Sim.Tol);

            world.Tick(new InputCommand(startWavePressed: true));
            Assert.AreEqual(1, world.Waves.CurrentWave, "a second press cannot restart the wave it started");
        }

        [Test]
        public void TheHudButtonsRequest_StartsTheWave_AndIsDroppedWhilePaused()
        {
            // The HUD's button cannot press a key, so it asks the world for the same thing the N key
            // latches into a command. A request is one tick wide and is never queued: one that lands
            // while the world is stopped (paused, or already won) is thrown away, so clicking the
            // button and pausing in the same breath cannot start a wave the player never asked for.
            SimWorld world = Sim.NewWorld();

            world.Paused = true;
            world.RequestNextWave();
            world.Tick(InputCommand.None);
            Assert.AreEqual(0, world.Waves.CurrentWave, "a request while paused is dropped, not queued");

            world.Paused = false;
            world.RequestNextWave();
            world.Tick(InputCommand.None);

            Assert.AreEqual(1, world.Waves.CurrentWave, "and while running it starts the wave on that tick");
            Assert.AreEqual(0f, world.Waves.IntermissionRemaining, Sim.Tol);

            world.RequestNextWave();
            world.Tick(InputCommand.None);
            Assert.AreEqual(1, world.Waves.CurrentWave, "a second click cannot restart the wave it started");
        }

        [Test]
        public void N_OnTheTickTheWaveIsCleared_DoesNotReopenAFinishedRun()
        {
            // The last enemy dies in the enemies step of the tick whose wave step then clears the
            // wave, so a press arriving in that tick finds a wave still on the clock unless the
            // command is applied after the wave step. On a one-wave map the clear is also the win,
            // and the press must not reopen it. The clearing tick is found by running the identical
            // rig once: the same map and the same commands reproduce it exactly, which is the
            // property the replay test pins.
            int clearTick = ClearingTick();

            SimWorld world = DefendedAndStarted();
            Sim.Tick(world, clearTick);
            Assert.AreEqual(GameStatus.Playing, world.Status, "the wave is still on the clock until this tick");

            world.Tick(new InputCommand(startWavePressed: true));

            Assert.AreEqual(GameStatus.Won, world.Status, "the tick that clears the only wave wins the map");
            Assert.IsTrue(world.Waves.Finished);
            Assert.AreEqual(0, world.Waves.CurrentWave);
            Assert.AreEqual(WavePhase.Over, world.Waves.Phase, "the clock's own word for an exhausted table");
            Assert.IsFalse(world.Waves.HasWaveToStart, "and nothing is left to start");
        }

        /// <summary>The wave defence, primed, with the wave already running.</summary>
        private static SimWorld DefendedAndStarted()
        {
            SimWorld world = Sim.NewWorld();
            Sim.BuildMapOneDefence(world, out _, out _);
            Prime(world);
            world.Tick(new InputCommand(startWavePressed: true));
            return world;
        }

        /// <summary>Index of the tick whose wave step clears the wave for that rig.</summary>
        private static int ClearingTick()
        {
            SimWorld probe = DefendedAndStarted();
            for (int i = 0; i < 180 * SimConfig.TickRate; i++)
            {
                probe.Tick(InputCommand.None);
                if (probe.Waves.Phase != WavePhase.Running) return i;
            }

            Assert.Fail("the wave never cleared");
            return -1;
        }

        [Test]
        public void AnUndefendedCore_LosesTheRun_WhenTheWaveArrives()
        {
            SimWorld world = Sim.NewWorld();

            world.Waves.StartNextWave();
            for (int i = 0; i < 150 * SimConfig.TickRate && world.Status == GameStatus.Playing; i++)
                world.Tick(InputCommand.None);

            Assert.AreEqual(GameStatus.Lost, world.Status, "sixteen Spikes and nothing to shoot them with");
            Assert.AreEqual(0f, world.Core.Hp, Sim.Tol);
            Assert.IsFalse(world.Core.Alive);
        }

        [Test]
        public void OneFedCannon_HoldsTheWave_AndAJamInItsLineDoesNot()
        {
            // The wave, two ways. The defence is the same build both times - a drill on the east
            // patch, a decomposer mid-line, and a cannon covering the Core's southern corner, eating
            // what the line delivers. The only difference is one jammed segment in the input run.
            SimWorld held = Sim.NewWorld();
            Sim.BuildMapOneDefence(held, out _, out _);
            Prime(held);

            held.Waves.StartNextWave();
            RunWave(held);

            Assert.AreEqual(GameStatus.Won, held.Status, "a fed cannon clears the one wave");
            Assert.AreEqual(held.Config.CoreMaxHp, held.Core.Hp, Sim.Tol,
                "without the Core being touched");
            Assert.Greater(held.ShotsFired, 0);

            SimWorld jammed = Sim.NewWorld();
            Sim.BuildMapOneDefence(jammed, out Int2 trunk, out _);

            // Jammed before the line is primed: this is a line that never delivered anything, which is
            // what a wrong-shape delivery earlier in the run leaves behind.
            Assert.IsTrue(jammed.Belts.TryJam(trunk), "one wrong-shape delivery, and that is the whole run");
            jammed.Waves.StartNextWave();
            RunWave(jammed);

            Assert.AreEqual(GameStatus.Lost, jammed.Status, "a jam upstream means the Core pays for it");
            Assert.AreEqual(1, jammed.Belts.JamCount, "and the jam is still there to be found");
        }

        /// <summary>Fight the wave out, with a cap so a stuck wave fails the test instead of hanging it.</summary>
        private static void RunWave(SimWorld world)
        {
            for (int i = 0; i < 180 * SimConfig.TickRate && world.Waves.CurrentWave == 1; i++)
                world.Tick(InputCommand.None);
        }

        /// <summary>
        /// Let the line fill before calling the wave, which is what a player does with the time the
        /// held first wave gives them: a belt is 1 tile/s, so the 27-tile route from drill to cannon is
        /// 27 seconds of latency before the first shot is even possible.
        /// </summary>
        private static void Prime(SimWorld world) => Sim.Tick(world, (int)(35 * SimConfig.TickRate));

        [Test]
        public void Restart_PutsTheRunBackToTheStart()
        {
            SimWorld world = Sim.NewWorld();
            var patch = world.Map.Patches[0].Origin;

            Sim.Place(world, BuildKind.Drill, patch, Dir.East);
            Sim.LayRun(world, new Int2(17, 10), Dir.East, 2);
            world.Waves.StartNextWave();
            Sim.Tick(world, 120);
            world.Core.Hp = 1f;

            Assert.Greater(world.Enemies.AliveCount, 0, "a wave is in progress and on the map");
            Assert.Less(world.Economy.Circles, world.Map.StartCircles, "the build spent real circles");

            world.Tick(new InputCommand(restartPressed: true));

            Assert.AreEqual(GameStatus.Playing, world.Status);
            Assert.AreEqual(0, world.TickCount);
            Assert.AreEqual(world.Config.CoreMaxHp, world.Core.Hp, Sim.Tol, "the Core is whole again");
            Assert.AreEqual(world.Map.StartCircles, world.Economy.Circles, "the stockpile is reset too");
            Assert.AreEqual(0, world.Economy.TotalBanked);
            Assert.AreEqual(0, world.Enemies.AliveCount);
            Assert.AreEqual(0, world.Projectiles.Count);
            Assert.AreEqual(0, world.Belts.JamCount);
            Assert.IsFalse(world.Belts.Has(new Int2(17, 10)), "the line is gone");
            Assert.IsFalse(world.Machines.Has(patch));
            Assert.IsFalse(world.TileGrid.Get(patch).IsBuilding(), "nothing is standing on it again");
            Assert.IsTrue(world.Terrain.HasOre(patch), "and the patch is back in the ground");
            Assert.AreEqual(0, world.Waves.CurrentWave);
            Assert.AreEqual(1, world.Waves.NextWave, "the next wave is the first wave again");
            Assert.IsTrue(world.Waves.FirstWaveHeld, "which is held for the player once more");
            Assert.AreEqual(0f, world.Waves.IntermissionRemaining, Sim.Tol, "with no countdown left to run");
            Assert.IsNull(world.SelectedKind, "and nothing is selected again - the cursor reads the map");
        }

        [Test]
        public void FullRun_ReplaysIdentically_FromTheSameCommands()
        {
            // The whole point of an engine-free simulation: the same map and the same input produce
            // the same run, so a bug is reproducible from a save plus a command log. A hash-order or
            // float-order leak anywhere in the systems would show up here and nowhere else.
            SimWorld a = Sim.NewWorld();
            SimWorld b = Sim.NewWorld();
            Rig(a);
            Rig(b);

            for (int i = 0; i < 100 * SimConfig.TickRate; i++)
            {
                // The wave is started once the ammo line has had time to fill: a cannon that is still
                // waiting for its first half-circle would watch the Core fall without firing, and the
                // test would prove determinism about a run that never fought.
                var cmd = new InputCommand(cursorCell: new Int2(20, 20), startWavePressed: i == 40 * 30);
                a.Tick(cmd);
                b.Tick(cmd);
            }

            Assert.AreEqual(a.Status, b.Status);
            Assert.Greater(a.ShotsFired, 0, "the run has to actually fight for this to mean anything");
            Assert.AreEqual(a.TickCount, b.TickCount);
            Assert.AreEqual(a.Core.Hp, b.Core.Hp, 0f, "the two runs diverged");
            Assert.AreEqual(a.ShotsFired, b.ShotsFired);
            Assert.AreEqual(a.Belts.JamCount, b.Belts.JamCount);
            Assert.AreEqual(a.Economy.Circles, b.Economy.Circles);
            Assert.AreEqual(a.Enemies.AliveCount, b.Enemies.AliveCount);
            Assert.AreEqual(a.Waves.CurrentWave, b.Waves.CurrentWave);
            Assert.AreEqual(a.Projectiles.Count, b.Projectiles.Count);
        }

        /// <summary>A small fixed rig, so the replay test fights something instead of watching an
        /// empty map. The stockpile top-up is identical in both worlds, which is all determinism asks.</summary>
        private static void Rig(SimWorld world)
        {
            world.Economy.Bank(500);

            Sim.BuildMapOneDefence(world, out _, out _);
            Sim.BuildMapOneEconomyLine(world);
        }
    }
}
