using System;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the siege rules: machines are solid and destructible, enemies follow the flow
    /// field around them and stop to chew on any attackable machine within reach, a sealed Core is
    /// chewed through, destruction pays no refund, and a definition with MaxHp 0 is untargetable and
    /// indestructible.
    ///
    /// The worlds are the standard test rigs: <see cref="Sim.NewWorld(int,int)"/> gives the Core at
    /// (8,4)..(11,7) with centre (10,6) on a featureless map, so every layout and every lane here is
    /// this file's own.
    /// </summary>
    public class SiegeTests
    {
        /// <summary>The same shipped wall with MaxHp 0: solid but not attackable - the definition
        /// level's opt-out (<see cref="MachineDef.MaxHp"/>). Handed to a world through the custom-table
        /// helper <see cref="CustomContentTests"/> uses, so the test proves the rule and not a private
        /// code path.</summary>
        private static MachineDef UnattackableWall()
        {
            MachineDef wall = ContentDatabase.Default.Machine(BuildKind.Wall);
            return new MachineDef(wall.Id, wall.Build, wall.Tile, wall.Behavior, wall.Cost, wall.Name,
                wall.Description, wall.RecipeId, wall.Interval, wall.Filter, maxHp: 0f);
        }

        [Test]
        public void AWallInTheLane_StopsTheWalk_AndIsChewedThrough()
        {
            // A wall on the straight lane to the Core. Aggressive targeting means the enemy does not
            // walk around it: it stops within reach and starts on the wall, which is what a wall in the
            // way is for. A straight-line walker would reach the Core's ring at tick ~51, so the wall
            // has to have bought the Core more than a second of life by then.
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
            // The flow field on its own: with MaxHp 0 the block is never targeted (that is what the
            // definition's zero means), so the only way past it is around it. The enemy must leave the
            // straight lane, never enter the solid cell, and still arrive - which is exactly the
            // walk-around the wall case hides behind the attack.
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
            // The notice range: a wall 2.0 tiles away is further than the attack reach, so the enemy
            // cannot hit it yet - but it is inside the notice range, so it leaves the lane and heads
            // for the building instead of walking past something it can plainly see, and only starts
            // hitting it once it is in reach.
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
            // The other side of the same rule, so the notice range is a range and not "always": a
            // machine four tiles off the lane stays untouched while the wave walks past it to the Core.
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
            // A pipe one tile east of the lane, 30 hp. The enemy walks until the pipe comes within
            // reach, stops beside it, chews it down in about four seconds, and then carries on to the
            // Core - the whole of the aggressive-scan rule on one walk.
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
            // grinds it down. The player paid for the cannon; its destruction must not pay them back,
            // or a chewed building would be a free undo.
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
            // 0 on the definition is the opt-out: solid, walk-around-able, and immune to the siege.
            // Park a spike next to one for ten seconds and nothing about it may change.
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
