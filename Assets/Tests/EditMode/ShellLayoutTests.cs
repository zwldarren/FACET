using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>The title's column, asked of the asset rather than of a screenshot: the size that broke - a canvas
    /// taller than 920 pixels - is one no Editor window has and every fullscreen does. The column is authored in
    /// design units by <c>Resources/Hud/ShellTitle</c>, plus the two numbers the view still names beside it
    /// (<see cref="ShellView.TitleFirstButtonTop"/> and the pause menu's compact step).</summary>
    public class ShellLayoutTests
    {
        /// <summary>The doors every build's column has. A desktop build adds quit under them, and a test that
        /// counted it would be testing the Editor's own build.</summary>
        private static readonly string[] Rows = { "play", "maps", "howto" };

        /// <summary>The wordmark's band as a fraction of the screen's height: the column hangs off it, and the
        /// asset says so with an anchor rather than with a number of design units.</summary>
        private const float Band = 1f - ShellView.TitleNameTop;

        [Test]
        public void TheTitlesColumn_IsAuthoredInDesignUnits_SoNoScreenCanStackIt()
        {
            Assert.GreaterOrEqual(ShellView.ButtonStep, ShellView.TitleButtonHeight,
                "the column's step is smaller than the door it steps past");

            GameObject prefab = Resources.Load<GameObject>("Hud/ShellTitle");
            Assert.IsNotNull(prefab, "the title screen is an asset (Resources/Hud/ShellTitle)");

            var doors = new RectTransform[Rows.Length];
            for (int i = 0; i < Rows.Length; i++)
            {
                Transform node = prefab.transform.Find(Rows[i]);
                Assert.IsNotNull(node, "the title's column carries its '" + Rows[i] + "' door");
                doors[i] = (RectTransform)node;
            }

            for (int i = 0; i < doors.Length; i++)
            {
                RectTransform door = doors[i];
                string at = " the " + door.name + " door";

                Assert.AreEqual(320f, door.sizeDelta.x, 0.01f, "a door is 320 design units wide," + at);
                Assert.AreEqual(ShellView.TitleButtonHeight, door.sizeDelta.y, 0.01f,
                    "and its design height," + at);
                Assert.AreEqual(0f, door.anchoredPosition.x, 0.01f, "centred on it," + at);
                Assert.AreEqual(-(ShellView.TitleFirstButtonTop + i * ShellView.ButtonStep),
                    door.anchoredPosition.y, 0.01f, "on its own step of the column," + at);
                Assert.AreEqual(Band, door.anchorMin.y, 1e-4f,
                    "hung off the wordmark's band, as a fraction of the screen's height," + at);
                Assert.AreEqual(door.anchorMin.y, door.anchorMax.y, 1e-4f,
                    "and on a point anchor, so the design units scale and the band does not," + at);
            }

            // One step between doors, and the step is the door's height plus its gap: the property the old
            // column lost when its step was taken in screen pixels while the doors around it scaled.
            for (int i = 0; i < doors.Length - 1; i++)
            {
                float gap = doors[i].anchoredPosition.y - doors[i + 1].anchoredPosition.y;
                Assert.AreEqual(ShellView.ButtonStep, gap, 0.01f,
                    "the column steps by design units once, not by screen pixels");
                Assert.GreaterOrEqual(gap, doors[i + 1].sizeDelta.y,
                    "door " + i + " is drawn on top of the one below it");
            }
        }
    }
}
