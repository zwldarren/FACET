using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Where the HUD's pieces sit, as pure geometry in screen pixels: the three cards and the bar the
    /// view draws, the stockpile readout floating beside the status card, the strip a toast floats in,
    /// and the strips of the screen they own.
    ///
    /// A table rather than fields on <see cref="HudView"/> because two views need the same answer: the
    /// HUD draws the pieces, and the camera rig must not leave a tile hidden behind one. A second copy
    /// of "the bar is 126 units tall" would drift from the first.
    ///
    /// Panel sizes are unscaled and multiplied by <see cref="Scale"/> - the same scale the HUD's fonts
    /// use - so the layout stays the same fraction of the screen at every resolution instead of the
    /// text growing inside a fixed box.
    /// </summary>
    public static class HudLayout
    {
        /// <summary>Gap between a panel and the screen edge.</summary>
        public const float Margin = 12f;

        // Unscaled panel sizes. The status card is the largest by a distance, which is why the left
        // and top strips are measured from it (see Insets).
        public const float StatusWidth = 470f;

        /// <summary>One line shorter than it used to be: the stockpile line moved out of the card and
        /// became the readout beside it.</summary>
        public const float StatusHeight = 232f;

        public const float ControlsWidth = 210f;
        public const float ControlsHeight = 100f;
        public const float InfoWidth = 392f;
        public const float InfoHeight = 134f;
        public const float BarHeight = 126f;

        /// <summary>The gap the info card floats above the bar with.</summary>
        public const float InfoGap = 8f;

        /// <summary>Room for the tutorial's heading and its four lines.</summary>
        public const float TutorialHeight = 134f;

        /// <summary>The gap the tutorial sits below the status card with. Deliberately the same 8 the
        /// info card floats above the bar with: those two are the HUD's only cards that hang off
        /// another piece rather than off a screen edge, and both should read as belonging to it.</summary>
        public const float TutorialGap = 8f;

        /// <summary>The gap between the status card's right edge and the stockpile readout - the
        /// readout has no panel of its own, so this is what says the two belong together.</summary>
        public const float StockpileGap = 10f;

        /// <summary>Room for a five-digit stockpile and its shape. The readout is drawn from its left
        /// edge - the number, then the shape after it - so it grows rightwards from the status card;
        /// this is the room it may grow within.</summary>
        public const float StockpileWidth = 210f;

        /// <summary>Tall enough to hold the largest type on the HUD, and to put its centre on the
        /// status card's title line - the readout and the card's first line are read as one row.</summary>
        public const float StockpileHeight = 44f;

        /// <summary>The gap a toast floats below the top cards with: inside the map area, clear of
        /// every panel, where a player who just clicked is already looking.</summary>
        public const float ToastGap = 16f;

        /// <summary>How much bigger than a 720p screen everything is drawn.</summary>
        public static float Scale(float screenHeight) => Mathf.Clamp(screenHeight / 720f, 0.8f, 2.2f);

        public static Rect Status(float s)
            => new Rect(Margin * s, Margin * s, StatusWidth * s, StatusHeight * s);

        public static Rect Controls(float screenWidth, float s)
            => new Rect(screenWidth - (Margin + ControlsWidth) * s, Margin * s,
                ControlsWidth * s, ControlsHeight * s);

        /// <summary>
        /// The tutorial card: directly under the status card, the width of it, on the campaign's first
        /// map only. Anchored to the card rather than to the screen for the same reason the stockpile
        /// readout is - it explains the corner of the HUD the player is reading, so it stays with it.
        ///
        /// It takes clicks, which is what keeps a build under it from being a click the player cannot
        /// see, and <see cref="Insets"/> widens the top strip over it for as long as it is up - so it
        /// neither hides a tile nor swallows a click meant for one.
        /// </summary>
        public static Rect Tutorial(float s)
        {
            Rect status = Status(s);

            return new Rect(status.x, status.yMax + TutorialGap * s, StatusWidth * s,
                TutorialHeight * s);
        }

        /// <summary>
        /// The stockpile readout: the shape it counts and the number, with no panel behind them,
        /// immediately right of the status card and at its top edge. Anchored to the card rather than to
        /// the screen, so it reads as part of the same corner of the HUD; it gives up width rather than
        /// ever reaching the controls card on a narrow screen.
        /// </summary>
        public static Rect Stockpile(float screenWidth, float s)
        {
            Rect status = Status(s);
            float left = status.xMax + StockpileGap * s;
            float right = Controls(screenWidth, s).x - StockpileGap * s;
            float width = Mathf.Min(StockpileWidth * s, Mathf.Max(0f, right - left));

            return new Rect(left, Margin * s, width, StockpileHeight * s);
        }

        public static Rect Bar(float screenWidth, float screenHeight, float s)
            => new Rect(Margin * s, screenHeight - (Margin + BarHeight) * s,
                screenWidth - 2f * Margin * s, BarHeight * s);

        public static Rect Info(Rect bar, float s)
            => new Rect(bar.xMax - InfoWidth * s, bar.y - (InfoGap + InfoHeight) * s,
                InfoWidth * s, InfoHeight * s);

        /// <summary>
        /// A toast: centred on the screen just under the top cards, sized to its own text by the view.
        /// It overlays the map on purpose - it is news about a click, so it arrives where the player who
        /// clicked is looking, and it takes no clicks itself. While the tutorial card is up it floats
        /// under that card instead: the card is solid HUD for as long as it is there, and the news
        /// should not land on the instructions.
        /// </summary>
        public static Rect Toast(float screenWidth, float s, float width, float height,
            bool tutorialUp = false)
            => new Rect((screenWidth - width) * 0.5f, Insets(s, tutorialUp).Top + ToastGap * s, width, height);

        /// <summary>
        /// How far the permanent pieces reach into the screen from each edge, in pixels: the strip the
        /// camera rig keeps the map clear of, so that every tile can be panned out from under the HUD.
        ///
        /// Each strip is the widest panel on its side, so two of the four are not the panel they are
        /// named after: the right strip is the wider of the two right-hand cards (the info card), and
        /// the top strip the taller of the two top cards (the status card). The bottom strip is the bar
        /// alone - the info card floats above it, where the right strip has already cleared it - and the
        /// stockpile readout lives inside the top strip, beside the card that strip is measured from.
        ///
        /// <paramref name="tutorialUp"/> extends the top strip over the tutorial card, which is not a
        /// permanent piece - it is up on the campaign's first map and gone for the rest of the run - but
        /// while it is up it is as solid as the status card above it, so the map has to be able to get
        /// out from under it too. Widened across the whole strip rather than only the left where the card
        /// sits, which is how the status card already sets the strip for the shorter controls card beside
        /// it: one number per side, and the panels arranged inside it.
        /// </summary>
        public static ScreenInsets Insets(float s, bool tutorialUp = false)
        {
            float top = Mathf.Max(StatusHeight + (tutorialUp ? TutorialGap + TutorialHeight : 0f),
                ControlsHeight);

            return new ScreenInsets(
                (Margin + StatusWidth) * s,
                (Margin + Mathf.Max(ControlsWidth, InfoWidth)) * s,
                (Margin + top) * s,
                (Margin + BarHeight) * s);
        }
    }
}
