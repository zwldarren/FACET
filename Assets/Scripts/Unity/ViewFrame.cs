using Facet.Core;

namespace Facet.Game
{
    /// <summary>
    /// The per-frame facts the view layer draws with: how far the renderer is between the previous
    /// and the current simulation tick, the size of one screen pixel in world units, and the tile the
    /// cursor is over.
    ///
    /// It carries no palette and no per-view state, so a view can be handed a frame without being
    /// handed the driver. Positions lerp with <see cref="Alpha"/>; anything measured in screen pixels -
    /// outlines, grid lines, range rings, cursor borders - scales with <see cref="WorldPerPixel"/>.
    /// </summary>
    public readonly struct ViewFrame
    {
        /// <summary>How far the renderer is between the previous and the current tick, 0..1.</summary>
        public readonly float Alpha;

        /// <summary>Size of one screen pixel in world units, at the current resolution and zoom.</summary>
        public readonly float WorldPerPixel;

        /// <summary>Tile under the cursor, or (-1,-1) when there is none.</summary>
        public readonly Int2 CursorCell;

        public ViewFrame(float alpha, float worldPerPixel, Int2 cursorCell = default)
        {
            Alpha = alpha;
            WorldPerPixel = worldPerPixel;
            CursorCell = cursorCell;
        }
    }
}
