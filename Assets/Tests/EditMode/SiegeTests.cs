using System;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the siege rules: machines are solid and destructible, enemies follow the
    /// flow field around them and stop to chew any attackable machine within reach <em>and in plain
    /// view</em>, a sealed Core is chewed through at the cheapest hole the player left, destruction pays
    /// no refund, and a MaxHp 0 definition is untargetable and indestructible. Worlds are the standard
    /// rigs: <see cref="Sim.NewWorld(int,int)"/> gives the Core at (8,4)..(11,7) on a featureless map, so
    /// every layout here is this file's own - bar <see cref="SealedMap"/>, the Core of that same rig under
    /// a rock ridge with two one-tile gaps, which is the world the breach reads are made in.
    /// </summary>
    public class SiegeTests
    {
        /// <summary>The rig's two ways through the ridge, and the rock a walker with no way to walk presses
        /// at - the Core's own column, with both ways in far enough off it to be out of a walker's reach.</summary>
        private static readonly Int2 WestGap = new Int2(6, 9);
        private static readonly Int2 EastGap = new Int2(14, 9);
        private static readonly Int2 RidgeRock = new Int2(10, 9);

        /// <summary>
        /// A rock ridge across y=9 that only the one-tile gaps at x=6 and x=14 pierce, over the Core
        /// <see cref="Sim.NewWorld(int,int)"/> gives - (8,4)..(11,7) - with open ground all the way up to
        /// y=23 to walk down. Plug both gaps and the whole north half is sealed in, with fourteen tiles of
        /// rock between the walker's column and either way in: exactly what a wave with no route used to walk
        /// into and stay, pressing at the ridge with nothing to bite.
        /// </summary>
        private static MapDefinition SealedMap()
            => new MapDefinition("sealed", 20, 24, Sim.TestCircles, new ShapePatch[0],
                new Int2[0], new WaveDefinition[0],
                TerrainLayout.Rects(20, 24,
                    new TerrainRect(new Int2(0, 9), 6, 1, TerrainKind.Rock),
                    new TerrainRect(new Int2(7, 9), 7, 1, TerrainKind.Rock),
                    new TerrainRect(new Int2(15, 9), 5, 1, TerrainKind.Rock)),
                coreCell: new Int2(8, 4));

        /// <summary>The shipped wall with MaxHp 0: solid but not attackable - the definition-level
        /// opt-out (<see cref="MachineDef.MaxHp"/>). Handed to a world through the custom-table helper
        /// <see cref="CustomContentTests"/> uses, so the test proves the rule and not a private code
        /// path.</summary>
        private static MachineDef UnattackableWall()
        {
            MachineDef wall = ContentDatabase.Default.Machine(BuildKind.Wall);
            return new MachineDef(wall.Id, wall.Build, wall.Tile, wall.Behavior, wall.Cost, wall.Name,
                wall.Description, wall.RecipeId, wall.Interval, wall.Filter, maxHp: 0f);
        }

        [Test]
        public void ASealedWave_WalksToTheBreach_InsteadOfPressingAtRock()
        {
            // The bug this file's breach rule exists for: the Core is sealed behind a rock ridge with a
            // walker's own column stopped by rock, so the plain field has no direction anywhere above the
            // ridge. Walking straight at the Core - which is all a wave used to do there - presses it into
            // the rock and leaves it there for the rest of the run. The breach field instead prices the two
            // plugged gaps and walks the wave to one of them.
            SimWorld world = new SimWorld(SealedMap(), new SimConfig { CoreMaxHp = 1000000f });
            Sim.Place(world, BuildKind.Wall, WestGap, Dir.East);
            Sim.Place(world, BuildKind.Wall, EastGap, Dir.East);
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10.5f, 20.5f));

            Vec2 last = world.Enemies.EnemyAt(0).Position;
            int still = 0, worstStill = 0;
            for (int i = 0; i < 30 * 120 && world.Events.CountOf(SimEventKind.CoreDamaged) == 0; i++)
            {
                world.Tick(InputCommand.None);
                EnemyState e = world.Enemies.EnemyAt(0);

                // A walker with nothing left to do stands still; a walker on its way somewhere does not.
                // Ten seconds of the one is the whole bug, so a single second of it is the failure line.
                still = e.Attacking || Vec2.Distance(e.Position, last) > 1e-4f ? 0 : still + 1;
                worstStill = Math.Max(worstStill, still);
                last = e.Position;

                Assert.AreNotEqual(RidgeRock, world.TileGrid.CellAt(e.Position),
                    "the rock is solid, however sealed in the wave is");
            }

            Assert.Less(worstStill, 30, "a sealed-in wave stood still for " + worstStill / 30f + "s");
            Assert.Greater(world.Events.CountOf(SimEventKind.BuildingDamaged), 0,
                "it walks to a gap and starts on the wall plugging it");

            Assert.IsTrue(world.Events.TryLast(SimEventKind.BuildingDestroyed, out SimEvent gone));
            Assert.IsTrue(gone.Cell == WestGap || gone.Cell == EastGap,
                "what it chewed was one of the plugs, not something else: " + gone.Cell);
            Assert.Greater(world.Events.CountOf(SimEventKind.CoreDamaged), 0,
                "and through the hole it walks in and reaches the Core");
        }

        [Test]
        public void ABreach_IsNotTakenThroughAWallNothingMayChew()
        {
            // The breach field's other half: a building no walker may attack is the map's own rock to it.
            // Both gaps are plugged - the near one above the Core with the untargetable block, the far one
            // at the map's edge with a pipe - so a field that priced the untargetable block would name the
            // near gap and stall the wave against it. It has to be the pipe.
            var nearGap = new Int2(10, 9);
            var farGap = new Int2(0, 9);
            SimWorld world = new SimWorld(
                new MapDefinition("plugged", 20, 24, Sim.TestCircles, new ShapePatch[0],
                    new Int2[0], new WaveDefinition[0],
                    TerrainLayout.Rects(20, 24,
                        new TerrainRect(new Int2(1, 9), 9, 1, TerrainKind.Rock),
                        new TerrainRect(new Int2(11, 9), 9, 1, TerrainKind.Rock)),
                    coreCell: new Int2(8, 4)),
                new SimConfig { CoreMaxHp = 1000000f },
                CustomContentTests.Rewrite(machine: UnattackableWall()));

            Sim.Place(world, BuildKind.Wall, nearGap, Dir.East);
            Sim.Place(world, BuildKind.Pipe, farGap, Dir.East);
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10.5f, 20.5f));

            for (int i = 0; i < 30 * 120 && world.Events.CountOf(SimEventKind.CoreDamaged) == 0; i++)
                world.Tick(InputCommand.None);

            Assert.IsTrue(world.Events.TryLast(SimEventKind.BuildingDamaged, out SimEvent hit),
                "the way in is walked to and chewed");
            Assert.AreEqual(farGap, hit.Cell, "and it is the gap that can be opened");
            Assert.IsTrue(world.Machines.Has(nearGap), "the block nothing may attack was left alone");
            Assert.Greater(world.Events.CountOf(SimEventKind.CoreDamaged), 0,
                "and the far hole lets the wave in");
        }

        [Test]
        public void AMachine_BehindRock_IsWalkedAround_NotHitThroughIt()
        {
            // Notice range reaches a wall right behind a rock; sight does not. The charge is only ever
            // along a line the walker can see, so it walks round the rock and hits the wall from beside it
            // - instead of standing in the rock's column and chewing a wall it cannot reach.
            SimWorld world = new SimWorld(
                new MapDefinition("rock in the lane", 20, 12, Sim.TestCircles, new ShapePatch[0],
                    new Int2[0], new WaveDefinition[0],
                    TerrainLayout.Rects(20, 12, new TerrainRect(new Int2(10, 9), 1, 1, TerrainKind.Rock)),
                    coreCell: new Int2(8, 4)),
                new SimConfig { CoreMaxHp = 1000000f });

            var wall = new Int2(10, 8);
            Sim.Place(world, BuildKind.Wall, wall, Dir.East);
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10.5f, 10.5f));

            Assert.Greater((new Vec2(10.5f, 10.5f) - new Vec2(10.5f, 8.5f)).Magnitude,
                world.Content.Enemy(EnemyKind.Spike).AggroRange, "the test's premise: not in reach yet");

            for (int i = 0; i < 30 * 60; i++)
            {
                world.Tick(InputCommand.None);
                if (world.Events.CountOf(SimEventKind.BuildingDamaged) == 0) continue;

                Assert.AreNotEqual(TileKind.Wall, world.TileGrid.Get(new Int2(10, 9)),
                    "the rock is still there, so the hit did not go through it");
                Assert.Greater(MathF.Abs(world.Enemies.EnemyAt(0).Position.X - 10.5f), 0.4f,
                    "and the walker had left the rock's column to get at the wall");
                return;
            }

            Assert.Fail("the wall was never reached");
        }

        [Test]
        public void AWallInTheLane_StopsTheWalk_AndIsChewedThrough()
        {
            // A wall on the straight lane: aggressive targeting means the enemy does not walk around it but
            // stops within reach and starts on the wall. A straight-line walker would reach the Core's ring
            // at tick ~51, so the wall must buy the Core more than a second of life by then.
            SimWorld world = Sim.NewWorld(20, 12);
            var wall = new Int2(10, 9);
            Sim.Place(world, BuildKind.Wall, wall, Dir.North);
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10.5f, 10.5f));

            for (int i = 0; i < 60; i++)
            {
                world.Tick(InputCommand.None);
                Int2 at = world.TileGrid.CellAt(world.Enemies.EnemyAt(0).Position);
                Assert.AreNotEqual(TileKind.Wall, world.TileGrid.Get(at),
                    "a machine is solid: the walker must never enter its cell");
            }

            Assert.AreEqual(world.Config.CoreMaxHp, world.Core.Hp, Sim.Tol,
                "the wall stopped the walk before the Core's ring");
            Assert.Greater(world.Events.CountOf(SimEventKind.BuildingDamaged), 0,
                "the enemy stopped to chew the wall instead of walking past it");

            // 120 hp at 6 damage a second: about twenty seconds of chewing, then the way opens and the
            // enemy walks the rest of the way in.
            for (int i = 0; i < 3000 && world.Events.CountOf(SimEventKind.CoreDamaged) == 0; i++)
                world.Tick(InputCommand.None);

            Assert.IsFalse(world.Machines.Has(wall), "the wall is ground down");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.BuildingDestroyed, out SimEvent gone));
            Assert.AreEqual(BuildKind.Wall, gone.Building, "and the stream says which building fell");
            Assert.Greater(world.Events.CountOf(SimEventKind.CoreDamaged), 0,
                "with the wall gone the enemy walks on and reaches the Core");
        }

        [Test]
        public void ABlockThatCannotBeAttacked_StillRedirectsTheWalk()
        {
            // The flow field on its own: with MaxHp 0 the block is never targeted, so the only way past is
            // around. The enemy must leave the straight lane, never enter the solid cell, and still arrive -
            // the walk-around the wall case hides behind the attack.
            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig(),
                CustomContentTests.Rewrite(machine: UnattackableWall()));
            var block = new Int2(10, 9);
            Sim.Place(world, BuildKind.Wall, block, Dir.North);
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10.5f, 10.5f));

            bool leftTheLane = false;
            for (int i = 0; i < 600 && world.Status == GameStatus.Playing; i++)
            {
                world.Tick(InputCommand.None);
                EnemyState e = world.Enemies.EnemyAt(0);
                Assert.AreNotEqual(block, world.TileGrid.CellAt(e.Position),
                    "solid means solid, even for a block nothing may attack");
                if (MathF.Abs(e.Position.X - 10.5f) > 0.4f) leftTheLane = true;
            }

            Assert.IsTrue(leftTheLane, "the walk had to leave the straight lane to get around the block");
            Assert.IsTrue(world.Machines.Has(block), "the block survives the whole run");
            Assert.AreEqual(0, world.Events.CountOf(SimEventKind.BuildingDamaged),
                "an untargetable machine is never hit");
            Assert.Greater(world.Events.CountOf(SimEventKind.CoreDamaged), 0,
                "and the walk around it still reaches the Core");
        }

        [Test]
        public void AnEnemy_ChargesAMachineItNotices_BeyondItsReach()
        {
            // The notice range: a wall 2.0 tiles away is beyond attack reach, so the enemy cannot hit it yet,
            // but inside notice range, so it leaves the lane and heads for the building, hitting only once in
            // reach.
            SimWorld world = Sim.NewWorld(20, 12);
            var wall = new Int2(10, 8);
            Sim.Place(world, BuildKind.Wall, wall, Dir.North);
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10.5f, 10.5f));

            EnemyDef spike = world.Content.Enemy(EnemyKind.Spike);
            var wallCentre = new Vec2(10.5f, 8.5f);
            float from = (new Vec2(10.5f, 10.5f) - wallCentre).Magnitude;
            Assert.Greater(from, spike.AggroRange, "the test's premise: two tiles is not in reach");
            Assert.LessOrEqual(from, spike.DetectionRange, "but it is in notice range");

            world.Tick(InputCommand.None);
            EnemyState charged = world.Enemies.EnemyAt(0);
            Assert.IsFalse(charged.Attacking, "noticing is not attacking");
            Assert.Less(charged.Position.Y, 10.5f, "but it charges: the step is toward the wall");

            for (int i = 0; i < 120 && !world.Enemies.EnemyAt(0).Attacking; i++)
                world.Tick(InputCommand.None);

            EnemyState engaged = world.Enemies.EnemyAt(0);
            Assert.IsTrue(engaged.Attacking, "and it starts hitting it once it is in reach");
            float gap = (engaged.Position - wallCentre).Magnitude;
            Assert.LessOrEqual(gap, spike.AggroRange + Sim.Tol, "from inside the reach");
            Assert.GreaterOrEqual(gap, 0.5f, "without entering the machine's cell");

            Sim.Tick(world, 60);
            Assert.IsTrue(world.Events.TryLast(SimEventKind.BuildingDamaged, out SimEvent hit));
            Assert.AreEqual(wall, hit.Cell);
            Assert.AreEqual(BuildKind.Wall, hit.Building);
        }

        [Test]
        public void AMachine_BeyondTheNoticeRange_IsLeftAlone()
        {
            // The other side of the same rule - notice range is a range, not "always": a machine four
            // tiles off the lane stays untouched while the wave walks past it to the Core.
            SimWorld world = Sim.NewWorld(20, 12);
            var machine = new Int2(14, 10);
            Sim.Place(world, BuildKind.Pipe, machine, Dir.North);
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10.5f, 10.5f));

            for (int i = 0; i < 600 && world.Events.CountOf(SimEventKind.CoreDamaged) == 0; i++)
                world.Tick(InputCommand.None);

            Assert.Greater(world.Events.CountOf(SimEventKind.CoreDamaged), 0,
                "the enemy walked on to the Core");
            Assert.IsTrue(world.Machines.Has(machine), "a machine four tiles off the lane is out of notice");
            Assert.AreEqual(0, world.Events.CountOf(SimEventKind.BuildingDamaged), "and untouched");
        }

        [Test]
        public void APasserby_StopsToAttack_AMachineInReach()
        {
            // A pipe one tile east of the lane, 30 hp: the enemy walks until it comes within reach, stops
            // beside it, chews it down in about four seconds, then carries on to the Core.
            SimWorld world = Sim.NewWorld(20, 12);
            var pipe = new Int2(11, 8);
            Sim.Place(world, BuildKind.Pipe, pipe, Dir.North);
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10.5f, 10.5f));

            float stoppedAt = 10.5f;
            int stoppedTick = 0;
            for (int i = 0; i < 300; i++)
            {
                world.Tick(InputCommand.None);
                EnemyState e = world.Enemies.EnemyAt(0);
                if (!e.Attacking) continue;

                stoppedAt = e.Position.Y;
                stoppedTick = world.TickCount;
                break;
            }

            Assert.Less(stoppedAt, 10.4f, "it walked before it stopped");
            Assert.Greater(stoppedAt, 9.0f, "it stopped beside the pipe, not at the Core");

            for (int i = 0; i < 400 && world.Machines.Has(pipe); i++)
            {
                world.Tick(InputCommand.None);
                Assert.GreaterOrEqual(world.Enemies.EnemyAt(0).Position.Y, stoppedAt - Sim.Tol,
                    "attacking holds the walk: no progress while the target stands");
            }

            Assert.IsFalse(world.Machines.Has(pipe), "the pipe is ground down");
            float seconds = (world.TickCount - stoppedTick) * SimConfig.TickDt;
            Assert.That(seconds, Is.InRange(3f, 6f),
                "30 hp at 6 damage a second is about five seconds, took " + seconds);

            Assert.IsTrue(world.Events.TryLast(SimEventKind.BuildingDamaged, out SimEvent hit));
            Assert.AreEqual(pipe, hit.Cell);
            Assert.AreEqual(BuildKind.Pipe, hit.Building);

            float atKill = world.Enemies.EnemyAt(0).Position.Y;
            Sim.Tick(world, 30);
            Assert.Less(world.Enemies.EnemyAt(0).Position.Y, atKill - 0.3f,
                "with the pipe gone the walk to the Core resumes");
        }

        [Test]
        public void ADestroyedMachine_PaysNoRefund()
        {
            // An unarmed cannon has no ammo line, so it never fires and cannot defend itself: the spike
            // grinds it down. Destruction must not refund what the player paid, or a chewed building is a
            // free undo.
            SimWorld world = Sim.NewWorld(20, 12);
            var cannon = new Int2(5, 5);
            Sim.Place(world, BuildKind.Cannon, cannon, Dir.North);
            int paid = world.Economy.Circles;
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(5.5f, 6.5f));

            for (int i = 0; i < 1200 && world.Machines.Has(cannon); i++)
                world.Tick(InputCommand.None);

            Assert.IsFalse(world.Machines.Has(cannon), "the unarmed gun is chewed down");
            Assert.AreEqual(TileKind.Empty, world.TileGrid.Get(cannon), "and its tile goes back to ground");
            Assert.AreEqual(paid, world.Economy.Circles, "destruction is not a refund");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.BuildingDestroyed, out SimEvent gone));
            Assert.AreEqual(cannon, gone.Cell);
            Assert.AreEqual(BuildKind.Cannon, gone.Building);

            int shots = world.ShotsFired;
            Sim.Tick(world, 60);
            Assert.AreEqual(shots, world.ShotsFired, "a destroyed gun cannot fire");
        }

        [Test]
        public void ZeroMaxHp_Machines_AreIndestructible()
        {
            // 0 on the definition is the opt-out: solid, walk-around-able, immune to the siege. Park a
            // spike next to one for ten seconds and nothing about it may change.
            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig(),
                CustomContentTests.Rewrite(machine: UnattackableWall()));
            var wall = new Int2(10, 9);
            Sim.Place(world, BuildKind.Wall, wall, Dir.North);
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10.5f, 10.5f));

            Sim.Tick(world, 300);

            Assert.IsTrue(world.Machines.Has(wall), "MaxHp 0 means indestructible");
            Assert.AreEqual(TileKind.Wall, world.TileGrid.Get(wall), "and it stays on its tile");
            Assert.AreEqual(0, world.Events.CountOf(SimEventKind.BuildingDamaged),
                "and untargetable: no damage event may ever name it");
        }
    }
}
