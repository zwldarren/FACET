using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the wave clock and the end of a run: the opening the first building arms, the
    /// countdown every wave runs, spawning, clearing, jams that lose the run, and restart. Tests needing a
    /// real map fight on <see cref="Sim.RigMap"/>; ones needing a run to END build their own one-wave map.
    /// </summary>
    public class WaveTests
    {
        [Test]
        public void FirstWave_WaitsForTheFirstBuilding_ThenCountsDown()
        {
            // The opening's clock is the player's to start: nothing ticks until they put something down, so
            // reading the map costs no time, and the table's countdown runs from that building, not the run.
            SimWorld world = Sim.Rig();
            float countdown = world.Map.Waves[0].Intermission;

            Assert.AreEqual(0, world.Waves.CurrentWave, "nothing has been sent yet");
            Assert.AreEqual(1, world.Waves.NextWave);
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase, "the opening is held for the first building");
            Assert.IsTrue(world.Waves.HasWaveToStart, "and the player may answer it early");
            Assert.AreEqual(countdown, world.Waves.IntermissionRemaining, Sim.Tol,
                "the countdown the table declares is waiting there, not running");
            Assert.Greater(countdown, 0f, "a first wave with no countdown would open as soon as it is armed");

            // Five minutes of doing nothing, longer than the whole table could hold: the clock has not moved
            // a tick. That is the arming rule's point - the time is the player's.
            Sim.Tick(world, (int)(5 * 60 * SimConfig.TickRate));

            Assert.AreEqual(0, world.Waves.CurrentWave, "a run nobody has built in never starts a wave");
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase);
            Assert.AreEqual(0, world.Enemies.AliveCount);
            Assert.AreEqual(GameStatus.Playing, world.Status, "waiting to build is neither winning nor losing");

            // The first building arms it. A belt counts, since waiting for a machine would leave the clock
            // stopped while they laid the line that machine feeds.
            Assert.IsTrue(world.TryPlaceBelt(new Int2(11, 12), Dir.East), "a belt to arm the clock with");
            world.Tick(InputCommand.None);

            Assert.AreEqual(WavePhase.Intermission, world.Waves.Phase, "and now the clock is on the first wave");
            Assert.AreEqual(countdown - SimConfig.TickDt, world.Waves.IntermissionRemaining, Sim.Tol,
                "counting from the tick the building landed on");

            // One tick short of the whole countdown: still waiting. Rounded up the way WaveDirector.SecondsToTicks
            // does, so a fractional table value cannot leave this budget short by a tick the follow-up misses.
            Sim.Tick(world, (int)System.MathF.Ceiling(countdown * SimConfig.TickRate) - 2);
            Assert.AreEqual(0, world.Waves.CurrentWave, "a countdown that has not run out has not started it");

            Sim.Tick(world, 1);
            Assert.AreEqual(1, world.Waves.CurrentWave, "and then the opening opens on its own");
            Assert.AreEqual(WavePhase.Running, world.Waves.Phase, "the clock is on the wave now");
            Assert.IsFalse(world.Waves.Finished, "with the whole table still ahead of it");

            // The group's own delay, generously rounded: the schedule is a float countdown, so it can land
            // a tick either side of the table's number.
            int delay = (int)(world.Map.Waves[0].Groups[0].FirstDelay * SimConfig.TickRate);
            Sim.Tick(world, delay + 5);

            Assert.AreEqual(1, world.Enemies.AliveCount, "and then the first enemy walks in");
            Assert.Greater(world.Waves.PendingInWave, 0, "with the rest of the wave still to come");
        }

        [Test]
        public void FirstWave_CanBeCalledEarly_BeforeAnyBuildingArmsIt()
        {
            // The other lever: N starts the wave now, and on a Held opening that means before a single
            // building exists - nothing forces a player to wait for the clock they started.
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
            // The line the arming rule draws: it only holds the opening. This map's first countdown is 0 (the
            // only wait that opens on the tick it is armed) and the intermission under test is wave 2's, the
            // table's 3; the rig's opening has its own test above.
            var map = new MapDefinition("two waves", 20, 12, Sim.TestCircles,
                new ShapePatch[0],
                new[] { new Int2(1, 1) },
                new[]
                {
                    new WaveDefinition(0f),
                    new WaveDefinition(3f, new SpawnGroup(EnemyKind.Runner, 1, 0, 0f, 1f, Sim.Circle)),
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
            // Through InputCommand, not around it: pressing N latches a one-shot into the command, and the tick
            // that consumes it is what turns that into a wave - calling StartNextWave() directly would leave the
            // wiring untested. On the rig the press cuts the opening's sixty seconds to nothing.
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
            // The HUD's button cannot press a key, so it asks the world for the same thing N latches into a
            // command. A request is one tick wide and never queued: one landing while the world is stopped
            // (paused, or already won) is thrown away, so it cannot start a wave the player never asked for.
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
            // The last enemy dies in the enemies step of the tick whose wave step then clears the wave, so a
            // press arriving in that tick finds a wave still on the clock unless the command is applied after the
            // wave step. On a one-wave map the clear is also the win, and the press must not reopen it. The
            // clearing tick is found by running the identical rig once - the same map and commands reproduce it.
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

        /// <summary>A one-wave map with a gun beside its lane, fed by hand each tick: the wave walks in, the
        /// gun kills it, the clear is the win. Its 0-second opening and pre-built gun arm and spend the clock on
        /// the first tick - which this helper ticks, press or no press - and whose determinism these tests pin.</summary>
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
        public void OneFedTurret_HoldsTheWave_AndAJamInItsLineDoesNot()
        {
            // The wave, two ways: the same defence both times (a drill on the rig's second vein, a cutter
            // mid-line, and a turret covering the lane into the Core's north face), differing only in one jammed
            // segment. A fed turret holds the wave and hands the clock to the intermission; a jammed line loses.
            SimWorld held = Sim.Rig();
            Sim.BuildRigDefence(held, out _);
            Prime(held);

            held.Waves.StartNextWave();
            RunWave(held);

            Assert.AreEqual(WavePhase.Intermission, held.Waves.Phase,
                "a fed turret clears the wave, and the clock waits on the next one");
            Assert.AreEqual(held.Config.CoreMaxHp, held.Core.Hp, Sim.Tol,
                "without the Core being touched");
            Assert.Greater(held.ShotsFired, 0);

            SimWorld jammed = Sim.Rig();
            Sim.BuildRigDefence(jammed, out Int2 trunk);

            // Jammed before the line is primed: a line that never delivered anything, what a wrong-shape
            // delivery earlier in the run leaves behind.
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
        /// Let the line fill before calling the wave, as a player would with the opening countdown's time: the
        /// drill &gt; cutter &gt; gun route plus the cutter's 1.5 s cut takes a few seconds before the
        /// first shot is possible, and the rig's opening allows sixty.
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
        public void AConfiguredSeedSource_DrawsAFreshSeed_PerRun()
        {
            // §5: one seed per run, shown in the HUD. The map's authored seed is the default - it is what
            // the engine-free tests and a demo that must replay exactly run on - but a host that injects
            // a source gets a run whose seed is its own, and a restart is a new run: it draws again.
            var map = new MapDefinition("seeded", 24, 16, Sim.TestCircles,
                new[] { new ShapePatch(new Int2(2, 2), 1, 1, Quad.Circle) },
                new[] { new Int2(12, 14) },
                new WaveDefinition[0],
                coreCell: new Int2(10, 6),
                unlocked: Operators.All,
                seed: 11,
                generatedWaves: 3);

            int draws = 0;
            var world = new SimWorld(map, new SimConfig { CoreMaxHp = 1000000f, FreshSeed = () => ++draws * 101 });

            Assert.AreEqual(101, world.Waves.Seed, "the source drew the run's seed, not the map's 11");

            world.Tick(new InputCommand(restartPressed: true));

            Assert.AreEqual(202, world.Waves.Seed, "a restarted run is a new run, on a newly drawn seed");
        }

        [Test]
        public void AnEndlessMap_NeverFinishes_AndHasNoTotalToPrint()
        {
            // Endless mode is not a second mode: it is the generator with no bound (§5), so the clock's
            // answer to "how many waves are there" is zero and the HUD prints the wave number alone. What
            // must never happen is the run reporting itself finished - there is no last wave to clear.
            var map = new MapDefinition("endless", 24, 16, Sim.TestCircles,
                new[] { new ShapePatch(new Int2(2, 2), 1, 1, Quad.Circle) },
                new[] { new Int2(12, 14) },
                new WaveDefinition[0],
                coreCell: new Int2(10, 6),
                unlocked: Operators.All,
                seed: 4242,
                generatedWaves: -1);

            var world = new SimWorld(map, new SimConfig { CoreMaxHp = 1000000f });

            Assert.IsTrue(world.Waves.Endless);
            Assert.AreEqual(0, world.Waves.TotalWaves, "an endless run has no total");
            Assert.AreEqual(0, world.Waves.AuthoredCount, "and nothing scripted to play first");
            Assert.AreEqual(4242, world.Waves.Seed, "the run's seed is the map's, for the HUD to print");
            Assert.IsNotNull(world.Waves.Definition(1), "the generator answers from wave one");
            Assert.IsNotNull(world.Waves.Definition(200), "and keeps answering: there is no last one");
            Assert.IsNotNull(world.Waves.Definition(1), "and asking twice is the same answer");

            world.Waves.StartNextWave();
            Sim.Tick(world, (int)(60 * SimConfig.TickRate));

            Assert.GreaterOrEqual(world.Waves.CurrentWave, 1, "a wave is on the clock");
            Assert.IsFalse(world.Waves.Finished, "a minute in, an endless run has not run out of waves");
            Assert.AreNotEqual(WavePhase.Over, world.Waves.Phase, "and the clock is not over");
            Assert.AreEqual(GameStatus.Playing, world.Status, "with the run still to be won or lost");

            // The control: the same questions asked of a bounded map, where the total is a number and the
            // clock can reach the end of it. (That it *does* end is the win test's business, above.)
            SimWorld bounded = Sim.OneWaveWorld();
            Assert.IsFalse(bounded.Waves.Endless);
            Assert.AreEqual(1, bounded.Waves.TotalWaves);
            Assert.AreEqual(1, bounded.Waves.AuthoredCount);
        }

        [Test]
        public void FullRun_ReplaysIdentically_FromTheSameCommands()
        {
            // The point of an engine-free simulation: the same map and input produce the same run, so a bug is
            // reproducible from a save plus a command log. A hash- or float-order leak would show up only here.
            SimWorld a = Sim.Rig();
            SimWorld b = Sim.Rig();
            Rig(a);
            Rig(b);

            for (int i = 0; i < 100 * SimConfig.TickRate; i++)
            {
                // The wave starts once the line has filled: a turret still waiting for its first half-circle
                // would watch the Core fall without firing, proving determinism about a run that never fought.
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

        /// <summary>The same fixed build in both worlds, so the replay test fights something instead of watching
        /// an empty map - the rig's own opening, paid for out of the rig's own budget.</summary>
        private static void Rig(SimWorld world) => Sim.BuildRig(world, out _);
    }
}
