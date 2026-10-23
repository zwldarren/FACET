using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The title's button column, as arithmetic rather than as a screenshot - because a screenshot is
    /// exactly what the suite cannot take of the size that broke. The column is drawn at the screen's
    /// height with buttons scaled to it, so a step taken in the wrong units only overlaps once the scale
    /// passes 46/36: a canvas taller than 920 pixels, which no Editor window is and every fullscreen is.
    /// Measured where it is computed, the question can be asked at the sizes that fail.
    /// </summary>
    public class ShellLayoutTests
    {
        /// <summary>The canvases the player is drawn at: the design size the HUD is laid out for, a
        /// windowed page a little under it, and the two fullscreens the letterboxed frame lands on - at
        /// 1080 and at 1440, where the scale is 1.5 and 2 and the old column had already stacked.</summary>
        private static readonly Vector2[] Screens =
        {
            new Vector2(960f, 600f),
            new Vector2(1280f, 800f),
            new Vector2(1728f, 1080f),
            new Vector2(2304f, 1440f),
        };

        /// <summary>How many buttons the column always has: play, the maps, how to play. A desktop build
        /// adds quit under them, and a test that counted it would be testing the Editor's own build.</summary>
        private const int Rows = 3;

        [Test]
        public void TheTitlesButtons_NeverOverlap_AtAnyScreenSize()
        {
            Assert.GreaterOrEqual(ShellView.ButtonStep, ShellView.TitleButtonHeight,
                "the column's step is smaller than the button it steps past");

            foreach (Vector2 screen in Screens)
            {
                float s = HudLayout.Scale(screen.y);
                float height = ShellView.TitleButtonHeight * s;
                string at = " at " + screen.x + "x" + screen.y + " (scale " + s.ToString("0.00") + ")";

                for (int i = 0; i < Rows - 1; i++)
                {
                    float gap = ShellView.TitleButtonTop(screen.y, s, i + 1) -
                        ShellView.TitleButtonTop(screen.y, s, i);

                    Assert.AreEqual(ShellView.ButtonStep * s, gap, 1e-3f,
                        "the step is design units scaled once, not screen pixels" + at);
                    Assert.GreaterOrEqual(gap, height,
                        "button " + i + " is drawn on top of the one below it" + at);
                }
            }
        }
    }
}
