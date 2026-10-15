using Facet.Core;
using Facet.Game;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The inspect cursor: nothing selected, so the left button reads the map instead of building on it.
    /// What a view draws from this - the cursor's border, the info card - is the views' business; proved
    /// here is the state they read: which tile is pinned, and that a command with no selection places
    /// nothing.
    /// </summary>
    public class InspectTests
    {
        private static readonly Int2 Ore = new Int2(3, 3);

        /// <summary>A world with a drill standing on a patch at (3,3) - something worth reading.</summary>
        private static SimWorld WorldWithADrill()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            world.Terrain.SetOre(Ore, Quad.Circle);
            Sim.Place(world, BuildKind.Drill, Ore, Dir.East);
            return world;
        }

        /// <summary>The press a left click with nothing selected produces - the very gesture the
        /// input adapter latches, spelled by <see cref="Gesture"/>.</summary>
        private static InputCommand Click(Int2 cell) => Gesture.Click(cell);

        [Test]
        public void ACommandWithNoSelection_PlacesNothing()
        {
            // The click that reads a building is the same press that used to build one. With nothing
            // selected it must do neither: no drill on the patch under the cursor, no run behind it, no
            // circles spent.
            SimWorld world = Sim.NewWorld(16, 10);
            world.Terrain.SetOre(Ore, Quad.Circle);
            int circles = world.Economy.Circles;

            world.Tick(Click(Ore));
            world.Tick(Gesture.Aim(new Int2(6, 3)));
            world.Tick(Gesture.Release(new Int2(6, 3)));

            Assert.IsFalse(world.Machines.Has(Ore), "nothing selected places no machine");
            Assert.IsFalse(world.Belts.Has(Ore), "and no belt on the press cell");
            Assert.IsFalse(world.Belts.Has(new Int2(4, 3)), "and no run out of it");
            Assert.IsFalse(world.Belts.Has(new Int2(6, 3)), "all the way to where the drag ended");
            Assert.AreEqual(circles, world.Economy.Circles, "nothing was paid for");
        }

        [Test]
        public void ClickingWithNothingSelected_PinsTheBuildingUnderIt()
        {
            SimWorld world = WorldWithADrill();

            world.Tick(Click(Ore));
            Assert.AreEqual(Ore, world.InspectedCell, "the click reads the drill under it");

            var grass = new Int2(9, 9);
            world.Tick(Click(grass));
            Assert.IsFalse(world.TileGrid.InBounds(world.InspectedCell),
                "bare ground is nothing to read, so it unpins rather than pinning a cell");
        }

        [Test]
        public void ABeltAndTheCore_AreBuildingsWorthReading()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 5), Dir.East, 2);

            var belt = new Int2(1, 5);
            world.Tick(Click(belt));
            Assert.AreEqual(belt, world.InspectedCell, "a belt carries state - direction, item, jam");

            Int2 core = world.Core.Cell;
            world.Tick(Click(core));
            Assert.AreEqual(core, world.InspectedCell, "and the Core's health is worth a click");
        }

        [Test]
        public void ClickingOre_PinsTheVein()
        {
            // Ore is not a building and has no state, but it is what a chain gets built around - so it
            // is something to read, not bare ground to unpin on.
            SimWorld world = Sim.NewWorld(16, 10);
            world.Terrain.SetOre(Ore, Quad.Circle);

            world.Tick(Click(Ore));

            Assert.AreEqual(Ore, world.InspectedCell, "a vein is worth a click");
        }

        [Test]
        public void ClickingAnEntryPoint_PinsIt()
        {
            SimWorld world = Sim.Rig();   // a map with a doorway on it
            Int2 door = world.Map.SpawnPoints[0];

            Assert.IsFalse(world.Machines.Has(door), "a doorway is bare ground otherwise");
            world.Tick(Click(door));

            Assert.AreEqual(door, world.InspectedCell, "a door the waves walk in at is worth a click");
        }

        [Test]
        public void AskingForTheSameBuildingTwice_DropsTheSelection()
        {
            // The digits and the bar's tiles are two doors onto one latch, so "again" belongs to the input
            // source: press 1 twice and nothing is selected, the read cursor the run opens in. The world is
            // only ever told what the selection ended up being.
            var input = new BuildInput();

            input.RequestSelect(BuildKind.Drill);
            Assert.AreEqual(BuildKind.Drill, input.Snapshot().Selected, "the first ask selects it");

            input.RequestSelect(BuildKind.Drill);
            Assert.IsNull(input.Snapshot().Selected, "and asking again drops it");

            input.RequestSelect(BuildKind.Turret);
            input.RequestSelect(BuildKind.Turret);
            Assert.IsNull(input.Snapshot().Selected, "for any building, not just the first");

            input.RequestSelect(BuildKind.Turret);
            input.RequestSelect(null);
            Assert.IsNull(input.Snapshot().Selected, "and the card's button clears rather than toggles");
        }

        [Test]
        public void TheSortersFilter_IsLatchedAsAOneShotWithItsOwnCell()
        {
            // The picker is a click on a panel rather than on the map, so the cell it is about travels with
            // the command - and it travels in a field of its own, because everything else that reads the
            // cursor in the same tick (the tooltip, a keyboard inspect, the ghost) is about where the mouse
            // really is. A one-shot like the other latches: applied once, then dropped.
            var input = new BuildInput();
            var hub = new Int2(6, 4);
            ShapeFilter rule = ShapeFilter.Is(Sim.Square);

            input.RequestFilter(hub, rule);
            InputCommand command = input.Snapshot();

            Assert.IsTrue(command.FilterSet, "the ask is latched");
            Assert.AreEqual(rule, command.SetFilter);
            Assert.AreEqual(hub, command.FilterCell, "with the machine it is about");
            Assert.AreNotEqual(hub, command.CursorCell,
                "and not in the cursor's field, which the rest of the tick reads as the map");

            input.ConsumeOneShots();
            InputCommand again = input.Snapshot();

            Assert.IsFalse(again.FilterSet, "a consumed one-shot is not sent twice");
            Assert.IsTrue(again.SetFilter.IsNone);
        }

        [Test]
        public void SelectingSomething_DropsTheReadPin()
        {
            SimWorld world = WorldWithADrill();
            world.Tick(Click(Ore));

            world.Tick(new InputCommand(cursorCell: Ore, selected: BuildKind.Belt));

            Assert.AreEqual(BuildKind.Belt, world.SelectedKind, "the bar's click selects as always");
            Assert.IsFalse(world.TileGrid.InBounds(world.InspectedCell),
                "and a highlight left behind would read as a second selection");
        }

        [Test]
        public void RemovingWhatIsPinned_ClearsTheReadPin()
        {
            SimWorld world = WorldWithADrill();
            world.Tick(Click(Ore));

            world.Tick(Gesture.Removal(Ore));

            Assert.IsFalse(world.Machines.Has(Ore), "the drill is gone");
            Assert.IsFalse(world.TileGrid.InBounds(world.InspectedCell),
                "and the card must not go on describing the hole it left");
        }

        [Test]
        public void TheReadPin_IsAppliedWhilePaused()
        {
            // Reading the base is what a paused player does. The pin is input state, not a world
            // change, so it lands on the same tick as the click without advancing the run.
            SimWorld world = WorldWithADrill();
            world.Paused = true;

            world.Tick(Click(Ore));

            Assert.AreEqual(Ore, world.InspectedCell, "a held run still answers the read");
            Assert.AreEqual(0, world.TickCount, "and nothing else advanced");
        }
    }
}
