using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The cast-shadow pass: the second mesh that makes a solid body read as standing on the ground
    /// rather than printed on it. What has to hold is that it carries the silhouette and nothing else,
    /// that it lies the palette's own offset from the body, and that it is drawn first.
    /// </summary>
    public class CastShadowTests
    {
        private static readonly ViewFrame Frame = new ViewFrame(1f, 0.02f);

        private Palette _palette;
        private FrameClock _clock;

        [SetUp]
        public void CreateFrame()
        {
            _palette = ScriptableObject.CreateInstance<Palette>();
            _clock = new FrameClock { Frame = Frame };
        }

        [TearDown]
        public void DestroyPalette()
        {
            if (_palette != null) Object.DestroyImmediate(_palette);
        }

        [Test]
        public void AWall_DropsItsSilhouette_ByThePalettesShadowOffset()
        {
            SimWorld world = Sim.NewWorld(8, 4);
            Sim.Place(world, BuildKind.Wall, new Int2(1, 1), Dir.North);

            var go = new GameObject("Machines");
            try
            {
                var view = go.AddComponent<MachineRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                Mesh body = go.GetComponent<MeshFilter>().sharedMesh;
                Mesh shadow = ShadowMeshOf(go);

                Assert.Greater(shadow.vertexCount, 0, "a machine standing on the ground casts a shadow");
                Assert.Less(shadow.vertexCount, body.vertexCount,
                    "the shadow is the silhouette alone: no outline, no stubs, no badge");

                Vector2 drop = _palette.ShadowOffsetPixels * Frame.WorldPerPixel;

                // The wall's own block, from the palette: half a tile less the inset, round the cell's
                // centre. The shadow is measured off that rather than off the body's bounds, because the
                // body's bounds carry its outline and the shadow deliberately has none.
                var centre = new Vector2(1.5f, 1.5f);
                float half = 0.5f - _palette.Machines.WallInset;

                // Two silhouettes - the body's own drop and a fainter one further out - so what the bounds
                // reach is the offset times the spread.
                Assert.AreEqual(centre.x + half + drop.x * _palette.ShadowSpread, shadow.bounds.max.x, 1e-3f,
                    "it lies the palette's offset to the right of the body, and its soft pass further still");
                Assert.AreEqual(centre.y - half + drop.y * _palette.ShadowSpread, shadow.bounds.min.y, 1e-3f,
                    "and the palette's offset below it, out to the same spread");
                Assert.AreEqual(_palette.Shadow, shadow.colors[0], "and wears the palette's shadow colour");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheShadowPass_SitsBehindTheBodyItBelongsTo()
        {
            // Both pass through the same transparent queue at the same sorting order, so the only thing
            // that keeps a body over its own shadow is that the shadow is further from the camera.
            SimWorld world = Sim.NewWorld(8, 4);
            Sim.Place(world, BuildKind.Wall, new Int2(1, 1), Dir.North);

            var go = new GameObject("Machines");
            try
            {
                var view = go.AddComponent<MachineRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                Transform shadow = go.transform.Find("Shadow");
                Assert.Less(go.transform.position.z, shadow.position.z,
                    "the camera looks down +z, so the shadow has to be the larger z of the two");
                Assert.AreEqual(go.GetComponent<MeshRenderer>().sortingOrder,
                    shadow.GetComponent<MeshRenderer>().sortingOrder, "within the same layer");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AViewOfTheGround_LeavesNoShadowAtAll()
        {
            // A belt bed is the surface things stand on. A shadow of it would be a darker patch of the
            // same surface, which is the one thing the pass must never draw.
            SimWorld world = Sim.NewWorld(12, 6);
            Sim.LayRun(world, new Int2(0, 0), Dir.East, 2);

            var go = new GameObject("Belts");
            try
            {
                var view = go.AddComponent<BeltRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                Assert.Greater(go.GetComponent<MeshFilter>().sharedMesh.vertexCount, 0, "the belts drew");
                Assert.IsNull(go.transform.Find("Shadow"), "and the ground casts nothing");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheCore_DropsItsSilhouette_AsTheMapsLargestBody()
        {
            SimWorld world = Sim.NewWorld(8, 6);

            var go = new GameObject("Core");
            try
            {
                var view = go.AddComponent<CoreView>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                Mesh body = go.GetComponent<MeshFilter>().sharedMesh;
                Mesh shadow = ShadowMeshOf(go);

                Assert.Greater(shadow.vertexCount, 0, "the Core stands on the ground like everything else");
                Assert.Less(shadow.vertexCount, body.vertexCount,
                    "the emblem sits inside the block and casts nothing of its own");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AWalker_DropsItsSilhouette_ButItsHealthBarDoesNot()
        {
            // OneWaveWorld's wave starts itself on the first tick, but the walker's group has a
            // FirstDelay of one second - thirty ticks at the simulation's rate - so the walker is on
            // the map from the thirty-first tick, not the first. Five ticks left the map empty and the
            // shadow pass had nothing to draw, which read as a broken shadow rather than as an empty
            // map. What the shadow must not pick up is the rest of the enemy mesh: the bar above
            // the body and the weakness figure inside it are readouts, not things that stand.
            SimWorld world = Sim.OneWaveWorld();
            Sim.Tick(world, 40);

            var go = new GameObject("Enemies");
            try
            {
                var view = go.AddComponent<EnemyRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                Mesh body = go.GetComponent<MeshFilter>().sharedMesh;
                Mesh shadow = ShadowMeshOf(go);

                Assert.Greater(shadow.vertexCount, 0, "a walker standing on the ground casts a shadow");
                Assert.Less(shadow.vertexCount, body.vertexCount,
                    "the shadow is the body alone: no health bar, no weakness figure, no spawn marker");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AGoneShadowPass_IsRebuilt_ByTheNextDraw()
        {
            // A view's mesh can be destroyed under a running script (the class doc says so), and the
            // shadow pass is a second mesh with the same hold on life. Losing it must cost one frame,
            // not the shadows for the rest of the run.
            SimWorld world = Sim.NewWorld(8, 4);
            Sim.Place(world, BuildKind.Wall, new Int2(1, 1), Dir.North);

            var go = new GameObject("Machines");
            try
            {
                var view = go.AddComponent<MachineRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                Object.DestroyImmediate(go.transform.Find("Shadow").gameObject);
                view.Draw(_clock.Frame);

                Mesh shadow = ShadowMeshOf(go);
                Assert.Greater(shadow.vertexCount, 0, "the pass came back with the body's silhouette");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ASpriteOverriddenBody_StillDropsTheShippedSilhouettesShadow()
        {
            // A sprite replaces the body's mark, not its footprint: the shipped block is still what stands
            // on the ground, so an authored body has to keep standing on something.
            SimWorld world = Sim.NewWorld(8, 4);
            Sim.Place(world, BuildKind.Wall, new Int2(1, 1), Dir.North);

            _palette.Visuals.Machines.Entries = new[]
            {
                new MachineVisuals.Entry
                {
                    Build = BuildKind.Wall,
                    Style = new VisualStyle { Override = true, Source = VisualSource.Sprite },
                },
            };

            var go = new GameObject("Machines");
            try
            {
                var view = go.AddComponent<MachineRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                Mesh shadow = ShadowMeshOf(go);
                Assert.Greater(shadow.vertexCount, 0,
                    "an authored body stands on the shipped footprint's shadow");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static Mesh ShadowMeshOf(GameObject view)
        {
            Transform shadow = view.transform.Find("Shadow");
            Assert.IsNotNull(shadow, "the view has a shadow pass");

            var filter = shadow.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, "which draws through a mesh filter");
            Assert.IsNotNull(filter.sharedMesh, "and built a mesh of its own");
            return filter.sharedMesh;
        }
    }
}
