using System;
using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The HUD's own picture of an item, rasterised without a scene. The HUD has no other view of a shape -
    /// the briefing's chips, the stockpile readout and the sorter's picker all read these textures - so a
    /// rasteriser that quietly regressed would take the whole "an enemy's weakness is an exact shape" reading
    /// with it, and nothing else in the suite would notice.
    ///
    /// Two pictures exist and they are not interchangeable: the <em>fitted</em> mark a chip draws, scaled to
    /// the figure's own box, and the <em>unfitted</em> canvas the picker draws, which is the item's own square
    /// so its quadrant buttons sit exactly over the cells they paint. Both are pinned here in pixels.
    /// </summary>
    public class HudIconsTests
    {
        /// <summary>The icon edge the rasteriser builds at: <see cref="HudIcons"/>'s own Size, which is not
        /// public, so it is restated here as the number these assertions are measured in.</summary>
        private const int IconSize = HudIcons.Size;

        /// <summary>Where a figure's pixels are, and how many of them there are, in icon pixels with (0,0) at
        /// the bottom-left corner - the same frame the rasteriser writes in.</summary>
        private readonly struct Ink
        {
            public readonly int Count;
            public readonly int MinX, MaxX, MinY, MaxY;

            public Ink(int count, int minX, int maxX, int minY, int maxY)
            {
                Count = count;
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
            }

            public int Width => MaxX - MinX + 1;
            public int Height => MaxY - MinY + 1;
        }

        private static Ink InkOf(HudIcons.Icon icon)
        {
            Assert.IsTrue(icon.Valid, "an icon with no texture is nothing to measure");

            Color32[] pixels = icon.Texture.GetPixels32();
            int count = 0;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;

            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    if (pixels[y * IconSize + x].a == 0) continue;

                    count++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            return new Ink(count, minX, maxX, minY, maxY);
        }

        /// <summary>Every colour the icon actually draws with, so "both of a mixed item's minerals reached the
        /// picture" can be asked of the pixels rather than of the code that wrote them.</summary>
        private static HashSet<Color32> ColoursOf(HudIcons.Icon icon)
        {
            var colours = new HashSet<Color32>();
            foreach (Color32 pixel in icon.Texture.GetPixels32())
                if (pixel.a > 0) colours.Add(pixel);

            return colours;
        }

        /// <summary>One palette and one icon set, disposed together - a built player never frees these, but a
        /// test that leaked a texture per case would be its own kind of bug.</summary>
        private static void WithIcons(Action<Palette, HudIcons> body)
        {
            Palette palette = ScriptableObject.CreateInstance<Palette>();
            var icons = new HudIcons(palette);
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

        [Test]
        public void EveryItem_HasAPicture_AndNothingHasNone()
        {
            // The item space is the algebra's eighty, and every one of them is something a card may have to
            // draw - a demand, a stockpiled shape, a machine's badge. Nothing draws nothing rather than a gap,
            // which is the contract every caller relies on when it asks for a picture of whatever it holds.
            WithIcons((palette, icons) =>
            {
                Assert.IsFalse(icons.Mark(Shape.None).Valid, "nothing has no picture");
                Assert.IsFalse(icons.Canvas(Shape.None).Valid, "on either picture");

                foreach (Shape shape in ShapePool.All)
                {
                    Assert.IsTrue(icons.Mark(shape).Valid, shape.Describe() + " has no picture");
                    Assert.Greater(InkOf(icons.Mark(shape)).Count, 0, shape.Describe() + " rasterised to nothing");

                    Assert.AreSame(icons.Mark(shape).Texture, icons.Mark(shape).Texture,
                        "and a code is rasterised once, not once per ask");
                }
            });
        }

        [Test]
        public void TheFittedMark_AndTheUnfittedCanvas_FrameTheSameItemDifferently()
        {
            // The difference the picker stands on. A chip fits the figure's own box, so one quadrant scaled up
            // reads as a picture of that piece - which is right in a row of chips and wrong in a picker, where
            // the four buttons have to sit over the four cells the figure is drawn in. So the canvas is the
            // item's own square: the same quadrant lands in the icon's own quadrant, and three quarters of the
            // icon stay empty.
            Shape quadrant = Shape.Quarter(Shape.NW, Quad.Circle);

            WithIcons((palette, icons) =>
            {
                Ink fitted = InkOf(icons.Mark(quadrant));
                Ink canvas = InkOf(icons.Canvas(quadrant));

                Assert.GreaterOrEqual(fitted.Width, IconSize - 2,
                    "the fitted mark fills the icon's box, whatever shape the figure itself is");
                Assert.GreaterOrEqual(fitted.Height, IconSize - 2);

                Assert.Less(canvas.Width, IconSize / 2 + 1,
                    "while the canvas draws the piece in its own quadrant of the item");
                Assert.Less(canvas.Height, IconSize / 2 + 1);
                Assert.Less(canvas.MaxX, IconSize / 2, "which for a NW piece is the icon's left half");
                Assert.GreaterOrEqual(canvas.MinY, IconSize / 2 - 1, "and its top half");

                // The whole item, by contrast, fills the canvas corner to corner: four quadrants, four cells,
                // each of them a button.
                Ink whole = InkOf(icons.Canvas(Shape.Full(Quad.Circle)));

                Assert.GreaterOrEqual(whole.Width, IconSize - 2, "a full item fills the picker's canvas");
                Assert.GreaterOrEqual(whole.Height, IconSize - 2);
                Assert.Greater(whole.Count, canvas.Count * 2, "with four quadrants' worth of ink in it");
            });
        }

        [Test]
        public void AMixedItem_DrawsBothItsMinerals_InTheirOwnColours()
        {
            // A mixed round is the reason the whole algebra exists, and its picture has to show both minerals:
            // two quadrants of one item are two figures in two colours. A rasteriser that stopped at the first
            // mark it landed in would draw half of it, and one that painted everything in the first mineral's
            // colour would draw it lying about what it is made of.
            Shape mixed = Shape.Of(Quad.Circle, Quad.Empty, Quad.Empty, Quad.Square);

            WithIcons((palette, icons) =>
            {
                HashSet<Color32> mixedColours = ColoursOf(icons.Canvas(mixed));

                Assert.IsTrue(mixedColours.Contains((Color32)palette.CircleMineral),
                    "the circle quadrant's fill is in the picture");
                Assert.IsTrue(mixedColours.Contains((Color32)palette.SquareMineral),
                    "and the square quadrant's, beside it");
                Assert.IsTrue(mixedColours.Contains((Color32)palette.Outline), "each mark wearing the outline");

                // A single-mineral item is the contrast: its own colour and the outline, and no third.
                HashSet<Color32> single = ColoursOf(icons.Canvas(Shape.Full(Quad.Circle)));

                Assert.AreEqual(2, single.Count, "one mineral draws in one colour plus its outline");
                Assert.IsTrue(single.Contains((Color32)palette.CircleMineral));
            });
        }

        [Test]
        public void TheCrossInACardFigure_IsThinnerThanTheRimThatFramesIt()
        {
            // The map's rule, rasterised: two quadrants share every seam and each side of one draws half of
            // it, so the cross through a figure is one line thinner than the edge around it. A card's mark is
            // the same figure as the item on the belt it describes, so a disc that reads as a disc there has
            // to read as a disc here - and at a card's size a seam drawn from both sides at the outline's own
            // width is most of the mark.
            WithIcons((palette, icons) =>
            {
                // The unfitted canvas of a full item: the item's own square is the icon, so a row across it
                // crosses the rim, the fill and the seam. A row off the middle, because the row through the
                // middle is the horizontal seam itself - from end to end it is one line of outline.
                Color32[] pixels = icons.Canvas(Shape.Full(Quad.Circle)).Texture.GetPixels32();
                var outline = (Color32)palette.Outline;
                int row = (IconSize / 2 + 6) * IconSize;

                int rim = 0;
                int seam = 0;
                int run = 0;
                int runStart = 0;
                for (int x = 0; x <= IconSize; x++)
                {
                    if (x < IconSize && pixels[row + x].Equals(outline))
                    {
                        if (run == 0) runStart = x;
                        run++;
                        continue;
                    }

                    if (run > 0)
                    {
                        // The run at the figure's edge is the rim; the one at its middle is the seam.
                        int middle = runStart + run / 2;
                        if (middle < IconSize / 4) rim = run;
                        else if (Mathf.Abs(middle - IconSize / 2) <= 1) seam = run;
                    }

                    run = 0;
                }

                Assert.Greater(rim, 0, "the figure's rim is drawn");
                Assert.Greater(seam, 0, "and so is the vertical seam at its middle");
                Assert.Less(seam, rim, "the seam is thinner than the rim, as it is on the map");
            });
        }

        // ---------------------------------------------------------------- issue #12: the demand figure

        [Test]
        public void TheDemandFigure_IsAtLeastFifteenPixels_At720p()
        {
            // The figure a player reads first each wave was the smallest thing on screen: 11 design units in
            // the briefing. One size now, for every HUD mark that means "build this".
            Assert.GreaterOrEqual(HudLayout.DemandFigure * HudLayout.Scale(720f), 15f,
                "the demand figure is at least 15 px at 720p");
        }

        [Test]
        public void TheOutline_IsAboutAPixelAndAHalf_AndLeavesTwelvePixelsOfShape()
        {
            // Measured on square figures, whose left edge is a straight vertical line, so a row's leading run
            // of outline pixels is exactly the outline's thickness. The full square is the case the outline cap
            // reaches; the quarter is the one it does not, and the one whose ring used to be most of the ink.
            float toScreen = HudLayout.DemandFigure / HudIcons.Size;

            WithIcons((palette, icons) =>
            {
                foreach (Shape shape in new[] { Shape.Full(Quad.Square), Shape.Quarter(Shape.NW, Quad.Square) })
                {
                    HudIcons.Icon icon = icons.Mark(shape);
                    Ink ink = InkOf(icon);
                    int rim = LeadingRun(icon, (Color32)palette.Outline, HudIcons.Size / 2 + 5);

                    Assert.Greater(rim, 0, shape.Describe() + " has an outline");
                    Assert.LessOrEqual(rim * toScreen, 1.75f,
                        shape.Describe() + "'s outline is about 1.5 px at the demand size, not " + rim * toScreen);
                    Assert.GreaterOrEqual((ink.Width - 2 * rim) * toScreen, 12f,
                        shape.Describe() + " keeps at least 12 px of shape inside its outline");
                }
            });
        }

        [Test]
        public void AMixedFigure_ShowsWhichHalfIsWhichMineral_WithoutColour()
        {
            // Asked of the alpha channel alone: a circle quadrant is a quarter disc and a square quadrant is a
            // square cell, so the corner of a circle half is empty and the corner of a square half is filled.
            // The colour says the same thing, but the colour is already carrying other news.
            int near = 1, far = HudIcons.Size - 2;

            WithIcons((palette, icons) =>
            {
                HudIcons.Icon circleOverSquare = icons.Mark(Shape.Of(Quad.Circle, Quad.Circle, Quad.Square, Quad.Square));
                Assert.AreEqual(0, AlphaAt(circleOverSquare, near, far), "the circle half's corner is rounded off");
                Assert.Greater(AlphaAt(circleOverSquare, near, near), 0, "the square half's corner is filled");

                HudIcons.Icon squareOverCircle = icons.Mark(Shape.Of(Quad.Square, Quad.Square, Quad.Circle, Quad.Circle));
                Assert.Greater(AlphaAt(squareOverCircle, near, far), 0, "upside down, the square half is on top");
                Assert.AreEqual(0, AlphaAt(squareOverCircle, near, near), "and the circle half below it");
            });
        }

        [Test]
        public void AtHalfSize_AQuarterCircle_AQuarterSquare_AndAHalf_StayApart()
        {
            // The demand figure at 50 %: eight pixels. Each figure is box-averaged down to an 8 x 8 coverage
            // mask - roughly what the mip chain hands the screen - and any two of the three must still differ
            // in enough cells to be told apart.
            WithIcons((palette, icons) =>
            {
                var figures = new[]
                {
                    ("a quarter-circle", Mask(icons.Mark(Shape.Quarter(Shape.NW, Quad.Circle)))),
                    ("a quarter-square", Mask(icons.Mark(Shape.Quarter(Shape.NW, Quad.Square)))),
                    ("a half", Mask(icons.Mark(Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty)))),
                };

                for (int i = 0; i < figures.Length; i++)
                for (int j = i + 1; j < figures.Length; j++)
                {
                    int differ = 0;
                    for (int k = 0; k < figures[i].Item2.Length; k++)
                        if (figures[i].Item2[k] != figures[j].Item2[k]) differ++;

                    Assert.GreaterOrEqual(differ, 6,
                        figures[i].Item1 + " and " + figures[j].Item1 + " read the same at half size");
                }
            });
        }

        /// <summary>How many pixels of <paramref name="colour"/> a row starts with, counting from the first
        /// pixel that has any ink.</summary>
        private static int LeadingRun(HudIcons.Icon icon, Color32 colour, int row)
        {
            Color32[] pixels = icon.Texture.GetPixels32();
            int x = 0;
            while (x < HudIcons.Size && pixels[row * HudIcons.Size + x].a == 0) x++;

            int run = 0;
            while (x < HudIcons.Size && pixels[row * HudIcons.Size + x].Equals(colour))
            {
                run++;
                x++;
            }

            return run;
        }

        private static byte AlphaAt(HudIcons.Icon icon, int x, int y)
            => icon.Texture.GetPixels32()[y * HudIcons.Size + x].a;

        /// <summary>The figure at an eighth of its picture's edge, as covered / not covered per cell.</summary>
        private static bool[] Mask(HudIcons.Icon icon)
        {
            const int cells = 8;
            int block = HudIcons.Size / cells;
            Color32[] pixels = icon.Texture.GetPixels32();
            var mask = new bool[cells * cells];

            for (int cy = 0; cy < cells; cy++)
            for (int cx = 0; cx < cells; cx++)
            {
                int sum = 0;
                for (int y = 0; y < block; y++)
                for (int x = 0; x < block; x++)
                    sum += pixels[(cy * block + y) * HudIcons.Size + cx * block + x].a;

                mask[cy * cells + cx] = sum >= 255 * block * block / 2;
            }

            return mask;
        }
    }
}
