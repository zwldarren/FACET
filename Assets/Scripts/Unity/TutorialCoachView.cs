using System;
using System.Collections.Generic;
using Facet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Facet.Game
{
    /// <summary>
    /// The coach: how a coached lesson (<see cref="TutorialScript.Coached"/>, the tutorial levels) is taught. One
    /// big popup set down beside what the line is about says it, the rest of the screen is dimmed so the boxed
    /// places are the only bright ones, and it says the current line - a heading, the sentence, and a button when the
    /// line is something to read or the wave to call - and a pulsing box is drawn around everything the line is
    /// about, on the map (the Core, the gate, an ore patch, a layout) or on the HUD (a bar key, the coin
    /// readout, a row of the status card). The HUD hides what the lesson has not reached yet
    /// (<c>HudView.ApplyCoachConcealment</c>); this view never touches the HUD's panels, it only asks the HUD
    /// where they are (<see cref="HudView.TryFocusRect"/>).
    ///
    /// It reads <see cref="TutorialState"/> and writes nothing to the world but the two requests a player makes
    /// - "next" and "start the wave" - which land on the next tick like every other request, so the lesson is
    /// the simulation's and this is only its voice. A map whose lesson is not coached (Quarry) draws nothing.
    /// </summary>
    public sealed class TutorialCoachView : MonoBehaviour
    {
        private SimWorld _world;
        private HudView _hud;
        private Camera _camera;
        private Palette _palette;

        private RectTransform _canvasRoot;
        private RectTransform _panel;
        private HudBox _panelBack;
        private HudBorder _panelFrame;
        private HudText _counter;
        private HudText _title;
        private HudText _body;
        private HudText _hint;
        private HudButton _button;
        private Action _buttonAction;

        private readonly List<FocusBox> _boxes = new List<FocusBox>();

        /// <summary>The dimming laid over everything the line is not about: a set of dark strips that tile the
        /// screen around the boxes, so the boxed places are the only bright ones. Pooled, like the boxes.</summary>
        private readonly List<HudBox> _shades = new List<HudBox>();

        /// <summary>This frame's boxed places in screen pixels, padded like their boxes but without the breath,
        /// so the popup's placement and the dimming's holes stand still while the frames pulse.</summary>
        private readonly List<Rect> _focusRects = new List<Rect>();

        /// <summary>The line on screen, so the hint's clock restarts when it changes. A ticked line goes
        /// straight to the next one: no "done" flash in between.</summary>
        private int _shownIndex = -1;
        private string _shownKey;
        private float _lineSince;

        // ---------------------------------------------------------------- the look, in design units at 720p

        private const float TitleSize = 26f;
        private const float BodySize = 19f;
        private const float SmallSize = 12f;
        private const float Inset = 22f;
        private const float MaxWidth = HudLayout.StatusWidth;
        private const float ButtonHeight = 40f;
        private const float ButtonWidth = 200f;

        /// <summary>How long a line waits before its hint appears: long enough not to talk over a player who
        /// is already doing it.</summary>
        private const float HintSeconds = 15f;

        /// <summary>The breathing room around a boxed thing, in screen pixels at 720p.</summary>
        private const float BoxPad = 12f;

        /// <summary>How far the box breathes in and out past its pad, in pixels at 720p: a box that moves is
        /// found before a box that only glows.</summary>
        private const float BoxBreath = 5f;

        /// <summary>The boxes' colour: a hot orange that nothing else on screen uses - not the HUD's warm
        /// accent, not the cyan of the tiles to click, not an ore.</summary>
        private static readonly Color BoxColour = new Color(1f, 0.38f, 0.05f, 1f);

        /// <summary>How dark the dimming is over everything outside the boxes.</summary>
        private const float ShadeAlpha = 0.6f;

        public void Initialize(SimWorld world, HudView hud, Camera camera, Palette palette)
        {
            _world = world;
            _hud = hud;
            _camera = camera;
            _palette = palette;

            if (_canvasRoot == null) BuildCanvas();
            if (_hud != null) _hud.ClaimPointer(_panel);

            _shownIndex = -1;
            _shownKey = null;
        }

        private void OnEnable()
        {
            if (_canvasRoot != null) _canvasRoot.gameObject.SetActive(true);
        }

        /// <summary>Stood down with the HUD while a menu holds the run, so the popup never floats over the title.</summary>
        private void OnDisable()
        {
            if (_canvasRoot != null) _canvasRoot.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_world == null || _canvasRoot == null) return;

            TutorialState tutorial = _world.Tutorial;
            if (!tutorial.Coached)
            {
                Hide();
                return;
            }

            float scale = HudLayout.Scale(Screen.height);
            ReadKeys(tutorial);

            // A finished lesson, a won map or a lost one each get their own last word; otherwise the current line.
            if (_world.Status == GameStatus.Won)
                ShowEnd("MAP CLEARED",
                    "You held the Core! Press N to continue.", scale);
            else if (_world.Status == GameStatus.Lost)
                ShowEnd("THE CORE FELL",
                    "Keep the turret fed with shapes. Press R to try again.",
                    scale);
            else
                ShowLine(tutorial, scale);
        }

        // ---------------------------------------------------------------- input

        /// <summary>Enter is "next" on a popup. N is the game's own wave key, which the world already hears.</summary>
        private void ReadKeys(TutorialState tutorial)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            bool enter = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            if (enter && tutorial.WaitsForAcknowledge) _world.RequestTutorialNext();
        }

        private void PressButton() => _buttonAction?.Invoke();

        // ---------------------------------------------------------------- the popup

        private void ShowLine(TutorialState tutorial, float scale)
        {
            int index = tutorial.StepsCompleted;

            if (tutorial.Complete)
            {
                Hide();
                return;
            }

            TutorialTask task = tutorial.Current;
            string key = "line" + index;
            if (key != _shownKey)
            {
                _shownKey = key;
                _shownIndex = index;
                _lineSince = Time.unscaledTime;
            }

            string counter = "STEP " + (index + 1) + " OF " + tutorial.Count;
            string button = null;
            _buttonAction = null;

            if (task.Step == TutorialStep.Acknowledge)
            {
                button = index == tutorial.Count - 1 ? "FINISH  ·  Enter" : "NEXT  ·  Enter";
                _buttonAction = _world.RequestTutorialNext;
            }
            else if (task.Step == TutorialStep.StartWave)
            {
                button = "START WAVE  ·  N";
                _buttonAction = _world.RequestNextWave;
            }

            bool hintDue = !string.IsNullOrEmpty(task.Hint) && Time.unscaledTime - _lineSince >= HintSeconds;
            LayoutBoxes(task, scale, done: false);
            Fill(counter, task.Title, task.Text, hintDue ? task.Hint : null, button, scale, _palette.HudAccent);

            // The fight is the one line not dimmed: the player has to see the enemies, wherever they walk. A
            // lesson without the spotlight is never dimmed.
            bool dim = tutorial.Spotlight && task.Step != TutorialStep.ClearWave;
            LayoutShade(dim ? _focusRects : null);
        }

        private void ShowEnd(string title, string body, float scale)
        {
            _buttonAction = null;
            _focusRects.Clear();
            Fill(string.Empty, title, body, null, null, scale,
                _world.Status == GameStatus.Won ? HudInk.Good(_palette) : _palette.HudWarn);
            HideBoxesFrom(0);
            LayoutShade(null);
        }

        /// <summary>Write the popup and size it to its words: the panel sits on the left, in the status card's
        /// column just under it, over the room the coached framing leaves left of the map - so it covers neither
        /// the HUD it points at nor the tiles the player has to click.</summary>
        private void Fill(string counter, string title, string body, string hint, string button, float scale,
            Color titleColour)
        {
            _canvasRoot.gameObject.SetActive(true);
            _panel.gameObject.SetActive(true);

            float width = Mathf.Min(MaxWidth * scale, Screen.width - 2f * HudLayout.Margin * scale);
            float inner = width - Inset * 2f * scale;

            _counter.Text.fontSize = SmallSize * scale;
            _title.Text.fontSize = TitleSize * scale;
            _body.Text.fontSize = BodySize * scale;
            _hint.Text.fontSize = (BodySize - 3f) * scale;

            _counter.Set(counter, HudInk.Muted(_palette));
            _title.Set(title ?? string.Empty, titleColour);
            _body.Set(body ?? string.Empty, _palette.HudText);
            _hint.Set(hint ?? string.Empty, HudInk.Muted(_palette));
            _hint.Show(!string.IsNullOrEmpty(hint));

            float y = Inset * 0.6f * scale;
            Place(_counter.Rect, Inset * scale, y, inner, SmallSize * 1.4f * scale);
            y += SmallSize * 1.6f * scale;

            float titleHeight = string.IsNullOrEmpty(title) ? 0f : Height(_title, inner);
            Place(_title.Rect, Inset * scale, y, inner, titleHeight);
            y += titleHeight + (titleHeight > 0f ? 6f * scale : 0f);

            float bodyHeight = Height(_body, inner);
            Place(_body.Rect, Inset * scale, y, inner, bodyHeight);
            y += bodyHeight;

            if (!string.IsNullOrEmpty(hint))
            {
                y += 8f * scale;
                float hintHeight = Height(_hint, inner);
                Place(_hint.Rect, Inset * scale, y, inner, hintHeight);
                y += hintHeight;
            }

            bool hasButton = !string.IsNullOrEmpty(button);
            _button.gameObject.SetActive(hasButton);
            if (hasButton)
            {
                y += 14f * scale;
                float bw = Mathf.Min(ButtonWidth * scale, inner);
                Place(_button.Root, width - Inset * scale - bw, y, bw, ButtonHeight * scale);
                _button.Label.Text.fontSize = 15f * scale;
                _button.Label.Set(button, _palette.HudPanel);

                // The one loud thing on the screen: filled with the accent, brighter under the pointer.
                Color fill = _button.Hovered ? Color.Lerp(_palette.HudAccent, Color.white, 0.25f) : _palette.HudAccent;
                _button.Fill.Set(fill);
                _button.Border.Set(Color.clear);
                y += ButtonHeight * scale;
            }

            y += Inset * scale;

            // Placed by its bottom-left corner in screen pixels, the way the boxes are measured.
            _panel.anchorMin = _panel.anchorMax = Vector2.zero;
            _panel.pivot = Vector2.zero;
            _panel.anchoredPosition = ChoosePanelSpot(y, scale);
            _panel.sizeDelta = new Vector2(width, y);

            // A slow breath on the frame, so the popup reads as the thing to look at without flashing.
            float breath = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);
            _panelFrame.Set(WithAlpha(titleColour, breath));
        }

        private void Hide()
        {
            if (_panel != null) _panel.gameObject.SetActive(false);
            HideBoxesFrom(0);
            LayoutShade(null);
        }

        private static float Height(HudText text, float width)
            => Mathf.Ceil(text.Text.GetPreferredValues(text.Text.text, width, 0f).y) + 2f;

        private static void Place(RectTransform rt, float x, float yFromTop, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -yFromTop);
            rt.sizeDelta = new Vector2(width, height);
        }

        // ---------------------------------------------------------------- the boxes

        /// <summary>One box per thing the line is about. World places are measured from the camera every frame,
        /// so a box follows the map as it pans and zooms; HUD pieces are measured by the HUD itself.</summary>
        private void LayoutBoxes(TutorialTask task, float scale, bool done)
        {
            int used = 0;
            BuildKind building = LineBuilding(task);
            _focusRects.Clear();

            for (int i = 0; i < task.Focus.Length; i++)
            {
                if (!TryScreenRect(task, task.Focus[i], building, out Rect rect)) continue;

                FocusBox box = BoxAt(used++);
                float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                _focusRects.Add(OnScreen(Grow(rect, (BoxPad + BoxBreath) * scale)));
                rect = OnScreen(Grow(rect, (BoxPad + BoxBreath * wave) * scale));

                box.Root.gameObject.SetActive(true);
                box.Root.anchorMin = box.Root.anchorMax = Vector2.zero;
                box.Root.pivot = Vector2.zero;
                box.Root.anchoredPosition = rect.position;
                box.Root.sizeDelta = rect.size;

                Color colour = done ? HudInk.Good(_palette) : BoxColour;
                float pulse = 0.8f + 0.2f * wave;
                box.Frame.Set(WithAlpha(colour, pulse));
                box.Wash.Set(WithAlpha(colour, 0.16f * pulse));
            }

            HideBoxesFrom(used);
        }

        private static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        /// <summary>Kept on screen, so a box around a corner piece of the HUD keeps all four sides.</summary>
        private static Rect OnScreen(Rect r)
        {
            const float edge = 2f;
            return Rect.MinMaxRect(Mathf.Max(edge, r.xMin), Mathf.Max(edge, r.yMin),
                Mathf.Min(Screen.width - edge, r.xMax), Mathf.Min(Screen.height - edge, r.yMax));
        }

        // ---------------------------------------------------------------- where the popup goes

        /// <summary>The popup's bottom-left corner: always the same place, the left column under the status card,
        /// in the room the coached framing leaves left of the map. It never moves between lines, so the player
        /// always knows where to read.</summary>
        private static Vector2 ChoosePanelSpot(float height, float scale)
        {
            float margin = HudLayout.Margin * scale;
            float top = (HudLayout.Margin + HudLayout.StatusHeight + HudLayout.TutorialGap * 2f) * scale;
            return new Vector2(margin, Mathf.Max(margin, Screen.height - top - height));
        }

        /// <summary>The tile a machine the line is about stands as; Empty for a belt or anything that is not a
        /// machine, which the feed box then leaves out.</summary>
        private static TileKind FedTile(BuildKind building)
        {
            if (building == BuildKind.Belt) return TileKind.Empty;
            try { return BuildCatalog.TileFor(building); }
            catch (ArgumentOutOfRangeException) { return TileKind.Empty; }
        }

        // ---------------------------------------------------------------- the dimming

        /// <summary>Darken the screen everywhere but the holes: the screen is cut into a grid along every hole's
        /// edges, and each run of cells in a row that no hole covers becomes one dark strip. A handful of boxes
        /// is a handful of strips. Null or no holes: no dimming at all - a line with nothing boxed is read plain.
        /// The strips take no clicks, so the player can still build anywhere.</summary>
        private void LayoutShade(List<Rect> holes)
        {
            int used = 0;
            if (holes != null && holes.Count > 0)
            {
                var xs = new List<float> { 0f, Screen.width };
                var ys = new List<float> { 0f, Screen.height };
                for (int i = 0; i < holes.Count; i++)
                {
                    xs.Add(holes[i].xMin); xs.Add(holes[i].xMax);
                    ys.Add(holes[i].yMin); ys.Add(holes[i].yMax);
                }
                xs.Sort();
                ys.Sort();

                Color dark = new Color(0f, 0f, 0f, ShadeAlpha);
                for (int j = 0; j + 1 < ys.Count; j++)
                {
                    float y0 = ys[j], y1 = ys[j + 1];
                    if (y1 - y0 < 0.5f) continue;

                    float runStart = -1f;
                    for (int i = 0; i + 1 < xs.Count; i++)
                    {
                        float x0 = xs[i], x1 = xs[i + 1];
                        if (x1 - x0 < 0.5f) continue;

                        bool covered = Covered(holes, new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f));
                        if (!covered && runStart < 0f) runStart = x0;
                        if (covered && runStart >= 0f)
                        {
                            Strip(used++, Rect.MinMaxRect(runStart, y0, x0, y1), dark);
                            runStart = -1f;
                        }
                    }
                    if (runStart >= 0f) Strip(used++, Rect.MinMaxRect(runStart, y0, Screen.width, y1), dark);
                }
            }

            for (int i = used; i < _shades.Count; i++) _shades[i].Image.gameObject.SetActive(false);
        }

        private static bool Covered(List<Rect> holes, Vector2 point)
        {
            for (int i = 0; i < holes.Count; i++)
                if (holes[i].Contains(point)) return true;
            return false;
        }

        private void Strip(int index, Rect rect, Color colour)
        {
            while (_shades.Count <= index)
            {
                RectTransform node = Node(_canvasRoot, "shade" + _shades.Count);
                Image image = node.gameObject.AddComponent<Image>();
                image.raycastTarget = false;   // dims the map, never blocks a click on it
                var shade = new HudBox(image);
                node.SetAsFirstSibling();   // under the boxes and the popup
                _shades.Add(shade);
            }

            HudBox strip = _shades[index];
            RectTransform rt = strip.Image.rectTransform;
            strip.Image.gameObject.SetActive(true);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = rect.position;
            rt.sizeDelta = rect.size;
            strip.Set(colour);
        }

        private void HideBoxesFrom(int first)
        {
            for (int i = first; i < _boxes.Count; i++) _boxes[i].Root.gameObject.SetActive(false);
        }

        /// <summary>The building a line is about: the one its rule names, or the first of its blueprint. What a
        /// <see cref="TutorialFocus.BarTile"/> box points at.</summary>
        /// <summary>The bar key a <see cref="TutorialFocus.BarTile"/> box points at: what the player has to pick
        /// up to do the line. A line with a blueprint is done with the blueprint's building (a belt to feed a
        /// turret is picked up as the belt, not the turret); otherwise the building the line's rule names.</summary>
        private static BuildKind BarBuilding(TutorialTask task)
            => task.Ghost != null && task.Ghost.Length > 0 ? task.Ghost[0].Build : LineBuilding(task);

        private static BuildKind LineBuilding(TutorialTask task)
        {
            if (TutorialScript.TryRequiredBuilding(task.Step, out BuildKind building)) return building;
            return task.Ghost.Length > 0 ? task.Ghost[0].Build : BuildKind.Belt;
        }

        /// <summary>The door a line about "the gate" means: the one the wave it is about walks in at, because a
        /// map may have more than one - the first map opens a second one for its last wave - and pointing at the
        /// first would box a gate that wave never uses. Falls back to the map's first door while the clock names
        /// none.</summary>
        private Int2 GateInPlay(MapDefinition map)
        {
            WaveDefinition next = _world != null ? _world.Waves.NextWaveDefinition : null;
            if (next != null)
                for (int g = 0; g < next.Groups.Length; g++)
                {
                    int door = map.ResolveSpawn(next.Groups[g].SpawnPoint);
                    if (door >= 0) return map.SpawnPoints[door];
                }

            return map.SpawnPoints[0];
        }

        private bool TryScreenRect(TutorialTask task, TutorialFocus focus, BuildKind building, out Rect rect)
        {
            rect = default;
            MapDefinition map = _world.Map;

            switch (focus)
            {
                case TutorialFocus.Core:
                    return WorldRect(map.CoreCell.X, map.CoreCell.Y, CoreState.Size, CoreState.Size, out rect);

                case TutorialFocus.Gate:
                    if (map.SpawnPoints.Length == 0) return false;
                    Int2 gate = GateInPlay(map);
                    return WorldRect(gate.X, gate.Y, 1, 1, out rect);

                case TutorialFocus.Target:
                    if (!task.Target.HasValue) return false;
                    return WorldRect(PatchAround(task.Target.Value), out rect);

                case TutorialFocus.Ghost:
                {
                    if (task.Ghost == null || task.Ghost.Length == 0) return false;
                    int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                    void Take(Int2 cell)
                    {
                        minX = Math.Min(minX, cell.X);
                        minY = Math.Min(minY, cell.Y);
                        maxX = Math.Max(maxX, cell.X);
                        maxY = Math.Max(maxY, cell.Y);
                    }

                    // The whole job in one box: the blueprint, the tile the drag starts on, and the machine the
                    // line is about where it already stands next to the blueprint (the turret a belt is to feed),
                    // so nothing the player has to touch is left in the dark.
                    for (int i = 0; i < task.Ghost.Length; i++) Take(task.Ghost[i].Cell);
                    if (task.Target.HasValue) Take(task.Target.Value);

                    TileKind fed = FedTile(building);
                    for (int i = 0; i < task.Ghost.Length; i++)
                    {
                        Int2 c = task.Ghost[i].Cell;
                        Int2[] around = { new Int2(c.X + 1, c.Y), new Int2(c.X - 1, c.Y),
                                          new Int2(c.X, c.Y + 1), new Int2(c.X, c.Y - 1) };
                        for (int n = 0; n < around.Length; n++)
                            if (_world.TileGrid.InBounds(around[n]) && _world.TileGrid.Get(around[n]) == fed &&
                                fed != TileKind.Empty)
                                Take(around[n]);
                    }

                    return WorldRect(minX, minY, maxX - minX + 1, maxY - minY + 1, out rect);
                }

                case TutorialFocus.None:
                    return false;

                case TutorialFocus.BarTile:
                    return _hud != null && _hud.TryFocusRect(focus, BarBuilding(task), out rect);

                default:
                    return _hud != null && _hud.TryFocusRect(focus, building, out rect);
            }
        }

        /// <summary>The tile, or the whole ore patch it is part of: "mine this patch", not "this one square".</summary>
        private RectInt PatchAround(Int2 start)
        {
            MapDefinition map = _world.Map;
            if (!InMap(start) || !_world.Terrain.HasOre(start)) return new RectInt(start.X, start.Y, 1, 1);

            int minX = start.X, minY = start.Y, maxX = start.X, maxY = start.Y;
            var seen = new HashSet<Int2> { start };
            var open = new Queue<Int2>();
            open.Enqueue(start);

            while (open.Count > 0 && seen.Count < 64)
            {
                Int2 c = open.Dequeue();
                minX = Math.Min(minX, c.X);
                minY = Math.Min(minY, c.Y);
                maxX = Math.Max(maxX, c.X);
                maxY = Math.Max(maxY, c.Y);

                foreach (Int2 n in new[] { new Int2(c.X + 1, c.Y), new Int2(c.X - 1, c.Y),
                                          new Int2(c.X, c.Y + 1), new Int2(c.X, c.Y - 1) })
                {
                    if (!InMap(n) || seen.Contains(n) || !_world.Terrain.HasOre(n)) continue;
                    seen.Add(n);
                    open.Enqueue(n);
                }
            }

            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);

            bool InMap(Int2 c) => c.X >= 0 && c.Y >= 0 && c.X < map.Width && c.Y < map.Height;
        }

        private bool WorldRect(RectInt cells, out Rect rect)
            => WorldRect(cells.x, cells.y, cells.width, cells.height, out rect);

        /// <summary>Tiles to screen pixels: a tile's coordinates are world coordinates, so a cell (x, y) covers
        /// x..x+1 by y..y+1.</summary>
        private bool WorldRect(int x, int y, int width, int height, out Rect rect)
        {
            rect = default;
            if (_camera == null) return false;

            Vector3 a = _camera.WorldToScreenPoint(new Vector3(x, y, 0f));
            Vector3 b = _camera.WorldToScreenPoint(new Vector3(x + width, y + height, 0f));
            rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return rect.width > 0f && rect.height > 0f;
        }

        private FocusBox BoxAt(int index)
        {
            while (_boxes.Count <= index)
            {
                RectTransform root = Node(_canvasRoot, "focus" + _boxes.Count);
                RectTransform wash = ImageNode(root, "wash");
                RectTransform frame = ImageNode(root, "frame");

                var box = new FocusBox
                {
                    Root = root,
                    Wash = HudUi.AdoptRound(wash, Color.clear, 8f),
                    Frame = HudUi.AdoptBorder(frame, Color.clear, 8f, 5f),
                };
                root.SetSiblingIndex(_shades.Count);   // over the dimming, under the popup
                _boxes.Add(box);
            }

            return _boxes[index];
        }

        private sealed class FocusBox
        {
            public RectTransform Root;
            public HudBox Wash;
            public HudBorder Frame;
        }

        // ---------------------------------------------------------------- building

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("CoachCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;   // over the HUD (100), under the menus (200)
            _canvasRoot = (RectTransform)canvasGo.transform;

            _panel = Node(_canvasRoot, "popup");
            HudUi.AdoptShadow(ImageNode(_panel, "shadow"), 14f, _palette.HudShadow);
            _panelBack = HudUi.AdoptRound(ImageNode(_panel, "back"), _palette.HudPanel, 14f);
            _panelBack.Image.raycastTarget = true;   // the popup is solid: a click on it is not a click on the map
            _panelFrame = HudUi.AdoptBorder(ImageNode(_panel, "frame"), _palette.HudAccent, 14f, 2.5f);

            _counter = Label(_panel, "counter", SmallSize, FontStyle.Bold, wrap: false, tracked: true);
            _title = Label(_panel, "title", TitleSize, FontStyle.Bold, wrap: true, tracked: false);
            _body = Label(_panel, "body", BodySize, FontStyle.Normal, wrap: true, tracked: false);
            _hint = Label(_panel, "hint", BodySize - 3f, FontStyle.Italic, wrap: true, tracked: false);

            // A button is a root with three named pieces under it - the shape HudUi.AdoptButton wires.
            RectTransform buttonRoot = Node(_panel, "next");
            ImageNode(buttonRoot, "next.back");
            RectTransform label = Node(buttonRoot, "next.label");
            label.gameObject.AddComponent<TextMeshProUGUI>();
            Stretch(label);
            ImageNode(buttonRoot, "next.frame");
            _button = HudUi.AdoptButton(buttonRoot, "NEXT", 15f, _palette.HudPanel, PressButton);

            _panel.gameObject.SetActive(false);
        }

        /// <summary>A full-size child carrying an Image, ready for one of HudUi's Adopt* calls.</summary>
        private static RectTransform ImageNode(Transform parent, string name)
        {
            RectTransform rt = Node(parent, name);
            rt.gameObject.AddComponent<Image>();
            Stretch(rt);
            return rt;
        }

        private HudText Label(RectTransform parent, string name, float size, FontStyle style, bool wrap, bool tracked)
        {
            RectTransform rt = Node(parent, name);
            rt.gameObject.AddComponent<TextMeshProUGUI>();
            return HudUi.AdoptText(rt, size, style, TextAnchor.UpperLeft, wrap, tracked);
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

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
