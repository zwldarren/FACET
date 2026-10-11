using System.Collections;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Facet.PlayTests
{
    /// <summary>
    /// The rebuild gate as Unity drives it: a view that fails to rebuild and a view that rebuilds twice
    /// a frame both look the same from the outside, so the only way to see the difference is to count
    /// the rebuilds a real play loop produces. This drives a view with a frame clock the test owns and
    /// counts how many times the gate let it rebuild, so the four reasons are pinned - the first frame,
    /// an unchanged frame, a zoom past the tolerance, a zoom inside it, and a view marking itself dirty.
    ///
    /// The gate's other caller - the Editor's map preview, which hands frames in by hand with no play
    /// loop at all - is pinned by MeshViewDrawTests in the EditMode suite. This one is the message path:
    /// that Unity's own frame call reaches the same gate.
    /// </summary>
    public class MeshViewGateTests
    {
        private static readonly ViewFrame Still = new ViewFrame(0f, 0.02f);

        private GameObject _object;
        private Palette _palette;
        private FrameClock _clock;
        private RecordingMeshView _view;

        [SetUp]
        public void CreateView()
        {
            _object = new GameObject("Recording");
            _view = _object.AddComponent<RecordingMeshView>();

            _palette = ScriptableObject.CreateInstance<Palette>();
            _clock = new FrameClock { Frame = Still };

            var world = new SimWorld(
                new MapDefinition("gate", 8, 4, 0, new ShapePatch[0], new Int2[0], new WaveDefinition[0]),
                new SimConfig());
            _view.Initialize(world, _clock, _palette);
        }

        [TearDown]
        public void DestroyView()
        {
            if (_object != null) Object.Destroy(_object);
            if (_palette != null) Object.Destroy(_palette);
        }

        [UnityTest]
        public IEnumerator Gate_RebuildsOnItsOwnReasons_AndNowhereElse()
        {
            yield return null;
            Assert.AreEqual(1, _view.Appends, "the first frame builds exactly once");

            yield return null;
            Assert.AreEqual(1, _view.Appends, "an unchanged frame does not rebuild");

            _clock.Frame = new ViewFrame(0.5f, Still.WorldPerPixel * 1.15f);   // zoom well past the tolerance
            yield return null;
            Assert.AreEqual(2, _view.Appends, "a zoom past the tolerance rebuilds");

            _clock.Frame = new ViewFrame(0.5f, _clock.Frame.WorldPerPixel * 1.02f);   // inside the tolerance
            yield return null;
            Assert.AreEqual(2, _view.Appends, "a zoom inside the tolerance does not rebuild");

            _view.Dirty();
            yield return null;
            Assert.AreEqual(3, _view.Appends, "a view that marks itself dirty rebuilds");

            yield return null;
            Assert.AreEqual(3, _view.Appends, "and settles again");
        }

        /// <summary>A <see cref="MeshView"/> with no geometry whose rebuilds can be counted, so the
        /// gate is exercised through the same interface the real views use.</summary>
        private sealed class RecordingMeshView : MeshView
        {
            public int Appends { get; private set; }

            protected override Palette.Layer Layer => Colors.GridLayer;

            public void Dirty() => MarkDirty();

            protected override void AppendFrame(in ViewFrame frame) => Appends++;
        }
    }
}
