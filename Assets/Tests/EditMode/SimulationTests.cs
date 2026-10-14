using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the engine-free simulation: the clock, the map, the defended object, and two
    /// invariants worth guarding forever - the tick rate is exactly 30 Hz so the design's balance
    /// numbers stay true, and the simulation is deterministic so a save plus the same input replays
    /// identically.
    /// </summary>
    public class SimulationTests
    {
        [Test]
        public void TickRate_IsExactly30Hz()
        {
            Assert.AreEqual(30f, SimConfig.TickRate, Sim.Tol);
            Assert.AreEqual(1f / 30f, SimConfig.TickDt, 1e-9f);
        }

        [Test]
        public void Core_StandsWhereTheMapSays_AtFullHealth_AndIsNotBuildable()
        {
            // The Core used to be the map's dead centre, which was the same thing while every map was
            // small and square. A map now names its Core tile (the rig puts it at (4,6) of a 16x22 map,
            // not the middle), so this asks that the world builds the Core where the map put it.
            SimWorld world = Sim.Rig();
            Int2 footprint = world.Map.CoreCell;
            float centreX = footprint.X + CoreState.Size * 0.5f;
            float centreY = footprint.Y + CoreState.Size * 0.5f;

            Assert.AreEqual(footprint, world.Core.Cell, "the Core stands where the map says it does");
            Assert.AreEqual(centreX, world.Core.Center.X, Sim.Tol, "and is the defended object, whatever it is centred on");
            Assert.AreEqual(centreY, world.Core.Center.Y, Sim.Tol);
            Assert.AreEqual(world.Config.CoreMaxHp, world.Core.Hp, Sim.Tol);
            Assert.AreEqual(1f, world.Core.HealthFraction, Sim.Tol);
            Assert.IsTrue(world.Core.Alive);

            for (int y = 0; y < CoreState.Size; y++)
            {
                for (int x = 0; x < CoreState.Size; x++)
                {
                    var cell = new Int2(world.Core.Cell.X + x, world.Core.Cell.Y + y);
                    Assert.AreEqual(TileKind.Core, world.TileGrid.Get(cell), cell + " is part of the Core's footprint");
                    Assert.IsFalse(world.CanPlaceBelt(cell), "nothing may be built on the Core: " + cell);
                }
            }

            var beside = new Int2(world.Core.Cell.X + CoreState.Size, world.Core.Cell.Y);
            Assert.IsTrue(world.CanPlaceBelt(beside), "the tile just past the Core's footprint is free: " + beside);
        }

        [Test]
        public void PausedWorld_DoesNotAdvance()
        {
            SimWorld world = Sim.Rig();
            int ticks = world.TickCount;

            world.Paused = true;
            Sim.Tick(world, 10);

            Assert.AreEqual(ticks, world.TickCount);
        }

        [Test]
        public void Selection_ChangesWhilePaused_SoTheBuildBarStaysLive()
        {
            // A pause is exactly when a player re-plans, so the HUD's build bar must keep working while
            // the world is held. Selection is not a world change - the one input the paused branch still
            // applies - which keeps the highlighted tile following the click instead of freezing.
            SimWorld world = Sim.Rig();

            world.Paused = true;
            world.Tick(new InputCommand(selected: BuildKind.Mortar));

            Assert.AreEqual(BuildKind.Mortar, world.SelectedKind, "the palette selected while paused");
            Assert.AreEqual(0, world.TickCount, "and nothing else advanced");
        }

        [Test]
        public void RestartRequested_RebuildsTheRun_OnTheNextTick()
        {
            // The HUD's restart button cannot reach into the middle of a run any more than its start-wave
            // button can, so it asks the world and the request lands on a tick boundary like every other
            // input. This is R's path with the key taken out.
            SimWorld world = Sim.NewWorld(20, 12);
            Assert.IsTrue(world.TryPlaceBelt(new Int2(5, 5), Dir.East));
            Assert.Less(world.Economy.Circles, world.Map.StartCircles, "the belt was paid for");

            world.RequestRestart();
            world.Tick(InputCommand.None);

            Assert.IsFalse(world.Belts.Has(new Int2(5, 5)), "the line is gone");
            Assert.AreEqual(world.Map.StartCircles, world.Economy.Circles, "and the stockpile is back to the start");
            Assert.AreEqual(0, world.TickCount, "the run is at tick zero again");
            Assert.AreEqual(GameStatus.Playing, world.Status);
        }

        [Test]
        public void SameCommandsFromSameStart_ReplayIdentically()
        {
            SimWorld a = Sim.NewWorld(24, 12);
            SimWorld b = Sim.NewWorld(24, 12);

            // An identical mix of belt laying, item spawning and stepping.
            for (int i = 0; i < 40; i++)
            {
                var cmd = Gesture.Aim(new Int2(2 + (i % 9), 1 + (i % 5)), BuildKind.Belt);
                a.Tick(cmd);
                b.Tick(cmd);
            }

            for (int i = 0; i < 20; i++)
            {
                a.Belts.TrySpawnItem(new Int2(2, 2), ShapeType.Circle);
                b.Belts.TrySpawnItem(new Int2(2, 2), ShapeType.Circle);
                Sim.Tick(a, 7);
                Sim.Tick(b, 7);
            }

            var itemsA = Sim.ItemsIn(a);
            var itemsB = Sim.ItemsIn(b);

            Assert.AreEqual(itemsA.Count, itemsB.Count, "identical input must produce identical state");
            for (int i = 0; i < itemsA.Count; i++)
            {
                Assert.AreEqual(itemsA[i].Shape, itemsB[i].Shape, "item " + i + " diverged");
                Assert.AreEqual(0f, Vec2.Distance(itemsA[i].Position, itemsB[i].Position), 0f,
                    "item " + i + " drifted");
            }
        }

        [Test]
        public void Grid_ConvertsBetweenCellsAndWorldSpace()
        {
            var grid = new TileGrid(8, 4);

            Assert.AreEqual(new Int2(0, 0), grid.CellAt(new Vec2(0.1f, 0.9f)));
            Assert.AreEqual(new Int2(3, 2), grid.CellAt(new Vec2(3.99f, 2.01f)));
            Assert.AreEqual(new Vec2(3.5f, 2.5f), grid.CellCenter(new Int2(3, 2)));
            Assert.IsFalse(grid.InBounds(new Int2(8, 0)));
            Assert.IsFalse(grid.InBounds(new Int2(-1, 2)));
        }

        [Test]
        public void Grid_ABeltGoesOnGroundOrOre_ButNotOnABuilding()
        {
            // The rule lives in TerrainField because it reads both layers - the ground and what stands on
            // it - and neither layer alone can answer it.
            var grid = new TileGrid(8, 4);
            var terrain = new TerrainField(grid);
            var cell = new Int2(2, 2);

            Assert.IsTrue(terrain.CanLayBelt(cell), "bare ground takes a belt");

            terrain.SetOre(cell, ShapeType.Circle);
            Assert.IsTrue(terrain.HasOre(cell), "and so does a shape patch: transport crosses ore");
            Assert.IsTrue(terrain.CanLayBelt(cell), "which is the point of the rule");
            Assert.IsFalse(grid.IsOccupied(cell), "but ore is not a building");

            grid.Set(cell, TileKind.Belt);

            Assert.IsTrue(grid.IsOccupied(cell));
            Assert.IsFalse(terrain.CanLayBelt(cell), "an existing belt is in the way");
        }

        [Test]
        public void Grid_ObstaclesTakeNothing_AndNothingWalksThem()
        {
            // Rock, water and the void are the three grounds that stop things, and they stop all three
            // movers: belts, machines and walkers. One rule per question, so a new obstacle is a row in
            // the enum rather than a case in each of them.
            var grid = new TileGrid(8, 4);
            var terrain = new TerrainField(grid);
            var cell = new Int2(3, 1);

            foreach (TerrainKind kind in new[] { TerrainKind.Rock, TerrainKind.Water, TerrainKind.Void })
            {
                terrain.Set(cell, kind);

                Assert.IsFalse(terrain.CanLayBelt(cell), kind + " takes no transport");
                Assert.IsFalse(terrain.CanPlaceMachine(cell, needsOre: false), kind + " takes no machine");
                Assert.IsFalse(terrain.Walkable(cell), "and nothing walks it");
                Assert.IsTrue(kind.IsObstacle(), kind + " is ground the read cursor will describe");
            }

            // Ore is the one ground with an exception, and it is the drill's: the vein takes the machine
            // that mines it, and nothing else.
            terrain.SetOre(cell, ShapeType.Circle);
            Assert.IsTrue(terrain.CanPlaceMachine(cell, needsOre: true), "a vein takes a drill");
            Assert.IsFalse(terrain.CanPlaceMachine(cell, needsOre: false), "and nothing else");

            // Plain ground is the mirror image of that.
            terrain.Set(cell, TerrainKind.Ground);
            Assert.IsTrue(terrain.CanPlaceMachine(cell, needsOre: false));
            Assert.IsFalse(terrain.CanPlaceMachine(cell, needsOre: true), "a drill needs something to mine");
            Assert.IsTrue(terrain.Walkable(cell));
        }

        [Test]
        public void Terrain_OutOfBoundsReadsAsVoid()
        {
            // The answer every caller asks is "may something be there", and off the map that is no.
            // Reading it as Void rather than as ground is what makes the cursor, the belt rule and the
            // flow field agree without each testing the bounds first.
            var terrain = new TerrainField(new TileGrid(8, 4));

            Assert.AreEqual(TerrainKind.Void, terrain.KindAt(new Int2(-1, 2)));
            Assert.AreEqual(TerrainKind.Void, terrain.KindAt(new Int2(8, 2)));
            Assert.IsFalse(terrain.Walkable(new Int2(8, 2)));
            Assert.IsFalse(terrain.CanLayBelt(new Int2(0, 4)));
            Assert.AreEqual(ShapeType.None, terrain.OreAt(new Int2(-1, -1)));
        }
    }
}
