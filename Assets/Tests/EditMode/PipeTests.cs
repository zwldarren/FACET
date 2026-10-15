using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the pipe: the crossing piece that carries one item over exactly one tile
    /// and drops it on the belt two cells ahead in its facing. The worlds are the 20x12 test map.
    /// </summary>
    public class PipeTests
    {
        /// <summary>A north-facing pipe line: belt (5,3) feeds the pipe at (5,4), which jumps the
        /// tile (5,5) and lands on the belt at (5,6)-(5,7). The 20x12 test map's Core sits at
        /// (8,4)-(11,7), well clear of every cell here.</summary>
        private static SimWorld PipeWorld()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(5, 3), Dir.North, 1);
            Sim.Place(world, BuildKind.Pipe, new Int2(5, 4), Dir.North);
            Sim.LayRun(world, new Int2(5, 6), Dir.North, 2);
            return world;
        }

        [Test]
        public void Pipe_CarriesAnItem_OverOneTile()
        {
            SimWorld world = PipeWorld();

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 3), Sim.Circle));
            Sim.Tick(world, 90);

            Assert.IsFalse(world.Belts.HasItemAt(new Int2(5, 4)), "the pipe swallows the delivery");
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(5, 7)),
                "the item crossed the gap and rode on to the end of the line");
            Assert.AreEqual(1, Sim.ItemCount(world), "nothing lost, nothing duplicated");
            Assert.AreEqual(0, world.Belts.JamCount, "a pipe is transport: it never jams");
        }

        [Test]
        public void Pipe_LetsTwoBeltsCross()
        {
            // The layout the pipe exists for: a vertical line whose middle tile is a horizontal belt.
            // Both lines run along the top of the map, clear of the Core's footprint - a belt pointing into
            // the Core would be banked, which is a different test's business.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(5, 0), Dir.North, 1);
            Sim.Place(world, BuildKind.Pipe, new Int2(5, 1), Dir.North);
            Sim.LayRun(world, new Int2(5, 3), Dir.North, 2);    // (5,3) (5,4)
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 5);     // (3,2) .. (7,2), through (5,2)

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 0), Sim.Circle));
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.TopHalfCircle));
            Sim.Tick(world, 150);

            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, new Int2(5, 4)),
                "the vertical item crossed over the horizontal belt");
            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(world, new Int2(7, 2)),
                "and the horizontal belt never noticed");
            Assert.AreEqual(2, Sim.ItemCount(world));
        }

        [Test]
        public void Pipe_HoldsItsItem_WhenTheLandingIsBlocked()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(5, 3), Dir.North, 1);
            Sim.Place(world, BuildKind.Pipe, new Int2(5, 4), Dir.North);
            Sim.LayRun(world, new Int2(5, 6), Dir.North, 1);   // the landing, with nowhere beyond

            // Park a blocker on the landing cell, then feed the pipe.
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 6), Sim.TopHalfCircle));
            Sim.Tick(world, 60);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(5, 3), Sim.Circle));
            Sim.Tick(world, 90);

            MachineState pipe = Sim.MachineAt(world, new Int2(5, 4));
            Assert.AreEqual(Sim.Circle, pipe.Carried,
                "no room at the landing: the item waits inside the pipe instead of vanishing");
            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(world, new Int2(5, 6)));

            // Make room and the pipe lets go.
            Assert.IsTrue(world.Belts.TryRemove(new Int2(5, 6)));
            Sim.LayRun(world, new Int2(5, 6), Dir.North, 2);
            Sim.Tick(world, 90);

            Assert.AreEqual(Shape.None, Sim.MachineAt(world, new Int2(5, 4)).Carried);
            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, new Int2(5, 7)));
            Assert.AreEqual(1, Sim.ItemCount(world), "the blocker was deleted with its belt; only the circle remains");
        }

        [Test]
        public void Pipe_EatsFromAnySide_AndCarriesAnyShape()
        {
            // Fed from the east instead of from behind: the delivery rule is the same one every
            // machine uses - the belt must point in. And transport takes any shape.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(7, 4), Dir.West, 2);    // (7,4) (6,4), pointing into the pipe
            Sim.Place(world, BuildKind.Pipe, new Int2(5, 4), Dir.North);
            Sim.LayRun(world, new Int2(5, 6), Dir.North, 2);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(7, 4), Sim.Circle));
            Sim.Tick(world, 120);
            Assert.AreEqual(Sim.Circle, Sim.ShapeAt(world, new Int2(5, 7)), "the circle crossed");

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(7, 4), Sim.TopHalfCircle));
            Sim.Tick(world, 120);
            Assert.AreEqual(Sim.TopHalfCircle, Sim.ShapeAt(world, new Int2(5, 6)),
                "and so did the half - a pipe has no diet");
            Assert.AreEqual(2, Sim.ItemCount(world));
            Assert.AreEqual(0, world.Belts.JamCount);
        }

        [Test]
        public void Pipe_CannotSitOnAPatch_OrInsideTheCore()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            world.Terrain.SetOre(new Int2(2, 2), Quad.Circle);

            Assert.IsFalse(world.TryPlace(BuildKind.Pipe, new Int2(2, 2), Dir.North), "patches are for drills");
            Assert.IsFalse(world.TryPlace(BuildKind.Pipe, world.Core.Cell, Dir.North));
            Assert.IsTrue(world.TryPlace(BuildKind.Pipe, new Int2(5, 5), Dir.North));
        }
    }
}
