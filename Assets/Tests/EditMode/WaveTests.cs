using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the wave clock and the end of a run: the opening the player's first building
    /// arms, the countdown every wave then runs, spawning, what counts as a cleared wave, whether a jam
    /// really is enough to lose, and what a restart puts back. The tests that need a real map fight on
    /// <see cref="Sim.RigMap"/>, the tests' own: a corridor, two veins, one door, two waves. The ones
    /// that need a run to END on a clear - the win, the clock's last tick - build their own one-wave map.
    /// </summary>
    public class WaveTests
    {
        [Test]
        public void FirstWave_WaitsForTheFirstBuilding_ThenCountsDown()
        {
            // The opening's clock is the player's to start. Nothing ticks until they put something down -
            // so reading the map and planning the first line cost no time at all - and the countdown the
            // table declares runs from that building rather than from the run.
            SimWorld world = Sim.Rig();
            float countdown = world.Map.Waves[0].Intermission;

            Assert.AreEqual(0, world.Waves.CurrentWave, "nothing has been sent yet");
            Assert.AreEqual(1, world.Waves.NextWave);
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase, "the opening is held for the first building");
            Assert.IsTrue(world.Waves.HasWaveToStart, "and the player may answer it early");
            Assert.AreEqual(countdown, world.Waves.IntermissionRemaining, Sim.Tol,
                "the countdown the table declares is waiting there, not running");
            Assert.Greater(countdown, 0f, "a first wave with no countdown would open as soon as it is armed");

            // Five minutes of doing nothing, which is longer than the whole table could hold: the clock has
            // not moved a tick. That is the whole point of the arming rule - the time is the player's.
            Sim.Tick(world, (int)(5 * 60 * SimConfig.TickRate));

            Assert.AreEqual(0, world.Waves.CurrentWave, "a run nobody has built in never starts a wave");
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase);
            Assert.AreEqual(0, world.Enemies.AliveCount);
            Assert.AreEqual(GameStatus.Playing, world.Status, "waiting to build is neither winning nor losing");

            // The first building arms it. A belt counts: a belt is something the player built, and waiting
            // for a machine would leave the clock stopped while they laid the line that machine feeds.
            Assert.IsTrue(world.TryPlaceBelt(new Int2(11, 12), Dir.East), "a belt to arm the clock with");
            world.Tick(InputCommand.None);

            Assert.AreEqual(WavePhase.Intermission, world.Waves.Phase, "and now the clock is on the first wave");
            Assert.AreEqual(countdown - SimConfig.TickDt, world.Waves.IntermissionRemaining, Sim.Tol,
                "counting from the tick the building landed on");

            // One tick short of the whole countdown from that building: still waiting. Rounded up the
            // way WaveDirector.SecondsToTicks does, so a fractional table value cannot leave this
            // budget short by a tick the follow-up does not cover.
            Sim.Tick(world, (int)System.MathF.Ceiling(countdown * SimConfig.TickRate) - 2);
            Assert.AreEqual(0, world.Waves.CurrentWave, "a countdown that has not run out has not started it");

            Sim.Tick(world, 1);
            Assert.AreEqual(1, world.Waves.CurrentWave, "and then the opening opens on its own");
            Assert.AreEqual(WavePhase.Running, world.Waves.Phase, "the clock is on the wave now");
            Assert.IsFalse(world.Waves.Finished, "with the whole table still ahead of it");

            // The group's own delay, generously rounded: the schedule is a float countdown, so it can
            // land a tick either side of the number in the table.
            int delay = (int)(world.Map.Waves[0].Groups[0].FirstDelay * SimConfig.TickRate);
            Sim.Tick(world, delay + 5);

            Assert.AreEqual(1, world.Enemies.AliveCount, "and then the first enemy walks in");
            Assert.Greater(world.Waves.PendingInWave, 0, "with the rest of the wave still to come");
        }

        [Test]
        public void FirstWave_CanBeCalledEarly_BeforeAnyBuildingArmsIt()
        {
            // The other lever, unchanged: N starts the wave now, and on a Held opening that means before a
            // single building exists. Nothing forces a player to wait for the clock they started.
            SimWorld world = Sim.Rig();

            world.Waves.StartNextWave();
            Assert.AreEqual(WavePhase.Running, world.Waves.Phase, "the wait ends whether or not it was armed");
            Assert.AreEqual(1, world.Waves.CurrentWave, "the first wave is the one that opens");
            Assert.IsFalse(world.Waves.HasWaveToStart, "and there is nothing left to start early");
            Assert.AreEqual(0f, world.Waves.IntermissionRemaining, Sim.Tol);
            Assert.AreEqual(0f, world.Waves.IntermissionTotal, Sim.Tol);

            // The clock never comes back for a later wave's wait: the opening is spent, not re-armed.
            Sim.Tick(world, 60);
            Assert.AreEqual(1, world.Waves.CurrentWave, "the wave runs whether or not its clock ever ran");
        }

        [Test]
        public void EveryWave_CountsDown_AndStartsItself_TheFirstIncluded()
        {
            // The line the arming rule draws: it only holds the opening. The map is built here because the
            // rig's opening has its own test above, and this one's first countdown is 0 - the only wait
            // that can open on the very tick it is armed. The intermission under test is the one before
            // wave 2, which is the table's 3.
            var map = new MapDefinition("two waves", 20, 12, Sim.TestCircles,
                new ShapePatch[0],
                new[] { new Int2(1, 1) },
                new[]
                {
                    new WaveDefinition(0f),
                    new WaveDefinition(3f, new SpawnGroup(EnemyKind.Spike, 1, 0, 0f, 1f)),
                });

            var world = new SimWorld(map, new SimConfig());

            Assert.AreEqual(1, world.Waves.NextWave, "the first wave is waiting on its own countdown");
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase, "held for the first building, like any opening");

            // One belt arms it, and this table gave the opening 0 seconds: the wave opens on that same tick.
            Sim.LayRun(world, new Int2(5, 5), Dir.East, 1);
            world.Tick(InputCommand.None);
            Assert.AreEqual(1, world.Waves.CurrentWave, "a 0-second countdown opens its wave as soon as it is armed");

            world.Tick(InputCommand.None);
            Assert.AreEqual(0, world.Waves.CurrentWave, "the empty first wave is cleared by its own tick");
            Assert.AreEqual(2, world.Waves.NextWave);
            Assert.AreEqual(WavePhase.Intermission, world.Waves.Phase, "and the clock is counting down");
            Assert.AreEqual(3f, world.Waves.IntermissionRemaining, Sim.Tol, "for the second wave's three");

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
            // untested. On the rig the press cuts the opening's sixty seconds down to nothing.
            SimWorld world = Sim.Rig();

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
            SimWorld world = Sim.Rig();

            Assert.AreEqual(WavePhase.Held, world.Waves.Phase, "the opening waits for a building or a request");

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
            // and the press must not reopen it. The map is built here - the claim is about the clock's
            // last tick, not about how many waves a map ships - and the clearing tick is found by
            // running the identical rig once: the same map and the same commands reproduce it exactly,
            // which is the property the replay test pins.
            int clearTick = ClearingTick();

            SimWorld world = DefendedAndStarted();
            for (int i = 0; i < clearTick; i++)
            {
                Sim.FeedOneWaveGun(world);
                world.Tick(InputCommand.None);
            }

            Assert.AreEqual(GameStatus.Playing, world.Status, "the wave is still on the clock until this tick");

            world.Tick(new InputCommand(startWavePressed: true));

            Assert.AreEqual(GameStatus.Won, world.Status, "the tick that clears the only wave wins the map");
            Assert.IsTrue(world.Waves.Finished);
            Assert.AreEqual(0, world.Waves.CurrentWave);
            Assert.AreEqual(WavePhase.Over, world.Waves.Phase, "the clock's own word for an exhausted table");
            Assert.IsFalse(world.Waves.HasWaveToStart, "and nothing is left to start");
        }

        /// <summary>A one-wave map with a gun beside its one lane, fed by hand every tick: the wave
        /// walks in, the gun kills it, and the clear is the win. The minimal rig that can actually end.
        /// The rig ships a 0-second opening and builds its gun before any tick runs, so the clock is
        /// armed and spent on the very first tick - which is the tick this helper ticks, press or no
        /// press; the press rides the same tick, and the determinism of that tick is what the tests
        /// here are about.</summary>
        private static SimWorld DefendedAndStarted()
        {
            SimWorld world = Sim.OneWaveWorld();
            world.Tick(new InputCommand(startWavePressed: true));
            return world;
        }

        /// <summary>Index of the tick whose wave step clears the wave for that rig.</summary>
        private static int ClearingTick()
        {
            SimWorld probe = DefendedAndStarted();
            for (int i = 0; i < 60 * SimConfig.TickRate; i++)
            {
                Sim.FeedOneWaveGun(probe);
                probe.Tick(InputCommand.None);
                if (probe.Waves.Phase != WavePhase.Running) return i;
            }

            Assert.Fail("the wave never cleared");
            return -1;
        }

        [Test]
        public void AnUndefendedCore_LosesTheRun_WhenTheWaveArrives()
        {
            SimWorld world = Sim.Rig();

            world.Waves.StartNextWave();
            for (int i = 0; i < 150 * SimConfig.TickRate && world.Status == GameStatus.Playing; i++)
                world.Tick(InputCommand.None);

            Assert.AreEqual(GameStatus.Lost, world.Status, "two Spikes and nothing to shoot them with");
            Assert.AreEqual(0f, world.Core.Hp, Sim.Tol);
            Assert.IsFalse(world.Core.Alive);
        }

        [Test]
        public void OneFedCannon_HoldsTheWave_AndAJamInItsLineDoesNot()
        {
            // The wave, two ways. The defence is the same build both times - a drill on the rig's second
            // vein, a decomposer mid-line, and a cannon covering the lane into the Core's north face,
            // eating what the line delivers. The only difference is one jammed segment in the input run.
            // The claim is wave 1's: a fed cannon holds it and hands the clock to the intermission; a
            // jammed line means the Core pays for it.
            SimWorld held = Sim.Rig();
            Sim.BuildRigDefence(held, out _);
            Prime(held);

            held.Waves.StartNextWave();
            RunWave(held);

            Assert.AreEqual(WavePhase.Intermission, held.Waves.Phase,
                "a fed cannon clears the wave, and the clock waits on the next one");
            Assert.AreEqual(held.Config.CoreMaxHp, held.Core.Hp, Sim.Tol,
                "without the Core being touched");
            Assert.Greater(held.ShotsFired, 0);

            SimWorld jammed = Sim.Rig();
            Sim.BuildRigDefence(jammed, out Int2 trunk);

            // Jammed before the line is primed: this is a line that never delivered anything, which is
            // what a wrong-shape delivery earlier in the run leaves behind.
            Assert.IsTrue(jammed.Belts.TryJam(trunk), "one wrong-shape delivery, and that is the whole run");
            jammed.Waves.StartNextWave();
            for (int i = 0; i < 180 * SimConfig.TickRate && jammed.Status == GameStatus.Playing; i++)
                jammed.Tick(InputCommand.None);

            Assert.AreEqual(GameStatus.Lost, jammed.Status, "a jam upstream means the Core pays for it");
            Assert.AreEqual(1, jammed.Belts.JamCount, "and the jam is still there to be found");
        }

        /// <summary>Fight the first wave out, with a cap so a stuck wave fails the test instead of hanging it.</summary>
        private static void RunWave(SimWorld world)
        {
            for (int i = 0; i < 180 * SimConfig.TickRate && world.Waves.Phase == WavePhase.Running; i++)
                world.Tick(InputCommand.None);
        }

        /// <summary>
        /// Let the line fill before calling the wave, which is what a player does with the time the
        /// opening countdown gives them: a belt is 1 tile/s, so the drill &gt; decomposer &gt; gun route,
        /// plus the decomposer's 1.5 s split, is a handful of seconds of latency before the first shot is
        /// even possible - and the rig's opening allows sixty of them.
        /// </summary>
        private static void Prime(SimWorld world) => Sim.Tick(world, (int)(35 * SimConfig.TickRate));

        [Test]
        public void Restart_PutsTheRunBackToTheStart()
        {
            SimWorld world = Sim.Rig();
            var patch = world.Map.Patches[0].Origin;

            Sim.BuildRig(world, out _);
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
            Assert.IsFalse(world.Belts.Has(new Int2(8, 6)), "the line is gone");
            Assert.IsFalse(world.Machines.Has(patch));
            Assert.IsFalse(world.TileGrid.Get(patch).IsBuilding(), "nothing is standing on it again");
            Assert.IsTrue(world.Terrain.HasOre(patch), "and the patch is back in the ground");
            Assert.AreEqual(0, world.Waves.CurrentWave);
            Assert.AreEqual(1, world.Waves.NextWave, "the next wave is the first wave again");
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase, "and its opening is the player's to arm once more");
            Assert.AreEqual(world.Map.Waves[0].Intermission, world.Waves.IntermissionRemaining, Sim.Tol,
                "with the whole countdown waiting on the first building");
            Assert.IsNull(world.SelectedKind, "and nothing is selected again - the cursor reads the map");
        }

        [Test]
        public void FullRun_ReplaysIdentically_FromTheSameCommands()
        {
            // The whole point of an engine-free simulation: the same map and the same input produce
            // the same run, so a bug is reproducible from a save plus a command log. A hash-order or
            // float-order leak anywhere in the systems would show up here and nowhere else.
            SimWorld a = Sim.Rig();
            SimWorld b = Sim.Rig();
            Rig(a);
            Rig(b);

            for (int i = 0; i < 100 * SimConfig.TickRate; i++)
            {
                // The wave is started once the ammo line has had time to fill: a cannon that is still
                // waiting for its first half-circle would watch the Core fall without firing, and the
                // test would prove determinism about a run that never fought.
                var cmd = new InputCommand(cursorCell: new Int2(10, 15), startWavePressed: i == 40 * SimConfig.TickRate);
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

        /// <summary>The same fixed build in both worlds, so the replay test fights something instead of
        /// watching an empty map - the rig's own opening, paid for out of the rig's own budget.</summary>
        private static void Rig(SimWorld world) => Sim.BuildRig(world, out _);
    }
}
