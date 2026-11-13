using System;
using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The build bar's machine previews, rasterised without a scene. A preview that came out blank, all one
    /// colour, or the same picture on every key is a silent failure of the bar's whole point.
    /// </summary>
    public class BuildIconsTests
    {
        private static void WithIcons(Action<Palette, BuildIcons> body)
        {
            Palette palette = ScriptableObject.CreateInstance<Palette>();
            var icons = new BuildIcons(palette);
            try
            {
                body(palette, icons);
            }
            finally
            {
                icons.Dispose();
                UnityEngine.Object.DestroyImmediate(palette);
            }
        }

        private static Color32[] Pixels(BuildIcons.Icon icon)
        {
            Assert.IsTrue(icon.Valid, "an icon with no texture is nothing to measure");
            Assert.AreEqual(BuildIcons.Size, icon.Texture.width);
            Assert.AreEqual(BuildIcons.Size, icon.Texture.height);
            return icon.Texture.GetPixels32();
        }

        [Test]
        public void EveryBuildableKind_HasAPicture_OfItsOwn()
        {
            WithIcons((palette, icons) =>
            {
                var seen = new HashSet<Texture2D>();

                foreach (BuildKind kind in ContentDatabase.Default.BuildKinds)
                {
                    BuildIcons.Icon icon = icons.Figure(kind, ContentDatabase.Default.Machine(kind).Behavior);
                    Color32[] pixels = Pixels(icon);

                    int inked = 0;
                    foreach (Color32 pixel in pixels)
                        if (pixel.a > 0) inked++;

                    Assert.Greater(inked, pixels.Length / 3,
                        kind + " draws its plate and its machine, not a few stray pixels");
                    Assert.IsTrue(seen.Add(icon.Texture),
                        kind + " has a picture of its own rather than a copy of another kind's");

                    Assert.AreSame(icon.Texture,
                        icons.Figure(kind, ContentDatabase.Default.Machine(kind).Behavior).Texture,
                        "and one kind is rasterised once, not once per ask");
                }
            });
        }

        [Test]
        public void ThePlate_IsLightAndOpaque_WithRoundedCorners()
        {
            WithIcons((palette, icons) =>
            {
                Color32[] pixels = Pixels(icons.Figure(BuildKind.Belt, BehaviorKind.None));

                // The plate's middle is opaque ground; its corner is rounded away, so the key sits on the bar
                // rather than in a white square. The sample is above the belt's bed, on the plate itself.
                Color32 middle = pixels[(BuildIcons.Size - 6) * BuildIcons.Size + BuildIcons.Size / 2];
                Assert.AreEqual(255, middle.a, "the plate is opaque");
                Assert.Greater(middle.r, 180, "and light, like the ground it stands for");
                Assert.Greater(middle.g, 170);
                Assert.Greater(middle.b, 160);

                Color32 corner = pixels[0];
                Assert.AreEqual(0, corner.a, "the plate's corner is rounded away");
            });
        }

        [Test]
        public void TheMachine_IsDarkAgainstItsPlate_ForEveryKind()
        {
            // A body in dark ink on the plate is what makes the silhouette legible at forty pixels. The belt is
            // the exception - its bed is the light belt colour by design - so it is skipped.
            WithIcons((palette, icons) =>
            {
                foreach (BuildKind kind in ContentDatabase.Default.BuildKinds)
                {
                    if (kind == BuildKind.Belt) continue;

                    BehaviorKind behavior = ContentDatabase.Default.Machine(kind).Behavior;
                    Color32[] pixels = Pixels(icons.Figure(kind, behavior));

                    int dark = 0;
                    int visible = 0;
                    foreach (Color32 pixel in pixels)
                    {
                        if (pixel.a == 0) continue;

                        visible++;
                        float y = 0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b;
                        if (y < 100f) dark++;
                    }

                    Assert.Greater(dark, visible / 10,
                        kind + "'s body covers a readable share of its plate in dark ink");
                }
            });
        }

        [Test]
        public void Dispose_FreesEveryTexture()
        {
            Palette palette = ScriptableObject.CreateInstance<Palette>();
            var icons = new BuildIcons(palette);
            var built = new List<Texture2D>();

            foreach (BuildKind kind in ContentDatabase.Default.BuildKinds)
                built.Add(icons.Figure(kind, ContentDatabase.Default.Machine(kind).Behavior).Texture);

            icons.Dispose();
            foreach (Texture2D texture in built)
                Assert.IsTrue(texture == null, "the HUD's teardown frees the picture it made");

            UnityEngine.Object.DestroyImmediate(palette);
        }
    }
}
