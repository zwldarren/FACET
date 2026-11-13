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

        /// <summary>Level 1: a surface lifted off the card - a key, a chip, a track, the stockpile, the lesson
        /// popup - by one step, never by a new colour. The step is 1.6:1 against the card, so a lifted surface
        /// reads as its own object at 1280x720 without any of its words being read; at 1.08:1 the HUD was one
        /// dark rectangle with text floating in it (#37).</summary>
        public static Color Raised(Palette palette) => Blend(palette.HudPanel, palette.HudText, 0.20f);

        /// <summary>A lifted surface under the pointer: 1.4:1 above <see cref="Raised"/>.</summary>
        public static Color Hovered(Palette palette) => Blend(palette.HudPanel, palette.HudText, 0.30f);

        /// <summary>A key's own hairline, lighter than the key it rings so the edge reads against the card
        /// (2.6:1) as well as against the key.</summary>
        public static Color Seam(Palette palette) => Blend(palette.HudPanel, palette.HudText, 0.34f);

        /// <summary>Level 1.5: the milled edge that keeps a card or a control apart from what is behind it -
        /// seen as an edge, never read as a line. A card beside the map sits on the dark background, which is
        /// within 1.05:1 of the card's own ink, so this edge is the only thing that outlines it there: 1.9:1
        /// against that background.</summary>
        public static Color Edge(Palette palette) => Blend(palette.HudPanel, palette.HudText, 0.24f);

        /// <summary>Level 2: a hairline or a divider - visible as an edge, never as a surface.</summary>
        public static Color Rule(Palette palette) => Blend(palette.HudPanel, palette.HudText, 0.17f);

        /// <summary>Level 3: secondary words - a role, a unit, a "then".</summary>
        public static Color Muted(Palette palette) => Blend(palette.HudText, palette.HudPanel, 0.42f);

        /// <summary>Level 3.5: the quietest readable words - seeds, hints, footers.</summary>
        public static Color Faint(Palette palette) => Blend(palette.HudText, palette.HudPanel, 0.62f);

        /// <summary>Secondary words on a <see cref="Raised"/> surface: <see cref="Muted"/> drops to 3.5:1 there,
        /// this keeps 5:1.</summary>
        public static Color Quiet(Palette palette) => Blend(palette.HudText, palette.HudPanel, 0.26f);

        /// <summary>A surface that speaks in a hue - the toast, the end-of-run banner: the card's ink a third of
        /// the way to the hue, so a refusal and good news are two different boxes and not one box in two text
        /// colours. Paper words stay 7:1 or better on either.</summary>
        public static Color Voice(Palette palette, Color hue) => Blend(palette.HudPanel, hue, 0.34f);

        /// <summary>The shadow of something that just arrived over the game - the toast, the lesson popup - a
        /// deeper band than a card's, so it reads as a layer above the cards rather than one of them.</summary>
        public static Color LayerShadow(Palette palette)
        {
            Color shadow = palette.HudShadow;
            shadow.a = Mathf.Min(1f, shadow.a * 1.5f);
            return shadow;
        }

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
    }
}
