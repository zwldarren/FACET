using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Facet.Game
{
    /// <summary>
    /// The handful of uGUI pieces the HUD is built from, and the builders that make them.
    ///
    /// Every widget is constructed in code - no prefabs, no scene authoring: a panel is an Image, a
    /// label is a TextMeshProUGUI, a button is an Image that answers the pointer. What the builders add
    /// over raw uGUI is the discipline the IMGUI view kept: every widget remembers what it is showing
    /// through the graphic it drives, so an update that changes nothing writes nothing and the canvas
    /// never rebuilds for a frame the HUD stood still in.
    ///
    /// The type itself is <see cref="HudType"/>'s one four-step scale and <see cref="HudInk"/>'s five
    /// levels; this class only places and drives. TMP replaced the engine's Arial because an asset font
    /// can carry the tracking the HUD asks for and because "no LegacyRuntime in the player" is a build
    /// fact, not a preference - see the HUD's issue.
    ///
    /// The geometry helpers speak in screen pixels measured from one named corner, the language
    /// <see cref="HudLayout"/> lays the panels out in - the view converts, this class only places.
    /// </summary>
    public static class HudUi
    {
        /// <summary>The one font every label uses: TMP's own Liberation Sans asset, imported with the
        /// TextMesh Pro essentials under <c>Assets/TextMesh Pro</c>. One place to swap in a licensed face
        /// later, and the reason no built player carries <c>LegacyRuntime.ttf</c>.</summary>
        private static TMP_FontAsset _font;

        /// <summary>The one white sprite every panel and border tints: a texture per colour would be a
        /// hundred textures differing only by a multiply.</summary>
        private static Sprite _pixel;

        public static TMP_FontAsset Font
        {
            get
            {
                if (_font == null)
                {
                    _font = TMP_Settings.defaultFontAsset;
                    if (_font == null)
                        _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                }

                return _font;
            }
        }

        public static Sprite Pixel
        {
            get
            {
                if (_pixel != null) return _pixel;

                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "FACET/HudPixel",
                    hideFlags = HideFlags.DontSave,
                };
                texture.SetPixel(0, 0, Color.white);
                texture.Apply(false);

                _pixel = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
                _pixel.name = "FACET/HudPixelSprite";
                return _pixel;
            }
        }

        /// <summary>The rounded-rectangle sprites, keyed by radius and stroke: every card, tile, button
        /// and keycap is one of these nine-sliced. Generated once per size rather than shipped, so the HUD
        /// has no art dependency and one corner language at every scale.</summary>
        private static readonly Dictionary<int, Sprite> _shapes = new Dictionary<int, Sprite>();

        /// <summary>How many texture pixels stand for one design pixel. The corners are shaded from a
        /// signed distance field, and three texels per point is what keeps an 8-point arc from stepping.</summary>
        private const int Supersample = 3;

        /// <summary>
        /// A rounded rectangle as a nine-sliced sprite: a filled one when <paramref name="stroke"/> is 0,
        /// otherwise a ring of that stroke hugging the outer edge. The corners carry the radius in design
        /// pixels; the middle is one texel of flat colour that stretches to whatever the rect asks for, which
        /// is what lets one 24-pixel sprite skin a 880-pixel bar and a 20-pixel button alike.
        /// </summary>
        public static Sprite Shape(float radius, float stroke = 0f)
        {
            int r = Mathf.Max(1, Mathf.RoundToInt(radius));
            int s = Mathf.Max(0, Mathf.RoundToInt(stroke));
            int key = r * 16 + s;
            if (_shapes.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            int corner = (r + 1) * Supersample;          // the arc plus one texel of straight run
            int size = corner * 2 + Supersample;         // and one texel of middle to stretch
            float half = size * 0.5f - Supersample;      // a texel of margin for the anti-aliased edge
            float strokeTex = s * Supersample;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "FACET/HudShape" + key,
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float py = y + 0.5f - size * 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - size * 0.5f;
                    float distance = RoundedBoxDistance(px, py, half, half, r * Supersample);

                    // Both bands are one pixel wide: the coverage is the signed distance measured against
                    // the nearer edge, which is the cheapest anti-alias a generated sprite can get.
                    float coverage = strokeTex <= 0f
                        ? Mathf.Clamp01(0.5f - distance)
                        : Mathf.Clamp01(0.5f - Mathf.Max(distance, -strokeTex - distance));

                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(coverage * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);

            var border = new Vector4(corner, corner, corner, corner);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f * Supersample, 0, SpriteMeshType.FullRect, border);
            sprite.name = "FACET/HudShapeSprite" + key;

            _shapes[key] = sprite;
            return sprite;
        }

        /// <summary>The signed distance to a rounded box of half-extents <paramref name="b"/>, negative
        /// inside: the one formula both the fill and the ring are shaded from. Public for the one test that
        /// holds the shapes to the radii it asks for.</summary>
        public static float RoundedBoxDistance(float x, float y, float bx, float by, float radius)
        {
            float qx = Mathf.Abs(x) - (bx - radius);
            float qy = Mathf.Abs(y) - (by - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        /// <summary>A filled surface with a radius: the card, the row, the tile, the keycap.</summary>
        public static HudBox Round(RectTransform parent, string name, Color colour, float radius)
        {
            HudBox box = Box(parent, name, colour);
            box.Image.sprite = Shape(radius);
            box.Image.type = Image.Type.Sliced;
            return box;
        }

        /// <summary>The soft sprites behind <see cref="Shadow"/>, keyed by the radius they wrap and the
        /// band they fade over.</summary>
        private static readonly Dictionary<int, Sprite> _shadows = new Dictionary<int, Sprite>();

        /// <summary>How far a card's shadow falls, in screen pixels - the same down-and-right the world's
        /// cast shadows take, because the light is one light (see <see cref="Palette.ShadowOffsetPixels"/>).</summary>
        private const float ShadowOffsetX = 2.5f;
        private const float ShadowOffsetY = -3.5f;

        /// <summary>How wide the shadow's soft band is, in screen pixels.</summary>
        public const float ShadowBlur = 7f;

        /// <summary>
        /// A card's cast shadow: the card's own rounded silhouette, its edge softened over
        /// <see cref="ShadowBlur"/> pixels, laid under the card and nudged down and right. The image is the
        /// card grown by the blur on every side and the silhouette inside it is the card's own, so the band
        /// that shows is exactly the part of the shadow the card does not cover.
        ///
        /// Screen pixels, like the world's shadows: a shadow that scaled with the HUD's 0.8-to-2.2 would be a
        /// smear on a large screen and a hairline on a small one. The cards sit on the pale map, where a
        /// hard opaque edge would read as a cutout; the band is what makes them sit on it.
        /// </summary>
        public static HudBox Shadow(RectTransform parent, string name, float radius, Color colour)
        {
            RectTransform rt = Node(parent, name);
            rt.SetAsFirstSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-ShadowBlur + ShadowOffsetX, -ShadowBlur + ShadowOffsetY);
            rt.offsetMax = new Vector2(ShadowBlur + ShadowOffsetX, ShadowBlur + ShadowOffsetY);

            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = ShadowShape(radius);
            image.type = Image.Type.Sliced;
            image.color = colour;
            image.raycastTarget = false;
            return new HudBox(image);
        }

        /// <summary>The soft silhouette a shadow is drawn from: opaque within the card, falling to nothing
        /// over the band outside it. The corners are the card's, so a rounded card casts a rounded shadow.</summary>
        private static Sprite ShadowShape(float radius)
        {
            int r = Mathf.Max(1, Mathf.RoundToInt(radius));
            int b = Mathf.RoundToInt(ShadowBlur);
            int key = r * 64 + b;
            if (_shadows.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            int corner = (r + b + 1) * Supersample;
            int size = corner * 2 + Supersample;
            float half = size * 0.5f - Supersample;
            float inside = half - b * Supersample;
            float radiusTex = r * Supersample;
            float band = b * Supersample;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "FACET/HudShadow" + key,
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float py = y + 0.5f - size * 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - size * 0.5f;
                    float distance = RoundedBoxDistance(px, py, inside, inside,
                        Mathf.Min(radiusTex, inside));

                    // Smoothstep rather than the linear ramp the fills use: a shadow's edge is soft, and a
                    // linear falloff reads as a second, darker border around the card.
                    float t = Mathf.Clamp01(distance / band);
                    float coverage = 1f - t * t * (3f - 2f * t);

                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(coverage * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);

            var border = new Vector4(corner, corner, corner, corner);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f * Supersample, 0, SpriteMeshType.FullRect, border);
            sprite.name = "FACET/HudShadowSprite" + key;

            _shadows[key] = sprite;
            return sprite;
        }

        /// <summary>The milled edge around a surface, as a ring rather than four square bars: one image,
        /// one write, and the corners of a card no longer read as cut paper.</summary>
        public static HudBorder Border(RectTransform parent, string name, Color colour, float radius,
            float stroke = 1f)
        {
            RectTransform rt = Node(parent, name);
            StretchFull(rt);

            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = Shape(radius, stroke);
            image.type = Image.Type.Sliced;
            image.color = colour;
            image.raycastTarget = false;
            return new HudBorder(image);
        }

        /// <summary>Free the shared sprites and textures, called on the HUD's teardown so an Editor's
        /// scene reloads do not leak a texture a play; whatever builds again recreates them from nothing.
        /// The font is an imported asset owned by the project, not ours to destroy.</summary>
        public static void ReleaseShared()
        {
            if (_pixel != null)
            {
                UnityEngine.Object.Destroy(_pixel.texture);
                UnityEngine.Object.Destroy(_pixel);
                _pixel = null;
            }

            FreeSprites(_shapes);
            FreeSprites(_shadows);
            _font = null;
        }

        private static void FreeSprites(Dictionary<int, Sprite> sprites)
        {
            foreach (Sprite sprite in sprites.Values)
            {
                if (sprite == null) continue;
                UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }

            sprites.Clear();
        }

        // ------------------------------------------------------------------ building

        /// <summary>An empty rectangle under <paramref name="parent"/>, the skeleton of every widget.</summary>
        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>A filled rectangle: the panels, the bars, the stripes, the tile backs.</summary>
        public static HudBox Box(RectTransform parent, string name, Color colour)
        {
            RectTransform rt = Node(parent, name);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = Pixel;
            image.color = colour;
            image.raycastTarget = false;
            return new HudBox(image);
        }

        /// <summary>A label at one of <see cref="HudType"/>'s four sizes. The colour arrives per update,
        /// the way <see cref="HudText.Set(string, Color)"/> takes it; the style arguments are the ones the
        /// IMGUI view named per style, and the tracking is the scale's one rule rather than a per-label
        /// decision.</summary>
        public static HudText Text(RectTransform parent, string name, float fontSize, FontStyle style,
            TextAnchor anchor, bool wrap, bool tracked = false, bool rich = false)
        {
            RectTransform rt = Node(parent, name);
            var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null) text.font = Font;
            text.text = string.Empty;
            text.fontSize = Mathf.Max(8f, fontSize);
            text.fontStyle = ToTmp(style);
            text.alignment = ToTmp(anchor);
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.color = Color.white;
            // Tracking is the labels' rule, not the prose's: +8 % on a six-word button is air, +8 % on
            // a sentence is a line that no longer fits the card it was written for.
            text.characterSpacing = tracked ? HudType.Tracking : 0f;
            text.richText = rich;
            text.raycastTarget = false;
            text.enableAutoSizing = false;
            return new HudText(text);
        }

        private static FontStyles ToTmp(FontStyle style)
        {
            switch (style)
            {
                case FontStyle.Bold: return FontStyles.Bold;
                case FontStyle.Italic: return FontStyles.Italic;
                case FontStyle.BoldAndItalic: return FontStyles.Bold | FontStyles.Italic;
                default: return FontStyles.Normal;
            }
        }

        private static TextAlignmentOptions ToTmp(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.TopLeft;
            }
        }

        /// <summary>A shape as a picture: the texture <see cref="HudIcons"/> rasterised, the shape's
        /// region as the raw image's uv rect, and a tint carrying only the alpha a fading number asks
        /// for - the texture already carries the palette's colour.</summary>
        public static RawImage Icon(RectTransform parent, string name, HudIcons.Icon icon)
        {
            RectTransform rt = Node(parent, name);
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = icon.Texture;
            raw.uvRect = icon.Uv;
            raw.raycastTarget = false;
            return raw;
        }

        /// <summary>A button: a filled, bordered rectangle that answers the pointer. It fires on the
        /// press - what made the IMGUI bar feel immediate - through uGUI's EventSystem, and reports its
        /// own hover so the view can dress it without an event.</summary>
        public static HudButton Button(RectTransform parent, string name, float width, float height,
            string label, float labelSize, Color labelColour, Action pressed)
        {
            RectTransform root = Node(parent, name);
            var button = root.gameObject.AddComponent<HudButton>();
            button.Fill = Round(root, name + ".back", Color.white, 6f);
            StretchFull(button.Fill.Rect);
            button.Fill.Image.raycastTarget = true;
            button.Label = Text(root, name + ".label", labelSize, FontStyle.Bold,
                TextAnchor.MiddleCenter, wrap: false, tracked: true);
            // Inset by 4 on every side, so the label's middle is the button's middle. Not Stretch: its
            // fourth number is a height, and a 4-tall strip hung off the top edge centred every label there.
            StretchFull(button.Label.Rect);
            button.Label.Rect.offsetMin = new Vector2(4f, 4f);
            button.Label.Rect.offsetMax = new Vector2(-4f, -4f);
            button.Border = Border(root, name + ".frame", Color.white, 6f, 1f);
            button.Label.Set(label, labelColour);
            button.Pressed = pressed;
            return button;
        }

        /// <summary>A bar's fill: a rectangle whose width is the fraction, read at a glance where a
        /// number has to be read twice. Hidden at zero, because an empty bar says the same thing.</summary>
        public static HudBar BarFill(RectTransform parent, string name, float left, float bottom,
            float width, float height, float radius = 0f)
        {
            RectTransform rt = Node(parent, name);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(left, bottom);
            rt.sizeDelta = new Vector2(width, height);

            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = radius > 0f ? Shape(radius) : Pixel;
            image.type = radius > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return new HudBar(rt, image, width);
        }

        // ------------------------------------------------------------------ placing

        /// <summary>The one primitive the corner helpers unfold: anchors, pivot, position, size.</summary>
        public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        /// <summary>Hang a rectangle off its parent's top-left, its own top-left on the anchor:
        /// (x, yFromTop) is where the piece's top-left corner lands.</summary>
        public static void TopLeft(RectTransform rt, float x, float yFromTop, float width, float height)
            => Place(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, -yFromTop), new Vector2(width, height));

        /// <summary>The same, off the parent's top-right: xFromRight grows leftwards.</summary>
        public static void TopRight(RectTransform rt, float xFromRight, float yFromTop, float width,
            float height)
            => Place(rt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-xFromRight, -yFromTop), new Vector2(width, height));

        /// <summary>Hang a rectangle off the parent's bottom-left corner.</summary>
        public static void BottomLeft(RectTransform rt, float x, float yFromBottom, float width,
            float height)
            => Place(rt, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(x, yFromBottom),
                new Vector2(width, height));

        /// <summary>Hang a rectangle off the parent's bottom-right corner, its own bottom-right on it.</summary>
        public static void BottomRight(RectTransform rt, float xFromRight, float yFromBottom, float width,
            float height)
            => Place(rt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-xFromRight, yFromBottom), new Vector2(width, height));

        /// <summary>Hang a rectangle off the parent's top edge, centred on it.</summary>
        public static void TopCentre(RectTransform rt, float yFromTop, float width, float height)
            => Place(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -yFromTop), new Vector2(width, height));

        /// <summary>Fill the parent.</summary>
        public static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Span the parent's width, inset left and right, hanging yFromTop below its top edge
        /// and <paramref name="height"/> tall: the row a card's lines sit on.</summary>
        public static void Stretch(RectTransform rt, float left, float yFromTop, float right, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(left, -yFromTop - height);
            rt.offsetMax = new Vector2(-right, -yFromTop);
        }

        // ------------------------------------------------------------------ event system

        /// <summary>The one EventSystem the HUD's buttons answer through, built only if the scene has
        /// none. The module is the Input System's own - this project enables only the new Input System,
        /// so a legacy StandaloneInputModule would error on its first frame. It lives beside the canvas
        /// rather than under it, so a HUD rebuild never kills it.</summary>
        public static void EnsureEventSystem(Transform parent)
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;

            var go = new GameObject("FACET EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                typeof(InputSystemUIInputModule));
            go.transform.SetParent(parent, false);
        }
    }

    /// <summary>A filled rectangle that writes only what changed: the Image holds its own last colour,
    /// so a setter that would paint the same colour costs a comparison and nothing else - and a canvas
    /// that is not touched is not rebuilt.</summary>
    public sealed class HudBox
    {
        public readonly Image Image;

        public HudBox(Image image) => Image = image;

        public RectTransform Rect => Image.rectTransform;

        public void Show(bool on) => Image.gameObject.SetActive(on);

        public void Set(Color colour)
        {
            if (Image.color == colour) return;
            Image.color = colour;
        }
    }

    /// <summary>A label that writes only what changed. The comparison is a string equality on a short
    /// line - cheaper than the vertex rebuild a changed label pays for, and free when nothing changed.</summary>
    public sealed class HudText
    {
        public readonly TMP_Text Text;

        public HudText(TMP_Text text) => Text = text;

        public RectTransform Rect => Text.rectTransform;

        /// <summary>The width the current text asks for, measured on demand: the stockpile's icon and
        /// the toast's box are sized to the words they sit with.</summary>
        public float PreferredWidth => Text.preferredWidth;
        public float PreferredHeight => Text.preferredHeight;

        public void Show(bool on) => Text.gameObject.SetActive(on);

        public void Set(string value)
        {
            value ??= string.Empty;
            if (Text.text == value) return;
            Text.text = value;
        }

        public void Set(string value, Color colour)
        {
            Set(value);
            SetColour(colour);
        }

        /// <summary>Only the colour: a line whose words hold while its news changes - a cost turning
        /// amber, a stripe going warning-coloured - is one write, not a rewrite.</summary>
        public void SetColour(Color colour)
        {
            if (Text.color == colour) return;
            Text.color = colour;
        }
    }

    /// <summary>The milled edge around a surface: one sliced ring image whose colour is the whole of the
    /// state a border has to carry. Replaces four edge bars, which is why the cards and buttons can be
    /// rounded without their outlines staying square.</summary>
    public sealed class HudBorder
    {
        public readonly Image Image;

        public HudBorder(Image image) => Image = image;

        public RectTransform Rect => Image.rectTransform;

        public void Show(bool on)
        {
            if (Image.gameObject.activeSelf != on) Image.gameObject.SetActive(on);
        }

        public void Set(Color colour)
        {
            if (Image.color == colour) return;
            Image.color = colour;
        }
    }

    /// <summary>A bar's fill, as a fraction of the width it was built with - how much Core is left, how
    /// much of the wave's clock has run. The colour and the fraction are each their own write.</summary>
    public sealed class HudBar
    {
        private readonly RectTransform _fill;
        private readonly Image _image;
        private readonly float _width;

        public HudBar(RectTransform fill, Image image, float width)
        {
            _fill = fill;
            _image = image;
            _width = width;
        }

        public void Set(float fraction, Color colour)
        {
            fraction = Mathf.Clamp01(fraction);
            _fill.gameObject.SetActive(fraction > 0f);
            if (fraction <= 0f) return;

            if (_image.color != colour) _image.color = colour;

            float width = _width * fraction;
            Vector2 size = _fill.sizeDelta;
            if (!Mathf.Approximately(size.x, width))
            {
                size.x = width;
                _fill.sizeDelta = size;
            }
        }
    }

    /// <summary>A button the EventSystem answers: it fires on the left-button press - what makes the
    /// build bar feel immediate - and reports its own hover, so the view dresses it from its update pass
    /// instead of an event storm. Visuals are the view's business.</summary>
    public sealed class HudButton : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler,
        IPointerExitHandler
    {
        /// <summary>The box the button is drawn from and the words on it - dressed by the view.</summary>
        public HudBox Fill;
        public HudText Label;
        public HudBorder Border;

        /// <summary>Fired on the press. The HUD hands it the same latch the keyboard uses, so a click
        /// can never say something a key press would not.</summary>
        public Action Pressed;

        /// <summary>The rectangle the button is drawn in, for the view's own placement passes.</summary>
        public RectTransform Root => (RectTransform)transform;

        public bool Hovered { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            Pressed?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData) => Hovered = true;
        public void OnPointerExit(PointerEventData eventData) => Hovered = false;

        private void OnDisable() => Hovered = false;
    }
}
