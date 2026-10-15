using System;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The Editor carrier's conversion to the engine-free table. The property that matters is that it is
    /// <b>additive</b>: an asset with no rows is the shipped game, and a row changes exactly what it
    /// names and nothing else - which lets the shipped asset exist while the code defaults stay the
    /// source of truth for everything nobody has retuned.
    /// </summary>
    public class ContentDatabaseAssetTests
    {
        private ContentDatabaseAsset _asset;

        [SetUp]
        public void CreateAsset() => _asset = ScriptableObject.CreateInstance<ContentDatabaseAsset>();

        [TearDown]
        public void DestroyAsset()
        {
            if (_asset != null) UnityEngine.Object.DestroyImmediate(_asset);
        }

        [Test]
        public void AnEmptyAsset_IsTheShippedTable()
        {
            ContentDatabase table = _asset.ToCore();

            foreach (BuildKind kind in (BuildKind[])Enum.GetValues(typeof(BuildKind)))
            {
                Assert.AreEqual(ContentDatabase.Default.Machine(kind).Cost, table.Machine(kind).Cost, kind + " cost");
                Assert.AreEqual(ContentDatabase.Default.Machine(kind).Tile, table.Machine(kind).Tile, kind + " tile");
                Assert.AreEqual(ContentDatabase.Default.Machine(kind).Behavior, table.Machine(kind).Behavior, kind + " behaviour");
            }

            Assert.AreEqual(ContentDatabase.Default.Turret(BuildKind.Cannon).AmmoFor(ShapeType.HalfCircle).Damage,
                table.Turret(BuildKind.Cannon).AmmoFor(ShapeType.HalfCircle).Damage, Sim.Tol);
            Assert.AreEqual(ContentDatabase.Default.Enemy(EnemyKind.Spike).Hp,
                table.Enemy(EnemyKind.Spike).Hp, Sim.Tol);
            Assert.AreEqual(ContentDatabase.Default.Shape(ShapeType.HalfCircle).Glyph,
                table.Shape(ShapeType.HalfCircle).Glyph);
            Assert.AreEqual(ContentDatabase.Default.Recipe(ContentDatabase.DecomposeRecipeId).Interval,
                table.Recipe(ContentDatabase.DecomposeRecipeId).Interval, Sim.Tol);
        }

        [Test]
        public void ARow_RetunesOnlyWhatItNames()
        {
            _asset.Turrets = new[]
            {
                new ContentDatabaseAsset.TurretRow
                {
                    Build = BuildKind.Cannon, Name = "Heavy",
                    AmmoTypes = new[]
                    {
                        new ContentDatabaseAsset.AmmoEntry { Shape = ShapeType.Circle, Damage = 9f },
                    },
                    FireInterval = 0.5f, Range = 12f, ProjectileSpeed = 20f,
                },
            };

            ContentDatabase table = _asset.ToCore();

            Assert.AreEqual(9f, table.Turret(BuildKind.Cannon).AmmoFor(ShapeType.Circle).Damage, Sim.Tol);
            Assert.IsNull(table.Turret(BuildKind.Cannon).AmmoFor(ShapeType.HalfCircle),
                "a row that names a diet replaces the whole diet");

            // Everything the asset did not mention is still the shipped value.
            Assert.AreEqual(ContentDatabase.Default.Machine(BuildKind.Drill).Cost, table.Machine(BuildKind.Drill).Cost);
            Assert.AreEqual(ContentDatabase.Default.Enemy(EnemyKind.Spike).Hp, table.Enemy(EnemyKind.Spike).Hp, Sim.Tol);
        }

        [Test]
        public void ARecipeRow_CanRetuneTheShippedRecipe_OrAddANewOne()
        {
            _asset.Recipes = new[]
            {
                new ContentDatabaseAsset.RecipeRow
                {
                    Id = "decompose", Input = ShapeType.HalfCircle, Output = ShapeType.Circle,
                    InputCount = 1, OutputCount = 2, Interval = 1f, Buffer = 4,
                },
                new ContentDatabaseAsset.RecipeRow
                {
                    Id = "smelt", Input = ShapeType.Circle, Output = ShapeType.Circle,
                    InputCount = 2, OutputCount = 1, Interval = 3f, Buffer = 1,
                },
            };

            ContentDatabase table = _asset.ToCore();

            Assert.AreEqual(ShapeType.HalfCircle, table.Recipe(ContentDatabase.DecomposeRecipeId).Input,
                "the shipped recipe was retuned in place rather than duplicated");
            Assert.AreEqual(1f, table.Recipe(ContentDatabase.DecomposeRecipeId).Interval, Sim.Tol);
            Assert.AreEqual(2, table.Recipe(new ContentId("smelt")).InputCount, "and a new recipe was added");
        }

        [Test]
        public void AMachineRow_PointsAtARecipe_SoAConverterIsData()
        {
            _asset.Machines = new[]
            {
                new ContentDatabaseAsset.MachineRow
                {
                    Build = BuildKind.Decomposer, Tile = TileKind.Decomposer, Behavior = BehaviorKind.Converter,
                    Cost = 15, Name = "Un-splitter", Description = "", RecipeId = "smelt", Interval = 0f,
                },
            };
            _asset.Recipes = new[]
            {
                new ContentDatabaseAsset.RecipeRow
                {
                    Id = "smelt", Input = ShapeType.Circle, Output = ShapeType.HalfCircle,
                    InputCount = 1, OutputCount = 1, Interval = 2f, Buffer = 2,
                },
            };

            ContentDatabase table = _asset.ToCore();

            MachineDef machine = table.Machine(BuildKind.Decomposer);
            Assert.AreEqual(BehaviorKind.Converter, machine.Behavior);
            Assert.AreEqual("smelt", machine.RecipeId.Value);
            Assert.AreEqual(2f, table.Recipe(machine.RecipeId).Interval, Sim.Tol);
        }

        [Test]
        public void ARowKeyedById_LandsOnTheBuildingItNames_WhateverTheDropdownSays()
        {
            // The reason a row may carry an id: an enum value inserted above a building moves every later
            // ordinal, so an asset authored before the insert would retune its new neighbour. With the id
            // filled in, the row still lands on the building it names.
            // A machine row overrides its whole definition, defaults included, so the fields here are
            // whatever the slot's shipped row says apart from the cost.
            _asset.Machines = new[]
            {
                new ContentDatabaseAsset.MachineRow
                {
                    Id = "decomposer", Build = BuildKind.Cannon, Cost = 7,
                    Tile = TileKind.Decomposer, Behavior = BehaviorKind.Converter,
                    Name = "Decomposer", RecipeId = "decompose",
                },
            };

            ContentDatabase table = _asset.ToCore();

            Assert.AreEqual(7, table.Machine(BuildKind.Decomposer).Cost,
                "the id named the decomposer, and the id wins over the dropdown");
            Assert.AreEqual(ContentDatabase.Default.Machine(BuildKind.Cannon).Cost,
                table.Machine(BuildKind.Cannon).Cost, "and the cannon was left alone");
            Assert.AreEqual("decomposer", table.Machine(BuildKind.Decomposer).Id.Value);
        }

        [Test]
        public void ARowWithNoId_IsKeyedByItsDropdown()
        {
            // The other half of the rule, and the behaviour every row had before ids existed: a hand-added
            // row that names nothing is keyed by the dropdown. (The row describes a converter because that
            // is the slot it fills.)
            _asset.Machines = new[]
            {
                new ContentDatabaseAsset.MachineRow
                {
                    Build = BuildKind.Decomposer, Cost = 7,
                    Tile = TileKind.Decomposer, Behavior = BehaviorKind.Converter,
                    Name = "Decomposer", RecipeId = "decompose",
                },
            };

            ContentDatabase table = _asset.ToCore();

            Assert.AreEqual(7, table.Machine(BuildKind.Decomposer).Cost);
            Assert.AreEqual("decomposer", table.Machine(BuildKind.Decomposer).Id.Value,
                "the row ends up describing the slot's own identity");
        }

        [Test]
        public void ARowLeftAtZero_KeepsTheShippedHealth_AndReach()
        {
            // 0 in a row means "unset", not "indestructible", for the same additive reason every other
            // field is an override: an asset authored before buildings had health must not silently make
            // its rows invulnerable, and an enemy row must not lose its aggressive-scan reach.
            _asset.Machines = new[]
            {
                new ContentDatabaseAsset.MachineRow { Id = "wall", Cost = 3, Name = "Wall",
                    Tile = TileKind.Wall, Behavior = BehaviorKind.None },        // MaxHp left at 0
                new ContentDatabaseAsset.MachineRow { Id = "cannon", MaxHp = 40f },   // this one is retuned
            };
            _asset.Enemies = new[]
            {
                new ContentDatabaseAsset.EnemyRow { Id = "spike" },                   // AggroRange left at 0
            };

            ContentDatabase table = _asset.ToCore();

            Assert.AreEqual(3, table.Machine(BuildKind.Wall).Cost, "the row landed on the wall");
            Assert.AreEqual(120f, table.Machine(BuildKind.Wall).MaxHp, Sim.Tol,
                "0 on the row keeps the shipped health");
            Assert.AreEqual(40f, table.Machine(BuildKind.Cannon).MaxHp, Sim.Tol, "a retune does land");
            Assert.AreEqual(1.6f, table.Enemy(EnemyKind.Spike).AggroRange, Sim.Tol,
                "0 keeps the shipped reach");
            Assert.AreEqual(2.5f, table.Enemy(EnemyKind.Spike).DetectionRange, Sim.Tol,
                "and the shipped notice range");
        }

        [Test]
        public void EveryRow_IsTheInverseOfItsDefinition()
        {
            // FromDef and ToDef must invert each other. CreateContentDatabase writes an asset through one
            // and merge reads it back through the other, so a dropped field would make a refreshed asset
            // quietly disagree with the table it was copied from.
            ContentDatabase shipped = ContentDatabase.Default;

            foreach (BuildKind kind in shipped.BuildKinds)
            {
                MachineDef def = shipped.Machine(kind);
                MachineDef back = ContentDatabaseAsset.MachineRow.FromDef(def)
                    .ToDef(def.Id, def.Build, def.MaxHp);

                Assert.AreEqual(def.Id, back.Id, kind + " id");
                Assert.AreEqual(def.Build, back.Build, kind + " build");
                Assert.AreEqual(def.Tile, back.Tile, kind + " tile");
                Assert.AreEqual(def.Behavior, back.Behavior, kind + " behaviour");
                Assert.AreEqual(def.Cost, back.Cost, kind + " cost");
                Assert.AreEqual(def.Name, back.Name, kind + " name");
                Assert.AreEqual(def.Description, back.Description, kind + " description");
                Assert.AreEqual(def.RecipeId, back.RecipeId, kind + " recipe");
                Assert.AreEqual(def.Interval, back.Interval, Sim.Tol, kind + " interval");
                Assert.AreEqual(def.Filter, back.Filter, kind + " filter");
                Assert.AreEqual(def.MaxHp, back.MaxHp, Sim.Tol, kind + " max hp");

                if (!shipped.IsTurret(kind)) continue;

                TurretDef gun = shipped.Turret(kind);
                TurretDef gunBack = ContentDatabaseAsset.TurretRow
                    .FromDef(def.Id, gun).ToDef(gun.Build, gun.AmmoTypes, gun.SplashRadius);

                Assert.AreEqual(def.Id.Value,
                    ContentDatabaseAsset.TurretRow.FromDef(def.Id, gun).Id, kind + " turret row id");
                Assert.AreEqual(gun.AmmoTypes.Length, gunBack.AmmoTypes.Length, kind + " diet size");
                for (int a = 0; a < gun.AmmoTypes.Length; a++)
                {
                    Assert.AreEqual(gun.AmmoTypes[a].Shape, gunBack.AmmoTypes[a].Shape, kind + " ammo shape " + a);
                    Assert.AreEqual(gun.AmmoTypes[a].Damage, gunBack.AmmoTypes[a].Damage, Sim.Tol,
                        kind + " ammo damage " + a);
                }
                Assert.AreEqual(gun.FireInterval, gunBack.FireInterval, Sim.Tol, kind + " fire interval");
                Assert.AreEqual(gun.Range, gunBack.Range, Sim.Tol, kind + " range");
                Assert.AreEqual(gun.ProjectileSpeed, gunBack.ProjectileSpeed, Sim.Tol, kind + " projectile speed");
                Assert.AreEqual(gun.SplashRadius, gunBack.SplashRadius, Sim.Tol, kind + " splash radius");
            }

            foreach (EnemyKind kind in shipped.EnemyKinds)
            {
                EnemyDef def = shipped.Enemy(kind);
                EnemyDef back = ContentDatabaseAsset.EnemyRow.FromDef(def)
                    .ToDef(def.Id, def.Kind, def.AggroRange, def.DetectionRange);

                Assert.AreEqual(def.Id, back.Id, kind + " id");
                Assert.AreEqual(def.Kind, back.Kind, kind + " kind");
                Assert.AreEqual(def.Name, back.Name, kind + " name");
                Assert.AreEqual(def.Hp, back.Hp, Sim.Tol, kind + " hp");
                Assert.AreEqual(def.Speed, back.Speed, Sim.Tol, kind + " speed");
                Assert.AreEqual(def.Damage, back.Damage, Sim.Tol, kind + " damage");
                Assert.AreEqual(def.AttackInterval, back.AttackInterval, Sim.Tol, kind + " attack interval");
                Assert.AreEqual(def.Weakness, back.Weakness, kind + " weakness");
                Assert.AreEqual(def.AggroRange, back.AggroRange, Sim.Tol, kind + " aggro range");
                Assert.AreEqual(def.DetectionRange, back.DetectionRange, Sim.Tol, kind + " detection range");
            }

            foreach (RecipeDef recipe in shipped.Recipes)
            {
                RecipeDef back = ContentDatabaseAsset.RecipeRow.FromDef(recipe).ToDef();

                Assert.AreEqual(recipe.Id, back.Id, recipe.Id.Value + " id");
                Assert.AreEqual(recipe.Input, back.Input, recipe.Id.Value + " input");
                Assert.AreEqual(recipe.Output, back.Output, recipe.Id.Value + " output");
                Assert.AreEqual(recipe.InputCount, back.InputCount, recipe.Id.Value + " input count");
                Assert.AreEqual(recipe.OutputCount, back.OutputCount, recipe.Id.Value + " output count");
                Assert.AreEqual(recipe.Interval, back.Interval, Sim.Tol, recipe.Id.Value + " interval");
                Assert.AreEqual(recipe.Buffer, back.Buffer, recipe.Id.Value + " buffer");
            }

            foreach (ShapeType shape in (ShapeType[])Enum.GetValues(typeof(ShapeType)))
            {
                ShapeDef def = shipped.Shape(shape);
                ShapeDef back = ContentDatabaseAsset.ShapeRow.FromDef(def).ToDef(def.Id);

                Assert.AreEqual(def.Id, back.Id, shape + " id");
                Assert.AreEqual(def.Shape, back.Shape, shape + " shape");
                Assert.AreEqual(def.Name, back.Name, shape + " name");
                Assert.AreEqual(def.Glyph, back.Glyph, shape + " glyph");
            }
        }
    }
}
