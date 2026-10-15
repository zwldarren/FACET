using System;
using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

// Core's shape-filter mode collides with UnityEngine.FilterMode (the texture filter), and this file is one
// of the two places the two namespaces meet, so the alias is made here rather than at every use below.
using FilterMode = Facet.Core.FilterMode;

namespace Facet.Tests
{
    /// <summary>
    /// The Editor carrier's conversion to the engine-free table. The property that matters is that it is
    /// <b>additive</b>: an asset with no rows is the shipped game, and a row changes exactly what it
    /// names and nothing else - which lets the shipped asset exist while the code defaults stay the
    /// source of truth for everything nobody has retuned.
    ///
    /// Two of the fields a row can name are <em>shapes</em> now - what a machine eats, and what a sorter
    /// routes for - and they are the ones most likely to be lost on the way across the boundary, since
    /// they are the only fields that are not a number, an enum or a string. Both are pinned below.
    /// </summary>
    public class ContentDatabaseAssetTests
    {
        private ContentDatabaseAsset _asset;

        /// <summary>
        /// A row for the machine already in that slot, with one thing changed - which is how this asset is
        /// meant to be used. A machine row describes a <em>whole</em> definition (its Tile and Behaviour are
        /// taken as authored, exactly as before the rework), so the safe way to retune one number is to start
        /// from the shipped row rather than from a blank one whose dropdowns name a turret.
        /// </summary>
        private static ContentDatabaseAsset.MachineRow Retune(BuildKind slot,
            Action<ContentDatabaseAsset.MachineRow> tune)
        {
            ContentDatabaseAsset.MachineRow row =
                ContentDatabaseAsset.MachineRow.FromDef(ContentDatabase.Default.Machine(slot));
            tune(row);
            return row;
        }

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
                Assert.AreEqual(ContentDatabase.Default.Machine(kind).Filter.Describe(),
                    table.Machine(kind).Filter.Describe(), kind + " filter");
                Assert.AreEqual(ContentDatabase.Default.Machine(kind).Diet.Describe(),
                    table.Machine(kind).Diet.Describe(), kind + " diet");
            }

