namespace Facet.Game
{
    /// <summary>
    /// The HUD's type scale: exactly four sizes, in 720p design units, and one tracking rule. The HUD
    /// used to carry eight sizes - 10, 11, 12, 13, 15, 17, 22, 26 - one picked per widget, which is a
    /// scale only by accident: nothing told a reader that a 13 was a "name" and a 12 was a "number".
    ///
    /// The four are what the screen actually needs: a micro line for numbers and keys, a label for the
    /// words that name a thing, a body for the one sentence a card leads with, and a display size for the
    /// one number the run is about. They are 10 / 12 / 15 / 28 after the art pass raised the middle two:
    /// at 960x600 the scale is 0.83, and an 11-point label there rounded down to 8 px. <see cref="Tracking"/>
    /// is TMP's character spacing in font units: 9 is +9 % of an em on every label, which is what makes
    /// 10 point uppercase legible without a second weight.
    /// </summary>
    public static class HudType
    {
        /// <summary>Counts, keys, seeds, diagnostics, table figures: the smallest type that still reads
        /// at 720p and survives a 50 % downscale.</summary>
        public const float Micro = 10f;

        /// <summary>The name of a thing - a building, a role, a button, a row on a card.</summary>
        public const float Label = 12f;

        /// <summary>The one sentence a card leads with, and every card title: the scale's one emphatic
        /// size below the display.</summary>
        public const float Body = 15f;

        /// <summary>The stockpile and the banner: the numbers a glance is allowed to land on.</summary>
        public const float Display = 28f;

        /// <summary>Letterspacing, as TMP's character-spacing value: +9 % of an em.</summary>
        public const float Tracking = 9f;

        /// <summary>The scale, for a test that has to hold the budget at four.</summary>
        public static readonly float[] Steps = { Micro, Label, Body, Display };
    }
}
