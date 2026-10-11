using System;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws the map's ground and its tile grid as one mesh of thin quads: every tile the map calls
    /// ground filled with <see cref="Palette.Platform"/>, then the lines on top of it. That is what
    /// separates the buildable world from the void the camera sits in.
    ///
    /// <b>The ground is drawn per tile, not as one rectangle.</b> A map may leave tiles out - the void
    /// kind is how a map is something other than a rectangle - and those tiles are not ground, so nothing
    /// is drawn there and the background shows through. What is filled is therefore runs of adjacent
    /// ground within a row, and what is lined is only the boundaries that touch ground, so a hole in the
    /// map is a hole and not a rectangle with a grid over it. A plain map comes out of the same loop as
    /// one run per row, which is why this costs a few quads less than filling the rectangle did.
    ///
    /// The grid only ever changes when the zoom moves the screen-pixel size, which is exactly the
    /// rebuild reason the gate owns: the line width is a screen-space constant, so the mesh is
    /// rebuilt when the pixel size moves and not otherwise. Without it the lines are only as wide as
    /// the zoom they happened to be built at, and vanish into sub-pixel aliasing at the far end of
    /// the camera's range.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class GridRenderer : MeshView
    {
        /// <summary>Floor for the line's half-width, so a degenerate quad can never reach the mesh.</summary>
        private const float MinHalfWidth = 0.0005f;

        private TileGrid _grid;
        private TerrainField _terrain;

        protected override Palette.Layer Layer => Colors.GridLayer;

        protected override void OnInitialized()
        {
            _grid = World.TileGrid;
            _terrain = World.Terrain;
        }

        /// <summary>The ground follows the map, and so do its lines.</summary>
        protected override void OnWorldRebound()
        {
            _grid = World.TileGrid;
            _terrain = World.Terrain;
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            float half = Mathf.Max(Colors.GridLinePixels * frame.WorldPerPixel * 0.5f, MinHalfWidth);

            AppendGround();

            // Every boundary that has ground on at least one side gets a line, and a boundary with
            // ground on both sides is an ordinary grid line. Runs of live boundaries are merged, so a
            // plain map draws the same W+1 and H+1 full-length lines it always did, at the same cost.
            for (int x = 0; x <= _grid.Width; x++)
            {
                AppendRuns(alongY: true, y => LiveColumnBoundary(x, y),
                    (start, end) => AppendRect(x - half, start, x + half, end, Colors.GridLine));
            }

            for (int y = 0; y <= _grid.Height; y++)
            {
                AppendRuns(alongY: false, x => LiveRowBoundary(x, y),
                    (start, end) => AppendRect(start, y - half, end, y + half, Colors.GridLine));
            }
        }

        /// <summary>Fill every run of adjacent ground in a row: one quad per run, so a plain map is one
        /// quad per row and an irregular one is as many as it has spans.</summary>
        private void AppendGround()
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                AppendRuns(alongY: false, x => Drawn(new Int2(x, y)),
                    (start, end) => AppendRect(start, y, end, y + 1, Colors.Platform));
            }
        }

        /// <summary>Walk one line of cells from its start to its end, merging consecutive live cells
        /// into a run and emitting one quad per run. The merge rule lives here so a run is defined
        /// once, not three times: scanning starts at 0, skips dead cells one at a time, and extends
        /// a run while the next cell along the line is still live, so the run boundaries and their
        /// order are exactly what the inline loops produced. The caller's <paramref name="live"/>
        /// predicate closes over the line's fixed coordinate; <paramref name="alongY"/> picks the
        /// walk direction - a column when true, whose length is the grid's height, and a row when
        /// false, whose length is its width.</summary>
        private void AppendRuns(bool alongY, Func<int, bool> live, Action<int, int> emit)
        {
            int count = alongY ? _grid.Height : _grid.Width;
            int p = 0;
            while (p < count)
            {
                if (!live(p)) { p++; continue; }

                int start = p;
                while (p < count && live(p)) p++;
                emit(start, p);
            }
        }

        /// <summary>Is the ground at this tile drawn at all? The void is the one kind that is not.</summary>
        private bool Drawn(Int2 cell)
            => _grid.InBounds(cell) && _terrain.KindAt(cell).IsDrawn();

        /// <summary>Does the vertical boundary at x, on row y, touch any ground? Off the map is not
        /// ground, so the map's own edge is lined.</summary>
        private bool LiveColumnBoundary(int x, int y)
            => Drawn(new Int2(x - 1, y)) || Drawn(new Int2(x, y));

        /// <summary>Does the horizontal boundary at y, on column x, touch any ground?</summary>
        private bool LiveRowBoundary(int x, int y)
            => Drawn(new Int2(x, y - 1)) || Drawn(new Int2(x, y));
    }
}
