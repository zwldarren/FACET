using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the twist itself: a turret fires only what a belt physically delivers to it, a wrong
    /// shape jams the segment it lands on, and a jam silences everything downstream. The world is 20x12 (Core at
    /// (8,4)-(11,7)) with an effectively immortal Core, so a turret test measures firing and never drifts into
    /// the run ending. Turrets sit at y=10; enemies walk to the Core and stop, keeping a target in range.
    /// </summary>
    public class TurretTests
    {
        private const int W = 20;
        private const int H = 12;

        private static readonly Int2 Turret = new Int2(10, 10);
        private static readonly Int2 DeliveryCell = new Int2(9, 10);

        /// <summary>An immortal world with a Cannon at (10,10) fed by a belt at (9,10) pointing into it.</summary>
        private static SimWorld CannonWorld()
        {
            SimWorld world = Sim.NewEnduringWorld(W, H);
            Sim.LayRun(world, DeliveryCell, Dir.East, 1);
            Sim.Place(world, BuildKind.Cannon, Turret, Dir.East);
            return world;
        }

        /// <summary>Another target walking in: Spikes die to two shots, so a firing-rate test keeps the range
        /// stocked rather than watching one corpse. It walks in south-east of the Core, out of the turret's own
        /// reach - a target spawned on the turret's lane (1.0 away) would stop and chew the 80-hp gun instead of
        /// being shot, measuring a dead turret rather than a fire rate. The walk stays 3+ tiles out, inside range.</summary>
        private static void SpawnFreshSpike(SimWorld world)
            => world.Enemies.Spawn(EnemyKind.Spike, new Vec2(13.5f, 7.5f));

        private static void TopUp(SimWorld world, ShapeType shape, int ticks, params Int2[] cells)
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
            SimWorld world = CannonWorld();
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(13.5f, 10.5f));

            Sim.Tick(world, 120);   // four seconds with a target in range and nothing on the belt

            Assert.AreEqual(0, world.ShotsFired, "no shape, no shot - a turret is a consumer, not a gun");
            Assert.IsFalse(Sim.ArmedAt(world, Turret));
            Assert.AreEqual(world.Content.Enemy(EnemyKind.Spike).Hp, world.Enemies.EnemyAt(0).Hp, Sim.Tol,
                "and nothing it could do about it");

            TopUp(world, ShapeType.HalfCircle, 90, DeliveryCell);

            Assert.Greater(world.ShotsFired, 0, "one delivered half-circle is one shot");
        }

        [Test]
        public void Turret_FireRateIsTheLinesRate_NotItsSpec()
        {
            // A saturated line: the belt delivers 1/s and the cannon's ceiling is 1/s, so twelve seconds of
            // targets and feed is about twelve shots, give or take travel time. Targets keep coming because a
            // Spike folds after two shots - it cannot tank a rate measurement.
            SimWorld fed = CannonWorld();
            for (int i = 0; i < 12 * 30; i++)
            {
                if (i % 90 == 0) SpawnFreshSpike(fed);
                fed.Belts.TrySpawnItem(DeliveryCell, ShapeType.HalfCircle);
                fed.Tick(InputCommand.None);
            }
            Assert.GreaterOrEqual(fed.ShotsFired, 8, "one belt reaches the cannon's 1/s ceiling");
            Assert.LessOrEqual(fed.ShotsFired, 13, "and the ceiling holds: no faster line exists here");

            // A starving line: one item every four seconds fires one shot every four seconds. The spec did not
            // change - the supply did, which is the whole mechanic.
            SimWorld starved = CannonWorld();
            for (int i = 0; i < 5; i++)
            {
                SpawnFreshSpike(starved);
                Assert.IsTrue(starved.Belts.TrySpawnItem(DeliveryCell, ShapeType.HalfCircle));
                Sim.Tick(starved, 120);
            }

            Assert.GreaterOrEqual(starved.ShotsFired, 4, "five halves delivered");
            Assert.LessOrEqual(starved.ShotsFired, 5, "is five shots, at the line's pace");
        }

        [Test]
        public void Turret_NeedsTwoShots_ForOneSpike()
        {
            SimWorld world = CannonWorld();
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(13.5f, 10.5f));

            TopUp(world, ShapeType.HalfCircle, 300, DeliveryCell);

            Assert.AreEqual(0, world.Enemies.AliveCount, "the Spike is down");
            Assert.AreEqual(2, world.ShotsFired,
                world.Content.Enemy(EnemyKind.Spike).Hp + " hp against " +
                world.Content.Turret(BuildKind.Cannon).AmmoFor(ShapeType.HalfCircle).Damage +
                " dmg is exactly two shots - the ammo bill is readable before the wave starts");
        }

        /// <summary>
        /// The weakness rule, on the field directly rather than through a turret: a shot matching the target's
        /// weakness lands full damage, one that does not a quarter of it. Read off the HP rather than the event
        /// stream so a change to either multiplier shows up as the number it is.
        /// </summary>
        [Test]
        public void EnemyWeakness_ControlsHowMuchDamageAShapeDeals()
        {
            SimWorld matching = Sim.NewEnduringWorld(W, H);
            int matchingId = matching.Enemies.Spawn(EnemyKind.Spike, new Vec2(13.5f, 10.5f));
            float startingHp = matching.Enemies.EnemyAt(0).Hp;

            matching.Enemies.ApplyDamage(matchingId, 3f, ShapeType.HalfCircle);

            Assert.AreEqual(startingHp - 3f, matching.Enemies.EnemyAt(0).Hp,
                Sim.Tol, "the Spike's half-circle weakness takes the full 3 damage");

            SimWorld resistant = Sim.NewEnduringWorld(W, H);
            int resistantId = resistant.Enemies.Spawn(EnemyKind.Spike, new Vec2(13.5f, 10.5f));

            resistant.Enemies.ApplyDamage(resistantId, 3f, ShapeType.HalfSquare);

            Assert.AreEqual(startingHp - 0.75f, resistant.Enemies.EnemyAt(0).Hp,
                Sim.Tol, "half-square ammo is resisted, so the same 3 damage lands a quarter of itself");
        }

        [Test]
        public void Turret_PrefersTheEnemyItsAmmoHurts()
        {
            // A Bulwark closer than a Spike, both in range: the cannon aims at the Spike, because a matched shot
            // lands four times what a resisted one does. Both spawn far enough off the turret's lane that neither
            // charges the gun (3.6 and 5 tiles against a notice of 3.2), and the aim is read after one tick.
            SimWorld world = CannonWorld();
            world.Enemies.Spawn(EnemyKind.Bulwark, new Vec2(13.5f, 8.5f));
            int spike = world.Enemies.Spawn(EnemyKind.Spike, new Vec2(14.5f, 7.5f));

            world.Tick(InputCommand.None);

            Assert.AreEqual(spike, Sim.MachineAt(world, Turret).TargetId,
                "the nearer Bulwark barely feels a half-circle - the farther Spike is the shot worth taking");
        }

        [Test]
        public void Turret_AimsWithTheAmmunitionTheBeltDelivered()
        {
            // The same two enemies as the test above, and the opposite answer: a composite round is the
            // weakness of whatever it hits, so the two are equally good targets and the nearest wins -
            // where the gun's own half-circles sent the shot at the farther Spike. Which belt delivered
            // decides the shot and the aim alike.
            SimWorld world = CannonWorld();

            // Arm the gun with nothing in range, so the item stays on the belt: the aim can then be read on
            // the tick the targets appear, which is the tick the gun fires.
            Assert.IsTrue(world.Belts.TrySpawnItem(DeliveryCell, ShapeType.Composite));
            for (int i = 0; i < 60 && !Sim.ArmedAt(world, Turret); i++) world.Tick(InputCommand.None);
            Assert.IsTrue(Sim.ArmedAt(world, Turret), "the composite reached the gun's input and waited there");

            int bulwark = world.Enemies.Spawn(EnemyKind.Bulwark, new Vec2(13.5f, 8.5f));
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(14.5f, 7.5f));

            world.Tick(InputCommand.None);

            Assert.IsTrue(world.Events.TryLast(SimEventKind.ShotFired, out SimEvent shot),
                "the armed gun fired the item it was holding");
            Assert.AreEqual(ShapeType.Composite, shot.Shape, "the shot is made of what the belt delivered");
            Assert.AreEqual(bulwark, Sim.MachineAt(world, Turret).TargetId,
                "and it aims at the nearer Bulwark, because a composite hurts it too - no farther target is");
        }

        [Test]
        public void Turret_StillShootsAResistedEnemy_WhenNothingWeakIsInRange()
        {
            // The preference is not a refusal: with only a Bulwark in range the cannon plinks away at a quarter
            // damage, and eight resisted halves still bring six health down. Its walk to the Core passes 3+ tiles
            // from the gun, inside the 7-tile range the whole way.
            SimWorld world = CannonWorld();
            world.Enemies.Spawn(EnemyKind.Bulwark, new Vec2(13.5f, 8.5f));

            TopUp(world, ShapeType.HalfCircle, 400, DeliveryCell);

            Assert.AreEqual(0, world.Enemies.AliveCount, "the Bulwark is down");
            Assert.AreEqual(8, world.ShotsFired,
                world.Content.Enemy(EnemyKind.Bulwark).Hp + " hp against a quarter of " +
                world.Content.Turret(BuildKind.Cannon).AmmoFor(ShapeType.HalfCircle).Damage +
                " dmg is exactly eight shots - a held shot is none");
        }

        [Test]
        public void WrongShapeDelivery_JamsTheSegment_AndClearingItRestoresTheLine()
        {
            SimWorld world = CannonWorld();
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(13.5f, 10.5f));

            Assert.IsTrue(world.Belts.TrySpawnItem(DeliveryCell, ShapeType.Circle));   // raw mineral, not ammo
            Sim.Tick(world, 90);                                            // let it reach the turret's mouth

            Assert.IsTrue(world.Belts.IsJammed(DeliveryCell), "the segment the circle landed on is blocked");
            Assert.IsTrue(world.Belts.HasItemAt(DeliveryCell), "and keeps the item that caused it");
            Assert.AreEqual(0, world.ShotsFired);
            Assert.IsFalse(Sim.ArmedAt(world, Turret), "so the turret reads as silenced");
            Assert.AreEqual(1, world.Belts.JamCount);

            Assert.IsTrue(world.TryRemoveBuilding(DeliveryCell), "right-click clears the jam, not the belt");

            Assert.IsFalse(world.Belts.IsJammed(DeliveryCell));
            Assert.IsTrue(world.Belts.Has(DeliveryCell), "the line keeps its shape");
            Assert.IsFalse(world.Belts.HasItemAt(DeliveryCell), "and the offending item goes with the jam");
            Assert.AreEqual(0, world.Belts.JamCount);

            TopUp(world, ShapeType.HalfCircle, 60, DeliveryCell);
            Assert.Greater(world.ShotsFired, 0, "the line works again once the jam is clear");
        }

        [Test]
        public void JamUpstream_SilencesTheTurretAtTheEndOfTheLine()
        {
            SimWorld world = Sim.NewEnduringWorld(24, H);
            Sim.LayRun(world, new Int2(3, 10), Dir.East, 4);      // (3,10) .. (6,10)
            Sim.Place(world, BuildKind.Cannon, new Int2(7, 10), Dir.East);
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(10.5f, 10.5f));

            Assert.IsTrue(world.Belts.TryJam(new Int2(4, 10)), "a jam three tiles upstream");
            TopUp(world, ShapeType.HalfCircle, 300, new Int2(3, 10));

            Assert.AreEqual(0, world.ShotsFired, "halves are produced, but nothing reaches the turret");
            Assert.IsFalse(Sim.ArmedAt(world, new Int2(7, 10)));
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(3, 10)), "the line backed up behind the jam");

            Assert.IsTrue(world.Belts.TryClearJam(new Int2(4, 10)));
            Sim.Tick(world, 300);

            Assert.Greater(world.ShotsFired, 0, "clearing it releases everything downstream");
        }

        [Test]
        public void ArmedTurret_WithoutATarget_BanksAmmoOnTheBelt()
        {
            SimWorld world = CannonWorld();

            Assert.IsTrue(world.Belts.TrySpawnItem(DeliveryCell, ShapeType.HalfCircle));
            Sim.Tick(world, 90);

            Assert.AreEqual(0, world.ShotsFired, "nothing to shoot at");
            Assert.IsTrue(Sim.ArmedAt(world, Turret), "but the turret is loaded and says so");
            Assert.IsTrue(world.Belts.HasItemAt(DeliveryCell), "the shot is still on the belt, not wasted");
        }

        [Test]
        public void Turret_IgnoresWhatIsOutOfRange()
        {
            // Two turrets, same supply: the enemy walks to the Core and dies there under the near one's fire,
            // never entering the far one's range. A Spike only tanks two shots, so the target flags are sampled
            // every tick instead of after the fight.
            SimWorld world = Sim.NewEnduringWorld(W, H);
            var near = new Int2(12, 11);
            var far = new Int2(19, 11);
            Sim.LayRun(world, new Int2(11, 11), Dir.East, 1);
            Sim.LayRun(world, new Int2(18, 11), Dir.East, 1);
            Sim.Place(world, BuildKind.Cannon, near, Dir.East);
            Sim.Place(world, BuildKind.Cannon, far, Dir.East);

            // Walks to the Core (10,6) and stops on its edge. At the spawn point it is already 7.4 tiles from
            // `far` against a range of 7, and every step it takes is toward the Core.
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(13f, 8f));

            bool nearSawTarget = false;
            bool farSawTarget = false;
            for (int i = 0; i < 180; i++)
            {
                world.Belts.TrySpawnItem(new Int2(11, 11), ShapeType.HalfCircle);
                world.Belts.TrySpawnItem(new Int2(18, 11), ShapeType.HalfCircle);
                world.Tick(InputCommand.None);
                nearSawTarget |= Sim.MachineAt(world, near).HasTarget;
                farSawTarget |= Sim.MachineAt(world, far).HasTarget;
            }

            Assert.Greater(world.ShotsFired, 0, "the turret that covers the Core's approach fires");
            Assert.IsTrue(nearSawTarget);
            Assert.IsFalse(farSawTarget, "and the one further out never even aims");
        }

        [Test]
        public void CannonSpec_MatchesTheDesign_AndItEatsOnlyHalves()
        {
            Assert.AreEqual(ShapeType.HalfCircle, ContentDatabase.Default.Turret(BuildKind.Cannon).PrimaryAmmo,
                "the cannon eats what the decomposer makes");
            Assert.AreEqual(1f, ContentDatabase.Default.Turret(BuildKind.Cannon).ShotsPerSecond, Sim.Tol,
                "the doc's 1 shot/s: exactly one belt's worth");
            Assert.AreEqual(ShapeType.HalfCircle, ContentDatabase.Default.Enemy(EnemyKind.Spike).Weakness,
                "and the one enemy is weak to the one ammo");

            SimWorld world = CannonWorld();
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(12.5f, 9.5f));

            Assert.IsTrue(world.Belts.TrySpawnItem(DeliveryCell, ShapeType.Circle));
            Sim.Tick(world, 90);

            Assert.IsTrue(world.Belts.IsJammed(DeliveryCell), "a circle is not ammo");
            Assert.AreEqual(0, world.ShotsFired);

            world.TryRemoveBuilding(DeliveryCell);
            TopUp(world, ShapeType.HalfCircle, 300, DeliveryCell);
            Assert.Greater(world.ShotsFired, 0, "half-circles are");
        }
    }
}
