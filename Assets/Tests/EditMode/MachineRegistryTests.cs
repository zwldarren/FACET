using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The machine field's two jobs: an explicit occupancy registry the tick walks instead of the whole
    /// grid, and the port masks it computes once so the view and the simulation read the same rule. The
    /// mask tests pin the cases where "ports are the belts around it" is not the whole rule - a drill's
    /// facing is its only outlet, a cutter's are the two sides of its cut line, and a turret only ever
    /// eats.
    /// </summary>
    public class MachineRegistryTests
    {
        [Test]
        public void Occupied_IsKeptInMapOrder_NotBuildOrder()
        {
            SimWorld world = Sim.NewWorld(20, 12);

            // Built north-east, then south, then in the middle: insertion order is not map order.
            Sim.Place(world, BuildKind.Splitter, new Int2(12, 9), Dir.East);
            Sim.Place(world, BuildKind.Turret, new Int2(2, 3), Dir.North);
            Sim.Place(world, BuildKind.Cutter, new Int2(6, 5), Dir.East);

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
            Sim.Place(world, BuildKind.Turret, new Int2(2, 3), Dir.East);
            Sim.Place(world, BuildKind.Splitter, new Int2(4, 3), Dir.East);
            Sim.Place(world, BuildKind.Turret, new Int2(6, 3), Dir.East);

            world.Machines.CountTurrets(out int total, out int armed);
            Assert.AreEqual(2, total);
            Assert.AreEqual(0, armed, "no round has arrived yet");

            Assert.IsTrue(world.TryRemoveBuilding(new Int2(2, 3)));
            world.Machines.CountTurrets(out total, out _);
            Assert.AreEqual(1, total);
        }

        [Test]
        public void Cutter_ReportsItsTwoCutSidesAsOutlets_AndNothingElse()
        {
            // What the port masks mean for an operator, and where the old hub rule no longer applies. A
            // cutter's outlets are the two sides of its own cut line - the side it faces and the one
            // opposite it - because those are the two pieces' exits. A belt pointing away on any other
            // side is not wired to it at all: it is not an outlet (nothing leaves that way) and not an
            // input either (it does not point at the machine), so it is simply a line passing by.
            SimWorld world = Sim.NewWorld(24, 12);
            Sim.LayRun(world, new Int2(4, 1), Dir.North, 1);              // input, from the south
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.East); // east/west: the vertical cut
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);              // the front outlet
            Sim.LayRun(world, new Int2(3, 2), Dir.West, 1);              // the back outlet
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 1);             // a line leaving sideways

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(4, 2), out MachineSnapshot machine));

            Assert.AreEqual(Dir.East, machine.Direction);
            Assert.IsTrue(machine.OutMask.Has(Dir.East), "the outlet it faces");
            Assert.IsTrue(machine.OutMask.Has(Dir.West), "and the one opposite it: the cut's other piece");
            Assert.IsFalse(machine.OutMask.Has(Dir.North),
                "a belt leaving sideways is not one of the cut's two exits");
            Assert.IsFalse(machine.OutMask.Has(Dir.South), "and the input side is not an outlet");

            Assert.IsTrue(machine.InMask.Has(Dir.South), "the belt pointing into it is the input");
            Assert.IsFalse(machine.InMask.Has(Dir.North), "and a line running away is nobody's input");
        }

        [Test]
        public void Drill_ReportsOnlyItsFacingAsAnOutlet()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            world.Terrain.SetOre(new Int2(2, 2), Quad.Circle);
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
            Sim.Place(world, BuildKind.Turret, new Int2(4, 2), Dir.East);

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(4, 2), out MachineSnapshot turret));

            Assert.IsTrue(turret.InMask.Has(Dir.West));
            Assert.IsTrue(turret.OutMask.IsEmpty, "a turret eats; it never pushes onto a belt");
        }

        [Test]
        public void TheBehaviourRegistry_IsSizedPastTheHighestOrdinal_NotByTheMemberCount()
        {
            // The registry is an ordinal-indexed table, and a closed enum here keeps its retired values'
            // numbers dead: BehaviorKind has holes where the converter and the assembler used to be, so its
            // member count is smaller than its highest value. Sizing by the count is exactly what broke every
            // world's constructor when the rework added the rotator and the stacker above those holes, so the
            // machine whose behaviour sits highest has to be reachable by the tick - and the premise below
            // makes sure this test still says something if the holes are ever filled in.
            int members = System.Enum.GetValues(typeof(BehaviorKind)).Length;
            var highest = BehaviorKind.None;
            foreach (BehaviorKind value in (BehaviorKind[])System.Enum.GetValues(typeof(BehaviorKind)))
                if ((int)value > (int)highest) highest = value;

            Assert.Greater((int)highest + 1, members,
                "the test's premise: this enum has a hole below its highest value");

            SimWorld world = Sim.NewWorld(20, 12);
            BuildKind kind = BuildKind.Belt;
            foreach (BuildKind candidate in world.Content.BuildKinds)
                if (world.Content.Machine(candidate).Behavior == highest) kind = candidate;

            Sim.Place(world, kind, new Int2(4, 2), Dir.East);
            Sim.Tick(world, 5);

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(4, 2), out MachineSnapshot machine));
            Assert.AreEqual(highest, machine.Behavior, "the tick reached the highest-valued behaviour");
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
