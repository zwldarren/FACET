using System.Collections.Generic;
using Facet.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Facet.Game
{
    /// <summary>
    /// An on-screen arrow pointer that shows a coached line's mouse action (<see cref="TutorialGesture"/>) on the
    /// place the line is about, on a loop, so a player who reads none of the popup still sees what to do.
    ///
    /// <b>Click:</b> the arrow waits just below and right of the spot and nudges in toward it and back - "here".
    ///
    /// <b>Drag:</b> an orange dotted path with an arrowhead runs the whole route (ore to Core, ore to turret)
    /// and stays on screen. The arrow taps the start (dips, and a ring spreads), then holds and slides along
    /// the path leaving a solid line behind, lets go at the end, and starts again.
    ///
    /// It is the screen's own cursor - the white arrow with a dark rim - drawn once into a small texture at
    /// start-up (no art asset). Plain uGUI pieces on the coach's canvas, in screen pixels like the coach's boxes;
    /// none of them takes a click, so the pointer can sit on the tile the player has to use.
    /// </summary>
    public sealed class TutorialGestureCue
    {
        /// <summary>The pointer's height in design pixels at 720p, multiplied by the HUD scale.</summary>
        private const float PointerHeight = 54f;

        /// <summary>The arrow's width over its height, from the outline below.</summary>
        private const float Aspect = 0.62f;

        /// <summary>How far the tip waits from its spot, down and to the right, and how far a nudge brings it
        /// in toward the spot. Design pixels.</summary>
        private const float Rest = 18f;
        private const float Nudge = 9f;
        private const float NudgePeriod = 0.6f;

        // The drag's loop, in seconds: tap the start, slide, rest on the end.
        private const float DragTap = 0.25f;
        private const float DragSlid = 1.25f;
        private const float DragPeriod = 1.9f;

        private const float RingFrom = 16f;
        private const float RingTo = 58f;
        private const float LineWidth = 5f;
        private const float DotSize = 6f;
        private const float DotSpacing = 14f;
        private const float HeadLength = 12f;

        private static readonly Color Orange = new Color(1f, 0.38f, 0.05f, 1f);

        /// <summary>The classic arrow cursor, tip at the top-left (0, 0), y running down, in units of its height.</summary>
        private static readonly Vector2[] Outline =
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 0.80f),
            new Vector2(0.19f, 0.63f),
            new Vector2(0.33f, 0.95f),
            new Vector2(0.46f, 0.89f),
            new Vector2(0.32f, 0.58f),
            new Vector2(0.58f, 0.58f),
        };

        private RectTransform _root;
        private RectTransform _pathLayer;
        private RectTransform _pointer;
        private CanvasGroup _pointerAlpha;
        private HudBorder _ring;
        private readonly List<HudBox> _dots = new List<HudBox>();
        private readonly List<HudBox> _lines = new List<HudBox>();
        private int _dotsUsed;
        private int _linesUsed;

        /// <summary>Build the pieces under <paramref name="parent"/> (the coach's canvas), hidden.</summary>
        public void Build(RectTransform parent)
        {
            _root = Node(parent, "gesture");
            Stretch(_root);

            _pathLayer = Node(_root, "path");
            Stretch(_pathLayer);
            _ring = HudUi.AdoptBorder(ImageNode(_root, "ring"), Color.clear, 10f, 3f);

            _pointer = ImageNode(_root, "pointer");
            Image arrow = _pointer.GetComponent<Image>();
            arrow.sprite = PointerSprite();
            arrow.preserveAspect = true;
            arrow.color = Color.white;
            _pointerAlpha = _pointer.gameObject.AddComponent<CanvasGroup>();
            _pointerAlpha.blocksRaycasts = false;
            _pointerAlpha.interactable = false;

            _root.gameObject.SetActive(false);
        }

        public void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        /// <summary>Act out <paramref name="kind"/> this frame along <paramref name="path"/>: screen pixels,
        /// origin bottom-left. A click uses the first point; a drag runs from the first to the last through the
        /// ones between (the corners of the route). <paramref name="time"/> is seconds since this action began,
        /// so each one starts its loop fresh.</summary>
        public void Show(TutorialGesture kind, List<Vector2> path, float scale, float time)
        {
            if (_root == null || kind == TutorialGesture.None || path == null || path.Count == 0)
            {
                Hide();
                return;
            }

            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();   // over the dimming and the boxes

            float h = PointerHeight * scale;
            _pointer.sizeDelta = new Vector2(h * Aspect, h);
            _dotsUsed = 0;
            _linesUsed = 0;

            if (kind == TutorialGesture.Drag && path.Count >= 2) ShowDrag(path, scale, time);
            else ShowNudge(path[0], scale, time);

            for (int i = _dotsUsed; i < _dots.Count; i++) _dots[i].Show(false);
            for (int i = _linesUsed; i < _lines.Count; i++) _lines[i].Show(false);
        }

        // ---------------------------------------------------------------- the actions

        /// <summary>"Here": the arrow waits off the spot's bottom-right and pokes in toward it and back.</summary>
        private void ShowNudge(Vector2 at, float scale, float time)
        {
            PlacePointer(NudgedTip(at, scale, time), 1f, 1f);
            _ring.Set(Color.clear);
        }

        /// <summary>Tap the start, hold and slide along the route, let go at the end, again. No nudging: the
        /// moving arrow and the dotted path already catch the eye.</summary>
        private void ShowDrag(List<Vector2> path, float scale, float time)
        {
            float p = Mathf.Repeat(time, DragPeriod);
            float total = Length(path);

            // The whole route, always: dots along it and an arrowhead at its end.
            DrawDots(path, total, scale);

            Ripple(path[0], scale, p / 0.7f);

            if (p < DragTap)
            {
                PlacePointer(path[0] + Corner(scale) * 0.2f, 0.86f, 1f);   // pressed on the start
                return;
            }

            if (p < DragSlid)
            {
                float along = Smooth((p - DragTap) / (DragSlid - DragTap)) * total;
                Vector2 at = PointAlong(path, along);
                DrawTrail(path, along, scale, 1f);
                PlacePointer(at + Corner(scale) * 0.2f, 0.86f, 1f);   // still held
                return;
            }

            // Let go on the end; fade the last moment so the loop restarts cleanly.
            float fade = 1f - Mathf.Clamp01((p - (DragPeriod - 0.3f)) / 0.3f);
            DrawTrail(path, total, scale, fade);
            PlacePointer(path[path.Count - 1] + Corner(scale), 1f, fade);
        }

        /// <summary>The tip's resting place off a spot (down and right), moved in toward the spot by a nudge.</summary>
        private static Vector2 NudgedTip(Vector2 spot, float scale, float time)
        {
            float k = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * time / NudgePeriod);   // 0 -> 1 -> 0
            float off = (Rest - Nudge * k) * scale;
            return spot + new Vector2(off, -off);
        }

        private static Vector2 Corner(float scale) => new Vector2(Rest, -Rest) * scale;

        // ---------------------------------------------------------------- the route

        private void DrawDots(List<Vector2> path, float total, float scale)
        {
            float spacing = DotSpacing * scale;
            float head = HeadLength * scale;
            float size = DotSize * scale;
            Color dot = new Color(Orange.r, Orange.g, Orange.b, 0.9f);

            for (float d = spacing; d < total - head; d += spacing)
            {
                Vector2 at = PointAlong(path, d);
                RectTransform rt = Dot(dot);
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = at;
                rt.sizeDelta = new Vector2(size, size);
            }

            // The arrowhead: two short strokes meeting at the route's end, along its last leg.
            Vector2 end = path[path.Count - 1];
            Vector2 back = (path[path.Count - 2] - end);
            if (back.magnitude < 0.01f) return;
            back /= back.magnitude;
            float angle = Mathf.Atan2(back.y, back.x) * Mathf.Rad2Deg;
            Stroke(end, angle + 35f, head, LineWidth * scale, dot);
            Stroke(end, angle - 35f, head, LineWidth * scale, dot);
        }

        /// <summary>The solid line behind a held drag, from the start to <paramref name="upTo"/> along the route.</summary>
        private void DrawTrail(List<Vector2> path, float upTo, float scale, float alpha)
        {
            Color ink = new Color(Orange.r, Orange.g, Orange.b, 0.85f * alpha);
            float left = upTo;
            for (int i = 0; i + 1 < path.Count && left > 0f; i++)
            {
                Vector2 a = path[i], b = path[i + 1];
                float leg = Vector2.Distance(a, b);
                if (leg <= 0f) continue;
                float run = Mathf.Min(leg, left);
                Vector2 dir = (b - a) / leg;
                Stroke(a, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, run, LineWidth * scale, ink);
                left -= run;
            }
        }

        /// <summary>A straight stroke from <paramref name="from"/>, <paramref name="length"/> long, at
        /// <paramref name="degrees"/>.</summary>
        private void Stroke(Vector2 from, float degrees, float length, float width, Color colour)
        {
            HudBox line = Line(colour);
            RectTransform rt = line.Rect;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = from;
            rt.sizeDelta = new Vector2(length, width);
            rt.localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        private static float Length(List<Vector2> path)
        {
            float total = 0f;
            for (int i = 0; i + 1 < path.Count; i++) total += Vector2.Distance(path[i], path[i + 1]);
            return total;
        }

        private static Vector2 PointAlong(List<Vector2> path, float distance)
        {
            for (int i = 0; i + 1 < path.Count; i++)
            {
                float leg = Vector2.Distance(path[i], path[i + 1]);
                if (distance <= leg && leg > 0f) return Vector2.Lerp(path[i], path[i + 1], distance / leg);
                distance -= leg;
            }
            return path[path.Count - 1];
        }

        // ---------------------------------------------------------------- pieces

        /// <summary>A ring growing out of the spot, fading as it grows.</summary>
        private void Ripple(Vector2 at, float scale, float t)
        {
            if (t < 0f || t > 1f)
            {
                _ring.Set(Color.clear);
                return;
            }

            float size = Mathf.Lerp(RingFrom, RingTo, t) * scale;
            RectTransform rt = _ring.Rect;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = at;
            rt.sizeDelta = new Vector2(size, size);
            _ring.Set(new Color(Orange.r, Orange.g, Orange.b, 0.95f * (1f - t)));
        }

        /// <summary>The arrow's tip at <paramref name="tip"/>; <paramref name="press"/> below 1 shrinks it toward
        /// the tip, which reads as the pointer pushing down.</summary>
        private void PlacePointer(Vector2 tip, float press, float alpha)
        {
            _pointer.anchorMin = _pointer.anchorMax = Vector2.zero;
            _pointer.pivot = new Vector2(0f, 1f);   // the tip
            _pointer.anchoredPosition = tip;
            _pointer.localScale = new Vector3(press, press, 1f);
            _pointerAlpha.alpha = alpha;
        }

        private RectTransform Dot(Color colour)
        {
            while (_dots.Count <= _dotsUsed)
                _dots.Add(HudUi.AdoptRound(ImageNode(_pathLayer, "dot" + _dots.Count), Color.clear, 3f));

            HudBox dot = _dots[_dotsUsed++];
            dot.Show(true);
            dot.Set(colour);
            return dot.Rect;
        }

        private HudBox Line(Color colour)
        {
            while (_lines.Count <= _linesUsed)
                _lines.Add(HudUi.AdoptBox(ImageNode(_pathLayer, "line" + _lines.Count), Color.clear));

            HudBox line = _lines[_linesUsed++];
            line.Show(true);
            line.Set(colour);
            return line;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // ---------------------------------------------------------------- the arrow, drawn once

        private static Sprite _sprite;

        /// <summary>The arrow cursor as a sprite: white inside, a dark rim, transparent outside, four samples a
        /// pixel so its slanted edges are smooth. Made the first time it is asked for and kept.</summary>
        private static Sprite PointerSprite()
        {
            if (_sprite != null) return _sprite;

            const int height = 128;
            int width = Mathf.CeilToInt(height * Aspect);
            const float rim = 0.035f;   // the dark edge, as a share of the height

            var pixels = new Color32[width * height];
            var dark = new Color32(30, 24, 20, 255);
            var light = new Color32(255, 255, 255, 255);

            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    int inside = 0, core = 0;
                    for (int s = 0; s < 4; s++)
                    {
                        // Texture rows run up; the outline's y runs down from the tip.
                        float x = (px + (s % 2 == 0 ? 0.25f : 0.75f)) / height;
                        float y = 1f - (py + (s < 2 ? 0.25f : 0.75f)) / height;
                        if (!Inside(x, y)) continue;
                        inside++;
                        if (EdgeDistance(x, y) > rim) core++;
                    }

                    if (inside == 0)
                    {
                        pixels[py * width + px] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    Color32 ink = core * 2 >= inside ? light : dark;
                    ink.a = (byte)(255 * inside / 4);
                    pixels[py * width + px] = ink;
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = UnityEngine.FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "TutorialPointer",
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            _sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0f, 1f), 100f);
            _sprite.name = "TutorialPointer";
            return _sprite;
        }

        /// <summary>Is (x, y) inside the arrow? Even-odd rule over the outline.</summary>
        private static bool Inside(float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = Outline.Length - 1; i < Outline.Length; j = i++)
            {
                Vector2 a = Outline[i], b = Outline[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>How far (x, y) is from the arrow's nearest edge.</summary>
        private static float EdgeDistance(float x, float y)
        {
            float best = float.MaxValue;
            var p = new Vector2(x, y);
            for (int i = 0, j = Outline.Length - 1; i < Outline.Length; j = i++)
            {
                Vector2 a = Outline[j], b = Outline[i];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, Vector2.Dot(ab, ab)));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        // ---------------------------------------------------------------- nodes

        private static RectTransform ImageNode(Transform parent, string name)
        {
            RectTransform rt = Node(parent, name);
            Image image = rt.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return rt;
        }

        private static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
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