            Assert.AreEqual(ContentDatabase.Default.Turret(BuildKind.Turret).Damage,
                table.Turret(BuildKind.Turret).Damage, Sim.Tol);
            Assert.AreEqual(ContentDatabase.Default.Enemy(EnemyKind.Runner).Hp,
                table.Enemy(EnemyKind.Runner).Hp, Sim.Tol);
        }

        [Test]
        public void ARow_RetunesOnlyWhatItNames()
        {
            _asset.Turrets = new[]
            {
                new ContentDatabaseAsset.TurretRow
                {
                    Build = BuildKind.Turret, Name = "Heavy",
                    Damage = 9f, FireInterval = 0.5f, Range = 12f, ProjectileSpeed = 20f,
                },
            };

            ContentDatabase table = _asset.ToCore();

            Assert.AreEqual(9f, table.Turret(BuildKind.Turret).Damage, Sim.Tol);
            Assert.AreEqual(0.5f, table.Turret(BuildKind.Turret).FireInterval, Sim.Tol, "the row's rate landed");
            Assert.IsFalse(table.IsTurret(BuildKind.Cutter), "and only the gun was retuned");

            // Everything the asset did not mention is still the shipped value.
            Assert.AreEqual(ContentDatabase.Default.Machine(BuildKind.Drill).Cost, table.Machine(BuildKind.Drill).Cost);
            Assert.AreEqual(ContentDatabase.Default.Enemy(EnemyKind.Runner).Hp, table.Enemy(EnemyKind.Runner).Hp, Sim.Tol);
        }

        [Test]
        public void AMachineRow_CanNarrowItsDiet_WhichIsWhatMakesTheJamRuleContent()
        {
            // The one shipped machine that eats everything is the shipped rule, not the only rule: a table
            // may declare a machine that refuses things, and the belt that hands it one jams. Before the
            // rework that was a recipe table; it is a filter now.
            _asset.Machines = new[]
            {
                Retune(BuildKind.Drill, row =>
                {
                    row.OverrideDiet = true;
                    row.Diet = new ShapeFilterField { Mode = FilterMode.Mineral, Mineral = Quad.Circle };
                }),
            };

            ContentDatabase table = _asset.ToCore();
            MachineDef drill = table.Machine(BuildKind.Drill);

            Assert.IsTrue(drill.Diet.Matches(Shape.Full(Quad.Circle)), "it still eats circles");
            Assert.IsFalse(drill.Diet.Matches(Shape.Full(Quad.Square)), "and now refuses squares");
            Assert.IsTrue(ContentDatabase.Default.Machine(BuildKind.Drill).Diet.Matches(Shape.Full(Quad.Square)),
                "where the shipped diet eats anything at all");
        }

        [Test]
        public void AMachineRow_CanRetuneASortersFilter_ExactOrByMineral()
        {
            var topHalfCircle = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty);

            _asset.Machines = new[]
            {
                Retune(BuildKind.Sorter, row => row.Filter = new ShapeFilterField
                {
                    Mode = FilterMode.Exact,
                    Shape = new ShapeField(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty),
                }),
            };

            ContentDatabase table = _asset.ToCore();
            ShapeFilter filter = table.Machine(BuildKind.Sorter).Filter;

            Assert.IsTrue(filter.IsExact, "the row's exact mode crossed the boundary");
            Assert.IsTrue(filter.Matches(topHalfCircle), "and it matches the item it names");
            Assert.IsFalse(filter.Matches(Shape.Full(Quad.Circle)),
                "and only that item: an exact filter is orientation-specific");

            // And the other half of the name: a mineral-mode row's payload is a flat field rather than a
            // nested one, so it is the copy most likely to be dropped on the way across the boundary.
            ContentDatabaseAsset.MachineRow mineral =
                Retune(BuildKind.Sorter, row => row.Filter = new ShapeFilterField
                {
                    Mode = FilterMode.Mineral,
                    Mineral = Quad.Square,
                });

            _asset.Machines = new[] { mineral };
            ShapeFilter byMineral = _asset.ToCore().Machine(BuildKind.Sorter).Filter;

            Assert.AreEqual(FilterMode.Mineral, byMineral.Mode, "the row's mineral mode landed");
            Assert.IsTrue(byMineral.Matches(Shape.Full(Quad.Square)), "and it matches the mineral it names");
            Assert.IsFalse(byMineral.Matches(Shape.Full(Quad.Circle)), "and only that mineral");
        }

        [Test]
        public void AMachineRow_CanDeclareThatItEatsNothing_AndItSurvivesTheRoundTrip()
        {
            // The extreme case of the jam rule: a machine that refuses everything. It has to survive
            // FromDef/ToCore, and the reason it needs a test of its own is that "eats nothing" is exactly the
            // value a naive "unset keeps the shipped" rule reads as "unset" - an authored rule silently
            // inverted on the next refresh, which no other field can do.
            ContentDatabaseAsset.MachineRow row = Retune(BuildKind.Cutter, tune =>
            {
                tune.OverrideDiet = true;
                tune.Diet = new ShapeFilterField { Mode = FilterMode.None };
            });

            Assert.IsTrue(row.OverrideDiet, "the row records that it means the diet");

            _asset.Machines = new[] { row };
            MachineDef cutter = _asset.ToCore().Machine(BuildKind.Cutter);

            Assert.IsTrue(cutter.Diet.IsNone, "a machine that eats nothing still eats nothing after a round trip");
            Assert.IsFalse(cutter.Diet.Matches(Shape.Full(Quad.Circle)), "so nothing is ever accepted");
        }

        [Test]
        public void ATurretRow_ThatNamesOneNumber_KeepsTheShippedOthers()
        {
            // A gun row is a whole definition like every other row, so the additive rule has to hold field by
            // field: a row that retunes the fire rate and stops there must not silently reset the damage, the
            // reach and the projectile speed to whatever the row's own defaults happen to be.
            _asset.Turrets = new[]
            {
                new ContentDatabaseAsset.TurretRow { Name = "Turret", FireInterval = 0.25f },
            };

            TurretDef shipped = ContentDatabase.Default.Turret(BuildKind.Turret);
            TurretDef gun = _asset.ToCore().Turret(BuildKind.Turret);

            Assert.AreEqual(0.25f, gun.FireInterval, Sim.Tol, "the row's rate landed");
            Assert.AreEqual(shipped.Damage, gun.Damage, Sim.Tol, "and the damage is the shipped one");
            Assert.AreEqual(shipped.Range, gun.Range, Sim.Tol, "and the reach");
            Assert.AreEqual(shipped.ProjectileSpeed, gun.ProjectileSpeed, Sim.Tol, "and the speed");
        }

        [Test]
        public void ARowThatLeavesItsShapesAlone_KeepsTheShippedOnes()
        {
            // The additive rule applied to the two shape fields, which is the case a designer hits by
            // accident: a row that only wants to change a cost must not silently stop a sorter routing or
            // make a machine starve.
            _asset.Machines = new[] { Retune(BuildKind.Sorter, row => row.Cost = 3) };

            ContentDatabase table = _asset.ToCore();

            Assert.AreEqual(3, table.Machine(BuildKind.Sorter).Cost, "the cost landed");
            Assert.AreEqual(ContentDatabase.Default.Machine(BuildKind.Sorter).Filter.Describe(),
                table.Machine(BuildKind.Sorter).Filter.Describe(), "and the filter is still the shipped one");
            Assert.IsFalse(table.Machine(BuildKind.Sorter).Filter.IsNone);
        }

        [Test]
        public void ARowNamingAnIdTheTableDoesNotHold_IsRefused()
        {
            // A retired or misspelled id falling back to the row's ordinal would silently retune whatever
            // now occupies that slot: a legacy "decomposer" row would become the cutter. So it throws,
            // which is also the migration path for a table written before the rework - the message names
            // the slot the id would have hit.
            _asset.Machines = new[] { Retune(BuildKind.Cutter, row => row.Id = "decomposer") };

            var refused = Assert.Throws<ArgumentException>(() => _asset.ToCore());
            StringAssert.Contains("decomposer", refused.Message, "the refusal names the id");
        }

        [Test]
        public void ARowKeyedById_LandsOnTheBuildingItNames_WhateverTheDropdownSays()
        {
            // The reason a row may carry an id: an enum value inserted above a building moves every later
            // ordinal, so an asset authored before the insert would retune its new neighbour. With the id
            // filled in, the row still lands on the building it names - and the rework moved nearly every
            // ordinal, which is exactly the insert this protects against.
            _asset.Machines = new[]
            {
                Retune(BuildKind.Sorter, row =>
                {
                    row.Build = BuildKind.Turret;   // the dropdown is deliberately the wrong one
                    row.Cost = 7;
                }),
            };

            ContentDatabase table = _asset.ToCore();

            Assert.AreEqual(7, table.Machine(BuildKind.Sorter).Cost,
                "the id named the sorter, and the id wins over the dropdown");
            Assert.AreEqual(ContentDatabase.Default.Machine(BuildKind.Turret).Cost,
                table.Machine(BuildKind.Turret).Cost, "and the turret was left alone");
            Assert.AreEqual("sorter", table.Machine(BuildKind.Sorter).Id.Value);
        }

        [Test]
        public void ARowWithNoId_IsKeyedByItsDropdown()
        {
            // The other half of the rule, and the behaviour every row had before ids existed: a hand-added
            // row that names nothing is keyed by the dropdown, and ends up describing the slot's own identity.
            ContentDatabaseAsset.MachineRow unnamed =
                ContentDatabaseAsset.MachineRow.FromDef(ContentDatabase.Default.Machine(BuildKind.Rotator));
            unnamed.Id = string.Empty;                  // keyed by the dropdown, not by an id
            unnamed.Cost = 7;
            _asset.Machines = new[] { unnamed };

            ContentDatabase table = _asset.ToCore();

            Assert.AreEqual(7, table.Machine(BuildKind.Rotator).Cost);
            Assert.AreEqual("rotator", table.Machine(BuildKind.Rotator).Id.Value);
        }

        [Test]
        public void ARowLeftAtZero_KeepsTheShippedHealth_AndReach()
        {
            // 0 in a row means "unset", not "indestructible", for the same additive reason every other
            // field is an override: an asset authored before buildings had health must not silently make
            // its rows invulnerable, and an enemy row must not lose its aggressive-scan reach.
            _asset.Machines = new[]
            {
                Retune(BuildKind.Wall, row => { row.Cost = 3; row.MaxHp = 0f; }),      // MaxHp left at 0
                Retune(BuildKind.Turret, row => row.MaxHp = 40f),                      // this one is retuned
            };

            ContentDatabaseAsset.EnemyRow runner =
                ContentDatabaseAsset.EnemyRow.FromDef(ContentDatabase.Default.Enemy(EnemyKind.Runner));
            runner.AggroRange = 0f;                     // and a reach left at 0
            runner.DetectionRange = 0f;
            _asset.Enemies = new[] { runner };

            ContentDatabase table = _asset.ToCore();

            Assert.AreEqual(3, table.Machine(BuildKind.Wall).Cost, "the row landed on the wall");
            Assert.AreEqual(120f, table.Machine(BuildKind.Wall).MaxHp, Sim.Tol,
                "0 on the row keeps the shipped health");
            Assert.AreEqual(40f, table.Machine(BuildKind.Turret).MaxHp, Sim.Tol, "a retune does land");
            Assert.AreEqual(1.6f, table.Enemy(EnemyKind.Runner).AggroRange, Sim.Tol,
                "0 keeps the shipped reach");
            Assert.AreEqual(2.5f, table.Enemy(EnemyKind.Runner).DetectionRange, Sim.Tol,
                "and the shipped notice range");
        }

        [Test]
        public void EveryRow_IsTheInverseOfItsDefinition()
        {
            // FromDef and ToCore must invert each other. CreateContentDatabase writes an asset through one and
            // the game reads it back through the other, so a dropped or mis-resolved field would make a
            // refreshed asset quietly disagree with the table it was copied from - and the two shape fields are
            // the easiest to drop, because they are the only ones that are not a number, an enum or a string.
            //
            // Driven through the whole path - every row written back and merged - rather than through ToDef
            // directly, because the merge is where the additive resolution lives: a row written from a
            // definition leaves every optional number at its "keep the shipped value" zero, so a rule that
            // resolved against the row instead of against the slot would show up here as a retuned gun's
            // damage reset to zero.
            ContentDatabase shipped = ContentDatabase.Default;

            var machines = new List<ContentDatabaseAsset.MachineRow>();
            var turrets = new List<ContentDatabaseAsset.TurretRow>();
            var enemies = new List<ContentDatabaseAsset.EnemyRow>();

            foreach (BuildKind kind in shipped.BuildKinds)
            {
                machines.Add(ContentDatabaseAsset.MachineRow.FromDef(shipped.Machine(kind)));
                if (shipped.IsTurret(kind))
                    turrets.Add(ContentDatabaseAsset.TurretRow.FromDef(shipped.Machine(kind).Id,
                        shipped.Turret(kind)));
            }

            foreach (EnemyKind kind in shipped.EnemyKinds)
                enemies.Add(ContentDatabaseAsset.EnemyRow.FromDef(shipped.Enemy(kind)));

            _asset.Machines = machines.ToArray();
            _asset.Turrets = turrets.ToArray();
            _asset.Enemies = enemies.ToArray();
            ContentDatabase table = _asset.ToCore();

            foreach (BuildKind kind in shipped.BuildKinds)
            {
                MachineDef def = shipped.Machine(kind);
                MachineDef back = table.Machine(kind);

                Assert.AreEqual(def.Id, back.Id, kind + " id");
                Assert.AreEqual(def.Build, back.Build, kind + " build");
                Assert.AreEqual(def.Tile, back.Tile, kind + " tile");
                Assert.AreEqual(def.Behavior, back.Behavior, kind + " behaviour");
                Assert.AreEqual(def.Cost, back.Cost, kind + " cost");
                Assert.AreEqual(def.Name, back.Name, kind + " name");
                Assert.AreEqual(def.Description, back.Description, kind + " description");
                Assert.AreEqual(def.Interval, back.Interval, Sim.Tol, kind + " interval");
                Assert.AreEqual(def.Filter.Describe(), back.Filter.Describe(), kind + " filter");
                Assert.AreEqual(def.Diet.Describe(), back.Diet.Describe(), kind + " diet");
                Assert.AreEqual(def.MaxHp, back.MaxHp, Sim.Tol, kind + " max hp");

                if (!shipped.IsTurret(kind)) continue;

                TurretDef gun = shipped.Turret(kind);
                TurretDef gunBack = table.Turret(kind);

                Assert.AreEqual(gun.Damage, gunBack.Damage, Sim.Tol, kind + " damage");
                Assert.AreEqual(gun.FireInterval, gunBack.FireInterval, Sim.Tol, kind + " fire interval");
                Assert.AreEqual(gun.Range, gunBack.Range, Sim.Tol, kind + " range");
                Assert.AreEqual(gun.ProjectileSpeed, gunBack.ProjectileSpeed, Sim.Tol, kind + " projectile speed");
            }

            foreach (EnemyKind kind in shipped.EnemyKinds)
            {
                EnemyDef def = shipped.Enemy(kind);
                EnemyDef back = table.Enemy(kind);

                Assert.AreEqual(def.Id, back.Id, kind + " id");
                Assert.AreEqual(def.Kind, back.Kind, kind + " kind");
                Assert.AreEqual(def.Name, back.Name, kind + " name");
                Assert.AreEqual(def.Hp, back.Hp, Sim.Tol, kind + " hp");
                Assert.AreEqual(def.Speed, back.Speed, Sim.Tol, kind + " speed");
                Assert.AreEqual(def.Damage, back.Damage, Sim.Tol, kind + " damage");
                Assert.AreEqual(def.AttackInterval, back.AttackInterval, Sim.Tol, kind + " attack interval");
                Assert.AreEqual(def.AggroRange, back.AggroRange, Sim.Tol, kind + " aggro range");
                Assert.AreEqual(def.DetectionRange, back.DetectionRange, Sim.Tol, kind + " detection range");
            }
        }
    }
}
