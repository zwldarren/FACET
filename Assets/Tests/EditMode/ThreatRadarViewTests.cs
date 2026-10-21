using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The locator's pencil: what a mark on the edge of the screen is actually made of. The arithmetic -
    /// what is out of view and where its arrow lands - is <see cref="ThreatRadarTests"/>'; this is the
    /// drawing rule the player reads at a glance, and the one thing that rule has to keep is that the
    /// three subjects cannot be confused with each other: a chevron is something coming, a filled mark is
    /// the way home, and a hollow mark is a line that stopped somewhere the player has to go and mend.
    ///
    /// The colours are the test's business as much as the shapes. A stopped line marked in the wave's red
    /// reads as an enemy on the edge of a map with no enemies on it - which is what a player reported, and
    /// why the mark wears the amber a blocked segment wears instead.
    /// </summary>
    public class ThreatRadarViewTests
    {
        /// <summary>One screen pixel in world units, the same number the other view tests draw at.</summary>
        private static readonly ViewFrame Frame = new ViewFrame(1f, 0.02f);

        /// <summary>Vertices one hollow diamond costs: four outline quads, four corners each. A filled
        /// mark adds a triangle fan inside them - three corners per edge - which is exactly the difference
        /// this test is here to hold.</summary>
        private const int HollowDiamondVertices = 16;

        /// <summary>Vertices a filled mark costs instead: that fan (four triangles of three) over the
        /// same four outline quads.</summary>
        private const int FilledDiamondVertices = 28;

        private Palette _palette;
        private FrameClock _clock;
        private GameObject _cameraObject;
        private GameObject _viewObject;

        [SetUp]
        public void CreateViews()
        {
            _palette = ScriptableObject.CreateInstance<Palette>();
            _clock = new FrameClock { Frame = Frame };
            _cameraObject = new GameObject("Locator Camera");
            _viewObject = new GameObject("Locator");
        }

        [TearDown]
        public void DestroyViews()
        {
            if (_viewObject != null) Object.DestroyImmediate(_viewObject);
            if (_cameraObject != null) Object.DestroyImmediate(_cameraObject);
            if (_palette != null) Object.DestroyImmediate(_palette);
        }

        /// <summary>
        /// A line that has stopped out of view is marked in the amber a blocked segment wears and drawn
        /// hollow, so the edge of a map with no wave on it can never read as "an enemy is down there". The
        /// mark is still a mark - it has to be found - it just stops borrowing the wave's body.
        /// </summary>
        [Test]
        public void AStoppedLine_IsMarkedInTheStopAmber_AndHollow()
        {
            // A world with nothing alive on it: one belt, jammed at the far corner of the map, and the
            // camera looking at the Core in the middle. The jam is the only subject out of view, and the
            // Core is in it, so the one marker on the mesh is the stopped line's.
            SimWorld world = Sim.NewWorld(24, 12);
            var stopped = new Int2(1, 1);
            Sim.LayRun(world, stopped, Dir.East, 1);
            Assert.IsTrue(world.Belts.TryJam(stopped), "the line is jammed");
            Assert.AreEqual(0, world.Enemies.AliveCount, "and the map is empty of enemies");

            Mesh mesh = DrawLocator(world);

            int amber = 0, wave = 0;
            foreach (Color colour in mesh.colors)
            {
                if (SameHue(colour, _palette.BeltBlocked)) amber++;
                else if (SameHue(colour, _palette.Jam) || SameHue(colour, _palette.RunnerBody) ||
                         SameHue(colour, _palette.TankBody)) wave++;
            }

            Assert.Greater(mesh.vertexCount, 0,
                "a stopped line out of view is located, or there is no way to go and mend it (" + Where() + ")");
            Assert.AreEqual(0, wave,
                "the wave's red belongs to the wave: no enemy colour on a map with no enemies (" + Where() + ")");
            Assert.AreEqual(HollowDiamondVertices, amber,
                "and the mark is hollow - four outline quads and no fill - where " + FilledDiamondVertices +
                " vertices would be a filled body that reads as something alive (" + Where() + ")");
        }

        /// <summary>Draw the locator by hand the way the Editor's map preview draws a view, and hand back
        /// the mesh it built. The camera shows one tile around the Core, so a cell in the map's far corner
        /// is out of view at any screen size the batch runner reports.</summary>
        private Mesh DrawLocator(SimWorld world)
        {
            Camera camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 0.5f;
            camera.aspect = 1.6f;

            Vec2 core = world.Core.Center;
            camera.transform.position = new Vector3(core.X, core.Y, -10f);

            var view = _viewObject.AddComponent<ThreatRadarView>();
            view.Initialize(world, _clock, _palette);
            view.InitializeLocator(camera, tutorialUp: false);
            view.Draw(_clock.Frame);

            MeshFilter filter = _viewObject.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, "the locator draws through a mesh filter");
            Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");
            return filter.sharedMesh;
        }

        /// <summary>Same RGB, whatever the alpha: the marks fade with distance, and the fade is not the
        /// colour a test is asking about.</summary>
        private static bool SameHue(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 1e-3f && Mathf.Abs(a.g - b.g) < 1e-3f && Mathf.Abs(a.b - b.b) < 1e-3f;

        /// <summary>The screen the locator measured itself against, for a failure that has to be read
        /// without a Game view: a headless runner reports a screen of its own, and a marker placed by a
        /// degenerate one would otherwise look like a mark that was never drawn.</summary>
        private string Where()
            => "screen " + Screen.width + "x" + Screen.height + ", aspect " +
               ((float)Screen.width / Mathf.Max(1, Screen.height)).ToString("0.00");
    }
}
