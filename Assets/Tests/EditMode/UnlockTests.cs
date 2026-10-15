using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The unlock ladder (§7): a map hands over an operator set, and that set - not a map's ordinal and not
    /// its cost - decides what may be built. A locked operator is refused outright rather than merely made
    /// expensive, because a machine the player can buy and then find useless is worse than one they cannot
    /// buy at all; and the same set is the guardrail the wave generator draws its demands from, so the
    /// ladder is also the promise that every wave is answerable.
    ///
    /// The maps here are hand-built, one rung each, mirroring the campaign's three teaching maps without
    /// reading them: Quarry hands over the cutter, Foundry adds the second mineral and the rotator, and
    /// Canyon opens everything.
    /// </summary>
    public class UnlockTests
    {
        /// <summary>One vein of circles and a door: the smallest map that can still be briefed a wave.</summary>
        private static MapDefinition Map(Operators unlocked, Quad[] minerals = null, int seed = 11)
            => new MapDefinition("ladder", 32, 20, Sim.TestCircles,
                Patches(minerals ?? new[] { Quad.Circle }),
                new[] { new Int2(16, 18) },
                new WaveDefinition[0],
                coreCell: new Int2(14, 8),
                unlocked: unlocked,
                seed: seed,
                generatedWaves: -1);

        private static ShapePatch[] Patches(Quad[] minerals)
        {
            var patches = new ShapePatch[minerals.Length];
            for (int i = 0; i < minerals.Length; i++)
                patches[i] = new ShapePatch(new Int2(2, 2 + i * 2), 1, 1, minerals[i]);

            return patches;
        }

        [Test]
        public void AMapWithOnlyTheCutterUnlocked_RefusesARotator()
        {
            // The refusal is the point: a rotator on map 1 is not expensive, it is not a thing this map
            // has. The world says so, the ghost greys it out, and a click that asks anyway is told why.
            SimWorld world = new SimWorld(Map(Operators.Cutter), new SimConfig());

            Assert.IsTrue(world.IsUnlocked(BuildKind.Cutter), "the map's one operator is buildable");
            Assert.IsFalse(world.IsUnlocked(BuildKind.Rotator));
            Assert.IsFalse(world.IsUnlocked(BuildKind.Stacker));

            var cell = new Int2(6, 6);
            Assert.IsFalse(world.CanPlace(BuildKind.Rotator, cell, Dir.East), "the ghost reads the same rule");
            Assert.IsFalse(world.TryPlace(BuildKind.Rotator, cell, Dir.East));
            Assert.IsFalse(world.Machines.Has(cell), "and nothing was built");

            // A click, not a direct TryPlace: the refusal is what the player is owed, and only the
            // gesture reports it - a button that quietly does nothing reads as broken.
            world.Tick(Gesture.Aim(cell, BuildKind.Rotator));
            world.Tick(Gesture.Release(cell, BuildKind.Rotator));

            Assert.IsTrue(world.Events.TryLast(SimEventKind.PlacementRejected, out SimEvent refusal),
                "the click says why");
            Assert.AreEqual(RejectionReason.Locked, refusal.Reason);
            Assert.AreEqual(BuildKind.Rotator, refusal.Building);
            Assert.AreEqual(cell, refusal.Cell);
            Assert.IsFalse(world.Machines.Has(cell), "and the click builds nothing");

            // The control: the same click with the operator the map does hand over.
            world.Tick(Gesture.Aim(cell, BuildKind.Cutter));
            world.Tick(Gesture.Release(cell, BuildKind.Cutter));

            Assert.IsTrue(world.Machines.Has(cell), "the cutter the map teaches lands where it was aimed");
            Assert.AreEqual(BuildKind.Cutter, Sim.MachineAt(world, cell).Build);
        }

        [Test]
        public void TheOperatorMachinesNeedTheirUnlock_AndTheScaffoldingNeedsNothing()
        {
            // Which buildings are gated comes from the content - `MachineDef.Requires` - rather than a list
            // of names here, so a new operator joins the ladder by being an operator. Only the three
            // operators are gated: a belt, a drill, a pipe, a splitter, a sorter, a wall and the gun are
            // scaffolding, and a map that gates them would have nothing to build with.
            foreach (Operators unlocked in new[] { Operators.None, Operators.Cutter, Operators.Rotator, Operators.All })
            {
                SimWorld world = new SimWorld(Map(unlocked), new SimConfig());

                foreach (BuildKind kind in world.Content.BuildKinds)
                {
                    Operators requires = world.Content.Machine(kind).Requires;
                    bool expected = (requires & ~unlocked) == 0;

                    Assert.AreEqual(expected, world.IsUnlocked(kind),
                        kind + " needs " + requires + " and the map unlocked " + unlocked);

                    if (kind == BuildKind.Cutter || kind == BuildKind.Rotator || kind == BuildKind.Stacker)
                        Assert.AreNotEqual(Operators.None, requires, kind + " is an operator and must be gated");
                    else
                        Assert.AreEqual(Operators.None, requires, kind + " is scaffolding and must not be");
                }
            }

            // A map that unlocks nothing can still be played with: belts, the drill that feeds them, the
            // sorter that routes and the gun that spends.
            SimWorld bare = new SimWorld(Map(Operators.None), new SimConfig());
            foreach (BuildKind kind in new[]
                     {
                         BuildKind.Belt, BuildKind.Drill, BuildKind.Pipe, BuildKind.Splitter,
                         BuildKind.Sorter, BuildKind.Wall, BuildKind.Turret,
                     })
                Assert.IsTrue(bare.IsUnlocked(kind), kind + " is scaffolding and needs no operator");
        }

        [Test]
        public void TheThreeRungsOfTheLadder_AreNineEighteenAndEightyItems()
        {
            // The ladder read off the pools rather than off a table: map 1 teaches cutting (nine circle
            // items), map 2 adds the second mineral and the rotator (eighteen, all pure - no stacker yet),
            // and map 3 opens the whole space. The numbers are the closure's, so a rung that quietly
            // stopped producing something would show up here as a map whose waves cannot be answered.
            ShapePool first = Map(Operators.Cutter).Pool();
            ShapePool second = Map(Operators.Cutter | Operators.Rotator,
                new[] { Quad.Circle, Quad.Square }).Pool();
            ShapePool third = Map(Operators.All, new[] { Quad.Circle, Quad.Square }).Pool();

            Assert.AreEqual(9, first.Shapes.Count, "cutting alone");
            Assert.AreEqual(18, second.Shapes.Count, "and the second mineral with the rotator");
            Assert.AreEqual(80, third.Shapes.Count, "and the stacker opens everything");

            Assert.Less(first.Shapes.Count, second.Shapes.Count, "each rung hands over more than the last");
            Assert.Less(second.Shapes.Count, third.Shapes.Count);

            Assert.IsFalse(first.CanProduce(Sim.TopHalfSquare), "map 1 has no square ore");
            Assert.IsFalse(second.CanProduce(Sim.Composite), "map 2 has no stacker");
            Assert.IsTrue(third.CanProduce(Sim.Composite), "map 3 can build the mixed round");
        }

        [Test]
        public void TheWaveGenerator_NeverDemandsAShapeTheMapCannotBuild()
        {
            // The ladder's teeth, read through the map's own director: every demand of every wave has to be
            // an item this map's operators can actually make, or the wave is unwinnable through no fault of
            // the player. Asserted over the whole opening and past it, on the map's *own* pool.
            foreach (Operators unlocked in new[] { Operators.Cutter, Operators.Cutter | Operators.Rotator, Operators.All })
            {
                SimWorld world = new SimWorld(
                    Map(unlocked, new[] { Quad.Circle, Quad.Square }, seed: 21), new SimConfig());

                ShapePool pool = world.Map.Pool();
                for (int n = 1; n <= 25; n++)
                {
                    WaveDefinition wave = world.Waves.Definition(n);
                    Assert.IsNotNull(wave, "an endless map answers for wave " + n);

                    foreach (SpawnGroup group in wave.Groups)
                    {
                        Assert.IsTrue(pool.CanProduce(group.Weakness),
                            "wave " + n + " on " + unlocked + " demands " + group.Weakness.Describe() +
                            ", which this map's operators cannot build");
                    }
                }
            }
        }

        [Test]
        public void UnlockingTheOperator_IsWhatChanges_NotTheMachineOrItsPrice()
        {
            // Two worlds, one map, one row: the only difference is the set. So "why can I not build this"
            // has exactly one answer, and a cost change can never be mistaken for a lock (which is why the
            // refusal names the lock rather than the stockpile).
            SimWorld locked = new SimWorld(Map(Operators.Cutter), new SimConfig());
            SimWorld open = new SimWorld(Map(Operators.Cutter | Operators.Stacker), new SimConfig());
            var cell = new Int2(6, 6);

            Assert.AreEqual(locked.Content.Machine(BuildKind.Stacker).Cost,
                open.Content.Machine(BuildKind.Stacker).Cost, "the same row, the same price");
            Assert.IsTrue(open.Economy.CanAfford(BuildKind.Stacker), "and either map could pay for it");

            Assert.IsFalse(locked.TryPlace(BuildKind.Stacker, cell, Dir.North));
            Assert.IsTrue(open.TryPlace(BuildKind.Stacker, cell, Dir.North),
                "the unlock is the whole difference");
        }
    }
}
