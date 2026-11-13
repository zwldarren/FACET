using UnityEngine;

namespace Facet.Game
{
    /// <summary>Where the HUD's pieces sit, as unscaled screen-pixel geometry multiplied by
    /// <see cref="Scale"/>, so the layout holds the same fraction of the screen at any resolution. A shared
    /// table because the HUD draws these panels and the camera rig has to keep the map out from under them.
    /// The four persistent pieces stay within 30 % of a 720p screen with the tutorial up. The budget was 22 %
    /// while a key was words alone; the machine previews raised it on purpose, not by drift.</summary>
    public static class HudLayout
    {
        /// <summary>Gap between a panel and the screen edge.</summary>
        public const float Margin = 12f;

        // Unscaled panel sizes. The status rail is the tallest card, which is why the left and top
        // strips are measured from it (see Insets).
        public const float StatusWidth = 360f;

        /// <summary>The one size every HUD mark meaning "build this" is drawn at (briefing chips, the
        /// stockpile coin). Map badges are in tiles instead - see <see cref="Palette.DemandFigureRadius"/>.</summary>
        public const float DemandFigure = 16f;

        public const float StatusHeight = 153f;

        /// <summary>The top-right cluster: the start-wave button, three square buttons, and MENU.</summary>
        public const float ControlsHeight = 34f;

        // The wave and menu buttons are wider than the squares: they carry words, not glyphs.
        public const float WaveButtonWidth = 128f;
        public const float MenuButtonWidth = 44f;
        public const float ControlGap = 6f;

        public const float ControlsWidth =
            WaveButtonWidth + 4f * ControlGap + 3f * ControlsHeight + MenuButtonWidth;

        /// <summary>Wide enough for the keyboard list: the card is reference material and is not read at a
        /// glance.</summary>
        public const float HelpWidth = 250f;
        public const float HelpInset = 14f;
        public const float HelpHeaderTop = 9f;
        public const float HelpLineTop = 30f;
        public const float HelpLineStep = 15f;
        public const float HelpLineHeight = 14f;
        public const float HelpLineSize = HudType.Label;

        public const float HelpButtonGap = 8f;
        public const float HelpButtonHeight = 24f;
        public const float HelpFoot = 12f;

        /// <summary>Derived rather than written down, so the card grows with the list it prints.</summary>
        public static float HelpHeightFor(int lines)
            => HelpLineTop + lines * HelpLineStep + HelpButtonGap + HelpButtonHeight + HelpFoot;

        /// <summary>The info card's base size: header, a three-line blurb, a stats row, the facing row and
        /// the six inspect rows.</summary>
        public const float InfoWidth = 300f;
        public const float InfoHeight = 138f;

        /// <summary>How much taller the card is with a sorter's filter picker on it.</summary>
        public const float InfoPickHeight = 66f;

        /// <summary>One key of the build bar: wide enough to carry a picture of the building (about forty design
        /// units) rather than only its name.</summary>
        public const float BarTileWidth = 100f;
        public const float BarTileHeight = 80f;
        public const float BarPad = 8f;
        public const float BarGap = 3f;

        /// <summary>Keys the shipped table puts on the bar - <see cref="BuildKind.Belt"/> to
        /// <see cref="BuildKind.Sink"/> less the two kinds retired from play (<see cref="BuildCatalog.Retired"/>:
        /// the drill and the wall), since a belt on ore mines it. A table that grows past it widens the bar
        /// through <see cref="BarWidthFor"/> rather than laying keys over the card's edge.</summary>
        public const int BarTiles = 9;

        /// <summary>The full table's width, spelled from the tile numbers so the two cannot drift. Fewer
        /// keys make a narrower bar, not a stretched one.</summary>
        public const float BarWidth = 2f * BarPad + BarTiles * BarTileWidth + (BarTiles - 1) * BarGap;
        public const float BarHeight = BarTileHeight + 2f * BarPad;

        public static float BarWidthFor(int tiles)
            => tiles <= 0 ? 0f : 2f * BarPad + tiles * BarTileWidth + (tiles - 1) * BarGap;

        public const float InfoGap = 8f;

        /// <summary>The tutorial's heading and its five steps at the micro size.</summary>
        public const float TutorialHeight = 108f;

        /// <summary>The same 8 as <see cref="InfoGap"/>: these two are the HUD's only cards that hang off
        /// another piece rather than a screen edge, and both should read as belonging to it.</summary>
        public const float TutorialGap = 8f;

        public const float StockpileGap = 10f;

        /// <summary>Room for a five-digit stockpile and its shape.</summary>
        public const float StockpileWidth = 124f;

        /// <summary>Tall enough to centre the readout on the status card's title line.</summary>
        public const float StockpileHeight = 44f;

        public const float ToastGap = 12f;

        /// <summary>How much bigger than a 720p screen everything is drawn.</summary>
        public static float Scale(float screenHeight) => Mathf.Clamp(screenHeight / 720f, 0.8f, 2.2f);

        public static Rect Status(float s)
            => new Rect(Margin * s, Margin * s, StatusWidth * s, StatusHeight * s);

