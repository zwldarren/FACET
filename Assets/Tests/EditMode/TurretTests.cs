using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the one gun and its universal chamber: it fires only what a belt physically
    /// delivers to it, it eats <em>any</em> non-empty shape, and a round is worth four times as much
    /// against an enemy whose demand it <em>is</em>. The world is 20x12 (Core at (8,4)-(11,7)) with an
    /// effectively immortal Core, so a turret test measures firing and never drifts into the run ending.
    /// The turret sits at (10,10); targets walk to the Core and stop, keeping a target in range.
    ///
    /// What used to be here - a jam on the segment that landed the wrong shape, and an ammo table deciding
    /// what the gun would deign to eat - is deliberately gone. Nothing a belt can deliver is refused now:
    /// the punishment for the wrong shape moved to the damage roll, and the answer to a mismatched line is
    /// the briefing rather than a jam to clear.
    /// </summary>
    public class TurretTests
    {
        private const int W = 20;
        private const int H = 12;

        private static readonly Int2 Turret = new Int2(10, 10);
        private static readonly Int2 DeliveryCell = new Int2(9, 10);

        /// <summary>What this file's targets demand, and therefore the round that does full damage to
        /// them. The gun is universal, so the number is a choice the test makes, not the machine's.</summary>
        private static readonly Shape Demand = Sim.TopHalfCircle;

        /// <summary>An immortal world with the one turret at (10,10) fed by a belt at (9,10) pointing into it.</summary>
        private static SimWorld TurretWorld()
        {
            SimWorld world = Sim.NewEnduringWorld(W, H);
            Sim.LayRun(world, DeliveryCell, Dir.East, 1);
            Sim.Place(world, BuildKind.Turret, Turret, Dir.East);
            return world;
        }

        /// <summary>Another target walking in: a Runner dies to two matched shots, so a firing-rate test
        /// keeps the range stocked rather than watching one corpse. It walks in south-east of the Core, out
        /// of the turret's own reach - a target spawned on the turret's lane (1.0 away) would stop and chew
        /// the 80-hp gun instead of being shot, measuring a dead turret rather than a fire rate. The walk
        /// stays 3+ tiles out, inside range.</summary>
        private static void SpawnFreshTarget(SimWorld world)
            => world.Enemies.Spawn(EnemyKind.Runner, new Vec2(13.5f, 7.5f), Demand);

        private static void TopUp(SimWorld world, Shape shape, int ticks, params Int2[] cells)
        {
            for (int i = 0; i < ticks; i++)
            {
                foreach (Int2 cell in cells) world.Belts.TrySpawnItem(cell, shape);
                world.Tick(InputCommand.None);
            }
        }

        [Test]
        public void Turret_StaysSilent_UntilAShapeArrives()
        {
            SimWorld world = TurretWorld();
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(13.5f, 10.5f), Demand);

            Sim.Tick(world, 120);   // four seconds with a target in range and nothing on the belt

            Assert.AreEqual(0, world.ShotsFired, "no shape, no shot - a turret is a consumer, not a gun");
            Assert.IsFalse(Sim.ArmedAt(world, Turret));
            Assert.AreEqual(world.Content.Enemy(EnemyKind.Runner).Hp, world.Enemies.EnemyAt(0).Hp, Sim.Tol,
                "and nothing it could do about it");

            TopUp(world, Demand, 90, DeliveryCell);

            Assert.Greater(world.ShotsFired, 0, "one delivered shape is one shot");
        }

        [Test]
        public void Turret_FireRateIsTheLinesRate_NotItsSpec()
        {
            // A saturated line: the belt delivers 1/s and the turret's ceiling is 1/s, so twelve seconds of
            // targets and feed is about twelve shots, give or take travel time. Targets keep coming because a
            // Runner folds after two shots - it cannot tank a rate measurement.
            SimWorld fed = TurretWorld();
            for (int i = 0; i < 12 * 30; i++)
            {
                if (i % 90 == 0) SpawnFreshTarget(fed);
                fed.Belts.TrySpawnItem(DeliveryCell, Demand);
                fed.Tick(InputCommand.None);
            }
            Assert.GreaterOrEqual(fed.ShotsFired, 8, "one belt reaches the turret's 1/s ceiling");
            Assert.LessOrEqual(fed.ShotsFired, 13, "and the ceiling holds: no faster line exists here");

            // A starving line: one item every four seconds fires one shot every four seconds. The spec did not
            // change - the supply did, which is the whole mechanic.
            SimWorld starved = TurretWorld();
            for (int i = 0; i < 5; i++)
            {
                SpawnFreshTarget(starved);
                Assert.IsTrue(starved.Belts.TrySpawnItem(DeliveryCell, Demand));
                Sim.Tick(starved, 120);
            }

            Assert.GreaterOrEqual(starved.ShotsFired, 4, "five rounds delivered");
            Assert.LessOrEqual(starved.ShotsFired, 5, "is five shots, at the line's pace");
        }

        [Test]
        public void Turret_NeedsTwoShots_ForOneEnemyThatDemandsItsRound()
        {
            SimWorld world = TurretWorld();
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(13.5f, 10.5f), Demand);

            TopUp(world, Demand, 300, DeliveryCell);

            Assert.AreEqual(0, world.Enemies.AliveCount, "the Runner is down");
            Assert.AreEqual(2, world.ShotsFired,
                world.Content.Enemy(EnemyKind.Runner).Hp + " hp against " +
                world.Content.Turret(BuildKind.Turret).Damage +
                " matched damage is exactly two shots - the round's price is readable before the wave starts");
        }

        /// <summary>
        /// The weakness rule, on the field directly rather than through a turret: a round that IS the
        /// target's demand lands full damage, anything else a quarter of it. Read off the HP rather than the
        /// event stream so a change to either multiplier shows up as the number it is.
        /// </summary>
        [Test]
        public void EnemyWeakness_ControlsHowMuchDamageAShapeDeals()
        {
            SimWorld matching = Sim.NewEnduringWorld(W, H);
            int matchingId = matching.Enemies.Spawn(EnemyKind.Runner, new Vec2(13.5f, 10.5f), Demand);
            float startingHp = matching.Enemies.EnemyAt(0).Hp;

            matching.Enemies.ApplyDamage(matchingId, 3f, Demand);

            Assert.AreEqual(startingHp - 3f, matching.Enemies.EnemyAt(0).Hp,
                Sim.Tol, "the round the target demands takes the full 3 damage");

            SimWorld resistant = Sim.NewEnduringWorld(W, H);
            int resistantId = resistant.Enemies.Spawn(EnemyKind.Runner, new Vec2(13.5f, 10.5f), Demand);

            resistant.Enemies.ApplyDamage(resistantId, 3f, Sim.Square);

            Assert.AreEqual(startingHp - 0.75f, resistant.Enemies.EnemyAt(0).Hp,
                Sim.Tol, "a round that is not the demand is resisted, so the same 3 damage lands a quarter");

            // And "is", not "contains": the item that carries both minerals used to match every weakness.
            SimWorld structural = Sim.NewEnduringWorld(W, H);
            int structuralId = structural.Enemies.Spawn(EnemyKind.Runner, new Vec2(13.5f, 10.5f), Demand);

            structural.Enemies.ApplyDamage(structuralId, 3f, Sim.Composite);

            Assert.IsTrue(Sim.Composite.Contains(Quad.Circle), "the composite does carry a circle quadrant");
            Assert.AreEqual(startingHp - 0.75f, structural.Enemies.EnemyAt(0).Hp, Sim.Tol,
                "and still does not match: a demand is an exact item, not a mineral it happens to contain");
        }

        [Test]
        public void Turret_PrefersTheEnemyItsRoundHurts()
        {
            // A Tank closer than the Runner the round is for, both in range: the gun aims at the Runner,
            // because a matched shot lands four times what a resisted one does. Both spawn far enough off the
            // turret's lane that neither charges the gun (3.6 and 5.0 tiles against a notice of 3.2 and 2.5),
            // and the aim is read after one tick.
            SimWorld world = TurretWorld();

            // Arm the gun with nothing in range, so the round stays on the belt: the aim can then be read on
            // the tick the targets appear, which is the tick the gun fires.
            Assert.IsTrue(world.Belts.TrySpawnItem(DeliveryCell, Demand));
            for (int i = 0; i < 60 && !Sim.ArmedAt(world, Turret); i++) world.Tick(InputCommand.None);
            Assert.IsTrue(Sim.ArmedAt(world, Turret), "the round reached the gun's input and waited there");

            world.Enemies.Spawn(EnemyKind.Tank, new Vec2(13.5f, 8.5f), Sim.Square);
            int runner = world.Enemies.Spawn(EnemyKind.Runner, new Vec2(14.5f, 7.5f), Demand);

            world.Tick(InputCommand.None);

            Assert.IsTrue(world.Events.TryLast(SimEventKind.ShotFired, out SimEvent shot),
                "the armed gun fired the round it was holding");
            Assert.AreEqual(Demand, shot.Shape, "the shot is made of what the belt delivered");
            Assert.AreEqual(runner, Sim.MachineAt(world, Turret).TargetId,
                "and it aims at the farther Runner, because the round is what that Runner demands");
        }

        [Test]
        public void Turret_FallsBackToTheNearest_WhenItsRoundMatchesNobody()
        {
            // The preference is not a refusal, and it is not a rule about distance either: with a round
            // nobody demands in range, both targets are equally (badly) matched and the nearest wins - where
            // the test above sent the same shot at the farther one because the round was its demand.
            SimWorld world = TurretWorld();

            Assert.IsTrue(world.Belts.TrySpawnItem(DeliveryCell, Demand));
            for (int i = 0; i < 60 && !Sim.ArmedAt(world, Turret); i++) world.Tick(InputCommand.None);

            int tank = world.Enemies.Spawn(EnemyKind.Tank, new Vec2(13.5f, 8.5f), Sim.Square);
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(14.5f, 7.5f), Sim.Square);

            world.Tick(InputCommand.None);

            Assert.AreEqual(tank, Sim.MachineAt(world, Turret).TargetId,
                "with nothing to prefer, the nearer enemy is the one in the sights");
        }

        [Test]
        public void Turret_StillShootsAResistedEnemy_WhenNothingWeakIsInRange()
        {
            // The preference is not a refusal: with only a mismatched target in range the gun plinks away at
            // a quarter damage, and six resisted rounds still bring six health down. Its walk to the Core
            // passes 3+ tiles from the gun, inside the 7-tile range the whole way - a held shot is none, and
            // holding fire while something it can still scratch is in range would be strictly worse.
            SimWorld world = TurretWorld();
            world.Enemies.Spawn(EnemyKind.Tank, new Vec2(13.5f, 8.5f), Sim.Square);

            TopUp(world, Demand, 400, DeliveryCell);

            Assert.AreEqual(0, world.Enemies.AliveCount, "the Tank is down");
            Assert.AreEqual(6, world.ShotsFired,
                world.Content.Enemy(EnemyKind.Tank).Hp + " hp against a quarter of " +
                world.Content.Turret(BuildKind.Turret).Damage +
                " is exactly six shots - a held shot is none");
        }

        [Test]
        public void NoShapeIsAWrongDelivery_SoNothingTheChamberTakesJams()
        {
            // The twist the old turret had, inverted. Raw mineral used to jam the segment that landed it,
            // which made feeding a gun safely a routing problem. The rework moved that pressure to the damage
            // roll: a circle is eaten and fired as a resisted round, and the belt it arrived on keeps working.
            SimWorld world = TurretWorld();
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(13.5f, 10.5f), Sim.Square);

            TopUp(world, Sim.Circle, 300, DeliveryCell);

            Assert.AreEqual(0, world.Belts.JamCount, "a circle is not a wrong-shape delivery any more");
            Assert.AreEqual(0, world.Enemies.AliveCount, "the circle round still brings it down");
            Assert.AreEqual(6, world.ShotsFired, "at a quarter of the damage, six times over");
        }

        [Test]
        public void JamUpstream_SilencesTheTurretAtTheEndOfTheLine()
        {
            // A jam is still a mechanic - a table with a narrowed diet can make one, and right-click clears
            // it - so the gun's dependence on its line is unchanged: a blocked segment upstream is silence
            // at the muzzle, and clearing it releases everything downstream.
            SimWorld world = Sim.NewEnduringWorld(24, H);
            Sim.LayRun(world, new Int2(3, 10), Dir.East, 4);      // (3,10) .. (6,10)
            Sim.Place(world, BuildKind.Turret, new Int2(7, 10), Dir.East);
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(10.5f, 10.5f), Demand);

            Assert.IsTrue(world.Belts.TryJam(new Int2(4, 10)), "a jam three tiles upstream");
            TopUp(world, Demand, 300, new Int2(3, 10));

            Assert.AreEqual(0, world.ShotsFired, "rounds are produced, but nothing reaches the turret");
            Assert.IsFalse(Sim.ArmedAt(world, new Int2(7, 10)));
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(3, 10)), "the line backed up behind the jam");

            Assert.IsTrue(world.Belts.TryClearJam(new Int2(4, 10)));
            Sim.Tick(world, 300);

            Assert.Greater(world.ShotsFired, 0, "clearing it releases everything downstream");
        }

        [Test]
        public void ArmedTurret_WithoutATarget_BanksAmmoOnTheBelt()
        {
            SimWorld world = TurretWorld();

            Assert.IsTrue(world.Belts.TrySpawnItem(DeliveryCell, Demand));
            Sim.Tick(world, 90);

            Assert.AreEqual(0, world.ShotsFired, "nothing to shoot at");
            Assert.IsTrue(Sim.ArmedAt(world, Turret), "but the turret is loaded and says so");
            Assert.IsTrue(world.Belts.HasItemAt(DeliveryCell), "the round is still on the belt, not wasted");
        }

        [Test]
        public void Turret_DoesNotConsume_WhileItIsCoolingDown()
        {
            // The design's own words - "with no target or while cooling down it does not consume; ammo queues
            // visibly on the belt". A three-second gun against the one-second belt opens the window wide
            // enough to read: the second round arrives at the muzzle two seconds before the gun is ready and
            // must still be lying there afterwards.
            var slow = new TurretDef(BuildKind.Turret, "Turret", damage: 4f, fireInterval: 3f, range: 7f,
                projectileSpeed: 14f);
            SimWorld world = new SimWorld(Sim.TestMap(W, H), new SimConfig { CoreMaxHp = 1000000f },
                CustomContentTests.Rewrite(turret: slow));
            Sim.LayRun(world, DeliveryCell, Dir.East, 1);
            Sim.Place(world, BuildKind.Turret, Turret, Dir.East);
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(13.5f, 7.5f), Demand);

            Assert.IsTrue(world.Belts.TrySpawnItem(DeliveryCell, Demand));
            Sim.Tick(world, 40);
            Assert.AreEqual(1, world.ShotsFired, "the first round was fired the moment it reached the muzzle");

            Assert.IsTrue(world.Belts.TrySpawnItem(DeliveryCell, Demand));
            Sim.Tick(world, 30);
            Assert.AreEqual(1, world.ShotsFired, "two seconds later the gun is still cooling down");
            Assert.IsTrue(world.Belts.HasItemAt(DeliveryCell),
                "so the round queues on the belt rather than being eaten");
            Assert.IsTrue(Sim.ArmedAt(world, Turret), "and the gun reads as armed, not as starved");

            Sim.Tick(world, 90);
            Assert.AreEqual(2, world.ShotsFired, "and it fires the moment it is off cooldown");
        }

        [Test]
        public void TheChamber_KeepsTheLastRoundItFired_AsItsBadge()
        {
            // A universal chamber holds nothing between shots, so the honest badge is what the line most
            // recently fed it - the only way a player can see which shape their factory is actually making.
            SimWorld world = TurretWorld();
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(13.5f, 7.5f), Sim.Square);

            TopUp(world, Sim.Circle, 300, DeliveryCell);

            Assert.Greater(world.ShotsFired, 0);
            Assert.AreEqual(Sim.Circle, Sim.MachineAt(world, Turret).Carried,
                "the badge is the last round fired, not an empty chamber");

            Assert.IsTrue(world.Machines.TryGetSnapshot(Turret, out MachineSnapshot snapshot));
            Assert.AreEqual(Sim.Circle, snapshot.Shape, "and the view reads that same badge");
        }

        [Test]
        public void Turret_IgnoresWhatIsOutOfRange()
        {
            // Two turrets, same supply: the enemy walks to the Core and dies there under the near one's fire,
            // never entering the far one's range. A Runner only tanks two shots, so the target flags are
            // sampled every tick instead of after the fight.
            SimWorld world = Sim.NewEnduringWorld(W, H);
            var near = new Int2(12, 11);
            var far = new Int2(19, 11);
            Sim.LayRun(world, new Int2(11, 11), Dir.East, 1);
            Sim.LayRun(world, new Int2(18, 11), Dir.East, 1);
            Sim.Place(world, BuildKind.Turret, near, Dir.East);
            Sim.Place(world, BuildKind.Turret, far, Dir.East);

            // Walks to the Core (10,6) and stops on its edge. At the spawn point it is already 7.4 tiles from
            // `far` against a range of 7, and every step it takes is toward the Core.
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(13f, 8f), Demand);

            bool nearSawTarget = false;
            bool farSawTarget = false;
            for (int i = 0; i < 180; i++)
            {
                world.Belts.TrySpawnItem(new Int2(11, 11), Demand);
                world.Belts.TrySpawnItem(new Int2(18, 11), Demand);
                world.Tick(InputCommand.None);
                nearSawTarget |= Sim.MachineAt(world, near).HasTarget;
                farSawTarget |= Sim.MachineAt(world, far).HasTarget;
            }

            Assert.Greater(world.ShotsFired, 0, "the turret that covers the Core's approach fires");
            Assert.IsTrue(nearSawTarget);
            Assert.IsFalse(farSawTarget, "and the one further out never even aims");
        }

        [Test]
        public void TheOneTurret_IsTheDesignsNumbers_AndTheChamberHasNoDiet()
        {
            TurretDef turret = ContentDatabase.Default.Turret(BuildKind.Turret);

            Assert.AreEqual(4f, turret.Damage, Sim.Tol, "the doc's base damage");
            Assert.AreEqual(1f, turret.ShotsPerSecond, Sim.Tol,
                "the doc's 1 shot/s: exactly one belt's worth");
            Assert.AreEqual(7f, turret.Range, Sim.Tol);

            Assert.AreEqual(FilterMode.Any, ContentDatabase.Default.Machine(BuildKind.Turret).Diet.Mode,
                "the universal chamber eats any non-empty shape - there is no ammo row and no splash");
            Assert.IsTrue(ContentDatabase.Default.IsTurret(BuildKind.Turret),
                "and there is exactly one gun in the table, whatever a custom one adds");

            // Behaviourally: a raw mineral is a round like any other, and it leaves the muzzle.
            SimWorld world = TurretWorld();
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(12.5f, 9.5f), Sim.Circle);

            TopUp(world, Sim.Circle, 300, DeliveryCell);

            Assert.Greater(world.ShotsFired, 0, "a circle is a round");
            Assert.AreEqual(0, world.Enemies.AliveCount, "and a matched one brings the enemy down");
        }
    }
}
