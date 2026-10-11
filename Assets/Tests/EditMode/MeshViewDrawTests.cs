using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The rebuild gate, driven by hand. A view is normally advanced by Unity's frame message - that is
    /// what the PlayMode gate test pins, and it is also why nothing outside a running game could draw
    /// one. The gate is a method (<see cref="MeshView.Draw"/>) for the one caller that has no frames at
    /// all: the Editor's map preview, which draws the game's own views in the Scene view with no Play
    /// mode. These are that caller's contract, and the reason a map can be looked at before it is played.
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
            // draws the map's own rock - so what lands in the mesh is the map's data and not a shape the
            // test asked for.
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
        public void Draw_AfterItsMeshWasDestroyed_MakesAnotherOne()
        {
            // What a Play-mode transition does to a preview that outlives it: the mesh goes, the view stays.
            // A lost mesh is a reason to rebuild - the gate would otherwise sit out an unchanged frame and
            // leave the view either drawing into nothing or throwing at the object that used to be there.
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
