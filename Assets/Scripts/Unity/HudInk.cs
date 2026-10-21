using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The HUD's five luminance levels, as a ramp over the palette's two anchors (the panel ink and the
    /// paper text), plus the three semantic hues the HUD is allowed to spend.
    ///
    /// The old HUD mixed three or four near-blacks and then gave every build role its own hue, so depth
    /// read as mush and colour meant "which tab" rather than "what is happening". Here depth is one
    /// ordered ramp - a card is <see cref="Panel"/>, a raised row or button is <see cref="Raised"/>, a
    /// hairline is <see cref="Rule"/>, secondary words are <see cref="Muted"/>/<see cref="Faint"/>, and
    /// the words that matter are <see cref="Paper"/> - and colour is left to say only: focused, warning,
    /// good. The two mineral hues reach the HUD through the rasterised shape marks, not through a
    /// tile's costume.
    /// </summary>
    public static class HudInk
    {
        /// <summary>Level 0: the card's own ink, the deepest surface on screen.</summary>
        public static Color Panel(Palette palette) => palette.HudPanel;

        /// <summary>Level 1: a row or a button lifted off the card by one step, never by a new colour.</summary>
        public static Color Raised(Palette palette) => Blend(palette.HudPanel, palette.HudText, 0.07f);

        /// <summary>Level 1.5: the milled edge that keeps a card or a control apart from the map - seen as
        /// an edge, never read as a line. Quieter than <see cref="Rule"/>, because a card's outline is
        /// repeated a dozen times a screen and a dozen visible outlines is a wireframe.</summary>
        public static Color Edge(Palette palette) => Blend(palette.HudPanel, palette.HudText, 0.11f);

        /// <summary>Level 2: a hairline or a divider - visible as an edge, never as a surface.</summary>
        public static Color Rule(Palette palette) => Blend(palette.HudPanel, palette.HudText, 0.17f);

        /// <summary>Level 3: secondary words - a role, a unit, a "then".</summary>
        public static Color Muted(Palette palette) => Blend(palette.HudText, palette.HudPanel, 0.42f);

        /// <summary>Level 3.5: the quietest readable words - seeds, hints, footers.</summary>
        public static Color Faint(Palette palette) => Blend(palette.HudText, palette.HudPanel, 0.62f);

        /// <summary>Level 4: the paper the words are printed on - body text and the card titles.</summary>
        public static Color Paper(Palette palette) => palette.HudText;

        /// <summary>The one focus hue: selection, the active tab, the wave number.</summary>
        public static Color Focus(Palette palette) => palette.HudAccent;

        /// <summary>The one warning hue: a jam, a starving gun, a Core being hit, a refusal.</summary>
        public static Color Warning(Palette palette) => palette.HudWarn;

        /// <summary>Good news is the focus hue at full strength: the HUD spends no seventh colour on it,
        /// so a green pixel on this screen always means something is wrong elsewhere.</summary>
        public static Color Good(Palette palette) => palette.HudAccent;

        public static Color Blend(Color from, Color to, float t) => Color.Lerp(from, to, t);

        /// <summary>The same colour, faded: for a refusal or a hit that ages out.</summary>
        public static Color Fade(Color colour, float alpha)
            => new Color(colour.r, colour.g, colour.b, colour.a * alpha);
    }
}
