using System;
using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The Phase-1 promise, tested end to end: the machine behaviours read their numbers from the
    /// <see cref="ContentDatabase"/> the world was handed, so a different table is a different game - a
    /// cutter that eats only circles, a turret with different numbers, an enemy with different stats -
    /// with no switch anywhere edited. This is what "add content = fill in data" has to mean.
    ///
    /// It is also where the two rules the shipped table no longer triggers are proved, because they are
    /// still live rules: <see cref="MachineDef.Diet"/> is a content field, so a table that narrows one gets
    /// the old wrong-shape jam back, and <see cref="MachineSystem"/> refuses at construction a row asking
    /// for a behaviour nothing implements.
    /// </summary>
    public class CustomContentTests
    {
        [Test]
        public void ANarrowedDiet_JamsTheBeltThatLandsTheWrongShape()
        {
            // The shipped cutter eats anything (its diet is ShapeFilter.Any), because the rework moved the
            // punishment for a wrong shape from the belt to the damage roll. The mechanism is still live -
            // "what may a machine take" is one question with one answer per machine - so a table that
            // narrows the diet gets the routing tension back for free, and this is that rule: a square
            // landing on a circle-only cutter blocks the segment it landed on and stays blocked.
            MachineDef picky = CutterOnADiet(ShapeFilter.Is(Sim.Circle));
            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig(), Rewrite(machine: picky));

            var input = new Int2(4, 9);
            Sim.LayRun(world, input, Dir.East, 1);
            Sim.Place(world, BuildKind.Cutter, new Int2(5, 9), Dir.North);

            Assert.IsTrue(world.Belts.TrySpawnItem(input, Sim.Square));
            Sim.Tick(world, 30 * 3);

            Assert.IsTrue(world.Belts.IsJammed(input), "the diet refused it, so its own belt pays");
            Assert.AreEqual(1, world.Belts.JamCount);
            Assert.AreEqual(Sim.Square, Sim.ShapeAt(world, input),
                "the offending shape is left where it stood, for the player to find");
            Assert.IsTrue(Sim.MachineAt(world, new Int2(5, 9)).Carried.IsEmpty,
                "and the cutter never swallowed it");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.Jammed, out SimEvent jam));
            Assert.AreEqual(input, jam.Cell);
            Assert.AreEqual(Sim.Square, jam.Shape, "the report names the shape that arrived wrong");

            // The control: the same machine, the shape its diet names. One rule, two answers.
            SimWorld fed = new SimWorld(Sim.TestMap(20, 12), new SimConfig(), Rewrite(machine: picky));
            Sim.LayRun(fed, input, Dir.East, 1);
            Sim.Place(fed, BuildKind.Cutter, new Int2(5, 9), Dir.North);
            Sim.LayRun(fed, new Int2(5, 10), Dir.North, 1);

            Assert.IsTrue(fed.Belts.TrySpawnItem(input, Sim.Circle));
            Sim.Tick(fed, 30 * 5);

            Assert.AreEqual(0, fed.Belts.JamCount, "a circle is in the diet, so nothing jams");
            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(fed, new Int2(5, 10)),
                "and the machine did the work it was bought for");
        }

        [Test]
        public void AMachineRow_WithNoRegisteredBehaviour_IsRefusedAtConstruction()
        {
            // A content row asking for a behaviour the machine system does not implement would build,
            // stand on its tile, and silently do nothing forever - no ports, no badge, no tick, no error.
            // The refusal is at construction, in MachineSystem, so it names the row rather than surfacing
            // as a machine that appears broken in whichever world happened to use the table.
            var alien = new MachineDef(new ContentId("alien"), BuildKind.Wall, TileKind.Wall,
                (BehaviorKind)99, cost: 1, name: "Alien", description: "unknown", interval: 0f);

            var thrown = Assert.Throws<InvalidOperationException>(
                () => new SimWorld(Sim.TestMap(20, 12), new SimConfig(), Rewrite(machine: alien)));

            StringAssert.Contains("Alien", thrown.Message, "the row that asked is named");
            StringAssert.Contains("99", thrown.Message, "and so is the behaviour it asked for");

            // And the shipped table passes the same check - the rule is a rule, not a special case.
            Assert.DoesNotThrow(() => Sim.NewWorld(20, 12));
        }

        [Test]
        public void ACutter_SplitsWhateverItIsFed_BecauseTheRecipeTableIsGone()
        {
            // The old decomposer ran a recipe chosen by the delivered shape; processing is an operator now,
            // so there is no table to consult and the machine cuts whatever arrives - a square comes out as
            // two half-squares, off the same machine that turned a circle into half-circles a moment ago.
            SimWorld world = Sim.NewWorld(20, 12);

            var input = new Int2(4, 9);
            Sim.LayRun(world, input, Dir.East, 1);
            Sim.Place(world, BuildKind.Cutter, new Int2(5, 9), Dir.North);
            Sim.LayRun(world, new Int2(5, 10), Dir.North, 1);   // the top piece's outlet

            Assert.IsTrue(world.Belts.TrySpawnItem(input, Sim.Square));
            Sim.Tick(world, 30 * 4);

            Assert.AreEqual(Sim.TopHalfSquare, Sim.ShapeAt(world, new Int2(5, 10)),
                "a square in, a half-square out - no recipe chose that, the cut line did");
            Assert.AreEqual(0, world.Belts.JamCount, "and nothing about it was a wrong-shape delivery");
        }

        [Test]
        public void ATurret_EatsAnyShape_AndTheRoundsOwnShapeSetsThePrice()
        {
            // The universal chamber: there is no diet to satisfy, so a circle line feeds the gun as readily
            // as a half-circle line - and what the shot is worth is the round against the enemy's own
            // demand, assigned at spawn. A 3-HP enemy weak to circles dies to one circle round here, where
            // the shipped enemy would have taken two halves.
            var fragile = new EnemyDef(new ContentId("runner"), EnemyKind.Runner, "Fragile", hp: 3f, speed: 1f,
                damage: 6f, attackInterval: 1f);

            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig { CoreMaxHp = 1000000f },
                Rewrite(enemy: fragile));

            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Turret, new Int2(4, 2), Dir.East);

            int id = world.Enemies.Spawn(EnemyKind.Runner, new Vec2(4.5f, 5f), Sim.Circle);
            Assert.Greater(id, 0);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Circle));
            Sim.Tick(world, 120);

            Assert.Greater(world.ShotsFired, 0, "the chamber has no diet, so the circle line fed it");
            Assert.AreEqual(0, world.Enemies.AliveCount,
                "and one matched shot of the shipped 4 damage killed the 3-HP enemy");
        }

        [Test]
        public void ATableThatChangesCosts_ChangesWhatCanBeAfforded()
        {
            // The economy reads the injected table too, so a retuned cost is a real constraint, not just a
            // HUD string.
            MachineDef dearTurret = Turret(BuildKind.Turret, cost: Sim.TestCircles + 1);

            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig(), Rewrite(machine: dearTurret));

            Assert.AreEqual(Sim.TestCircles + 1, world.Content.Machine(BuildKind.Turret).Cost);
            Assert.IsFalse(world.Economy.CanAfford(BuildKind.Turret),
                "the start stockpile cannot cover the retuned cost");
            Assert.IsFalse(world.TryPlace(BuildKind.Turret, new Int2(2, 2), Dir.East));

            Assert.AreEqual(20, ContentDatabase.Default.Machine(BuildKind.Turret).Cost,
                "the shipped table is not affected by a table one world was handed");
            Assert.IsTrue(Sim.NewWorld(20, 12).Economy.CanAfford(BuildKind.Turret));
        }

        [Test]
        public void ASorter_FiltersWhateverItsDefinitionNames()
        {
            // Mirror the shipped sorter: route half-circles out the facing side instead of anything with a
            // circle in it. Same behaviour, same code path, opposite machine - a row, not a class.
            MachineDef mirrored = SorterRouting(ShapeFilter.Is(Sim.TopHalfCircle));
            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig(), Rewrite(machine: mirrored));

            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);       // input
            Sim.Place(world, BuildKind.Sorter, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);       // the facing outlet
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 1);      // the other outlet

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.TopHalfCircle));
            Sim.Tick(world, 90);

            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(world, new Int2(5, 2)),
                "the retuned filter sent the item it names out the side it faces");
            Assert.AreEqual(Shape.None, Sim.ShapeAt(world, new Int2(4, 3)),
                "and the other outlet stayed empty");
        }

        // ---------------------------------------------------------------- the two rows a test retunes

        /// <summary>The shipped cutter with a diet that admits one shape rule and nothing else - the row a
        /// table narrows when it wants the old wrong-shape jam back. Shared with the event suite, which
        /// proves the jam the mechanism produces is reported with the shape that caused it.</summary>
        internal static MachineDef CutterOnADiet(ShapeFilter diet)
        {
            MachineDef shipped = ContentDatabase.Default.Machine(BuildKind.Cutter);
            return new MachineDef(shipped.Id, shipped.Build, shipped.Tile, shipped.Behavior, shipped.Cost,
                shipped.Name, shipped.Description, shipped.Interval, maxHp: shipped.MaxHp, diet: diet);
        }

        /// <summary>The shipped turret's row with a different cost and nothing else changed.</summary>
        private static MachineDef Turret(BuildKind build, int cost)
        {
            MachineDef shipped = ContentDatabase.Default.Machine(build);
            return new MachineDef(shipped.Id, shipped.Build, shipped.Tile, shipped.Behavior, cost,
                shipped.Name, shipped.Description, shipped.Interval);
        }

        /// <summary>The shipped sorter's row with a different filter. Shared with the sorter suite, which proves both filter modes route correctly.</summary>
        internal static MachineDef SorterRouting(ShapeFilter filter)
        {
            MachineDef shipped = ContentDatabase.Default.Machine(BuildKind.Sorter);
            return new MachineDef(shipped.Id, shipped.Build, shipped.Tile, shipped.Behavior, shipped.Cost,
                shipped.Name, shipped.Description, shipped.Interval, filter: filter, maxHp: shipped.MaxHp);
        }

        /// <summary>
        /// A copy of the shipped table with one or two rows swapped, built by reading
        /// <see cref="ContentDatabase.Default"/> rather than restating every number - so a test is
        /// explicit about what it changes and keeps passing when shipped numbers are retuned. Shared with
        /// the other suites that need a table of their own.
        /// </summary>
        internal static ContentDatabase Rewrite(MachineDef machine = null, TurretDef turret = null,
            EnemyDef enemy = null)
        {
            ContentDatabase shipped = ContentDatabase.Default;

            var machines = new MachineDef[shipped.BuildKinds.Length];
            for (int i = 0; i < shipped.BuildKinds.Length; i++)
                machines[i] = shipped.Machine(shipped.BuildKinds[i]);
            if (machine != null) machines[(int)machine.Build] = machine;

            var turrets = new TurretDef[shipped.BuildKinds.Length];
            for (int i = 0; i < shipped.BuildKinds.Length; i++)
                if (shipped.IsTurret(shipped.BuildKinds[i])) turrets[i] = shipped.Turret(shipped.BuildKinds[i]);
            if (turret != null) turrets[(int)turret.Build] = turret;

            var enemies = new EnemyDef[shipped.EnemyKinds.Length];
            for (int i = 0; i < shipped.EnemyKinds.Length; i++)
                enemies[i] = shipped.Enemy(shipped.EnemyKinds[i]);
            if (enemy != null) enemies[(int)enemy.Kind] = enemy;

            return new ContentDatabase(machines, turrets, enemies);
        }
    }
}
