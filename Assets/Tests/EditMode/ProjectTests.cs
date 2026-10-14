using Facet.Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// Guards on the project wiring the game cannot check about itself at runtime. FACET has no textures
    /// or sprites: every polygon is a runtime-generated mesh whose colour lives in its vertices, which only
    /// works if the shader multiplies vertex colour and survives the build - it does through the asset chain
    /// (scene -> Palette -> view material -> shader). The failure is not an error but a build that renders
    /// wholly in one flat colour, in a build only, never in the Editor.
    /// </summary>
    public class ProjectTests
    {
        private const string PalettePath = "Assets/Data/Palette.asset";

        [Test]
        public void TheSharedViewMaterial_UsesTheVertexColourShader()
        {
            var palette = AssetDatabase.LoadAssetAtPath<Palette>(PalettePath);
            Assert.IsNotNull(palette, "no Palette asset at " + PalettePath);

            Assert.IsNotNull(palette.ViewMaterial,
                "the Palette at " + PalettePath + " has no View Material assigned. Without it the views " +
                "fall back to a runtime-built material, whose shader nothing in the build references - " +
                "the build strips it and every polygon renders in a single flat colour.");

            Assert.AreEqual(ProcMesh.VertexColorShader, palette.ViewMaterial.shader.name,
                "every FACET polygon gets its colour from mesh vertices, so the view material's shader " +
                "must multiply vertex colour: '" + ProcMesh.VertexColorShader + "' does, '" +
                palette.ViewMaterial.shader.name + "' does not");
        }

        [Test]
        public void TheShippedScene_IsTheOneInTheBuildSettings()
        {
            // The WebGL link the assignment asks for is worth nothing if the build ships a scene the
            // game is not in, so what gets built is pinned here as well as in the build tool.
            bool main = false;
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled) continue;
                if (scene.path == "Assets/Scenes/Main.unity") main = true;
            }

            Assert.IsTrue(main, "Assets/Scenes/Main.unity has to be enabled in the build settings");
        }
    }
}
