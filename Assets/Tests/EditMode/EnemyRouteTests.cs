using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>The route preview's promise: the line drawn before a wave is the way the wave walks. The trace
    /// makes the walker's own choices, so a runner sent down the field steps only on traced cells, and the
    /// trace ends at the Core.</summary>
    public class EnemyRouteTests
    {
        [Test]
        public void TheTrace_RunsFromTheGate_IntoTheCore()
        {
            SimWorld world = Bent();
            var route = new List<Int2>();

            int cells = world.TraceEnemyRoute(0, route);

            Assert.Greater(cells, 10);
            Assert.AreEqual(world.Map.SpawnPoints[0], route[0], "the route starts at the gate");
            Assert.IsTrue(InCore(world, route[route.Count - 1]), "and ends in the Core");
            for (int i = 1; i < route.Count; i++)
                Assert.AreEqual(1, System.Math.Abs(route[i].X - route[i - 1].X) + System.Math.Abs(route[i].Y - route[i - 1].Y),
                    "one tile at a time, no diagonal jumps");
        }

        [Test]
        public void AWave_WalksOnlyTheTracedCells()
        {
            SimWorld world = Bent();
            var route = new List<Int2>();
            world.TraceEnemyRoute(0, route);
            var onRoute = new HashSet<Int2>(route);

            world.Tick(new InputCommand(startWavePressed: true));
            int checkedSteps = 0;
            for (int t = 0; t < 30 * 60 && world.Status == GameStatus.Playing; t++)
            {
                world.Tick(InputCommand.None);
                for (int id = 1; id <= 3; id++)
                {
                    if (!world.Enemies.TryGetPosition(id, out Vec2 at)) continue;
                    Int2 cell = world.TileGrid.CellAt(at);
                    if (InCore(world, cell)) continue;
                    Assert.IsTrue(onRoute.Contains(cell), "runner " + id + " left the drawn route at " + cell);
                    checkedSteps++;
                }
            }
            Assert.Greater(checkedSteps, 30, "the runners actually walked");
        }

        [Test]
        public void ASpawnPointTheMapDoesNotHave_TracesNothing()
        {
            var route = new List<Int2> { new Int2(1, 1) };
            Assert.AreEqual(0, Bent().TraceEnemyRoute(5, route));
            Assert.AreEqual(0, route.Count);
        }

        private static bool InCore(SimWorld world, Int2 c)
            => c.X >= world.Core.Cell.X && c.X < world.Core.Cell.X + CoreState.Size
            && c.Y >= world.Core.Cell.Y && c.Y < world.Core.Cell.Y + CoreState.Size;

        /// <summary>A gate on the right, the Core on the left, and a rock wall in between with one gap at the
        /// top: the way in has corners, so a route that only went straight would be caught.</summary>
        private static SimWorld Bent()
        {
            const int w = 24, h = 14;
            var ground = new TerrainKind[w * h];
            for (int y = 0; y < h - 3; y++) ground[y * w + 12] = TerrainKind.Rock;

            var map = new MapDefinition("Bent", w, h, 100,
                new ShapePatch[0],
                new[] { new Int2(22, 3) },
                new[] { new WaveDefinition(5f, new SpawnGroup(EnemyKind.Runner, 3, 0, 0.5f, 1f, Sim.Circle)) },
                ground, new Int2(2, 2), Operators.None, 0, 0, null);
            Assert.AreEqual(0, MapValidation.Validate(map).Count);
            return new SimWorld(map, new SimConfig());
        }
    }
}
