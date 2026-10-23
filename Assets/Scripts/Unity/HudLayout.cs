using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Where the HUD's pieces sit, as pure geometry in screen pixels: the cards and the bar the view
    /// draws, the stockpile readout beside the status card, and the strips of the screen they own.
    ///
    /// The budget is the point of this table. At 720p the screen is 1152 x 720 design units and the four
    /// persistent pieces - status, tutorial, build bar, info - must spend no more than 22 % of it with the
    /// tutorial up. The old table spent 48 %: two 470-wide cards down the left edge, a full-width 126-unit
    /// bar, and a 392-wide info card floating over it. Here the left column is one 360-wide rail, the bar
    /// is a centred 880 x 52 strip, and the info card is 300 x 138; the four together are 181,120 of the
    /// 829,440 units, 21.8 %. A table rather than fields on <see cref="HudView"/> because two views need
    /// the same answer: the HUD draws the pieces, and the camera rig must not leave a tile hidden behind
    /// one. Panel sizes are unscaled and multiplied by <see cref="Scale"/> - the same scale the HUD's
    /// fonts use - so the layout keeps the same fraction of the screen at every resolution.
    /// </summary>
    public static class HudLayout
    {
        /// <summary>Gap between a panel and the screen edge.</summary>
        public const float Margin = 12f;

        // Unscaled panel sizes. The status rail is the tallest card, which is why the left and top
        // strips are measured from it (see Insets).
        public const float StatusWidth = 360f;

        /// <summary>
        /// The demand figure's edge, in design units: the one size every HUD mark that means "build this"
        /// is drawn at - the briefing's chips and the stockpile's coin. 16 at 720p is 16 px, and 13 px in
        /// the 960x600 published frame; it was 11 in the chips and 24 beside the stockpile, and the 11 was
        /// the smallest thing on screen while being the first thing a player has to read (issue #12).
        /// The map's own badges are in tiles rather than pixels and zoom with the map, so they share
        /// <see cref="Palette.DemandFigureRadius"/> instead.
        /// </summary>
        public const float DemandFigure = 16f;

        /// <summary>The rail after the quieting: the event feed and the always-on diagnostic sentences
        /// are gone - a line that only ever says "all clear" trains the player to stop reading it -
        /// leaving a wave news line, two rows of the bill of materials (the wave coming and the one after
        /// it: §5 previews both), the two diagnostic lines and the seed. Five units taller than it was so
        /// each briefing row is tall enough for a <see cref="DemandFigure"/>.</summary>
        public const float StatusHeight = 153f;

        /// <summary>The controls cluster collapsed: the wave, pause, speed-gear, help and menu buttons, a
        /// small cluster in the screen's top-right corner (a 54-unit wave, three 34-unit squares, a 44-unit
        /// MENU and four 6-unit gaps). The keyboard list lives behind the help button instead of costing a
        /// corner of the screen all run; the wave button lives here so the status rail does not owe it a
        /// row. The menu button is the cluster's one door out of the run - the same door Esc opens - and is
        /// a word rather than a glyph for the same reason the wave button is.</summary>
        public const float ControlsWidth = 224f;
        public const float ControlsHeight = 34f;

        /// <summary>The wave button is wider than the square ones: it carries a word, not a glyph.</summary>
        public const float WaveButtonWidth = 54f;

        /// <summary>The menu button, on the same reasoning: four letters at the micro size, and the
        /// cluster's rightmost piece, so the way out is where the eye looks for it.</summary>
        public const float MenuButtonWidth = 44f;

        /// <summary>The gap between the cluster's buttons.</summary>
        public const float ControlGap = 6f;

        /// <summary>The help card the help button opens: the keyboard and the restart button, which
        /// stays two clicks from a misfire. It is a transient overlay - it covers the map while open the
        /// way a toast does, and the camera reserves no strip for it.</summary>
        public const float HelpWidth = 250f;

        /// <summary>The help card's inner geometry, in design units. One place the HUD's card and the
        /// shell's copy of it both read from, so the line step, the inset and the card's own height cannot
        /// drift apart when a line is added or the step is retuned. Wide enough for the label-size lines:
        /// the card is reference material, and reference material that has to be squinted at is not read.</summary>
        public const float HelpInset = 14f;
        public const float HelpHeaderTop = 9f;
        public const float HelpLineTop = 30f;
        public const float HelpLineStep = 15f;
        public const float HelpLineHeight = 14f;
        public const float HelpLineSize = HudType.Label;

        /// <summary>The restart button's gap under the last key line, its height, and the card's foot
        /// below it.</summary>
        public const float HelpButtonGap = 8f;
        public const float HelpButtonHeight = 24f;
        public const float HelpFoot = 12f;

        /// <summary>How tall the help card is for a list of <paramref name="lines"/> pairs: the last
        /// line, the restart button under it, and the foot. Derived rather than written down, so a tenth
        /// line pushes the button down and the card grows with it.</summary>
        public static float HelpHeightFor(int lines)
            => HelpLineTop + lines * HelpLineStep + HelpButtonGap + HelpButtonHeight + HelpFoot;

        /// <summary>The info card's base size: a header, a three-line blurb, one stats row and the facing
        /// row with its three buttons, and room for the six inspect rows. The blurb's box is three lines
        /// because the catalogue's sentences are three lines at this width - the card used to assume two and
        /// a long one (the cutter's) printed through the stats row.</summary>
        public const float InfoWidth = 300f;
        public const float InfoHeight = 138f;

        /// <summary>
        /// How much taller the info card is while a sorter's filter picker is on it: the quadrant canvas,
        /// the mode button beside it and the one line that says what the picker would route for. The card
        /// is bottom-anchored, so it grows upward and the picker takes the band directly under the header -
        /// the machine's setting is the first thing to read, before its ports and its health.
        /// </summary>
        public const float InfoPickHeight = 66f;

        /// <summary>One key's width, and the air either side of the row: the numbers that lay a bar out, so a
        /// bar of any length is the same keys in a shorter strip rather than a different set of keys.</summary>
        public const float BarTileWidth = 66.75f;
        public const float BarPad = 12f;
        public const float BarGap = 5f;

        /// <summary>How many keys the shipped content table has - the bar's width at the end of a run. Eleven,
        /// the ordinals <see cref="BuildKind.Belt"/> to <see cref="BuildKind.Sink"/>; a table that grows past it
        /// widens the bar through <see cref="BarWidthFor"/> rather than laying keys over the card's edge.</summary>
        public const int BarTiles = 11;

        /// <summary>The build bar: a centred strip, not a full-width shelf. 808.25 wide is enough for the
        /// whole table at 720p on one row - eleven 66.75-unit keys with their air - so the bar is a keyboard
        /// rather than a two-level catalogue, and the map stays visible either side of it. 52 tall holds the
        /// 40-unit key and its 6-unit air; the four units it gives back pay for the info card's third blurb
        /// line, so the pair stays inside the 22 % the layout may spend.
        ///
        /// A bar with fewer keys is <em>narrower</em> rather than stretched, and it is
        /// <see cref="BarWidthFor"/> that says so: a run whose tutorial has handed over one building is a bar
        /// one key wide, not one key six hundred units wide or a wide card with an empty shelf in it. This
        /// constant is the full table's width, spelled from the tile numbers so the two cannot drift.
        /// </summary>
        public const float BarWidth = 2f * BarPad + BarTiles * BarTileWidth + (BarTiles - 1) * BarGap;
        public const float BarHeight = 52f;

        /// <summary>The width a bar of this many keys needs. A negative or zero count is a bar of no keys,
        /// which is not a width at all - the caller that has nothing to draw draws nothing.</summary>
        public static float BarWidthFor(int tiles)
            => tiles <= 0 ? 0f : 2f * BarPad + tiles * BarTileWidth + (tiles - 1) * BarGap;

        /// <summary>The gap the info card floats above the bar with.</summary>
        public const float InfoGap = 8f;

        /// <summary>Room for the tutorial's heading and its five steps at the micro size: five 11-unit
        /// lines under a 24-unit header, with the air the card needs to not look clipped at the bottom.
        /// The "stuck?" tip the card used to carry is the help card's business now - one list of keys in
        /// one place. The card is the same width as the status rail and hangs off it, so the left column
        /// reads as one piece.</summary>
        public const float TutorialHeight = 108f;

        /// <summary>The gap the tutorial sits below the status card with. Deliberately the same 8 the
        /// info card floats above the bar with: those two are the HUD's only cards that hang off another
        /// piece rather than a screen edge, and both should read as belonging to it.</summary>
        public const float TutorialGap = 8f;

        /// <summary>The gap between the status card's right edge and the stockpile readout - the readout
        /// has no panel of its own, so this is what says the two belong together.</summary>
        public const float StockpileGap = 10f;

        /// <summary>Room for a five-digit stockpile and its shape; the readout grows rightwards from the
        /// status card, and this is the room it may grow within. The seed moved to the status card's footer,
        /// so the pill is only as wide as a bank has to be.</summary>
        public const float StockpileWidth = 124f;

        /// <summary>Tall enough to hold the display size, and to centre the readout on the status card's
        /// title line - the two are read as one row.</summary>
        public const float StockpileHeight = 44f;

        /// <summary>The gap a toast floats below the top cards with: inside the map area, clear of every
        /// panel, where a player who just clicked is already looking.</summary>
        public const float ToastGap = 12f;

        /// <summary>How much bigger than a 720p screen everything is drawn.</summary>
        public static float Scale(float screenHeight) => Mathf.Clamp(screenHeight / 720f, 0.8f, 2.2f);

        public static Rect Status(float s)
            => new Rect(Margin * s, Margin * s, StatusWidth * s, StatusHeight * s);

        /// <summary>The controls cluster: wave, pause, gear, menu and help, hung off the screen's top-right
        /// corner. The keyboard list lives in <see cref="Help"/> behind the help button - reference
        /// material is worth a click, not a corner of the screen.</summary>
        public static Rect Controls(float screenWidth, float s)
            => new Rect(screenWidth - (Margin + ControlsWidth) * s, Margin * s,
                ControlsWidth * s, ControlsHeight * s);

        /// <summary>The help card the help button opens, hung off the same corner as the cluster, just
        /// below it. Deliberately not in <see cref="Panels"/> and not in <see cref="Insets"/>: it is
        /// transient the way a toast is, a panel the player opens and closes over the map. Its height is
        /// <see cref="HelpHeightFor"/>, so it follows the list it prints.</summary>
        public static Rect Help(float screenWidth, float s, int lines)
            => new Rect(screenWidth - (Margin + HelpWidth) * s,
                (Margin + ControlsHeight + ControlGap) * s, HelpWidth * s, HelpHeightFor(lines) * s);

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

        /// <summary>The build bar: a centred strip, narrower than the screen on purpose. A full-width
        /// shelf spent 17 % of the frame on its own; the same tiles fit in a strip the eye reads as a
        /// control, with the map visible either side of it.
        ///
        /// <paramref name="tiles"/> is how many keys it carries, which is the run's to say and not the
        /// table's: a tutorial hands buildings over one line at a time, so the bar starts one key wide and
        /// grows. Left out, it is the shipped table's full row.</summary>
        public static Rect Bar(float screenWidth, float screenHeight, float s, int tiles = BarTiles)
        {
            float width = Mathf.Min(BarWidthFor(tiles) * s, screenWidth - 2f * Margin * s);

            return new Rect((screenWidth - width) * 0.5f,
                screenHeight - (Margin + BarHeight) * s, width, BarHeight * s);
        }

        /// <summary>The info card floating above the bar, bottom-anchored so a taller card grows upward.
        /// It hangs off the screen's right edge rather than the bar's, because the bar is centred now and
        /// a card aligned to its right corner would float in the middle of the frame.
        /// <paramref name="pickerUp"/> is the sorter picker's band: the one thing on the card that a
        /// reader of the panels cannot see from the card's place, which is why it is a parameter rather
        /// than a number the reader guesses.</summary>
        public static Rect Info(float screenWidth, float screenHeight, float s, bool pickerUp = false)
        {
            Rect bar = Bar(screenWidth, screenHeight, s);
            float height = InfoHeight + (pickerUp ? InfoPickHeight : 0f);

            return new Rect(screenWidth - (Margin + InfoWidth) * s,
                bar.y - (InfoGap + height) * s, InfoWidth * s, height * s);
        }

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
        ///
        /// <paramref name="pickerUp"/> is the sorter picker's band on the info card, which the locator has
        /// to know about for the same reason it knows about the tutorial card: an arrow put under a panel
        /// points at nothing the player can see. <paramref name="barTiles"/> is how many keys the bar carries
        /// this frame - a tutorial's bar grows a key at a time, and a reader that measured the full shelf
        /// would keep arrows clear of a strip that is not there.</summary>
        public static int Panels(float screenWidth, float screenHeight, float s, bool tutorialUp,
            Rect[] into, bool pickerUp = false, int barTiles = BarTiles)
        {
            int n = 0;
            into[n++] = Status(s);
            if (tutorialUp) into[n++] = Tutorial(s);
            into[n++] = Controls(screenWidth, s);
            into[n++] = Bar(screenWidth, screenHeight, s, barTiles);
            into[n++] = Info(screenWidth, screenHeight, s, pickerUp);
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
