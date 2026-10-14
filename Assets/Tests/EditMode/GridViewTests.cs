using Facet.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The map editor's tile-to-pixel arithmetic, tested off the IMGUI that normally hosts it. Every
    /// hazard the window's own comments used to worry about - the y-flip, the fit, the clamps, the
    /// scrollbars that quietly eat the mouse - is an assertion here, and none of them needs a mouse, a
    /// window, or a human watching for the grid to stop answering.
    /// </summary>
    public class GridViewTests
    {
        private static readonly Rect View = new Rect(0f, 0f, 640f, 400f);
        private const int MapW = 64;
        private const int MapH = 40;

        private static GridFrame At(float zoom, Vector2 scroll = default)
            => GridFrame.Layout(View, MapW, MapH, zoom, scroll);

        [Test]
        public void Zoom1_FitsTheWholeMap_WithoutEvokingScrollbars()
        {
            GridFrame frame = At(1f);

            Assert.IsTrue(frame.Valid);
            Assert.LessOrEqual(frame.ContentSize.x, View.width + 1e-3f, "zoom 1 means everything at once");
            Assert.LessOrEqual(frame.ContentSize.y, View.height + 1e-3f);
            Assert.GreaterOrEqual(frame.Cell, GridFrame.MinCellSize);
            Assert.AreEqual(View.width, frame.Hit.width, "nothing to scroll means no bars to stop short of");
            Assert.AreEqual(View.height, frame.Hit.height);
            Assert.AreEqual(Vector2.zero, frame.Scroll, "and a fitting map has nowhere to scroll");
        }

        [Test]
        public void TooSmallAView_KeepsTheTilesReadable_AtTheCostOfScrollbars()
        {
            // The floor wins over the fit: below two pixels a tile stops reading as a tile at any zoom, and a
            // map that stops fitting is the honest answer, not a smeared one.
            GridFrame frame = GridFrame.Layout(new Rect(0f, 0f, 20f, 20f), MapW, MapH, 1f, Vector2.zero);

            Assert.AreEqual(GridFrame.MinCellSize, frame.Cell, 1e-4f);
            Assert.AreEqual(20f - GridFrame.ScrollbarThickness, frame.Hit.width, 1e-4f, "and the bars are where the brush must not go");
        }

        [Test]
        public void ZoomingIn_LeavesScrollbarTracks_TheBrushRefuses()
        {
            GridFrame frame = At(4f);

            Assert.Greater(frame.ContentSize.x, View.width);
            Assert.Greater(frame.ContentSize.y, View.height);
            Assert.AreEqual(View.width - GridFrame.ScrollbarThickness, frame.Hit.width, 1e-4f);
            Assert.AreEqual(View.height - GridFrame.ScrollbarThickness, frame.Hit.height, 1e-4f);

            // A point on the vertical bar's strip: inside the content, explicitly not a tile - the bar
            // takes the mouse after the window has had its turn, and painting under it would answer a drag
            // that never touched the map.
            Assert.IsFalse(frame.TryCellAt(new Vector2(View.xMax - 8f, 100f), out _, out _));
            Assert.IsFalse(frame.TryCellAt(new Vector2(100f, View.yMax - 8f), out _, out _));
        }

        [Test]
        public void TheRequestedScroll_IsClampedToWhatTheContentCanPay()
        {
            GridFrame frame = At(4f, new Vector2(9999f, 9999f));

            Assert.AreEqual(frame.ContentSize.x - View.width, frame.Scroll.x, 1e-3f);
            Assert.AreEqual(frame.ContentSize.y - View.height, frame.Scroll.y, 1e-3f);
            Assert.AreEqual(View.x - frame.Scroll.x, frame.Content.x, 1e-3f, "the content origin follows the clamp");

            GridFrame again = At(4f, frame.Scroll);
            Assert.AreEqual(frame.Scroll.x, again.Scroll.x, 1e-4f, "and a clamp is idempotent: the applied answer is stable");
            Assert.AreEqual(frame.Scroll.y, again.Scroll.y, 1e-4f);

            GridFrame fitted = At(1f, new Vector2(50f, 50f));
            Assert.AreEqual(0f, fitted.Scroll.x, "a whole map that fits swallows any requested scroll");
            Assert.AreEqual(0f, fitted.Scroll.y);
        }

        [Test]
        public void TheFlip_HasExactlyOneHome()
        {
            GridFrame frame = At(1f);

            Assert.AreEqual((MapH - 1) * frame.Cell, frame.CellRect(0, 0).y, 1e-4f,
                "the map's y = 0 row is the last one drawn");
            Assert.AreEqual(0f, frame.CellRect(0, MapH - 1).y, 1e-4f, "and the top of the screen is the far end of y");

            // Draw and hit-test read the same walk, so every cell's own centre must read back as that
            // cell - at the corners too, where an off-by-one row would show up.
            int[][] corners =
            {
                new[] { 0, 0 }, new[] { MapW - 1, 0 }, new[] { 0, MapH - 1 }, new[] { MapW - 1, MapH - 1 },
            };
            for (int i = 0; i < corners.Length; i++)
            {
                Assert.IsTrue(frame.TryCellAt(frame.CellRect(corners[i][0], corners[i][1]).center, out int x, out int y),
                    "corner " + i + " resolves");
                Assert.AreEqual(corners[i][0], x);
                Assert.AreEqual(corners[i][1], y);
            }
        }

        [Test]
        public void EdgePoints_ResolveToTheCellThatCoversThem()
        {
            GridFrame frame = At(1f);

            Assert.IsTrue(frame.TryCellAt(frame.Content.position + new Vector2(0.01f, 0.01f), out int tx, out int ty));
            Assert.AreEqual(0, tx);
            Assert.AreEqual(MapH - 1, ty, "the content's first pixels are the map's top-left");

            Assert.IsTrue(frame.TryCellAt(new Vector2(frame.Content.xMin + 0.01f, frame.Content.yMax - 0.01f), out int bx, out int by));
            Assert.AreEqual(0, bx);
            Assert.AreEqual(0, by, "and its last row is the map's y = 0");

            Assert.IsFalse(frame.TryCellAt(new Vector2(frame.Content.xMin - 0.01f, 10f), out _, out _),
                "off the content's left is nobody's tile");
        }

        [Test]
        public void ScrollTo_CentresTheTile_AndClampsAtTheBorders()
        {
            GridFrame frame = At(4f);

            Vector2 centre = frame.ScrollTo(32, 20);
            Vector2 where = frame.CellRect(32, 20).center - centre;
            Assert.AreEqual(View.width * 0.5f, where.x, 1e-3f, "the named tile sits dead centre of the view");
            Assert.AreEqual(View.height * 0.5f, where.y, 1e-3f);

            Vector2 topLeft = frame.ScrollTo(0, MapH - 1);
            Assert.AreEqual(0f, topLeft.x, 1e-4f, "an edge tile cannot centre: the clamp stops at the edge");
            Assert.AreEqual(0f, topLeft.y, 1e-4f);

            Vector2 bottomRight = frame.ScrollTo(MapW - 1, 0);
            Assert.AreEqual(frame.ContentSize.x - View.width, bottomRight.x, 1e-3f);
            Assert.AreEqual(frame.ContentSize.y - View.height, bottomRight.y, 1e-3f);
        }

        [Test]
        public void ADegenerateLayout_DrawsNothingAndAnswersNothing()
        {
            GridFrame noView = GridFrame.Layout(new Rect(0f, 0f, 0f, 0f), MapW, MapH, 1f, new Vector2(120f, 60f));
            Assert.IsFalse(noView.Valid);
            Assert.IsFalse(noView.TryCellAt(new Vector2(5f, 5f), out _, out _));
            Assert.AreEqual(Vector2.zero, noView.ScrollTo(3, 3), "no grid: nowhere to go, and no crash on the way");

            GridFrame emptyMap = At(1f, Vector2.zero);
            GridFrame zeroTiles = GridFrame.Layout(View, 0, 10, 1f, Vector2.zero);
            Assert.IsFalse(zeroTiles.Valid, "a map with no tiles is not a grid");
            Assert.IsTrue(emptyMap.Valid, "while the ordinary one still is");
        }
    }
}
