using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the belt layer, the mechanic the whole design hangs off. The two that matter most
    /// are <see cref="PackedBelt_ShiftsAsATrain_InsteadOfDeadlocking"/> and
    /// <see cref="Throughput_IsTheSameFacingAnyDirection"/>: a packed-full belt is where every item hits its
    /// cell's exit edge on the same tick with every successor occupied - exactly where a naive implementation
    /// either deadlocks or silently drains at one item per tick. If those two go red, the game is lying about
    /// its own throughput, the one number the design asks the player to reason with.
    /// </summary>
    public class BeltTests
    {

        [Test]
        public void Item_TakesExactlyOneSecondPerCell()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 5);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(1, 1), ShapeType.Circle));

            Sim.Tick(world, 29);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(1, 1)), "still on its first cell after 29 ticks");
            Assert.AreEqual(29f / 30f, Sim.ProgressAt(world, new Int2(1, 1)), Sim.ProgressTol);

            Sim.Tick(world, 1);
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(1, 1)), "one second per cell: it left on tick 30");
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(2, 1)));
            Assert.AreEqual(0f, Sim.ProgressAt(world, new Int2(2, 1)), Sim.ProgressTol);
        }

        [Test]
        public void PackedBelt_ShiftsAsATrain_InsteadOfDeadlocking()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 8);   // (1,1) .. (8,1)

            // Pack cells 1..7 and leave the head cell empty, so the train has somewhere to go.
            for (int x = 1; x <= 7; x++)
                Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(x, 1), ShapeType.HalfCircle));

            Sim.Tick(world, 30);

            Assert.IsFalse(world.Belts.HasItemAt(new Int2(1, 1)), "the tail cell must empty");
            for (int x = 2; x <= 8; x++)
                Assert.IsTrue(world.Belts.HasItemAt(new Int2(x, 1)),
                    "cell (" + x + ",1) should have taken its predecessor's item in the same tick");

            Assert.AreEqual(7, Sim.ItemCount(world), "no item may be lost or duplicated");
        }

        /// <summary>
        /// The jammed cells are separately enumerable: "how many" is a diagnostic, "which ones" is what an
        /// off-screen player has to be told - a jam that cannot be found is a jam that cannot be right-clicked.
        /// </summary>
        [Test]
        public void GetJammed_ListsEveryBlockedCell_AndNothingWhenClear()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(2, 5), Dir.East, 3);           // (2,5) (3,5) (4,5)

            var cells = new List<Int2>();
            world.Belts.GetJammed(cells);
            Assert.AreEqual(0, cells.Count, "a line that is not jammed has nothing to find");

            world.Belts.TryJam(new Int2(3, 5));
            world.Belts.TryJam(new Int2(4, 5));

            world.Belts.GetJammed(cells);
            CollectionAssert.AreEqual(new[] { new Int2(3, 5), new Int2(4, 5) }, cells,
                "every jammed cell, in the field's own index order");

            world.Belts.TryClearJam(new Int2(3, 5));
            world.Belts.GetJammed(cells);
            CollectionAssert.AreEqual(new[] { new Int2(4, 5) }, cells, "and a cleared one stops being listed");
        }

        [Test]
        public void Throughput_IsTheSameFacingAnyDirection()
        {
            // Guards against iteration order leaking into the result: if the sweep favoured one flow
            // direction, these four would disagree.
            foreach (Dir dir in new[] { Dir.North, Dir.East, Dir.South, Dir.West })
            {
                SimWorld world = Sim.NewWorld(20, 20);        // Core sits at (10,10)
                var head = new Int2(6, 6);
                Sim.LayRun(world, head, dir, 6);

                Int2 cell = head;
                for (int i = 0; i < 5; i++)
                {
                    Assert.IsTrue(world.Belts.TrySpawnItem(cell, ShapeType.Circle));
                    cell = cell + dir.Offset();
                }

                Sim.Tick(world, 30);

                Assert.IsFalse(world.Belts.HasItemAt(head), dir + ": the tail cell must empty");
                Assert.IsTrue(world.Belts.HasItemAt(head + dir.Offset() * 5),
                    dir + ": the train must reach the free head cell");
                Assert.AreEqual(5, Sim.ItemCount(world), dir + ": no item may be lost");
            }
        }

        [Test]
        public void ItemPositions_StepNormallyAcrossAHandoff()
        {
            // Progress 1 of a cell and progress 0 of its successor are the same world point, so a
            // hand-off must cost exactly one ordinary tick of travel - not half a tile.
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 3);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(1, 1), ShapeType.Circle));

            Sim.Tick(world, 29);
            Sim.Tick(world, 1);

            ItemSnapshot item = Sim.ItemsIn(world)[0];
            Assert.AreEqual(1f / 30f, Vec2.Distance(item.PreviousPosition, item.Position), Sim.ProgressTol,
                "a hand-off must move the item one normal tick step, not half a tile");

            Vec2 boundary = world.TileGrid.CellCenter(new Int2(1, 1)) + Dir.East.ToVec() * 0.5f;
            Assert.AreEqual(0f, Vec2.Distance(boundary, item.Position), Sim.ProgressTol,
                "and it should be sitting on the shared edge");
        }

        [Test]
        public void BackPressure_ABlockedItemNeverEntersTheCellAheadOfIt()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 4);   // (1,1) .. (4,1)

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(4, 1), ShapeType.Circle));  // head, nowhere to go
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 1), ShapeType.HalfCircle));

            Sim.Tick(world, 120);

            Assert.IsTrue(world.Belts.TryGet(new Int2(4, 1), out BeltState head));
            Assert.AreEqual(ShapeType.Circle, head.Item, "the two items must not swap");
            Assert.AreEqual(1f, head.Progress, Sim.ProgressTol);

            Assert.IsTrue(world.Belts.TryGet(new Int2(3, 1), out BeltState queued));
            Assert.AreEqual(ShapeType.HalfCircle, queued.Item);
            Assert.AreEqual(1f, queued.Progress, Sim.ProgressTol, "queued on the boundary behind it");

            Assert.AreEqual(2, Sim.ItemCount(world));
        }

        [Test]
        public void Item_TurnsAtACorner()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Assert.IsTrue(world.TryPlaceBelt(new Int2(1, 1), Dir.East));
            Assert.IsTrue(world.TryPlaceBelt(new Int2(2, 1), Dir.East));
            Assert.IsTrue(world.TryPlaceBelt(new Int2(3, 1), Dir.North));   // the corner
            Assert.IsTrue(world.TryPlaceBelt(new Int2(3, 2), Dir.North));
            Assert.IsTrue(world.TryPlaceBelt(new Int2(3, 3), Dir.North));

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(1, 1), ShapeType.Circle));

            Sim.Tick(world, 60);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(3, 1)), "three cells in: the corner");

            Sim.Tick(world, 30);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(3, 2)), "and it turned north rather than stalling");
        }

        [Test]
        public void Jam_FreezesTheLine_AndClearingItReleasesIt()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 5);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(1, 1), ShapeType.Circle));
            Assert.IsTrue(world.Belts.TryJam(new Int2(3, 1)));

            Sim.Tick(world, 90);

            Assert.IsTrue(world.Belts.IsJammed(new Int2(3, 1)));
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(3, 1)), "nothing reaches the jammed cell");

            Assert.IsTrue(world.Belts.HasItemAt(new Int2(2, 1)), "the line backed up behind the jam");
            Assert.AreEqual(1f, Sim.ProgressAt(world, new Int2(2, 1)), Sim.ProgressTol);

            Assert.IsTrue(world.Belts.TryClearJam(new Int2(3, 1)));
            Sim.Tick(world, 1);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(3, 1)), "clearing the jam lets the line move again");
        }

        [Test]
        public void ClearingAJam_DestroysTheItemThatCausedIt()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 3);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(2, 1), ShapeType.Circle));
            Assert.IsTrue(world.Belts.TryJam(new Int2(2, 1)));

            Sim.Tick(world, 10);
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(2, 1)), "a jammed cell keeps its item where it stood");

            Assert.IsTrue(world.Belts.TryClearJam(new Int2(2, 1)));

            Assert.IsFalse(world.Belts.HasItemAt(new Int2(2, 1)),
                "leaving the offending item in place would re-jam the same cell next tick");
            Assert.IsFalse(world.Belts.IsJammed(new Int2(2, 1)));
        }

        [Test]
        public void JammedCell_RefusesNewItems()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 2);
            Assert.IsTrue(world.Belts.TryJam(new Int2(2, 1)));

            Assert.IsFalse(world.Belts.TrySpawnItem(new Int2(2, 1), ShapeType.Circle));
        }

        [Test]
        public void Drag_BuildsAStraightRunFacingTheDragDirection()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.Drag(world, new Int2(2, 1), new Int2(2, 4));

            for (int y = 1; y <= 4; y++)
                Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(2, y)),
                    "the run should face the way the cursor was dragged");

            Assert.IsFalse(world.Belts.Has(new Int2(2, 0)), "the run must not bleed past the press cell");
            Assert.IsFalse(world.Belts.Has(new Int2(2, 5)), "nor past the cursor");
        }

        [Test]
        public void Drag_TurnsTheCorner_WhenTheCursorJumpsDiagonally()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.Drag(world, new Int2(1, 1), new Int2(4, 3));

            // Manhattan, the axis the drag ran further along first: a run east along y=1, then north.
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(1, 1)));
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(2, 1)));
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(3, 1)));
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(4, 1)), "the corner turns north");
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(4, 2)));
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(4, 3)));
        }

        [Test]
        public void Drag_LeavesThePressCellAlongTheDragsOwnAxis()
        {
            // A drag up the map has to leave the press cell *up*: the first leg is the axis the drag ran further
            // along, so the sideways step lands at the far end. Always stepping in X first made every run come out
            // sideways whatever the player dragged - and a run drawn north out of a drill landed one tile east of it.
            SimWorld world = Sim.NewWorld(16, 12);
            Sim.Drag(world, new Int2(1, 1), new Int2(4, 8));

            Assert.IsFalse(world.Belts.Has(new Int2(2, 1)), "the run must not step sideways first");
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(1, 1)), "it starts upwards");
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(1, 7)));
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(1, 8)), "and turns at the far end");
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(4, 8)));

            // A mostly-horizontal drag is unaffected: X is the axis *it* ran further along.
            SimWorld wide = Sim.NewWorld(16, 12);
            Sim.Drag(wide, new Int2(1, 1), new Int2(8, 3));
            Assert.AreEqual(Dir.East, Sim.DirAt(wide, new Int2(1, 1)));
            Assert.AreEqual(Dir.North, Sim.DirAt(wide, new Int2(8, 1)), "the corner turns north");
        }

        [Test]
        public void DragOutOfADrill_PointsTheDrillAtTheRun_SoAVerticalLineIsFed()
        {
            // The reported bug, as a test: a drill placed by a plain click keeps the ghost's default facing
            // (east), so a belt dragged north out of it was never mined into, and the run looked like the
            // mistake while the drill's facing was the cause. A run leaving a drill's own cell can only be that
            // drill's output, so the drill turns to face it. The map is this test's own - the drag is on trial.
            const int Dragged = 4;                          // cells the run leaves the drill for
            var drill = new Int2(4, 2);
            var map = new MapDefinition("drill run", 16, 12, Sim.TestCircles,
                new[] { new ShapePatch(drill, 1, 1, ShapeType.Circle) }, new Int2[0], new WaveDefinition[0]);
            var world = new SimWorld(map, new SimConfig());

            for (int i = 0; i < Dragged; i++)
            {
                var next = new Int2(drill.X, drill.Y + i + 1);
                Assert.AreEqual(TerrainKind.Ground, world.Terrain.KindAt(next),
                    "the run needs plain ground north of the vein: " + next);
                Assert.IsFalse(world.Terrain.HasOre(next), "and not more of the vein: " + next);
            }

            var head = new Int2(drill.X, drill.Y + 1);       // the first cell off the patch

            world.Tick(Gesture.Aim(drill, BuildKind.Drill));
            world.Tick(Gesture.Release(drill, BuildKind.Drill));
            Assert.AreEqual(Dir.East, Sim.MachineAt(world, drill).Direction, "a click keeps the ghost's facing");

            Sim.Drag(world, drill, new Int2(drill.X, drill.Y + Dragged));

            Assert.AreEqual(Dir.North, Sim.MachineAt(world, drill).Direction, "the drill follows the run");
            Assert.AreEqual(Dir.North, Sim.DirAt(world, head), "and the run leaves it northwards");

            Sim.Tick(world, 120);
            Assert.Greater(Sim.ItemCount(world), 0, "so the shape it mines reaches the belt at last");
        }

        [Test]
        public void DragOutOfADecomposer_LeavesItsFacingAlone()
        {
            // A decomposer has no facing to point: its inlet is the belt pointing into it and its outlets are
            // the belts pointing away, read fresh each tick. So a run dragged out of one wires itself up - the
            // belt leaving southwards is an outlet the moment it exists - and the machine keeps its placed
            // facing. (The drill is the other case: its facing *is* its output, so a run leaving it turns it.)
            SimWorld world = Sim.NewWorld(16, 12);
            var decomposer = new Int2(2, 8);

            world.Tick(Gesture.Aim(decomposer, BuildKind.Decomposer));
            world.Tick(Gesture.Release(decomposer, BuildKind.Decomposer));
            Assert.AreEqual(Dir.East, Sim.MachineAt(world, decomposer).Direction);

            Sim.Drag(world, decomposer, new Int2(2, 4));

            Assert.AreEqual(Dir.East, Sim.MachineAt(world, decomposer).Direction,
                "a machine whose ports are read off the belts is not turned by a run");
            Assert.AreEqual(Dir.South, Sim.DirAt(world, new Int2(2, 7)),
                "the run itself still leaves southwards, which is what makes that side an outlet");
        }

        [Test]
        public void TheCursorGhost_ShowsTheRunBeingDragged()
        {
            // The chevron under the cursor used to keep pointing wherever Q/E left the ghost, so a belt
            // dragged north still advertised itself as an east-west belt.
            SimWorld world = Sim.NewWorld(16, 12);

            world.Tick(Gesture.Aim(new Int2(4, 4), BuildKind.Belt));
            world.Tick(Gesture.Aim(new Int2(4, 9), BuildKind.Belt));

            Assert.AreEqual(Dir.North, world.PlacementDirection, "the ghost follows the drag");

            world.Tick(InputCommand.None);
            Assert.AreEqual(Dir.East, world.PlacementDirection, "and its own facing is back when the drag ends");
        }

        [Test]
        public void Drag_SkipsABlockedCell_WithoutRepointingIt()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Assert.IsTrue(world.TryPlaceBelt(new Int2(3, 1), Dir.South));   // laid before the drag

            Sim.Drag(world, new Int2(1, 1), new Int2(5, 1));

            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(1, 1)));
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(2, 1)));
            Assert.AreEqual(Dir.South, Sim.DirAt(world, new Int2(3, 1)),
                "the drag must not rotate a belt it did not lay");
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(4, 1)), "and it resumes on the far side");
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(5, 1)));
        }

        [Test]
        public void PressingOntoAnExistingRun_PointsItTheWayYouDrag()
        {
            // Drawing a run in two segments is normal play, so pressing the end of a run and dragging on
            // has to turn that corner instead of leaving it facing the old way.
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 3);   // (1,1) (2,1) (3,1), all East

            Sim.Drag(world, new Int2(3, 1), new Int2(3, 3));

            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(3, 1)), "the corner must turn");
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(3, 2)));
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(3, 3)));
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(2, 1)), "the rest of the run is untouched");
        }

        [Test]
        public void ABareClickLaysOneBelt_BecauseTheGhostNamesItsDirection()
        {
            // A click used to lay nothing, on the theory that a belt needs a direction. It has one - the ghost's,
            // which Q/E turn and the info card names - so a click that built nothing promised a belt and delivered
            // silence, which is what "the game won't let me build" looks like from outside.
            SimWorld world = Sim.NewWorld(16, 10);
            var click = Gesture.Aim(new Int2(2, 2), BuildKind.Belt);
            Sim.Tick(world, click, 10);
            Sim.Tick(world, 1);

            Assert.IsTrue(world.Belts.Has(new Int2(2, 2)), "the click lays the cell under the cursor");
            Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(2, 2)), "facing the ghost");

            // The ghost turns, so the click after it does too.
            world.Tick(new InputCommand(cursorCell: new Int2(5, 5), selected: BuildKind.Belt, rotateSteps: 1));
            Sim.Tick(world, Gesture.Aim(new Int2(5, 5), BuildKind.Belt), 2);
            world.Tick(InputCommand.None);

            Assert.AreEqual(Dir.South, Sim.DirAt(world, new Int2(5, 5)), "one clockwise turn from east");
            Assert.AreEqual(0, Sim.ItemCount(world));
        }

        [Test]
        public void RightDrag_ErasesTheRunItSweeps()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 4);

            var erase = Gesture.Removal(new Int2(2, 1));
            Sim.Tick(world, erase, 2);
            world.Tick(Gesture.Removal(new Int2(3, 1)));
            Sim.Tick(world, 1);

            Assert.IsFalse(world.Belts.Has(new Int2(2, 1)));
            Assert.IsFalse(world.Belts.Has(new Int2(3, 1)));
            Assert.IsTrue(world.Belts.Has(new Int2(1, 1)), "only what the cursor swept is gone");
            Assert.IsTrue(world.Belts.Has(new Int2(4, 1)));
        }

        [Test]
        public void Belt_RefusesOutOfBoundsOccupiedAndCoreCells()
        {
            SimWorld world = Sim.NewWorld(16, 10);

            Assert.IsFalse(world.TryPlaceBelt(new Int2(-1, 6), Dir.East));
            Assert.IsFalse(world.TryPlaceBelt(new Int2(16, 6), Dir.East));
            Assert.IsFalse(world.TryPlaceBelt(world.Core.Cell, Dir.East), "the Core is not buildable");

            Assert.IsTrue(world.TryPlaceBelt(new Int2(2, 2), Dir.East));
            Assert.IsFalse(world.TryPlaceBelt(new Int2(2, 2), Dir.East), "no stacking belts");
        }

        [Test]
        public void RemovingABelt_DestroysTheItemRidingIt()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 2);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(1, 1), ShapeType.HalfCircle));

            Assert.IsTrue(world.Belts.TryRemove(new Int2(1, 1)));

            Assert.IsFalse(world.Belts.Has(new Int2(1, 1)));
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(1, 1)));
            Assert.AreEqual(0, Sim.ItemCount(world));
        }

        [Test]
        public void Item_RefusesOccupiedAndBareCells()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 2);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(1, 1), ShapeType.HalfCircle));
            Assert.IsFalse(world.Belts.TrySpawnItem(new Int2(1, 1), ShapeType.Circle), "one item per cell");
            Assert.IsFalse(world.Belts.TrySpawnItem(new Int2(9, 9), ShapeType.Circle), "not onto bare ground");
        }

        [Test]
        public void EveryBeltMutation_BumpsTheRenderRevision()
        {
            // The belt mesh only rebuilds when this counter moves, so a mutator that forgets to bump
            // it leaves the player staring at a belt that is not on screen. Trivially easy to miss
            // when adding a mutator, and invisible in every other test, so it gets its own.
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 3);

            int revision = world.Belts.Revision;
            Assert.Greater(revision, 0, "laying a belt must be visible to the renderer");

            Assert.IsTrue(world.Belts.TrySetDirection(new Int2(1, 1), Dir.North));
            Assert.Greater(world.Belts.Revision, revision, "re-pointing");
            revision = world.Belts.Revision;

            Assert.IsTrue(world.Belts.TryJam(new Int2(2, 1)));
            Assert.Greater(world.Belts.Revision, revision, "jamming");
            revision = world.Belts.Revision;

            Assert.IsTrue(world.Belts.TryClearJam(new Int2(2, 1)));
            Assert.Greater(world.Belts.Revision, revision, "clearing a jam");
            revision = world.Belts.Revision;

            Assert.IsTrue(world.Belts.TryRemove(new Int2(3, 1)));
            Assert.Greater(world.Belts.Revision, revision, "removing");
        }

        [Test]
        public void RemovingAndReplacingABelt_ClearsTheStaleState()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(1, 1), Dir.East, 2);
            Assert.IsTrue(world.Belts.TryJam(new Int2(1, 1)));
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(2, 1), ShapeType.Circle));

            Assert.IsTrue(world.Belts.TryRemove(new Int2(1, 1)));
            Assert.IsTrue(world.Belts.TryRemove(new Int2(2, 1)));
            Assert.IsTrue(world.TryPlaceBelt(new Int2(2, 1), Dir.North));

            Assert.IsFalse(world.Belts.IsJammed(new Int2(2, 1)), "a rebuilt cell starts clean");
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(2, 1)));
            Assert.AreEqual(Dir.North, Sim.DirAt(world, new Int2(2, 1)));
            Assert.AreEqual(0, Sim.ItemCount(world));
        }

        [Test]
        public void ADraggedRun_CrossesOre_InsteadOfBreakingOnIt()
        {
            // A patch used to be treated like the Core by the drag: the cell was skipped and the direction link
            // dropped, so a line over a vein came out as two runs with a one-cell hole - the hole being the ore
            // the player was trying to thread. Ore is ground a belt can be laid on, so the run is unbroken.
            SimWorld world = Sim.NewWorld(20, 12);
            for (int x = 0; x < 3; x++)
                world.Terrain.SetOre(new Int2(6 + x, 8), ShapeType.Circle);

            Sim.Route(world, new Int2(3, 8), new Int2(12, 8));

            for (int x = 3; x <= 12; x++)
                Assert.AreEqual(Dir.East, Sim.DirAt(world, new Int2(x, 8)), "the run is unbroken at " + x);

            for (int x = 0; x < 3; x++)
                Assert.IsTrue(world.Terrain.HasOre(new Int2(6 + x, 8)), "and the vein is still under it");
        }

        [Test]
        public void ABeltOnOre_GivesTheOreBack_WhenRemoved()
        {
            // The hazard that comes with the rule above: a belt on a patch is the one case where clearing a tile
            // could delete the map's terrain - permanently and silently, leaving the player a vein short one cell.
            // Removing the belt is a right-click that refunds, so the only thing it may change is the belt.
            SimWorld world = Sim.NewWorld(20, 12);
            var ore = new Int2(7, 7);
            world.Terrain.SetOre(ore, ShapeType.Square);

            Assert.IsTrue(world.TryPlaceBelt(ore, Dir.East));
            Assert.IsTrue(world.Terrain.HasOre(ore), "the ore is under the belt, not gone");

            Assert.IsTrue(world.Belts.TryRemove(ore));

            Assert.IsFalse(world.TileGrid.Get(ore).IsBuilding(), "and nothing is left standing on the vein");
            Assert.IsTrue(world.Terrain.HasOre(ore), "the vein is back");
            Assert.AreEqual(ShapeType.Square, world.Terrain.OreAt(ore), "with its own shape");
            Assert.IsTrue(world.CanPlaceBelt(ore), "and it can be built over again");
        }
        [Test]
        public void ADragKeepsBuildingWhatThePressStarted_WhenTheSelectionChanges()
        {
            // The press decides, so the gesture cannot be re-purposed under the player's hand. Before, aiming a
            // drill at a patch and tapping the belt key mid-drag turned the held button into "lay a belt run from
            // the press cell" - which would pave the very cell the drill was aimed at. Ghost and outcome agree.
            SimWorld world = Sim.NewWorld(20, 12);
            var ore = new Int2(3, 9);
            world.Terrain.SetOre(ore, ShapeType.Circle);

            world.Tick(Gesture.Aim(ore, BuildKind.Drill));
            Assert.AreEqual(BuildKind.Drill, world.SelectedKind, "the press takes the selection");

            world.Tick(Gesture.Aim(new Int2(8, 9), BuildKind.Belt));
            Assert.AreEqual(BuildKind.Drill, world.SelectedKind, "which then waits for the next gesture");
            Assert.IsFalse(world.Belts.Has(new Int2(4, 9)), "the drag did not become a belt run");
            Assert.IsFalse(world.Belts.Has(ore), "and the cell it was aimed at is still ore");

            world.Tick(Gesture.Release(new Int2(8, 9), BuildKind.Belt));

            Assert.IsTrue(world.Machines.Has(ore), "the drill landed where the press was");
            Assert.AreEqual(Dir.East, Sim.MachineAt(world, ore).Direction, "aimed by the drag");
            Assert.AreEqual(ShapeType.Circle, world.Terrain.OreAt(ore), "on the ore, which is untouched");

            world.Tick(new InputCommand(cursorCell: new Int2(8, 9), selected: BuildKind.Belt));
            Assert.AreEqual(BuildKind.Belt, world.SelectedKind, "and the next gesture gets the new selection");
        }
    }
}
