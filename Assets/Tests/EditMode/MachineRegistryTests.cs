using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The machine field's jobs: an explicit occupancy registry the tick walks instead of the whole grid, the
    /// two-tile footprint a machine is placed and counted by, and the port masks it computes once so the view
    /// and the simulation read the same rule. The mask tests pin the cases where "ports are the belts around
    /// it" is not the whole rule - a drill's facing is its only outlet, a cutter's is the front of each of its
    /// two tiles, and a turret only ever eats.
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

            Assert.AreEqual(3, world.Machines.Occupied.Count,
                "three machines: the cutter's two tiles are one entry, because the registry counts machines");
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
        public void Cutter_ReportsOneOutletPerTile_AndNothingElse()
        {
            // What the port masks mean for a machine on two tiles: each tile has one outlet, the side it faces,
            // because that is where its piece leaves - and the two are gathered into the one set the snapshot
            // carries, so the card reads the machine rather than a tile. A belt pointing away anywhere else is
            // not wired to it at all: not an outlet (nothing leaves that way) and not an input either (it does
            // not point at the machine), so it is simply a line passing by.
            SimWorld world = Sim.NewWorld(24, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);                // into the blade tile from the west
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.East);  // flowing east: top from bottom
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);               // the blade tile's front
            Sim.LayRun(world, new Int2(5, 1), Dir.East, 1);               // and the lane tile's, one over
            Sim.LayRun(world, new Int2(4, 0), Dir.North, 1);              // fed into the lane tile from the south
            Sim.LayRun(world, new Int2(4, 3), Dir.North, 1);              // a line leaving sideways

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(4, 2), out MachineSnapshot machine));

            Assert.AreEqual(Dir.East, machine.Direction);
            Assert.IsTrue(machine.OutMask.Has(Dir.East), "the side each of its tiles faces");
            Assert.IsFalse(machine.OutMask.Has(Dir.West),
                "and the side a belt points in on is a feed, not one of the exits");
            Assert.IsFalse(machine.OutMask.Has(Dir.North),
                "a belt leaving sideways is not an outlet either");
            Assert.IsFalse(machine.OutMask.Has(Dir.South), "nor is any other side");

            Assert.IsTrue(machine.InMask.Has(Dir.West), "the belt pointing into the blade tile is an input");
            Assert.IsTrue(machine.InMask.Has(Dir.South), "and so is the one into the lane tile");
            Assert.IsFalse(machine.InMask.Has(Dir.North), "while a line running away is nobody's input");

            // The union is the machine's; the one-tile answer is what a two-tile body drawing its own stubs on
            // its own front reads, so the two must not be confused for each other.
            world.Machines.PortMasksAt(new Int2(4, 2), out DirMask bladeIn, out DirMask bladeOut);
            world.Machines.PortMasksAt(new Int2(4, 1), out DirMask laneIn, out DirMask laneOut);

            Assert.IsTrue(bladeOut.Has(Dir.East) && !bladeOut.Has(Dir.West),
                "the blade tile's own outlet is its own front");
            Assert.IsTrue(laneOut.Has(Dir.East) && !laneOut.Has(Dir.South),
                "and the lane tile's is its own");
            Assert.IsTrue(bladeIn.Has(Dir.West) && !bladeIn.Has(Dir.South),
                "each tile is fed on its own side, not on its neighbour's");
            Assert.IsTrue(laneIn.Has(Dir.South) && !laneIn.Has(Dir.West));
        }

        [Test]
        public void APlacedCutter_StandsOnTwoTiles_AndTheRegistryCountsItOnce()
        {
            // The footprint, as the field keeps it: two tiles of solid ground and one entry in the registry, so
            // the tick steps the machine once and "how many machines" stays a count of machines. The second tile
            // is not a copy to be read past - it answers for the machine it belongs to, naming the tile the state
            // lives on - and either tile takes the whole machine away for one refund.
            SimWorld world = Sim.NewWorld(20, 12);
            var cell = new Int2(6, 5);
            Sim.Place(world, BuildKind.Cutter, cell, Dir.North);

            Int2 lane = cell + MachineFootprint.LaneOf(Dir.North).Offset();
            Assert.AreEqual(2, MachineFootprint.Tiles(BehaviorKind.Cutter), "a cutter is two tiles");
            Assert.AreEqual(1, MachineFootprint.Tiles(BehaviorKind.Rotator), "and everything else is one");
            Assert.AreEqual(1, world.Machines.Occupied.Count, "so the registry holds the machine once");
            Assert.IsTrue(world.Machines.Has(cell), "it stands on the tile it was placed on");
            Assert.IsTrue(world.Machines.Has(lane), "and on the lane one to the flow's right");
            Assert.AreEqual(TileKind.Cutter, world.TileGrid.Get(lane), "which is solid ground like the first");

            Assert.IsTrue(world.Machines.TryGet(lane, out MachineState fromLane),
                "the lane answers for the machine");
            Assert.AreEqual(cell, fromLane.Home, "and names the tile its state lives on");
            Assert.AreEqual(Dir.North, fromLane.Direction, "with the machine's own facing");
            Assert.IsTrue(world.Machines.TryGetSnapshot(lane, out MachineSnapshot snapshot));
            Assert.AreEqual(BehaviorKind.Cutter, snapshot.Behavior);
            Assert.AreEqual(Dir.North, snapshot.Direction);

            int before = world.Economy.Circles;
            Assert.IsTrue(world.TryRemoveBuilding(lane), "and either tile takes the machine away");
            Assert.AreEqual(before + world.Economy.CostOf(BuildKind.Cutter), world.Economy.Circles,
                "for one machine's worth of refund, not two");
            Assert.IsFalse(world.Machines.Has(cell), "which clears the tile it stood on");
            Assert.IsFalse(world.Machines.Has(lane), "and the lane with it");
            Assert.AreEqual(0, world.Machines.Occupied.Count, "leaving the registry as it was");

            SimWorld blade = Sim.NewWorld(20, 12);
            Sim.Place(blade, BuildKind.Cutter, cell, Dir.North);
            int paid = blade.Economy.Circles;
            Assert.IsTrue(blade.TryRemoveBuilding(cell), "and the tile it was placed on does the same");
            Assert.AreEqual(paid + blade.Economy.CostOf(BuildKind.Cutter), blade.Economy.Circles,
                "for the same one refund");
            Assert.IsFalse(blade.Machines.Has(lane), "with the lane gone too");
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
