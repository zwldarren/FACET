using System;
using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The content table, unit-tested where the shipped numbers used to be a pile of statics. "What
    /// exists" is data: every kind the enums name must have a row, and a lookup for something that is
    /// not a turret must fail loudly rather than quietly answering "turret".
    ///
    /// The rework removed three tables from this one - the recipe rows, the ammo rows and the shape rows.
    /// Processing is an operator, so there is no recipe to name; a turret's chamber is universal, so there
    /// is no diet to satisfy; and the item space is the algebra's fixed eighty, so a table of shape rows
    /// would be a second, driftable statement of it. What is left is machines, guns and enemy
    /// <em>archetypes</em> - an enemy has no weakness of its own any more, because a wave assigns one per
    /// group.
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
                    Assert.IsFalse(def.Filter.IsNone, kind + " is a sorter with no filter at all");
            }
        }

        [Test]
        public void TheShippedSorter_SendsAnythingWithACircleInIt_TheWayItFaces()
        {
            // The shipped filter is one mineral predicate - "anything with a circle in it" - which is what a
            // routing piece wants: face one way and every circle-bearing item goes there while the rest
            // carries on down the line. One rule covers fifty mixed items, which is why the filter is a
            // mineral predicate rather than a list of exact shapes. It is deliberately not the currency rule:
            // money is a <em>complete</em> shape, and completeness is not one of the filter's modes.
            MachineDef sorter = Content.Machine(BuildKind.Sorter);

            Assert.AreEqual(BehaviorKind.Sorter, sorter.Behavior);
            Assert.AreEqual(TileKind.Sorter, sorter.Tile);
            Assert.AreEqual(FilterMode.Mineral, sorter.Filter.Mode);
            Assert.AreEqual(Quad.Circle, sorter.Filter.Mineral);
            Assert.IsTrue(sorter.Filter.Matches(Sim.TopHalfCircle), "a half-circle bears a circle");
            Assert.IsFalse(sorter.Filter.Matches(Sim.Square), "a square does not");
        }

        [Test]
        public void EveryEnemyKind_HasAnArchetype_AndNoWeaknessOfItsOwn()
        {
            var seen = new HashSet<EnemyKind>();

            foreach (EnemyKind kind in (EnemyKind[])Enum.GetValues(typeof(EnemyKind)))
            {
                EnemyDef def = Content.Enemy(kind);
                Assert.AreEqual(kind, def.Kind);
                Assert.IsFalse(string.IsNullOrEmpty(def.Name), kind + " has no name to print");
                Assert.IsTrue(seen.Add(kind), kind + " is defined twice");
            }

            Assert.AreEqual(2, seen.Count,
                "two archetypes are enough for v1: a fast fragile one and a slow heavy one");

            // What kills an enemy is not a fact about its kind any more: the same archetype demands a
            // different item every wave, so the demand is a field of the spawn and of the wave group - and
            // there is deliberately no default for it, because a group with no demand can never be matched.
            Assert.IsNull(typeof(EnemyDef).GetField("Weakness"),
                "a weakness on the definition would make every wave of that kind the same puzzle again");

            // The item space has no rows either: it is the algebra's fixed eighty.
            Assert.AreEqual(80, ShapePool.All.Count, "the table of shapes is the algebra itself");
        }

        [Test]
        public void Turrets_AreFoundByIdentity_NotByOrdinalArithmetic()
        {
            Assert.IsTrue(Content.IsTurret(BuildKind.Turret));
            Assert.AreEqual(BuildKind.Turret, Content.Turret(BuildKind.Turret).Build);

            // The old convention answered "turret" for anything it did not recognise, and read a spec by
            // subtracting a kind's ordinal. Neither is possible now: a non-turret is a hard failure, and
            // which kinds are turrets comes from the table, not a list of names.
            foreach (BuildKind kind in (BuildKind[])Enum.GetValues(typeof(BuildKind)))
            {
                bool expected = kind == BuildKind.Turret;
                Assert.AreEqual(expected, Content.IsTurret(kind), kind + " turret-ness");
                if (!expected) Assert.Throws<ArgumentOutOfRangeException>(() => Content.Turret(kind));
            }
        }

        [Test]
        public void UnknownLookups_Throw_InsteadOfReturningADefault()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Content.Machine((BuildKind)200));
            Assert.Throws<ArgumentOutOfRangeException>(() => Content.Enemy((EnemyKind)200));
            Assert.Throws<ArgumentOutOfRangeException>(() => Content.Turret(BuildKind.Drill));
            Assert.Throws<ArgumentOutOfRangeException>(() => Content.Machine(new ContentId("no-such-machine")));
            Assert.Throws<ArgumentOutOfRangeException>(() => Content.Enemy(new ContentId("no-such-enemy")));
        }

        [Test]
        public void MachineTile_IsTheOneTheBuildCatalogReports()
        {
            foreach (BuildKind kind in (BuildKind[])Enum.GetValues(typeof(BuildKind)))
                Assert.AreEqual(Content.Machine(kind).Tile, BuildCatalog.TileFor(kind), kind.ToString());
        }

        [Test]
        public void EveryEatingMachine_EatsAnything_UnlessItsRowSaysOtherwise()
        {
            // The rework's jam rule: the pressure moved from "never land the wrong item" to "make the item
            // the briefing asked for", so every shipped machine that eats has a diet of anything and
            // nothing in the shipped game jams. The rule is still a content field - a table may narrow one,
            // and CustomContentTests proves that it bites - but the shipped answer is stated here so a row
            // that quietly acquires a diet is a red test.
            foreach (BuildKind kind in Content.BuildKinds)
            {
                MachineDef def = Content.Machine(kind);
                if (def.Behavior == BehaviorKind.None)
                {
                    Assert.IsTrue(def.Diet.IsNone,
                        kind + " has no behaviour, so it eats nothing at all");
                    continue;
                }

                Assert.AreEqual(FilterMode.Any, def.Diet.Mode,
                    kind + " eats, so its diet is anything a belt can carry");
                Assert.IsTrue(def.Diet.Matches(Sim.Circle) && def.Diet.Matches(Sim.Composite),
                    kind + " refuses nothing");
            }
        }

        [Test]
        public void TheThreeOperators_RequireTheirOwnUnlock_AndNothingElseDoes()
        {
            // The unlock ladder (§7) needs no separate table of "which machine does this map allow": the
            // machine that IS an operator requires it, and every other building is scaffolding.
            Assert.AreEqual(Operators.Cutter, Content.Machine(BuildKind.Cutter).Requires);
            Assert.AreEqual(Operators.Rotator, Content.Machine(BuildKind.Rotator).Requires);
            Assert.AreEqual(Operators.Stacker, Content.Machine(BuildKind.Stacker).Requires);

            foreach (BuildKind kind in Content.BuildKinds)
            {
                if (kind == BuildKind.Cutter || kind == BuildKind.Rotator || kind == BuildKind.Stacker) continue;

                Assert.AreEqual(Operators.None, Content.Machine(kind).Requires,
                    kind + " is scaffolding: it needs no operator unlocked");
            }
        }

        [Test]
        public void ShippedNumbers_AreTheOnesTheDesignWasBalancedOn()
        {
            // Pinned so a refactor cannot quietly retune the prototype: these are the exact values the
            // tests' timing (30 ticks per item, 1.5 s per cut, 0.75 s per turn, 2 s per union) was derived
            // from, read straight from the table.
            Assert.AreEqual(1f, Content.Machine(BuildKind.Drill).Interval, Sim.Tol);
            Assert.AreEqual(1.5f, Content.Machine(BuildKind.Cutter).Interval, Sim.Tol);
            Assert.AreEqual(0.75f, Content.Machine(BuildKind.Rotator).Interval, Sim.Tol);
            Assert.AreEqual(2f, Content.Machine(BuildKind.Stacker).Interval, Sim.Tol);
            Assert.AreEqual(0.25f, Content.Machine(BuildKind.Pipe).Interval, Sim.Tol);

            Assert.AreEqual(1, Content.Machine(BuildKind.Belt).Cost);
            Assert.AreEqual(10, Content.Machine(BuildKind.Drill).Cost);
            Assert.AreEqual(15, Content.Machine(BuildKind.Cutter).Cost);
            Assert.AreEqual(10, Content.Machine(BuildKind.Rotator).Cost);
            Assert.AreEqual(25, Content.Machine(BuildKind.Stacker).Cost);
            Assert.AreEqual(6, Content.Machine(BuildKind.Pipe).Cost);
            Assert.AreEqual(10, Content.Machine(BuildKind.Splitter).Cost);
            Assert.AreEqual(12, Content.Machine(BuildKind.Sorter).Cost);
            Assert.AreEqual(2, Content.Machine(BuildKind.Wall).Cost);
            Assert.AreEqual(20, Content.Machine(BuildKind.Turret).Cost);

            // Health is content now: the wall is the pool the siege is balanced against (120 hp is twenty
            // seconds of chewing for one Runner), and the belt's 0 is what says it is walkable and
            // untouchable rather than a machine with no health.
            Assert.AreEqual(120f, Content.Machine(BuildKind.Wall).MaxHp, Sim.Tol);
            Assert.AreEqual(80f, Content.Machine(BuildKind.Turret).MaxHp, Sim.Tol);
            Assert.AreEqual(0f, Content.Machine(BuildKind.Belt).MaxHp, Sim.Tol,
                "a belt is not a machine and has no health to chew");

            TurretDef turret = Content.Turret(BuildKind.Turret);
            Assert.AreEqual(4f, turret.Damage, Sim.Tol, "one matched shot is 4 damage");
            Assert.AreEqual(1f, turret.FireInterval, Sim.Tol);
            Assert.AreEqual(1f, turret.ShotsPerSecond, Sim.Tol, "exactly one belt's worth");
            Assert.AreEqual(7f, turret.Range, Sim.Tol);
            Assert.AreEqual(14f, turret.ProjectileSpeed, Sim.Tol);

            // There is no ammo table to read a damage out of: what a shot is worth is the round against the
            // target's demand, and both multipliers live in Balance - full for the briefing's own shape, half
            // for anything else.
            Assert.AreEqual(1f, Balance.WeaknessDamageMultiplier, Sim.Tol);
            Assert.AreEqual(0.5f, Balance.ResistantDamageMultiplier, Sim.Tol);
            Assert.AreEqual(turret.Damage * Balance.WeaknessDamageMultiplier, turret.Damage, Sim.Tol);

            EnemyDef runner = Content.Enemy(EnemyKind.Runner);
            Assert.AreEqual(6f, runner.Hp, Sim.Tol);
            Assert.AreEqual(1.5f, runner.Speed, Sim.Tol);
            Assert.AreEqual(6f, runner.Damage, Sim.Tol);
            Assert.AreEqual(1.6f, runner.AggroRange, Sim.Tol, "the aggressive scan's reach");
            Assert.AreEqual(2.5f, runner.DetectionRange, Sim.Tol, "and the range it charges from");

            EnemyDef tank = Content.Enemy(EnemyKind.Tank);
            Assert.Greater(tank.Hp, runner.Hp, "the deep demand's archetype is the tankier one - three " +
                "matched shots to the Runner's two, which is what its budget cost claims");
            Assert.Less(tank.Speed, runner.Speed, "but it walks slower");
            Assert.Greater(tank.Damage, runner.Damage, "and hits the Core harder");
            Assert.Greater(tank.DetectionRange, runner.DetectionRange,
                "and notices defences further out, so what stands in its way has to hurt it");
        }

        [Test]
        public void ATableIsNotASingleton_ACustomOneIsHonoured()
        {
            // The seam the whole layer exists for: a world can be handed a different table and get a
            // different, reproducible game. Here the one gun hits harder and the one archetype is a brute.
            var machines = new[]
            {
                new MachineDef(new ContentId("turret"), BuildKind.Turret, TileKind.Turret,
                    BehaviorKind.Turret, cost: 99, name: "Siege", description: "", interval: 0f),
            };
            var turrets = new[]
            {
                new TurretDef(BuildKind.Turret, "Siege", damage: 9f, fireInterval: 0.5f, range: 12f,
                    projectileSpeed: 20f),
            };
            var enemies = new[]
            {
                new EnemyDef(new ContentId("runner"), EnemyKind.Runner, "Brute", hp: 40f, speed: 1f,
                    damage: 20f, attackInterval: 2f),
            };

            var custom = new ContentDatabase(machines, turrets, enemies);

            Assert.AreEqual(99, custom.Machine(BuildKind.Turret).Cost);
            Assert.AreEqual(9f, custom.Turret(BuildKind.Turret).Damage, Sim.Tol);
            Assert.AreEqual(40f, custom.Enemy(EnemyKind.Runner).Hp, Sim.Tol);

            Assert.Throws<ArgumentOutOfRangeException>(() => custom.Machine(BuildKind.Drill),
                "a table that defines only a turret has no drill to report");
            Assert.Throws<ArgumentOutOfRangeException>(() => custom.Enemy(EnemyKind.Tank),
                "nor a Tank archetype");

            // And the shipped table is untouched by any of it.
            Assert.AreEqual(20, ContentDatabase.Default.Machine(BuildKind.Turret).Cost);
            Assert.AreEqual(4f, ContentDatabase.Default.Turret(BuildKind.Turret).Damage, Sim.Tol);
        }

        [Test]
        public void AnInconsistentTable_IsRefusedAtConstruction_NotAtRuntime()
        {
            // The two silences the table used to ship: a turret row with no gun (a machine that stands
            // built, unfireable and uncomplaining) and a sorter row with no filter (a machine that routes
            // by no rule at all). Neither failure throws on its own, so the constructor refuses both.
            var gunless = new MachineDef(new ContentId("gunless"), BuildKind.Splitter, TileKind.Splitter,
                BehaviorKind.Turret, cost: 10, name: "Gunless", description: "", interval: 0f);

            var thrown = Assert.Throws<ArgumentException>(() => BuildTable(machine: gunless));
            StringAssert.Contains("Gunless", thrown.Message, "the row is named, not just the rule");

            var blind = new MachineDef(new ContentId("blind"), BuildKind.Sorter, TileKind.Sorter,
                BehaviorKind.Sorter, cost: 12, name: "Blind", description: "", interval: 0f);

            Assert.Throws<ArgumentException>(() => BuildTable(machine: blind),
                "a sorter filtering for nothing cannot be constructed");
        }

        [Test]
        public void AMachineRow_WithAnUnimplementedBehaviour_IsRefusedWhenAWorldIsBuilt()
        {
            // The third silence, and the one only a world can catch: the table happily holds a row asking
            // for a behaviour nothing implements, and MachineSystem is where that becomes a refusal naming
            // the row. CustomContentTests holds the full rule; this is the content-table half of it.
            const BehaviorKind alien99 = (BehaviorKind)99;
            var alien = new MachineDef(new ContentId("alien"), BuildKind.Wall, TileKind.Wall,
                alien99, cost: 1, name: "Alien", description: "unknown", interval: 0f);

            ContentDatabase table = BuildTable(machine: alien);
            Assert.AreEqual(alien99, table.Machine(BuildKind.Wall).Behavior,
                "the table is data: it holds what it was handed");

            Assert.Throws<InvalidOperationException>(() =>
                new SimWorld(Sim.TestMap(12, 10), new SimConfig(), table));
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
        }

        /// <summary>The shipped table with one machine row swapped, built by reading the shipped rows rather
        /// than restating them - so a test is explicit about what it changes and keeps passing when shipped
        /// numbers are retuned.</summary>
        private static ContentDatabase BuildTable(MachineDef machine)
        {
            ContentDatabase shipped = ContentDatabase.Default;

            var machines = new MachineDef[shipped.BuildKinds.Length];
            for (int i = 0; i < shipped.BuildKinds.Length; i++)
                machines[i] = shipped.Machine(shipped.BuildKinds[i]);
            machines[(int)machine.Build] = machine;

            var turrets = new TurretDef[shipped.BuildKinds.Length];
            for (int i = 0; i < shipped.BuildKinds.Length; i++)
                if (shipped.IsTurret(shipped.BuildKinds[i])) turrets[i] = shipped.Turret(shipped.BuildKinds[i]);

            var enemies = new EnemyDef[shipped.EnemyKinds.Length];
            for (int i = 0; i < shipped.EnemyKinds.Length; i++)
                enemies[i] = shipped.Enemy(shipped.EnemyKinds[i]);

            return new ContentDatabase(machines, turrets, enemies);
        }
    }
}