        /// <summary>Hung off the screen's top-right corner.</summary>
        public static Rect Controls(float screenWidth, float s)
            => new Rect(screenWidth - (Margin + ControlsWidth) * s, Margin * s,
                ControlsWidth * s, ControlsHeight * s);

        /// <summary>Opens under the cluster. Deliberately not in <see cref="Panels"/> nor
        /// <see cref="Insets"/>: it is transient the way a toast is, so the camera reserves no strip for
        /// it.</summary>
        public static Rect Help(float screenWidth, float s, int lines)
            => new Rect(screenWidth - (Margin + HelpWidth) * s,
                (Margin + ControlsHeight + ControlGap) * s, HelpWidth * s, HelpHeightFor(lines) * s);

        /// <summary>Directly under the status card, its width, on the campaign's first map only. It takes
        /// clicks, which is why <see cref="Insets"/> widens the top strip while it is up.</summary>
        public static Rect Tutorial(float s)
        {
            Rect status = Status(s);

            return new Rect(status.x, status.yMax + TutorialGap * s, StatusWidth * s,
                TutorialHeight * s);
        }

        /// <summary>Right of the status card, at its top edge. Gives up width rather than ever reaching the
        /// controls on a narrow screen.</summary>
        public static Rect Stockpile(float screenWidth, float s)
        {
            Rect status = Status(s);
            float left = status.xMax + StockpileGap * s;
            float right = Controls(screenWidth, s).x - StockpileGap * s;
            float width = Mathf.Min(StockpileWidth * s, Mathf.Max(0f, right - left));

            return new Rect(left, Margin * s, width, StockpileHeight * s);
        }

        /// <summary>A centred strip, narrower than the screen on purpose, so the map stays visible either
        /// side of it. <paramref name="tiles"/> is the run's, not the table's: a tutorial hands buildings
        /// over one line at a time, so the bar starts one key wide and grows.</summary>
        public static Rect Bar(float screenWidth, float screenHeight, float s, int tiles = BarTiles)
        {
            float width = Mathf.Min(BarWidthFor(tiles) * s, screenWidth - 2f * Margin * s);

            return new Rect((screenWidth - width) * 0.5f,
                screenHeight - (Margin + BarHeight) * s, width, BarHeight * s);
        }

        /// <summary>Floats above the bar, bottom-anchored so a taller card grows upward, and hangs off the
        /// screen's right edge rather than the centred bar's. <paramref name="pickerUp"/> and
        /// <paramref name="extra"/> are the card's two kinds of growth, which a reader of the panel boxes
        /// cannot infer from this method's own numbers.</summary>
        public static Rect Info(float screenWidth, float screenHeight, float s, bool pickerUp = false,
            float extra = 0f)
        {
            Rect bar = Bar(screenWidth, screenHeight, s);
            float height = InfoHeight + (pickerUp ? InfoPickHeight : 0f) + extra;

            return new Rect(screenWidth - (Margin + InfoWidth) * s,
                bar.y - (InfoGap + height) * s, InfoWidth * s, height * s);
        }

        /// <summary>Centred just under the top cards, sized to its own text by the view, and takes no
        /// clicks: news about a click arrives where the player who clicked is looking.</summary>
        public static Rect Toast(float screenWidth, float s, float width, float height,
            bool tutorialUp = false)
            => new Rect((screenWidth - width) * 0.5f, Insets(s, tutorialUp).Top + ToastGap * s, width, height);

        /// <summary>Slots <see cref="Panels"/> needs.</summary>
        public const int PanelCount = 5;

        /// <summary>The boxes the HUD draws, for a reader asking "is this tile behind a panel?". Not
        /// <see cref="Insets"/>, which is one reserve per side and not a shape - the screen below the
        /// status card is free. Written into <paramref name="into"/> so per-frame readers do not allocate.
        /// <paramref name="pickerUp"/>, <paramref name="barTiles"/> and <paramref name="infoExtra"/> must be
        /// the growth the HUD actually drew, or an arrow is placed on a strip the player cannot see.</summary>
        public static int Panels(float screenWidth, float screenHeight, float s, bool tutorialUp,
            Rect[] into, bool pickerUp = false, int barTiles = BarTiles, float infoExtra = 0f)
        {
            int n = 0;
            into[n++] = Status(s);
            if (tutorialUp) into[n++] = Tutorial(s);
            into[n++] = Controls(screenWidth, s);
            into[n++] = Bar(screenWidth, screenHeight, s, barTiles);
            into[n++] = Info(screenWidth, screenHeight, s, pickerUp, infoExtra);
            return n;
        }

        /// <summary>How far the permanent pieces reach into the screen from each edge: the strips the camera
        /// rig keeps the map clear of, so every tile can be panned out from under the HUD. Each strip is the
        /// widest panel on its side, so a strip is not necessarily the panel it is named after.
        /// <paramref name="tutorialUp"/> extends the top strip, across its whole width rather than only the
        /// left where the card sits: one number per side.</summary>
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
