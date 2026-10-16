using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// Issue #1: an item turns at the centre of a turning belt rather than cutting its corner, and one free
    /// cell beside a machine is enough to take its product - the belt there takes the delivery in from the
    /// machine-facing edge and turns inside the cell. Both rest on the same fact: an item knows which way it
    /// came onto its cell (<see cref="BeltState.Entry"/>).
    /// </summary>
    public class BeltTurnTests
    {
        /// <summary>One tick's travel at the shipped belt speed: a tile a second at 30 Hz.</summary>
        private const float TickStep = 1f / 30f;

        // ---------------------------------------------------------------- Part A: the turn

        /// <summary>East along (1,1)-(2,1), then north up (3,1)-(3,3): the corner cell is (3,1).</summary>
        private static SimWorld CornerWorld(out Int2 corner)
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 2);
            Sim.LayRun(world, new Int2(3, 1), Dir.North, 3);
            corner = new Int2(3, 1);
            return world;
        }

        private static ItemSnapshot OnlyItem(SimWorld world)
        {
            List<ItemSnapshot> items = Sim.ItemsIn(world);
            Assert.AreEqual(1, items.Count, "the test rides exactly one item");
            return items[0];
        }

        [Test]
        public void ACornerItem_EntersOnItsInEdge_PassesThroughTheCentre_AndLeavesOnItsOutEdge()
        {
            SimWorld world = CornerWorld(out Int2 corner);
            Vec2 centre = world.TileGrid.CellCenter(corner);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(2, 1), Sim.Circle));

            Sim.Tick(world, 30);   // handed onto the corner this tick, at progress 0
            Assert.IsTrue(world.Belts.HasItemAt(corner), "the item reached the corner cell");
            Assert.AreEqual(0f, Sim.ProgressAt(world, corner), Sim.ProgressTol);
            Assert.AreEqual(0f, Vec2.Distance(centre + new Vec2(-0.5f, 0f), OnlyItem(world).Position), Sim.ProgressTol,
                "it enters on the corner's WEST edge - the edge it came in through - not its south edge");

            Sim.Tick(world, 15);   // half a tile in
            Assert.AreEqual(0f, Vec2.Distance(centre, OnlyItem(world).Position), Sim.ProgressTol,
                "half way along the cell it is on the centre");

            Sim.Tick(world, 8);    // past the centre: now travelling north
            Vec2 p = OnlyItem(world).Position;
            Assert.AreEqual(centre.X, p.X, Sim.ProgressTol, "after the centre it is on the exit line");
            Assert.Greater(p.Y, centre.Y, "and heading north");

            Sim.Tick(world, 7);    // progress 1: on the north edge, and handed on
            Assert.AreEqual(0f, Vec2.Distance(centre + new Vec2(0f, 0.5f), OnlyItem(world).Position), Sim.ProgressTol,
                "it leaves by the corner's north edge");
        }

        [Test]
        public void ACornerItem_NeverJumps_AndNeverCutsTheCorner()
        {
            // Every tick's travel is at most one tick's step, so nothing pops at a hand-off; and every
            // position on the corner cell lies on one of its two legs (on the entry row or the exit column),
            // so the item never takes the diagonal the old one-direction formula drew.
            SimWorld world = CornerWorld(out Int2 corner);
            Vec2 centre = world.TileGrid.CellCenter(corner);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(1, 1), Sim.Circle));

            for (int t = 0; t < 30 * 4; t++)
            {
                Sim.Tick(world, 1);
                ItemSnapshot item = OnlyItem(world);

                Assert.LessOrEqual(Vec2.Distance(item.PreviousPosition, item.Position), TickStep + Sim.ProgressTol,
                    "tick " + t + ": an item moves one tick's step at most - no hand-off jump");

                if (!world.Belts.HasItemAt(corner)) continue;
                bool onEntryLeg = System.Math.Abs(item.Position.Y - centre.Y) <= Sim.ProgressTol;
                bool onExitLeg = System.Math.Abs(item.Position.X - centre.X) <= Sim.ProgressTol;
                Assert.IsTrue(onEntryLeg || onExitLeg,
                    "tick " + t + ": on the corner the item is on one of its two legs, not the diagonal, at " +
                    item.Position);
            }
        }

        [Test]
        public void ATurnCostsNoThroughput()
        {
            // The L through the centre is one tile long, exactly like a straight cell: an item takes the same
            // number of ticks across three cells whether the middle one turns or not.
            SimWorld straight = Sim.NewWorld(16, 10);
            Sim.LayRun(straight, new Int2(1, 1), Dir.East, 4);
            SimWorld turning = CornerWorld(out _);

            Assert.IsTrue(straight.Belts.TrySpawnItem(new Int2(1, 1), Sim.Circle));
            Assert.IsTrue(turning.Belts.TrySpawnItem(new Int2(1, 1), Sim.Circle));
            Sim.Tick(straight, 90);
            Sim.Tick(turning, 90);

            Assert.IsTrue(straight.Belts.HasItemAt(new Int2(4, 1)));
            Assert.IsTrue(turning.Belts.HasItemAt(new Int2(3, 2)), "three cells in, round the corner, on the same tick");
            Assert.AreEqual(Sim.ProgressAt(straight, new Int2(4, 1)), Sim.ProgressAt(turning, new Int2(3, 2)), Sim.ProgressTol);
        }

        [Test]
        public void AStraightRun_DrawsExactlyWhereItAlwaysDid()
        {
            // The entry side only bends a turning cell: on a straight one entry and exit agree and the
            // position is the old centre + direction * (progress - 0.5) to the bit.
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 4);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(1, 1), Sim.Circle));

            for (int t = 0; t < 30 * 3; t++)
            {
                Sim.Tick(world, 1);
                foreach (Int2 cell in new[] { new Int2(1, 1), new Int2(2, 1), new Int2(3, 1), new Int2(4, 1) })
                {
                    if (!world.Belts.HasItemAt(cell)) continue;
                    Vec2 expected = world.TileGrid.CellCenter(cell) + Dir.East.ToVec() * (Sim.ProgressAt(world, cell) - 0.5f);
                    Assert.AreEqual(expected, OnlyItem(world).Position, "tick " + t);
                }
            }
        }

        // ---------------------------------------------------------------- Part B: one free cell

        /// <summary>A circle drill at (2,2) facing east, with rock at (4,2) so the one free cell in front of it
        /// is (3,2). That cell's belt points <paramref name="beltDirection"/>.</summary>
        private static SimWorld OneFreeCellDrill(Dir beltDirection, out Int2 drill, out Int2 free)
        {
            SimWorld world = Sim.NewWorld(20, 12);   // Core at (8,4)-(11,7)
            drill = new Int2(2, 2);
            free = new Int2(3, 2);
            world.Terrain.SetOre(drill, Quad.Circle);
            world.Terrain.Set(new Int2(4, 2), TerrainKind.Rock);
            Sim.LayRun(world, free, beltDirection, 1);
            Sim.Place(world, BuildKind.Drill, drill, Dir.East);
            return world;
        }

        [Test]
        public void ADrillFacingAWall_DeliversIntoTheOneFreeCell_AndTheItemTurnsThere()
        {
            SimWorld world = OneFreeCellDrill(Dir.North, out Int2 drill, out Int2 free);
            Sim.LayRun(world, free + Dir.North.Offset(), Dir.North, 2);

            world.Machines.PortMasksAt(drill, out _, out DirMask outMask);
            Assert.IsTrue(outMask.Has(Dir.East), "the belt turning north in front of it is the drill's outlet");

            Sim.Tick(world, 1);
            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, free), "the mined circle landed on the one free cell");
            Vec2 westEdge = world.TileGrid.CellCenter(free) + new Vec2(-0.5f, 0f);
            Assert.AreEqual(0f, Vec2.Distance(westEdge, OnlyItem(world).Position), Sim.ProgressTol,
                "it starts on the drill-facing edge, not on the cell's south edge");

            Sim.Tick(world, 45);
            Assert.IsTrue(world.Belts.HasItemAt(free + Dir.North.Offset()), "and rode the turn north, out of that side");

            world.Machines.TryGetSnapshot(drill, out MachineSnapshot snapshot);
            ReadingNote note = MachineReading.LiveNote(world, drill, in snapshot);
            Assert.AreNotEqual(NoteSeverity.Warn, note.Severity, "the card does not call a working outlet a problem: " + note.Text);
        }

        [Test]
        public void ABeltPointingIntoAMachine_StaysAnInputOnly()
        {
            // "A belt pointing in is an input, any other belt is an output" stays unambiguous: the drill no
            // longer drops its product onto a belt that would carry it straight back into the drill.
            SimWorld world = OneFreeCellDrill(Dir.West, out Int2 drill, out Int2 free);

            world.Machines.PortMasksAt(drill, out _, out DirMask outMask);
            Assert.IsFalse(outMask.Has(Dir.East), "a belt pointing back in is not an outlet");

            Sim.Tick(world, 60);
            Assert.AreEqual(Shape.None, Sim.ShapeAt(world, free), "so nothing is mined onto it");

            world.Machines.TryGetSnapshot(drill, out MachineSnapshot snapshot);
            Assert.AreEqual(NoteSeverity.Warn, MachineReading.LiveNote(world, drill, in snapshot).Severity,
                "and the card says there is nowhere to push");
        }

        [Test]
        public void ARotatorWithOneFreeCell_HandsItsProductOffRoundTheTurn()
        {
            // Rotator at (4,2) facing east, fed from the west; (5,2) is the only free cell before rock at
            // (6,2), and its belt turns south.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Rotator, new Int2(4, 2), Dir.East);
            world.Terrain.Set(new Int2(6, 2), TerrainKind.Rock);
            Sim.LayRun(world, new Int2(5, 2), Dir.South, 2);   // (5,2), (5,1)

            world.Machines.TryGetSnapshot(new Int2(4, 2), out MachineSnapshot snapshot);
            Assert.IsFalse(snapshot.OutMask.IsEmpty, "the turning belt is wired as the rotator's outlet");

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.BottomHalfCircle));
            Sim.Tick(world, 30 + 23);
            Assert.AreEqual(Sim.LeftHalfCircle, Sim.ShapeAt(world, new Int2(5, 2)),
                "the turned piece left through the one free cell");

            Sim.Tick(world, 30);
            Assert.AreEqual(Sim.LeftHalfCircle, Sim.ShapeAt(world, new Int2(5, 1)), "and went on south");
        }
    }
}
