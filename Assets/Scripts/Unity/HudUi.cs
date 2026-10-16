using System;
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
    /// label is a Text, a button is an Image that answers the pointer. What the builders add over raw
    /// uGUI is the discipline the IMGUI view kept: every widget remembers what it is showing through
    /// the graphic it drives, so an update that changes nothing writes nothing and the canvas never
    /// rebuilds for a frame the HUD stood still in.
    ///
    /// The geometry helpers speak in screen pixels measured from one named corner, the language
    /// <see cref="HudLayout"/> lays the panels out in - the view converts, this class only places.
    /// </summary>
    public static class HudUi
    {
        /// <summary>The one font every label uses: the engine's own, so the HUD ships no asset, and one
        /// place to swap in a licensed face later.</summary>
        private static Font _font;

        /// <summary>The one white sprite every panel and border tints: a texture per colour would be a
        /// hundred textures differing only by a multiply.</summary>
        private static Sprite _pixel;

        public static Font BuiltinFont
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

        /// <summary>Free the shared sprite and texture, called on the HUD's teardown so an Editor's
        /// scene reloads do not leak a texture a play; whatever builds again recreates both from nothing.</summary>
        public static void ReleaseShared()
        {
            if (_pixel != null)
            {
                UnityEngine.Object.Destroy(_pixel.texture);
                UnityEngine.Object.Destroy(_pixel);
                _pixel = null;
            }
            // The builtin font is the engine's, not ours to destroy.
            _font = null;
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

        /// <summary>A label. The colour arrives per update, the way <see cref="HudText.Set(string, Color)"/>
        /// takes it; the style arguments are the ones the IMGUI view named per style.</summary>
        public static HudText Text(RectTransform parent, string name, int fontSize, FontStyle style,
            TextAnchor anchor, bool wrap)
        {
            RectTransform rt = Node(parent, name);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = BuiltinFont;
            text.fontSize = Mathf.Max(8, fontSize);
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            return new HudText(text);
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

        /// <summary>A border drawn as four thin edges around whatever rectangle it is given, so the
        /// panels do not bleed into the map. The frame stretches over its parent, so the edges read the
        /// parent's size whatever it is, and a parent resized after the fact keeps its border.</summary>
        public static HudFrame Frame(RectTransform parent, string name, Color colour, float thickness)
        {
            RectTransform root = Node(parent, name);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            return new HudFrame(root,
                Edge(root, name + ".top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f)),
                Edge(root, name + ".bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f)),
                Edge(root, name + ".left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f)),
                Edge(root, name + ".right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f)),
                colour, thickness);
        }

        private static Image Edge(RectTransform root, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot)
        {
            RectTransform rt = Node(root, name);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;

            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = Pixel;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A button: a filled, bordered rectangle that answers the pointer. It fires on the
        /// press - what made the IMGUI bar feel immediate - through uGUI's EventSystem, and reports its
        /// own hover so the view can dress it without an event.</summary>
        public static HudButton Button(RectTransform parent, string name, float width, float height,
            string label, int labelSize, Color labelColour, Action pressed)
        {
            RectTransform root = Node(parent, name);
            var button = root.gameObject.AddComponent<HudButton>();
            button.Fill = Box(root, name + ".back", Color.white);
            StretchFull(button.Fill.Rect);
            button.Fill.Image.raycastTarget = true;
            button.Label = Text(root, name + ".label", labelSize, FontStyle.Bold,
                TextAnchor.MiddleCenter, wrap: false);
            // Inset by 4 on every side, so the label's middle is the button's middle. Not Stretch: its
            // fourth number is a height, and a 4-tall strip hung off the top edge centred every label there.
            StretchFull(button.Label.Rect);
            button.Label.Rect.offsetMin = new Vector2(4f, 4f);
            button.Label.Rect.offsetMax = new Vector2(-4f, -4f);
            button.Border = Frame(root, name + ".frame", Color.white, 1f);
            button.Label.Set(label, labelColour);
            button.Pressed = pressed;
            return button;
        }

        /// <summary>A bar's fill: a rectangle whose width is the fraction, read at a glance where a
        /// number has to be read twice. Hidden at zero, because an empty bar says the same thing.</summary>
        public static HudBar BarFill(RectTransform parent, string name, float left, float bottom,
            float width, float height)
        {
            RectTransform rt = Node(parent, name);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(left, bottom);
            rt.sizeDelta = new Vector2(width, height);

            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = Pixel;
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
    /// line - cheaper than the vertex rebuild a changed Text pays for, and free when nothing changed.</summary>
    public sealed class HudText
    {
        public readonly Text Text;

        public HudText(Text text) => Text = text;

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

    /// <summary>The thin border around a panel or a tile, as four edge images the thickness and colour
    /// of the moment - a selected tile's border is wider than a hovered one's, and a bar being chewed on
    /// pulses in the warning colour. Both changes are writes only when they happen.</summary>
    public sealed class HudFrame
    {
        private readonly RectTransform _root;
        private readonly Image _top;
        private readonly Image _bottom;
        private readonly Image _left;
        private readonly Image _right;
        private float _thickness = -1f;

        public HudFrame(RectTransform root, Image top, Image bottom, Image left, Image right, Color colour,
            float thickness)
        {
            _root = root;
            _top = top; _bottom = bottom; _left = left; _right = right;
            Set(colour, thickness);
        }

        public RectTransform Rect => _root;

        public void Show(bool on) => _root.gameObject.SetActive(on);

        public void Set(Color colour)
        {
            if (_top.color == colour) return;
            _top.color = colour;
            _bottom.color = colour;
            _left.color = colour;
            _right.color = colour;
        }

        public void Set(Color colour, float thickness)
        {
            Set(colour);
            if (Mathf.Approximately(_thickness, thickness)) return;
            _thickness = thickness;

            _top.rectTransform.sizeDelta = new Vector2(0f, thickness);
            _bottom.rectTransform.sizeDelta = new Vector2(0f, thickness);
            _left.rectTransform.sizeDelta = new Vector2(thickness, 0f);
            _right.rectTransform.sizeDelta = new Vector2(thickness, 0f);
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
        public HudFrame Border;

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
