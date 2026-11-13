using System.Collections.Generic;
using Facet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Facet.Game
{
    /// <summary>
    /// A looping picture of one building at work: a strip of map with belts in and out, the building in the middle,
    /// and items riding through it - a whole circle going in and two halves coming out of a cutter, a top half
    /// turning into a right half in a rotator, a round leaving a turret for the enemy it is fired at. It plays like a
    /// short GIF, so the player sees what a building does in two seconds instead of reading a paragraph about it.
    ///
    /// Two places show it: the lesson popup (<see cref="TutorialCoachView"/>) and the build card the HUD shows when a
    /// key on the bar is hovered or selected (<see cref="HudView"/>). Everything is drawn from the palette and the
    /// HUD's own item pictures, so it matches the map. It runs on unscaled time and touches nothing in the world.
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
        private static readonly Shape FullSquare = Shape.Full(Quad.Square);
        private static readonly Shape TopCircle = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty);
        private static readonly Shape BottomCircle = Shape.Of(Quad.Empty, Quad.Empty, Quad.Circle, Quad.Circle);
        private static readonly Shape RightCircle = Shape.Of(Quad.Empty, Quad.Circle, Quad.Circle, Quad.Empty);
        private static readonly Shape BottomSquare = Shape.Of(Quad.Empty, Quad.Empty, Quad.Square, Quad.Square);
        private static readonly Shape CircleOverSquare = Shape.Of(Quad.Circle, Quad.Circle, Quad.Square, Quad.Square);

        /// <summary>Seconds one loop takes.</summary>
        private const float Loop = 2.8f;

        /// <summary>Design height of the strip at 720p, in the lesson popup.</summary>
        public const float Height = 96f;

        private const float LaneGap = 26f;
        private const float ItemSize = 22f;
        private const float MachineWidth = 64f;

        private readonly Palette _palette;
        private readonly HudIcons _icons;

        private RectTransform _root;
        private HudBox _ground;
        private readonly HudBox[] _belts = new HudBox[6];
        private HudBox _crossing;
        private HudBox _machine;
        private HudBox _enemy;
        private HudText _machineName;
        private HudText _upperEnd;
        private HudText _lowerEnd;
        private readonly List<RawImage> _items = new List<RawImage>();

        private BuildKind _kind;
        private bool _ridesBuilt;
        private Ride[] _rides = new Ride[0];

        public MachineDemo(Palette palette, HudIcons icons)
        {
            _palette = palette;
            _icons = icons;
        }

        /// <summary>Does this building have a demo? Every building the bar offers does; the retired ones do not.</summary>
        public static bool Covers(BuildKind kind)
        {
            switch (kind)
            {
                case BuildKind.Belt:
                case BuildKind.Cutter:
                case BuildKind.Rotator:
                case BuildKind.Stacker:
                case BuildKind.Pipe:
                case BuildKind.Splitter:
                case BuildKind.Sorter:
                case BuildKind.Turret:
                case BuildKind.Sink:
                    return true;
                default:
                    return false;
            }
        }

        public RectTransform Root => _root;

        public bool Shown => _root != null && _root.gameObject.activeSelf;

        public void Build(RectTransform parent)
        {
            _root = Node(parent, "demo");
            _ground = HudUi.AdoptRound(ImageNode(_root, "ground"), _palette.Platform, 8f);
            Stretch(_ground.Image.rectTransform);

            for (int i = 0; i < _belts.Length; i++)
                _belts[i] = HudUi.AdoptRound(ImageNode(_root, "belt" + i), _palette.Belt, 3f);

            // The line a pipe carries its own under: a belt running across the strip, under the pipe.
            _crossing = HudUi.AdoptRound(ImageNode(_root, "crossing"), _palette.BeltRail, 3f);

            _machine = HudUi.AdoptRound(ImageNode(_root, "machine"), _palette.MachineBody, 6f);
            _machineName = Label(_machine.Image.rectTransform, "name");

            // What a turret's rounds are fired at.
            _enemy = HudUi.AdoptRound(ImageNode(_root, "enemy"), _palette.RunnerBody, 6f);

            _upperEnd = Label(_root, "upperEnd");
            _lowerEnd = Label(_root, "lowerEnd");

            _root.gameObject.SetActive(false);
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        /// <summary>The popup's strip, at its design height.</summary>
        public void Show(BuildKind kind, float x, float yFromTop, float width, float scale, string machineName)
            => Show(kind, x, yFromTop, width, Height * scale, scale, machineName);

        /// <summary>Lay the strip into a box (top-left, in the parent's pixels) and play the frame for now.</summary>
        public void Show(BuildKind kind, float x, float yFromTop, float width, float height, float scale,
            string machineName)
        {
            if (_root == null) return;

            if (kind != _kind || !_ridesBuilt)
            {
                _kind = kind;
                _rides = RidesFor(kind);
                _ridesBuilt = true;
            }

            _root.gameObject.SetActive(true);
            Place(_root, x, yFromTop, width, height);

            // Everything is sized off the box the strip got, so it reads at the hover card's height as well as
            // the popup's.
            float fit = Mathf.Min(1f, height / (Height * scale));
            float mid = height * 0.5f;
            float lane = LaneGap * scale * fit;
            float size = ItemSize * scale * Mathf.Max(0.8f, fit);
            float machineW = MachineWidth * scale * Mathf.Max(0.85f, fit);
            float machineH = (_kind == BuildKind.Rotator || _kind == BuildKind.Pipe || _kind == BuildKind.Turret ||
                              _kind == BuildKind.Sink ? 1.4f : 2.6f) * lane;
            machineH = Mathf.Max(machineH, size + 8f * scale * fit);
            float machineX = (width - machineW) * 0.5f;
            float beltH = 8f * scale * fit;
            bool belt = _kind == BuildKind.Belt;

            // The belts: in on the left, out on the right, on the lanes this building uses. A belt is the one
            // building that is all belt, so it runs the strip's whole width with no machine in it.
            int used = 0;
            if (belt)
            {
                PlaceBelt(used++, 6f * scale, mid, width - 12f * scale, beltH);
            }
            else
            {
                foreach (int inLane in Lanes(true))
                    PlaceBelt(used++, 6f * scale, mid + inLane * lane, machineX - 6f * scale, beltH);
                if (_kind != BuildKind.Sink)
                    foreach (int outLane in Lanes(false))
                        PlaceBelt(used++, machineX + machineW, mid + outLane * lane,
                            width - machineX - machineW - 6f * scale, beltH);
            }
            for (int i = used; i < _belts.Length; i++) _belts[i].Image.gameObject.SetActive(false);

            bool crossing = _kind == BuildKind.Pipe;
            _crossing.Image.gameObject.SetActive(crossing);
            if (crossing)
                Place(_crossing.Image.rectTransform, (width - beltH) * 0.5f, 4f * scale, beltH, height - 8f * scale);

            _machine.Image.gameObject.SetActive(!belt);
            if (!belt)
            {
                Place(_machine.Image.rectTransform, machineX, mid - machineH * 0.5f, machineW, machineH);
                _machineName.Text.fontSize = 11f * scale * Mathf.Max(0.85f, fit);
                _machineName.Set(machineName.ToUpperInvariant(), _palette.HudText);
                Stretch(_machineName.Rect);
            }

            bool enemy = _kind == BuildKind.Turret;
            _enemy.Image.gameObject.SetActive(enemy);
            if (enemy)
            {
                float e = size * 1.1f;
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 9f);
                Place(_enemy.Image.rectTransform, width - e - 10f * scale, mid - e * 0.5f, e, e);
                Color body = _palette.RunnerBody;
                _enemy.Set(new Color(body.r * pulse, body.g * pulse, body.b * pulse, 1f));
            }

            // Where a fork's pieces end up, said at the ends of their belts.
            string upper = null, lower = null;
            if (_kind == BuildKind.Cutter) { upper = "TURRET"; lower = "SINK"; }
            else if (_kind == BuildKind.Sorter) { upper = "CIRCLES"; lower = "THE REST"; }
            Ends(upper, lower, width, mid, lane, scale, fit);

            PlayRides(width, mid, lane, machineX, machineW, size, belt, enemy ? size * 1.1f + 10f * scale : 0f);
        }

        private void Ends(string upper, string lower, float width, float mid, float lane, float scale, float fit)
        {
            bool show = upper != null;
            _upperEnd.Show(show);
            _lowerEnd.Show(show);
            if (!show) return;

            float font = 10f * scale * Mathf.Max(0.85f, fit);
            float labelW = 64f * scale;
            float labelH = font * 1.4f;
            _upperEnd.Text.fontSize = _lowerEnd.Text.fontSize = font;
            _upperEnd.Set(upper, _palette.Outline);
            _lowerEnd.Set(lower, _palette.Outline);

            // The sorter's matching shapes leave on the upper lane, the rest go straight on.
            float lowerLane = _kind == BuildKind.Sorter ? 0f : lane;
            Place(_upperEnd.Rect, width - labelW - 4f * scale, mid - lane - labelH - 2f * scale, labelW, labelH);
            Place(_lowerEnd.Rect, width - labelW - 4f * scale, mid + lowerLane + 6f * scale, labelW, labelH);
        }

        private void PlaceBelt(int index, float x, float centreY, float width, float height)
        {
            RectTransform rt = _belts[index].Image.rectTransform;
            _belts[index].Image.gameObject.SetActive(true);
            Place(rt, x, centreY - height * 0.5f, Mathf.Max(0f, width), height);
        }

        /// <summary>Each item enters, is hidden inside the building for a beat, then leaves as what the building
        /// made of it. On a belt there is nothing to hide inside, so it rides straight across.</summary>
        private void PlayRides(float width, float mid, float lane, float machineX, float machineW, float size,
            bool belt, float stopShort)
        {
            float t = Time.unscaledTime / Loop;
            int shown = 0;

            for (int r = 0; r < _rides.Length; r++)
            {
                Ride ride = _rides[r];
                float p = Mathf.Repeat(t - ride.Start, 1f);

                float cx, cy;
                Shape shape;
                if (belt)
                {
                    shape = ride.In;
                    cx = Mathf.Lerp(0f, width - size, p);
                    cy = mid - size * 0.5f;
                }
                else
                {
                    // 0-0.42 riding in, 0.42-0.58 inside, 0.58-1 riding out.
                    if (p > 0.42f && p < 0.58f) continue;

                    bool coming = p <= 0.42f;
                    float k = coming ? p / 0.42f : (p - 0.58f) / 0.42f;
                    float fromX = coming ? 0f : machineX + machineW;
                    float toX = coming ? machineX - size : width - size - stopShort;
                    cx = Mathf.Lerp(fromX, toX, k);
                    cy = mid + (coming ? ride.InLane : ride.OutLane) * lane - size * 0.5f;
                    shape = coming ? ride.In : ride.Out;
                }

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
            for (int i = 0; i < _rides.Length; i++)
            {
                Ride ride = _rides[i];
                if (incoming && ride.In.IsEmpty) continue;
                if (!incoming && ride.Out.IsEmpty) continue;
                lanes.Add(incoming ? ride.InLane : ride.OutLane);
            }
            return lanes;
        }

        private static Ride[] RidesFor(BuildKind kind)
        {
            switch (kind)
            {
                // Shapes ride along, one after another.
                case BuildKind.Belt:
                    return new[]
                    {
                        new Ride(FullCircle, 0, FullCircle, 0, 0f),
                        new Ride(FullSquare, 0, FullSquare, 0, 0.5f),
                    };

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

                // The line goes under the crossing belt and comes up the other side unchanged.
                case BuildKind.Pipe:
                    return new[]
                    {
                        new Ride(FullCircle, 0, FullCircle, 0, 0f),
                        new Ride(FullSquare, 0, FullSquare, 0, 0.5f),
                    };

                // Circles leave by the side, everything else goes straight on.
                case BuildKind.Sorter:
                    return new[]
                    {
                        new Ride(FullCircle, 0, FullCircle, -1, 0f),
                        new Ride(FullSquare, 0, FullSquare, 0, 0.5f),
                    };

                // A round goes in and is fired at the enemy.
                case BuildKind.Turret:
                    return new[] { new Ride(TopCircle, 0, TopCircle, 0, 0f) };

                // Whatever goes in is gone.
                case BuildKind.Sink:
                    return new[]
                    {
                        new Ride(BottomCircle, 0, Shape.None, 0, 0f),
                        new Ride(FullSquare, 0, Shape.None, 0, 0.5f),
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
