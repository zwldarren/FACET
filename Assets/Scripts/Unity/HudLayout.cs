using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Where the HUD's pieces sit, as pure geometry in screen pixels: the cards and the bar the view
    /// draws, the stockpile readout beside the status card, and the strips of the screen they own.
    ///
    /// A table rather than fields on <see cref="HudView"/> because two views need the same answer: the
    /// HUD draws the pieces, and the camera rig must not leave a tile hidden behind one. Panel sizes
    /// are unscaled and multiplied by <see cref="Scale"/> - the same scale the HUD's fonts use - so the
    /// layout keeps the same fraction of the screen at every resolution.
    /// </summary>
    public static class HudLayout
    {
        /// <summary>Gap between a panel and the screen edge.</summary>
        public const float Margin = 12f;

        // Unscaled panel sizes. The status card is the largest by a distance, which is why the left
        // and top strips are measured from it (see Insets).
        public const float StatusWidth = 470f;

        /// <summary>The card after the quieting: the event feed and the always-on diagnostic sentences
        /// are gone - a line that only ever says "all clear" trains the player to stop reading it -
        /// leaving a wave news line, the wave's bill of materials as chips, and the start-wave button.</summary>
        public const float StatusHeight = 200f;

        /// <summary>The controls cluster collapsed: the pause and help buttons, a small cluster in the
        /// screen's top-right corner. The keyboard list lives behind the help button instead of costing
        /// a corner of the screen all run.</summary>
        public const float ControlsWidth = 74f;
        public const float ControlsHeight = 34f;

        /// <summary>The gap between the cluster's two buttons.</summary>
        public const float ControlGap = 6f;

        /// <summary>The help card the help button opens: the keyboard and the restart button, which
        /// stays two clicks from a misfire. It is a transient overlay - it covers the map while open the
        /// way a toast does, and the camera reserves no strip for it.</summary>
        public const float HelpWidth = 210f;
        public const float HelpHeight = 178f;

        public const float InfoWidth = 392f;
        public const float InfoHeight = 134f;
        public const float BarHeight = 126f;

        /// <summary>The gap the info card floats above the bar with.</summary>
        public const float InfoGap = 8f;

        /// <summary>Room for the tutorial's heading and its four lines.</summary>
        public const float TutorialHeight = 134f;

        /// <summary>The gap the tutorial sits below the status card with. Deliberately the same 8 the
        /// info card floats above the bar with: those two are the HUD's only cards that hang off another
        /// piece rather than a screen edge, and both should read as belonging to it.</summary>
        public const float TutorialGap = 8f;

        /// <summary>The gap between the status card's right edge and the stockpile readout - the readout
        /// has no panel of its own, so this is what says the two belong together.</summary>
        public const float StockpileGap = 10f;

        /// <summary>Room for a five-digit stockpile and its shape; the readout grows rightwards from the
        /// status card, and this is the room it may grow within.</summary>
        public const float StockpileWidth = 210f;

        /// <summary>Tall enough to hold the largest type, and to centre the readout on the status card's
        /// title line - the two are read as one row.</summary>
        public const float StockpileHeight = 44f;

        /// <summary>The gap a toast floats below the top cards with: inside the map area, clear of every
        /// panel, where a player who just clicked is already looking.</summary>
        public const float ToastGap = 16f;

        /// <summary>How much bigger than a 720p screen everything is drawn.</summary>
        public static float Scale(float screenHeight) => Mathf.Clamp(screenHeight / 720f, 0.8f, 2.2f);

        public static Rect Status(float s)
            => new Rect(Margin * s, Margin * s, StatusWidth * s, StatusHeight * s);

        /// <summary>The controls cluster: pause and help, hung off the screen's top-right corner. The
        /// keyboard list lives in <see cref="Help"/> behind the help button - reference material is
        /// worth a click, not a corner of the screen.</summary>
        public static Rect Controls(float screenWidth, float s)
            => new Rect(screenWidth - (Margin + ControlsWidth) * s, Margin * s,
                ControlsWidth * s, ControlsHeight * s);

        /// <summary>The help card the help button opens, hung off the same corner as the cluster, just
        /// below it. Deliberately not in <see cref="Panels"/> and not in <see cref="Insets"/>: it is
        /// transient the way a toast is, a panel the player opens and closes over the map.</summary>
        public static Rect Help(float screenWidth, float s)
            => new Rect(screenWidth - (Margin + HelpWidth) * s,
                (Margin + ControlsHeight + ControlGap) * s, HelpWidth * s, HelpHeight * s);

        /// <summary>The tutorial card: directly under the status card, its width, on the campaign's
        /// first map only. Anchored to the card, like the stockpile readout, because it explains that
        /// corner of the HUD.
        ///
        /// It takes clicks, so a build under it is not an unclickable tile, and <see cref="Insets"/>
        /// widens the top strip over it while it is up.</summary>
        public static Rect Tutorial(float s)
        {
            Rect status = Status(s);

            return new Rect(status.x, status.yMax + TutorialGap * s, StatusWidth * s,
                TutorialHeight * s);
        }

        /// <summary>The stockpile readout: the shape it counts and the number, no panel behind them,
        /// immediately right of the status card at its top edge. Anchored to the card, and it gives up
        /// width rather than ever reaching the controls card on a narrow screen.</summary>
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

        /// <summary>A toast: centred just under the top cards, sized to its own text by the view. It
        /// overlays the map on purpose - news about a click arrives where the player who clicked is
        /// looking - and takes no clicks. While the tutorial card is up it floats under that card.</summary>
        public static Rect Toast(float screenWidth, float s, float width, float height,
            bool tutorialUp = false)
            => new Rect((screenWidth - width) * 0.5f, Insets(s, tutorialUp).Top + ToastGap * s, width, height);

        /// <summary>How many slots <see cref="Panels"/> needs: the two top cards, the tutorial card that
        /// hangs off one of them, the bar, and the info card floating above it.</summary>
        public const int PanelCount = 5;

        /// <summary>
        /// The panels the HUD draws, in screen pixels, for a reader that has to know what the map cannot
        /// be seen <em>through</em>.
        ///
        /// Deliberately not <see cref="Insets"/>: those are one number per side, which is what the camera
        /// bound needs, but a reserve is not a shape - the screen below the status card is free - and a
        /// reader asking "is this tile under a panel?" needs the boxes the HUD draws. Written into
        /// <paramref name="into"/> (which holds <see cref="PanelCount"/> slots) rather than returned, so
        /// the per-frame readers ask it without allocating.
        /// </summary>
        public static int Panels(float screenWidth, float screenHeight, float s, bool tutorialUp,
            Rect[] into)
        {
            int n = 0;
            into[n++] = Status(s);
            if (tutorialUp) into[n++] = Tutorial(s);
            into[n++] = Controls(screenWidth, s);
            Rect bar = Bar(screenWidth, screenHeight, s);
            into[n++] = bar;
            into[n++] = Info(bar, s);
            return n;
        }

        /// <summary>
        /// How far the permanent pieces reach into the screen from each edge, in pixels: the strip the
        /// camera rig keeps the map clear of, so every tile can be panned out from under the HUD.
        ///
        /// Each strip is the widest panel on its side, so two are not the panel they are named after: the
        /// right strip is the info card, the top strip the status card. The bottom strip is the bar alone
        /// and the stockpile readout lives inside the top strip. The controls cluster and the help card
        /// are covered by the right strip already; the help card is transient besides (see
        /// <see cref="Help"/>).
        ///
        /// <paramref name="tutorialUp"/> extends the top strip over the tutorial card while it is up,
        /// widened across the whole strip rather than only the left where the card sits: one number per
        /// side, with the panels arranged inside it.
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
