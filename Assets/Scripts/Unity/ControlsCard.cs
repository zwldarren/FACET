using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The CONTROLS card's rows, drawn as keys rather than sentences: each row is the key caps on the left and one
    /// short action on the right, so the card is read by scanning down a column of keys instead of parsing nine
    /// lines of "X · Y" prose. Shared by the run's help card (<see cref="HudView"/>) and the shell's
    /// (<see cref="ShellView"/>), which print the same list.
    /// </summary>
    public static class ControlsCard
    {
        /// <summary>One row: the caps to press and what pressing them does.</summary>
        public readonly struct Row
        {
            public readonly string[] Caps;
            public readonly string Action;

            public Row(string action, params string[] caps)
            {
                Action = action;
                Caps = caps;
            }
        }

        /// <summary>Design width of the column the caps sit in, so every action starts at the same x.</summary>
        public const float CapsColumn = 84f;

        private const float CapGap = 3f;
        private const float CapPad = 5f;
        private const float CapCharWidth = 6.2f;
        private const float CapMinWidth = 15f;
        private const float CapFontSize = 9f;

        /// <summary>The rows for a bar of <paramref name="barCount"/> keys: the build row names the digits the bar
        /// actually carries.</summary>
        public static Row[] Rows(int barCount)
        {
            return new[]
            {
                new Row("Move camera", "W", "A", "S", "D"),
                new Row("Zoom", "Scroll"),
                new Row(barCount > 0 ? "Pick a building" : "Buildings come later", BuildCaps(barCount)),
                new Row("Rotate", "Q", "E"),
                new Row("Lay belts", "Drag"),
                new Row("Delete", "R-click"),
                new Row("Next wave", "N"),
                new Row("Pause", "Space"),
                new Row("Speed", "[", "]"),
            };
        }

        private static string[] BuildCaps(int barCount)
        {
            if (barCount <= 0) return new string[0];
            if (barCount == 1) return new[] { "1" };
            if (barCount < 10) return new[] { "1-" + barCount };
            return barCount < 11 ? new[] { "1-9", "0" } : new[] { "1-9", "0", "X" };
        }

        /// <summary>The action words, row by row - what each row's label prints.</summary>
        public static string[] Actions(int barCount)
        {
            Row[] rows = Rows(barCount);
            var actions = new string[rows.Length];
            for (int i = 0; i < rows.Length; i++) actions[i] = rows[i].Action;
            return actions;
        }

        /// <summary>
        /// Draw row <paramref name="index"/>'s caps beside its label and move the label right of the caps column.
        /// The label is the asset's row node, already placed and scaled; the caps are built next to it as
        /// "caps{index}", so a rebuilt card gets them again.
        /// </summary>
        public static void DressRow(RectTransform card, HudText label, Row row, int index, Palette palette, float scale)
        {
            RectTransform rt = label.Rect;
            float column = CapsColumn * scale;

            var caps = new GameObject("caps" + index, typeof(RectTransform)).GetComponent<RectTransform>();
            caps.SetParent(card, false);
            caps.anchorMin = rt.anchorMin;
            caps.anchorMax = rt.anchorMax;
            caps.pivot = rt.pivot;
            caps.anchoredPosition = rt.anchoredPosition;
            caps.sizeDelta = new Vector2(column, rt.sizeDelta.y);

            float x = 0f;
            float height = rt.sizeDelta.y;
            for (int i = 0; i < row.Caps.Length; i++)
            {
                string key = row.Caps[i];
                float width = Mathf.Max(CapMinWidth, CapPad * 2f + key.Length * CapCharWidth) * scale;
                Cap(caps, key, x, width, height, palette, scale);
                x += width + CapGap * scale;
            }

            rt.anchoredPosition += new Vector2(column, 0f);
            rt.sizeDelta -= new Vector2(column, 0f);
        }

        private static void Cap(RectTransform parent, string key, float x, float width, float height, Palette palette,
            float scale)
        {
            var node = new GameObject("cap", typeof(RectTransform)).GetComponent<RectTransform>();
            node.SetParent(parent, false);
            node.anchorMin = node.anchorMax = new Vector2(0f, 1f);
            node.pivot = new Vector2(0f, 1f);
            node.anchoredPosition = new Vector2(x, 0f);
            node.sizeDelta = new Vector2(width, height);

            var back = new GameObject("back", typeof(RectTransform), typeof(UnityEngine.UI.Image))
                .GetComponent<RectTransform>();
            back.SetParent(node, false);
            Stretch(back);
            HudUi.AdoptRound(back, HudInk.Raised(palette), 3f).Image.raycastTarget = false;

            var frame = new GameObject("frame", typeof(RectTransform), typeof(UnityEngine.UI.Image))
                .GetComponent<RectTransform>();
            frame.SetParent(node, false);
            Stretch(frame);
            HudUi.AdoptBorder(frame, HudInk.Rule(palette), 3f, 1f);

            var text = new GameObject("label", typeof(RectTransform), typeof(TextMeshProUGUI))
                .GetComponent<RectTransform>();
            text.SetParent(node, false);
            Stretch(text);
            HudText words = HudUi.AdoptText(text, CapFontSize * scale, FontStyle.Bold, TextAnchor.MiddleCenter,
                false, false);
            words.Set(key, palette.HudText);
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
