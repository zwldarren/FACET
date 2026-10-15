using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The terrain view, driven by hand the way <see cref="MeshViewDrawTests"/> drives its counting view:
    /// a frame in, one mesh out. What is pinned is the cheap end of the terrain layer - a map with only
    /// ground and ore costs the terrain view no geometry, and rock and water are what it exists to draw.
    /// The PlayMode suite pins a map with a ground of its own drawing it through the driver's own views.
    /// </summary>
    public class TerrainRendererTests
    {
        private static readonly ViewFrame Frame = new ViewFrame(1f, 0.02f);

        private Palette _palette;

        [SetUp]
        public void CreatePalette()
        {
            _palette = ScriptableObject.CreateInstance<Palette>();
        }

        [TearDown]
        public void DestroyPalette()
        {
            if (_palette != null) Object.DestroyImmediate(_palette);
        }

        private int Vertices(SimWorld world)
        {
            // A fresh object per reading: Initialize attaches its own filter and renderer, and a view
            // re-initialised on the same object would stack a second of each.
            GameObject viewObject = new GameObject("Terrain");
            try
            {
                TerrainRenderer view = viewObject.AddComponent<TerrainRenderer>();
                view.Initialize(world, new FrameClock { Frame = Frame }, _palette);
                view.Draw(Frame);

                var filter = viewObject.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the view owns a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and a mesh, drawn or empty");

                return filter.sharedMesh.vertexCount;
            }
            finally
            {
                Object.DestroyImmediate(viewObject);
            }
        }

        /// <summary>A world on an otherwise empty 12x10 map whose only ground is
        /// <paramref name="terrain"/>: the stockpile every unit test starts with, no doors and no waves.</summary>
        private static SimWorld TerrainWorld(string name, TerrainKind[] terrain)
            => new SimWorld(
                new MapDefinition(name, 12, 10, Sim.TestShapes, new ShapePatch[0],
                    new Int2[0], new WaveDefinition[0], terrain),
                new SimConfig());

        [Test]
        public void APlainMap_CostsTheTerrainView_Nothing()
        {
            Assert.AreEqual(0, Vertices(Sim.NewWorld(8, 4)),
                "ground and ore have their own views; an obstacle-free map draws no terrain geometry");
        }

        [Test]
        public void Rock_AndWater_AreWhatTheTerrainViewDraws()
        {
            SimWorld world = TerrainWorld("ridged", TerrainLayout.Rects(12, 10,
                new TerrainRect(new Int2(2, 2), 6, 2, TerrainKind.Rock),
                new TerrainRect(new Int2(2, 6), 6, 2, TerrainKind.Water)));

            Assert.Greater(Vertices(world), 0, "a ridge and a pond are geometry");

            // And the void is not: the one kind the ground does not draw at all, wherever it runs.
            SimWorld voided = TerrainWorld("voided",
                TerrainLayout.Rects(12, 10, new TerrainRect(new Int2(0, 0), 12, 2, TerrainKind.Void)));

            Assert.AreEqual(0, Vertices(voided), "the void is the absence of map, not a kind of tile to draw");
        }
    }
}
