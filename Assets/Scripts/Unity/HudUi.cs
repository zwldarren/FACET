using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Facet.Game
{
    /// <summary>The handful of uGUI pieces the HUD is built from, and the builders that adopt the ones a prefab
    /// supplies.
    ///
    /// Every widget remembers what it is showing through the graphic it drives, so an update that changes nothing
    /// writes nothing and the canvas never rebuilds for a frame the HUD stood still in. Type is
    /// <see cref="HudType"/>'s one four-step scale and ink is <see cref="HudInk"/>'s five levels; this class only
    /// places and drives. The geometry helpers speak in screen pixels from one named corner, the language
    /// <see cref="HudLayout"/> lays the panels out in.</summary>
    public static class HudUi
    {
        /// <summary>The one font every label uses: TMP's Liberation Sans, imported with the TextMesh Pro
        /// essentials. One place to swap in a licensed face later, and the reason no built player carries
        /// <c>LegacyRuntime.ttf</c>.</summary>
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

        /// <summary>The rounded-rectangle sprites, keyed by radius and stroke: every card, tile, button and keycap
        /// is one of these nine-sliced. Generated rather than shipped, so the HUD has no art dependency and one
        /// corner language at every scale.</summary>
        private static readonly Dictionary<int, Sprite> _shapes = new Dictionary<int, Sprite>();

        /// <summary>How many texture pixels stand for one design pixel: the corners are shaded from a signed
        /// distance field, and three texels per point keeps an 8-point arc from stepping.</summary>
        private const int Supersample = 3;

        /// <summary>
        /// A rounded rectangle as a nine-sliced sprite: a filled one when <paramref name="stroke"/> is 0,
        /// otherwise a ring of that stroke hugging the outer edge. The corners carry the radius; the middle is one
        /// texel of flat colour that stretches to whatever the rect asks for, which is what lets one 24-pixel
        /// sprite skin an 880-pixel bar and a 20-pixel button alike.
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

        /// <summary>The signed distance to a rounded box of half-extents <paramref name="b"/>, negative inside:
        /// the one formula both the fill and the ring are shaded from.</summary>
        public static float RoundedBoxDistance(float x, float y, float bx, float by, float radius)
        {
            float qx = Mathf.Abs(x) - (bx - radius);
            float qy = Mathf.Abs(y) - (by - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        /// <summary>The soft sprites behind a card's cast shadow, keyed by the radius they wrap and the band they
        /// fade over.</summary>
        private static readonly Dictionary<int, Sprite> _shadows = new Dictionary<int, Sprite>();

        public const float ShadowBlur = 7f;

        /// <summary>The soft silhouette a shadow is drawn from: opaque within the card, falling to nothing over
        /// the band outside it, with the card's own corners.
        ///
        /// Screen pixels, like the world's shadows: a shadow that scaled with the HUD's 0.8-to-2.2 would be a smear
        /// on a large screen. The image is the card grown by the blur on every side, so the band that shows is
        /// exactly the part of the shadow the card does not cover, and the down-and-right nudge is the asset's
        /// (see <see cref="Palette.ShadowOffsetPixels"/>).</summary>
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

        public static HudBox AdoptBox(RectTransform rt, Color colour)
        {
            Image image = RequireImage(rt);
            image.sprite = Pixel;
            image.type = Image.Type.Simple;
            image.color = colour;
            image.raycastTarget = false;
            return new HudBox(image);
        }

        /// <summary>A filled surface with a radius, adopted: the card, the bar's track, a chip's back.</summary>
        public static HudBox AdoptRound(RectTransform rt, Color colour, float radius)
        {
            Image image = RequireImage(rt);
            image.sprite = Shape(radius);
            image.type = Image.Type.Sliced;
            image.color = colour;
            image.raycastTarget = false;
            return new HudBox(image);
        }

        public static HudBorder AdoptBorder(RectTransform rt, Color colour, float radius, float stroke = 1f)
        {
            Image image = RequireImage(rt);
            image.sprite = Shape(radius, stroke);
            image.type = Image.Type.Sliced;
            image.color = colour;
            image.raycastTarget = false;
            return new HudBorder(image);
        }

        /// <summary>A card's cast shadow, adopted. Its band is screen pixels, which is why a panel's design-unit
        /// scaling must leave this node's own rect alone.</summary>
        public static HudBox AdoptShadow(RectTransform rt, float radius, Color colour)
        {
            Image image = RequireImage(rt);
            image.sprite = ShadowShape(radius);
            image.type = Image.Type.Sliced;
            image.color = colour;
            image.raycastTarget = false;
            return new HudBox(image);
        }

        /// <summary>
        /// A shape a prefab already carries: the picture is a runtime-rasterised atlas (see <see cref="Icon"/>), so
        /// the asset holds the node and the uv rect is painted by whoever knows which shape belongs there. What
        /// this adds is the one rule every mark on the HUD obeys - a mark is read, never clicked.
        /// </summary>
        public static RawImage AdoptIcon(RectTransform rt)
        {
            var raw = rt.GetComponent<RawImage>();
            if (raw == null)
                throw new MissingComponentException("HUD prefab icon '" + rt.name + "' carries no RawImage");
            raw.raycastTarget = false;
            return raw;
        }

        private static Image RequireImage(RectTransform rt)
        {
            var image = rt.GetComponent<Image>();
            if (image == null)
                throw new MissingComponentException("HUD prefab surface '" + rt.name + "' carries no Image");
            return image;
        }

        /// <summary>
        /// A prefab panel's design units at this screen's scale: every node's offset and size times the one HUD
        /// scale, which is what a CanvasScaler does for a canvas it owns. The root is left alone - the caller has
        /// already placed it in pixels - and so is a card's shadow, whose band is screen pixels by the shadow's own
        /// rule.
        /// </summary>
        public static void ScaleDesignUnits(RectTransform root, float scale)
        {
            RectTransform[] nodes = root.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < nodes.Length; i++)
            {
                RectTransform node = nodes[i];
                if (node == root || node.name.EndsWith("shadow", StringComparison.Ordinal)) continue;

                node.anchoredPosition *= scale;
                node.sizeDelta *= scale;
            }
        }

        /// <summary>Called on the HUD's teardown so an Editor's scene reloads do not leak a texture a play. The font
        /// is an imported asset owned by the project, not ours to destroy.</summary>
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

        /// <summary>
        /// A label a prefab already carries: the prefab owns where it sits, this owns how it reads, so a label's
        /// rules stay in one place - there is no second way to make one.
        /// </summary>
        public static HudText AdoptText(RectTransform rt, float fontSize, FontStyle style,
            TextAnchor anchor, bool wrap, bool tracked = false, bool rich = false)
        {
            var text = rt.GetComponent<TextMeshProUGUI>();
            if (text == null)
                throw new MissingComponentException("HUD prefab label '" + rt.name + "' carries no TMP text");
            ConfigureText(text, fontSize, style, anchor, wrap, tracked, rich);
            return new HudText(text);
        }

        private static void ConfigureText(TMP_Text text, float fontSize, FontStyle style,
            TextAnchor anchor, bool wrap, bool tracked, bool rich)
        {
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
        }

        /// <summary>A child a prefab promises, named rather than serialized: a missing node is a broken asset, so
        /// it fails here with the name that went missing rather than as a null image three updates later.</summary>
        public static RectTransform Child(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child == null)
                throw new InvalidOperationException(
                    "HUD prefab node '" + name + "' is missing under '" + parent.name + "'");
            return (RectTransform)child;
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

        /// <summary>The radius every button shares: smaller than a card's, because a control is a milled strip
        /// inside the surface rather than a surface of its own.</summary>
        public const float ButtonRadius = 6f;

        /// <summary>
        /// A bar's fill a prefab already carries, adopted: the track and the fill's place in it are the asset's, the
        /// length the fraction measures against is the view's. <paramref name="width"/> is the full width in the
        /// caller's pixels, so a bar reads the same on every card that carries one.
        /// </summary>
        public static HudBar AdoptBarFill(RectTransform track, string name, float width,
            float radius = 0f)
            => AdoptBar(Child(track, name), width, radius);

        /// <summary>
        /// A bar that is the node itself rather than a fill inside a track: a build tile's afford line has no track
        /// of its own - the key's surface is the track. <paramref name="width"/> is the full length the fraction
        /// measures against, in the caller's pixels.
        /// </summary>
        public static HudBar AdoptBar(RectTransform fill, float width, float radius = 0f)
        {
            Image image = RequireImage(fill);
            image.sprite = radius > 0f ? Shape(radius) : Pixel;
            image.type = radius > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return new HudBar(fill, image, width);
        }

        /// <summary>
        /// A button a prefab carries: a root with three pieces under it - the fill, the words and the edge - found by
        /// the names every button's pieces are given. The <see cref="HudButton"/> is added here rather than stored in
        /// the asset: what it carries is wiring - a delegate and three adopted views - and a prefab can no more hold
        /// a delegate than it can hold a generated sprite.
        /// </summary>
        public static HudButton AdoptButton(RectTransform rt, string label, float labelSize,
            Color labelColour, Action pressed)
        {
            HudButton button = rt.GetComponent<HudButton>();
            if (button == null) button = rt.gameObject.AddComponent<HudButton>();

            button.Fill = AdoptRound(Child(rt, rt.name + ".back"), Color.white, ButtonRadius);
            button.Fill.Image.raycastTarget = true;
            button.Label = AdoptText(Child(rt, rt.name + ".label"), labelSize, FontStyle.Bold,
                TextAnchor.MiddleCenter, wrap: false, tracked: true);
            button.Border = AdoptBorder(Child(rt, rt.name + ".frame"), Color.white, ButtonRadius);
            button.Label.Set(label, labelColour);
            button.Pressed = pressed;
            return button;
        }

        // ------------------------------------------------------------------ placing

        public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        public static void TopLeft(RectTransform rt, float x, float yFromTop, float width, float height)
            => Place(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, -yFromTop), new Vector2(width, height));

        /// <summary>xFromRight grows leftwards.</summary>
        public static void TopRight(RectTransform rt, float xFromRight, float yFromTop, float width,
            float height)
            => Place(rt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-xFromRight, -yFromTop), new Vector2(width, height));

        public static void BottomLeft(RectTransform rt, float x, float yFromBottom, float width,
            float height)
            => Place(rt, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(x, yFromBottom),
                new Vector2(width, height));

        public static void BottomRight(RectTransform rt, float xFromRight, float yFromBottom, float width,
            float height)
            => Place(rt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-xFromRight, yFromBottom), new Vector2(width, height));

        public static void TopCentre(RectTransform rt, float yFromTop, float width, float height)
            => Place(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -yFromTop), new Vector2(width, height));

        public static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }


        // ------------------------------------------------------------------ event system

        /// <summary>The one EventSystem the HUD's buttons answer through, built only if the scene has none. The
        /// module is the Input System's own - this project enables only the new Input System, so a legacy
        /// StandaloneInputModule would error on its first frame. It lives beside the canvas rather than under it,
        /// so a HUD rebuild never kills it.</summary>
        public static void EnsureEventSystem(Transform parent)
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;

            var go = new GameObject("FACET EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                typeof(InputSystemUIInputModule));
            go.transform.SetParent(parent, false);
        }
    }

    /// <summary>A filled rectangle that writes only what changed: the Image holds its own last colour, so a setter
    /// that would paint the same colour costs a comparison and nothing else - and a canvas that is not touched is
    /// not rebuilt.</summary>
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

    /// <summary>A label that writes only what changed. The comparison is a string equality on a short line -
    /// cheaper than the vertex rebuild a changed label pays for, and free when nothing changed.</summary>
    public sealed class HudText
    {
        public readonly TMP_Text Text;

        public HudText(TMP_Text text) => Text = text;

        public RectTransform Rect => Text.rectTransform;

        /// <summary>The width the current text asks for, measured on demand: the stockpile's icon and the toast's
        /// box are sized to the words they sit with.</summary>
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

        /// <summary>Only the colour: a line whose words hold while its news changes is one write, not a
        /// rewrite.</summary>
        public void SetColour(Color colour)
        {
            if (Text.color == colour) return;
            Text.color = colour;
        }
    }

    /// <summary>The milled edge around a surface: one sliced ring image whose colour is the whole of the state a
    /// border has to carry. Replaces four edge bars, which is why the cards and buttons can be rounded without
    /// their outlines staying square.</summary>
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

    /// <summary>A bar's fill, as a fraction of the width it was built with. The colour and the fraction are each
    /// their own write.</summary>
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

        /// <summary>The fill's rectangle, for the few callers that size a bar the run's width rather than the
        /// asset's.</summary>
        public RectTransform Rect => _fill;

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

    /// <summary>A button the EventSystem answers: it fires on the left-button press - what makes the build bar feel
    /// immediate - and reports its own hover, so the view dresses it from its update pass instead of an event
    /// storm. Visuals are the view's business.</summary>
    public sealed class HudButton : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler,
        IPointerExitHandler
    {
        public HudBox Fill;
        public HudText Label;
        public HudBorder Border;

        /// <summary>Fired on the press. The HUD hands it the same latch the keyboard uses, so a click can never
        /// say something a key press would not.</summary>
        public Action Pressed;

        /// <summary>Is a button running its own press callback right now?</summary>
        /// <remarks>
        /// A press is dispatched from inside the event system's own walk of the UI: by the time
        /// <see cref="OnPointerDown"/> returns, uGUI still has to read the widget it just dispatched to. A handler
        /// that destroys that widget - a screen that rebuilds itself on a click - makes that throw: the button keeps
        /// reading as "pressed", and the same still-held click is delivered again to whatever the new screen put
        /// under the pointer.
        ///
        /// <see cref="ShellView.RequestRebuild"/> is the one reader: it queues a screen change for the frame's own
        /// update - after the dispatch - instead of taking the card down inside the press.
        /// </remarks>
        internal static bool Dispatching { get; private set; }

        public RectTransform Root => (RectTransform)transform;

        public bool Hovered { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            // try/finally rather than a straight call: a handler that throws must not leave the flag
            // standing, or every later press would be deferred to a frame that no longer exists.
            Dispatching = true;
            try
            {
                Pressed?.Invoke();
            }
            finally
            {
                Dispatching = false;
            }
        }

        public void OnPointerEnter(PointerEventData eventData) => Hovered = true;
        public void OnPointerExit(PointerEventData eventData) => Hovered = false;

        private void OnDisable() => Hovered = false;
    }
}
