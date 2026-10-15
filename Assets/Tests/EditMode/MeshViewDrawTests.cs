using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The rebuild gate, driven by hand. A view is normally advanced by Unity's frame message - what the
    /// PlayMode gate test pins, and why nothing outside a running game could draw one. The gate is a
    /// method (<see cref="MeshView.Draw"/>) for the one caller with no frames at all: the Editor's map
    /// preview, which draws the game's views in the Scene view. These are that caller's contract.
    /// </summary>
    public class MeshViewDrawTests
    {
        /// <summary>A frame with a real pixel size: the gate refuses to draw at zero zoom, which is the
        /// one frame a preview could otherwise hand it while a Scene view is still being laid out.</summary>
        private static readonly ViewFrame Frame = new ViewFrame(1f, 0.02f);

        private GameObject _object;
        private Palette _palette;
        private FrameClock _clock;
        private CountingView _view;

        [SetUp]
        public void CreateView()
        {
            _object = new GameObject("Counting");
            _view = _object.AddComponent<CountingView>();

            _palette = ScriptableObject.CreateInstance<Palette>();
            _clock = new FrameClock { Frame = Frame };

            _view.Initialize(Sim.NewWorld(8, 4), _clock, _palette);
        }

        [TearDown]
        public void DestroyView()
        {
            if (_object != null) Object.DestroyImmediate(_object);
            if (_palette != null) Object.DestroyImmediate(_palette);
        }

        [Test]
        public void Draw_BuildsWithNoFrame_AndGatesTheSameWay()
        {
            _view.Draw(_clock.Frame);
            Assert.AreEqual(1, _view.Appends, "a frame handed in by hand builds the mesh once");

            _view.Draw(_clock.Frame);
            Assert.AreEqual(1, _view.Appends, "the same frame again does not rebuild");

            _view.Draw(new ViewFrame(1f, Frame.WorldPerPixel * 1.15f));
            Assert.AreEqual(2, _view.Appends, "a zoom past the tolerance rebuilds, as it does in game");

            _view.Draw(new ViewFrame(0f, 0f));
            Assert.AreEqual(2, _view.Appends, "and a frame with no zoom is refused rather than drawn");
        }

        [Test]
        public void Draw_OfARealView_PutsTheMapsObstaclesInAMesh()
        {
            // The map the preview is really for: ground that is not all ground, drawn by the view that
            // draws the map's own rock - so what lands in the mesh is the map's data, not a shape the test
            // asked for.
            var go = new GameObject("Terrain");
            try
            {
                TerrainRenderer view = go.AddComponent<TerrainRenderer>();
                view.Initialize(RockyWorld(), _clock, _palette);
                view.Draw(_clock.Frame);

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the view draws through a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");
                Assert.Greater(filter.sharedMesh.vertexCount, 0, "with the map's rock in it");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Draw_OfTheItemView_DrawsTheFilledQuadrantsAsOneFigure()
        {
            // An item is drawn as its quadrants, so the mesh grows with them: nothing for a hole, one mark
            // for a quarter, two for a half and four for a full circle. That is what "a full circle reads as
            // a disc and a top half-circle as half a disc" means once the geometry is counted.
            int quarter = ItemVertices(Shape.Quarter(Shape.NW, Quad.Circle));
            int half = ItemVertices(Sim.TopHalfCircle);
            int full = ItemVertices(Sim.Circle);

            Assert.Greater(quarter, 0, "a filled quadrant is geometry - the empty ones are not");
            Assert.Greater(half, quarter, "two quadrants are more than one");
            Assert.Greater(full, half, "and a full circle is four of them");
        }

        /// <summary>The item view's mesh size for a world whose one belt cell carries the given item, drawn
        /// by hand the way the preview draws it. The cell is clear of the Core, which an 8x4 test map puts
        /// down its middle.</summary>
        private int ItemVertices(Shape item)
        {
            SimWorld world = Sim.NewWorld(8, 4);
            var cell = new Int2(0, 1);
            Assert.IsTrue(world.TryPlaceBelt(cell, Dir.East), "the item's cell is free");
            Assert.IsTrue(world.Belts.TrySpawnItem(cell, item), "the belt takes the item");

            var go = new GameObject("Items");
            try
            {
                ItemRenderer view = go.AddComponent<ItemRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the view draws through a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");
                return filter.sharedMesh.vertexCount;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Draw_AfterItsMeshWasDestroyed_MakesAnotherOne()
        {
            // What a Play-mode transition does to a preview that outlives it: the mesh goes, the view stays.
            // A lost mesh is a reason to rebuild - otherwise the gate would sit out an unchanged frame and
            // leave the view drawing into nothing.
            var go = new GameObject("Terrain");
            try
            {
                TerrainRenderer view = go.AddComponent<TerrainRenderer>();
                view.Initialize(RockyWorld(), _clock, _palette);
                view.Draw(_clock.Frame);

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Mesh first = filter.sharedMesh;
                Object.DestroyImmediate(first);

                Assert.DoesNotThrow(() => view.Draw(_clock.Frame), "the same unchanged frame is still drawn");

                Mesh second = filter.sharedMesh;
                Assert.AreNotSame(first, second, "into a mesh made again");
                Assert.Greater(second.vertexCount, 0, "and filled with what the frame appends");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Initialize_TheMeshLivesAndDiesWithTheViewThatMadeIt()
        {
            // A preview object is DontSave so a scene reload does not take it away, so its mesh has to be
            // too - and then nothing else frees it, which is the view's own job.
            var persistent = new GameObject("Preview") { hideFlags = HideFlags.HideAndDontSave };
            var scene = new GameObject("Scene");
            try
            {
                Mesh kept = MeshOf(persistent);
                Mesh ordinary = MeshOf(scene);

                Assert.AreEqual(HideFlags.DontSave, kept.hideFlags,
                    "a DontSave view's mesh is DontSave too, so a scene unload does not take the geometry away from a view that survives it");
                Assert.AreEqual(HideFlags.None, ordinary.hideFlags,
                    "and a view that belongs to a scene asks for no special hold on it");

                Object.DestroyImmediate(persistent);
                Assert.IsTrue(kept == null,
                    "the view frees its own DontSave mesh when it goes, rather than leaking one per object");
            }
            finally
            {
                if (scene != null) Object.DestroyImmediate(scene);
                if (persistent != null) Object.DestroyImmediate(persistent);
            }
        }

        [Test]
        public void APlacedCutter_DrawsItsCutLine_WhereARotatorDrawsNone()
        {
            // The cut line is a placed cutter's one facing readout - the outlets are read off the belts,
            // so without the line nothing on the machine says which two pieces it is making, and a line
            // of cutters all reads as the same machine whatever way each is aimed. A rotator in the same
            // empty state draws the same body with no line, so the mesh difference is the line alone.
            int cutter = OperatorVertices(BuildKind.Cutter, Dir.North);
            int rotator = OperatorVertices(BuildKind.Rotator, Dir.North);

            Assert.Greater(cutter, rotator, "the cutter's blade is geometry its fellow operator lacks");
        }

        [Test]
        public void TheCutterGhost_DrawsItsCutLine_WhereADrillGhostDrawsNone()
        {
            // The ghost is where the aim is chosen, so the blade is drawn there too: a ghost faced north
            // reads "top and bottom pieces" before the click rather than after. A drill's ghost shows
            // the same body, border and facing chevron with no line, so the mesh difference is the line.
            int cutter = GhostVertices(BuildKind.Cutter);
            int drill = GhostVertices(BuildKind.Drill);

            Assert.Greater(cutter, drill, "the ghost's blade is geometry a drill's ghost lacks");
        }

        /// <summary>A map with rock in it, so a real view has something to put in the mesh.</summary>
        private static SimWorld RockyWorld()
            => new SimWorld(new MapDefinition("rocky", 8, 4, 0,
                new ShapePatch[0], new Int2[0], new WaveDefinition[0],
                TerrainLayout.Rects(8, 4, new TerrainRect(new Int2(1, 1), 3, 2, TerrainKind.Rock))),
                new SimConfig());

        /// <summary>A view on the given object, and the mesh it made for itself.</summary>
        private Mesh MeshOf(GameObject go)
        {
            CountingView view = go.AddComponent<CountingView>();
            view.Initialize(Sim.NewWorld(8, 4), _clock, _palette);

            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh, go.name + " drew no mesh");
            return mesh;
        }

        /// <summary>The machine view's mesh size for one operator placed alone on bare ground: no belts
        /// wired, so no stubs, and an empty chamber, so no badge - the two operators differ by the cut
        /// line and nothing else. Drawn by hand the way the preview draws it.</summary>
        private int OperatorVertices(BuildKind kind, Dir facing)
        {
            SimWorld world = Sim.NewWorld(8, 4);
            Sim.Place(world, kind, new Int2(1, 1), facing);

            var go = new GameObject("Machines");
            try
            {
                MachineRenderer view = go.AddComponent<MachineRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the machine view draws through a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");
                return filter.sharedMesh.vertexCount;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>The cursor view's mesh size with the given kind selected and the cursor on bare
        /// ground - the ghost's own geometry, nothing pinned and no turret ring.</summary>
        private int GhostVertices(BuildKind kind)
        {
            SimWorld world = Sim.NewWorld(8, 4);
            world.Tick(new InputCommand(selected: kind));

            var go = new GameObject("Cursor");
            try
            {
                CursorView view = go.AddComponent<CursorView>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the cursor view draws through a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");
                return filter.sharedMesh.vertexCount;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>A view whose rebuilds can be counted, so the gate is exercised through the same
        /// interface the preview uses rather than around it.</summary>
        private sealed class CountingView : MeshView
        {
            public int Appends { get; private set; }

            protected override Palette.Layer Layer => Colors.GridLayer;

            protected override void AppendFrame(in ViewFrame frame) => Appends++;
        }
    }
}
