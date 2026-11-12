using System.Collections.Generic;
using Facet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Facet.Game
{
    /// <summary>
    /// A looping picture of one machine at work, for the lesson popup: a strip of map with belts in and out, the
    /// machine in the middle, and items riding through it - a whole circle going in and two halves coming out of a
    /// cutter, a top half turning into a right half in a rotator. It plays like a short GIF, so the player sees
    /// what the machine does in two seconds instead of being walked through building one.
    ///
    /// Everything is drawn from the palette and the HUD's own item pictures, so it matches the map the player is
    /// looking at. It runs on unscaled time and touches nothing in the world.
    /// </summary>
    public sealed class MachineDemo
    {
        /// <summary>One item's ride: where it enters (lane), what it is before and after, and where it leaves.
        /// Lanes are -1 (upper), 0 (middle) and 1 (lower).</summary>
        private readonly struct Ride
        {
            public readonly Shape In;
            public readonly Shape Out;
            public readonly int InLane;
            public readonly int OutLane;

            /// <summary>When in the loop this ride starts, as a fraction of the loop.</summary>
            public readonly float Start;

            public Ride(Shape inShape, int inLane, Shape outShape, int outLane, float start)
            {
                In = inShape;
                InLane = inLane;
                Out = outShape;
                OutLane = outLane;
                Start = start;
            }
        }

        private static readonly Shape FullCircle = Shape.Full(Quad.Circle);
        private static readonly Shape TopCircle = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty);
        private static readonly Shape BottomCircle = Shape.Of(Quad.Empty, Quad.Empty, Quad.Circle, Quad.Circle);
        private static readonly Shape RightCircle = Shape.Of(Quad.Empty, Quad.Circle, Quad.Circle, Quad.Empty);
        private static readonly Shape BottomSquare = Shape.Of(Quad.Empty, Quad.Empty, Quad.Square, Quad.Square);
        private static readonly Shape CircleOverSquare = Shape.Of(Quad.Circle, Quad.Circle, Quad.Square, Quad.Square);

        /// <summary>Seconds one loop takes.</summary>
        private const float Loop = 2.8f;

        /// <summary>Design height of the strip at 720p.</summary>
        public const float Height = 96f;

        private const float LaneGap = 26f;
        private const float ItemSize = 22f;
        private const float MachineWidth = 64f;

        private readonly Palette _palette;
        private readonly HudIcons _icons;

        private RectTransform _root;
        private HudBox _ground;
        private readonly HudBox[] _belts = new HudBox[6];
        private HudBox _machine;
        private HudText _machineName;
        private HudText _upperEnd;
        private HudText _lowerEnd;
        private readonly List<RawImage> _items = new List<RawImage>();

        private BuildKind _kind;
        private Ride[] _rides = new Ride[0];

        public MachineDemo(Palette palette, HudIcons icons)
        {
            _palette = palette;
            _icons = icons;
        }

        /// <summary>Does this building have a demo?</summary>
        public static bool Covers(BuildKind kind)
            => kind == BuildKind.Splitter || kind == BuildKind.Cutter || kind == BuildKind.Rotator ||
               kind == BuildKind.Stacker;

        public RectTransform Root => _root;

        public void Build(RectTransform parent)
        {
            _root = Node(parent, "demo");
            _ground = HudUi.AdoptRound(ImageNode(_root, "ground"), _palette.Platform, 8f);
            Stretch(_ground.Image.rectTransform);

            for (int i = 0; i < _belts.Length; i++)
                _belts[i] = HudUi.AdoptRound(ImageNode(_root, "belt" + i), _palette.Belt, 3f);

            _machine = HudUi.AdoptRound(ImageNode(_root, "machine"), _palette.MachineBody, 6f);
            _machineName = Label(_machine.Image.rectTransform, "name");
            _upperEnd = Label(_root, "upperEnd");
            _lowerEnd = Label(_root, "lowerEnd");

            _root.gameObject.SetActive(false);
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        /// <summary>Lay the strip into <paramref name="rect"/> (top-left, in the popup's pixels) and play the frame
        /// for now.</summary>
        public void Show(BuildKind kind, float x, float yFromTop, float width, float scale, string machineName)
        {
            if (_root == null) return;

            if (kind != _kind || _rides.Length == 0)
            {
                _kind = kind;
                _rides = RidesFor(kind);
            }

            _root.gameObject.SetActive(true);
            float height = Height * scale;
            Place(_root, x, yFromTop, width, height);

            float mid = height * 0.5f;
            float lane = LaneGap * scale;
            float machineW = MachineWidth * scale;
            float machineH = (_kind == BuildKind.Rotator ? 1.4f : 2.6f) * lane;
            float machineX = (width - machineW) * 0.5f;
            float beltH = 8f * scale;

            // The belts: in on the left, out on the right, on the lanes this machine uses.
            int used = 0;
            foreach (int inLane in Lanes(true)) PlaceBelt(used++, 6f * scale, mid + inLane * lane, machineX - 6f * scale, beltH);
            foreach (int outLane in Lanes(false))
                PlaceBelt(used++, machineX + machineW, mid + outLane * lane, width - machineX - machineW - 6f * scale, beltH);
            for (int i = used; i < _belts.Length; i++) _belts[i].Image.gameObject.SetActive(false);

            Place(_machine.Image.rectTransform, machineX, mid - machineH * 0.5f, machineW, machineH);
            _machineName.Text.fontSize = 11f * scale;
            _machineName.Set(machineName.ToUpperInvariant(), _palette.HudText);
            Stretch(_machineName.Rect);

            // Where the cutter's two halves end up, said at the ends of their belts.
            bool ends = _kind == BuildKind.Cutter;
            _upperEnd.Show(ends);
            _lowerEnd.Show(ends);
            if (ends)
            {
                _upperEnd.Text.fontSize = _lowerEnd.Text.fontSize = 10f * scale;
                _upperEnd.Set("TURRET", _palette.Outline);
                _lowerEnd.Set("SINK", _palette.Outline);
                Place(_upperEnd.Rect, width - 64f * scale, mid - lane - 22f * scale, 58f * scale, 14f * scale);
                Place(_lowerEnd.Rect, width - 64f * scale, mid + lane + 8f * scale, 58f * scale, 14f * scale);
            }

            PlayRides(width, mid, lane, machineX, machineW, scale);
        }

        private void PlaceBelt(int index, float x, float centreY, float width, float height)
        {
            RectTransform rt = _belts[index].Image.rectTransform;
            _belts[index].Image.gameObject.SetActive(true);
            Place(rt, x, centreY - height * 0.5f, Mathf.Max(0f, width), height);
        }

        /// <summary>Each item enters, is hidden inside the machine for a beat, then leaves as what the machine
        /// made of it.</summary>
        private void PlayRides(float width, float mid, float lane, float machineX, float machineW, float scale)
        {
            float t = Time.unscaledTime / Loop;
            float size = ItemSize * scale;
            int shown = 0;

            for (int r = 0; r < _rides.Length; r++)
            {
                Ride ride = _rides[r];
                float p = Mathf.Repeat(t - ride.Start, 1f);

                // 0-0.42 riding in, 0.42-0.58 inside, 0.58-1 riding out.
                bool inside = p > 0.42f && p < 0.58f;
                if (inside) continue;

                bool coming = p <= 0.42f;
                float k = coming ? p / 0.42f : (p - 0.58f) / 0.42f;
                float fromX = coming ? 0f : machineX + machineW;
                float toX = coming ? machineX - size : width - size;
                float cx = Mathf.Lerp(fromX, toX, k);
                float cy = mid + (coming ? ride.InLane : ride.OutLane) * lane - size * 0.5f;

                Shape shape = coming ? ride.In : ride.Out;
                if (shape.IsEmpty) continue;

                RawImage item = ItemAt(shown++);
                HudIcons.Icon icon = _icons.Whole(shape);
                item.texture = icon.Texture;
                item.uvRect = icon.Uv;
                item.gameObject.SetActive(icon.Valid);
                Place(item.rectTransform, cx, cy, size, size);
            }

            for (int i = shown; i < _items.Count; i++) _items[i].gameObject.SetActive(false);
        }

        private IEnumerable<int> Lanes(bool incoming)
        {
            var lanes = new HashSet<int>();
            for (int i = 0; i < _rides.Length; i++) lanes.Add(incoming ? _rides[i].InLane : _rides[i].OutLane);
            return lanes;
        }

        private static Ride[] RidesFor(BuildKind kind)
        {
            switch (kind)
            {
                // One line in, every other shape out of each side.
                case BuildKind.Splitter:
                    return new[]
                    {
                        new Ride(FullCircle, 0, FullCircle, -1, 0f),
                        new Ride(FullCircle, 0, FullCircle, 1, 0.5f),
                    };

                // A whole circle in, its two halves out at once: the top to a turret, the bottom to a sink.
                case BuildKind.Cutter:
                    return new[]
                    {
                        new Ride(FullCircle, 0, TopCircle, -1, 0f),
                        new Ride(Shape.None, 0, BottomCircle, 1, 0f),
                    };

                // A top half in, a right half out: a quarter turn clockwise.
                case BuildKind.Rotator:
                    return new[] { new Ride(TopCircle, 0, RightCircle, 0, 0f) };

                // A top circle half and a bottom square half in, one shape out.
                case BuildKind.Stacker:
                    return new[]
                    {
                        new Ride(TopCircle, -1, CircleOverSquare, 0, 0f),
                        new Ride(BottomSquare, 1, Shape.None, 0, 0f),
                    };

                default:
                    return new Ride[0];
            }
        }

        private RawImage ItemAt(int index)
        {
            while (_items.Count <= index)
            {
                RectTransform rt = Node(_root, "item" + _items.Count);
                RawImage image = rt.gameObject.AddComponent<RawImage>();
                image.raycastTarget = false;
                _items.Add(image);
            }

            return _items[index];
        }

        // ---------------------------------------------------------------- building helpers

        private static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static RectTransform ImageNode(Transform parent, string name)
        {
            RectTransform rt = Node(parent, name);
            Image image = rt.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return rt;
        }

        private static HudText Label(Transform parent, string name)
        {
            RectTransform rt = Node(parent, name);
            rt.gameObject.AddComponent<TextMeshProUGUI>();
            return HudUi.AdoptText(rt, 11f, FontStyle.Bold, TextAnchor.MiddleCenter, false, true);
        }

        private static void Place(RectTransform rt, float x, float yFromTop, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -yFromTop);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
