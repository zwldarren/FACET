using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The HUD's budget, as arithmetic rather than as a screenshot: issue #11's first acceptance is that
    /// status + tutorial + bar + info spend no more than 22 % of the screen with the tutorial up, at both
    /// sizes the published player is looked at in. The old table spent 48 %, and the failure was invisible
    /// to every other test in the suite because nothing else asks how much of the frame the HUD takes.
    /// </summary>
    public class HudLayoutTests
    {
        private static readonly Vector2[] Screens =
        {
            new Vector2(960f, 600f),
            new Vector2(1400f, 875f),
        };

        [Test]
        public void PersistentPanels_SpendNoMoreThanTwentyTwoPercent_WithTutorialUp()
        {
            foreach (Vector2 screen in Screens)
            {
                string at = " at " + screen.x + "x" + screen.y;
                float s = HudLayout.Scale(screen.y);

                var panels = new Rect[HudLayout.PanelCount];
                int count = HudLayout.Panels(screen.x, screen.y, s, tutorialUp: true, into: panels,
                    pickerUp: false);

                Assert.AreEqual(5, count, "status, tutorial, controls, bar and info are the permanent pieces" + at);

                float area = 0f;
                for (int i = 0; i < count; i++) area += panels[i].width * panels[i].height;

                // The controls cluster is not a panel the issue budgets for: it is three buttons in a corner.
                // Everything else - the four the acceptance names - is what the share is measured over.
                Rect controls = HudLayout.Controls(screen.x, s);
                area -= controls.width * controls.height;

                float share = area / (screen.x * screen.y);
                Assert.LessOrEqual(share, 0.22f,
                    "the HUD spends " + share.ToString("P1") + " of the frame" + at);
            }
        }

        [Test]
        public void EveryPanel_SitsInsideTheScreen()
        {
            foreach (Vector2 screen in Screens)
            {
                string at = " at " + screen.x + "x" + screen.y;
                float s = HudLayout.Scale(screen.y);

                var panels = new Rect[HudLayout.PanelCount];
                int count = HudLayout.Panels(screen.x, screen.y, s, tutorialUp: true, into: panels,
                    pickerUp: true);

                for (int i = 0; i < count; i++)
                {
                    Rect panel = panels[i];
                    Assert.GreaterOrEqual(panel.xMin, -1e-2f, "a panel starts left of the screen" + at);
                    Assert.GreaterOrEqual(panel.yMin, -1e-2f, "a panel starts above the screen" + at);
                    Assert.LessOrEqual(panel.xMax, screen.x + 1e-2f, "a panel runs off the right edge" + at);
                    Assert.LessOrEqual(panel.yMax, screen.y + 1e-2f, "a panel runs off the bottom edge" + at);
                }
            }
        }

        [Test]
        public void TypeScale_IsExactlyFourSteps()
        {
            // Four steps, not eight: the art pass raised the middle two and the display by a point or two
            // after the first playtest, because 11-point labels at 960x600 rounded down to 8 px. What the
            // test guards is the count - one size per job - not the exact numbers, which are the design's
            // to tune.
            CollectionAssert.AreEqual(new[] { 10f, 12f, 15f, 28f }, HudType.Steps,
                "the HUD's scale is 10 / 12 / 15 / 28 design units at 720p, and nothing else");
        }
    }
}
