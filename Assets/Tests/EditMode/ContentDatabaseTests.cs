using System;
using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The content table, unit-tested where the shipped numbers used to be a pile of statics. "What
    /// exists" is data: every kind the enums name must have a row, and a lookup for something that is
    /// not a turret must fail loudly rather than quietly answering "turret". The old <see cref="Balance"/>
    /// facade over this table is gone - the pins below read <see cref="Default"/> directly.
    /// </summary>
    public class ContentDatabaseTests
    {
        private static ContentDatabase Content => ContentDatabase.Default;

        [Test]
        public void EveryBuildKind_HasAMachineDefinition()
        {
            foreach (BuildKind kind in (BuildKind[])Enum.GetValues(typeof(BuildKind)))
            {
                MachineDef def = Content.Machine(kind);
                Assert.IsNotNull(def, "no definition for " + kind);
                Assert.AreEqual(kind, def.Build);
                Assert.IsFalse(string.IsNullOrEmpty(def.Name), kind + " has no name to print");

                // A sorter's whole job is the filter, so a row naming the behaviour without one would
                // route nothing anywhere - a data error worth catching here rather than in a player's line.
                if (def.Behavior == BehaviorKind.Sorter)
                    Assert.IsTrue(def.Filter.IsShape(), kind + " is a sorter with no filter shape");
            }
        }

        [Test]
        public void TheShippedSorter_SendsCirclesTheWayItFaces()
        {
            // The shipped filter is the money shape, the split an opening wants: face the Core and the
            // circles bank while everything else carries on down the line.
            MachineDef sorter = Content.Machine(BuildKind.Sorter);

            Assert.AreEqual(BehaviorKind.Sorter, sorter.Behavior);
            Assert.AreEqual(TileKind.Sorter, sorter.Tile);
            Assert.AreEqual(ShapeType.Circle, sorter.Filter);
        }

        [Test]
        public void EveryEnemyKind_AndEveryShape_HasADefinition()
        {
            foreach (EnemyKind kind in (EnemyKind[])Enum.GetValues(typeof(EnemyKind)))
            {
                EnemyDef def = Content.Enemy(kind);
                Assert.AreEqual(kind, def.Kind);
                Assert.IsTrue(def.Weakness.IsShape(), kind + " has no weakness shape");
            }

            foreach (ShapeType shape in (ShapeType[])Enum.GetValues(typeof(ShapeType)))
            {
                ShapeDef def = Content.Shape(shape);
                Assert.AreEqual(shape, def.Shape);
                Assert.IsFalse(string.IsNullOrEmpty(def.Glyph), shape + " has no glyph");
            }
        }

        [Test]
        public void Turrets_AreFoundByIdentity_NotByOrdinalArithmetic()
        {
            Assert.IsTrue(Content.IsTurret(BuildKind.Cannon));
            Assert.AreEqual(BuildKind.Cannon, Content.Turret(BuildKind.Cannon).Build);
            Assert.IsTrue(Content.IsTurret(BuildKind.Mortar), "the second gun is a turret too");
            Assert.AreEqual(BuildKind.Mortar, Content.Turret(BuildKind.Mortar).Build);

            // The old convention answered "turret" for anything it did not recognise, and read a spec by
            // subtracting Cannon's ordinal. Neither is possible now: a non-turret is a hard failure, and
            // which kinds are turrets comes from the table, not a list of names.
            foreach (BuildKind kind in (BuildKind[])Enum.GetValues(typeof(BuildKind)))
            {
                bool expected = kind == BuildKind.Cannon || kind == BuildKind.Mortar;
                Assert.AreEqual(expected, Content.IsTurret(kind), kind + " turret-ness");
                if (!expected) Assert.Throws<ArgumentOutOfRangeException>(() => Content.Turret(kind));
            }
        }

        [Test]
        public void UnknownLookups_Throw_InsteadOfReturningADefault()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Content.Recipe(new ContentId("no-such-recipe")));
            Assert.Throws<ArgumentOutOfRangeException>(() => Content.Machine((BuildKind)200));
            Assert.Throws<ArgumentOutOfRangeException>(() => Content.Enemy((EnemyKind)200));
        }

        [Test]
        public void MachineTile_IsTheOneTheBuildCatalogReports()
        {
            foreach (BuildKind kind in (BuildKind[])Enum.GetValues(typeof(BuildKind)))
                Assert.AreEqual(Content.Machine(kind).Tile, BuildCatalog.TileFor(kind), kind.ToString());
        }

        [Test]
        public void DecomposeRecipe_IsOneCircleIntoTwoHalves()
        {
            RecipeDef recipe = Content.Recipe(ContentDatabase.DecomposeRecipeId);

            Assert.AreEqual(ShapeType.Circle, recipe.Input);
            Assert.AreEqual(1, recipe.InputCount);
            Assert.AreEqual(ShapeType.HalfCircle, recipe.Output);
            Assert.AreEqual(2, recipe.OutputCount);
            Assert.AreEqual(1.5f, recipe.Interval, Sim.Tol);
            Assert.AreEqual(4, recipe.Buffer);

            // The converter names no recipe - it runs them all, the delivered shape picking - and the
            // drill mines an interval, not a recipe.
            Assert.IsTrue(Content.Machine(BuildKind.Decomposer).RecipeId.IsNone);
            Assert.AreSame(recipe,
                Content.RecipeForInput(Content.Machine(BuildKind.Decomposer), ShapeType.Circle));
            Assert.AreEqual(BehaviorKind.Converter, Content.Machine(BuildKind.Decomposer).Behavior);
            Assert.AreEqual(BehaviorKind.Drill, Content.Machine(BuildKind.Drill).Behavior);
            Assert.AreEqual(BehaviorKind.None, Content.Machine(BuildKind.Belt).Behavior,
                "a belt is not a machine behaviour; it lives on the belt layer");
        }

        [Test]
        public void ShippedNumbers_AreTheOnesTheDesignWasBalancedOn()
        {
            // Pinned so a refactor cannot quietly retune the prototype: these are the exact values the
            // tests' timing (30 ticks per item, 1.5 s per split) was derived from, read straight from the
            // table now that the Balance facade is gone.
            Assert.AreEqual(1f, Content.Machine(BuildKind.Drill).Interval, Sim.Tol);
            Assert.AreEqual(1.5f, Content.Recipe(ContentDatabase.DecomposeRecipeId).Interval, Sim.Tol);
            Assert.AreEqual(0.25f, Content.Machine(BuildKind.Pipe).Interval, Sim.Tol);
            Assert.AreEqual(2, Content.Recipe(ContentDatabase.DecomposeRecipeId).OutputCount);
            Assert.AreEqual(4, Content.Recipe(ContentDatabase.DecomposeRecipeId).Buffer);

            Assert.AreEqual(1, Content.Machine(BuildKind.Belt).Cost);
            Assert.AreEqual(10, Content.Machine(BuildKind.Drill).Cost);
            Assert.AreEqual(15, Content.Machine(BuildKind.Decomposer).Cost);
            Assert.AreEqual(6, Content.Machine(BuildKind.Pipe).Cost);
            Assert.AreEqual(10, Content.Machine(BuildKind.Splitter).Cost);
            Assert.AreEqual(20, Content.Machine(BuildKind.Cannon).Cost);
            Assert.AreEqual(12, Content.Machine(BuildKind.Sorter).Cost);
            Assert.AreEqual(30, Content.Machine(BuildKind.Mortar).Cost);
            Assert.AreEqual(2, Content.Machine(BuildKind.Wall).Cost);

            // Health is content now: the wall is the pool the siege is balanced against (120 hp is twenty
            // seconds of chewing for one Spike), and the belt's 0 is what says it is walkable and
            // untouchable rather than a machine with no health.
            Assert.AreEqual(120f, Content.Machine(BuildKind.Wall).MaxHp, Sim.Tol);
            Assert.AreEqual(80f, Content.Machine(BuildKind.Cannon).MaxHp, Sim.Tol);
            Assert.AreEqual(0f, Content.Machine(BuildKind.Belt).MaxHp, Sim.Tol,
                "a belt is not a machine and has no health to chew");

            TurretDef cannon = Content.Turret(BuildKind.Cannon);
            Assert.AreEqual(ShapeType.HalfCircle, cannon.PrimaryAmmo);
            Assert.AreEqual(1f, cannon.FireInterval, Sim.Tol);
            Assert.AreEqual(3f, cannon.AmmoFor(ShapeType.HalfCircle).Damage, Sim.Tol);
            Assert.AreEqual(7f, cannon.Range, Sim.Tol);
            Assert.AreEqual(14f, cannon.ProjectileSpeed, Sim.Tol);

            // Both guns also eat the composite: twice the cannon's base damage, never resisted, at a
            // chain two minerals and an assembler deep.
            Assert.AreEqual(6f, cannon.AmmoFor(ShapeType.Composite).Damage, Sim.Tol);

            // The two guns are a pair, not a ladder: the mortar fires two-thirds as often for twice the
            // damage, and its shell bursts over an area where the cannon's shot answers one enemy.
            TurretDef mortar = Content.Turret(BuildKind.Mortar);
            Assert.AreEqual(ShapeType.HalfSquare, mortar.PrimaryAmmo);
            Assert.AreEqual(1.5f, mortar.FireInterval, Sim.Tol);
            Assert.AreEqual(cannon.AmmoFor(ShapeType.HalfCircle).Damage * 2f,
                mortar.AmmoFor(ShapeType.HalfSquare).Damage, Sim.Tol);
            Assert.AreEqual(10f, mortar.AmmoFor(ShapeType.Composite).Damage, Sim.Tol);
            Assert.AreEqual(cannon.FireInterval * 1.5f, mortar.FireInterval, Sim.Tol);
            Assert.Greater(mortar.Range, cannon.Range, "the mortar reaches further");
            Assert.AreEqual(0f, cannon.SplashRadius, Sim.Tol, "the cannon is the single-target gun");
            Assert.Greater(mortar.SplashRadius, 0f, "and the mortar is the area one - the role, not a stat");

            EnemyDef spike = Content.Enemy(EnemyKind.Spike);
            Assert.AreEqual(6f, spike.Hp, Sim.Tol);
            Assert.AreEqual(1.5f, spike.Speed, Sim.Tol);
            Assert.AreEqual(6f, spike.Damage, Sim.Tol);
            Assert.AreEqual(ShapeType.HalfCircle, spike.Weakness);
            Assert.AreEqual(1.6f, spike.AggroRange, Sim.Tol, "the aggressive scan's reach");
            Assert.AreEqual(2.5f, spike.DetectionRange, Sim.Tol, "and the range it charges from");
        }

        [Test]
        public void BisectRecipe_IsOneSquareIntoTwoHalves()
        {
            // The second chain is the first one's shape applied to the other mineral - a row, not a
            // machine: the one decomposer runs both recipes, chosen by what is fed to it. Pinned so the
            // two cannot drift apart.
            RecipeDef decom = Content.Recipe(ContentDatabase.DecomposeRecipeId);
            RecipeDef bisect = Content.Recipe(ContentDatabase.BisectRecipeId);

            Assert.AreEqual(ShapeType.Square, bisect.Input);
            Assert.AreEqual(ShapeType.HalfSquare, bisect.Output);
            Assert.AreEqual(decom.InputCount, bisect.InputCount);
            Assert.AreEqual(decom.OutputCount, bisect.OutputCount);
            Assert.AreEqual(decom.Interval, bisect.Interval, Sim.Tol);
            Assert.AreEqual(decom.Buffer, bisect.Buffer);

            // One converter, both recipes: the decomposer names no recipe and the delivered shape picks.
            MachineDef decomposer = Content.Machine(BuildKind.Decomposer);
            Assert.AreEqual(BehaviorKind.Converter, decomposer.Behavior);
            Assert.IsTrue(decomposer.RecipeId.IsNone,
                "the decomposer runs every recipe in the table - there is no second splitter machine");
            Assert.AreSame(decom, Content.RecipeForInput(decomposer, ShapeType.Circle));
            Assert.AreSame(bisect, Content.RecipeForInput(decomposer, ShapeType.Square));
            Assert.IsNull(Content.RecipeForInput(decomposer, ShapeType.HalfCircle),
                "a split shape has no recipe - feeding one back is the wrong-shape jam");

            // And the minerals are distinct, or the two chains would be one line drawn twice.
            Assert.AreNotEqual(decom.Input, bisect.Input);
            Assert.AreNotEqual(decom.Output, bisect.Output);
        }

        [Test]
        public void ATableIsNotASingleton_ACustomOneIsHonoured()
        {
            // The seam the whole layer exists for: a world can be handed a different table and get a
            // different, reproducible game. Here the cannon costs more and hits harder.
            var machines = new[]
            {
                new MachineDef(new ContentId("cannon"), BuildKind.Cannon, TileKind.Turret,
                    BehaviorKind.Turret, cost: 99, name: "Siege", description: "",
                    recipeId: ContentId.None, interval: 0f),
            };
            var turrets = new[]
            {
                new TurretDef(BuildKind.Cannon, "Siege", new[] { new AmmoDef(ShapeType.HalfCircle, 9f) },
                    fireInterval: 0.5f, range: 12f, projectileSpeed: 20f),
            };
            var enemies = new[]
            {
                new EnemyDef(new ContentId("spike"), EnemyKind.Spike, "Brute", hp: 40f, speed: 1f,
                    damage: 20f, attackInterval: 2f, weakness: ShapeType.Circle),
            };
            var shapes = new[] { new ShapeDef(ShapeType.Circle, "circle", "Circle", "○") };
            var recipes = new RecipeDef[0];

            var custom = new ContentDatabase(machines, turrets, enemies, shapes, recipes);

            Assert.AreEqual(99, custom.Machine(BuildKind.Cannon).Cost);
            Assert.AreEqual(9f, custom.Turret(BuildKind.Cannon).AmmoFor(ShapeType.HalfCircle).Damage, Sim.Tol);
            Assert.AreEqual(ShapeType.Circle, custom.Enemy(EnemyKind.Spike).Weakness);

            Assert.Throws<ArgumentOutOfRangeException>(() => custom.Machine(BuildKind.Drill),
                "a table that defines only a cannon has no drill to report");

            // And the shipped table is untouched by any of it.
            Assert.AreEqual(20, ContentDatabase.Default.Machine(BuildKind.Cannon).Cost);
        }

        [Test]
        public void EveryDefinition_CarriesAStableId_ThatResolvesBackToIt()
        {
            // Content identity is a string, not an enum ordinal: an ordinal is an implementation detail
            // that shifts the moment a value is inserted, the failure mode the content ids exist to
            // remove. So every definition has one, and looking it up finds the definition again.
            var seen = new HashSet<string>();

            foreach (BuildKind kind in ContentDatabase.Default.BuildKinds)
            {
                MachineDef machine = ContentDatabase.Default.Machine(kind);

                Assert.IsFalse(machine.Id.IsNone, kind + " has an id");
                Assert.IsTrue(seen.Add(machine.Id.Value), kind + " reuses the id " + machine.Id.Value);
                Assert.AreSame(machine, ContentDatabase.Default.Machine(machine.Id), kind + " resolves by id");
                Assert.AreEqual((int)kind, ContentDatabase.Default.MachineSlot(machine.Id), kind + " slot");

                if (!ContentDatabase.Default.IsTurret(kind)) continue;

                // A turret's identity is its machine's: the gun reports the machine it hangs off, and
                // that machine is the one the id resolves to.
                Assert.AreSame(machine, ContentDatabase.Default.Machine(ContentDatabase.Default.Turret(kind).Build),
                    kind + "'s turret row names the machine that carries its id");
            }

            seen.Clear();
            foreach (EnemyKind kind in ContentDatabase.Default.EnemyKinds)
            {
                EnemyDef enemy = ContentDatabase.Default.Enemy(kind);

                Assert.IsFalse(enemy.Id.IsNone, kind + " has an id");
                Assert.IsTrue(seen.Add(enemy.Id.Value), kind + " reuses the id " + enemy.Id.Value);
                Assert.AreSame(enemy, ContentDatabase.Default.Enemy(enemy.Id), kind + " resolves by id");
                Assert.AreEqual((int)kind, ContentDatabase.Default.EnemySlot(enemy.Id), kind + " slot");
            }

            Assert.AreEqual(-1, ContentDatabase.Default.MachineSlot(new ContentId("nothing")),
                "an id nothing carries resolves to no slot rather than to one");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ContentDatabase.Default.Enemy(new ContentId("nothing")));
        }
    }
}
