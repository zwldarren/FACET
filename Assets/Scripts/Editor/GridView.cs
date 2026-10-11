using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// The map editor's tile↔pixel geometry, stated as arithmetic and nothing else: fit, zoom, clamp,
    /// the y-flip, hit-testing and scroll-to-cell. The painting window hosts this inside an IMGUI
    /// scroll view, but the computation answers the same whether IMGUI is there or not - which is what
    /// lets it be tested from EditMode instead of only by a human moving a mouse over a window.
    ///
    /// One <see cref="Layout"/> per pass produces the frame; drawing, hit-testing and focusing all
    /// read the same frame, so the flip and the division each have exactly one home. Before this, the
    /// window's class comment feared the failure mode by name ("the classic way a custom grid stops
    /// answering the mouse") and guarded against it with a comment about pass order; a comment about
    /// fragility is what an assertion is for.
    /// </summary>
    public struct GridFrame
    {
        /// <summary>A cell never gets smaller than this. Below it the grid stops reading as tiles at all,
        /// and the map's shape is the one thing the view has to show at every zoom.</summary>
        public const float MinCellSize = 2f;

        /// <summary>The scroll view's bars are drawn over the content's right and bottom edges and take
        /// the mouse when the view ends - after this window has already had its turn - so the paintable
        /// rectangle stops short of them and a drag on a bar cannot paint the tiles underneath.</summary>
        public const float ScrollbarThickness = 16f;

        /// <summary>Map width in tiles. Zero (with <see cref="Valid"/> false) for a layout nothing was
        /// drawn into.</summary>
        public int MapWidth;

        /// <summary>Map height in tiles; the y-flip turns rows over from this.</summary>
        public int MapHeight;

        /// <summary>Pixels per tile as laid out. Zero means there is no grid - a view too small for
        /// one, or a map with no tiles.</summary>
        public float Cell;

        /// <summary>The visible rectangle, in GUI coordinates.</summary>
        public Rect View;

        /// <summary>The map's whole drawn size in pixels: cell times tiles.</summary>
        public Vector2 ContentSize;

        /// <summary>The scroll offset actually in effect, clamped to what the content permits - the
        /// offset the cells are really drawn at.</summary>
        public Vector2 Scroll;

        /// <summary>The content's rectangle in GUI coordinates: the view's origin pushed back by the
        /// scroll. A GUI point is inside a cell of the map when it is inside this.</summary>
        public Rect Content;

        /// <summary>The visible part of the view that the brush owns: the view minus the scrollbar
        /// tracks a drag over them is not a drag over a tile.</summary>
        public Rect Hit;

        /// <summary>Whether this frame describes a grid at all.</summary>
        public bool Valid => Cell > 0f && MapWidth > 0 && MapHeight > 0 && View.width >= 1f;

        /// <summary>The content rect <c>GUI.BeginScrollView</c> takes: the map's whole size at the
        /// layout's origin, in the scroll view's own coordinate space.</summary>
        public Rect ScrollContent => new Rect(0f, 0f, ContentSize.x, ContentSize.y);

        /// <summary>
        /// Lay the map out in the view at the given zoom, and return the frame the whole pass should
        /// draw and hit-test against. The map is fitted to the view first, so zoom 1 means "the whole
        /// map, as large as it goes", and the zoom multiplies that fit rather than naming a pixel
        /// size - which is what keeps resizing the window from needing the zoom re-derived. The fit
        /// stops a pixel short of the view because zoom 1 is meant to show everything at once, and
        /// float rounding that left the content a fraction wider than the view would put a scrollbar
        /// on a grid nobody scrolled. The requested scroll is clamped here, where the content size is
        /// known: a zoom back out leaves a larger offset behind than the content can take.
        /// </summary>
        public static GridFrame Layout(Rect view, int mapWidth, int mapHeight, float zoom, Vector2 requestedScroll)
        {
            if (mapWidth < 1 || mapHeight < 1 || view.width < 1f || view.height < 1f)
                return new GridFrame { View = view };   // Cell 0: a frame that draws and answers nothing

            float fit = Mathf.Min((view.width - 1f) / mapWidth, (view.height - 1f) / mapHeight);
            float cell = Mathf.Max(MinCellSize, fit * zoom);
            var size = new Vector2(cell * mapWidth, cell * mapHeight);

            var scroll = requestedScroll;
            scroll.x = Mathf.Clamp(scroll.x, 0f, Mathf.Max(0f, size.x - view.width));
            scroll.y = Mathf.Clamp(scroll.y, 0f, Mathf.Max(0f, size.y - view.height));

            var hit = new Rect(view.x, view.y, view.width, view.height);
            if (size.x > view.width) hit.width -= ScrollbarThickness;
            if (size.y > view.height) hit.height -= ScrollbarThickness;

            return new GridFrame
            {
                MapWidth = mapWidth,
                MapHeight = mapHeight,
                Cell = cell,
                View = view,
                ContentSize = size,
                Scroll = scroll,
                Content = new Rect(view.x - scroll.x, view.y - scroll.y, size.x, size.y),
                Hit = hit,
            };
        }

        /// <summary>The tile at a cell, in the scroll view's content coordinates. The grid is drawn
        /// bottom-up - the map's y = 0 row is the last one - because the map's y grows upward and the
        /// screen's grows down, and this is the one place that flip is made. Every other walk of the
        /// layout (fills, doors, focusing) asks for a rect here rather than restating the flip.</summary>
        public Rect CellRect(int x, int y)
            => new Rect(x * Cell, (MapHeight - 1 - y) * Cell, Cell, Cell);

        /// <summary>Which tile a GUI point is over, if any. Both the visible bar-free rectangle and
        /// the scrolled content have to agree, because the content runs on under the panel and under
        /// the scrollbars when the map is zoomed in; a point inside the map itself never leaves the
        /// content, but the arithmetic is bounded here so the caller cannot index a tile the map
        /// does not have.</summary>
        public bool TryCellAt(Vector2 guiPoint, out int x, out int y)
        {
            x = y = -1;
            if (!Valid || !Hit.Contains(guiPoint) || !Content.Contains(guiPoint)) return false;

            int cx = Mathf.FloorToInt((guiPoint.x - Content.x) / Cell);
            int cy = Mathf.FloorToInt((Content.yMax - guiPoint.y) / Cell);
            if (cx < 0 || cy < 0 || cx >= MapWidth || cy >= MapHeight) return false;

            x = cx;
            y = cy;
            return true;
        }

        /// <summary>The scroll offset that would put a tile in the middle of what is visible, clamped
        /// against the scroll view's own extent - that is what the scroll view clamps against, and the
        /// bar-free hit rect would stop short of the last stretch a bar can travel, so an edge tile
        /// would then never quite centre. Returns the current offset when there is no grid.</summary>
        public Vector2 ScrollTo(int x, int y)
        {
            if (!Valid) return Scroll;

            Vector2 centre = CellRect(x, y).center;
            return new Vector2(
                Mathf.Clamp(centre.x - View.width * 0.5f, 0f, Mathf.Max(0f, ContentSize.x - View.width)),
                Mathf.Clamp(centre.y - View.height * 0.5f, 0f, Mathf.Max(0f, ContentSize.y - View.height)));
        }
    }
}
