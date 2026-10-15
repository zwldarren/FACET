using System;
using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The Phase-1 promise, tested end to end: the machine behaviours read their numbers from the
    /// <see cref="ContentDatabase"/> the world was handed, so a different table is a different game - a
    /// converter that eats half-circles, a turret that eats circles, an enemy with different stats - with
    /// no switch anywhere edited. This is what "add content = fill in data" has to mean.
    /// </summary>
    public class CustomContentTests
    {
        [Test]
        public void AConverter_RunsWhateverRecipeItsDefinitionNames()
        {
            // The shipped decomposer eats circles. Hand it the mirror recipe: eat a half-circle, put out
            // two circles. Same behaviour, same code path, opposite machine.
            var mirrored = new RecipeDef(ContentDatabase.DecomposeRecipeId, ShapeType.HalfCircle,
                ShapeType.Circle, inputCount: 1, outputCount: 2, interval: 1f, buffer: 4);

            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig(), Rewrite(recipe: mirrored));

            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);                 // input belt
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);                 // output belt

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), ShapeType.HalfCircle));
            Sim.Tick(world, 90);

            Assert.AreEqual(ShapeType.Circle, Sim.ShapeAt(world, new Int2(5, 2)),
                "the converter produced its recipe's output, not the decomposer's hardcoded one");
            Assert.IsFalse(world.Belts.IsJammed(new Int2(3, 2)),
                "its recipe's input is accepted, so nothing jams");
        }

        [Test]
        public void TheSameConversion_WithTheShippedTable_StillJams()
        {
            // The control for the test above: identical layout and identical delivery, shipped table.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), ShapeType.HalfCircle));
            Sim.Tick(world, 90);

            Assert.IsTrue(world.Belts.IsJammed(new Int2(3, 2)),
                "the shipped decomposer eats circles, so a half-circle is a wrong-shape delivery");
        }

        [Test]
        public void ATurret_EatsItsDefinitionsDiet_AndTheEnemysDefinitionSetsThePrice()
        {
            // A circle-fed turret against a 3-HP spike: one shot, not the shipped two.
            var rockThrower = new TurretDef(BuildKind.Cannon, "Rock Thrower",
                new[] { new AmmoDef(ShapeType.Circle, 3f) },
                fireInterval: 1f, range: 7f, projectileSpeed: 14f);
            var fragile = new EnemyDef(new ContentId("spike"), EnemyKind.Spike, "Fragile", hp: 3f, speed: 1f,
                damage: 6f, attackInterval: 1f, weakness: ShapeType.Circle);

            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig { CoreMaxHp = 1000000f },
                Rewrite(turret: rockThrower, enemy: fragile));

            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);                 // a circle line into the turret
            Sim.Place(world, BuildKind.Cannon, new Int2(4, 2), Dir.East);

            int id = world.Enemies.Spawn(EnemyKind.Spike, new Vec2(4.5f, 5f));
            Assert.Greater(id, 0);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), ShapeType.Circle));
            Sim.Tick(world, 120);

            Assert.Greater(world.ShotsFired, 0, "the turret's diet is a circle, so the circle line fed it");
            Assert.AreEqual(0, world.Enemies.AliveCount, "and one 3-damage shot killed the 3-HP enemy");
        }

        [Test]
        public void ATableThatChangesCosts_ChangesWhatCanBeAfforded()
        {
            // The economy reads the injected table too, so a retuned cost is a real constraint, not just a
            // HUD string.
            var dearCannon = new MachineDef(new ContentId("cannon"), BuildKind.Cannon, TileKind.Turret,
                BehaviorKind.Turret, cost: Sim.TestCircles + 1, name: "Cannon", description: "",
                recipeId: ContentId.None, interval: 0f);

            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig(), Rewrite(machine: dearCannon));

            Assert.AreEqual(Sim.TestCircles + 1, world.Content.Machine(BuildKind.Cannon).Cost);
            Assert.IsFalse(world.Economy.CanAfford(BuildKind.Cannon),
                "the start stockpile cannot cover the retuned cost");
            Assert.IsFalse(world.TryPlace(BuildKind.Cannon, new Int2(2, 2), Dir.East));

            Assert.AreEqual(20, ContentDatabase.Default.Machine(BuildKind.Cannon).Cost,
                "the shipped table is not affected by a table one world was handed");
            Assert.IsTrue(Sim.NewWorld(20, 12).Economy.CanAfford(BuildKind.Cannon));
        }

        [Test]
        public void ASorter_FiltersWhateverItsDefinitionNames()
        {
            // Mirror the shipped sorter: send half-circles out the facing side instead of circles. Same
            // behaviour, same code path, opposite machine - a row, not a class.
            var mirrored = new MachineDef(new ContentId("sorter"), BuildKind.Sorter, TileKind.Sorter,
                BehaviorKind.Sorter, cost: 12, name: "Ammo Router", description: "",
                recipeId: ContentId.None, interval: 0f, filter: ShapeType.HalfCircle);

            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig(), Rewrite(machine: mirrored));

            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);       // input
            Sim.Place(world, BuildKind.Sorter, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);       // the facing outlet
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 1);      // the other outlet

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), ShapeType.HalfCircle));
            Sim.Tick(world, 90);

            Assert.AreEqual(ShapeType.HalfCircle, Sim.ShapeAt(world, new Int2(5, 2)),
                "the retuned filter sent the half-circle out the side it faces");
            Assert.AreEqual(ShapeType.None, Sim.ShapeAt(world, new Int2(4, 3)),
                "and the other outlet stayed empty");
        }

        /// <summary>
        /// A copy of the shipped table with one or two rows swapped, built by reading
        /// <see cref="ContentDatabase.Default"/> rather than restating every number - so a test is
        /// explicit about what it changes and keeps passing when shipped numbers are retuned. Shared with
        /// the other suites that need a table of their own.
        /// </summary>
        internal static ContentDatabase Rewrite(MachineDef machine = null, RecipeDef recipe = null,
            TurretDef turret = null, EnemyDef enemy = null, RecipeDef extraRecipe = null)
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

            var shapes = new ShapeDef[Enum.GetValues(typeof(ShapeType)).Length];
            foreach (ShapeType shape in (ShapeType[])Enum.GetValues(typeof(ShapeType)))
                shapes[(int)shape] = shipped.Shape(shape);

            // Every shipped recipe is carried, plus an optional extra row: the decomposer names no recipe
            // and runs the whole table, so a table missing one would quietly halve its diet rather than
            // fail at construction. The extra row is how a suite gives two recipes a shared input.
            var recipes = new List<RecipeDef>
            {
                recipe ?? shipped.Recipe(ContentDatabase.DecomposeRecipeId),
                shipped.Recipe(ContentDatabase.BisectRecipeId),
                shipped.Recipe(ContentDatabase.CombineRecipeId),
            };
            if (extraRecipe != null) recipes.Add(extraRecipe);

            return new ContentDatabase(machines, turrets, enemies, shapes, recipes.ToArray());
        }
    }
}
