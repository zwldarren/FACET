using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The machine field's two new jobs: an explicit occupancy registry the tick walks instead of the
    /// whole grid, and the port masks it computes once so the view and the simulation read the same
    /// rule. The mask test pins the case the old view got wrong - a decomposer's second outlet on a
    /// side that is not its facing.
    /// </summary>
    public class MachineRegistryTests
    {
        [Test]
        public void Occupied_IsKeptInMapOrder_NotBuildOrder()
        {
            SimWorld world = Sim.NewWorld(20, 12);

            // Built north-east, then south, then in the middle: insertion order is not map order.
            Sim.Place(world, BuildKind.Splitter, new Int2(12, 9), Dir.East);
            Sim.Place(world, BuildKind.Cannon, new Int2(2, 3), Dir.North);
            Sim.Place(world, BuildKind.Decomposer, new Int2(6, 5), Dir.East);

            Assert.AreEqual(3, world.Machines.Occupied.Count);
            AssertAreAscending(world);

            // Removing one leaves the rest in order, and the registry with no hole.
            Assert.IsTrue(world.TryRemoveBuilding(new Int2(6, 5)));
            Assert.AreEqual(2, world.Machines.Occupied.Count);
            AssertAreAscending(world);

            Assert.IsTrue(world.TryRemoveBuilding(new Int2(2, 3)));
            Assert.IsTrue(world.TryRemoveBuilding(new Int2(12, 9)));
            Assert.AreEqual(0, world.Machines.Occupied.Count);
        }

        [Test]
        public void CountTurrets_WalksTheRegistry_AndFollowsRemoval()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.Place(world, BuildKind.Cannon, new Int2(2, 3), Dir.East);
            Sim.Place(world, BuildKind.Splitter, new Int2(4, 3), Dir.East);
            Sim.Place(world, BuildKind.Cannon, new Int2(6, 3), Dir.East);

            world.Machines.CountTurrets(out int total, out int armed);
            Assert.AreEqual(2, total);
            Assert.AreEqual(0, armed, "no ammo has arrived yet");

            Assert.IsTrue(world.TryRemoveBuilding(new Int2(2, 3)));
            world.Machines.CountTurrets(out total, out _);
            Assert.AreEqual(1, total);
        }

        [Test]
        public void Decomposer_ReportsEveryWiredOutlet_NotJustItsFacing()
        {
            SimWorld world = Sim.NewWorld(24, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);              // input
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);              // outlet on the facing side
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 1);             // second outlet, sideways

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(4, 2), out MachineSnapshot machine));

            Assert.AreEqual(Dir.East, machine.Direction);
            Assert.IsTrue(machine.OutMask.Has(Dir.East), "the outlet it faces");
            Assert.IsTrue(machine.OutMask.Has(Dir.North),
                "and the sideways one too - ports are the belts around it, not its facing");
            Assert.IsFalse(machine.OutMask.Has(Dir.South));
            Assert.IsFalse(machine.OutMask.Has(Dir.West), "west is the input side");

            Assert.IsTrue(machine.InMask.Has(Dir.West));
            Assert.IsFalse(machine.InMask.Has(Dir.North));
        }

        [Test]
        public void Drill_ReportsOnlyItsFacingAsAnOutlet()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            world.Terrain.SetOre(new Int2(2, 2), ShapeType.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 2), Dir.East);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.LayRun(world, new Int2(2, 3), Dir.North, 1);             // a belt running away north

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(2, 2), out MachineSnapshot drill));

            Assert.IsTrue(drill.OutMask.Has(Dir.East));
            Assert.IsFalse(drill.OutMask.Has(Dir.North),
                "a drill's facing IS its output; another belt pointing away is not wired to it");
            Assert.IsTrue(drill.InMask.IsEmpty, "nothing points into a drill");
        }

        [Test]
        public void Turret_HasInputsButNeverAnOutlet()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);              // into the turret
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);              // away from it
            Sim.Place(world, BuildKind.Cannon, new Int2(4, 2), Dir.East);

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(4, 2), out MachineSnapshot turret));

            Assert.IsTrue(turret.InMask.Has(Dir.West));
            Assert.IsTrue(turret.OutMask.IsEmpty, "a turret eats; it never pushes onto a belt");
        }

        private static void AssertAreAscending(SimWorld world)
        {
            int previous = int.MinValue;
            for (int i = 0; i < world.Machines.Occupied.Count; i++)
            {
                int index = world.Machines.Occupied[i];
                Assert.Greater(index, previous, "the registry must stay in ascending map order");
                previous = index;

                Int2 cell = world.Machines.CellAt(i);
                Assert.AreEqual(index, cell.Y * world.TileGrid.Width + cell.X);
            }
        }
    }
}
