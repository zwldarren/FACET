using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// How far the HUD's panels reach into the screen from each edge, in pixels. The map has to cover
    /// everything else; see <see cref="PanLimits"/>. Built by <see cref="HudLayout.Insets"/>.
    /// </summary>
    public readonly struct ScreenInsets
    {
        public readonly float Left;
        public readonly float Right;
        public readonly float Top;
        public readonly float Bottom;

        public ScreenInsets(float left, float right, float top, float bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }
    }

    /// <summary>
    /// Where the camera may pan, given how much of the screen the HUD owns.
    ///
    /// The rule used to be "the view is never allowed to show the void outside the map", which reads
    /// well until the HUD covers part of the screen: the camera could then only pan the map's bottom
    /// edge down to the top of the build bar, so at a working zoom the bottom rows - exactly where a
    /// belt run has to be routed - were rows the player could never look at. The rule here is the one
    /// that survives the HUD: **the map covers everything the HUD does not**, so the void behind a panel
    /// is allowed and a tile at the map's edge can always be panned into the open.
    ///
    /// Covering the open screen is the minimum, not the whole bound: the camera may also cross the map's
    /// edge by <see cref="DefaultEdgeSlack"/> of the screen, so an edge tile ends up clear of the card
    /// that used to hide it rather than glued against it.
    ///
    /// Pure arithmetic - no camera, no scene, no input - so the bound is unit-testable and the rig stays
    /// a reader of input and nothing else.
    /// </summary>
    public static class PanLimits
    {
        /// <summary>How far past the map's edge the camera may pan, as a fraction of the screen's width
        /// and height. A twentieth of the screen reads as a deliberate margin rather than a gap, at
        /// every resolution and every zoom.</summary>
        public const float DefaultEdgeSlack = 0.05f;

        /// <summary>
        /// Where an orthographic, top-down camera centred on <paramref name="centre"/> may sit, in world
        /// units. <paramref name="halfHeight"/> is half the visible height (the camera's orthographic
        /// size) and <paramref name="aspect"/> the screen's, so the visible width follows from it.
        /// <paramref name="edgeSlack"/> is <see cref="DefaultEdgeSlack"/> by default; 0 keeps the map
        /// hard against the panels' strips.
        /// </summary>
        public static Vector3 Clamp(Vector3 centre, float halfHeight, float aspect, float screenWidth,
            float screenHeight, int mapWidth, int mapHeight, ScreenInsets insets,
            float edgeSlack = DefaultEdgeSlack)
        {
            float halfWidth = halfHeight * aspect;
            float slackWidth = edgeSlack * screenWidth;
            float slackHeight = edgeSlack * screenHeight;

            return new Vector3(
                ClampAxis(centre.x, halfWidth, mapWidth, insets.Left + slackWidth, insets.Right + slackWidth, screenWidth),
                // World y grows up the screen and screen y grows down it, so the bottom strip is the
                // "low" inset on this axis and the top strip the "high" one.
                ClampAxis(centre.y, halfHeight, mapHeight, insets.Bottom + slackHeight, insets.Top + slackHeight, screenHeight),
                centre.z);
        }

        /// <summary>
        /// One axis of the clamp, in world units. The camera may sit half a screen closer to the map's
        /// edge than the map's own edge would allow - by exactly the strip the panels own on that side -
        /// which is what puts the edge tile in the open instead of under a card. A map too small to
        /// cover the open strip is centred in it instead, so a small map is not pushed against a panel.
        /// </summary>
        public static float ClampAxis(float value, float halfExtent, int mapSize, float loInset,
            float hiInset, float screenPixels)
        {
            if (screenPixels <= 0f) return mapSize * 0.5f;   // nothing to measure the inset against yet

            float lowest = halfExtent * (1f - 2f * loInset / screenPixels);
            float highest = mapSize - halfExtent * (1f - 2f * hiInset / screenPixels);

            if (lowest >= highest) return mapSize * 0.5f - halfExtent * (loInset - hiInset) / screenPixels;
            return Mathf.Clamp(value, lowest, highest);
        }
    }
}
