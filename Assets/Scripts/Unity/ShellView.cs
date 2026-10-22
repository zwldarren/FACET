using System;
using System.Collections.Generic;
using System.Text;
using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Facet.Game
{
    /// <summary>
    /// The game's shell: the level select that lists the campaign's maps, the in-run menu that carries a
    /// player back to them, and the title screen the menu leads to. Built and wired by the driver like
    /// every other view - no prefabs, no scene authoring - out of the same <see cref="HudUi"/> widgets
    /// the HUD is made of, dressed in the palette's HUD colours rather than colours of its own.
    ///
    /// The shell owns the screen while it is open: it covers it with one full-screen panel, the driver
    /// stops sampling the devices and ticking the run for as long as <see cref="Open"/> stands, and the
    /// HUD and the camera rig are stood down with it - so no click or key aimed at the menu can reach the
    /// simulation, and no second of the run is spent inside it.
    ///
    /// What the shell decides, on purpose (issue §5's questions, answered here rather than by accident):
    ///
    /// - One scene, one driver: the title and the level list are states of the same runtime-built
    ///   canvas, not a second scene, so the WebGL build and <c>Tools/publish-pages.sh</c> are untouched.
    /// - The run behind the shell is held, not rebuilt: the driver builds its world at boot as it always
    ///   has, and stops sampling the devices and ticking it for as long as <see cref="Open"/> stands, so
    ///   no second of a run is spent in a menu and no key aimed at the menu reaches the simulation. A map
    ///   picked from the list is built as a fresh run - a run is one run.
    /// - "Continue" opens the campaign's frontier map - the first one not yet cleared - as a fresh run.
    ///   A run is never carried across a menu; the maps are what persist, not a factory.
    /// - Progress is <see cref="CampaignState.ClearedCount"/>, seeded from <see cref="CampaignProgress"/>
    ///   and written back by the driver when it grows. A map is open when it is cleared, or when it is
    ///   the frontier; a cleared map stays open for good, so the ladder is kept, not re-walked. A locked
    ///   row says why in the campaign's own currency: the map standing in the way, and the operators
    ///   that map is there to hand over - the unlock ladder, not the map's ordinal.
    /// - The level list shows a map's own facts - size, starting budget, its unlocked operators, how
    ///   many waves its table holds (or "endless") and a pinned seed - and never the waves themselves:
    ///   the demands are the run's surprises, and the run is where they are met.
    /// - Endless mode is the last map played unbounded, and it is reached the way it always was - the
    ///   post-run N - plus one more door: the title's play button becomes endless once every map is
    ///   cleared. The level list lists authored maps only; endless is a mode of the last one, not a map.
    /// - The tutorial card and the opening camera strip belong to map 0 itself, whoever reaches it and
    ///   however (<see cref="CampaignState.IsFirstMap"/> stays the map's own number, not the entry path),
    ///   and the player's own hide/show choice outranks the default from their first press on.
    /// </summary>
    [DefaultExecutionOrder(210)]   // after the HUD (200): the shell's keys are read with the run already held
    public sealed class ShellView : MonoBehaviour
    {
        private enum Mode { Title, Levels, Help }

        private Palette _palette;
        private MapCatalog _catalog;
        private CampaignState _campaign;

        /// <summary>The shell's three doors back into the driver: play a map (endless or not), close
        /// the in-run menu, and leave a run for the title. Handed in by the driver, which owns every
        /// transition - the shell only asks.</summary>
        private Action<int, bool> _play;
        private Action _resume;
        private Action _toTitle;

        private RectTransform _canvasRoot;

        private Mode _mode = Mode.Title;

        /// <summary>The frame the current mode was built in. The keys that opened the shell are still
        /// "pressed this frame" when the shell's own update runs - the HUD's M opens the menu, and the
        /// shell's M would close it again the same frame - so a mode just built ignores its keys until
        /// the next one. One frame of silence, and only on the frame something changed.</summary>
        private int _shownAtFrame = -1;

        /// <summary>Is the shell on screen? The driver's one question: while it stands the devices are
        /// the shell's, the run is held, and nothing ticks.</summary>
        public bool Open { get; private set; }

        /// <summary>Whether the shell was opened over a run - the in-run menu - rather than at the boot
        /// or a return to the title. It decides what the back button does and says.</summary>
        private bool _inRun;

        private float _scale = -1f;
        private int _screenWidth = -1;
        private int _screenHeight = -1;

        /// <summary>The buttons dressed for hover each open frame, gathered as built: the shell's
        /// buttons are the HUD's generic one - a fill that lightens under the pointer and an accent
        /// edge - so one dressing pass covers every one of them.</summary>
        private readonly List<HudButton> _buttons = new List<HudButton>();

        /// <summary>Locked rows dress themselves dim and take no press; they are remembered apart so
        /// the hover pass can skip them without a flag on the row.</summary>
        private readonly List<HudButton> _locked = new List<HudButton>();

        /// <summary>The title's one filled action: the accent is the shell's "this is the door" colour,
        /// and a title with three identical buttons has no door at all.</summary>
        private readonly List<HudButton> _primary = new List<HudButton>();

        // ------------------------------------------------------------------ wiring and lifecycle

        /// <summary>Point the shell at the palette, the campaign and its catalog, and hand it the two
        /// doors back into the driver. The canvas is built once; every open rebuilds the content against
        /// the progress of the moment, which is what keeps a level list honest after a run has cleared
        /// something.</summary>
        public void Initialize(Palette palette, MapCatalog catalog, CampaignState campaign,
            Action<int, bool> play, Action resume, Action toTitle)
        {
            _palette = palette;
            _catalog = catalog;
            _campaign = campaign;
            _play = play;
            _resume = resume;
            _toTitle = toTitle;

            if (_canvasRoot == null) BuildCanvas();
        }

        private void Update()
        {
            if (_palette == null || _canvasRoot == null || !Open) return;

            RefreshScale();
            ReadKeys();
            DressButtons();
        }

        private void OnDestroy()
        {
            // The shared font and pixel sprite are the HUD's lifecycle (see HudView.OnDestroy): the
            // shell owns nothing of theirs and releases nothing of theirs.
            _canvasRoot = null;

            // The canvas children died with the object, so the lists would be holding destroyed widgets
            // until the next rebuild replaced them. Cleared here so teardown is symmetric with Rebuild and
            // a late dress pass cannot walk a destroyed component.
            _buttons.Clear();
            _locked.Clear();
            _primary.Clear();
        }

        // ------------------------------------------------------------------ the driver's doors

        /// <summary>Open on the title screen - where the in-run menu's far door lands. Everything on it is
        /// rebuilt from the progress of this moment.</summary>
        public void ShowTitle()
        {
            _inRun = false;
            SetMode(Mode.Title);
        }

        /// <summary>Open on the level list. Over a run (<paramref name="inRun"/>) it is the in-run menu:
        /// the back button returns to the run held paused behind it, and the list's bottom carries the
        /// way on to the title.</summary>
        public void ShowLevels(bool inRun)
        {
            _inRun = inRun;
            SetMode(Mode.Levels);
        }

        /// <summary>Close the shell. The run behind an in-run menu is the driver's to hand back - it held
        /// the run and it is the one that knows how - so the shell only goes away.</summary>
        public void Close()
        {
            Open = false;
            if (_canvasRoot != null) _canvasRoot.gameObject.SetActive(false);
        }

        private void SetMode(Mode mode)
        {
            _mode = mode;
            Rebuild();
            Open = true;
            _shownAtFrame = Time.frameCount;
            if (_canvasRoot != null) _canvasRoot.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------ building the canvas

        private void BuildCanvas()
        {
            HudUi.EnsureEventSystem(transform);

            var canvasGo = new GameObject("ShellCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the HUD's own canvas (100): the shell covers the screen even where a card of the
            // HUD's would reach, so a menu click can never land on a button that is not the menu's.
            canvas.sortingOrder = 200;

            _canvasRoot = (RectTransform)canvasGo.transform;
        }

        /// <summary>Tear the content down and build it again for the mode and progress of the moment.
        /// The one heavy pass the shell has, asked for by an open, a mode switch or a resize - the same
        /// trade the HUD's rebuild makes, and for the same reason: the fonts need the scale inside their
        /// point size to stay crisp.</summary>
        private void Rebuild()
        {
            _scale = HudLayout.Scale(Screen.height);
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;

            for (int i = _canvasRoot.childCount - 1; i >= 0; i--)
                DestroyImmediate(_canvasRoot.GetChild(i).gameObject);

            _buttons.Clear();
            _locked.Clear();
            _primary.Clear();

            BuildBackdrop();

            switch (_mode)
            {
                case Mode.Title: BuildTitle(); break;
                case Mode.Levels: BuildLevels(); break;
                case Mode.Help: BuildHelp(); break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Mode), _mode, "a shell mode must be built");
            }
        }

        private void BuildBackdrop()
        {
            // Opaque, not the panel's 96.5 %: the shell is a place, not a veil, and at 3.5 % of a bright
            // map the cards and the Core's square showed through the title exactly where the eye was
            // trying to read the wordmark.
            Color ink = _palette.HudPanel;
            ink.a = 1f;

            HudBox backdrop = HudUi.Box(_canvasRoot, "backdrop", ink);
            HudUi.StretchFull(backdrop.Rect);
            // The one panel that does take clicks: the shell owns the screen while it is up, and a
            // click that misses every button lands here rather than on the map behind it.
            backdrop.Image.raycastTarget = true;
        }

        // ------------------------------------------------------------------ the title screen

        /// <summary>The wordmark's size in design units: a title is an identity, not a line of HUD - the
        /// four-step scale stops at the stockpile's 28, and a title set at a data size was the smallest
        /// thing on its own screen.</summary>
        private const float WordmarkSize = 64f;

        private void BuildTitle()
        {
            RectTransform root = HudUi.Node(_canvasRoot, "title");
            // The children below speak in screen coordinates measured from the canvas's top edge, so the
            // root they hang off has to span the canvas rather than sit unsized at its centre.
            HudUi.StretchFull(root);

            BuildTitleMotif(root);

            HudText name = Label(root, "name", WordmarkSize, FontStyle.Bold, TextAnchor.MiddleCenter);
            // The one place the HUD's tracking rule widens: a wordmark is drawn, not set, and 64-point type
            // at the labels' 9 % reads as a line of text that happens to be big.
            name.Text.characterSpacing = 16f;
            HudUi.TopCentre(name.Rect, Screen.height * TitleNameTop, Screen.width, 80f * _scale);
            name.Set("FACET", _palette.HudText);

            HudText subtitle = Label(root, "subtitle", HudType.Body, FontStyle.Normal,
                TextAnchor.UpperCenter, wrap: true);
            HudUi.TopCentre(subtitle.Rect, Screen.height * TitleNameTop + 86f * _scale,
                Mathf.Min(680f, Screen.width / _scale - 40f) * _scale, 40f * _scale);
            subtitle.Set("an enemy's weakness is a shape, and what your factory builds is what your guns fire",
                _palette.MachineIdle);

            float y = Screen.height * TitleNameTop + 150f * _scale;
            AddMenuButton(root, "play", y, PlayLabel(), PressPlay, primary: true);
            y += ButtonStep;
            AddMenuButton(root, "maps", y, "the maps  ·  M", ShowLevelScreen);
            y += ButtonStep;
            AddMenuButton(root, "howto", y, "how to play  ·  T", () => SetMode(Mode.Help));
            y += ButtonStep;

#if !UNITY_WEBGL || UNITY_EDITOR
            // A quit is a host's luxury: a WebGL build has no tab to close from inside, so the button
            // is a desktop one and says nothing where it cannot mean it.
            AddMenuButton(root, "quit", y, "quit", Application.Quit);
#endif

            HudText footer = Label(root, "footer", HudType.Micro, FontStyle.Normal, TextAnchor.LowerCenter);
            HudUi.TopCentre(footer.Rect, Screen.height - 30f * _scale, Screen.width - 40f, 18f * _scale);
            footer.Set("a factory-defence prototype  ·  github.com/zwldarren/FACET",
                HudInk.Blend(_palette.HudText, _palette.HudPanel, 0.45f));
        }

        /// <summary>
        /// The title's ground: a square and the diamond inside it, drawn in the world's own ink at a
        /// watermark. It is the game's one figure and the game's own name - a facet is a square seen at the
        /// angle its cut makes - and it costs two generated outlines rather than an asset or a ring whose
        /// corner radius would have to be generated at the size of the screen.
        /// </summary>
        private void BuildTitleMotif(RectTransform root)
        {
            float side = Mathf.Min(Screen.width, Screen.height) * 0.62f;
            float y = (0.44f - 0.5f) * Screen.height;
            Color ink = new Color(_palette.HudText.r, _palette.HudText.g, _palette.HudText.b, 0.05f);

            RectTransform square = HudUi.Node(root, "motif.square");
            HudUi.Place(square, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, y), new Vector2(side, side));
            HudUi.Border(square, "line", ink, 6f, 1f);

            RectTransform diamond = HudUi.Node(root, "motif.diamond");
            float inner = side * 0.70f;
            HudUi.Place(diamond, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, y), new Vector2(inner, inner));
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            HudUi.Border(diamond, "line", ink, 4f, 1f);
        }

        /// <summary>The play button's words, from where the campaign stands: a fresh profile plays,
        /// a started campaign continues at its frontier map, and a finished one has nothing left but
        /// the last map played endless - which is the same button's honest answer by then.</summary>
        private string PlayLabel()
        {
            if (_campaign.ClearedCount >= _campaign.MapCount)
                return "endless on " + _catalog.At(_campaign.MapCount - 1).Name + "  ·  Enter";
            if (_campaign.ClearedCount == 0) return "play  ·  Enter";

            return "continue, map " + (_campaign.ClearedCount + 1) + " of " + _campaign.MapCount + "  ·  Enter";
        }

        private HudButton AddMenuButton(RectTransform parent, string name, float yFromTop, string label,
            Action pressed, bool primary = false)
        {
            HudButton button = HudUi.Button(parent, name, 320f * _scale, 36f * _scale, label,
                FontSize(HudType.Label), primary ? _palette.HudPanel : _palette.HudText, pressed);
            HudUi.TopCentre(button.Root, yFromTop, 320f * _scale, 36f * _scale);
            _buttons.Add(button);
            if (primary) _primary.Add(button);
            return button;
        }

        private void PressPlay()
        {
            // The frontier is the first map the campaign has not cleared; with the whole campaign
            // cleared, the same button is the endless entry - the last map, unbounded.
            bool endless = _campaign.ClearedCount >= _campaign.MapCount;
            int index = Mathf.Min(_campaign.ClearedCount, _campaign.MapCount - 1);
            _play?.Invoke(index, endless);
        }

        private void ShowLevelScreen() => SetMode(Mode.Levels);

        // ------------------------------------------------------------------ the level select

        private void BuildLevels()
        {
            RectTransform levels = HudUi.Node(_canvasRoot, "levels");
            // The card below is placed in screen coordinates measured from the canvas's top edge, so the
            // root it hangs off has to span the canvas rather than sit unsized at its centre.
            HudUi.StretchFull(levels);

            // The rows must all fit: a campaign longer than the screen shrinks its rows rather than
            // clipping the tail off, because a map the list cannot show is a map the player cannot pick.
            // Everything below is in design units and scaled once, at the card - a pixel term that rode
            // through the final * _scale would make the card _scale times taller than the screen.
            float header = 44f;
            float foot = InRun ? 44f : 16f;   // the in-run menu's door to the title hangs in this band
            float available = Screen.height / _scale - 2f * HudLayout.Margin - header - foot;
            float rows = Mathf.Max(1, _catalog.Count);
            float fit = (available - (rows - 1) * LevelRowGap) / rows;
            float rowHeight = Mathf.Min(LevelRowHeight, Mathf.Max(LevelRowFloor, fit));

            // The gap shrinks with the rows once they reach the floor, so the floor is reachable and a
            // campaign long enough to bind it still fits the screen instead of overflowing the card.
            float rowGap = fit < LevelRowFloor && rows > 1
                ? Mathf.Max(0f, (available - rows * rowHeight) / (rows - 1))
                : LevelRowGap;

            float cardWidthDesign = Mathf.Min(660f, Screen.width / _scale - 2f * HudLayout.Margin);
            float cardWidth = cardWidthDesign * _scale;
            float rowsBottom = header + _catalog.Count * rowHeight
                + Mathf.Max(0, _catalog.Count - 1) * rowGap;
            float cardHeight = (rowsBottom + foot) * _scale;

            RectTransform card = HudUi.Node(levels, "card");
            HudUi.TopCentre(card, (Screen.height - cardHeight) * 0.5f, cardWidth, cardHeight);

            HudUi.Shadow(card, "shadow", 12f, _palette.HudShadow);
            HudBox back = HudUi.Round(card, "back", _palette.HudPanel, 12f);
            HudUi.StretchFull(back.Rect);
            HudUi.Border(card, "frame", PanelBorder(), 12f, 1f);

            HudText title = Label(card, "title", HudType.Body, FontStyle.Bold, TextAnchor.UpperLeft,
                tracked: true);
            HudUi.TopLeft(title.Rect, 14f * _scale, 9f * _scale, 200f * _scale, 22f * _scale);
            title.Set("MAPS", _palette.HudText);

            HudText count = Label(card, "count", HudType.Micro, FontStyle.Normal, TextAnchor.UpperRight);
            HudUi.TopLeft(count.Rect, 14f * _scale, 12f * _scale,
                cardWidth - (14f + (InRun ? 160f : 100f)) * _scale, 16f * _scale);
            count.Set(_campaign.ClearedCount + " of " + _campaign.MapCount + " cleared",
                HudInk.Blend(_palette.HudText, _palette.HudPanel, 0.45f));

            // The one way out, top-right where the help card's close button sits. Over a run it goes
            // back to that run; from the title's level list it goes back to the title.
            HudButton backButton = HudUi.Button(card, "back", (InRun ? 150f : 90f) * _scale, 22f * _scale,
                InRun ? "back to the run  ·  M" : "title  ·  Esc", FontSize(HudType.Micro), _palette.HudText,
                InRun ? (Action)(() => _resume?.Invoke()) : () => SetMode(Mode.Title));
            HudUi.TopRight(backButton.Root, 10f * _scale, 9f * _scale, (InRun ? 150f : 90f) * _scale,
                22f * _scale);
            _buttons.Add(backButton);

            float rowTop = header;
            for (int i = 0; i < _catalog.Count; i++)
            {
                BuildRow(card, i, rowTop, rowHeight, cardWidthDesign);
                rowTop += rowHeight + rowGap;
            }

            // The in-run menu's one extra door: the title itself, under the maps. From the title's own
            // level list it would point at where the player already came from, so it is the in-run
            // menu's alone.
            if (InRun)
            {
                HudButton titleButton = HudUi.Button(card, "totitle", cardWidth - 28f * _scale, 26f * _scale,
                    "to the title screen", FontSize(HudType.Label), _palette.HudText, () => _toTitle?.Invoke());
                HudUi.TopLeft(titleButton.Root, 14f * _scale, (rowsBottom + 4f) * _scale,
                    cardWidth - 28f * _scale, 26f * _scale);
                _buttons.Add(titleButton);
            }
        }

        /// <summary>One map as a row: its name and the facts that choose between maps on the left, its
        /// state on the right - CLEARED, NEXT for the frontier, PLAYING for the run behind an in-run
        /// menu, or LOCKED with the reason. An open row is a button that asks the driver for the map; a
        /// locked one is drawn dim and presses nothing, the honest read-only answer.
        ///
        /// Everything here is in design units, scaled at the widget: the row's height is the caller's
        /// fit, and both the row and its contents have to shrink together.</summary>
        private void BuildRow(RectTransform card, int index, float yFromTop, float rowHeight,
            float cardWidth)
        {
            MapDefinition map = _catalog.At(index);
            bool locked = index > _campaign.ClearedCount;

            // A shrunk row cannot carry the facts line and the reason under it as well: at less than a
            // full row they run into the next row's name. The name and the state word are what a picker
            // has to read; the facts are the first thing to go.
            bool roomy = rowHeight >= LevelRowHeight - 0.01f;

            RectTransform root = HudUi.Node(card, "map" + index);
            HudUi.TopLeft(root, 14f * _scale, yFromTop * _scale,
                (cardWidth - 28f) * _scale, rowHeight * _scale);

            HudButton button = root.gameObject.AddComponent<HudButton>();
            button.Fill = HudUi.Round(root, "back", _palette.HudPanel, 8f);
            HudUi.StretchFull(button.Fill.Rect);
            button.Fill.Image.raycastTarget = true;
            button.Border = HudUi.Border(root, "frame", Color.clear, 8f, 1f);

            // An open row asks for its map; a locked one presses nothing, and is dressed dim by the
            // hover pass's locked list - the honest read-only answer, like the read-only picker's.
            if (!locked)
            {
                int captured = index;
                button.Pressed = () => _play?.Invoke(captured, false);
                _buttons.Add(button);
            }
            else _locked.Add(button);

            HudText name = Label(root, "name", HudType.Body, FontStyle.Bold, TextAnchor.UpperLeft,
                tracked: true);
            HudUi.TopLeft(name.Rect, 12f * _scale, 7f * _scale, 300f * _scale, 20f * _scale);
            name.Set(map.Name, locked ? LockedText() : _palette.HudText);

            if (roomy)
            {
                HudText facts = Label(root, "facts", HudType.Micro, FontStyle.Normal, TextAnchor.UpperLeft);
                HudUi.TopLeft(facts.Rect, 12f * _scale, 29f * _scale, (cardWidth - 184f) * _scale,
                    16f * _scale);
                facts.Set(Facts(map), locked ? LockedText() : _palette.MachineIdle);
            }

            HudText state = Label(root, "state", HudType.Micro, FontStyle.Bold, TextAnchor.UpperRight,
                tracked: true);
            HudUi.TopRight(state.Rect, 12f * _scale, 8f * _scale, 120f * _scale, 17f * _scale);
            state.Set(StateWord(index), StateColour(index));

            if (!locked || !roomy) return;

            // The reason, under the state word: the map standing in the way, and - when the map on the
            // way is the one that teaches something new - the operator it is there to hand over. That
            // is the unlock ladder said out loud, not a bare ordinal.
            HudText reason = Label(root, "reason", HudType.Micro, FontStyle.Normal, TextAnchor.UpperRight,
                wrap: true);
            HudUi.TopRight(reason.Rect, 12f * _scale, 27f * _scale, 260f * _scale, 24f * _scale);
            reason.Set(LockedReason(), LockedText());
        }

        /// <summary>The row's state word, in the priority the run itself would name: the map behind the
        /// in-run menu is playing, a cleared map stays cleared for good, the frontier is the campaign's
        /// next step, and anything past it is locked.</summary>
        private string StateWord(int index)
        {
            if (index == _campaign.MapIndex && InRun) return "PLAYING";
            if (index < _campaign.ClearedCount) return "CLEARED";
            if (index == _campaign.ClearedCount) return "NEXT";
            return "LOCKED";
        }

        private Color StateColour(int index)
        {
            if (index == _campaign.MapIndex && InRun) return _palette.HudAccent;
            if (index < _campaign.ClearedCount) return HudInk.Good(_palette);
            if (index == _campaign.ClearedCount) return _palette.HudAccent;
            return LockedText();
        }

        /// <summary>Why a row past the frontier is closed, in the campaign's own currency - the same answer
        /// for every locked row, because the gate is the campaign's, not the row's. The frontier map is what
        /// stands in the way; when it is the map that hands the campaign an operator it has never held, the
        /// reason names it - the unlock ladder, not "map 3 of 6".</summary>
        private string LockedReason()
        {
            int frontier = Mathf.Min(_campaign.ClearedCount, _campaign.MapCount - 1);
            MapDefinition gate = _catalog.At(frontier);

            string fresh = FreshOperatorNames(frontier);
            return fresh.Length > 0
                ? "clear " + gate.Name + " - it hands over the " + fresh
                : "clear " + gate.Name;
        }

        /// <summary>The operators map <paramref name="index"/> is the campaign's first to carry - the
        /// part of the unlock ladder clearing it buys - or the empty string when it teaches nothing new.</summary>
        private string FreshOperatorNames(int index)
        {
            Operators granted = Operators.None;
            for (int i = 0; i < index; i++) granted |= _catalog.At(i).Unlocked;

            return OperatorNames(_catalog.At(index).Unlocked & ~granted);
        }

        /// <summary>The operator names, every flag named: the dispatch the HUD's cards and the badge
        /// keep, so a fourth operator cannot slip into a map and ride out nameless.</summary>
        private static string OperatorNames(Operators unlocked)
        {
            var names = new StringBuilder();
            AppendOperator(names, unlocked, Operators.Cutter, "cutter");
            AppendOperator(names, unlocked, Operators.Rotator, "rotator");
            AppendOperator(names, unlocked, Operators.Stacker, "stacker");
            return names.ToString();
        }

        private static void AppendOperator(StringBuilder names, Operators unlocked, Operators flag,
            string name)
        {
            if ((unlocked & flag) == 0) return;
            if (names.Length > 0) names.Append(", ");
            names.Append(name);
        }

        /// <summary>A map's own facts, one line: the size a route is planned on, the budget it starts
        /// with, its wave count (or "endless"), the operators it hands over, and the seed only when the
        /// map pins one. Deliberately no wave table here: the demands are the run's surprises.</summary>
        private static string Facts(MapDefinition map)
        {
            var text = new StringBuilder();
            text.Append(map.Width).Append(" × ").Append(map.Height);
            text.Append("  ·  start ").Append(map.StartShapes);
            text.Append("  ·  ").Append(map.GeneratedWaves < 0 ? "endless" : map.Waves.Length + " waves");

            string operators = OperatorNames(map.Unlocked);
            if (operators.Length > 0) text.Append("  ·  ").Append(operators);
            if (map.GeneratedWaves >= 0) text.Append("  ·  endless on N");
            if (map.Seed != 0) text.Append("  ·  seed ").Append(map.Seed);

            return text.ToString();
        }

        private Color LockedText() => HudInk.Blend(_palette.HudText, _palette.HudPanel, 0.55f);

        // ------------------------------------------------------------------ the help card

        /// <summary>The shell's help card: the keyboard, the same list the run's help card prints -
        /// one keyboard, one list, which is why the HUD's is shared rather than copied. Built here
        /// because at boot there is no HUD yet to open its own.</summary>
        private void BuildHelp()
        {
            RectTransform root = HudUi.Node(_canvasRoot, "help");
            // The card is centred in screen coordinates, so the root it hangs off spans the canvas.
            HudUi.StretchFull(root);

            float width = 300f * _scale;
            string[] lines = HudView.KeyLines();
            // The card is as tall as the HUD's own help card, off the same numbers: one keyboard, one list,
            // and one place the line step, the inset and the height live (HudLayout).
            float height = HudLayout.HelpHeightFor(lines.Length) * _scale;

            RectTransform card = HudUi.Node(root, "card");
            HudUi.TopCentre(card, (Screen.height - height) * 0.5f, width, height);

            HudUi.Shadow(card, "shadow", 12f, _palette.HudShadow);
            HudBox back = HudUi.Round(card, "back", _palette.HudPanel, 12f);
            HudUi.StretchFull(back.Rect);
            HudUi.Border(card, "frame", PanelBorder(), 12f, 1f);

            HudText header = Label(card, "header", HudLayout.HelpLineSize, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);
            HudUi.TopLeft(header.Rect, HudLayout.HelpInset * _scale, HudLayout.HelpHeaderTop * _scale,
                width - 60f * _scale, HudLayout.HelpLineHeight * _scale);
            header.Set("CONTROLS", _palette.HudText);

            HudBox rule = HudUi.Box(card, "rule", HudInk.Rule(_palette));
            HudUi.TopLeft(rule.Rect, HudLayout.HelpInset * _scale, 23f * _scale,
                width - 2f * HudLayout.HelpInset * _scale, 1f);

            float y = HudLayout.HelpLineTop;
            foreach (string line in lines)
            {
                HudText lineText = Label(card, "key" + y, HudLayout.HelpLineSize, FontStyle.Normal,
                    TextAnchor.MiddleLeft);
                HudUi.TopLeft(lineText.Rect, HudLayout.HelpInset * _scale, y * _scale,
                    width - 2f * HudLayout.HelpInset * _scale, HudLayout.HelpLineHeight * _scale);
                lineText.Set(line, HudInk.Muted(_palette));
                y += HudLayout.HelpLineStep;
            }

            HudButton close = HudUi.Button(card, "close", 20f * _scale, 20f * _scale, "×", FontSize(HudType.Label),
                _palette.HudText, () => SetMode(Mode.Title));
            HudUi.TopRight(close.Root, 8f * _scale, 7f * _scale, 22f * _scale, 22f * _scale);
            _buttons.Add(close);
        }

        // ------------------------------------------------------------------ keys, scale, dressing

        /// <summary>The keys the shell itself answers, read only while it is open - the run is held and
        /// unsampled, so these cannot collide with a run key by sharing a frame with one.</summary>
        private void ReadKeys()
        {
            if (_shownAtFrame == Time.frameCount) return;   // the keys that opened this mode already spent themselves

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (_mode == Mode.Title &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                PressPlay();

            if (keyboard.tKey.wasPressedThisFrame && _mode == Mode.Title) SetMode(Mode.Help);

            // M is the map list's key everywhere it exists: from the title it opens the list, from the
            // list it goes back one step - to the title, or to the run the in-run menu came from.
            // Escape reads the same way back, and on the title it has no step back to take.
            if (keyboard.mKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)
            {
                switch (_mode)
                {
                    case Mode.Title:
                        // M opens the map list here; Escape has no step back to take on the title, which
                        // is why the title's own button advertises M and not Esc.
                        if (keyboard.mKey.wasPressedThisFrame) SetMode(Mode.Levels);
                        break;
                    case Mode.Help:
                        SetMode(Mode.Title);
                        break;
                    case Mode.Levels:
                        if (_inRun) _resume?.Invoke();
                        else SetMode(Mode.Title);
                        break;
                }
            }
        }

        private void RefreshScale()
        {
            float s = HudLayout.Scale(Screen.height);
            if (_screenWidth == Screen.width && _screenHeight == Screen.height &&
                Math.Abs(s - _scale) <= _scale * RebuildFraction)
                return;

            Rebuild();
        }

        private void DressButtons()
        {
            for (int i = 0; i < _buttons.Count; i++) Dress(_buttons[i]);
            for (int i = 0; i < _locked.Count; i++) _locked[i].Border.Set(LockedText());
        }

        /// <summary>The HUD's generic button look, said once here as the HUD says it once: a fill that
        /// lightens under the pointer and an edge that takes the accent - the shell's buttons are the
        /// HUD's buttons, and a menu that dressed differently would read as a different game. The title's
        /// play button is the exception the rule exists for: the one filled accent surface on the screen,
        /// because "start here" is the only thing a first-time player has to find.</summary>
        private void Dress(HudButton button)
        {
            bool hovered = button.Hovered;

            if (_primary.Contains(button))
            {
                button.Fill.Set(hovered ? HudInk.Blend(_palette.HudAccent, _palette.HudText, 0.16f) : _palette.HudAccent);
                button.Border.Set(hovered ? _palette.HudText : HudInk.Blend(_palette.HudAccent, _palette.HudPanel, 0.35f));
                return;
            }

            button.Fill.Set(hovered ? HudInk.Blend(ButtonFill, _palette.HudText, 0.14f) : ButtonFill);
            button.Border.Set(hovered ? _palette.HudAccent : PanelBorder());
        }

        /// <summary>Is the shell open over a run? The header's back button and the rows' PLAYING mark
        /// both read it; the boot's shell has no run behind it to go back to.</summary>
        private bool InRun => _inRun;

        private int FontSize(float design) => Mathf.Max(8, Mathf.CeilToInt(design * _scale));

        private HudText Label(RectTransform parent, string name, float design, FontStyle style,
            TextAnchor anchor, bool wrap = false, bool tracked = false)
            => HudUi.Text(parent, name, FontSize(design), style, anchor, wrap, tracked);

        private Color PanelBorder() => HudInk.Edge(_palette);

        private Color ButtonFill => HudInk.Raised(_palette);

        // ------------------------------------------------------------------ geometry

        /// <summary>Rebuild once the screen has scaled this far from the built size - the HUD's own
        /// rule, for the same reason: the fonts keep the scale inside their point size.</summary>
        private const float RebuildFraction = 0.02f;

        /// <summary>Where the game's name starts, as a fraction of the screen height from the top.</summary>
        private const float TitleNameTop = 0.14f;

        /// <summary>The title column's step: one button's height and the gap to the next.</summary>
        private const float ButtonStep = 46f;

        /// <summary>A level row's height and the gap between rows, in design units, before the
        /// fit-into-the-screen shrink; the floor is the shortest row that still carries a name and a
        /// state word legibly, past which the list would rather run off the screen than lie.</summary>
        private const float LevelRowHeight = 54f;
        private const float LevelRowGap = 8f;
        private const float LevelRowFloor = 30f;
    }
}
