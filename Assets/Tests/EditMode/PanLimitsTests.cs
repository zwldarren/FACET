using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The pan bound decides whether a tile at the map's edge can be looked at, and the HUD is what broke the
    /// old bound: with the map forced to cover the whole screen, the bottom rows could not be panned clear of
    /// the build bar at any usable zoom. These tests pin the bound in screen pixels - how much screen each panel
    /// owns, how far past the map edge the camera may travel, and that both leave background showing on every
    /// side. Measured on real screens, maps and zoom, because the failure they guard is rings of invisible tiles.
    /// </summary>
    public class PanLimitsTests
    {
        /// <summary>Half the visible height, i.e. the camera's orthographic size. 14 is the scene's.</summary>
        private const float SceneZoom = 14f;

        private static readonly Vector2[] Screens =
        {
            new Vector2(1280f, 720f),
            new Vector2(1920f, 1080f),
            new Vector2(2560f, 1440f),
            new Vector2(3840f, 2160f),
            new Vector2(800f, 600f),      // small and 4:3, to reach the bottom of the scale clamp
        };

        /// <summary>Two map sizes to clamp against: one wider than a 1080p screen and one narrower.</summary>
        private static readonly Vector2[] Maps = { new Vector2(80f, 48f), new Vector2(64f, 40f) };

        /// <summary>The rig's zoom range, plus the scene's default in the middle.</summary>
        private static readonly float[] Zooms = { 4f, SceneZoom, 18f };

        private static readonly ScreenInsets None = new ScreenInsets(0f, 0f, 0f, 0f);

        /// <summary>A camera position far past anything the bound allows, so the clamp returns the limit itself.</summary>
        private static readonly Vector3 FarCorner = new Vector3(-1e6f, -1e6f, 0f);

        [Test]
        public void WithNoPanelOnASide_TheMapStillCoversThatSide()
        {
            // The old rule, which is now just the no-panel, no-slack case of the new one.
            Assert.AreEqual(SceneZoom, PanLimits.ClampAxis(0f, SceneZoom, 48, None.Bottom, None.Top, 1080f), 1e-4f);
            Assert.AreEqual(48f - SceneZoom, PanLimits.ClampAxis(1e6f, SceneZoom, 48, None.Bottom, None.Top, 1080f), 1e-4f);
        }

        /// <summary>
        /// No slack: the bound stops the moment the map covers everything the HUD does not - the rule the slack
        /// is added to.
        /// </summary>
        [Test]
        public void WithoutSlack_TheMapStopsOnTheStripEachPanelOwns()
        {
            var screen = new Vector2(1920f, 1080f);
            ScreenInsets insets = HudLayout.Insets(HudLayout.Scale(screen.y));

            Vector3 centre = PanLimits.Clamp(FarCorner, SceneZoom, screen.x / screen.y, screen.x, screen.y,
                80, 48, insets, edgeSlack: 0f);
            Vector2 corner = ScreenPoint(0f, 0f, new Vector2(centre.x, centre.y), SceneZoom, screen);

            Assert.AreEqual(insets.Left, corner.x, 1e-2f, "the left column does not stop on the status card's strip");
            Assert.AreEqual(screen.y - insets.Bottom, corner.y, 1e-2f, "the bottom row does not stop on the build bar's strip");
        }

        /// <summary>
        /// The bound with its slack: panned as far as allowed, every side of the map is carried past the panel
        /// that used to hide it, into the open screen - background visible behind the edge on all four sides,
        /// including the bottom one, which the build bar covers across its whole strip.
        /// </summary>
        [Test]
        public void PannedAsFarAsItGoes_EverySideOfTheMapShowsBackground()
        {
            foreach (Vector2 screen in Screens)
            foreach (Vector2 map in Maps)
            foreach (float zoom in Zooms)
            {
                string at = " at " + screen.x + "x" + screen.y + ", map " + map.x + "x" + map.y + ", zoom " + zoom;
                ScreenInsets insets = HudLayout.Insets(HudLayout.Scale(screen.y));
                float aspect = screen.x / screen.y;
                float slackX = PanLimits.DefaultEdgeSlack * screen.x;
                float slackY = PanLimits.DefaultEdgeSlack * screen.y;

                Vector3 low = PanLimits.Clamp(FarCorner, zoom, aspect, screen.x, screen.y,
                    (int)map.x, (int)map.y, insets);
                Vector2 lowCorner = ScreenPoint(0f, 0f, new Vector2(low.x, low.y), zoom, screen);

                Assert.AreEqual(insets.Left + slackX, lowCorner.x, 1e-2f, "the left column has the wrong slack" + at);
                Assert.AreEqual(screen.y - insets.Bottom - slackY, lowCorner.y, 1e-2f,
                    "the bottom row is not clear of the build bar" + at);
                Assert.Greater(lowCorner.x - insets.Left, 0f, "no background beside the left column" + at);
                Assert.Greater(screen.y - insets.Bottom - lowCorner.y, 0f, "no background under the bottom row" + at);

                Vector3 high = PanLimits.Clamp(new Vector3(1e6f, 1e6f, 0f), zoom, aspect, screen.x, screen.y,
                    (int)map.x, (int)map.y, insets);
                Vector2 highCorner = ScreenPoint(map.x, map.y, new Vector2(high.x, high.y), zoom, screen);

                Assert.AreEqual(screen.x - insets.Right - slackX, highCorner.x, 1e-2f, "the right column has the wrong slack" + at);
                Assert.AreEqual(insets.Top + slackY, highCorner.y, 1e-2f, "the top row is not clear of the top cards" + at);
                Assert.Greater(screen.x - insets.Right - highCorner.x, 0f, "no background beside the right column" + at);
                Assert.Greater(highCorner.y - insets.Top, 0f, "no background above the top row" + at);
            }
        }

        /// <summary>A map too small to fill the open screen is placed in the middle of it rather than pushed up
        /// against a panel by a bound that assumes it is bigger. The slack is symmetric, so it does not move that middle.</summary>
        [Test]
        public void AMapSmallerThanTheOpenScreen_IsCentredInIt()
        {
            var screen = new Vector2(1920f, 1080f);
            ScreenInsets insets = HudLayout.Insets(HudLayout.Scale(screen.y));
            const float size = 4f;                     // world units, so the whole map is one screenful

            Vector3 centre = PanLimits.Clamp(FarCorner, SceneZoom, screen.x / screen.y, screen.x, screen.y,
                (int)size, (int)size, insets);
            var camera = new Vector2(size * 0.5f, centre.y);

            float openMiddle = (insets.Top + screen.y - insets.Bottom) * 0.5f;
            Assert.AreEqual(openMiddle, ScreenPoint(size * 0.5f, size * 0.5f, camera, SceneZoom, screen).y, 1e-2f,
                "the map is not centred in the screen the HUD leaves open");
            Assert.GreaterOrEqual(ScreenPoint(size * 0.5f, size, camera, SceneZoom, screen).y, insets.Top - 1e-2f,
                "the map's top row is under the top cards");
            Assert.LessOrEqual(ScreenPoint(size * 0.5f, 0f, camera, SceneZoom, screen).y, screen.y - insets.Bottom + 1e-2f,
                "the map's bottom row is under the build bar");
        }

        /// <summary>
        /// The other half of the contract: the strips the rig clears are the strips the panels occupy. The reserve
        /// and the panels are written separately on purpose - the reserve follows the widest card on each side - so
        /// a card moved or added without the strip following it is a tile the camera leaves underneath it.
        /// </summary>
        [Test]
        public void EveryPanel_SitsInsideTheStripTheCameraClears()
        {
            foreach (Vector2 screen in Screens)
            {
                string at = " at " + screen.x + "x" + screen.y;
                float s = HudLayout.Scale(screen.y);
                ScreenInsets insets = HudLayout.Insets(s);

                Rect stockpile = HudLayout.Stockpile(screen.x, s);
                Rect bar = HudLayout.Bar(screen.x, screen.y, s);
                Rect status = HudLayout.Status(s);
                Rect controls = HudLayout.Controls(screen.x, s);
                Rect info = HudLayout.Info(screen.x, screen.y, s);

                // The readout has no panel of its own, so its contract is the top strip, the screen edges, and
                // the right of the card it is measured from - the overlap test below holds the rest.
                Assert.GreaterOrEqual(stockpile.x, status.xMax - 1e-2f, "the stockpile readout is not beside the status card" + at);
                Assert.LessOrEqual(stockpile.xMax, screen.x + 1e-2f, "the stockpile readout runs off the screen" + at);
                Assert.LessOrEqual(stockpile.yMax, insets.Top + 1e-2f, "the stockpile readout reaches below the top strip" + at);
                Assert.LessOrEqual(status.xMax, insets.Left + 1e-2f, "the status card is wider than the left strip" + at);
                Assert.LessOrEqual(status.yMax, insets.Top + 1e-2f, "the status card is taller than the top strip" + at);
                Assert.GreaterOrEqual(controls.x, screen.x - insets.Right - 1e-2f, "the controls card reaches into the open screen" + at);
                Assert.LessOrEqual(controls.yMax, insets.Top + 1e-2f, "the controls card is taller than the top strip" + at);
                Assert.GreaterOrEqual(info.x, screen.x - insets.Right - 1e-2f, "the info card reaches into the open screen" + at);
                Assert.LessOrEqual(info.yMax, bar.y + 1e-2f, "the info card reaches into the build bar" + at);
                Assert.GreaterOrEqual(bar.y, screen.y - insets.Bottom - 1e-2f, "the build bar is taller than the bottom strip" + at);
                Assert.LessOrEqual(bar.yMax, screen.y + 1e-2f, "the build bar runs off the screen" + at);

                // The tutorial card is the one non-permanent panel, checked against the strip the camera clears
                // *while it is up* (insets above is the map with no tutorial). This catches a card added under
                // the status card without the strip growing: the failure is a band of tiles the player can neither see nor click.
                ScreenInsets withTutorial = HudLayout.Insets(s, tutorialUp: true);
                Rect tutorial = HudLayout.Tutorial(s);

                Assert.LessOrEqual(tutorial.xMax, withTutorial.Left + 1e-2f, "the tutorial card is wider than the left strip" + at);
                Assert.LessOrEqual(tutorial.yMax, withTutorial.Top + 1e-2f,
                    "the tutorial card reaches below the top strip the camera clears for it" + at);
            }
        }

        /// <summary>
        /// The other half of "the panels own their strips": nothing the HUD draws permanently may sit on anything
        /// else it draws permanently. The check covers every pair, because two pieces drifting into each other is
        /// the same bug wherever it comes from; toasts are left out on purpose, since overlaying the map is their
        /// whole job. The tutorial card is included even though it is only up on the first map: it hangs off the
        /// status card, the one arrangement where a new piece is placed by eye.
        /// </summary>
        [Test]
        public void NoHudPieceOverlapsAnother()
        {
            foreach (Vector2 screen in Screens)
            {
                string at = " at " + screen.x + "x" + screen.y;
                float s = HudLayout.Scale(screen.y);
                Rect bar = HudLayout.Bar(screen.x, screen.y, s);

                (string Name, Rect Rect)[] pieces =
                {
                    ("stockpile", HudLayout.Stockpile(screen.x, s)),
                    ("status", HudLayout.Status(s)),
                    ("tutorial", HudLayout.Tutorial(s)),
                    ("controls", HudLayout.Controls(screen.x, s)),
                    ("info", HudLayout.Info(screen.x, screen.y, s)),
                    ("bar", bar),
                };

                for (int i = 0; i < pieces.Length; i++)
                for (int j = i + 1; j < pieces.Length; j++)
                {
                    Assert.IsFalse(Overlaps(pieces[i].Rect, pieces[j].Rect),
                        "the " + pieces[i].Name + " and the " + pieces[j].Name + " pieces overlap" + at);
                }
            }
        }

        /// <summary>
        /// The info card's grown state - the sorter picker's band - is the same two properties as the base
        /// one, and the reason it needs its own case is that a reader has to be told about it: the locator draws
        /// its arrows off these boxes, so an arrow under a band nobody declared points at nothing the player can
        /// see. Asserted in the reader's own terms: the box <see cref="HudLayout.Panels"/> reports has to be the
        /// box <see cref="HudLayout.Info"/> lays out, it has to be taller than the base card, and it still has to
        /// clear everything else the HUD draws.
        /// </summary>
        [Test]
        public void TheGrownInfoCard_IsWhatThePanelReaderReports_AndStillClearsTheRest()
        {
            foreach (Vector2 screen in Screens)
            {
                string at = " at " + screen.x + "x" + screen.y;
                float s = HudLayout.Scale(screen.y);
                Rect bar = HudLayout.Bar(screen.x, screen.y, s);
                Rect grown = HudLayout.Info(screen.x, screen.y, s, pickerUp: true);

                Assert.Greater(grown.height, HudLayout.Info(screen.x, screen.y, s).height,
                    "the picker's band makes the card taller, which is the whole reason it grows" + at);
                Assert.AreEqual(HudLayout.Info(screen.x, screen.y, s).yMax, grown.yMax, 1e-2f,
                    "and it grows upward from the same bottom edge, so the bar under it does not move" + at);
                Assert.Less(grown.yMin, HudLayout.Info(screen.x, screen.y, s).yMin,
                    "which means its far edge is higher up the screen" + at);

                var panels = new Rect[HudLayout.PanelCount];
                int count = HudLayout.Panels(screen.x, screen.y, s, tutorialUp: false, into: panels,
                    pickerUp: true);

                bool reported = false;
                for (int i = 0; i < count; i++) reported |= panels[i] == grown;
                Assert.IsTrue(reported,
                    "the panel reader has to report the card the view actually draws" + at);

                foreach (Rect other in new[]
                         {
                             HudLayout.Stockpile(screen.x, s),
                             HudLayout.Status(s),
                             HudLayout.Controls(screen.x, s),
                             bar,
                         })
                {
                    Assert.IsFalse(Overlaps(grown, other),
                        "the grown info card overlaps a panel it is drawn beside" + at);
                }
            }
        }

        private static bool Overlaps(Rect a, Rect b)
            => a.xMin < b.xMax - 1e-2f && b.xMin < a.xMax - 1e-2f &&
               a.yMin < b.yMax - 1e-2f && b.yMin < a.yMax - 1e-2f;

        /// <summary>Where a world point lands on the screen, in pixels, for an orthographic camera centred on
        /// <paramref name="centre"/> on a screen <paramref name="screen"/> pixels across.</summary>
        private static Vector2 ScreenPoint(float worldX, float worldY, Vector2 centre, float zoom, Vector2 screen)
        {
            float halfWidth = zoom * screen.x / screen.y;
            float pixelsPerUnit = screen.y / (2f * zoom);

            return new Vector2(
                (worldX - centre.x + halfWidth) * pixelsPerUnit,
                (centre.y + zoom - worldY) * pixelsPerUnit);
        }
    }
}
